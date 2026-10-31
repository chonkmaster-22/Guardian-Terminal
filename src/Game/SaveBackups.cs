using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GuardianTerminal.Game
{
    internal sealed class SaveBackup
    {
        public string Folder;   // save-backups\<when> - <why>
        public DateTime When;
        public string Why;
        public long Bytes;
        public int Files;
    }

    // Copies of Savage Wind's save folder (save\ next to savagewind.exe), kept in save-backups\: made by hand, or by
    // themselves before a game with a different set of mods (mods like a randomizer change a save for good), the
    // newest 15 kept. Restoring one first backs up the saves it replaces.
    internal static class SaveBackups
    {
        private const int Keep = 15;

        public static string SaveFolder(SavageWindInstall sw)
        {
            return Path.Combine(sw.Folder, "save");
        }

        public static bool HasSaves(SavageWindInstall sw)
        {
            string s = SaveFolder(sw);
            return Directory.Exists(s) && Directory.EnumerateFiles(s, "*", SearchOption.AllDirectories).Any();
        }

        public static SaveBackup Backup(SavageWindInstall sw, string why)
        {
            string from = SaveFolder(sw);
            if (!Directory.Exists(from)) return null;
            DateTime now = DateTime.Now;
            string name = now.ToString("yyyy-MM-dd HH.mm.ss") + " - " + Clean(why);
            string to = Path.Combine(AppPaths.SaveBackups, name);
            CopyFolder(from, to);
            File.WriteAllText(Path.Combine(to, "backup.txt"), "Savage Wind saves from " + from + "\r\nBacked up " + now + " by Guardian Terminal: " + why + "\r\n");
            Prune();
            return Read(to);
        }

        public static List<SaveBackup> List()
        {
            if (!Directory.Exists(AppPaths.SaveBackups)) return new List<SaveBackup>();
            return Directory.GetDirectories(AppPaths.SaveBackups).Select(Read).Where(b => b != null).OrderByDescending(b => b.When).ToList();
        }

        // Puts a backup back: the saves there now are backed up first, then replaced
        public static void Restore(SavageWindInstall sw, SaveBackup b)
        {
            if (sw.IsRunning()) throw new InvalidOperationException(L.T("Close Savage Wind first: it's using the saves."));
            string to = SaveFolder(sw);
            if (Directory.Exists(to))
            {
                Backup(sw, "before restoring " + Path.GetFileName(b.Folder));
                AppPaths.DeleteFolderInside(to, sw.Folder);
            }
            CopyFolder(b.Folder, to, "backup.txt");
        }

        public static void Delete(SaveBackup b)
        {
            AppPaths.DeleteFolderInside(b.Folder, AppPaths.Root);
        }

        private static SaveBackup Read(string folder)
        {
            string name = Path.GetFileName(folder);
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            DateTime when;
            if (dash < 0 || !DateTime.TryParseExact(name.Substring(0, dash), "yyyy-MM-dd HH.mm.ss", null, System.Globalization.DateTimeStyles.None, out when))
                when = Directory.GetCreationTime(folder);
            List<FileInfo> files = new DirectoryInfo(folder).GetFiles("*", SearchOption.AllDirectories).Where(f => f.Name != "backup.txt").ToList();
            return new SaveBackup { Folder = folder, When = when, Why = dash < 0 ? name : name.Substring(dash + 3), Bytes = files.Sum(f => f.Length), Files = files.Count };
        }

        private static void Prune()
        {
            foreach (SaveBackup old in List().Skip(Keep))
            {
                try { Delete(old); }
                catch (Exception) { }
            }
        }

        private static void CopyFolder(string from, string to, string skip = null)
        {
            Directory.CreateDirectory(to);
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length + 1)));
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                if (skip != null && Path.GetFileName(f) == skip && Path.GetDirectoryName(f).TrimEnd('\\') == from.TrimEnd('\\')) continue;
                File.Copy(f, Path.Combine(to, f.Substring(from.Length + 1)), true);
            }
        }

        private static string Clean(string s)
        {
            string r = string.Concat((s ?? "backup").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            return r.Length > 60 ? r.Substring(0, 60) : r;
        }
    }
}
