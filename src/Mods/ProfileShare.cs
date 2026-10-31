using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GuardianTerminal.Mods
{
    public sealed class SharedMod
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Url { get; set; }   // its GameBanana page, when it came from there
        public bool Enabled { get; set; }
    }

    // A profile as a file to share (.gtprofile, JSON): its mods in load order, with where to get them. No mod files
    // are in it: whoever opens it installs the mods themselves (from their GameBanana pages).
    public sealed class SharedProfile
    {
        public string Format { get; set; }
        public string Name { get; set; }
        public string MadeWith { get; set; }
        public List<SharedMod> Mods { get; set; }

        public SharedProfile()
        {
            Mods = new List<SharedMod>();
        }
    }

    internal static class ProfileShare
    {
        public const string Extension = ".gtprofile";

        public static void Export(ModLibrary library, string path)
        {
            SharedProfile p = new SharedProfile { Format = "guardian-terminal-profile-1", Name = library.ProfileName, MadeWith = "Guardian Terminal " + Updates.CurrentVersion };
            foreach (InstalledMod m in library.Mods.Where(m => m.Enabled))
                p.Mods.Add(new SharedMod { Name = m.DisplayName, Version = m.Info.Version, Url = m.Info.SourceUrl, Enabled = true });
            Json.Write(path, p);
        }

        public sealed class ImportResult
        {
            public string ProfileName;
            public List<SharedMod> Missing = new List<SharedMod>();  // not installed here (install them, then import again)
            public int Matched;
        }

        // A new profile from the file: its mods that are installed here, in its order and on; the others listed
        public static ImportResult Import(string path, ModLibrary library)
        {
            SharedProfile p = Json.Read<SharedProfile>(path);
            if (p == null || p.Mods == null) throw new InvalidDataException(L.T("That file isn't a Guardian Terminal profile."));
            ImportResult r = new ImportResult();
            string name = string.IsNullOrWhiteSpace(p.Name) ? Path.GetFileNameWithoutExtension(path) : p.Name.Trim();
            string unique = name;
            for (int i = 2; library.ProfileExists(unique); i++) unique = name + " (" + i + ")";
            library.CreateProfile(unique, false);
            List<InstalledMod> order = new List<InstalledMod>();
            foreach (SharedMod s in p.Mods)
            {
                InstalledMod m = Find(library, s);
                if (m == null) { r.Missing.Add(s); continue; }
                m.Enabled = s.Enabled;
                order.Add(m);
                r.Matched++;
            }
            // its mods first, in its order; the rest after them, off
            List<InstalledMod> rest = library.Mods.Where(m => !order.Contains(m)).ToList();
            foreach (InstalledMod m in rest) m.Enabled = false;
            library.Mods.Clear();
            library.Mods.AddRange(order);
            library.Mods.AddRange(rest);
            library.Save();
            r.ProfileName = unique;
            return r;
        }

        // By GameBanana page first, then by name
        private static InstalledMod Find(ModLibrary library, SharedMod s)
        {
            long id = GameBanana.IdOf(s.Url);
            if (id > 0)
            {
                InstalledMod byUrl = library.Mods.FirstOrDefault(m => GameBanana.IdOf(m.Info.SourceUrl) == id);
                if (byUrl != null) return byUrl;
            }
            return library.Mods.FirstOrDefault(m => string.Equals(m.DisplayName, s.Name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
