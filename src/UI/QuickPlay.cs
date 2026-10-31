using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using GuardianTerminal.Build;
using GuardianTerminal.Game;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    // "--play" (the "Play with mods" shortcut): gets the mods ready in a small window, starts the game with the loading
    // screen over it (which says what went wrong if it fails), and closes once the game is on screen.
    internal sealed class QuickPlay : Form
    {
        private readonly Label label = new Label();

        public QuickPlay()
        {
            float s = DeviceDpi / 96f;
            Text = "Guardian Terminal";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size((int)(420 * s), (int)(110 * s));
            label.SetBounds((int)(20 * s), (int)(22 * s), (int)(380 * s), (int)(40 * s));
            label.Text = L.T("Getting your mods ready...");
            ProgressBar bar = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            bar.SetBounds((int)(20 * s), (int)(70 * s), (int)(380 * s), (int)(10 * s));
            Controls.Add(label);
            Controls.Add(bar);
            HandleCreated += delegate { NativeMethods.UseDarkTitleBar(Handle); };
            Shown += OnShown;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            ModLibrary library = ModLibrary.Load();
            string problem;
            SavageWindInstall sw = SavageWindInstall.Open(library.Config.SavageWindExe, out problem);
            if (sw == null)
            {
                Fail((problem ?? L.T("Savage Wind wasn't found.")) + "\n\n" + L.T("Open Guardian Terminal to set it up."));
                return;
            }
            if (sw.IsRunning())
            {
                Fail(L.T("Savage Wind is already running."));
                return;
            }
            BuildResult r;
            try
            {
                Action<string> progress = t => BeginInvoke((Action)(() => label.Text = t));
                r = await Task.Run(() =>
                {
                    VanillaIndex van = VanillaIndex.Build(sw.RomfsDir);
                    VanillaIndex dlcVan = sw.HasDlc ? VanillaIndex.Build(sw.DlcRomfsDir) : null;
                    return OverlayBuilder.Build(library.EnabledInOrder(), sw, van, dlcVan, library.Config, false, progress);
                });
            }
            catch (Exception ex)
            {
                Fail(L.T("Getting the mods ready failed:") + "\n\n" + ex.Message + "\n\n" + L.T("Open Guardian Terminal for details."));
                return;
            }
            label.Text = L.T("Starting Savage Wind...");
            Process game;
            try { game = sw.Launch(r.OverlayDir); }
            catch (Exception ex)
            {
                Fail(L.T("Savage Wind couldn't be started:") + "\n\n" + ex.Message);
                return;
            }
            // this window goes; the app ends with the loading screen (when the game is on screen, or after its error)
            LaunchScreen screen = new LaunchScreen(sw, game, r.OverlayDir != null, r.Summary);
            screen.Finished += ok => BeginInvoke((Action)Close);
            Hide();
            screen.Show();
        }

        private void Fail(string message)
        {
            MessageBox.Show(this, message, "Guardian Terminal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }
}
