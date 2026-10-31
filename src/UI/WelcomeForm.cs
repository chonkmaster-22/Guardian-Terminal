using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuardianTerminal.Game;

namespace GuardianTerminal.UI
{
    // The welcome every new player sees once (and again from About): what Guardian Terminal is, who made it and who
    // made Savage Wind, and the three steps to playing mods.
    internal sealed class WelcomeForm : Form
    {
        private readonly float s;

        public WelcomeForm(AppConfig config)
        {
            s = DeviceDpi / 96f;
            Text = L.T("Welcome to Guardian Terminal");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(S(720), S(560));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            Panel art = new Panel { Location = new Point(S(32), S(30)), Size = new Size(S(92), S(92)), BackColor = Theme.Window };
            art.Paint += (o, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Brand.Draw(e.Graphics, new Rectangle(0, 0, art.Width, art.Height), Color.White, Theme.Window); };
            Label title = new Label { Text = L.T("Welcome to Guardian Terminal"), Font = new Font("Segoe UI Semibold", 19F), ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(140), S(38)) };
            Label tag = new Label { Text = L.T("Breath of the Wild Switch mods, merged and played in Savage Wind."), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(143), S(80)) };

            Card credits = new Card { Fill = Theme.Surface, Location = new Point(S(32), S(140)), Size = new Size(S(656), S(118)) };
            RichTextBox text = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ScrollBars = RichTextBoxScrollBars.None, TabStop = false, DetectUrls = false };
            text.SetBounds(S(18), S(14), S(620), S(96));
            Action<string, Color, Font> add = (t, c, f) => { text.SelectionStart = text.TextLength; text.SelectionLength = 0; text.SelectionColor = c; text.SelectionFont = f; text.AppendText(t); };
            // (filled once the box exists: before that, its colours are lost)
            text.HandleCreated += delegate
            {
                MainForm.WithNames(add, L.T("{0} is made by {1}."), "Guardian Terminal", "chonkmaster-22");
                add(" ", Theme.TextSoft, Theme.Body);
                MainForm.WithNames(add, L.T("It plays your mods in {0}, the Breath of the Wild PC port made by {1} (built on LAS Recomp by peiikai). They made running the game possible; Guardian Terminal takes care of your mods. Fan-made and free: not affiliated with Nintendo or the Savage Wind project."),
                    "Savage Wind", L.T("Revivedx and the Savage Wind Recomp contributors"));
            };
            credits.Controls.Add(text);

            Label how = new Label { Text = L.T("How it works"), Font = Theme.Big, ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(32), S(276)) };
            Control step1 = Step(1, L.T("Savage Wind, with your own game files"), L.T("Guardian Terminal finds it, or installs the newest release from its GitHub page (Settings). Its setup asks for your own 1.9.0 game files."), S(310));
            Control step2 = Step(2, L.T("Add mods"), L.T("From the GameBanana page, or drop a mod's .zip / .7z / .rar / .bnp on the window. Switch mods (with a romfs folder)."), S(372));
            Control step3 = Step(3, L.T("Press Play"), L.T("The mods that are on are merged into a copy for Savage Wind: your game files are never changed, and your saves are backed up first."), S(434));

            ToggleSwitch updates = new ToggleSwitch { Location = new Point(S(34), S(508)), Size = new Size(S(40), S(22)) };
            updates.Show(config.CheckForUpdates);
            updates.CheckedChanged += delegate { config.CheckForUpdates = updates.Checked; };
            Label updatesText = new Label { Text = L.T("Check for updates once a day"), ForeColor = Theme.TextSoft, AutoSize = true, Location = new Point(S(82), S(510)) };
            updatesText.Click += delegate { updates.Checked = !updates.Checked; };
            FlatButton go = new FlatButton { Text = L.T("Get started"), Kind = ButtonKind.Primary, Compact = true, Size = new Size(S(160), S(42)), Location = new Point(S(528), S(498)), DialogResult = DialogResult.OK };
            AcceptButton = go;
            CancelButton = go;

            Controls.AddRange(new Control[] { art, title, tag, credits, how, step1, step2, step3, updates, updatesText, go });
            HandleCreated += delegate { NativeMethods.UseDarkTitleBar(Handle); };
            Shown += delegate { go.Focus(); };
        }

        private int S(int px) { return (int)Math.Round(px * s); }

        private Control Step(int n, string title, string text, int y)
        {
            Panel p = new Panel { Location = new Point(S(32), y), Size = new Size(S(656), S(56)), BackColor = Theme.Window };
            p.Paint += (o, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, 0, S(4), S(32), S(32));
                TextRenderer.DrawText(g, n.ToString(), Theme.BodyBold, new Rectangle(0, S(4), S(32), S(32)), Theme.AccentSoftText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, title, Theme.BodyBold, new Point(S(46), S(2)), Theme.Text);
                TextRenderer.DrawText(g, text, Theme.Small, new Rectangle(S(46), S(22), p.Width - S(46), S(34)), Theme.Muted, TextFormatFlags.WordBreak);
            };
            return p;
        }
    }
}
