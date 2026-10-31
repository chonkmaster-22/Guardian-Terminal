using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GuardianTerminal.Build;
using GuardianTerminal.Game;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    // Guardian Terminal's window: the sidebar (the pages, Savage Wind and the DLC switch), the page that's open, and
    // the bar at the bottom (what's going on; Merge, Play unmodded, Play). The pages are in MainForm.Mods.cs (the
    // mods of the profile) and MainForm.Pages.cs (profiles, GameBanana, saves, tools, settings, about).
    internal sealed partial class MainForm : Form
    {
        private readonly float scale;
        private readonly ModLibrary library;
        private SavageWindInstall sw;
        private VanillaIndex van, dlcVan;  // the game's RomFS, the DLC's (null: no DLC, or it's switched off)
        private Task<VanillaIndex> indexing, dlcIndexing;
        private Process game;
        private bool busy;
        private BuildSummaryFile lastBuild;

        // sidebar
        private readonly Panel sidebar = new Panel();
        private readonly Dictionary<string, NavItem> nav = new Dictionary<string, NavItem>();
        private readonly Label swName = new Label(), swDetail = new Label();
        private readonly Panel swDot = new Panel();
        private readonly ToggleSwitch dlcSwitch = new ToggleSwitch();
        private readonly Label dlcLabel = new Label();
        // pages
        private readonly Panel host = new Panel();
        private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        private string currentPage;
        // the notice across the top (updates, ...)
        private readonly Panel banner = new Panel();
        private readonly Label bannerText = new Label();
        private readonly FlatButton bannerAction = new FlatButton(), bannerClose = new FlatButton();
        private Action bannerDo;
        // footer
        private readonly Label status = new Label();
        private readonly Label statusIcon = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly FlatButton merge = new FlatButton(), playVanilla = new FlatButton(), play = new FlatButton();
        private readonly ToolTip tips = new ToolTip();

        private readonly string startPage;

        public MainForm(string startPage = null)
        {
            this.startPage = startPage;
            scale = DeviceDpi / 96f;
            library = ModLibrary.Load();

            Text = "Guardian Terminal";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(1120), S(720));
            MinimumSize = new Size(S(900), S(600));
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            KeyPreview = true;

            BuildSidebar();
            BuildFooter();
            BuildBanner();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Window;

            pages["mods"] = BuildModsPage();
            pages["profiles"] = BuildProfilesPage();
            pages["browse"] = BuildBrowsePage();
            pages["saves"] = BuildSavesPage();
            pages["tools"] = BuildToolsPage();
            pages["settings"] = BuildSettingsPage();
            pages["about"] = BuildAboutPage();
            foreach (Control p in pages.Values) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }

            Controls.Add(host);
            Controls.Add(banner);
            Controls.Add(BuildFooterPanel());
            Controls.Add(sidebar);

            ShowPage("mods");
            HandleCreated += delegate { NativeMethods.UseDarkTitleBar(Handle); };
            Shown += OnShown;
            FormClosing += OnClosing;
            KeyDown += OnKey;
            // back from Savage Wind's own setup: is it ready now?
            Activated += delegate
            {
                if (sw == null && !busy && library.Config.SavageWindExe != null && File.Exists(library.Config.SavageWindExe)) RecheckPendingSetup();
            };
        }

        private int S(float px)
        {
            return (int)Math.Round(px * scale);
        }

        // ------------------------------------------------------------------ sidebar
        private void BuildSidebar()
        {
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = S(212);
            sidebar.BackColor = Theme.Sidebar;
            sidebar.Padding = new Padding(S(12), S(14), S(12), S(14));

            Panel brand = new Panel { Location = new Point(S(12), S(14)), Size = new Size(S(188), S(64)), BackColor = Theme.Sidebar };
            brand.Paint += (o, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Brand.Draw(e.Graphics, new Rectangle(S(2), S(4), S(54), S(54)), Color.White, Theme.Sidebar);
                TextRenderer.DrawText(e.Graphics, "Guardian", Theme.Brand, new Point(S(62), S(12)), Theme.Text);
                TextRenderer.DrawText(e.Graphics, "Terminal", Theme.Brand, new Point(S(62), S(32)), Theme.Text);
            };
            brand.Cursor = Cursors.Hand;
            brand.Click += delegate { ShowPage("about"); };
            sidebar.Controls.Add(brand);

            int y = S(96);
            foreach (string[] item in new[] {
                new[] { "mods", L.T("Mods"), Theme.IconMods }, new[] { "profiles", L.T("Profiles"), Theme.IconProfiles }, new[] { "browse", "GameBanana", Theme.IconBrowse },
                new[] { "saves", L.T("Saves"), Theme.IconSaves }, new[] { "tools", L.T("Tools"), Theme.IconTools }, new[] { "settings", L.T("Settings"), Theme.IconSettings },
                new[] { "about", L.T("About"), Theme.IconAbout } })
            {
                string key = item[0];
                NavItem n = new NavItem { Text = item[1], Glyph = item[2], Location = new Point(S(12), y), Size = new Size(S(188), S(38)) };
                n.Click += delegate { ShowPage(key); };
                nav[key] = n;
                sidebar.Controls.Add(n);
                y += S(42);
            }

            // Savage Wind and the DLC, at the bottom
            Card card = new Card { Fill = Theme.Surface, Size = new Size(S(188), S(104)), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            swDot.Size = new Size(S(8), S(8));
            swDot.Location = new Point(S(12), S(17));
            swDot.BackColor = Theme.Surface;
            swDot.Tag = Theme.Muted;
            swDot.Paint += (o, e) =>
            {
                e.Graphics.Clear(Theme.Surface);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush((Color)swDot.Tag)) e.Graphics.FillEllipse(b, 0, 0, swDot.Width - 1, swDot.Height - 1);
            };
            swName.SetBounds(S(26), S(9), S(156), S(22));
            swName.Font = Theme.SmallBold;
            swName.ForeColor = Theme.Text;
            swName.BackColor = Theme.Surface;
            swName.AutoEllipsis = true;
            swName.Text = "Savage Wind";
            swDetail.SetBounds(S(12), S(31), S(170), S(32));
            swDetail.Font = Theme.Small;
            swDetail.ForeColor = Theme.Muted;
            swDetail.BackColor = Theme.Surface;
            swDetail.AutoEllipsis = true;
            swDetail.Text = L.T("Looking for it...");
            swName.Cursor = swDetail.Cursor = Cursors.Hand;
            swName.Click += delegate { ShowPage("settings"); };
            swDetail.Click += delegate { ShowPage("settings"); };
            dlcLabel.SetBounds(S(12), S(70), S(124), S(24));
            dlcLabel.Text = "DLC";
            dlcLabel.ForeColor = Theme.TextSoft;
            dlcLabel.BackColor = Theme.Surface;
            dlcLabel.TextAlign = ContentAlignment.MiddleLeft;
            dlcLabel.AutoEllipsis = true;
            dlcSwitch.SetBounds(S(140), S(72), S(36), S(20));
            dlcSwitch.CheckedChanged += delegate { SwitchDlc(dlcSwitch.Checked); };
            tips.SetToolTip(dlcSwitch, L.T("Turn the DLC (The Master Trials and The Champions' Ballad) on or off for Savage Wind"));
            card.Controls.AddRange(new Control[] { swDot, swName, swDetail, dlcLabel, dlcSwitch });
            sidebar.Controls.Add(card);
            sidebar.Resize += delegate { card.Location = new Point(S(12), sidebar.ClientSize.Height - card.Height - S(14)); };
        }

        private void ShowPage(string key)
        {
            if (!pages.ContainsKey(key)) return;
            currentPage = key;
            foreach (KeyValuePair<string, Control> p in pages) p.Value.Visible = p.Key == key;
            foreach (KeyValuePair<string, NavItem> n in nav) n.Value.Selected = n.Key == key;
            if (key == "profiles") RefreshProfilesPage();
            if (key == "saves") RefreshSavesPage();
            if (key == "settings") RefreshSettingsPage();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F && currentPage == "mods") { search.Box.Focus(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5 && !busy) { PlayAsync(true); e.Handled = true; }
        }

        // ------------------------------------------------------------------ footer
        private void BuildFooter()
        {
            statusIcon.Font = Theme.IconFont;
            statusIcon.Text = "";
            statusIcon.ForeColor = Theme.Muted;
            statusIcon.TextAlign = ContentAlignment.MiddleCenter;
            status.AutoEllipsis = true;
            status.ForeColor = Theme.TextSoft;
            status.TextAlign = ContentAlignment.MiddleLeft;
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Visible = false;

            merge.Text = L.T("Merge");
            merge.Glyph = Theme.IconMerge;
            merge.Kind = ButtonKind.Ghost;
            merge.Click += delegate { MergeAsync(); };
            tips.SetToolTip(merge, L.T("Merge the mods that are on into the files Savage Wind loads, now (Play does it by itself when something changed)"));
            playVanilla.Text = L.T("Play unmodded");
            playVanilla.Kind = ButtonKind.Ghost;
            playVanilla.Click += delegate { PlayAsync(false); };
            tips.SetToolTip(playVanilla, L.T("Start Savage Wind without any mods (your mods stay as they are)"));
            play.Text = L.T("Play");
            play.Glyph = Theme.IconPlay;
            play.Kind = ButtonKind.Primary;
            play.Click += delegate { PlayAsync(true); };
        }

        private Panel footerPanel;

        private Panel BuildFooterPanel()
        {
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = S(66), BackColor = Theme.Footer };
            footerPanel = footer;
            footer.Paint += (o, e) => { using (Pen p = new Pen(Theme.Divider)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
            footer.Controls.AddRange(new Control[] { statusIcon, status, progress, merge, playVanilla, play });
            statusIcon.BackColor = status.BackColor = Theme.Footer;
            footer.Resize += delegate { LayoutFooter(); };
            return footer;
        }

        private void LayoutFooter()
        {
            Panel footer = footerPanel;
            if (footer == null) return;
            int h = footer.ClientSize.Height;
            // (as wide as their words need: they're longer in some languages)
            int pw = Math.Max(S(170), play.PreferredWidth(S(22))), vw = Math.Max(S(138), playVanilla.PreferredWidth(S(16))), mw = Math.Max(S(100), merge.PreferredWidth(S(14)));
            play.SetBounds(footer.ClientSize.Width - pw - S(18), (h - S(44)) / 2, pw, S(44));
            playVanilla.SetBounds(play.Left - vw - S(10), (h - S(36)) / 2, vw, S(36));
            merge.SetBounds(playVanilla.Left - mw - S(8), (h - S(36)) / 2, mw, S(36));
            int lift = progress.Visible ? S(5) : 0;
            statusIcon.SetBounds(S(16), (h - S(26)) / 2 - lift, S(24), S(26));
            status.SetBounds(S(44), (h - S(26)) / 2 - lift, Math.Max(S(100), merge.Left - S(60)), S(26));
            progress.SetBounds(S(46), status.Bottom + S(3), Math.Min(S(260), status.Width), S(5));
        }

        // ------------------------------------------------------------------ the notice across the top
        private void BuildBanner()
        {
            banner.Dock = DockStyle.Top;
            banner.Height = S(46);
            banner.BackColor = Theme.AccentSoft;
            banner.Visible = false;
            bannerText.ForeColor = Theme.AccentSoftText;
            bannerText.BackColor = Theme.AccentSoft;
            bannerText.AutoEllipsis = true;
            bannerText.TextAlign = ContentAlignment.MiddleLeft;
            bannerText.Font = Theme.BodyBold;
            bannerAction.Kind = ButtonKind.Primary;
            bannerAction.Compact = true;
            bannerAction.Click += delegate { if (bannerDo != null) bannerDo(); };
            bannerClose.Kind = ButtonKind.Ghost;
            bannerClose.Glyph = Theme.IconClose;
            bannerClose.Text = "";
            bannerClose.Click += delegate { banner.Visible = false; };
            banner.Controls.AddRange(new Control[] { bannerText, bannerAction, bannerClose });
            banner.Resize += delegate
            {
                bannerClose.SetBounds(banner.Width - S(46), S(7), S(32), S(32));
                bannerAction.SetBounds(bannerClose.Left - S(176), S(8), S(166), S(30));
                bannerText.SetBounds(S(18), 0, Math.Max(S(100), (bannerAction.Visible ? bannerAction.Left : bannerClose.Left) - S(30)), banner.Height);
            };
        }

        private void ShowBanner(string text, string action, Action onAction, Color back, Color fore)
        {
            bannerText.Text = text;
            banner.BackColor = bannerText.BackColor = back;
            bannerText.ForeColor = fore;
            bannerAction.Text = action ?? "";
            bannerAction.Visible = action != null;
            bannerDo = onAction;
            banner.Visible = true;
            banner.Width++;
            banner.Width--;
        }

        // ------------------------------------------------------------------ startup
        private async void OnShown(object sender, EventArgs e)
        {
            RefreshMods(null);
            LoadLastBuild();
            modList.Focus();
            if (!library.Config.WelcomeShown)
            {
                using (WelcomeForm w = new WelcomeForm(library.Config)) w.ShowDialog(this);
                library.Config.WelcomeShown = true;
                library.Config.Save();
            }
            await FindSavageWind();
            if (startPage != null) ShowPage(startPage);
            if (sw == null) return;
            StartIndexing();
            SetStatus(library.Mods.Count == 0 ? L.T("Ready. Add a mod to begin: Add mod, the GameBanana page, or drop a mod file on the window.") : L.T("Ready."), Theme.Muted);
            await RecheckOutdatedMods();
            CheckForUpdatesAsync(false);
        }

        // Savage Wind from the config, else looked for
        private async Task FindSavageWind()
        {
            string exe = library.Config.SavageWindExe;
            string problem = null;
            if (exe != null) sw = SavageWindInstall.Open(exe, out problem);
            // (one that's there but waiting for its game files to be set up is the one to use: no looking further)
            bool pendingSetup = sw == null && exe != null && File.Exists(exe);
            if (sw == null && !pendingSetup)
            {
                SetBusy(true, L.T("Looking for Savage Wind..."));
                string found = await Task.Run(() => SavageWindInstall.Detect());
                SetBusy(false, null);
                if (found != null) sw = SavageWindInstall.Open(found, out problem);
                if (sw != null) { library.Config.SavageWindExe = sw.Exe; library.Config.Save(); }
            }
            ShowInstall();
            if (sw == null && pendingSetup)
            {
                SetStatus(L.T("Savage Wind needs its game files set up: start it once and pick your own game files (1.9.0)."), Theme.Warn);
                ShowBanner(L.F("Savage Wind ({0}) needs its game files set up before mods can be played.", Path.GetDirectoryName(exe)), L.T("Start its setup"),
                    StartSavageWindSetup, Theme.WarnSoft, Theme.Warn);
            }
            else if (sw == null)
            {
                SetStatus(problem ?? L.T("Savage Wind wasn't found. Open Settings to install it or to show Guardian Terminal where it is."), Theme.Warn);
                ShowBanner(problem ?? L.T("Savage Wind wasn't found on this PC. Guardian Terminal needs it to play your mods."), L.T("Set up Savage Wind"),
                    () => ShowPage("settings"), Theme.WarnSoft, Theme.Warn);
            }
            else if (banner.Visible && banner.BackColor == Theme.WarnSoft) banner.Visible = false;
        }

        private void ShowInstall()
        {
            if (sw == null)
            {
                swDot.Tag = Theme.Warn;
                swName.Text = "Savage Wind";
                swDetail.Text = L.T("Not found: open Settings");
                dlcSwitch.Enabled = false;
                dlcSwitch.Show(false);
                dlcLabel.Text = "DLC";
            }
            else
            {
                swDot.Tag = Theme.Good;
                swName.Text = "Savage Wind" + (string.IsNullOrEmpty(sw.AppVersion) ? "" : " " + sw.AppVersion);
                swDetail.Text = L.F("Game files {0}", sw.GameVersion) + "\r\n" + sw.Folder;
                dlcSwitch.Enabled = sw.DlcSetUp;
                dlcSwitch.Show(sw.HasDlc);
                dlcLabel.Text = !sw.DlcSetUp ? L.T("DLC: not set up") : !sw.HasDlc ? L.T("DLC: off") : sw.DlcVersion != null ? "DLC " + sw.DlcVersion : L.T("DLC: on");
                tips.SetToolTip(swDetail, sw.Folder);
            }
            swDot.Invalidate();
            UpdateButtons();
        }

        private void StartIndexing()
        {
            van = dlcVan = null;
            SavageWindInstall current = sw;
            indexing = Task.Run(() => VanillaIndex.Build(current.RomfsDir));
            dlcIndexing = current.HasDlc ? Task.Run(() => VanillaIndex.Build(current.DlcRomfsDir)) : Task.FromResult<VanillaIndex>(null);
        }

        private async Task<bool> EnsureReady()
        {
            if (sw == null)
            {
                MessageBox.Show(this, L.T("First set up Savage Wind: open Settings to install it, or to show Guardian Terminal where it is."), "Savage Wind", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPage("settings");
                return false;
            }
            if (van != null) return true;
            string before = status.Text;
            Color beforeColor = status.ForeColor;
            SetBusy(true, L.T("Reading your game files..."));
            try
            {
                VanillaIndex d = await dlcIndexing;
                van = await indexing;
                dlcVan = d;
            }
            catch (Exception ex)
            {
                SetBusy(false, L.F("Couldn't read the game files: {0}", ex.Message), Theme.Bad);
                return false;
            }
            SetBusy(false, before, beforeColor);  // (what it said before: the caller goes on from there)
            return true;
        }

        // Mods checked by an older version, or before the DLC was set up, switched on or off, are checked again
        private async Task RecheckOutdatedMods()
        {
            if (sw == null || !await EnsureReady()) return;
            bool dlc = dlcVan != null;
            List<InstalledMod> outdated = library.Mods.Where(m => Directory.Exists(m.RomfsDir) &&
                (m.Info.AnalyzerVersion < ModImporter.AnalyzerVersion || m.Info.DlcFiles > 0 && m.Info.AnalyzedWithDlc != dlc)).ToList();
            if (outdated.Count == 0) return;
            SetBusy(true, L.T("Checking your mods against your game files..."));
            try
            {
                VanillaIndex v = van, d = dlcVan;
                bool dlcOff = sw.DlcSwitchedOff;
                await Task.Run(() =>
                {
                    foreach (InstalledMod m in outdated)
                    {
                        bool wasOff = m.Info.Status == ModStatus.NeedsDlc;
                        ModImporter.Analyze(m, v, d);
                        m.SaveInfo();
                        // (a mod that was waiting for the DLC isn't turned on by itself: the player decides; and one
                        // that needs the DLC stays as it is while the DLC is only switched off)
                        if (!wasOff && !dlcOff && (m.Info.Status == ModStatus.NeedsDlc || m.Info.Status == ModStatus.WiiU)) m.Enabled = false;
                    }
                });
                library.Save();
            }
            catch (Exception ex)
            {
                SetBusy(false, L.F("Couldn't check the mods: {0}", ex.Message), Theme.Warn);
                return;
            }
            SetBusy(false, L.T("Ready."));
            RefreshMods(modList.Selected);
        }

        // ------------------------------------------------------------------ the DLC switch
        private async void SwitchDlc(bool on)
        {
            if (sw == null || busy) { ShowInstall(); return; }
            if (game != null && !game.HasExited || sw.IsRunning())
            {
                MessageBox.Show(this, L.T("Close Savage Wind first: it's using the DLC's files."), "DLC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowInstall();
                return;
            }
            if (!on)
            {
                List<string> needing = library.Mods.Where(m => m.Enabled && m.Info.DlcFiles > 0).Select(m => m.DisplayName).ToList();
                string msg = L.T("Switch the DLC off?") + "\n\n" + L.T("Savage Wind then starts without The Master Trials and The Champions' Ballad. Nothing is deleted: its folders are renamed, and switching it back on is instant.") +
                    (needing.Count > 0 ? "\n\n" + L.T("These mods use the DLC, so their DLC parts are left out while it's off (they may not work right):") + "\n• " + string.Join("\n• ", needing) : "") +
                    "\n\n" + L.T("A save that used DLC items may miss them until the DLC is back on.");
                if (MessageBox.Show(this, msg, L.T("Switch the DLC off"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    ShowInstall();
                    return;
                }
            }
            try { sw.SetDlc(on); }
            catch (Exception ex)
            {
                MessageBox.Show(this, on ? L.F("Couldn't switch the DLC on: {0}", ex.Message) : L.F("Couldn't switch the DLC off: {0}", ex.Message), "DLC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowInstall();
                return;
            }
            ShowInstall();
            StartIndexing();
            SetStatus(on ? L.T("The DLC is on. Your mods are merged again for it when you press Play.") : L.T("The DLC is off. Your mods are merged again for it when you press Play."), Theme.Good);
            await RecheckOutdatedMods();
            RefreshMods(modList.Selected);
        }

        // ------------------------------------------------------------------ updates
        // Guardian Terminal, Savage Wind and the GameBanana mods; daily (when on) or now
        private async void CheckForUpdatesAsync(bool now)
        {
            if (!now && !Updates.Due(library.Config)) return;
            library.Config.LastUpdateCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            library.Config.Save();
            Updates.Release app = null;
            SavageWindRelease swNew = null;
            int modUpdates = 0;
            List<InstalledMod> mods = library.Mods.ToList();
            if (now) SetBusy(true, L.T("Checking for updates..."));
            try
            {
                await Task.Run(() =>
                {
                    try { app = Updates.CheckNow(); } catch (Exception) { }
                    try { swNew = SavageWindRelease.Newest(); } catch (Exception) { }
                    try { modUpdates = ModUpdates.Check(mods, t => { }); } catch (Exception) { }
                });
            }
            finally { if (now) SetBusy(false, null); }
            RefreshMods(modList.Selected);
            bool swOutdated = swNew != null && sw != null && !string.IsNullOrEmpty(sw.AppVersion) && Net.CompareVersions(swNew.Version, sw.AppVersion) > 0;
            if (app != null && app.Version != library.Config.IgnoredUpdate)
                ShowBanner(L.F("Guardian Terminal {0} is out (you have {1}).", app.Version, Updates.CurrentVersion), L.T("Get it on GitHub"), () => Open(app.PageUrl), Theme.AccentSoft, Theme.AccentSoftText);
            else if (swOutdated)
                ShowBanner(L.F("Savage Wind {0} is out (you have {1}).", swNew.Version, sw.AppVersion), L.T("Install it"), () => InstallSavageWind(swNew), Theme.AccentSoft, Theme.AccentSoftText);
            else if (modUpdates > 0)
                ShowBanner(L.P(modUpdates, "{0} of your mods has a newer version on GameBanana: look for \"Update\" on it.", "{0} of your mods have a newer version on GameBanana: look for \"Update\" on them."),
                    null, null, Theme.AccentSoft, Theme.AccentSoftText);
            if (now)
                SetStatus(app == null && !swOutdated && modUpdates == 0 ? L.T("Everything's up to date.") : L.T("Updates found: see the notice at the top."), Theme.Good);
        }

        // ------------------------------------------------------------------ status
        private void SetBusy(bool on, string text)
        {
            SetBusy(on, text, Theme.Muted);
        }

        private void SetBusy(bool on, string text, Color color)
        {
            busy = on;
            progress.Visible = on;
            UseWaitCursor = on;
            if (text != null) SetStatus(text, color);
            LayoutFooter();
            UpdateButtons();
        }

        private void SetStatus(string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color == Theme.Muted ? Theme.TextSoft : color;
            statusIcon.Text = color == Theme.Bad || color == Theme.Warn ? Theme.IconWarn : color == Theme.Good ? Theme.IconCheck : "";
            statusIcon.ForeColor = color;
            tips.SetToolTip(status, text);
        }

        private void Progress(string text)
        {
            if (IsHandleCreated) BeginInvoke((Action)(() => { status.Text = text; status.ForeColor = Theme.TextSoft; }));
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (busy && MessageBox.Show(this, L.T("Guardian Terminal is still working. Close it anyway?"), "Guardian Terminal", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                e.Cancel = true;
        }

        // ------------------------------------------------------------------ helpers
        private static void Open(string url)
        {
            try { Process.Start(url); }
            catch (Exception) { }
        }

        private void OpenFolder(string dir, string missing = null)
        {
            if (!Directory.Exists(dir))
            {
                if (missing != null) { SetStatus(missing, Theme.Muted); return; }
                Directory.CreateDirectory(dir);
            }
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        private void OpenFile(string path, string missing)
        {
            if (!File.Exists(path)) { SetStatus(missing, Theme.Muted); return; }
            Process.Start(path);
        }

        private static long FolderSize(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            long n = 0;
            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { n += new FileInfo(f).Length; }
                catch (IOException) { }
            }
            return n;
        }

        private static string SizeText(long bytes)
        {
            if (bytes >= 1L << 30) return string.Format("{0:N1} GB", bytes / 1073741824.0);
            if (bytes >= 1L << 20) return string.Format("{0:N0} MB", bytes / 1048576.0);
            return string.Format("{0:N0} KB", Math.Max(1, bytes / 1024));
        }

        // A page's heading and the line under it
        private Panel PageHeader(string title, string subtitle)
        {
            Panel p = new Panel { Dock = DockStyle.Top, Height = S(subtitle == null ? 64 : 86), BackColor = Theme.Window };
            Label t = new Label { Text = title, Font = Theme.PageTitle, ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(28), S(20)), BackColor = Theme.Window };
            p.Controls.Add(t);
            if (subtitle != null)
            {
                Label s = new Label { Text = subtitle, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Location = new Point(S(30), S(56)), Size = new Size(S(800), S(22)), BackColor = Theme.Window };
                p.Controls.Add(s);
                p.Resize += delegate { s.Width = Math.Max(S(200), p.Width - S(60)); };
            }
            return p;
        }
    }
}
