using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    // The base of the small dark dialogs: a title line, a message, room for content, buttons at the bottom right
    internal class DarkDialog : Form
    {
        protected readonly float s;
        protected readonly Label message = new Label();
        protected readonly FlowLayoutPanel buttons = new FlowLayoutPanel();

        public DarkDialog(string title, string text, int width, int height)
        {
            s = DeviceDpi / 96f;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(S(width), S(height));
            message.Text = text;
            message.ForeColor = Theme.TextSoft;
            message.SetBounds(S(22), S(18), ClientSize.Width - S(44), S(44));
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.SetBounds(S(16), ClientSize.Height - S(60), ClientSize.Width - S(32), S(48));
            buttons.BackColor = Theme.Window;
            Controls.Add(message);
            Controls.Add(buttons);
            HandleCreated += delegate { NativeMethods.UseDarkTitleBar(Handle); };
        }

        protected int S(int px) { return (int)Math.Round(px * s); }

        protected FlatButton AddButton(string text, ButtonKind kind, DialogResult result)
        {
            FlatButton b = new FlatButton { Text = text, Kind = kind, Compact = true, DialogResult = result };
            b.Size = new Size(Math.Max(S(96), b.PreferredWidth(S(16))), S(38));
            b.Margin = new Padding(S(8), S(4), 0, 0);
            buttons.Controls.Add(b);
            return b;
        }
    }

    // A list to tick: which versions in a download to install, which found mods to import
    internal sealed class CheckDialog : DarkDialog
    {
        private readonly DarkList list = new DarkList { CheckBoxes = true };
        private readonly FlatButton ok;

        private CheckDialog(string title, string text, List<DarkList.Row> rows, string okText) : base(title, text, 620, 480)
        {
            list.SetBounds(S(22), S(70), ClientSize.Width - S(44), ClientSize.Height - S(150));
            foreach (DarkList.Row r in rows) list.Items.Add(r);
            FlatButton all = new FlatButton { Text = L.T("All"), Kind = ButtonKind.Ghost, Compact = true, Size = new Size(S(80), S(32)), Location = new Point(S(22), ClientSize.Height - S(76)) };
            all.Click += delegate { list.SetAll(list.CheckedRows.Count < list.Items.Count); };
            Controls.Add(list);
            Controls.Add(all);
            ok = AddButton(okText, ButtonKind.Primary, DialogResult.OK);
            AddButton(L.T("Cancel"), ButtonKind.Secondary, DialogResult.Cancel);
            AcceptButton = ok;
            list.CheckedChanged += delegate { Update(); };
            Update();
        }

        private new void Update()
        {
            ok.Enabled = list.CheckedRows.Count > 0;
        }

        // The indexes ticked, or null
        public static List<int> Ask(IWin32Window owner, string title, string text, List<DarkList.Row> rows, string okText)
        {
            using (CheckDialog d = new CheckDialog(title, text, rows, okText))
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                return rows.Select((r, i) => r.Checked ? i : -1).Where(i => i >= 0).ToList();
            }
        }
    }

    internal enum DuplicateChoice { Replace, KeepBoth, Skip }

    // "This mod is already installed": replace it, keep both, or skip; optionally for the rest of a batch too
    internal sealed class DuplicateDialog : DarkDialog
    {
        private readonly ToggleSwitch forAll = new ToggleSwitch();
        private DuplicateChoice choice = DuplicateChoice.Skip;

        private DuplicateDialog(string name, bool more) : base(L.T("Already installed"),
            L.F("{0} is already installed. Replace it with this one (it keeps its place and on / off in every profile), keep both, or skip it?", name), 560, more ? 200 : 170)
        {
            message.Height = S(60);
            if (more)
            {
                forAll.SetBounds(S(22), S(92), S(40), S(22));
                Label t = new Label { Text = L.T("Do the same for the other mods already installed"), AutoSize = true, ForeColor = Theme.TextSoft, Location = new Point(S(70), S(94)) };
                t.Click += delegate { forAll.Checked = !forAll.Checked; };
                Controls.Add(forAll);
                Controls.Add(t);
            }
            FlatButton replace = AddButton(L.T("Replace"), ButtonKind.Primary, DialogResult.OK);
            replace.Click += delegate { choice = DuplicateChoice.Replace; };
            FlatButton both = AddButton(L.T("Keep both"), ButtonKind.Secondary, DialogResult.OK);
            both.Click += delegate { choice = DuplicateChoice.KeepBoth; };
            FlatButton skip = AddButton(L.T("Skip"), ButtonKind.Ghost, DialogResult.Cancel);
            skip.Click += delegate { choice = DuplicateChoice.Skip; };
            AcceptButton = replace;
            CancelButton = skip;
        }

        public static DuplicateChoice Ask(IWin32Window owner, string name, bool more, out bool forAllOthers)
        {
            using (DuplicateDialog d = new DuplicateDialog(name, more))
            {
                d.ShowDialog(owner);
                forAllOthers = d.forAll.Checked;
                return d.choice;
            }
        }
    }

    // "Import mods": the mods found in emulators and on a Switch SD card, and any folder of downloads, ticked to
    // install them all at once
    internal sealed class ImportDialog : DarkDialog
    {
        private readonly DarkList list = new DarkList { CheckBoxes = true };
        private readonly FlatButton ok;
        public readonly List<FoundMod> Chosen = new List<FoundMod>();

        public ImportDialog() : base(L.T("Import mods"), L.T("Mods found on this PC: in emulators (Ryujinx, yuzu and the like) and on a Switch SD card. Tick the ones to install, or add a folder of mod downloads."), 680, 520)
        {
            list.SetBounds(S(22), S(70), ClientSize.Width - S(44), ClientSize.Height - S(150));
            Controls.Add(list);
            FlatButton folder = new FlatButton { Text = L.T("Add a folder of mods..."), Glyph = Theme.IconFolder, Kind = ButtonKind.Secondary, Compact = true, Location = new Point(S(22), ClientSize.Height - S(56)) };
            folder.Size = new Size(folder.PreferredWidth(S(16)), S(38));
            folder.Click += delegate { AddFolder(); };
            FlatButton all = new FlatButton { Text = L.T("All"), Kind = ButtonKind.Ghost, Compact = true, Size = new Size(S(70), S(38)), Location = new Point(folder.Right + S(8), ClientSize.Height - S(56)) };
            all.Click += delegate { list.SetAll(list.CheckedRows.Count < list.Items.Count); };
            Controls.Add(folder);
            Controls.Add(all);
            buttons.SetBounds(all.Right + S(8), ClientSize.Height - S(60), ClientSize.Width - all.Right - S(24), S(48));
            ok = AddButton(L.T("Install"), ButtonKind.Primary, DialogResult.OK);
            AddButton(L.T("Cancel"), ButtonKind.Secondary, DialogResult.Cancel);
            AcceptButton = ok;
            list.CheckedChanged += delegate { UpdateOk(); };
            ok.Enabled = false;
            Shown += delegate { Scan(); };
            FormClosing += (o, e) => { if (DialogResult == DialogResult.OK) Chosen.AddRange(list.CheckedRows.Select(r => (FoundMod)r.Tag)); };
        }

        private void Scan()
        {
            UseWaitCursor = true;
            List<ModSource> sources;
            try { sources = ModSources.Find(); }
            catch (Exception) { sources = new List<ModSource>(); }
            UseWaitCursor = false;
            foreach (ModSource src in sources)
                foreach (FoundMod m in src.Mods)
                    list.Items.Add(new DarkList.Row { Title = m.Name, Detail = src.Name + "  ·  " + m.Path + (m.DlcFolder != null ? "  ·  " + L.T("with DLC files") : ""), Tag = m, Checked = false });
            if (list.Items.Count == 0)
                message.Text = L.T("No emulator or SD card mods were found on this PC. Add a folder of mod downloads instead (each .zip, .7z, .rar, .bnp and mod folder in it is installed).");
        }

        private void AddFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("A folder with mod downloads (.zip, .7z, .rar, .bnp) or mod folders in it");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<FoundMod> mods = ModSources.InFolder(d.SelectedPath);
                if (mods.Count == 0)
                {
                    MessageBox.Show(this, L.T("No mods were found in that folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                foreach (FoundMod m in mods)
                    list.Items.Add(new DarkList.Row { Title = m.Name, Detail = m.Path, Tag = m, Checked = true });
                list.Invalidate();
                UpdateOk();
            }
        }

        private void UpdateOk()
        {
            int n = list.CheckedRows.Count;
            ok.Text = n > 0 ? L.F("Install ({0})", n) : L.T("Install");
            ok.Size = new Size(Math.Max(S(96), ok.PreferredWidth(S(16))), S(38));
            ok.Enabled = n > 0;
        }
    }
}
