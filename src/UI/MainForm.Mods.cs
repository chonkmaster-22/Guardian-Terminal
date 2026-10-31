using System;
using System.Collections.Concurrent;
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
    // The Mods page: the profile's mods as cards (on / off, load order by dragging), search, Add mod, and the details
    // of the one selected; with what merging and playing do.
    internal sealed partial class MainForm
    {
        private readonly ModCardList modList = new ModCardList();
        private SearchBox search;
        private readonly FlatButton profileButton = new FlatButton(), viewButton = new FlatButton(), addButton = new FlatButton();
        private readonly ContextMenuStrip profileMenu = Theme.Menu(), addMenu = Theme.Menu(), itemMenu = Theme.Menu();
        private Panel emptyState, detailsPanel;
        private Panel picture;
        private readonly Label detailName = new Label(), detailMeta = new Label(), detailStatus = new Label();
        private readonly RichTextBox detailNotes = new RichTextBox();
        private readonly FlatButton detailFolder = new FlatButton(), detailPage = new FlatButton(), detailUpdate = new FlatButton(), detailRemove = new FlatButton();
        private readonly ConcurrentDictionary<string, long> modSizes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        private Control BuildModsPage()
        {
            Panel page = new Panel { BackColor = Theme.Window };

            // ---- the bar: profile, search, view, add
            Panel bar = new Panel { Dock = DockStyle.Top, Height = S(68), BackColor = Theme.Window };
            profileButton.Glyph = Theme.IconProfiles;
            profileButton.Kind = ButtonKind.Secondary;
            profileButton.Click += delegate { FillProfileMenu(); profileMenu.Show(profileButton, new Point(0, profileButton.Height + S(2))); };
            tips.SetToolTip(profileButton, L.T("Profiles: separate sets of mods, each with its own on / off and load order"));
            search = new SearchBox(L.T("Search your mods  (Ctrl+F)"));
            search.SearchChanged += delegate { modList.SetFilter(search.Query); ShowDetails(); };
            viewButton.Kind = ButtonKind.Ghost;
            viewButton.Glyph = library.Config.ShowCards ? Theme.IconRows : Theme.IconCards;
            viewButton.Text = "";
            viewButton.Click += delegate
            {
                library.Config.ShowCards = !library.Config.ShowCards;
                library.Config.Save();
                modList.Cards = library.Config.ShowCards;
                viewButton.Glyph = library.Config.ShowCards ? Theme.IconRows : Theme.IconCards;
                viewButton.Invalidate();
            };
            tips.SetToolTip(viewButton, L.T("Cards with pictures, or compact rows"));
            addButton.Text = L.T("Add mod");
            addButton.Glyph = Theme.IconAdd;
            addButton.Kind = ButtonKind.Secondary;
            addButton.Click += delegate { addMenu.Show(addButton, new Point(addButton.Width - addMenu.Width, addButton.Height + S(2))); };
            addMenu.Items.Add(L.T("Mod files (.zip, .7z, .rar, .bnp)..."), null, delegate { AddFromFileDialog(); });
            addMenu.Items.Add(L.T("Mod folder (already unpacked)..."), null, delegate { AddFromFolderDialog(); });
            addMenu.Items.Add(L.T("Import many: emulators, SD card, a folder of mods..."), null, delegate { ImportMods(); });
            addMenu.Items.Add(new ToolStripSeparator());
            addMenu.Items.Add(L.T("Browse GameBanana"), null, delegate { ShowPage("browse"); });
            bar.Controls.AddRange(new Control[] { profileButton, search, viewButton, addButton });
            bar.Resize += delegate
            {
                int y = S(18), h = S(36);
                profileButton.SetBounds(S(24), y, Math.Min(S(260), profileButton.PreferredWidth(S(16)) + S(14)), h);
                int aw = Math.Max(S(130), addButton.PreferredWidth(S(16)));
                addButton.SetBounds(bar.Width - S(24) - aw, y, aw, h);
                viewButton.SetBounds(addButton.Left - S(10) - S(38), y, S(38), h);
                search.SetBounds(profileButton.Right + S(12), y, Math.Max(S(120), viewButton.Left - S(12) - profileButton.Right - S(12)), h);
            };

            // ---- the filter chips: all, on, off, updates, needs a look
            FlowLayoutPanel chipRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(42), BackColor = Theme.Window, WrapContents = false, Padding = new Padding(S(22), 0, S(12), S(6)) };
            foreach (string key in new[] { "all", "on", "off", "updates", "look" })
            {
                string k = key;
                FlatButton chip = new FlatButton { Kind = k == "all" ? ButtonKind.Secondary : ButtonKind.Ghost, Compact = true, Margin = new Padding(0, 0, S(6), 0) };
                chip.Click += delegate { SetChip(k); };
                chips[k] = chip;
                chipRow.Controls.Add(chip);
            }

            // ---- the cards, the details beside them
            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(S(12), 0, S(12), S(8)) };
            modList.Dock = DockStyle.Fill;
            modList.Cards = library.Config.ShowCards;
            modList.AllowDrop = true;
            modList.DragEnter += OnDragEnter;
            modList.DragDrop += OnDragDrop;
            modList.Subtitle = Subtitle;
            modList.Badges = BadgesFor;
            modList.SelectionChanged += delegate { ShowDetails(); UpdateButtons(); };
            modList.ToggleRequested += ToggleMod;
            modList.MoveRequested += (m, to) => { if (!busy) { library.MoveTo(m, to); RefreshMods(m); SetStatus(L.F("{0} moved to place {1}. Later mods win when two change the same file.", m.DisplayName, to + 1), Theme.Muted); } };
            modList.OpenRequested += m => OpenModFolder(m);
            modList.DeleteRequested += m => RemoveMod(m);
            itemMenu.Items.Add("-");  // (filled each time it opens: FillItemMenu)
            itemMenu.Opening += (o, e) =>
            {
                e.Cancel = modList.Selected == null || busy;
                if (!e.Cancel) FillItemMenu();
            };
            modList.ItemMenu = itemMenu;

            detailsPanel = BuildDetails();
            detailsPanel.Dock = DockStyle.Right;
            emptyState = BuildEmptyState();
            emptyState.Dock = DockStyle.Fill;
            emptyState.Visible = false;
            body.Controls.Add(modList);
            body.Controls.Add(emptyState);
            body.Controls.Add(detailsPanel);

            page.Controls.Add(body);
            page.Controls.Add(chipRow);
            page.Controls.Add(bar);
            return page;
        }

        // ------------------------------------------------------------------ filter chips
        private readonly Dictionary<string, FlatButton> chips = new Dictionary<string, FlatButton>();
        private string chip = "all";

        private Func<InstalledMod, bool> ChipTest(string key)
        {
            switch (key)
            {
                case "on": return m => m.Enabled;
                case "off": return m => !m.Enabled;
                case "updates": return m => m.Info.NewerFileDate != null;
                case "look": return m => m.Info.Status != ModStatus.Ok || m.Enabled && ConflictsOf(m).Any(c => c.Winner != m.DisplayName);
                default: return m => true;
            }
        }

        private void SetChip(string key)
        {
            chip = key;
            modList.SetPredicate(ChipTest(key), key != "all");
            UpdateChips();
            ShowDetails();
        }

        private void UpdateChips()
        {
            Dictionary<string, string> names = new Dictionary<string, string>
            {
                { "all", L.T("All") }, { "on", L.T("On") }, { "off", L.T("Off") }, { "updates", L.T("Updates") }, { "look", L.T("Needs a look") }
            };
            foreach (KeyValuePair<string, FlatButton> c in chips)
            {
                int n = library.Mods.Count(ChipTest(c.Key));
                c.Value.Text = names[c.Key] + "  " + n;
                c.Value.Kind = c.Key == chip ? ButtonKind.Secondary : ButtonKind.Ghost;
                c.Value.Size = new Size(Math.Max(S(56), c.Value.PreferredWidth(S(12))), S(30));
                c.Value.Visible = c.Key == "all" || c.Key == "on" || c.Key == "off" || n > 0;
                c.Value.Invalidate();
            }
        }

        // The mod's right-click menu: for the one selected, or for several
        private void FillItemMenu()
        {
            itemMenu.Items.Clear();
            List<InstalledMod> sel = modList.SelectedMods;
            if (sel.Count > 1)
            {
                itemMenu.Items.Add(L.F("Turn on ({0})", sel.Count), null, delegate { BulkEnable(true); });
                itemMenu.Items.Add(L.F("Turn off ({0})", sel.Count), null, delegate { BulkEnable(false); });
                itemMenu.Items.Add(new ToolStripSeparator());
                itemMenu.Items.Add(L.T("Move to the top (load first)"), null, delegate { BulkMove(true); });
                itemMenu.Items.Add(L.T("Move to the bottom (load last: wins)"), null, delegate { BulkMove(false); });
                itemMenu.Items.Add(new ToolStripSeparator());
                itemMenu.Items.Add(L.F("Uninstall ({0})...", sel.Count), null, delegate { BulkRemove(); });
                return;
            }
            InstalledMod m = modList.Selected;
            itemMenu.Items.Add(m.Enabled ? L.T("Turn off") : L.T("Turn on"), null, delegate { ToggleMod(m); });
            itemMenu.Items.Add(L.T("Move up (load earlier)"), null, delegate { MoveSelected(-1); });
            itemMenu.Items.Add(L.T("Move down (load later: wins)"), null, delegate { MoveSelected(1); });
            itemMenu.Items.Add(L.T("Move to the top"), null, delegate { BulkMove(true); });
            itemMenu.Items.Add(L.T("Move to the bottom"), null, delegate { BulkMove(false); });
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add(L.T("Open its folder"), null, delegate { OpenModFolder(m); });
            if (!string.IsNullOrEmpty(m.Info.SourceUrl))
            {
                itemMenu.Items.Add(L.T("Open its GameBanana page"), null, delegate { Open(m.Info.SourceUrl); });
                itemMenu.Items.Add(L.T("Find it on the GameBanana page"), null, delegate { UpdateMod(m); });
            }
            itemMenu.Items.Add(new ToolStripSeparator());
            itemMenu.Items.Add(L.T("Select all (Ctrl+A)"), null, delegate { modList.SelectAll(); });
            itemMenu.Items.Add(L.T("Uninstall..."), null, delegate { RemoveMod(m); });
        }

        // ------------------------------------------------------------------ several mods at once
        private void BulkEnable(bool on)
        {
            List<InstalledMod> sel = modList.SelectedMods;
            if (busy || sel.Count == 0) return;
            if (on)
            {
                List<string> risky = sel.Where(m => !m.Enabled && (m.Info.Status == ModStatus.NeedsDlc || m.Info.Status == ModStatus.WiiU || m.Info.Status == ModStatus.Empty)).Select(m => m.DisplayName).ToList();
                if (risky.Count > 0 && MessageBox.Show(this, L.T("These probably won't work with Savage Wind:") + "\n• " + string.Join("\n• ", risky) + "\n\n" + L.T("Turn them on anyway?"),
                        L.T("Turn mods on"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    sel = sel.Where(m => !risky.Contains(m.DisplayName)).ToList();
            }
            foreach (InstalledMod m in sel) m.Enabled = on;
            library.Save();
            RefreshMods(null);
            SetStatus((on ? L.P(sel.Count, "{0} mod turned on.", "{0} mods turned on.") : L.P(sel.Count, "{0} mod turned off.", "{0} mods turned off.")) + " " + L.T("Press Play to use them (they're merged then)."), Theme.Muted);
        }

        private void BulkMove(bool top)
        {
            List<InstalledMod> sel = modList.SelectedMods;
            if (busy || sel.Count == 0) return;
            if (top) { for (int i = sel.Count - 1; i >= 0; i--) library.MoveTo(sel[i], 0); }
            else foreach (InstalledMod m in sel) library.MoveTo(m, library.Mods.Count - 1);
            RefreshMods(null);
            SetStatus(top ? L.T("Moved to the top: loaded first (other mods win over them).") : L.T("Moved to the bottom: loaded last (they win over the others)."), Theme.Muted);
        }

        private void BulkRemove()
        {
            List<InstalledMod> sel = modList.SelectedMods;
            if (busy || sel.Count == 0) return;
            if (sel.Count == 1) { RemoveMod(sel[0]); return; }
            if (MessageBox.Show(this, L.F("Uninstall these {0} mods?", sel.Count) + "\n• " + string.Join("\n• ", sel.Select(m => m.DisplayName).Take(20)) + "\n\n" +
                    L.T("They're deleted from Guardian Terminal and taken out of every profile. Your original downloads aren't touched."),
                    L.T("Uninstall mods"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int failed = 0;
            foreach (InstalledMod m in sel)
            {
                modList.ForgetThumbnail(m);
                try { library.Remove(m); }
                catch (Exception) { failed++; }
            }
            RefreshMods(null);
            SetStatus(L.P(sel.Count - failed, "Uninstalled {0} mod.", "Uninstalled {0} mods.") + (failed > 0 ? " " + L.T("Some files couldn't be deleted: close Savage Wind and try again.") : ""), failed > 0 ? Theme.Warn : Theme.Muted);
        }

        private Panel BuildEmptyState()
        {
            Panel p = new Panel { BackColor = Theme.Window, AllowDrop = true };
            p.DragEnter += OnDragEnter;
            p.DragDrop += OnDragDrop;
            FlatButton file = new FlatButton { Text = L.T("Add a mod file"), Glyph = Theme.IconAdd, Kind = ButtonKind.Primary, Compact = true, Size = new Size(S(180), S(40)) };
            file.Click += delegate { AddFromFileDialog(); };
            FlatButton gb = new FlatButton { Text = L.T("Find mods on GameBanana"), Glyph = Theme.IconBrowse, Kind = ButtonKind.Secondary, Size = new Size(S(230), S(40)) };
            gb.Click += delegate { ShowPage("browse"); };
            p.Controls.Add(file);
            p.Controls.Add(gb);
            p.Paint += (o, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = p.Width / 2, top = Math.Max(S(20), p.Height / 2 - S(150));
                Brand.Draw(g, new Rectangle(cx - S(56), top, S(112), S(112)), Theme.Border, Theme.Window);
                TextRenderer.DrawText(g, L.T("Add your first mod"), Theme.Title, new Rectangle(0, top + S(124), p.Width, S(32)), Theme.Text, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, L.T("Drop a mod's .zip, .7z, .rar or .bnp anywhere on this window, or pick one.\nBreath of the Wild Switch mods work (the ones with a romfs folder)."),
                    Theme.Body, new Rectangle(S(20), top + S(162), p.Width - S(40), S(46)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            };
            p.Resize += delegate
            {
                int top = Math.Max(S(20), p.Height / 2 - S(150)) + S(222);
                file.Location = new Point(p.Width / 2 - file.Width - S(6), top);
                gb.Location = new Point(p.Width / 2 + S(6), top);
                p.Invalidate();
            };
            return p;
        }

        private Panel BuildDetails()
        {
            Panel outer = new Panel { Width = S(320), BackColor = Theme.Window, Padding = new Padding(S(10), S(12), S(12), S(4)) };
            Card card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Padding = new Padding(S(16)) };
            picture = new Panel { BackColor = Theme.Surface };
            picture.Paint += PaintDetailPicture;
            detailName.Font = Theme.Big;
            detailName.ForeColor = Theme.Text;
            detailName.BackColor = Theme.Surface;
            detailName.AutoEllipsis = true;
            detailMeta.Font = Theme.Small;
            detailMeta.ForeColor = Theme.Muted;
            detailMeta.BackColor = Theme.Surface;
            detailMeta.AutoEllipsis = true;
            detailStatus.Font = Theme.SmallBold;
            detailStatus.BackColor = Theme.Surface;
            detailStatus.AutoEllipsis = true;
            detailNotes.ReadOnly = true;
            detailNotes.BorderStyle = BorderStyle.None;
            detailNotes.BackColor = Theme.Surface;
            detailNotes.ForeColor = Theme.TextSoft;
            detailNotes.Font = Theme.Small;
            detailNotes.DetectUrls = true;
            detailNotes.ScrollBars = RichTextBoxScrollBars.Vertical;
            detailNotes.LinkClicked += (o, e) => Open(e.LinkText);
            detailNotes.HandleCreated += delegate { NativeMethods.SetWindowTheme(detailNotes.Handle, "DarkMode_Explorer", null); };

            detailFolder.Glyph = Theme.IconFolder; detailFolder.Text = ""; detailFolder.Kind = ButtonKind.Secondary;
            detailFolder.Click += delegate { OpenModFolder(modList.Selected); };
            tips.SetToolTip(detailFolder, L.T("Open its folder"));
            detailPage.Glyph = Theme.IconLink; detailPage.Text = ""; detailPage.Kind = ButtonKind.Secondary;
            detailPage.Click += delegate { if (modList.Selected != null && modList.Selected.Info.SourceUrl != null) Open(modList.Selected.Info.SourceUrl); };
            tips.SetToolTip(detailPage, L.T("Open its GameBanana page"));
            detailUpdate.Glyph = Theme.IconUpdate; detailUpdate.Text = L.T("Update"); detailUpdate.Kind = ButtonKind.Primary; detailUpdate.Compact = true;
            detailUpdate.Click += delegate { UpdateMod(modList.Selected); };
            tips.SetToolTip(detailUpdate, L.T("A newer file is on its GameBanana page: open it there to install it"));
            detailRemove.Glyph = Theme.IconDelete; detailRemove.Text = ""; detailRemove.Kind = ButtonKind.Danger;
            detailRemove.Click += delegate { RemoveMod(modList.Selected); };
            tips.SetToolTip(detailRemove, L.T("Uninstall it (from every profile)"));

            // with several mods selected: what can be done to all of them
            bulkRow.FlowDirection = FlowDirection.LeftToRight;
            bulkRow.WrapContents = true;
            bulkRow.BackColor = Theme.Surface;
            bulkRow.Visible = false;
            foreach (FlatButton b in new[] {
                Bulk(L.T("Turn on"), Theme.IconCheck, ButtonKind.Primary, delegate { BulkEnable(true); }),
                Bulk(L.T("Turn off"), Theme.IconClose, ButtonKind.Secondary, delegate { BulkEnable(false); }),
                Bulk(L.T("To the top"), "", ButtonKind.Secondary, delegate { BulkMove(true); }),
                Bulk(L.T("To the bottom"), "", ButtonKind.Secondary, delegate { BulkMove(false); }),
                Bulk(L.T("Uninstall"), Theme.IconDelete, ButtonKind.Danger, delegate { BulkRemove(); }) })
                bulkRow.Controls.Add(b);

            card.Controls.AddRange(new Control[] { picture, detailName, detailMeta, detailStatus, detailFolder, detailPage, detailUpdate, detailRemove, bulkRow, detailNotes });
            detailsCard = card;
            card.Resize += delegate { LayoutDetails(); };
            outer.Controls.Add(card);
            return outer;
        }

        private readonly FlowLayoutPanel bulkRow = new FlowLayoutPanel();
        private Card detailsCard;

        private FlatButton Bulk(string text, string glyph, ButtonKind kind, EventHandler click)
        {
            FlatButton b = new FlatButton { Text = text, Glyph = glyph, Kind = kind, Compact = true, Margin = new Padding(0, 0, S(6), S(6)) };
            b.Size = new Size(Math.Max(S(60), b.PreferredWidth(S(12))), S(32));
            b.Click += click;
            return b;
        }

        private void LayoutDetails()
        {
            Card card = detailsCard;
            if (card == null) return;
            int w = card.ClientSize.Width - 2 * S(16), x = S(16);
            picture.SetBounds(x, S(16), w, w * 9 / 16);
            detailName.SetBounds(x, picture.Bottom + S(12), w, S(26));
            detailMeta.SetBounds(x, detailName.Bottom, w, S(20));
            detailStatus.SetBounds(x, detailMeta.Bottom + S(2), w, S(20));
            int by = detailStatus.Bottom + S(8), bh = S(32);
            detailFolder.SetBounds(x, by, S(38), bh);
            detailPage.SetBounds(detailFolder.Right + S(6), by, S(38), bh);
            detailRemove.SetBounds(x + w - S(38), by, S(38), bh);
            detailUpdate.SetBounds(detailPage.Right + S(6), by, Math.Max(S(60), detailRemove.Left - S(6) - detailPage.Right - S(6)), bh);
            bulkRow.SetBounds(x, by, w + S(6), S(80));
            int notesTop = bulkRow.Visible ? bulkRow.Bottom + S(4) : by + bh + S(12);
            detailNotes.SetBounds(x, notesTop, w, Math.Max(S(40), card.ClientSize.Height - notesTop - S(16)));
        }

        private void PaintDetailPicture(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle r = new Rectangle(0, 0, picture.Width - 1, picture.Height - 1);
            InstalledMod m = modList.Selected;
            Image img = null;
            try
            {
                if (m != null && !string.IsNullOrEmpty(m.Info.Thumbnail))
                {
                    string path = Path.Combine(AppPaths.Thumbnails, m.Info.Thumbnail);
                    if (File.Exists(path)) using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path))) img = Image.FromStream(ms);
                }
            }
            catch (Exception) { img = null; }
            using (GraphicsPath clip = Theme.Rounded(r, S(8)))
            {
                g.SetClip(clip);
                if (img != null)
                {
                    float sc = Math.Max((float)r.Width / img.Width, (float)r.Height / img.Height);
                    g.DrawImage(img, r.X + (r.Width - img.Width * sc) / 2, r.Y + (r.Height - img.Height * sc) / 2, img.Width * sc, img.Height * sc);
                    img.Dispose();
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Sunken)) g.FillRectangle(b, r);
                    int e2 = r.Height - S(30);
                    Brand.Draw(g, new Rectangle(r.X + (r.Width - e2) / 2, r.Y + S(15), e2, e2), Theme.Border, Theme.Sunken);
                }
                g.ResetClip();
            }
        }

        // ------------------------------------------------------------------ list contents
        private void RefreshMods(InstalledMod select)
        {
            profileButton.Text = library.ProfileName;
            profileButton.Width = Math.Min(S(260), profileButton.PreferredWidth(S(16)) + S(14));
            if (profileButton.Parent != null) profileButton.Parent.Width = profileButton.Parent.Width;  // (lays the bar out again)
            Control bar = profileButton.Parent;
            if (bar != null) { bar.Width++; bar.Width--; }
            modList.SetMods(library.Mods, select);
            UpdateChips();
            bool none = library.Mods.Count == 0;
            emptyState.Visible = none;
            modList.Visible = !none;
            detailsPanel.Visible = !none;
            ShowDetails();
            UpdateButtons();
            MeasureSizesAsync();
            int updates = library.Mods.Count(m => m.Info.NewerFileDate != null);
            nav["mods"].Badge = updates > 0 ? updates.ToString() : null;
            nav["mods"].Invalidate();
        }

        // Each mod's size on disk, worked out once in the background (big mods take a moment to count)
        private void MeasureSizesAsync()
        {
            List<InstalledMod> todo = library.Mods.Where(m => !modSizes.ContainsKey(m.Folder)).ToList();
            if (todo.Count == 0) return;
            Task.Run(() =>
            {
                foreach (InstalledMod m in todo) modSizes[m.Folder] = FolderSize(m.Dir);
            }).ContinueWith(t => { if (IsHandleCreated) BeginInvoke((Action)(() => modList.Invalidate())); });
        }

        private string Subtitle(InstalledMod m)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(m.Info.Author)) parts.Add(m.Info.Author);
            if (!string.IsNullOrEmpty(m.Info.Version)) parts.Add(m.Info.Version);
            long size;
            if (modSizes.TryGetValue(m.Folder, out size)) parts.Add(SizeText(size));
            parts.Add(L.P(m.Info.UsableFiles, "{0} file", "{0} files"));
            if (!m.Enabled) parts.Add(L.T("off in this profile"));
            return string.Join("  ·  ", parts);
        }

        private List<Badge> BadgesFor(InstalledMod m)
        {
            List<Badge> b = new List<Badge>();
            if (m.Info.NewerFileDate != null) b.Add(new Badge(L.T("Update"), Theme.AccentText, Theme.Accent));
            switch (m.Info.Status)
            {
                case ModStatus.NeedsDlc: b.Add(new Badge(L.T("Needs DLC"), Theme.Bad, Theme.BadSoft)); break;
                case ModStatus.WiiU: b.Add(new Badge(L.T("Wii U mod"), Theme.Bad, Theme.BadSoft)); break;
                case ModStatus.Empty: b.Add(new Badge(L.T("Nothing to load"), Theme.Bad, Theme.BadSoft)); break;
            }
            if (m.Info.DlcFiles > 0 && m.Info.Status != ModStatus.NeedsDlc) b.Add(new Badge("DLC", Theme.Warn, Theme.WarnSoft));
            int lost = ConflictsOf(m).Count(c => c.Winner != m.DisplayName);
            if (m.Enabled && lost > 0) b.Add(new Badge(L.F("{0} overridden", lost), Theme.Warn, Theme.WarnSoft));
            return b;
        }

        // ------------------------------------------------------------------ details
        private void ShowDetails()
        {
            InstalledMod m = modList.Selected;
            picture.Invalidate();
            detailNotes.Clear();
            List<InstalledMod> sel = modList.SelectedMods;
            bool multi = sel.Count > 1;
            if (bulkRow.Visible != multi)
            {
                bulkRow.Visible = multi;
                foreach (Control c in new Control[] { detailFolder, detailPage, detailUpdate, detailRemove }) c.Visible = !multi;
                LayoutDetails();
            }
            if (multi)
            {
                detailName.Text = L.P(sel.Count, "{0} mod selected", "{0} mods selected");
                detailMeta.Text = L.F("{0} on, {1} off", sel.Count(x => x.Enabled), sel.Count(x => !x.Enabled));
                detailStatus.Text = L.T("Ctrl+click adds or removes one, Shift+click a range.");
                detailStatus.ForeColor = Theme.Muted;
                foreach (InstalledMod x in sel) Append((x.Enabled ? "●  " : "○  ") + x.DisplayName + "\n", x.Enabled ? Theme.TextSoft : Theme.Muted, Theme.Small);
                return;
            }
            bool has = m != null;
            foreach (Control c in new Control[] { detailFolder, detailRemove }) c.Enabled = has && !busy;
            detailPage.Visible = has && !string.IsNullOrEmpty(m.Info.SourceUrl);
            detailUpdate.Visible = has && m.Info.NewerFileDate != null;
            if (!has)
            {
                detailName.Text = library.Mods.Count == 0 ? L.T("No mods yet") : modList.Filtered ? L.T("No mod matches") : "";
                detailMeta.Text = detailStatus.Text = "";
                return;
            }
            ModInfo i = m.Info;
            detailName.Text = m.DisplayName;
            tips.SetToolTip(detailName, m.DisplayName);
            detailMeta.Text = string.Join("  ·  ", new[] { i.Author, string.IsNullOrEmpty(i.Version) ? null : L.F("version {0}", i.Version),
                string.IsNullOrEmpty(i.Imported) ? null : L.F("installed {0}", i.Imported) }.Where(x => !string.IsNullOrEmpty(x)));
            detailStatus.Text = (m.Enabled ? L.F("On in “{0}”", library.ProfileName) : L.F("Off in “{0}”", library.ProfileName)) + "  ·  " + StatusText(i.Status) +
                "  ·  " + L.F("place {0} of {1}", library.Mods.IndexOf(m) + 1, library.Mods.Count);
            detailStatus.ForeColor = i.Status == ModStatus.Ok ? Theme.Good : i.Status == ModStatus.Warning ? Theme.Warn : Theme.Bad;

            if (i.NewerFileDate != null) Append(L.F("A newer file is on its GameBanana page ({0}). Press Update to open it there.", i.NewerFileDate) + "\n\n", Theme.AccentSoftText, Theme.SmallBold);
            Append(L.P(i.UsableFiles, "{0} file changes the game ({1} replaced, {2} new).", "{0} files change the game ({1} replaced, {2} new).",
                    i.ReplacedFiles + i.DlcReplacedFiles, i.NewFiles + i.DlcNewFiles) +
                (i.DlcUsableFiles > 0 ? " " + L.P(i.DlcUsableFiles, "{0} of them is in the DLC.", "{0} of them are in the DLC.") : "") +
                (i.HasSizeTable ? " " + L.T("It has its own size table.") : "") + "\n", Theme.TextSoft, Theme.Small);
            foreach (string n in i.Notes)
            {
                string key = L.NoteKey(n);
                bool warn = key.StartsWith("Most of") || key.StartsWith("Its files are") || key.StartsWith("Nothing") || key.StartsWith("Made for");
                Append("• " + L.Note(n) + "\n", warn ? Theme.Warn : Theme.TextSoft, Theme.Small);
            }

            // what the last merge did with it, next to the other mods
            List<ConflictRecord> conflicts = ConflictsOf(m);
            List<MergedRecord> merged = lastBuild == null || lastBuild.Merged == null ? new List<MergedRecord>() : lastBuild.Merged.Where(r => r.Mods != null && r.Mods.Contains(m.DisplayName)).ToList();
            if (conflicts.Count > 0 || merged.Count > 0)
            {
                Append("\n" + L.T("With your other mods (last merge)") + "\n", Theme.Text, Theme.SmallBold);
                if (merged.Count > 0)
                    Append("• " + L.P(merged.Count, "{0} file shared with {1} is merged piece by piece: both mods' changes are in.", "{0} files shared with {1} are merged piece by piece: both mods' changes are in.",
                        string.Join(", ", merged.SelectMany(r => r.Mods).Where(x => x != m.DisplayName).Distinct())) + "\n", Theme.TextSoft, Theme.Small);
                foreach (IGrouping<string, ConflictRecord> g in conflicts.GroupBy(c => c.Winner))
                {
                    string others = string.Join(", ", g.SelectMany(c => c.Mods).Where(x => x != m.DisplayName).Distinct());
                    bool mine = g.Key == m.DisplayName;
                    Append("• " + (mine ? L.P(g.Count(), "{0} file also changed by {1}: this mod's is used.", "{0} files also changed by {1}: this mod's are used.", others)
                                        : L.P(g.Count(), "{0} file also changed by {1}: the one from {2} is used.", "{0} files also changed by {1}: the ones from {2} are used.", others, g.Key)) + "\n",
                        mine ? Theme.TextSoft : Theme.Warn, Theme.Small);
                }
                List<ConflictRecord> lostFiles = conflicts.Where(c => c.Winner != m.DisplayName).Take(6).ToList();
                foreach (ConflictRecord c in lostFiles) Append("    " + c.Path + "\n", Theme.Faint, Theme.Small);
                if (conflicts.Count(c => c.Winner != m.DisplayName) > lostFiles.Count) Append("    " + L.T("... (all in Tools > Merge report)") + "\n", Theme.Faint, Theme.Small);
            }
            if (!string.IsNullOrEmpty(i.SourceUrl)) Append("\n" + i.SourceUrl + "\n", Theme.Accent, Theme.Small);
            if (!string.IsNullOrEmpty(i.Description))
            {
                string d = i.Description.Trim();
                if (d.Length > 1500) d = d.Substring(0, 1500) + "…";
                Append("\n" + d + "\n", Theme.Muted, Theme.Small);
            }
            detailNotes.SelectionStart = 0;
            detailNotes.ScrollToCaret();
        }

        private List<ConflictRecord> ConflictsOf(InstalledMod m)
        {
            if (lastBuild == null || lastBuild.Conflicts == null) return new List<ConflictRecord>();
            return lastBuild.Conflicts.Where(c => c.Mods != null && c.Mods.Contains(m.DisplayName)).ToList();
        }

        private void LoadLastBuild()
        {
            BuildSummaryFile f = Json.Read<BuildSummaryFile>(AppPaths.BuildSummary);
            lastBuild = f != null && string.Equals(f.Profile, library.ProfileName, StringComparison.OrdinalIgnoreCase) ? f : null;
        }

        private void Append(string text, Color color, Font font)
        {
            detailNotes.SelectionStart = detailNotes.TextLength;
            detailNotes.SelectionLength = 0;
            detailNotes.SelectionColor = color;
            detailNotes.SelectionFont = font;
            detailNotes.AppendText(text);
        }

        private static string StatusText(string s)
        {
            switch (s)
            {
                case ModStatus.Ok: return L.T("ready");
                case ModStatus.Warning: return L.T("ready, see notes");
                case ModStatus.NeedsDlc: return L.T("needs the DLC");
                case ModStatus.WiiU: return L.T("Wii U mod");
                case ModStatus.Empty: return L.T("nothing to load");
                default: return "?";
            }
        }

        private void UpdateButtons()
        {
            bool ready = sw != null && !busy;
            addButton.Enabled = !busy;
            play.Enabled = playVanilla.Enabled = merge.Enabled = ready;
            profileButton.Enabled = !busy;
            modList.Enabled = !busy;
            int on = library.Mods.Count(x => x.Enabled);
            play.Text = on == 0 ? L.T("Play") : L.F("Play ({0})", on);
            tips.SetToolTip(play, on == 0 ? L.T("No mods are on: starts the game as it is (F5)") : L.P(on, "Starts Savage Wind with the {0} mod that is on (F5)", "Starts Savage Wind with the {0} mods that are on (F5)"));
            detailFolder.Enabled = detailRemove.Enabled = modList.Selected != null && !busy;
        }

        private void ToggleMod(InstalledMod m)
        {
            if (m == null || busy) return;
            // one of several selected: all of them, to the switch's new state
            if (modList.SelectedMods.Count > 1 && modList.SelectedMods.Contains(m)) { BulkEnable(!m.Enabled); return; }
            bool on = !m.Enabled;
            if (on && (m.Info.Status == ModStatus.NeedsDlc || m.Info.Status == ModStatus.WiiU || m.Info.Status == ModStatus.Empty))
            {
                string why = m.Info.Notes.Count > 0 ? L.Note(m.Info.Notes[0]) : L.T("This mod probably won't work with Savage Wind.");
                if (MessageBox.Show(this, why + "\n\n" + L.T("Turn it on anyway?"), m.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            m.Enabled = on;
            library.Save();
            modList.Invalidate();
            ShowDetails();
            UpdateButtons();
            SetStatus((on ? L.F("{0} is on.", m.DisplayName) : L.F("{0} is off.", m.DisplayName)) + " " + L.T("Press Play to use it (it's merged then)."), Theme.Muted);
        }

        private void MoveSelected(int delta)
        {
            InstalledMod m = modList.Selected;
            if (m == null || busy) return;
            library.Move(m, delta);
            RefreshMods(m);
        }

        // ------------------------------------------------------------------ profiles (the profile button's menu)
        private void FillProfileMenu()
        {
            profileMenu.Items.Clear();
            foreach (string name in library.ProfileNames)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(name, null, delegate { SwitchProfile(name); });
                item.Checked = string.Equals(name, library.ProfileName, StringComparison.OrdinalIgnoreCase);
                profileMenu.Items.Add(item);
            }
            profileMenu.Items.Add(new ToolStripSeparator());
            profileMenu.Items.Add(L.T("New profile..."), null, delegate { NewProfile(false); });
            profileMenu.Items.Add(L.T("Duplicate this profile..."), null, delegate { NewProfile(true); });
            profileMenu.Items.Add(L.T("Manage profiles..."), null, delegate { ShowPage("profiles"); });
        }

        private void SwitchProfile(string name)
        {
            if (busy || string.Equals(name, library.ProfileName, StringComparison.OrdinalIgnoreCase)) return;
            library.SwitchProfile(name);
            LoadLastBuild();
            RefreshMods(null);
            int on = library.Mods.Count(m => m.Enabled);
            SetStatus(L.P(on, "Profile “{1}”: {0} mod on.", "Profile “{1}”: {0} mods on.", library.ProfileName), Theme.Muted);
            if (currentPage == "profiles") RefreshProfilesPage();
        }

        private string AskProfileName(string title, string initial, bool renaming)
        {
            for (;;)
            {
                string name = TextPrompt.Ask(this, title, L.T("Name of the profile:"), initial);
                if (name == null) return null;
                if (!library.ProfileExists(name) || renaming && string.Equals(name, initial, StringComparison.OrdinalIgnoreCase)) return name;
                MessageBox.Show(this, L.F("There's already a profile called “{0}”.", name), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                initial = name;
            }
        }

        private void NewProfile(bool copy)
        {
            string name = AskProfileName(copy ? L.T("Duplicate the profile") : L.T("New profile"), copy ? L.F("{0} (copy)", library.ProfileName) : "", false);
            if (name == null) return;
            library.CreateProfile(name, copy);
            LoadLastBuild();
            RefreshMods(null);
            if (currentPage == "profiles") RefreshProfilesPage();
            SetStatus(copy ? L.F("Made the profile “{0}”, a copy of the last one.", name) : L.F("Made the profile “{0}”: turn mods on for it.", name), Theme.Good);
        }

        // ------------------------------------------------------------------ adding mods
        private void AddFromFileDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("Add a mod");
                d.Filter = L.T("Mod downloads (*.zip;*.7z;*.rar;*.bnp)|*.zip;*.7z;*.rar;*.bnp|All files (*.*)|*.*");
                d.Multiselect = true;
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
                if (d.ShowDialog(this) == DialogResult.OK) AddModsAsync(d.FileNames, null);
            }
        }

        private void AddFromFolderDialog()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("Choose the mod's folder (the one with romfs in it, or above it)");
                if (d.ShowDialog(this) == DialogResult.OK) AddModsAsync(new[] { d.SelectedPath }, null);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            if (paths.Length == 1 && paths[0].EndsWith(ProfileShare.Extension, StringComparison.OrdinalIgnoreCase)) { BeginInvoke((Action)(() => ImportProfile(paths[0]))); return; }
            BeginInvoke((Action)(() => { ShowPage("mods"); AddModsAsync(paths, null); }));
        }

        // A mod to install in a batch: a download or a folder, where it came from, and its DLC files when they're apart
        private sealed class InstallJob
        {
            public string Path, DlcFolder;
            public ModOrigin Origin;
        }

        // Files and folders dropped or picked: a folder holding several mods (a folder of downloads) is each of them
        private void AddModsAsync(string[] paths, ModOrigin origin)
        {
            List<InstallJob> jobs = new List<InstallJob>();
            foreach (string p in paths)
            {
                List<FoundMod> inside = Directory.Exists(p) ? ModSources.InFolder(p) : null;
                if (inside != null && inside.Count > 1) jobs.AddRange(inside.Select(f => new InstallJob { Path = f.Path, DlcFolder = f.DlcFolder }));
                else jobs.Add(new InstallJob { Path = p, Origin = origin });
            }
            InstallBatch(jobs);
        }

        // "Import mods": from emulators, a Switch SD card, a folder of downloads
        private void ImportMods()
        {
            if (busy) return;
            using (ImportDialog d = new ImportDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Chosen.Count == 0) return;
                InstallBatch(d.Chosen.Select(f => new InstallJob { Path = f.Path, DlcFolder = f.DlcFolder }).ToList());
            }
        }

        // Installs one mod or many, one after the other: a mod that fails doesn't stop the rest; mods already installed
        // are asked about (once for all, when the player says so); one summary at the end
        private async void InstallBatch(List<InstallJob> jobs)
        {
            if (busy || jobs.Count == 0 || !await EnsureReady()) return;
            List<InstalledMod> added = new List<InstalledMod>();
            List<string> failed = new List<string>(), leftOff = new List<string>(), skipped = new List<string>();
            DuplicateChoice? always = null;
            for (int k = 0; k < jobs.Count; k++)
            {
                InstallJob job = jobs[k];
                string name = Path.GetFileName(job.Path.TrimEnd('\\'));
                string prefix = jobs.Count > 1 ? L.F("{0} of {1}: ", k + 1, jobs.Count) : "";
                SetBusy(true, prefix + L.F("Adding {0}...", name));
                List<InstalledMod> mods;
                try
                {
                    Func<List<string>, List<string>> choose = options => (List<string>)Invoke(new Func<List<string>>(() => ChooseVersions(name, options)));
                    Action<string> report = t => Progress(prefix + t);
                    VanillaIndex v = van, d = dlcVan;
                    mods = await Task.Run(() => ModImporter.Import(job.Path, library, v, d, choose, report, job.Origin, job.DlcFolder));
                }
                catch (Exception ex)
                {
                    failed.Add(name + ": " + ex.Message);
                    continue;
                }
                foreach (InstalledMod m in mods)
                {
                    InstalledMod old = library.FindByName(m.DisplayName);
                    DuplicateChoice c = DuplicateChoice.KeepBoth;
                    if (old != null)
                    {
                        if (always.HasValue) c = always.Value;
                        else
                        {
                            bool all;
                            c = DuplicateDialog.Ask(this, m.DisplayName, jobs.Count > 1 || mods.Count > 1, out all);
                            if (all) always = c;
                        }
                    }
                    if (old != null && c == DuplicateChoice.Skip)
                    {
                        try { AppPaths.DeleteFolderInside(m.Dir, AppPaths.Mods); }
                        catch (Exception) { }
                        skipped.Add(m.DisplayName);
                        continue;
                    }
                    if (old != null && c == DuplicateChoice.Replace)
                    {
                        modList.ForgetThumbnail(old);
                        library.Replace(old, m);
                        long ignored;
                        modSizes.TryRemove(old.Folder, out ignored);
                    }
                    else library.Add(m);
                    added.Add(m);
                    if (!m.Enabled) leftOff.Add(m.DisplayName + ": " + (m.Info.Notes.Count > 0 ? L.Note(m.Info.Notes[0]) : StatusText(m.Info.Status)));
                }
                RefreshMods(added.LastOrDefault() ?? modList.Selected);
            }
            SetBusy(false, null);
            ShowPage("mods");
            RefreshMods(added.LastOrDefault() ?? modList.Selected);
            if (browsePanel != null) browsePanel.RefreshInstalled();

            string done = added.Count == 0 ? L.T("Nothing was installed.") : L.P(added.Count, "Installed {0} mod.", "Installed {0} mods.");
            if (added.Count > 0 && added.Any(m => m.Enabled)) done += " " + L.T("Press Play to use them.");
            SetStatus(done + (failed.Count > 0 ? " " + L.F("{0} couldn't be installed.", failed.Count) : ""),
                failed.Count > 0 ? Theme.Warn : added.Count > 0 ? Theme.Good : Theme.Muted);
            if (failed.Count > 0 || leftOff.Count > 0 || skipped.Count > 0 && jobs.Count > 1)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(done);
                if (failed.Count > 0) sb.Append("\n\n" + L.T("Couldn't be installed:") + "\n• " + string.Join("\n• ", failed.Take(15)) + (failed.Count > 15 ? "\n• ..." : ""));
                if (leftOff.Count > 0) sb.Append("\n\n" + L.T("Installed, but left off (they probably won't work with Savage Wind):") + "\n• " + string.Join("\n• ", leftOff.Take(15)));
                if (skipped.Count > 0) sb.Append("\n\n" + L.T("Skipped (already installed):") + "\n• " + string.Join("\n• ", skipped.Take(15)));
                MessageBox.Show(this, sb.ToString(), L.T("Installing mods"), MessageBoxButtons.OK, failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        // A download with several versions of the game files: which to install (each becomes a mod)
        private List<string> ChooseVersions(string name, List<string> options)
        {
            List<DarkList.Row> rows = options.Select(o => new DarkList.Row { Title = o }).ToList();
            List<int> picked = CheckDialog.Ask(this, L.T("Choose what to install"),
                L.F("{0} has more than one version of the game files in it. Tick the ones to install: each becomes a mod of its own (most of the time you want one).", name), rows, L.T("Install"));
            return picked == null ? null : picked.Select(i => options[i]).ToList();
        }

        // An update from GameBanana: its page opened on the files (installing it is the player's choice there)
        private void UpdateMod(InstalledMod m)
        {
            if (m == null || string.IsNullOrEmpty(m.Info.SourceUrl)) return;
            ShowPage("browse");
            browsePanel.LookUpLink(m.Info.SourceUrl);
            SetStatus(L.F("Pick the newer file of {0} and install it: Guardian Terminal asks to replace the old one.", m.DisplayName), Theme.Muted);
        }

        // ------------------------------------------------------------------ mod actions
        private void RemoveMod(InstalledMod m)
        {
            if (m == null || busy) return;
            if (MessageBox.Show(this, L.F("Uninstall {0}?", m.DisplayName) + "\n\n" + L.T("It's deleted from Guardian Terminal and taken out of every profile. Your original download isn't touched."),
                    L.T("Uninstall mod"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            modList.ForgetThumbnail(m);
            try { library.Remove(m); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("Couldn't delete all of its files ({0}). Close Savage Wind if it's running and try again.", ex.Message), L.T("Uninstall mod"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshMods(null);
            SetStatus(L.F("Uninstalled {0}.", m.DisplayName), Theme.Muted);
        }

        private void OpenModFolder(InstalledMod m)
        {
            if (m != null && Directory.Exists(m.Dir)) OpenFolder(m.Dir);
        }

        // ------------------------------------------------------------------ merging
        private async Task<BuildResult> BuildAsync(bool force)
        {
            List<InstalledMod> enabled = library.EnabledInOrder();
            Action<string> p = Progress;
            VanillaIndex v = van, d = dlcVan;
            SavageWindInstall install = sw;
            BuildResult r = await Task.Run(() => OverlayBuilder.Build(enabled, install, v, d, library.Config, force, p));
            if (!r.UpToDate) LoadLastBuild();
            return r;
        }

        // "Merge" (TKMM's Apply Mods): the mods that are on, merged into the files Savage Wind loads, now
        private async void MergeAsync()
        {
            if (busy || !await EnsureReady()) return;
            if (game != null && !game.HasExited || sw.IsRunning())
            {
                MessageBox.Show(this, L.T("Close Savage Wind first: it's using the merged files."), L.T("Merge"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetBusy(true, L.T("Merging..."));
            BuildResult r;
            try { r = await BuildAsync(true); }
            catch (Exception ex)
            {
                SetBusy(false, L.T("Merge failed."), Theme.Bad);
                MessageBox.Show(this, L.F("Merging the profile “{0}” failed:", library.ProfileName) + "\n\n" + ex.Message, L.T("Merge failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            RefreshMods(modList.Selected);
            if (library.EnabledInOrder().Count == 0)
            {
                SetBusy(false, L.T("No mods are on, so the merge output is empty: Play starts the game as it is."), Theme.Muted);
                return;
            }
            SetBusy(false, L.F("Merged: {0}.", r.Summary), Theme.Good);
            string extra = (r.Report.Conflicts.Count > 0 ? "\n\n" + L.P(r.Report.Conflicts.Count,
                    "{0} file is changed by more than one mod in a way that can't be merged: the one lower in the list is used (each mod's details show which).",
                    "{0} files are changed by more than one mod in a way that can't be merged: the one lower in the list is used (each mod's details show which).") : "") +
                (r.Report.Warnings.Count > 0 ? "\n\n" + L.T("Warnings:") + "\n• " + string.Join("\n• ", r.Report.Warnings.Take(4)) : "");
            if (MessageBox.Show(this, L.F("The profile “{0}” is merged.", library.ProfileName) + "\n\n" + r.Summary + "." + extra + "\n\n" + L.T("Open the merge report?"),
                    L.T("Merge done"), MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                OpenFile(AppPaths.BuildReport, "");
        }

        // "Empty merge output": the merged files deleted (the next Play or Merge builds them again)
        private void EmptyMergeOutput()
        {
            if (busy) return;
            if (game != null && !game.HasExited || sw != null && sw.IsRunning())
            {
                MessageBox.Show(this, L.T("Close Savage Wind first: it's using the merged files."), L.T("Empty merge output"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                OverlayBuilder.ClearOverlay();
                library.Config.LastBuildSignature = null;
                library.Config.LastBuildSummary = null;
                library.Config.Save();
                SetStatus(L.T("Merge output emptied. The next Merge or Play builds it again."), Theme.Good);
            }
            catch (Exception ex)
            {
                SetStatus(L.F("Couldn't empty the merge output: {0}", ex.Message), Theme.Bad);
            }
        }

        // "Export to SD card": the merge made for a modded Switch, onto its SD card
        private async void ExportToSdCard()
        {
            if (busy || !await EnsureReady()) return;
            if (library.EnabledInOrder().Count == 0)
            {
                MessageBox.Show(this, L.T("No mods are on in this profile: nothing to export."), L.T("Export to SD card"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<string> cards = SdExport.FindCards();
            string sd;
            if (cards.Count == 1) sd = cards[0];
            else if (cards.Count > 1) sd = ChoiceDialog.Ask(this, L.T("More than one drive has an atmosphere folder. Which one is your Switch's SD card?"), cards);
            else
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = L.T("Choose your Switch's SD card (plug it in with hekate's USB mode or a card reader). No drive with an atmosphere folder was found.");
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    sd = Path.GetPathRoot(d.SelectedPath);
                }
            }
            if (sd == null) return;
            List<string> existing = SdExport.ExistingMods(sd);
            string msg = L.F("Export the profile “{0}” to {1} for your modded Switch?", library.ProfileName, sd) + "\n\n" +
                L.F("It goes to {0}.", "atmosphere\\contents\\01007EF00011E000\\romfs" + (dlcVan != null ? " (+ ...F001\\romfs, DLC)" : "")) + "\n" +
                (existing.Count > 0 ? L.T("The Breath of the Wild mods already there are deleted first:") + "\n   " + string.Join("\n   ", existing) + "\n" : "") +
                "\n" + L.F("The merge is made for your game files ({0}): the Switch needs the same versions.", sw.GameVersion + (dlcVan != null ? ", DLC " + sw.DlcVersion : ""));
            if (MessageBox.Show(this, msg, L.T("Export to SD card"), MessageBoxButtons.YesNo, existing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            SetBusy(true, L.T("Exporting to the SD card..."));
            BuildResult r;
            try
            {
                List<InstalledMod> enabled = library.EnabledInOrder();
                Action<string> p = Progress;
                VanillaIndex v = van, d = dlcVan;
                SavageWindInstall install = sw;
                r = await Task.Run(() => SdExport.Export(sd, enabled, install, v, d, p));
            }
            catch (Exception ex)
            {
                SetBusy(false, L.T("Export failed."), Theme.Bad);
                MessageBox.Show(this, L.T("Exporting to the SD card failed:") + "\n\n" + ex.Message, L.T("Export to SD card"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SetBusy(false, L.F("Exported to {0}: {1}.", sd, r.Summary), Theme.Good);
            MessageBox.Show(this, L.F("Exported to {0}: {1}.", sd, r.Summary) + "\n\n" + L.T("Safely eject the card before you put it back in the Switch."), L.T("Export to SD card"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ playing
        private async void PlayAsync(bool withMods)
        {
            if (busy || !await EnsureReady()) return;
            if (game != null && !game.HasExited || sw.IsRunning())
            {
                MessageBox.Show(this, L.T("Savage Wind is already running. Close it first."), "Savage Wind", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string overlay = null, summary = null;
            List<InstalledMod> enabled = library.EnabledInOrder();
            bool mods = withMods && enabled.Count > 0;
            if (mods)
            {
                SetBusy(true, L.T("Getting your mods ready..."));
                try
                {
                    BuildResult r = await BuildAsync(false);
                    overlay = r.OverlayDir;
                    summary = r.Summary;
                }
                catch (Exception ex)
                {
                    SetBusy(false, L.T("Couldn't get the mods ready."), Theme.Bad);
                    MessageBox.Show(this, L.T("Merging the mods failed, so the game wasn't started:") + "\n\n" + ex.Message, "Guardian Terminal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                SetBusy(false, null);
                RefreshMods(modList.Selected);
            }

            // a copy of the saves before a game with a different set of mods (some mods change a save for good)
            string set = mods ? string.Join("|", enabled.Select(m => m.Folder)) : "(no mods)";
            if (library.Config.BackUpSaves && set != library.Config.LastSaveBackupFor && SaveBackups.HasSaves(sw))
            {
                SetBusy(true, L.T("Backing up your saves first..."));
                try
                {
                    SavageWindInstall install = sw;
                    string why = "before playing " + (mods ? "“" + library.ProfileName + "” (" + enabled.Count + " mod" + (enabled.Count == 1 ? "" : "s") + ")" : "unmodded");
                    await Task.Run(() => SaveBackups.Backup(install, why));
                    library.Config.LastSaveBackupFor = set;
                    library.Config.Save();
                }
                catch (Exception ex)
                {
                    SetBusy(false, null);
                    if (MessageBox.Show(this, L.F("Your saves couldn't be backed up ({0}). Play anyway?", ex.Message), L.T("Save backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                }
                SetBusy(false, null);
            }

            try
            {
                game = sw.Launch(overlay);
                game.EnableRaisingEvents = true;
                game.Exited += OnGameExited;
            }
            catch (Exception ex)
            {
                SetStatus(L.F("Couldn't start Savage Wind: {0}", ex.Message), Theme.Bad);
                MessageBox.Show(this, L.T("Savage Wind couldn't be started:") + "\n\n" + ex.Message, "Savage Wind", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SetStatus(overlay == null ? L.T("Savage Wind is starting without mods...") : L.F("Savage Wind is starting with mods ({0})...", summary), Theme.Muted);
            LaunchScreen screen = new LaunchScreen(sw, game, overlay != null, summary);
            screen.Finished += ok =>
            {
                if (ok) { SetStatus(overlay == null ? L.T("Savage Wind is running without mods.") : L.F("Savage Wind is running with mods ({0}).", summary), Theme.Good); return; }
                if (screen.Error != null) SetStatus(L.T("Savage Wind couldn't start the game.") + " " + screen.Error, Theme.Bad);
                else if (screen.Stage == LaunchStage.SetupScreen) SetStatus(L.T("Savage Wind opened its own setup screen: finish it there, then press Play again."), Theme.Warn);
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            };
            screen.Show();
            WindowState = FormWindowState.Minimized;
        }

        private void OnGameExited(object sender, EventArgs e)
        {
            int code;
            try { code = game.ExitCode; }
            catch (Exception) { code = 0; }
            List<string> lines = sw.OverlayLogLines();
            BeginInvoke((Action)(() =>
            {
                if (code != 0)
                    SetStatus(L.F("Savage Wind closed with an error ({0}). If it happens with mods on, turn them off one at a time to find the one causing it.", LaunchWatcher.ExitCodeText(code)), Theme.Bad);
                else if (lines.Count > 0)
                    SetStatus(L.F("Last game: Savage Wind loaded the mods ({0}).", string.Join("; ", lines.Select(l => l.Replace("[fs] overlay: ", "")))), Theme.Good);
                else
                    SetStatus(L.T("Savage Wind closed."), Theme.Muted);
            }));
        }
    }
}
