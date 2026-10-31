using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GuardianTerminal.Game
{
    // Savage Wind's official Windows releases, from its own GitHub page (Revivedx/SavageWindRecomp): Guardian
    // Terminal doesn't carry Savage Wind itself, it downloads the newest release from there, as its README says to:
    // extracted to a folder of its own, then Savage Wind's own setup asks for the player's game files.
    internal sealed class SavageWindRelease
    {
        public const string Repository = "Revivedx/SavageWindRecomp";
        public const string ReleasesPage = "https://github.com/Revivedx/SavageWindRecomp/releases";
        public const string ProjectPage = "https://github.com/Revivedx/SavageWindRecomp";

        public string Tag, Name, AssetName, AssetUrl, PageUrl;
        public long AssetSize;
        public DateTime Published;
        public bool Prerelease;

        public string Version { get { return Tag == null ? null : Tag.TrimStart('v', 'V'); } }

        // The newest release with a Windows zip (every release so far is a pre-release, so GitHub's "latest" can't be
        // used: the list comes newest first)
        public static SavageWindRelease Newest()
        {
            object[] list = Net.GetJson("https://api.github.com/repos/" + Repository + "/releases?per_page=10") as object[];
            if (list == null) return null;
            foreach (Dictionary<string, object> r in list.OfType<Dictionary<string, object>>())
            {
                if (Net.Get(r, "draft") == "True") continue;
                object[] assets = r.ContainsKey("assets") ? r["assets"] as object[] : null;
                Dictionary<string, object> zip = assets == null ? null : assets.OfType<Dictionary<string, object>>()
                    .FirstOrDefault(a => (Net.Get(a, "name") ?? "").EndsWith("-windows.zip", StringComparison.OrdinalIgnoreCase));
                if (zip == null) continue;
                SavageWindRelease rel = new SavageWindRelease();
                rel.Tag = Net.Get(r, "tag_name");
                rel.Name = Net.Get(r, "name");
                rel.PageUrl = Net.Get(r, "html_url");
                rel.Prerelease = Net.Get(r, "prerelease") == "True";
                DateTime when;
                rel.Published = DateTime.TryParse(Net.Get(r, "published_at"), out when) ? when : DateTime.MinValue;
                rel.AssetName = Net.Get(zip, "name");
                rel.AssetUrl = Net.Get(zip, "browser_download_url");
                long size;
                rel.AssetSize = long.TryParse(Net.Get(zip, "size"), out size) ? size : 0;
                return rel;
            }
            return null;
        }

        // Downloads the zip and extracts it into parent\SavageWind-<version>-windows (as the zip is named); returns
        // savagewind.exe's path. progress(text)
        public string Install(string parent, Action<string> progress)
        {
            string zip = Path.Combine(AppPaths.Downloads, AssetName);
            Net.Download(AssetUrl, zip, (done, total) =>
                progress(total > 0 ? L.F("Downloading Savage Wind {0}... {1}% of {2:N0} MB", Version, done * 100 / total, total / 1048576.0)
                                   : L.F("Downloading Savage Wind {0}... {1:N0} MB", Version, done / 1048576.0)));
            string dest = Path.Combine(parent, Path.GetFileNameWithoutExtension(AssetName));
            if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
                throw new IOException(L.F("{0} already exists and isn't empty: choose another folder.", dest));
            progress(L.F("Extracting Savage Wind {0}...", Version));
            string root = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                int done = 0;
                foreach (ZipArchiveEntry e in archive.Entries)
                {
                    string to = Path.GetFullPath(Path.Combine(dest, e.FullName));
                    if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;  // (nothing outside the folder)
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(to); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    e.ExtractToFile(to, true);
                    if (++done % 40 == 0) progress(L.F("Extracting Savage Wind {0}... {1}%", Version, done * 100 / Math.Max(1, archive.Entries.Count)));
                }
            }
            string exe = Directory.GetFiles(dest, "savagewind.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
            if (exe == null) throw new FileNotFoundException(L.T("The Savage Wind download has no savagewind.exe in it."));
            try { File.Delete(zip); }
            catch (IOException) { }
            return exe;
        }

        // A save folder copied from an older Savage Wind into a new one (as its README says for updating); the new
        // one's own, if any, is kept aside first
        public static void CopySaves(string fromFolder, string toFolder)
        {
            string from = Path.Combine(fromFolder, "save"), to = Path.Combine(toFolder, "save");
            if (!Directory.Exists(from)) return;
            if (Directory.Exists(to)) Directory.Move(to, to + " (before copy " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ")");
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length + 1)));
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(to, f.Substring(from.Length + 1)), false);
        }
    }
}
