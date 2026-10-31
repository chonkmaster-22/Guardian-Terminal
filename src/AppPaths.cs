using System;
using System.IO;

namespace GuardianTerminal
{
    // Everything Guardian Terminal keeps lives next to its .exe (portable, like Savage Wind itself).
    internal static class AppPaths
    {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        public static string Mods { get { return Path.Combine(Root, "mods"); } }
        public static string Overlay { get { return Path.Combine(Root, "overlay"); } }
        public static string Staging { get { return Path.Combine(Root, "staging"); } }
        public static string Downloads { get { return Path.Combine(Root, "downloads"); } }
        public static string Logs { get { return Path.Combine(Root, "logs"); } }
        public static string SaveBackups { get { return Path.Combine(Root, "save-backups"); } }
        public static string Thumbnails { get { return Path.Combine(Root, "thumbnails"); } }
        public static string ConfigFile { get { return Path.Combine(Root, "config.json"); } }
        public static string BuildReport { get { return Path.Combine(Logs, "last-build.txt"); } }
        public static string BuildSummary { get { return Path.Combine(Logs, "last-build.json"); } }
        public static string OverlayMarker { get { return Path.Combine(Overlay, ".guardian-terminal-overlay"); } }

        // the overlay folder's marker file (and the one versions before the rename left)
        public static bool IsOverlayMarker(string path)
        {
            string name = Path.GetFileName(path);
            return name == ".guardian-terminal-overlay" || name == ".swmm-overlay";
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(Mods);
            Directory.CreateDirectory(Logs);
        }

        // True when `path` is inside `folder` (both made absolute; never the folder itself).
        public static bool IsInside(string path, string folder)
        {
            string p = Path.GetFullPath(path).TrimEnd('\\'), f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        public static void DeleteFolderInside(string path, string allowedParent)
        {
            if (!Directory.Exists(path)) return;
            if (!IsInside(path, allowedParent)) throw new InvalidOperationException("Refusing to delete a folder outside " + allowedParent + ": " + path);
            foreach (string f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);  // (read-only files from archives)
            Directory.Delete(path, true);
        }
    }
}
