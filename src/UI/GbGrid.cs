using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    // GameBanana's mods as tiles: the picture, the name, who made it, likes. The pictures come in the background
    // (kept in thumbnails\gamebanana\ for next time); only the tiles in view are drawn.
    internal sealed class GbGrid : Control
    {
        private List<GbListing> items = new List<GbListing>();
        private GbListing selected;
        private int offset, hover = -1;
        private readonly Dictionary<long, Image> thumbs = new Dictionary<long, Image>();
        private readonly HashSet<long> asked = new HashSet<long>();
        private readonly object sync = new object();
        public Func<long, bool> IsInstalled = id => false;
        public event EventHandler SelectionChanged;
        public string EmptyText = "";

        public GbGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Window;
            TabStop = true;
        }

        private float Sc { get { return DeviceDpi / 96f; } }
        private int S(float px) { return (int)Math.Round(px * Sc); }
        private int TileW { get { return S(196); } }
        private int TileH { get { return S(196); } }
        private int Gap { get { return S(12); } }
        private int Columns { get { return Math.Max(1, (Width - Gap - S(10)) / (TileW + Gap)); } }
        private int Rows { get { return (items.Count + Columns - 1) / Columns; } }
        private int ContentHeight { get { return Rows * (TileH + Gap) + Gap; } }
        private int MaxOffset { get { return Math.Max(0, ContentHeight - Height); } }

        public GbListing Selected { get { return selected; } }

        public void SetItems(List<GbListing> list)
        {
            items = list ?? new List<GbListing>();
            offset = 0;
            selected = null;
            hover = -1;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private Rectangle TileRect(int i)
        {
            int cols = Columns, used = cols * TileW + (cols - 1) * Gap, left = Math.Max(Gap, (Width - S(10) - used) / 2);
            return new Rectangle(left + (i % cols) * (TileW + Gap), Gap + (i / cols) * (TileH + Gap) - offset, TileW, TileH);
        }

        private int IndexAt(Point p)
        {
            for (int i = 0; i < items.Count; i++) if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Theme.Body, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = TileRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                PaintTile(g, items[i], r, items[i] == selected, i == hover);
            }
            if (MaxOffset > 0)
            {
                int track = Height - 2 * S(6), h = Math.Max(S(30), (int)((long)track * Height / Math.Max(1, ContentHeight)));
                int y = S(6) + (int)((long)(track - h) * offset / Math.Max(1, MaxOffset));
                Theme.FillRounded(g, Theme.Border, new Rectangle(Width - S(8), y, S(5), h), S(2));
            }
        }

        private void PaintTile(Graphics g, GbListing m, Rectangle r, bool sel, bool hov)
        {
            Theme.FillRounded(g, sel ? Theme.SurfaceSelected : hov ? Theme.SurfaceHover : Theme.Surface, r, S(10));
            if (sel) Theme.DrawRounded(g, Theme.Accent, Math.Max(1, Sc), new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), S(10));
            Rectangle pic = new Rectangle(r.X + S(8), r.Y + S(8), r.Width - S(16), (r.Width - S(16)) * 9 / 16);
            using (GraphicsPath clip = Theme.Rounded(pic, S(6)))
            {
                Region old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                Image img = Thumb(m);
                if (img != null)
                {
                    float sc = Math.Max((float)pic.Width / img.Width, (float)pic.Height / img.Height);
                    g.DrawImage(img, pic.X + (pic.Width - img.Width * sc) / 2, pic.Y + (pic.Height - img.Height * sc) / 2, img.Width * sc, img.Height * sc);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Sunken)) g.FillRectangle(b, pic);
                    int e = pic.Height - S(16);
                    Brand.Draw(g, new Rectangle(pic.X + (pic.Width - e) / 2, pic.Y + S(8), e, e), Theme.Border, Theme.Sunken);
                }
                g.Clip = old;
            }
            if (IsInstalled(m.Id))
            {
                string t = L.T("Installed");
                Size sz = TextRenderer.MeasureText(t, Theme.SmallBold);
                Rectangle b = new Rectangle(pic.Right - sz.Width - S(14), pic.Y + S(6), sz.Width + S(10), S(20));
                Theme.FillRounded(g, Theme.Accent, b, S(5));
                TextRenderer.DrawText(g, t, Theme.SmallBold, b, Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            int y = pic.Bottom + S(6);
            TextRenderer.DrawText(g, m.Name, Theme.BodyBold, new Rectangle(r.X + S(10), y, r.Width - S(20), S(38)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string line = (m.Author ?? "") + (string.IsNullOrEmpty(m.Category) ? "" : "  ·  " + m.Category);
            TextRenderer.DrawText(g, line, Theme.Small, new Rectangle(r.X + S(10), r.Bottom - S(42), r.Width - S(20), S(18)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "♥ " + m.Likes.ToString("N0") + "    " + L.F("{0} views", Short(m.Views)), Theme.Small, new Rectangle(r.X + S(10), r.Bottom - S(24), r.Width - S(20), S(18)), Theme.Faint,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static string Short(long n)
        {
            return n >= 1000000 ? (n / 1000000.0).ToString("0.#") + "M" : n >= 1000 ? (n / 1000.0).ToString("0.#") + "k" : n.ToString();
        }

        // A tile's picture: from memory, else from the disk cache, else downloaded in the background
        private Image Thumb(GbListing m)
        {
            Image img;
            lock (sync)
            {
                if (thumbs.TryGetValue(m.Id, out img)) return img;
                if (asked.Contains(m.Id) || string.IsNullOrEmpty(m.ThumbUrl)) return null;
                asked.Add(m.Id);
            }
            string cache = Path.Combine(AppPaths.Thumbnails, "gamebanana", m.Id + ".jpg");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Image loaded = null;
                try
                {
                    byte[] data = File.Exists(cache) ? File.ReadAllBytes(cache) : GameBanana.GetBytes(m.ThumbUrl);
                    if (!File.Exists(cache))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cache));
                        File.WriteAllBytes(cache, data);
                    }
                    using (MemoryStream ms = new MemoryStream(data))
                    using (Image raw = Image.FromStream(ms))
                    {
                        int w = 360, h = Math.Max(1, raw.Height * w / Math.Max(1, raw.Width));
                        Bitmap small = new Bitmap(w, h);
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(raw, 0, 0, w, h);
                        }
                        loaded = small;
                    }
                }
                catch (Exception) { loaded = null; }
                lock (sync) thumbs[m.Id] = loaded;
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)Invalidate); }
                catch (Exception) { }
            });
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int i = IndexAt(e.Location);
            if (i >= 0 && items[i] != selected)
            {
                selected = items[i];
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != hover) { hover = i; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            offset = Math.Max(0, Math.Min(MaxOffset, offset - e.Delta * (TileH + Gap) / 240));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            offset = Math.Max(0, Math.Min(MaxOffset, offset));
            base.OnResize(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode) { case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: return true; }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (items.Count == 0) return;
            int i = selected == null ? -1 : items.IndexOf(selected), cols = Columns, to = i;
            switch (e.KeyCode)
            {
                case Keys.Left: to = Math.Max(0, i - 1); break;
                case Keys.Right: to = Math.Min(items.Count - 1, i + 1); break;
                case Keys.Up: to = Math.Max(0, i - cols); break;
                case Keys.Down: to = Math.Min(items.Count - 1, i < 0 ? 0 : i + cols); break;
                default: base.OnKeyDown(e); return;
            }
            if (to >= 0 && to != i)
            {
                selected = items[to];
                Rectangle r = TileRect(to);
                if (r.Top < 0) offset += r.Top - Gap;
                else if (r.Bottom > Height) offset += r.Bottom - Height + Gap;
                offset = Math.Max(0, Math.Min(MaxOffset, offset));
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            e.Handled = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) lock (sync) { foreach (Image i in thumbs.Values) if (i != null) i.Dispose(); thumbs.Clear(); }
            base.Dispose(disposing);
        }
    }
}
