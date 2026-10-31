using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace GuardianTerminal
{
    public sealed class ModEntry
    {
        public string Folder { get; set; }
        public bool Enabled { get; set; }
    }

    // A named set of mods: which of the installed mods are on, and their load order. The installed mods themselves
    // are shared by every profile.
    public sealed class Profile
    {
        public string Name { get; set; }
        public List<ModEntry> Mods { get; set; }

        public Profile()
        {
            Mods = new List<ModEntry>();
        }
    }

    // config.json: where Savage Wind is, the profiles (the mods in load order), and what the overlay was last built from.
    public sealed class AppConfig
    {
        public const string DefaultProfile = "Default";

        public string SavageWindExe { get; set; }
        public List<Profile> Profiles { get; set; }
        public string CurrentProfile { get; set; }
        public string LastBuildSignature { get; set; }
        public string LastBuildSummary { get; set; }
        public List<ModEntry> Mods { get; set; }  // (before profiles: read once into the default profile)

        // ---- settings
        public bool WelcomeShown { get; set; }          // the first-run welcome (with the credits) was seen
        public bool CheckForUpdates { get; set; }       // Guardian Terminal, Savage Wind and GameBanana mods, once a day
        public string LastUpdateCheck { get; set; }
        public bool BackUpSaves { get; set; }           // a save backup before a game with a different set of mods
        public string LastSaveBackupFor { get; set; }   // the mods the last automatic backup was made before
        public bool ShowCards { get; set; }             // the mod list as picture cards (else compact rows)
        public string IgnoredUpdate { get; set; }       // a Guardian Terminal version the player said no to
        public bool ShowMatureMods { get; set; }        // the GameBanana page shows mods marked mature
        public string Language { get; set; }            // "auto" (Windows' language) or a code: en, es, fr, de, it, pt, ja, zh

        public AppConfig()
        {
            Profiles = new List<Profile>();
            CheckForUpdates = true;
            BackUpSaves = true;
            ShowCards = true;
        }

        public static AppConfig Load()
        {
            AppConfig c = Json.Read<AppConfig>(AppPaths.ConfigFile) ?? new AppConfig();
            if (c.Profiles == null) c.Profiles = new List<Profile>();
            c.Profiles.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name));
            foreach (Profile p in c.Profiles) if (p.Mods == null) p.Mods = new List<ModEntry>();
            if (c.Profiles.Count == 0)
            {
                Profile p = new Profile { Name = DefaultProfile };
                if (c.Mods != null) p.Mods.AddRange(c.Mods.Where(m => m != null));
                c.Profiles.Add(p);
            }
            c.Mods = null;
            if (c.FindProfile(c.CurrentProfile) == null) c.CurrentProfile = c.Profiles[0].Name;
            return c;
        }

        public void Save()
        {
            Json.Write(AppPaths.ConfigFile, this);
        }

        public Profile FindProfile(string name)
        {
            return name == null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        [ScriptIgnore]
        public Profile Current { get { return FindProfile(CurrentProfile) ?? Profiles[0]; } }
    }
}
