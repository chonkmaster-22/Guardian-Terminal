using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GuardianTerminal.Game;

namespace GuardianTerminal.UI
{
    // Shown over the screen while Savage Wind starts (its window stays black, with an empty frame, until the game's
    // first frame: 15 seconds or more): what it's doing, step by step, from its log (LaunchWatcher). It goes away when
    // the game is on screen; if Savage Wind fails first, it says why and shows the end of the log. Savage Wind's own
    // message boxes (a fatal error: the error and advice, then it closes) are left in front: this screen hides while
    // one is open, and doesn't repeat an error Savage Wind has already shown.
    internal sealed class LaunchScreen : Form
    {
        // no frame yet but still working after this long: this screen gets out of the game's way all the same
        private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(3);

        private readonly LaunchWatcher watcher;
        private readonly Process game;
        private readonly bool withMods;
        private readonly Timer timer = new Timer { Interval = 250 };
        private readonly DateTime shownAt = DateTime.UtcNow;
        private readonly Panel card = new Panel();
        private readonly Label title = new Label(), hint = new Label(), elapsed = new Label();
        private readonly Canvas steps = new Canvas();
        private readonly TextBox logBox = new TextBox();
        private readonly FlowLayoutPanel buttons = new FlowLayoutPanel();
        private readonly float s;
        private int dots;
        private bool failedShown;
        private bool gameSpoke;        // Savage Wind showed a message box of its own
        private DateTime? failedSeen;  // when the log first said it failed

        // Raised once the game is on screen (true) or the start failed or was given up on (false)
        public event Action<bool> Finished;

        public LaunchStage Stage { get { return watcher.Stage; } }
        public string Error { get { return watcher.Error; } }

        public LaunchScreen(SavageWindInstall sw, Process game, bool withMods, string modSummary)
        {
            this.game = game;
            this.withMods = withMods;
            watcher = new LaunchWatcher(sw, game);
            s = DeviceDpi / 96f;

            Text = L.T("Starting Breath of the Wild");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;  // (Savage Wind opens on the main display)
            BackColor = Color.Black;
            ForeColor = Theme.Text;
            Font = Theme.Body;

            card.BackColor = Theme.Window;
            card.Size = new Size(S(620), S(380));  // (room for all six steps)
            card.Padding = new Padding(S(28), S(24), S(28), S(20));
            title.Font = Theme.Title;
            title.AutoSize = false;
            title.Dock = DockStyle.Top;
            title.Height = S(44);
            title.Text = L.T("Starting Breath of the Wild");
            hint.Dock = DockStyle.Top;
            hint.Height = S(46);
            hint.ForeColor = Theme.Muted;
            hint.Text = (withMods && !string.IsNullOrEmpty(modSummary) ? L.F("With your mods: {0}.", modSummary) + "\n" : "") + L.T("This screen closes when the game appears.");
            hint.AutoEllipsis = true;
            steps.Dock = DockStyle.Fill;
            steps.Font = new Font("Segoe UI", 11F);
            steps.ForeColor = Theme.Text;
            steps.Paint += OnPaintSteps;
            elapsed.Dock = DockStyle.Bottom;
            elapsed.Height = S(22);
            elapsed.ForeColor = Theme.Muted;
            elapsed.Font = Theme.Small;
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.WordWrap = false;
            logBox.BackColor = Theme.Surface;
            logBox.ForeColor = Theme.Muted;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Consolas", 8.5F);
            logBox.Visible = false;
            logBox.HandleCreated += delegate { NativeMethods.SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null); };
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = S(46);
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(0, S(8), 0, 0);
            FlatButton hide = Theme.MakeButton(L.T("Hide this screen"), false);
            hide.Size = new Size(S(150), S(32));
            hide.Click += delegate { Done(true); };  // (the game goes on; this is only the screen in front of it)
            buttons.Controls.Add(hide);
            card.Controls.Add(steps);
            card.Controls.Add(logBox);
            card.Controls.Add(elapsed);
            card.Controls.Add(buttons);
            card.Controls.Add(hint);
            card.Controls.Add(title);
            Controls.Add(card);
            Resize += delegate { Center(); };

