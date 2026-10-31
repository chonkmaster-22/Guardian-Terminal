using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GuardianTerminal.Mods
{
    // One mod found somewhere else: an emulator's mod folder, a Switch's SD card, a folder of downloads
    internal sealed class FoundMod
    {
        public string Name;
        public string Path;        // a folder (with romfs in it) or an archive
        public string DlcFolder;   // its DLC files, when they're kept apart (the emulator's DLC folder of the same name)
    }

    internal sealed class ModSource
    {
        public string Name, Folder;
        public List<FoundMod> Mods = new List<FoundMod>();
    }

    // Where players already keep Breath of the Wild mods, so a whole set can come in at once:
    //  - Ryujinx: %APPDATA%\Ryujinx\mods\contents\01007ef00011e000\<mod>\romfs (and ...f001\<mod> for the DLC);
    //  - yuzu and the emulators made from it (suyu, sudachi, citron, eden, torzu):
    //    %APPDATA%\<name>\load\01007EF00011E000\<mod>\romfs;
    //  - a modded Switch's SD card: atmosphere\contents\01007EF00011E000\romfs (all its mods, as one);
    //  - any folder: each archive and each mod folder in it.
    internal static class ModSources
    {
        private const string Base = "01007EF00011E000", Dlc = "01007EF00011F001";

        public static List<ModSource> Find()
        {
            List<ModSource> found = new List<ModSource>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Add(found, "Ryujinx", Path.Combine(appData, "Ryujinx", "mods", "contents"));
            foreach (string emu in new[] { "yuzu", "suyu", "sudachi", "citron", "eden", "torzu" })
                Add(found, char.ToUpperInvariant(emu[0]) + emu.Substring(1), Path.Combine(appData, emu, "load"));
            foreach (string sd in Build.SdExport.FindCards())
            {
                string romfs = Path.Combine(sd, "atmosphere", "contents", Base, "romfs");
                if (!Directory.Exists(romfs)) continue;
                ModSource s = new ModSource { Name = L.F("Switch SD card ({0})", sd.TrimEnd('\\')), Folder = Path.Combine(sd, "atmosphere", "contents", Base) };
                string dlc = Path.Combine(sd, "atmosphere", "contents", Dlc);
                s.Mods.Add(new FoundMod { Name = L.T("Mods from the SD card"), Path = s.Folder, DlcFolder = Directory.Exists(Path.Combine(dlc, "romfs")) ? dlc : null });
                found.Add(s);
            }
            return found;
        }

        // An emulator's folder of titles: its BotW folder's mod folders (those with a romfs in them)
        private static void Add(List<ModSource> found, string name, string titles)
        {
            string game = Child(titles, Base);
            if (game == null) return;
            string dlc = Child(titles, Dlc);
            ModSource s = new ModSource { Name = name, Folder = game };
            foreach (string mod in SafeDirs(game))
            {
                if (!Directory.Exists(Path.Combine(mod, "romfs"))) continue;
                string dlcMod = dlc == null ? null : Child(dlc, Path.GetFileName(mod));
                s.Mods.Add(new FoundMod { Name = Path.GetFileName(mod), Path = mod, DlcFolder = dlcMod != null && Directory.Exists(Path.Combine(dlcMod, "romfs")) ? dlcMod : null });
            }
            if (s.Mods.Count > 0) found.Add(s);
        }

        // A folder of mods: its archives, and its subfolders that hold game files (a romfs, a title folder, or
        // romfs-like folders); a folder that is a mod itself is that one mod
        public static List<FoundMod> InFolder(string folder)
        {
            List<FoundMod> mods = new List<FoundMod>();
            if (LooksLikeMod(folder) && !SafeDirs(folder).Any(LooksLikeMod))
            {
                mods.Add(new FoundMod { Name = Path.GetFileName(folder), Path = folder });
                return mods;
            }
            foreach (string f in SafeFiles(folder).Where(f => ModImporter.ArchiveExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                mods.Add(new FoundMod { Name = Path.GetFileNameWithoutExtension(f), Path = f });
            foreach (string d in SafeDirs(folder).Where(LooksLikeMod))
                mods.Add(new FoundMod { Name = Path.GetFileName(d), Path = d });
            return mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool LooksLikeMod(string dir)
        {
            try
            {
                return Directory.EnumerateDirectories(dir, "romfs", SearchOption.AllDirectories).Any()
                    || Directory.EnumerateDirectories(dir).Any(d => new[] { "Actor", "Pack", "Model", "Map", "UI", "Sound", "Event" }.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }

        private static string Child(string parent, string name)
        {
            if (!Directory.Exists(parent)) return null;
            return SafeDirs(parent).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); }
            catch (Exception) { return new string[0]; }
        }

        private static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir); }
            catch (Exception) { return new string[0]; }
        }
    }
}
