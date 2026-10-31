using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using GuardianTerminal.Game;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    // The other pages: profiles, GameBanana, saves, tools, settings, about.
    internal sealed partial class MainForm
    {
        // ------------------------------------------------------------------ helpers for the pages
        // A row of buttons under a page's header
        private FlowLayoutPanel ButtonRow(params Control[] buttons)
        {
            FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(56), BackColor = Theme.Window, WrapContents = false, Padding = new Padding(S(26), S(4), S(26), S(10)) };
            foreach (Control b in buttons)
            {
                FlatButton fb = b as FlatButton;
                if (fb != null) fb.Size = new Size(Math.Max(S(44), fb.PreferredWidth(S(14))), S(38));
                b.Margin = new Padding(0, 0, S(8), 0);
                row.Controls.Add(b);
            }
            return row;
        }

        private FlatButton Button(string text, string glyph, ButtonKind kind, EventHandler click)
        {
            FlatButton b = new FlatButton { Text = text, Glyph = glyph, Kind = kind };
            if (kind == ButtonKind.Primary) b.Compact = true;
            b.Click += click;
            return b;
        }

        // A settings / tools section: a title, a line of text, and controls on the right
        private Card Section(string title, string text, out Panel actions)
        {
            Card c = new Card { Fill = Theme.Surface, Height = S(96), Margin = new Padding(0, 0, 0, S(12)) };
            Label t = new Label { Text = title, Font = Theme.BodyBold, ForeColor = Theme.Text, BackColor = Theme.Surface, AutoSize = false, AutoEllipsis = true };
            Label d = new Label { Text = text, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Surface, AutoSize = false };
            Panel a = new Panel { BackColor = Theme.Surface };
            c.Controls.AddRange(new Control[] { t, d, a });
            d.Visible = !string.IsNullOrEmpty(text);  // (an empty one would cover what a section puts there itself)
            c.Resize += delegate
            {
                int aw = 0;
                foreach (Control x in a.Controls) aw = Math.Max(aw, x.Right);
                a.SetBounds(c.Width - aw - S(18), (c.Height - S(40)) / 2, aw, S(40));
                t.SetBounds(S(18), S(14), a.Left - S(32), S(24));
                d.SetBounds(S(18), S(40), a.Left - S(32), c.Height - S(48));
            };
            actions = a;
            return c;
        }

        // Puts buttons in a section's actions, right to left as given left to right
        private void Actions(Panel a, params Control[] items)
        {
            int x = 0;
            foreach (Control b in items)
            {
                FlatButton fb = b as FlatButton;
                int w = fb != null ? Math.Max(S(44), fb.PreferredWidth(S(14))) : b.Width;
                int h = fb != null ? S(36) : b.Height;
                b.SetBounds(x, (S(40) - h) / 2, w, h);
                a.Controls.Add(b);
                x += w + S(8);
            }
        }

        // A scrolling column of sections
        private Panel SectionColumn()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, AutoScroll = true, Padding = new Padding(S(26), S(4), S(26), S(20)) };
            p.HandleCreated += delegate { NativeMethods.SetWindowTheme(p.Handle, "DarkMode_Explorer", null); };  // (a dark scrollbar)
            return p;
        }

        private void Stack(Panel column, params Control[] items)
        {
            column.Resize += delegate { StackLayout(column, items); };
            column.Controls.AddRange(items);
            StackLayout(column, items);
        }

        private void StackLayout(Panel column, Control[] items)
        {
            int y = column.Padding.Top - column.VerticalScroll.Value, w = Math.Max(S(300), column.ClientSize.Width - column.Padding.Horizontal);
            foreach (Control c in items)
            {
                if (!c.Visible) continue;
                c.SetBounds(column.Padding.Left, y, w, c.Height);
                y += c.Height + S(12);
            }
        }

        // ------------------------------------------------------------------ profiles
        private readonly DarkList profileList = new DarkList();

        private Control BuildProfilesPage()
        {
            Panel page = new Panel { BackColor = Theme.Window };
            Panel header = PageHeader(L.T("Profiles"), L.T("Separate sets of mods: each profile has its own mods on and off, and its own load order. The mods themselves are shared."));
            FlowLayoutPanel buttons = ButtonRow(
                Button(L.T("Switch to it"), Theme.IconCheck, ButtonKind.Primary, delegate { if (profileList.SelectedItem != null) SwitchProfile(((DarkList.Row)profileList.SelectedItem).Tag as string); }),
                Button(L.T("New"), Theme.IconAdd, ButtonKind.Secondary, delegate { NewProfile(false); }),
                Button(L.T("Duplicate"), "\uE8C8", ButtonKind.Secondary, delegate { NewProfile(true); }),
                Button(L.T("Rename"), "\uE70F", ButtonKind.Secondary, delegate { RenameProfile(); }),
                Button(L.T("Delete"), Theme.IconDelete, ButtonKind.Danger, delegate { DeleteProfile(); }),
                Button(L.T("Import..."), Theme.IconOpen, ButtonKind.Ghost, delegate { ImportProfileDialog(); }),
                Button(L.T("Share (export)..."), Theme.IconShare, ButtonKind.Ghost, delegate { ExportProfile(); }));
            Panel listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(S(26), 0, S(26), S(18)) };
            profileList.Dock = DockStyle.Fill;
            profileList.DoubleClick += delegate { if (profileList.SelectedItem != null) SwitchProfile(((DarkList.Row)profileList.SelectedItem).Tag as string); };
            listHost.Controls.Add(profileList);
            page.Controls.Add(listHost);
            page.Controls.Add(buttons);
            page.Controls.Add(header);
            return page;
        }

        private void RefreshProfilesPage()
        {
            Dictionary<string, string> names = library.Mods.ToDictionary(m => m.Folder, m => m.DisplayName, StringComparer.OrdinalIgnoreCase);
            profileList.Items.Clear();
            foreach (Profile p in library.Config.Profiles)
            {
                List<string> on = p.Mods.Where(e => e.Enabled && names.ContainsKey(e.Folder)).Select(e => names[e.Folder]).ToList();
                bool current = string.Equals(p.Name, library.ProfileName, StringComparison.OrdinalIgnoreCase);
                profileList.Items.Add(new DarkList.Row
                {
                    Title = p.Name,
                    Detail = on.Count == 0 ? L.T("No mods on") : L.P(on.Count, "{0} mod on: {1}", "{0} mods on: {1}", string.Join(", ", on)),
                    Badge = current ? L.T("current") : null,
                    Tag = p.Name
                });
                if (current) profileList.SelectedIndex = profileList.Items.Count - 1;
            }
        }

        private void RenameProfile()
        {
            string old = library.ProfileName;
            string name = AskProfileName(L.T("Rename the profile"), old, true);
            if (name == null || name == old) return;
            library.RenameProfile(name);
            RefreshMods(modList.Selected);
            RefreshProfilesPage();
            SetStatus(L.F("Renamed “{0}” to “{1}”.", old, name), Theme.Muted);
        }

        private void DeleteProfile()
        {
            string name = library.ProfileName;
            if (library.ProfileNames.Count <= 1)
            {
                MessageBox.Show(this, L.T("At least one profile must always exist."), L.T("Delete profile"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, L.F("Delete the profile “{0}” (the current one)?", name) + "\n\n" + L.T("Only the profile goes (which mods are on, and their order): the mods stay installed."),
                    L.T("Delete profile"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            library.DeleteProfile();
            LoadLastBuild();
            RefreshMods(null);
            RefreshProfilesPage();
            SetStatus(L.F("Deleted the profile “{0}”.", name), Theme.Muted);
        }

        private void ExportProfile()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = L.F("Share the profile “{0}”", library.ProfileName);
                d.Filter = L.T("Guardian Terminal profile") + " (*" + ProfileShare.Extension + ")|*" + ProfileShare.Extension;
                d.FileName = string.Concat(library.ProfileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ProfileShare.Extension;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ProfileShare.Export(library, d.FileName);
                    SetStatus(L.F("Saved the profile as {0}. It lists the mods and where to get them (no mod files are in it).", Path.GetFileName(d.FileName)), Theme.Good);
                }
                catch (Exception ex) { SetStatus(L.F("Couldn't save the profile: {0}", ex.Message), Theme.Bad); }
            }
        }

        private void ImportProfileDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("Import a profile");
                d.Filter = L.T("Guardian Terminal profile") + " (*" + ProfileShare.Extension + ")|*" + ProfileShare.Extension + "|" + L.T("All files") + " (*.*)|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) ImportProfile(d.FileName);
            }
        }

        private void ImportProfile(string path)
        {
            if (busy) return;
            ProfileShare.ImportResult r;
            try { r = ProfileShare.Import(path, library); }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("Couldn't import the profile: {0}", ex.Message), L.T("Import a profile"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            LoadLastBuild();
            RefreshMods(null);
            if (currentPage == "profiles") RefreshProfilesPage();
            SetStatus(L.F("Imported the profile “{0}”: {1} of its mods are installed here.", r.ProfileName, r.Matched), Theme.Good);
            if (r.Missing.Count > 0)
            {
                List<SharedMod> withLinks = r.Missing.Where(m => !string.IsNullOrEmpty(m.Url)).ToList();
                string list = string.Join("\n", r.Missing.Select(m => "• " + m.Name + (string.IsNullOrEmpty(m.Version) ? "" : " (" + m.Version + ")") + (string.IsNullOrEmpty(m.Url) ? "" : "  " + m.Url)));
                if (MessageBox.Show(this, L.F("The profile “{0}” uses mods that aren't installed here:", r.ProfileName) + "\n\n" + list +
                        "\n\n" + L.T("Install them (GameBanana page, Add mod), then import the profile again to put them in its order.") +
                        (withLinks.Count > 0 ? "\n\n" + L.T("Open their GameBanana pages now?") : ""), L.T("Import a profile"),
                        withLinks.Count > 0 ? MessageBoxButtons.YesNo : MessageBoxButtons.OK, MessageBoxIcon.Information) == DialogResult.Yes)
                    foreach (SharedMod m in withLinks) Open(m.Url);
            }
        }

        // ------------------------------------------------------------------ GameBanana
        private GameBananaPanel browsePanel;

        private Control BuildBrowsePage()
        {
            browsePanel = new GameBananaPanel();
            browsePanel.CanInstall = () => !busy;
            browsePanel.IsInstalled = id => library.Mods.Any(m => GameBanana.IdOf(m.Info.SourceUrl) == id);
            browsePanel.ShowMature = () => library.Config.ShowMatureMods;
            browsePanel.Downloaded += (file, origin) => InstallBatch(new List<InstallJob> { new InstallJob { Path = file, Origin = origin } });
            return browsePanel;
        }

        // ------------------------------------------------------------------ saves
        private readonly DarkList backupList = new DarkList();
        private readonly ToggleSwitch autoBackup = new ToggleSwitch();
        private Label savesWhere;

        private Control BuildSavesPage()
        {
            Panel page = new Panel { BackColor = Theme.Window };
            Panel header = PageHeader(L.T("Saves"), L.T("Copies of your Savage Wind saves, so a mod (or a randomizer) can never cost you your progress. The newest 15 are kept."));
            FlowLayoutPanel buttons = ButtonRow(
                Button(L.T("Back up now"), Theme.IconSaves, ButtonKind.Primary, delegate { BackUpSavesNow(); }),
                Button(L.T("Restore"), Theme.IconHistory, ButtonKind.Secondary, delegate { RestoreBackup(); }),
                Button(L.T("Delete"), Theme.IconDelete, ButtonKind.Danger, delegate { DeleteBackup(); }),
                Button(L.T("Backups folder"), Theme.IconFolder, ButtonKind.Ghost, delegate { OpenFolder(AppPaths.SaveBackups); }),
                Button(L.T("Savage Wind's save folder"), Theme.IconFolder, ButtonKind.Ghost, delegate { if (sw != null) OpenFolder(SaveBackups.SaveFolder(sw), L.T("Savage Wind has no save folder yet.")); }));
            Panel auto = new Panel { Dock = DockStyle.Top, Height = S(48), BackColor = Theme.Window };
            autoBackup.SetBounds(S(30), S(12), S(40), S(22));
            autoBackup.Show(library.Config.BackUpSaves);
            autoBackup.CheckedChanged += delegate { library.Config.BackUpSaves = autoBackup.Checked; library.Config.Save(); };
            Label autoText = new Label { Text = L.T("Back up my saves by themselves before I play with a different set of mods"), ForeColor = Theme.Text, BackColor = Theme.Window, AutoSize = true, Location = new Point(S(80), S(14)) };
            autoText.Click += delegate { autoBackup.Checked = !autoBackup.Checked; };
            savesWhere = new Label { ForeColor = Theme.Muted, BackColor = Theme.Window, AutoSize = false, AutoEllipsis = true, Location = new Point(S(30), S(40)), Size = new Size(S(800), S(20)), Font = Theme.Small };
            auto.Height = S(66);
            auto.Controls.AddRange(new Control[] { autoBackup, autoText, savesWhere });
            Panel listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(S(26), S(4), S(26), S(18)) };
            backupList.Dock = DockStyle.Fill;
            listHost.Controls.Add(backupList);
            page.Controls.Add(listHost);
            page.Controls.Add(buttons);
            page.Controls.Add(auto);
            page.Controls.Add(header);
            return page;
        }

        private void RefreshSavesPage()
        {
            autoBackup.Show(library.Config.BackUpSaves);
            savesWhere.Text = sw == null ? L.T("Savage Wind isn't set up yet.") : L.F("Savage Wind's saves: {0}     Backups: {1}", SaveBackups.SaveFolder(sw), AppPaths.SaveBackups);
            backupList.Items.Clear();
            foreach (SaveBackup b in SaveBackups.List())
                backupList.Items.Add(new DarkList.Row
                {
                    Title = b.When.ToString("yyyy-MM-dd  HH:mm"),
                    Detail = BackupReason(b.Why) + "  ·  " + L.P(b.Files, "{0} file", "{0} files") + ", " + SizeText(b.Bytes),
                    Tag = b
                });
            if (backupList.Items.Count == 0)
                backupList.Items.Add(new DarkList.Row { Title = L.T("No backups yet"), Detail = L.T("Press Back up now, or play: one is made before a game with a different set of mods.") });
        }

        // A backup's reason, in the player's language (it's kept in English in the backup's folder name)
        private static string BackupReason(string why)
        {
            Match m = Regex.Match(why ?? "", "^before playing “(.*)” \\((\\d+) mods?\\)$");
            if (m.Success) return L.P(int.Parse(m.Groups[2].Value), "before playing “{1}” ({0} mod)", "before playing “{1}” ({0} mods)", m.Groups[1].Value);
            if (why == "before playing unmodded") return L.T("before playing unmodded");
            if (why == "by hand") return L.T("by hand");
            if (why != null && why.StartsWith("before restoring ", StringComparison.Ordinal)) return L.F("before restoring {0}", why.Substring(17));
            return why;
        }

        private async void BackUpSavesNow()
        {
            if (busy || sw == null) return;
            if (!SaveBackups.HasSaves(sw)) { SetStatus(L.T("Savage Wind has no saves yet."), Theme.Muted); return; }
            SetBusy(true, L.T("Backing up your saves..."));
            SavageWindInstall install = sw;
            try { await Task.Run(() => SaveBackups.Backup(install, "by hand")); }
            catch (Exception ex) { SetBusy(false, L.F("Couldn't back up the saves: {0}", ex.Message), Theme.Bad); return; }
            SetBusy(false, L.T("Your saves are backed up."), Theme.Good);
            RefreshSavesPage();
        }

        private SaveBackup SelectedBackup
        {
            get { DarkList.Row r = backupList.SelectedItem as DarkList.Row; return r == null ? null : r.Tag as SaveBackup; }
        }

        private async void RestoreBackup()
        {
            SaveBackup b = SelectedBackup;
            if (b == null || busy || sw == null) return;
            if (game != null && !game.HasExited || sw.IsRunning())
            {
                MessageBox.Show(this, L.T("Close Savage Wind first: it's using the saves."), L.T("Restore saves"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, L.F("Put back the saves from {0} ({1})?", b.When.ToString("yyyy-MM-dd HH:mm"), BackupReason(b.Why)) + "\n\n" + L.T("The saves there now are backed up first, so nothing is lost."),
                    L.T("Restore saves"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            SetBusy(true, L.T("Restoring your saves..."));
            SavageWindInstall install = sw;
            try { await Task.Run(() => SaveBackups.Restore(install, b)); }
            catch (Exception ex) { SetBusy(false, L.F("Couldn't restore the saves: {0}", ex.Message), Theme.Bad); return; }
            SetBusy(false, L.F("Your saves from {0} are back.", b.When.ToString("yyyy-MM-dd HH:mm")), Theme.Good);
            RefreshSavesPage();
        }

        private void DeleteBackup()
        {
            SaveBackup b = SelectedBackup;
            if (b == null || busy) return;
            if (MessageBox.Show(this, L.F("Delete the backup from {0}? It can't be undone.", b.When.ToString("yyyy-MM-dd HH:mm")), L.T("Delete backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { SaveBackups.Delete(b); }
            catch (Exception ex) { SetStatus(L.F("Couldn't delete it: {0}", ex.Message), Theme.Bad); }
            RefreshSavesPage();
        }

        // ------------------------------------------------------------------ tools
        private Control BuildToolsPage()
        {
            Panel page = new Panel { BackColor = Theme.Window };
            Panel header = PageHeader(L.T("Tools"), null);
            Panel column = SectionColumn();
            List<Control> items = new List<Control>();
            Func<string, string, string, string, ButtonKind, EventHandler, Card> tool = (title, text, label, glyph, kind, click) =>
            {
                Panel a;
                Card c = Section(title, text, out a);
                Actions(a, Button(label, glyph, kind, click));
                return c;
            };
            items.Add(tool(L.T("Merge now"), L.T("Merges the mods that are on into the files Savage Wind loads. Play does it by itself when something changed; this is to check a set of mods without starting the game."), L.T("Merge"), Theme.IconMerge, ButtonKind.Primary, delegate { MergeAsync(); }));
            items.Add(tool(L.T("Merge report"), L.T("What the last merge did: the files merged piece by piece, the ones only one mod could win, resource sizes, warnings."), L.T("Open"), Theme.IconFile, ButtonKind.Secondary, delegate { OpenFile(AppPaths.BuildReport, L.T("There's no merge report yet: press Merge first.")); }));
            items.Add(tool(L.T("Empty merge output"), L.T("Deletes the merged files (your mods stay). The next Play or Merge makes them again."), L.T("Empty"), Theme.IconDelete, ButtonKind.Secondary, delegate { EmptyMergeOutput(); }));
            items.Add(tool(L.T("Export to SD card"), L.T("The same merge for a modded Switch (Atmosphere): written to its SD card, for the same game version."), L.T("Export..."), "\uE88E", ButtonKind.Secondary, delegate { ExportToSdCard(); }));
            items.Add(tool(L.T("Check for updates"), L.T("Guardian Terminal, Savage Wind and your mods from GameBanana. Nothing is installed by itself."), L.T("Check now"), Theme.IconUpdate, ButtonKind.Secondary, delegate { CheckForUpdatesAsync(true); }));
            items.Add(tool(L.T("Desktop shortcut"), L.T("A “Savage Wind (with mods)” shortcut on your desktop: it merges if needed and starts the game with the current profile's mods."), L.T("Make it"), "\uE71B", ButtonKind.Secondary, delegate { CreatePlayShortcut(); }));
            Panel a2;
            Card folders = Section(L.T("Folders"), L.T("Where Guardian Terminal keeps things: installed mods, the merged files Savage Wind loads, logs."), out a2);
            Actions(a2, Button(L.T("Mods"), Theme.IconFolder, ButtonKind.Ghost, delegate { OpenFolder(AppPaths.Mods); }),
                Button(L.T("Merged files"), Theme.IconFolder, ButtonKind.Ghost, delegate { OpenFolder(AppPaths.Overlay, L.T("The merge output is empty: press Merge first.")); }),
                Button(L.T("Logs"), Theme.IconFolder, ButtonKind.Ghost, delegate { OpenFolder(AppPaths.Logs); }));
            items.Add(folders);
            items.Add(tool(L.T("Clear temporary files"), L.T("Downloaded mod archives and unpacking leftovers. Your installed mods stay."), L.T("Clear..."), Theme.IconDelete, ButtonKind.Secondary, delegate { ClearTemporaryFiles(); }));
            Stack(column, items.ToArray());
            page.Controls.Add(column);
            page.Controls.Add(header);
            return page;
        }

        private void CreatePlayShortcut()
        {
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Savage Wind (with mods).lnk");
                Shortcuts.Create(lnk, Application.ExecutablePath, "--play", L.T("Start Savage Wind with your mods, from Guardian Terminal"), Application.ExecutablePath + ",0");
                SetStatus(L.T("Made a “Savage Wind (with mods)” shortcut on your desktop."), Theme.Good);
            }
            catch (Exception ex)
            {
                SetStatus(L.F("Couldn't make the shortcut: {0}", ex.Message), Theme.Bad);
            }
        }

        // "Clear temporary files": leftovers of unpacking and the downloaded mod archives (installed mods stay)
        private void ClearTemporaryFiles()
        {
            if (busy) return;
            long bytes = FolderSize(AppPaths.Staging) + FolderSize(AppPaths.Downloads);
            if (bytes == 0) { SetStatus(L.T("There are no temporary files."), Theme.Muted); return; }
            if (MessageBox.Show(this, L.F("Delete {0} of temporary files?", SizeText(bytes)) + "\n\n" +
                    L.T("That's the downloaded mod archives (downloads\\) and unpacking leftovers. Your installed mods stay; you'd need to download a mod again to reinstall it."),
                    L.T("Clear temporary files"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                AppPaths.DeleteFolderInside(AppPaths.Staging, AppPaths.Root);
                AppPaths.DeleteFolderInside(AppPaths.Downloads, AppPaths.Root);
                SetStatus(L.F("Cleared {0} of temporary files.", SizeText(bytes)), Theme.Good);
            }
            catch (Exception ex)
            {
                SetStatus(L.F("Couldn't clear everything: {0}", ex.Message), Theme.Warn);
            }
        }

        // ------------------------------------------------------------------ settings
        private readonly Label swPath = new Label();
        private readonly ToggleSwitch dlcSetting = new ToggleSwitch(), updatesSetting = new ToggleSwitch(), cardsSetting = new ToggleSwitch();
        private Card swSection;

        private Control BuildSettingsPage()
        {
            Panel page = new Panel { BackColor = Theme.Window };
            Panel header = PageHeader(L.T("Settings"), null);
            Panel column = SectionColumn();

            Panel a;
            swSection = Section("Savage Wind", "", out a);
            swSection.Height = S(124);
            swPath.Font = Theme.Small;
            swPath.ForeColor = Theme.Muted;
            swPath.BackColor = Theme.Surface;
            swPath.AutoEllipsis = true;
            swSection.Controls.Add(swPath);
            swSection.Resize += delegate { swPath.SetBounds(S(18), S(40), swSection.Width - S(36), S(70)); };
            FlatButton install = Button(L.T("Install or update..."), Theme.IconBrowse, ButtonKind.Secondary, delegate { InstallSavageWind(null); });
            FlatButton find = Button(L.T("Find it..."), Theme.IconSearch, ButtonKind.Secondary, delegate { ChooseSavageWind(); });
            FlatButton setup = Button(L.T("Start its setup"), Theme.IconPlay, ButtonKind.Ghost, delegate { StartSavageWindSetup(); });
            FlatButton log = Button(L.T("Its log"), Theme.IconFile, ButtonKind.Ghost, delegate { if (sw != null) OpenFile(sw.LogPath, L.T("Savage Wind hasn't written a log yet.")); });
            Actions(a, install, find, setup, log);
            // (the actions sit at the top right here, over the longer text)
            swSection.Resize += delegate { a.Top = S(12); swPath.Width = Math.Max(S(100), swSection.Width - S(36)); swPath.Top = S(56); };

            Panel b;
            Card dlc = Section("DLC", L.T("The Master Trials and The Champions' Ballad, set up in Savage Wind. Switching it off renames Savage Wind's DLC folders (nothing is deleted); mods' DLC parts are left out while it's off."), out b);
            dlcSetting.Size = new Size(S(44), S(24));
            dlcSetting.CheckedChanged += delegate { if (dlcSetting.Checked != (sw != null && sw.HasDlc)) SwitchDlc(dlcSetting.Checked); };
            Actions(b, dlcSetting);

            Panel c;
            Card updates = Section(L.T("Check for updates"), L.T("Once a day: Guardian Terminal (its GitHub page), Savage Wind (its GitHub releases) and your mods from GameBanana. You're only told: nothing is installed by itself."), out c);
            updatesSetting.Size = new Size(S(44), S(24));
            updatesSetting.CheckedChanged += delegate { library.Config.CheckForUpdates = updatesSetting.Checked; library.Config.Save(); };
            Actions(c, Button(L.T("Check now"), Theme.IconUpdate, ButtonKind.Ghost, delegate { CheckForUpdatesAsync(true); }), updatesSetting);

            Panel d;
            Card cards = Section(L.T("Mod pictures"), L.T("Show the mods as cards with their pictures (from GameBanana), or as compact rows."), out d);
            cardsSetting.Size = new Size(S(44), S(24));
            cardsSetting.CheckedChanged += delegate
            {
                library.Config.ShowCards = cardsSetting.Checked;
                library.Config.Save();
                modList.Cards = cardsSetting.Checked;
                viewButton.Glyph = cardsSetting.Checked ? Theme.IconRows : Theme.IconCards;
                viewButton.Invalidate();
            };
            Actions(d, cardsSetting);

            Panel lp;
            Card language = Section(L.T("Language"), L.T("The language Guardian Terminal speaks. It changes when Guardian Terminal starts again."), out lp);
            FlatButton langButton = Button(LanguageName(library.Config.Language), "", ButtonKind.Secondary, null);
            ContextMenuStrip langMenu = Theme.Menu();
            langMenu.Items.Add(L.T("Automatic (Windows' language)"), null, delegate { SetLanguage("auto"); });
            foreach (L.Language lang in L.All)
            {
                L.Language l = lang;
                langMenu.Items.Add(l.Name, null, delegate { SetLanguage(l.Code); });
            }
            langButton.Click += delegate { langMenu.Show(langButton, new Point(0, langButton.Height + S(2))); };
            Actions(lp, langButton);

            Panel mp;
            Card mature = Section(L.T("Mature mods on GameBanana"), L.T("Show the mods GameBanana marks as mature (18+) on the GameBanana page. They're hidden while this is off."), out mp);
            matureSetting.Size = new Size(S(44), S(24));
            matureSetting.CheckedChanged += delegate { library.Config.ShowMatureMods = matureSetting.Checked; library.Config.Save(); };
            Actions(mp, matureSetting);

            Panel e;
            Card folder = Section(L.T("Guardian Terminal's folder"), AppPaths.Root + "\n" + L.T("Your mods, profiles, merged files, save backups and settings are all kept here. Move the whole folder to move Guardian Terminal."), out e);
            Actions(e, Button(L.T("Open"), Theme.IconFolder, ButtonKind.Ghost, delegate { OpenFolder(AppPaths.Root); }));

            Stack(column, swSection, language, dlc, updates, cards, mature, folder);
            page.Controls.Add(column);
            page.Controls.Add(header);
            return page;
        }

        private readonly ToggleSwitch matureSetting = new ToggleSwitch();

        private static string LanguageName(string code)
        {
            if (string.IsNullOrEmpty(code) || code == "auto") return L.T("Automatic (Windows' language)");
            L.Language l = L.All.FirstOrDefault(x => x.Code == code);
            return l == null ? code : l.Name;
        }

        // set when the window closes to start again (in another language)
        public bool RestartAfterClose { get; private set; }

        private void SetLanguage(string code)
        {
            if (code == (library.Config.Language ?? "auto")) return;
            library.Config.Language = code;
            library.Config.LastBuildSummary = null;  // (the status line's summary of the last merge was in the old language)
            library.Config.Save();
            L.Language next = L.All.FirstOrDefault(x => x.Code == code);
            string name = next == null ? LanguageName(code) : next.Name;
            if (busy || MessageBox.Show(this, L.F("Guardian Terminal will speak {0} after it starts again. Restart it now?", name), L.T("Language"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RestartAfterClose = true;
            Close();
        }

        private void RefreshSettingsPage()
        {
            matureSetting.Show(library.Config.ShowMatureMods);
            string pending = sw == null && library.Config.SavageWindExe != null && File.Exists(library.Config.SavageWindExe) ? library.Config.SavageWindExe : null;
            swPath.Text = sw != null
                ? sw.Exe + (string.IsNullOrEmpty(sw.AppVersion) ? "" : "   " + L.F("(version {0})", sw.AppVersion)) + "\r\n" +
                  L.F("Game files {0}  ·  {1}  ·  saves in {2}", sw.GameVersion,
                    !sw.DlcSetUp ? L.T("no DLC set up") : !sw.HasDlc ? L.T("DLC switched off") : sw.DlcVersion != null ? "DLC " + sw.DlcVersion : L.T("DLC: on"), SaveBackups.SaveFolder(sw))
                : pending != null
                    ? pending + "\r\n" + L.T("Its game files aren't set up yet: press Start its setup, pick your own 1.9.0 game files there, then come back (Guardian Terminal checks again when you open it).")
                    : L.T("Not found. Install it (Guardian Terminal downloads the newest release from Savage Wind's GitHub page), or find savagewind.exe if you have it.");
            swPath.ForeColor = sw != null ? Theme.Muted : Theme.Warn;
            dlcSetting.Enabled = sw != null && sw.DlcSetUp;
            dlcSetting.Show(sw != null && sw.HasDlc);
            updatesSetting.Show(library.Config.CheckForUpdates);
            cardsSetting.Show(library.Config.ShowCards);
            if (sw == null && pending != null) RecheckPendingSetup();
        }

        // A Savage Wind that was waiting for its game files to be set up: ready now?
        private async void RecheckPendingSetup()
        {
            string problem;
            SavageWindInstall s = SavageWindInstall.Open(library.Config.SavageWindExe, out problem);
            if (s == null) return;
            sw = s;
            library.Config.LastBuildSignature = null;
            library.Config.Save();
            ShowInstall();
            StartIndexing();
            banner.Visible = false;
            SetStatus(L.T("Savage Wind is set up. Ready to play your mods."), Theme.Good);
            RefreshSettingsPage();
            await RecheckOutdatedMods();
        }

        private async void ChooseSavageWind()
        {
            if (busy) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("Find savagewind.exe");
                d.Filter = "Savage Wind (savagewind.exe)|savagewind.exe|" + L.T("Programs") + " (*.exe)|*.exe";
                if (sw != null) d.InitialDirectory = sw.Folder;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string problem;
                SavageWindInstall s = SavageWindInstall.Open(d.FileName, out problem);
                library.Config.SavageWindExe = d.FileName;
                library.Config.LastBuildSignature = null;
                library.Config.Save();
                if (s == null)
                {
                    sw = null;
                    ShowInstall();
                    RefreshSettingsPage();
                    MessageBox.Show(this, problem, "Savage Wind", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                sw = s;
                ShowInstall();
                StartIndexing();
                banner.Visible = false;
                RefreshSettingsPage();
                SetStatus(L.F("Using Savage Wind in {0}.", s.Folder), Theme.Good);
            }
            await RecheckOutdatedMods();
        }

        // Savage Wind's own window, to set up the game files (no mods)
        private void StartSavageWindSetup()
        {
            string exe = sw != null ? sw.Exe : library.Config.SavageWindExe;
            if (exe == null || !File.Exists(exe)) { SetStatus(L.T("Install Savage Wind first, or find it."), Theme.Warn); return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false };
                psi.EnvironmentVariables.Remove(SavageWindInstall.OverlayVariable);
                Process.Start(psi);
                SetStatus(L.T("Savage Wind is open: set up your game files there (your own 1.9.0 dump). Come back here when it's done."), Theme.Muted);
            }
            catch (Exception ex) { SetStatus(L.F("Couldn't start Savage Wind: {0}", ex.Message), Theme.Bad); }
        }

        // Downloads Savage Wind's newest release from its GitHub page into a folder chosen here; the saves of the one
        // in use can come along (as Savage Wind's README says for updating)
        private async void InstallSavageWind(SavageWindRelease release)
        {
            if (busy) return;
            if (release == null)
            {
                SetBusy(true, L.T("Looking up Savage Wind's newest release..."));
                try { release = await Task.Run(() => SavageWindRelease.Newest()); }
                catch (Exception ex) { SetBusy(false, L.F("Couldn't reach Savage Wind's GitHub page: {0}", ex.Message), Theme.Bad); return; }
                SetBusy(false, null);
                if (release == null) { SetStatus(L.T("No Windows release of Savage Wind was found on its GitHub page."), Theme.Warn); return; }
            }
            string have = sw != null && !string.IsNullOrEmpty(sw.AppVersion) ? sw.AppVersion : null;
            string intro = (release.Prerelease
                    ? L.F("Savage Wind {0} (a pre-release), from Savage Wind's own GitHub page ({1}): {2}.", release.Version, SavageWindRelease.ReleasesPage, SizeText(release.AssetSize))
                    : L.F("Savage Wind {0}, from Savage Wind's own GitHub page ({1}): {2}.", release.Version, SavageWindRelease.ReleasesPage, SizeText(release.AssetSize))) +
                (have != null ? "\n" + L.F("You have {0}.", have) : "") +
                "\n\n" + L.T("It goes in a new folder of its own. Then start it once: its setup asks for your own game files (version 1.9.0) and builds the game code (about 15 minutes, once).") +
                "\n\n" + L.T("Choose where to put it (not Program Files: it keeps your game files, saves and settings in its folder).");
            if (MessageBox.Show(this, intro, L.T("Install Savage Wind"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            string parent;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.F("Where to put Savage Wind {0} (a folder named after it is made inside)", release.Version);
                d.SelectedPath = sw != null ? Path.GetDirectoryName(sw.Folder) : Directory.Exists(@"D:\Games") ? @"D:\Games" : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                parent = d.SelectedPath;
            }
            if (parent.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, L.T("Program Files isn't a good place: Savage Wind needs to write its game files and saves in its folder. Choose another folder."), L.T("Install Savage Wind"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetBusy(true, L.F("Downloading Savage Wind {0}...", release.Version));
            string exe;
            try { exe = await Task.Run(() => release.Install(parent, Progress)); }
            catch (Exception ex) { SetBusy(false, L.F("Couldn't install Savage Wind: {0}", ex.Message), Theme.Bad); return; }
            SetBusy(false, L.F("Savage Wind {0} is in {1}.", release.Version, Path.GetDirectoryName(exe)), Theme.Good);
            if (sw != null && SaveBackups.HasSaves(sw) && MessageBox.Show(this, L.F("Copy your saves from the Savage Wind you have ({0}) to the new one?", sw.Folder) + "\n\n" + L.T("(They're copied: the old ones stay.)"),
                    L.T("Install Savage Wind"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { SavageWindRelease.CopySaves(sw.Folder, Path.GetDirectoryName(exe)); }
                catch (Exception ex) { MessageBox.Show(this, L.F("Couldn't copy the saves: {0}", ex.Message), L.T("Install Savage Wind"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            library.Config.SavageWindExe = exe;
            library.Config.LastBuildSignature = null;
            library.Config.Save();
            sw = null;
            ShowInstall();
            ShowPage("settings");
            ShowBanner(L.F("Savage Wind {0} is installed. Start it once to set up your game files, then come back here.", release.Version), L.T("Start its setup"), StartSavageWindSetup, Theme.AccentSoft, Theme.AccentSoftText);
        }

        // ------------------------------------------------------------------ about (and the credits)
        private Control BuildAboutPage()
        {
            Panel page = new Panel { BackColor = Theme.Window, AutoScroll = true };
            Panel art = new Panel { BackColor = Theme.Window };
            art.Paint += (o, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Brand.Draw(e.Graphics, new Rectangle(0, 0, art.Height, art.Height), Color.White, Theme.Window);
            };
            Label name = new Label { Text = "Guardian Terminal", Font = new Font("Segoe UI Semibold", 22F), ForeColor = Theme.Text, BackColor = Theme.Window, AutoSize = true };
            Label version = new Label { Text = L.F("Version {0}  ·  a mod manager for Savage Wind", Updates.CurrentVersion), ForeColor = Theme.Muted, BackColor = Theme.Window, AutoSize = true };

            Card credits = new Card { Fill = Theme.Surface };
            RichTextBox text = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.TextSoft, DetectUrls = true, ScrollBars = RichTextBoxScrollBars.None, TabStop = false };
            text.LinkClicked += (o, e) => Open(e.LinkText);
            credits.Controls.Add(text);
            Action<string, Color, Font> add = (s, c, f) => { text.SelectionStart = text.TextLength; text.SelectionLength = 0; text.SelectionColor = c; text.SelectionFont = f; text.AppendText(s); };
            text.HandleCreated += delegate { FillCredits(add); };
            FlatButton gh = Button(L.T("Guardian Terminal on GitHub"), Theme.IconLink, ButtonKind.Secondary, delegate { Open(Updates.ProjectPage); });
            FlatButton swb = Button(L.T("Savage Wind on GitHub"), Theme.IconLink, ButtonKind.Secondary, delegate { Open(SavageWindRelease.ProjectPage); });
            FlatButton welcome = Button(L.T("Show the welcome screen"), "", ButtonKind.Ghost, delegate { using (WelcomeForm w = new WelcomeForm(library.Config)) w.ShowDialog(this); });
            page.Controls.AddRange(new Control[] { art, name, version, credits, gh, swb, welcome });
            page.Resize += delegate
            {
                int w = Math.Min(S(760), page.ClientSize.Width - S(60)), x = (page.ClientSize.Width - w) / 2;
                art.SetBounds(x, S(34), S(104), S(104));
                name.Location = new Point(art.Right + S(20), S(48));
                version.Location = new Point(art.Right + S(24), S(96));
                credits.SetBounds(x, art.Bottom + S(24), w, S(330));
                text.SetBounds(S(20), S(16), w - S(40), S(300));
                foreach (FlatButton fb in new[] { gh, swb, welcome }) fb.Size = new Size(fb.PreferredWidth(S(14)), S(38));
                gh.Location = new Point(x, credits.Bottom + S(16));
                swb.Location = new Point(gh.Right + S(10), gh.Top);
                welcome.Location = new Point(x + w - welcome.Width, gh.Top);
            };
            return page;
        }

        // The credits every player sees (About): who made Guardian Terminal, who made Savage Wind
        private static void FillCredits(Action<string, Color, Font> add)
        {
            add(L.T("Credits") + "\n", Theme.Text, Theme.Big);
            WithNames(add, L.T("{0} is made by {1}, its owner."), "Guardian Terminal", "chonkmaster-22");
            add("\n" + Updates.ProjectPage + "\n\n", Theme.TextSoft, Theme.Body);
            WithNames(add, L.T("{0} is the PC port of Breath of the Wild that Guardian Terminal plays your mods with. It's made by {1}, and built on LAS Recomp by peiikai. All the credit for running the game goes to them: report the game's bugs on Savage Wind's page, and support it."),
                "Savage Wind", L.T("Revivedx and the Savage Wind Recomp contributors"));
            add("\n" + SavageWindRelease.ProjectPage + "\n\n", Theme.TextSoft, Theme.Body);
            add(L.T("Thanks to the mod makers, and to BCML and TKMM, whose ideas (merging, profiles, Merge) this follows.") + "\n\n", Theme.TextSoft, Theme.Body);
            add(L.T("Fan-made and free, under the MIT License. Not affiliated with Nintendo, the Savage Wind project, BCML or TKMM. You need your own copy of the game: Guardian Terminal never includes or downloads any game files."), Theme.Muted, Theme.Small);
        }

        // A sentence of the credits in the player's language, its {0} (what) and {1} (who) in bold: the names stay
        // where each language puts them
        internal static void WithNames(Action<string, Color, Font> add, string sentence, string what, string who)
        {
            foreach (string part in Regex.Split(sentence, @"(\{[01]\})"))
            {
                if (part == "{0}") add(what, Theme.Text, Theme.BodyBold);
                else if (part == "{1}") add(who, Theme.AccentSoftText, Theme.BodyBold);
                else if (part.Length > 0) add(part, Theme.TextSoft, Theme.Body);
            }
        }
    }
}
