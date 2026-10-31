using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GuardianTerminal.Mods
{
    internal sealed class InstalledMod
    {
        public string Folder;
        public ModInfo Info;
        public bool Enabled;

        public string Dir { get { return Path.Combine(AppPaths.Mods, Folder); } }
        public string RomfsDir { get { return Path.Combine(Dir, "romfs"); } }
        public string DlcRomfsDir { get { return Path.Combine(Dir, "dlc", "romfs"); } }
        // a BCML package's own resource sizes (its logs/rstb.json), kept at import
        public string BcmlSizesFile { get { return Path.Combine(Dir, "bcml-rstb.json"); } }
        public string DisplayName { get { return Info != null && !string.IsNullOrEmpty(Info.Name) ? Info.Name : Folder; } }

        public void SaveInfo()
        {
            Json.Write(Path.Combine(Dir, "mod.json"), Info);
        }
    }

    // The installed mods (mods/<folder>/romfs + mod.json), as the current profile has them: on or off, in load order
    // (later ones win when two change the same file). Mods the profile doesn't list yet come last, off.
    internal sealed class ModLibrary
    {
        public readonly AppConfig Config;
        public readonly List<InstalledMod> Mods = new List<InstalledMod>();

        private ModLibrary(AppConfig config)
        {
            Config = config;
        }

        public static ModLibrary Load()
        {
            ModLibrary lib = new ModLibrary(AppConfig.Load());
            lib.LoadProfile();
            return lib;
        }

        private void LoadProfile()
        {
            Mods.Clear();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModEntry e in Config.Current.Mods)
            {
                if (e == null || string.IsNullOrEmpty(e.Folder) || !seen.Add(e.Folder) || !Directory.Exists(Path.Combine(AppPaths.Mods, e.Folder))) continue;
                Mods.Add(Open(e.Folder, e.Enabled));
            }
            if (Directory.Exists(AppPaths.Mods))
                foreach (string d in Directory.GetDirectories(AppPaths.Mods).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string folder = Path.GetFileName(d);
                    if (seen.Add(folder) && Directory.Exists(Path.Combine(d, "romfs"))) Mods.Add(Open(folder, false));
                }
        }

        private static InstalledMod Open(string folder, bool enabled)
        {
            InstalledMod m = new InstalledMod();
            m.Folder = folder;
            m.Enabled = enabled;
            m.Info = Json.Read<ModInfo>(Path.Combine(m.Dir, "mod.json"));
            if (m.Info == null)
            {
                m.Info = new ModInfo();
                m.Info.Name = folder;
                m.Info.Status = ModStatus.Warning;
                m.Info.Notes.Add(L.MakeNote("Added by hand: check it in the build report after you merge."));
            }
            return m;
        }

        public void Save()
        {
            Config.Current.Mods = Mods.Select(m => new ModEntry { Folder = m.Folder, Enabled = m.Enabled }).ToList();
            Config.Save();
        }

        public List<InstalledMod> EnabledInOrder()
        {
            return Mods.Where(m => m.Enabled).ToList();
        }

        public InstalledMod FindByName(string name)
        {
            return Mods.FirstOrDefault(m => string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ profiles
        public string ProfileName { get { return Config.Current.Name; } }
        public List<string> ProfileNames { get { return Config.Profiles.Select(p => p.Name).ToList(); } }

        public void SwitchProfile(string name)
        {
            if (Config.FindProfile(name) == null || string.Equals(name, ProfileName, StringComparison.OrdinalIgnoreCase)) return;
            Save();
            Config.CurrentProfile = Config.FindProfile(name).Name;
            Config.Save();
            LoadProfile();
        }

        // A new profile, empty (every mod off) or a copy of the current one; it becomes the current one.
        public void CreateProfile(string name, bool copyCurrent)
        {
            Save();
            Profile p = new Profile { Name = name };
            if (copyCurrent) p.Mods = Config.Current.Mods.Select(e => new ModEntry { Folder = e.Folder, Enabled = e.Enabled }).ToList();
            Config.Profiles.Add(p);
            Config.CurrentProfile = name;
            Config.Save();
            LoadProfile();
        }

        public void RenameProfile(string name)
        {
            Config.Current.Name = name;
            Config.CurrentProfile = name;
            Save();
        }

        // false: it's the only one (one must always exist)
        public bool DeleteProfile()
        {
            if (Config.Profiles.Count <= 1) return false;
            Config.Profiles.Remove(Config.Current);
            Config.CurrentProfile = Config.Profiles[0].Name;
            Config.Save();
            LoadProfile();
            return true;
        }

        public bool ProfileExists(string name)
        {
            return Config.FindProfile(name) != null;
        }

        // ------------------------------------------------------------------ installed mods
        // A folder name for a new mod: the name made file-system safe, made unique.
        public string NewFolderName(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == '.' && sb.Length == 0 ? '_' : c);
            string baseName = sb.ToString().Trim().TrimEnd('.');
            if (baseName.Length == 0) baseName = "mod";
            if (baseName.Length > 60) baseName = baseName.Substring(0, 60).Trim();
            string folder = baseName;
            for (int i = 2; Directory.Exists(Path.Combine(AppPaths.Mods, folder)) || Mods.Any(m => m.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)); i++)
                folder = baseName + " (" + i + ")";
            return folder;
        }

        public void Add(InstalledMod m)
        {
            Mods.Add(m);
            Save();
        }

        // Puts a freshly imported mod where an older copy of it was, in every profile (same place, same on/off).
        public void Replace(InstalledMod old, InstalledMod fresh)
        {
            int i = Mods.IndexOf(old);
            fresh.Enabled = old.Enabled && fresh.Enabled;
            Mods[i] = fresh;
            foreach (Profile p in Config.Profiles)
                foreach (ModEntry e in p.Mods)
                    if (string.Equals(e.Folder, old.Folder, StringComparison.OrdinalIgnoreCase)) e.Folder = fresh.Folder;
            Save();
            AppPaths.DeleteFolderInside(old.Dir, AppPaths.Mods);
        }

        // Uninstalls it: gone from every profile, its files deleted.
        public void Remove(InstalledMod m)
        {
            Mods.Remove(m);
            foreach (Profile p in Config.Profiles) p.Mods.RemoveAll(e => string.Equals(e.Folder, m.Folder, StringComparison.OrdinalIgnoreCase));
            Save();
            AppPaths.DeleteFolderInside(m.Dir, AppPaths.Mods);
        }

        public void Move(InstalledMod m, int delta)
        {
            MoveTo(m, Mods.IndexOf(m) + delta);
        }

        // To a place in the load order (0: first, loaded before the rest)
        public void MoveTo(InstalledMod m, int to)
        {
            int i = Mods.IndexOf(m);
            if (i < 0 || to < 0 || to >= Mods.Count || to == i) return;
            Mods.RemoveAt(i);
            Mods.Insert(to, m);
            Save();
        }
    }
}
