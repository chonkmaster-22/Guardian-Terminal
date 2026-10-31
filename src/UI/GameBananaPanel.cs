using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    // The GameBanana page: Breath of the Wild (Switch) mods browsed, searched and installed without the website.
    // The mods as tiles (popular, newest, recently updated; by category; or a search); the one picked shows its
    // pictures, description and files on the right, and the file picked is downloaded into downloads\ and handed to
    // the window (Downloaded), which installs it. A GameBanana link pasted in the search box opens that mod.
    internal sealed class GameBananaPanel : Panel
    {
        private const int PerPage = 24;
        private readonly float s;
        private readonly SearchBox search;
        private readonly FlatButton sortButton = new FlatButton(), categoryButton = new FlatButton(), prev = new FlatButton(), next = new FlatButton();
        private readonly ContextMenuStrip sortMenu = Theme.Menu(), categoryMenu = Theme.Menu();
        private readonly Label pageLabel = new Label();
        private readonly GbGrid grid = new GbGrid();
        private readonly Panel detail = new Panel(), picture = new Panel();
        private readonly Label name = new Label(), meta = new Label();
        private readonly FlatButton install = new FlatButton(), open = new FlatButton();
        private readonly DarkList files = new DarkList();
        private readonly RichTextBox about = new RichTextBox();
        private readonly ProgressBar bar = new ProgressBar();

        private string sort = GameBanana.SortPopular, query = "";
        private long category;
        private string categoryName;
        private int page = 1;
        private long total;
        private int loadId, detailId;
        private bool loaded, downloading;
        private GbMod mod;
        private Image preview;

        public Func<bool> CanInstall = () => true;
        public Func<long, bool> IsInstalled = id => false;
        public Func<bool> ShowMature = () => false;
        public event Action<string, ModOrigin> Downloaded;

        public GameBananaPanel()
        {
            s = DeviceDpi / 96f;
            BackColor = Theme.Window;

            Label title = new Label { Text = "GameBanana", Font = Theme.PageTitle, ForeColor = Theme.Text, AutoSize = true, Location = new Point(S(28), S(18)), BackColor = Theme.Window };
            Label sub = new Label { Text = L.T("Breath of the Wild Switch mods: find one, pick a file, install it. No browser needed."), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(30), S(54)), BackColor = Theme.Window };
            search = new SearchBox(L.T("Search GameBanana, or paste a mod's link"));
            search.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(); } };
            sortButton.Kind = categoryButton.Kind = ButtonKind.Secondary;
            sortButton.Glyph = Theme.IconSort;
            categoryButton.Glyph = Theme.IconRows;
            sortButton.Click += delegate { sortMenu.Show(sortButton, new Point(0, sortButton.Height + S(2))); };
            categoryButton.Click += delegate { FillCategories(); categoryMenu.Show(categoryButton, new Point(0, categoryButton.Height + S(2))); };
            foreach (KeyValuePair<string, string> o in new[] { Pair(L.T("Most liked"), GameBanana.SortPopular), Pair(L.T("Newest"), GameBanana.SortNewest), Pair(L.T("Recently updated"), GameBanana.SortUpdated) })
            {
                KeyValuePair<string, string> opt = o;
                sortMenu.Items.Add(opt.Key, null, delegate { sort = opt.Value; query = ""; search.Box.Text = ""; page = 1; ShowFilters(); Load(); });
            }

            grid.Dock = DockStyle.Fill;
            grid.IsInstalled = id => IsInstalled(id);
            grid.SelectionChanged += delegate { if (grid.Selected != null) ShowMod(grid.Selected.Id); };
            prev.Text = L.T("Previous"); prev.Kind = ButtonKind.Ghost; prev.Compact = true;
            next.Text = L.T("Next"); next.Kind = ButtonKind.Ghost; next.Compact = true;
            prev.Click += delegate { if (page > 1) { page--; Load(); } };
            next.Click += delegate { if (page * PerPage < total) { page++; Load(); } };
            pageLabel.ForeColor = Theme.Muted;
            pageLabel.TextAlign = ContentAlignment.MiddleCenter;
            Panel pager = new Panel { Dock = DockStyle.Bottom, Height = S(48), BackColor = Theme.Window };
            pager.Controls.AddRange(new Control[] { prev, pageLabel, next });
            pager.Resize += delegate
            {
                prev.SetBounds(S(12), S(6), S(110), S(36));
                next.SetBounds(pager.Width - S(122), S(6), S(110), S(36));
                pageLabel.SetBounds(prev.Right, S(6), next.Left - prev.Right, S(36));
            };
            Panel left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
            left.Controls.Add(grid);
            left.Controls.Add(pager);

            BuildDetail();
            detail.Dock = DockStyle.Right;
            detail.Width = S(372);

            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(S(14), 0, S(12), S(8)) };
            body.Controls.Add(left);
            body.Controls.Add(detail);
            Panel top = new Panel { Dock = DockStyle.Top, Height = S(132), BackColor = Theme.Window };
            top.Controls.AddRange(new Control[] { title, sub, search, sortButton, categoryButton });
            top.Resize += delegate
            {
                int y = S(86), h = S(36);
                categoryButton.SetBounds(top.Width - S(26) - S(200), y, S(200), h);
                sortButton.SetBounds(categoryButton.Left - S(10) - S(180), y, S(180), h);
                search.SetBounds(S(26), y, Math.Max(S(160), sortButton.Left - S(36)), h);
            };
            Controls.Add(body);
            Controls.Add(top);
            ShowFilters();
            VisibleChanged += delegate
            {
                if (!Visible) return;
                if (!loaded) { loaded = true; Load(); }
                // a GameBanana link just copied: ready to open
                string clip = null;
                try { clip = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (Exception) { }
                if (GameBanana.ParseModId(clip) > 0 && search.Box.Text.Length == 0) search.Box.Text = clip.Trim();
                search.Box.Focus();
            };
        }

        private int S(int px) { return (int)Math.Round(px * s); }

        private static KeyValuePair<string, string> Pair(string a, string b) { return new KeyValuePair<string, string>(a, b); }

        private void ShowFilters()
        {
            sortButton.Text = query.Length > 0 ? L.T("Best match") : sort == GameBanana.SortPopular ? L.T("Most liked") : sort == GameBanana.SortUpdated ? L.T("Recently updated") : L.T("Newest");
            categoryButton.Text = category == 0 ? L.T("All categories") : categoryName;
            sortButton.Invalidate();
            categoryButton.Invalidate();
        }

        private async void FillCategories()
        {
            if (categoryMenu.Items.Count > 0) return;
            categoryMenu.Items.Add(L.T("All categories"), null, delegate { category = 0; page = 1; query = ""; search.Box.Text = ""; ShowFilters(); Load(); });
            List<GbCategory> cats;
            try { cats = await Task.Run(() => GameBanana.Categories()); }
            catch (Exception) { return; }
            foreach (GbCategory c in cats.Where(c => c.Count > 0))
            {
                GbCategory cat = c;
                categoryMenu.Items.Add(cat.Name + "  (" + cat.Count + ")", null, delegate { category = cat.Id; categoryName = cat.Name; page = 1; query = ""; search.Box.Text = ""; ShowFilters(); Load(); });
            }
            if (categoryMenu.Visible) { categoryMenu.Close(); categoryMenu.Show(categoryButton, new Point(0, categoryButton.Height + S(2))); }
        }

        private void Search()
        {
            string text = search.Query;
            long id = GameBanana.ParseModId(text);
            if (id > 0 && text.IndexOf("gamebanana", StringComparison.OrdinalIgnoreCase) >= 0) { ShowMod(id); return; }
            query = text;
            page = 1;
            ShowFilters();
            Load();
        }

        // Opens a mod by its link (an installed mod's, for its update)
        public void LookUpLink(string url)
        {
            long id = GameBanana.ParseModId(url);
            if (id > 0) { loaded = true; ShowMod(id); }
        }

        private async void Load()
        {
            int me = ++loadId;
            grid.EmptyText = L.T("Loading mods from GameBanana...");
            grid.SetItems(null);
            prev.Enabled = next.Enabled = false;
            pageLabel.Text = "";
            GbPage result;
            string q = query, so = sort;
            long cat = category;
            int pg = page;
            try { result = await Task.Run(() => q.Length > 0 ? GameBanana.Search(q, pg, PerPage) : GameBanana.Browse(so, cat, pg, PerPage)); }
            catch (Exception e)
            {
                if (me != loadId) return;
                grid.EmptyText = L.F("Couldn't reach GameBanana: {0}", e.Message);
                grid.Invalidate();
                return;
            }
            if (me != loadId) return;
            total = result.Total;
            grid.EmptyText = q.Length > 0 ? L.F("No Breath of the Wild Switch mods match “{0}”.", q) : L.T("No mods here.");
            // (mods marked mature are left out unless Settings says to show them)
            grid.SetItems(ShowMature() ? result.Items : result.Items.Where(m => !m.Mature).ToList());
            int pages = (int)Math.Max(1, (total + PerPage - 1) / PerPage);
            pageLabel.Text = L.F("Page {0} of {1}  ·  {2} mods", page, pages, total.ToString("N0"));
            prev.Enabled = page > 1;
            next.Enabled = page < pages;
        }

        // ------------------------------------------------------------------ the mod picked
        private void BuildDetail()
        {
            detail.BackColor = Theme.Window;
            detail.Padding = new Padding(S(10), S(4), 0, S(4));
            Card card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface };
            picture.BackColor = Theme.Surface;
            picture.Paint += PaintPicture;
            name.Font = Theme.Big;
            name.ForeColor = Theme.Text;
            name.BackColor = Theme.Surface;
            name.AutoEllipsis = true;
            meta.Font = Theme.Small;
            meta.ForeColor = Theme.Muted;
            meta.BackColor = Theme.Surface;
            meta.AutoEllipsis = true;
            install.Text = L.T("Download and install");
            install.Glyph = Theme.IconBrowse;
            install.Kind = ButtonKind.Primary;
            install.Compact = true;
            install.Click += delegate { Install(); };
            open.Glyph = Theme.IconLink;
            open.Text = "";
            open.Kind = ButtonKind.Secondary;
            open.Click += delegate { if (mod != null) Open(mod.PageUrl); };
            files.ItemHeight = S(42);
            files.SelectedIndexChanged += delegate { UpdateInstall(); };
            files.DoubleClick += delegate { Install(); };
            about.ReadOnly = true;
            about.BorderStyle = BorderStyle.None;
            about.BackColor = Theme.Surface;
            about.ForeColor = Theme.TextSoft;
            about.Font = Theme.Small;
            about.DetectUrls = true;
            about.ScrollBars = RichTextBoxScrollBars.Vertical;
            about.LinkClicked += (o, e) => Open(e.LinkText);
            about.HandleCreated += delegate { NativeMethods.SetWindowTheme(about.Handle, "DarkMode_Explorer", null); };
            bar.Visible = false;
            card.Controls.AddRange(new Control[] { picture, name, meta, install, open, bar, files, about });
            card.Resize += delegate
            {
                int w = card.ClientSize.Width - S(32), x = S(16);
                picture.SetBounds(x, S(16), w, w * 9 / 16);
                name.SetBounds(x, picture.Bottom + S(10), w, S(26));
                meta.SetBounds(x, name.Bottom, w, S(20));
                open.SetBounds(x + w - S(40), meta.Bottom + S(8), S(40), S(38));
                install.SetBounds(x, meta.Bottom + S(8), w - S(48), S(38));
                bar.SetBounds(x, install.Bottom + S(4), w, S(5));
                files.SetBounds(x, install.Bottom + S(14), w, S(130));
                about.SetBounds(x, files.Bottom + S(10), w, Math.Max(S(40), card.ClientSize.Height - files.Bottom - S(26)));
            };
            detail.Controls.Add(card);
            ClearDetail(L.T("Pick a mod to see its files."));
        }

        private void ClearDetail(string text)
        {
            mod = null;
            SetPreview(null);
            name.Text = "";
            meta.Text = text;
            files.Items.Clear();
            about.Clear();
            install.Visible = open.Visible = false;
        }

        private async void ShowMod(long id)
        {
            int me = ++detailId;
            ClearDetail(L.T("Loading..."));
            GbMod m;
            try { m = await Task.Run(() => GameBanana.GetMod(id)); }
            catch (Exception e)
            {
                if (me == detailId) ClearDetail(L.F("Couldn't get that mod from GameBanana: {0}", e.Message));
                return;
            }
            if (me != detailId) return;
            mod = m;
            name.Text = m.Name;
            meta.Text = string.Join("  ·  ", new[] { string.IsNullOrEmpty(m.Submitter) ? null : L.F("by {0}", m.Submitter), string.IsNullOrEmpty(m.Version) ? null : m.Version,
                IsInstalled(id) ? L.T("installed") : null }.Where(x => x != null));
            install.Visible = open.Visible = true;
            files.Items.Clear();
            foreach (GbFile f in m.Files)
                files.Items.Add(new DarkList.Row
                {
                    Title = f.Name,
                    Detail = SizeText(f.Size) + (f.Added == DateTime.MinValue ? "" : "  ·  " + f.Added.ToString("yyyy-MM-dd")) + (string.IsNullOrWhiteSpace(f.Description) ? "" : "  ·  " + f.Description.Trim()),
                    Badge = f.Scan != null && f.Scan.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0 ? L.T("scan ok") : L.F("scan: {0}", f.Scan ?? "?"),
                    BadgeColor = f.Scan != null && f.Scan.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0 ? Theme.Good : Theme.Warn,
                    Tag = f
                });
            if (files.Items.Count > 0) files.SelectedIndex = 0;
            about.Clear();
            if (!m.IsBotwSwitch)
            {
                about.SelectionColor = Theme.Warn;
                about.AppendText(L.F("This mod is for {0}. Savage Wind needs Breath of the Wild SWITCH mods: it probably won't work.", m.Game ?? "another game") + "\n\n");
            }
            about.SelectionColor = Theme.TextSoft;
            about.AppendText(string.IsNullOrEmpty(m.Description) ? L.T("No description.") : m.Description.Length > 4000 ? m.Description.Substring(0, 4000) + "…" : m.Description);
            about.SelectionStart = 0;
            about.ScrollToCaret();
            UpdateInstall();
            // its picture, bigger
            if (!string.IsNullOrEmpty(m.PreviewUrl))
            {
                Image img = null;
                try
                {
                    byte[] data = await Task.Run(() => GameBanana.GetBytes(m.PreviewUrl));
                    using (MemoryStream ms = new MemoryStream(data)) img = new Bitmap(Image.FromStream(ms));
                }
                catch (Exception) { img = null; }
                if (me == detailId) SetPreview(img);
                else if (img != null) img.Dispose();
            }
        }

        private void SetPreview(Image img)
        {
            if (preview != null) preview.Dispose();
            preview = img;
            picture.Invalidate();
        }

        private void PaintPicture(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle r = new Rectangle(0, 0, picture.Width - 1, picture.Height - 1);
            using (GraphicsPath clip = Theme.Rounded(r, S(8)))
            {
                g.SetClip(clip);
                if (preview != null)
                {
                    float sc = Math.Max((float)r.Width / preview.Width, (float)r.Height / preview.Height);
                    g.DrawImage(preview, r.X + (r.Width - preview.Width * sc) / 2, r.Y + (r.Height - preview.Height * sc) / 2, preview.Width * sc, preview.Height * sc);
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

        private void UpdateInstall()
        {
            install.Enabled = mod != null && files.SelectedIndex >= 0 && !downloading;
            install.Text = mod != null && IsInstalled(mod.Id) ? L.T("Download and reinstall") : L.T("Download and install");
        }

        private async void Install()
        {
            if (mod == null || files.SelectedIndex < 0 || downloading || !CanInstall()) return;
            GbFile f = (GbFile)((DarkList.Row)files.SelectedItem).Tag;
            if (f.Scan != null && f.Scan.IndexOf("ok", StringComparison.OrdinalIgnoreCase) < 0 &&
                MessageBox.Show(FindForm(), L.F("GameBanana's malware scan of this file says: \"{0}\". Download it anyway?", f.Scan),
                    L.T("Download"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            downloading = true;
            UpdateInstall();
            bar.Visible = true;
            bar.Style = ProgressBarStyle.Continuous;
            bar.Value = 0;
            GbMod m = mod;
            string file;
            try
            {
                file = await Task.Run(() => GameBanana.Download(f, AppPaths.Downloads, (done, all) =>
                {
                    if (all > 0) BeginInvoke((Action)(() => bar.Value = (int)Math.Min(100, done * 100 / all)));
                }));
            }
            catch (Exception e)
            {
                downloading = false;
                bar.Visible = false;
                UpdateInstall();
                MessageBox.Show(FindForm(), L.F("The download failed: {0}", e.Message), "GameBanana", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            downloading = false;
            bar.Visible = false;
            UpdateInstall();
            ModOrigin origin = new ModOrigin
            {
                Name = m.Name, Version = m.Version, Description = m.Description, Url = m.PageUrl, Author = m.Submitter,
                FileDate = f.Added == DateTime.MinValue ? null : f.Added.ToString("yyyy-MM-dd HH:mm"), Page = m
            };
            if (Downloaded != null) Downloaded(file, origin);
        }

        // after an install: the tiles' "Installed" badges
        public void RefreshInstalled()
        {
            grid.Invalidate();
            UpdateInstall();
        }

        private static string SizeText(long b)
        {
            return b >= 1 << 20 ? string.Format("{0:N1} MB", b / 1048576.0) : string.Format("{0:N0} KB", Math.Max(1, b / 1024));
        }

        private static void Open(string url)
        {
            try { Process.Start(url); }
            catch (Exception) { }
        }
    }
}
