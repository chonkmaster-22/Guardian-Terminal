using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GuardianTerminal.Game
{
    internal enum LaunchStage
    {
        Starting,     // the process is up; its log not written yet
        GameCode,     // [gamelib] the game code library loaded
        GameData,     // [fs] romfs image extracted/romfs: laid out
        Mods,         // [fs] overlay: the mod files served
        Dlc,          // [aoc] / [fs] add-on content: the DLC mounted
        Running,      // [vk] t=... frame N: frames presented - the game is on screen
        SetupScreen,  // [launcher] / [gamebuild]: Savage Wind showed its own start screen instead
        Failed        // FATAL, closed before its first frame, or stuck
    }

    // Follows a starting Savage Wind through its log (savagewind.log next to its exe, written as it goes): which step
    // of the start it's at, and whether it failed - a "FATAL:" line, the process ending before the game is on screen,
    // or no progress at all for StuckAfter. The log of an earlier run is told apart by its time: the new process
    // moves it to savagewind.prev.log and writes a new one from its first moment.
    internal sealed class LaunchWatcher
    {
        public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(120);

        private readonly string logPath;
        private readonly Process process;
        private readonly DateTime startedUtc;
        private bool attached;
        private long position;
        private readonly StringBuilder partial = new StringBuilder();
        private readonly LinkedList<string> tail = new LinkedList<string>();

        public LaunchStage Stage { get; private set; }
        public bool ModsSeen { get; private set; }
        public bool DlcSeen { get; private set; }
        public string Error { get; private set; }
        public DateTime LastProgressUtc { get; private set; }
        public bool Stuck { get { return Stage < LaunchStage.Running && DateTime.UtcNow - LastProgressUtc > StuckAfter; } }

        public LaunchWatcher(SavageWindInstall sw, Process process)
        {
            logPath = sw.LogPath;
            this.process = process;
            startedUtc = DateTime.UtcNow.AddSeconds(-2);
            LastProgressUtc = DateTime.UtcNow;
        }

        public IEnumerable<string> Tail { get { return tail; } }
        public bool Attached { get { return attached; } }
        public string LogPath { get { return logPath; } }

        // Reads what the log has gained since last time. Call it every fraction of a second.
        public void Poll()
        {
            if (Stage == LaunchStage.Failed || Stage == LaunchStage.Running || Stage == LaunchStage.SetupScreen) return;
            ReadNewLines();
            bool exited;
            try { exited = process.HasExited; }
            catch (InvalidOperationException) { exited = true; }
            if (exited && Stage < LaunchStage.Running)
            {
                ReadNewLines();  // (its last words)
                if (Stage == LaunchStage.Failed || Stage == LaunchStage.Running || Stage == LaunchStage.SetupScreen) return;
                int code = 0;
                try { code = process.ExitCode; }
                catch (InvalidOperationException) { }
                Fail(code == 0
                    ? L.T("Savage Wind closed before the game appeared.")
                    : L.F("Savage Wind closed before the game appeared ({0}).", ExitCodeText(code)));
            }
            else if (Stuck)
            {
                Fail(L.F("Savage Wind hasn't made any progress for {0} minutes: it may be stuck.", (int)StuckAfter.TotalMinutes));
            }
        }

        // "exit code 1", or for a Windows error code "exit code 0xC0000005, a crash"
        public static string ExitCodeText(int code)
        {
            if (code >= 0 && code < 0x10000) return L.F("exit code {0}", code);
            string hex = "0x" + code.ToString("X8");
            return code == unchecked((int)0xC0000005) || code == unchecked((int)0xC0000409) || code == unchecked((int)0xC000001D) ? L.F("exit code {0}, a crash", hex) : L.F("exit code {0}", hex);
        }

        private void Fail(string message)
        {
            Stage = LaunchStage.Failed;
            Error = message;
        }

        private void ReadNewLines()
        {
            if (!File.Exists(logPath)) return;
            try
            {
                if (!attached)
                {
                    if (File.GetLastWriteTimeUtc(logPath) < startedUtc) return;  // (an earlier run's)
                    attached = true;
                    position = 0;
                }
                using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < position) position = 0;  // (a new log started)
                    if (fs.Length == position) return;
                    fs.Seek(position, SeekOrigin.Begin);
                    byte[] buf = new byte[fs.Length - position];
                    int n = fs.Read(buf, 0, buf.Length);
                    position += n;
                    partial.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            string text = partial.ToString();
            int end = text.LastIndexOf('\n');
            if (end < 0) return;
            partial.Clear();
            partial.Append(text.Substring(end + 1));
            foreach (string raw in text.Substring(0, end).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                tail.AddLast(line);
                if (tail.Count > 40) tail.RemoveFirst();
                LastProgressUtc = DateTime.UtcNow;  // (any line: it's alive and working)
                Take(line);
                if (Stage == LaunchStage.Failed || Stage == LaunchStage.Running || Stage == LaunchStage.SetupScreen) break;
            }
        }

        private void Take(string line)
        {
            if (line.StartsWith("FATAL:", StringComparison.Ordinal))
            {
                Fail(L.F("Savage Wind stopped with an error: {0}", line.Substring(6).Trim()));
                return;
            }
            if (line.StartsWith("[launcher]", StringComparison.Ordinal) || line.StartsWith("[gamebuild]", StringComparison.Ordinal))
            {
                Stage = LaunchStage.SetupScreen;
                return;
            }
            if (line.StartsWith("[gamelib]", StringComparison.Ordinal)) Advance(LaunchStage.GameCode);
            else if (line.StartsWith("[fs] romfs image extracted/romfs", StringComparison.Ordinal) || line.StartsWith("[fs] romfs index", StringComparison.Ordinal)) Advance(LaunchStage.GameData);
            else if (line.StartsWith("[fs] overlay:", StringComparison.Ordinal)) { ModsSeen = true; Advance(LaunchStage.Mods); }
            else if (line.StartsWith("[aoc]", StringComparison.Ordinal) || line.StartsWith("[fs] add-on content", StringComparison.Ordinal)) { DlcSeen = true; Advance(LaunchStage.Dlc); }
            else if (line.StartsWith("[vk] t=", StringComparison.Ordinal) && line.Contains(" frame ")) Advance(LaunchStage.Running);
        }

        private void Advance(LaunchStage s)
        {
            if (s > Stage) Stage = s;
        }
    }
}