            timer.Tick += delegate { Tick(); };
            Shown += delegate { Center(); timer.Start(); Tick(); };
            FormClosed += delegate { timer.Stop(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private int S(int px)
        {
            return (int)Math.Round(px * s);
        }

        private void Center()
        {
            card.Location = new Point((ClientSize.Width - card.Width) / 2, (ClientSize.Height - card.Height) / 2);
        }

        private void Tick()
        {
            watcher.Poll();
            if (!failedShown && NativeMethods.HasOpenDialog(game.Id))
            {
                gameSpoke = true;
                if (Visible) Hide();  // (its message box, in front of everything, to be read and answered)
                return;
            }
            if (!Visible && !failedShown && watcher.Stage != LaunchStage.Failed) Show();
            TimeSpan t = DateTime.UtcNow - shownAt;
            // no log to follow (another Savage Wind that logs elsewhere), or minutes without a frame while it keeps
            // working: this screen mustn't sit in front of the game
            if (watcher.Stage != LaunchStage.Failed && (!watcher.Attached && t > TimeSpan.FromSeconds(30) || t > GiveUpAfter))
            {
                timer.Stop();
                Done(true);
                return;
            }
            switch (watcher.Stage)
            {
                case LaunchStage.Running:
                    timer.Stop();
                    steps.Refresh();
                    Done(true);
                    return;
                case LaunchStage.SetupScreen:  // (Savage Wind shows its own start screen: it says what it needs)
                    timer.Stop();
                    Done(false);
                    return;
                case LaunchStage.Failed:
                    if (failedShown) return;
                    if (failedSeen == null) failedSeen = DateTime.UtcNow;
                    bool exited = GameExited();
                    if (!exited && DateTime.UtcNow - failedSeen.Value < TimeSpan.FromSeconds(1.5)) return;  // (its own message box may be coming)
                    if (gameSpoke && exited)
                    {
                        // it said what went wrong itself
                        timer.Stop();
                        Done(false);
                        return;
                    }
                    ShowFailure();
                    return;
            }
            dots = (dots + 1) % 4;
            steps.Invalidate();
            elapsed.Text = L.F("{0}:{1:00}  -  the first start after an update or a new mod can take a minute or two.", (int)t.TotalMinutes, t.Seconds);
        }

        private bool GameExited()
        {
            try { return game.HasExited; }
            catch (InvalidOperationException) { return true; }
        }

        // The steps, each with its mark in a column of its own: ✔ done, ▸ now (with dots going), • still to come
        private void OnPaintSteps(object sender, PaintEventArgs e)
        {
            LaunchStage at = watcher.Stage;
            string busy = new string('.', dots + 1);
            var lines = new[]
            {
                Tuple.Create(L.T("Starting Savage Wind"), at > LaunchStage.Starting, at == LaunchStage.Starting),
                Tuple.Create(L.T("Loading the game code"), at > LaunchStage.GameCode, at == LaunchStage.GameCode),
                Tuple.Create(L.T("Reading the game data"), at > LaunchStage.GameData, at == LaunchStage.GameData),
                withMods ? Tuple.Create(L.T("Loading your mods"), watcher.ModsSeen && at > LaunchStage.Mods, at == LaunchStage.Mods) : null,
                Tuple.Create(L.T("Loading the DLC (if it's set up)"), at >= LaunchStage.Running || watcher.DlcSeen && at > LaunchStage.Dlc, at == LaunchStage.Dlc),
                Tuple.Create(L.T("Starting the game"), at >= LaunchStage.Running, false)
            }.Where(l => l != null);
            int y = 0, h = S(26);
            foreach (var l in lines)
            {
                bool done = l.Item2, now = l.Item3;
                Color mark = done ? Theme.Good : now ? Theme.Accent : Theme.Muted;
                TextRenderer.DrawText(e.Graphics, done ? "✔" : now ? "▸" : "•", steps.Font, new Rectangle(0, y, S(24), h), mark,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, l.Item1 + (now ? busy : ""), steps.Font, new Rectangle(S(32), y, steps.Width - S(32), h),
                    done || now ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                y += h;
            }
        }

        private void ShowFailure()
        {
            failedShown = true;
            timer.Interval = 1000;
            BackColor = Color.FromArgb(0x0B, 0x10, 0x15);
            title.Text = L.T("Savage Wind couldn't start the game");
            title.ForeColor = Theme.Bad;
            hint.Text = watcher.Error;
            hint.ForeColor = Theme.Text;
            hint.Height = S(44);
            steps.Visible = false;
            elapsed.Text = L.T("The end of Savage Wind's log:") + (withMods ? "  " + L.T("If it only happens with mods on, turn them off one at a time to find the one causing it.") : "");
            elapsed.Dock = DockStyle.Top;
            logBox.Text = string.Join("\r\n", watcher.Tail.Skip(Math.Max(0, watcher.Tail.Count() - 14)));
            logBox.Visible = true;
            logBox.BringToFront();
            card.Size = new Size(S(820), S(470));
            Center();
            buttons.Controls.Clear();
            FlatButton close = Theme.MakeButton(L.T("Close"), true);
            close.Font = Theme.BodyBold;
            close.Size = new Size(S(110), S(34));
            close.Click += delegate { StopGame(); Done(false); };
            FlatButton log = Theme.MakeButton(L.T("Open the full log"), false);
            log.Size = new Size(S(150), S(34));
            log.Click += delegate { try { Process.Start(watcher.LogPath); } catch (Exception) { } };
            buttons.Controls.Add(close);
            buttons.Controls.Add(log);
            if (watcher.Stuck)
            {
                FlatButton wait = Theme.MakeButton(L.T("Keep waiting"), false);
                wait.Size = new Size(S(130), S(34));
                wait.Click += delegate { Done(false); };  // (the game keeps going; this screen goes)
                buttons.Controls.Add(wait);
            }
            buttons.Visible = true;
            Show();
            Activate();
        }

        // Ends a stuck or failed start for good (the game window would otherwise stay black).
        private void StopGame()
        {
            try { if (!game.HasExited) game.Kill(); }
            catch (Exception) { }
        }

        private void Done(bool ok)
        {
            Action<bool> f = Finished;
            Finished = null;
            if (f != null) f(ok);
            Close();
        }

        // (painted 4 times a second: drawn off screen first, so it doesn't flicker)
        private sealed class Canvas : Panel
        {
            public Canvas()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }
    }
}
