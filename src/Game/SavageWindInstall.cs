using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace GuardianTerminal.Game
{
    // A Savage Wind folder: savagewind.exe and, next to it (its data root on Windows), extracted/romfs,
    // savagewind.ini and savagewind.log.
    internal sealed class SavageWindInstall
    {
        public const string OverlayVariable = "SAVAGEWIND_ROMFS_OVERLAY";
        public const string RstbRelPath = "System/Resource/ResourceSizeTable.product.srsizetable";

        // The Voice/ folders Savage Wind can redirect voices to (runtime/language.c, g_voice_languages)
        private static readonly string[] VoiceFolders = { "USen", "USes", "EUes", "EUfr", "EUde", "EUit", "EUru", "JPja" };

        // Where a mod's DLC files go in the overlay folder: Savage Wind serves aoc/0010/<path> in place of the DLC's
        // <path> (runtime/services/fs.c, ov_sub; Alpha 4 on)
        public const string OverlayDlcFolder = "aoc/0010/";
        // ... and the ResourceSizeTable's names for files the game reads from the DLC
        public const string RstbDlcPrefix = "Aoc/0010/";

        public string Exe { get; private set; }
        public string Folder { get; private set; }
        public string RomfsDir { get { return Path.Combine(Folder, "extracted", "romfs"); } }
        public string IniPath { get { return Path.Combine(Folder, "savagewind.ini"); } }
        public string LogPath { get { return Path.Combine(Folder, "savagewind.log"); } }
        public string GameVersion { get; private set; }
        public string AppVersion { get; private set; }

        // The DLC as Savage Wind (Alpha 4 on) sets it up: each DLC's RomFS copied to extracted/aoc<index>, the first
        // (01007EF00011F001) with the content of both packs, the second with only its System/AocVersion.txt. The game
        // uses the DLC only when it finds both.
        // Whether this Savage Wind patches the HUD's button letters into icons (Alpha 4 on: hud_letters_patch, which
        // logs "[fs] HUD letters: ..."); its exe has that log line's text.
        private bool? hudLetters;
        public bool HasHudLettersPatch
        {
            get
            {
                if (hudLetters == null)
                {
                    try { hudLetters = IndexOf(File.ReadAllBytes(Exe), System.Text.Encoding.ASCII.GetBytes("[fs] HUD letters")) >= 0; }
                    catch (IOException) { hudLetters = false; }
                }
                return hudLetters.Value;
            }
        }

        private static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                if (hay[i] != needle[0]) continue;
                int k = 1;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        public string DlcDir(int index) { return Path.Combine(Folder, "extracted", "aoc" + index); }
        public string DlcRomfsDir { get { return DlcDir(1); } }
        public bool HasDlc { get { return Directory.Exists(DlcDir(1)) && Directory.Exists(DlcDir(2)); } }

        // ---- the DLC switch: Savage Wind mounts the DLC when extracted\aoc1 and aoc2 exist (and has no setting for
        // it), so switching it off renames them "aoc1 (off)" and "aoc2 (off)"; switching it on renames them back.
        // Nothing is deleted, and it's instant.
        public string DlcOffDir(int index) { return Path.Combine(Folder, "extracted", "aoc" + index + " (off)"); }
        public bool DlcSwitchedOff { get { return !Directory.Exists(DlcDir(1)) && Directory.Exists(DlcOffDir(1)); } }
        public bool DlcSetUp { get { return HasDlc || DlcSwitchedOff; } }

        public void SetDlc(bool on)
        {
            if (IsRunning()) throw new InvalidOperationException(L.T("Close Savage Wind first: it's using the DLC's files."));
            List<KeyValuePair<string, string>> moved = new List<KeyValuePair<string, string>>();
            try
            {
                for (int i = 1; i <= 2; i++)
                {
                    string from = on ? DlcOffDir(i) : DlcDir(i), to = on ? DlcDir(i) : DlcOffDir(i);
                    if (!Directory.Exists(from) || Directory.Exists(to)) continue;
                    Directory.Move(from, to);
                    moved.Add(new KeyValuePair<string, string>(from, to));
                }
            }
            catch (Exception)
            {
                // (half done: put back what moved)
                foreach (KeyValuePair<string, string> m in moved)
                {
                    try { Directory.Move(m.Value, m.Key); }
                    catch (Exception) { }
                }
                throw;
            }
        }
        public string DlcVersion
        {
            get
            {
                try { return File.ReadAllText(Path.Combine(DlcDir(1), "System", "AocVersion.txt")).Trim(); }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
        }

        public static SavageWindInstall Open(string exe, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                problem = L.T("savagewind.exe wasn't found.");
                return null;
            }
            SavageWindInstall s = new SavageWindInstall();
            s.Exe = Path.GetFullPath(exe);
            s.Folder = Path.GetDirectoryName(s.Exe);
            if (!Directory.Exists(s.RomfsDir) || !File.Exists(Path.Combine(s.RomfsDir, RstbRelPath.Replace('/', '\\'))))
            {
                problem = L.T("Savage Wind hasn't set up your game files in this folder yet. Start Savage Wind once and finish its setup, then come back.");
                return null;
            }
            try { s.GameVersion = File.ReadAllText(Path.Combine(s.RomfsDir, "System", "Version.txt")).Trim(); }
            catch (IOException) { s.GameVersion = "?"; }
            try { s.AppVersion = FileVersionInfo.GetVersionInfo(s.Exe).ProductVersion; }
            catch (Exception) { s.AppVersion = null; }
            if (string.IsNullOrWhiteSpace(s.AppVersion))
            {
                // (the exe has no version resource; the release zip's folder is named SavageWind-<version>-windows)
                string parent = Path.GetFileName(Path.GetDirectoryName(s.Folder) ?? "");
                Match m = Regex.Match(parent, @"^SavageWind-(\d+\.\d+\.\d+[\w.\-]*?)-windows$", RegexOptions.IgnoreCase);
                s.AppVersion = m.Success ? m.Groups[1].Value : null;
            }
            return s;
        }

        // Finds savagewind.exe: desktop / start menu shortcuts first, then the usual folders.
        public static string Detect()
        {
            List<string> found = new List<string>();
            foreach (string dir in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (string lnk in SafeFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    string t = Shortcuts.Target(lnk);
                    if (t != null && Path.GetFileName(t).Equals("savagewind.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(t)) found.Add(t);
                }
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string dir in new[] { Path.Combine(home, "Downloads"), Path.Combine(home, "Desktop"), Path.Combine(home, "Documents"), @"C:\Games", @"D:\Games" })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string sub in SafeDirs(dir, 3))
                {
                    string exe = Path.Combine(sub, "savagewind.exe");
                    if (File.Exists(exe)) found.Add(exe);
                }
            }
            foreach (string exe in found)
            {
                string why;
                if (Open(exe, out why) != null) return exe;
            }
            return found.FirstOrDefault();
        }

        // savagewind.ini: "key = value" lines
        public string ReadSetting(string key)
        {
            if (!File.Exists(IniPath)) return null;
            foreach (string line in File.ReadAllLines(IniPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                if (line.Substring(0, eq).Trim() == key) return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        // The Voice/ folder voices are redirected to (voice_language), or null for "same as text".
        public string VoiceRedirectFolder()
        {
            string v = ReadSetting("voice_language");
            return v != null && VoiceFolders.Contains(v) ? v : null;
        }

        public bool IsRunning()
        {
            foreach (Process p in Process.GetProcessesByName("savagewind"))
            {
                try
                {
                    if (string.Equals(p.MainModule.FileName, Exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception) { return true; }  // (can't read its path: assume it's ours)
                finally { p.Dispose(); }
            }
            return false;
        }

        // Starts the game from its own folder (so its data root is the same as from a shortcut), with the overlay
        // variable set to our folder, or cleared for an unmodded run.
        public Process Launch(string overlayDir)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Exe);
            psi.WorkingDirectory = Folder;
            psi.UseShellExecute = false;
            if (overlayDir != null) psi.EnvironmentVariables[OverlayVariable] = overlayDir;
            else psi.EnvironmentVariables.Remove(OverlayVariable);
            return Process.Start(psi);
        }

        // What the last run's log says about the overlay ("[fs] overlay: ..." lines).
        public List<string> OverlayLogLines()
        {
            List<string> lines = new List<string>();
            if (!File.Exists(LogPath)) return lines;
            try
            {
                using (FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader r = new StreamReader(fs))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                        if (line.StartsWith("[fs] overlay", StringComparison.Ordinal) || line.StartsWith("[fs] voices: the overlay", StringComparison.Ordinal)) lines.Add(line);
                }
            }
            catch (IOException) { }
            return lines;
        }

        private static IEnumerable<string> SafeFiles(string dir, string pattern, SearchOption option)
        {
            try { return Directory.GetFiles(dir, pattern, option); }
            catch (Exception) { return new string[0]; }
        }

        private static IEnumerable<string> SafeDirs(string dir, int depth)
        {
            List<string> all = new List<string> { dir };
            List<string> level = new List<string> { dir };
            for (int d = 0; d < depth; d++)
            {
                List<string> next = new List<string>();
                foreach (string l in level)
                {
                    try { next.AddRange(Directory.GetDirectories(l)); }
                    catch (Exception) { }
                }
                all.AddRange(next);
                level = next;
            }
            return all;
        }
    }

    // .lnk files through the Windows Script Host (no extra references needed).
    internal static class Shortcuts
    {
        public static string Target(string lnk)
        {
            try
            {
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                return (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
            }
            catch (Exception) { return null; }
        }

        public static void Create(string lnk, string target, string arguments, string description, string icon)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type t = link.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
            t.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments ?? "" });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description ?? "" });
            if (icon != null) t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { icon });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
    }
}
