using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GuardianTerminal.UI;

namespace GuardianTerminal
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);

            // the player's language, before anything is shown
            AppConfig config = null;
            try { config = AppConfig.Load(); }
            catch (Exception) { }
            L.Use(config == null ? null : config.Language);

            bool first;
            MainForm main = null;
            // one Guardian Terminal at a time: two could rebuild the same overlay folder at once
            using (Mutex single = new Mutex(true, "Local\\GuardianTerminal", out first))
            {
                if (!first)
                {
                    // (one that's restarting is given a moment to close)
                    bool got;
                    try { got = single.WaitOne(TimeSpan.FromSeconds(args.Contains("--restarted") ? 10 : 0)); }
                    catch (AbandonedMutexException) { got = true; }
                    if (!got)
                    {
                        MessageBox.Show(L.T("Guardian Terminal is already open."), "Guardian Terminal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 1;
                    }
                }
                AppPaths.EnsureDirectories();
                if (args.Any(a => a.Equals("--play", StringComparison.OrdinalIgnoreCase))) Application.Run(new QuickPlay());
                else
                {
                    // --page <mods|profiles|browse|saves|tools|settings|about>: opens on that page
                    int p = Array.FindIndex(args, a => a.Equals("--page", StringComparison.OrdinalIgnoreCase));
                    main = new MainForm(p >= 0 && p + 1 < args.Length ? args[p + 1].ToLowerInvariant() : null);
                    Application.Run(main);
                }
                single.ReleaseMutex();
            }
            // (the language changed: start again in it)
            if (main != null && main.RestartAfterClose)
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restarted") { WorkingDirectory = AppPaths.Root, UseShellExecute = false }); }
                catch (Exception) { }
            }
            return 0;
        }

        private static void ShowCrash(Exception e)
        {
            string text = e == null ? "Unknown error" : e.ToString();
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Logs, "crash.txt"), DateTime.Now + "\r\n" + text); }
            catch (Exception) { }
            MessageBox.Show(L.T("Something went wrong:") + "\n\n" + (e == null ? text : e.Message) + "\n\n" + L.T("Details were saved to logs\\crash.txt."),
                "Guardian Terminal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
