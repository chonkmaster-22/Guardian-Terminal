using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace GuardianTerminal
{
    // Whether a newer Guardian Terminal is out, from its GitHub releases. Checked at most once a day, and only when
    // the player leaves "Check for updates" on; nothing is downloaded by itself.
    internal static class Updates
    {
        public const string Repository = "chonkmaster-22/guardian-terminal";
        public const string ProjectPage = "https://github.com/chonkmaster-22/guardian-terminal";
        public const string ReleasesPage = ProjectPage + "/releases";

        public sealed class Release
        {
            public string Version, Name, PageUrl, Notes;
        }

        public static string CurrentVersion
        {
            get
            {
                string v = Application.ProductVersion;
                return v.EndsWith(".0") && v.Count(c => c == '.') == 3 ? v.Substring(0, v.Length - 2) : v;
            }
        }

        // A newer release, or null (none, or it couldn't be checked)
        public static Release CheckNow()
        {
            object[] list = Net.GetJson("https://api.github.com/repos/" + Repository + "/releases?per_page=5") as object[];
            if (list == null) return null;
            Dictionary<string, object> newest = list.OfType<Dictionary<string, object>>().FirstOrDefault(r => Net.Get(r, "draft") != "True");
            if (newest == null) return null;
            string tag = (Net.Get(newest, "tag_name") ?? "").TrimStart('v', 'V');
            if (Net.CompareVersions(tag, CurrentVersion) <= 0) return null;
            return new Release { Version = tag, Name = Net.Get(newest, "name"), PageUrl = Net.Get(newest, "html_url") ?? ReleasesPage, Notes = Net.Get(newest, "body") };
        }

        // Once a day at most (the day is remembered in the config)
        public static bool Due(AppConfig config)
        {
            if (!config.CheckForUpdates) return false;
            DateTime last;
            return !DateTime.TryParse(config.LastUpdateCheck, out last) || (DateTime.Now - last).TotalHours >= 20;
        }
    }
}
