using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GuardianTerminal.Game;
using GuardianTerminal.UI;

// Runs the loading screen against FakeGame (a stand-in savagewind.exe), off screen, and saves pictures of it.
//   LaunchProbe <fake savagewind.exe> <mode> <pictures folder> [--mods]
// <mode>-result.txt says how it ended; a failure view is closed once it's been pictured.
internal static class LaunchProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string mode = args[1], outDir = args[2];
        Directory.CreateDirectory(outDir);
        string resultFile = Path.Combine(outDir, mode + "-result.txt");
        string problem;
        SavageWindInstall sw = SavageWindInstall.Open(args[0], out problem);
        if (sw == null)
        {
            File.WriteAllText(resultFile, "open failed: " + problem);
            return 1;
        }
        Environment.SetEnvironmentVariable("SWMM_FAKE_MODE", mode);
        bool mods = args.Contains("--mods");
        Process game = sw.Launch(mods ? Path.GetTempPath() : null);
        LaunchScreen screen = new LaunchScreen(sw, game, mods, "629 game files changed by 3 mods");
        screen.Bounds = new Rectangle(-6000, -6000, 1280, 720);  // (off screen: the test mustn't cover the desktop)

        string result = "closed without finishing";
        int n = 0;
        Stopwatch clock = Stopwatch.StartNew();
        Timer shots = new Timer { Interval = 1000 };
        shots.Tick += delegate
        {
            Save(screen, Path.Combine(outDir, string.Format("{0}-{1:00}.png", mode, ++n)), string.Format("t={0:0.0}s visible={1}", clock.Elapsed.TotalSeconds, screen.Visible));
            if (FindLabel(screen, "Savage Wind couldn't") != null)
            {
                Save(screen, Path.Combine(outDir, mode + "-failure.png"), "failure view");
                result = "failure view shown: " + screen.Error;
                screen.Close();
            }
            else if (clock.Elapsed.TotalSeconds > 40)
            {
                result = "timed out at stage " + screen.Stage;
                screen.Close();
            }
        };
        screen.Finished += ok =>
        {
            result = string.Format("finished ok={0} at {1:0.0}s, stage {2}, error: {3}", ok, clock.Elapsed.TotalSeconds, screen.Stage, screen.Error ?? "(none)");
            Save(screen, Path.Combine(outDir, mode + "-final.png"), "final");
        };
        screen.FormClosed += delegate
        {
            shots.Stop();
            File.WriteAllText(resultFile, result + "\r\n");
            Application.ExitThread();
        };
        shots.Start();
        screen.Show();
        Application.Run();
        try { if (!game.HasExited) game.Kill(); }
        catch (Exception) { }
        return 0;
    }

    private static Label FindLabel(Control c, string start)
    {
        foreach (Control child in c.Controls)
        {
            Label l = child as Label;
            if (l != null && l.Visible && l.Text.StartsWith(start)) return l;
            Label inner = FindLabel(child, start);
            if (inner != null) return inner;
        }
        return null;
    }

    private static void Save(Form f, string path, string note)
    {
        if (f.IsDisposed || !f.Visible)
        {
            File.AppendAllText(Path.ChangeExtension(path, ".txt"), note + " (hidden: no picture)\r\n");
            return;
        }
        using (Bitmap b = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
            b.Save(path, ImageFormat.Png);
        }
    }
}
