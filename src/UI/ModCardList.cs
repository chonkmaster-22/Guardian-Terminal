using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GuardianTerminal.Mods;

namespace GuardianTerminal.UI
{
    internal sealed class Badge
    {
        public string Text;
        public Color Fore, Back;
        public Badge(string text, Color fore, Color back) { Text = text; Fore = fore; Back = back; }
    }

    // The profile's mods as cards, in load order (the later ones win): each with its on / off switch, its place in
    // the order, its picture (cards view) and what's worth knowing at a glance. Drag a card to move it; click the
    // switch to turn it on or off. Only the cards in view are drawn, so long lists stay quick.
    internal sealed class ModCardList : Control
    {
        private List<InstalledMod> mods = new List<InstalledMod>();
        private List<InstalledMod> view = new List<InstalledMod>();
        private string filter = "";
        private bool cards = true;
        private int offset, hoverIndex = -1;
        private InstalledMod selected, anchor;
        private readonly HashSet<InstalledMod> many = new HashSet<InstalledMod>();  // the selection (Ctrl / Shift click)
        private Func<InstalledMod, bool> show = m => true;                         // the filter chips' choice
        private readonly Dictionary<string, Image> thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        // dragging a card
        private Point downAt;
        private int downIndex = -1, dropIndex = -1;
        private bool dragging;
        // dragging the scrollbar
        private bool scrolling;
        private int scrollGrab;
        private readonly Timer edgeScroll = new Timer { Interval = 40 };

        public event EventHandler SelectionChanged;
        public event Action<InstalledMod> ToggleRequested;
        public event Action<InstalledMod, int> MoveRequested;     // to this place in the profile's order
        public event Action<InstalledMod> OpenRequested;
        public event Action<InstalledMod> DeleteRequested;
        public Func<InstalledMod, string> Subtitle = m => "";
        public Func<InstalledMod, List<Badge>> Badges = m => new List<Badge>();
        public ContextMenuStrip ItemMenu;

        public ModCardList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            BackColor = Theme.Window;
            TabStop = true;
            edgeScroll.Tick += delegate { EdgeScroll(); };
        }

        private float Sc { get { return DeviceDpi / 96f; } }
        private int S(float px) { return (int)Math.Round(px * Sc); }
        private int RowHeight { get { return S(cards ? 78 : 54); } }
        private int Gap { get { return S(8); } }
        private int Pad { get { return S(12); } }
        private int ContentHeight { get { return view.Count * (RowHeight + Gap) + Pad; } }
        private int MaxOffset { get { return Math.Max(0, ContentHeight - Height); } }

        public bool Cards { get { return cards; } set { cards = value; ClampOffset(); Invalidate(); } }
        public InstalledMod Selected { get { return selected; } }
        public int Count { get { return view.Count; } }
        public bool Filtered { get { return filter.Length > 0 || chipFiltered; } }
        private bool chipFiltered;

        // Every mod selected, in load order (one, or several with Ctrl / Shift click)
        public List<InstalledMod> SelectedMods
        {
            get { return mods.Where(m => many.Contains(m) && view.Contains(m)).ToList(); }
        }

        public void SetPredicate(Func<InstalledMod, bool> predicate, bool filtering)
        {
            show = predicate ?? (m => true);
            chipFiltered = filtering;
            Rebuild();
            if (selected != null && !view.Contains(selected)) Select(view.FirstOrDefault(), false);
            Invalidate();
        }

        public void SelectAll()
        {
            many.Clear();
            foreach (InstalledMod m in view) many.Add(m);
            if (selected == null) selected = view.FirstOrDefault();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void SetMods(List<InstalledMod> list, InstalledMod select)
        {
            mods = list;
            Rebuild();
            if (select != null && view.Contains(select)) Select(select, true);
            else if (selected == null || !view.Contains(selected)) Select(view.FirstOrDefault(), false);
            Invalidate();
        }

        public void SetFilter(string text)
        {
            filter = (text ?? "").Trim();
            Rebuild();
            if (selected != null && !view.Contains(selected)) Select(view.FirstOrDefault(), false);
            Invalidate();
        }

        private void Rebuild()
        {
            view = mods.Where(m => show(m) && (filter.Length == 0 || Matches(m, filter))).ToList();
            many.RemoveWhere(m => !mods.Contains(m));
            ClampOffset();
        }

        private static bool Matches(InstalledMod m, string q)
        {
            return Contains(m.DisplayName, q) || Contains(m.Info.Author, q) || Contains(m.Info.Version, q) || Contains(m.Info.Description, q);
        }

        private static bool Contains(string s, string q)
        {
            return s != null && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Selects one mod (the others are deselected)
        public void Select(InstalledMod m, bool scrollTo)
        {
            bool changed = selected != m || many.Count != (m == null ? 0 : 1) || m != null && !many.Contains(m);
            selected = anchor = m;
            many.Clear();
            if (m != null) many.Add(m);
            if (changed && SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            if (scrollTo && m != null) ScrollTo(view.IndexOf(m));
            Invalidate();
        }

        // Ctrl click: adds or removes one; Shift click: everything from the last one clicked to this one
        private void SelectMore(InstalledMod m, bool range)
        {
            if (range && anchor != null && view.Contains(anchor))
            {
                int a = view.IndexOf(anchor), b = view.IndexOf(m);
                many.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) many.Add(view[i]);
            }
            else
            {
                if (!many.Add(m)) many.Remove(m);
                anchor = m;
            }
            selected = many.Contains(m) ? m : many.FirstOrDefault();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void ForgetThumbnail(InstalledMod m)
        {
            string path = ThumbPath(m);
            Image img;
            if (path != null && thumbs.TryGetValue(path, out img)) { if (img != null) img.Dispose(); thumbs.Remove(path); }
        }

        private void ScrollTo(int index)
        {
            if (index < 0) return;
            int top = Pad + index * (RowHeight + Gap), bottom = top + RowHeight;
            if (top < offset) offset = top - Pad;
            else if (bottom > offset + Height) offset = bottom - Height + Pad;
            ClampOffset();
        }

        private void ClampOffset()
        {
            offset = Math.Max(0, Math.Min(offset, MaxOffset));
        }

        private Rectangle RowRect(int i)
        {
            int right = MaxOffset > 0 ? S(10) : 0;
            return new Rectangle(Pad, Pad + i * (RowHeight + Gap) - offset, Width - 2 * Pad - right, RowHeight);
        }

        private Rectangle ToggleRect(Rectangle row)
        {
            return new Rectangle(row.X + S(38), row.Y + (row.Height - S(20)) / 2, S(36), S(20));
        }

        private int IndexAt(Point p)
        {
            int i = (p.Y + offset - Pad) / (RowHeight + Gap);
            if (i < 0 || i >= view.Count) return -1;
            return RowRect(i).Contains(p) ? i : -1;
        }

        // ------------------------------------------------------------------ painting
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int first = Math.Max(0, (offset - Pad) / (RowHeight + Gap)), last = Math.Min(view.Count - 1, (offset + Height) / (RowHeight + Gap) + 1);
            for (int i = first; i <= last; i++) PaintRow(g, i, view[i]);
            if (dragging && dropIndex >= 0)
            {
                int y = dropIndex < view.Count ? RowRect(dropIndex).Y - Gap / 2 : RowRect(view.Count - 1).Bottom + Gap / 2;
                using (Pen p = new Pen(Theme.Accent, S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, Pad + S(4), y, Width - Pad - S(14), y);
            }
            if (MaxOffset > 0)
            {
                Rectangle thumb = ScrollThumb();
                Theme.FillRounded(g, scrolling ? Theme.Muted : Theme.Border, thumb, thumb.Width / 2f);
            }
        }

        private Rectangle ScrollThumb()
        {
            int track = Height - 2 * S(6);
            int h = Math.Max(S(30), (int)((long)track * Height / Math.Max(1, ContentHeight)));
            int y = S(6) + (int)((long)(track - h) * offset / Math.Max(1, MaxOffset));
            return new Rectangle(Width - S(8), y, S(5), h);
        }

        private void PaintRow(Graphics g, int i, InstalledMod m)
        {
            Rectangle r = RowRect(i);
            bool sel = many.Contains(m), hover = i == hoverIndex && !dragging;
            bool beingDragged = dragging && i == downIndex;
            Theme.FillRounded(g, sel ? Theme.SurfaceSelected : hover ? Theme.SurfaceHover : Theme.Surface, r, S(10));
            if (sel) Theme.DrawRounded(g, Theme.Accent, Math.Max(1, Sc), new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), S(10));
            if (beingDragged) Theme.DrawRounded(g, Theme.AccentHover, S(2), r, S(10));

            // its place in the order, its switch
            int place = mods.IndexOf(m) + 1;
            TextRenderer.DrawText(g, place.ToString(), Theme.SmallBold, new Rectangle(r.X + S(6), r.Y, S(30), r.Height), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Rectangle t = ToggleRect(r);
            Theme.FillRounded(g, m.Enabled ? Theme.Accent : Theme.Border, t, t.Height / 2f);
            float k = t.Height - S(6);
            using (SolidBrush knob = new SolidBrush(Color.White))
                g.FillEllipse(knob, m.Enabled ? t.Right - S(3) - k : t.X + S(3), t.Y + S(3), k, k);

            int x = t.Right + S(14);
            if (cards)
            {
                Rectangle pic = new Rectangle(x, r.Y + S(10), S(104), r.Height - S(20));
                PaintPicture(g, m, pic);
                x = pic.Right + S(14);
            }

            // badges on the right
            int right = r.Right - S(12);
            foreach (Badge b in Badges(m).AsEnumerable().Reverse())
            {
                Size sz = TextRenderer.MeasureText(b.Text, Theme.SmallBold);
                Rectangle br = new Rectangle(right - sz.Width - S(12), r.Y + (r.Height - S(22)) / 2, sz.Width + S(12), S(22));
                Theme.FillRounded(g, b.Back, br, S(6));
                TextRenderer.DrawText(g, b.Text, Theme.SmallBold, br, b.Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                right = br.X - S(6);
            }

            Color name = m.Enabled ? Theme.Text : Theme.TextSoft;
            int textH = r.Height / 2;
            TextRenderer.DrawText(g, m.DisplayName, cards ? Theme.Big : Theme.BodyBold, new Rectangle(x, r.Y + S(cards ? 10 : 6), right - x - S(8), textH - S(4)), name,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Subtitle(m), Theme.Small, new Rectangle(x, r.Y + textH + S(2), right - x - S(8), textH - S(cards ? 12 : 8)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void PaintPicture(Graphics g, InstalledMod m, Rectangle pic)
        {
            using (GraphicsPath clip = Theme.Rounded(pic, S(6)))
            {
                Image img = Thumb(m);
                Region old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                if (img != null)
                {
                    // fill the frame, cropping the picture's longer side
                    float scale = Math.Max((float)pic.Width / img.Width, (float)pic.Height / img.Height);
                    float w = img.Width * scale, h = img.Height * scale;
                    g.DrawImage(img, pic.X + (pic.Width - w) / 2, pic.Y + (pic.Height - h) / 2, w, h);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Sunken)) g.FillRectangle(b, pic);
                    int e = pic.Height - S(10);
                    Brand.Draw(g, new Rectangle(pic.X + (pic.Width - e) / 2, pic.Y + S(5), e, e), Theme.Border, Theme.Sunken);
                }
                g.Clip = old;
            }
        }

        private static string ThumbPath(InstalledMod m)
        {
            return string.IsNullOrEmpty(m.Info.Thumbnail) ? null : Path.Combine(AppPaths.Thumbnails, m.Info.Thumbnail);
        }

        private Image Thumb(InstalledMod m)
        {
            string path = ThumbPath(m);
            if (path == null) return null;
            Image img;
            if (thumbs.TryGetValue(path, out img)) return img;
            img = null;
            try
            {
                if (File.Exists(path))
                    using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(path)))  // (not keeping the file open)
                    using (Image raw = Image.FromStream(ms))
                    {
                        // kept small: the card's frame at twice its size
                        int w = S(208), h = Math.Max(1, raw.Height * w / Math.Max(1, raw.Width));
                        Bitmap small = new Bitmap(w, h);
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(raw, 0, 0, w, h);
                        }
                        img = small;
                    }
            }
            catch (Exception) { img = null; }
            thumbs[path] = img;
            return img;
        }

        // ------------------------------------------------------------------ mouse and keys
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (MaxOffset > 0 && e.X >= Width - S(12))
            {
                Rectangle thumb = ScrollThumb();
                if (thumb.Contains(e.X - S(2), e.Y) || thumb.Contains(e.Location)) { scrolling = true; scrollGrab = e.Y - thumb.Y; Capture = true; }
                else { offset += e.Y < thumb.Y ? -Height : Height; ClampOffset(); }
                Invalidate();
                return;
            }
            int i = IndexAt(e.Location);
            if (i < 0) return;
            InstalledMod m = view[i];
            if (e.Button == MouseButtons.Left && ToggleRect(RowRect(i)).Contains(e.Location))
            {
                if (!many.Contains(m)) Select(m, false);
                if (ToggleRequested != null) ToggleRequested(m);
                return;
            }
            if (e.Button == MouseButtons.Left && (ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
            {
                SelectMore(m, (ModifierKeys & Keys.Shift) != 0);
                return;
            }
            // (a right click on one of several selected keeps them all, for the menu)
            if (!(e.Button == MouseButtons.Right && many.Contains(m))) Select(m, false);
            if (e.Button == MouseButtons.Right && ItemMenu != null) { ItemMenu.Show(this, e.Location); return; }
            if (e.Button == MouseButtons.Left && e.Clicks == 2) { if (OpenRequested != null) OpenRequested(m); return; }
            if (e.Button == MouseButtons.Left) { downAt = e.Location; downIndex = i; }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scrolling)
            {
                int track = Height - 2 * S(6), h = ScrollThumb().Height;
                offset = (int)((long)(e.Y - scrollGrab - S(6)) * MaxOffset / Math.Max(1, track - h));
                ClampOffset();
                Invalidate();
                return;
            }
            if (downIndex >= 0 && e.Button == MouseButtons.Left && !Filtered)
            {
                if (!dragging && (Math.Abs(e.Y - downAt.Y) > S(6) || Math.Abs(e.X - downAt.X) > S(6))) { dragging = true; Cursor = Cursors.SizeNS; edgeScroll.Start(); }
                if (dragging)
                {
                    int slot = (e.Y + offset - Pad + (RowHeight + Gap) / 2) / (RowHeight + Gap);
                    dropIndex = Math.Max(0, Math.Min(view.Count, slot));
                    Invalidate();
                    return;
                }
            }
            int h2 = IndexAt(e.Location);
            if (h2 != hoverIndex) { hoverIndex = h2; Invalidate(); }
            Cursor = h2 >= 0 && ToggleRect(RowRect(h2)).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (scrolling) { scrolling = false; Capture = false; Invalidate(); return; }
            if (dragging && downIndex >= 0 && dropIndex >= 0)
            {
                InstalledMod m = view[downIndex];
                int to = dropIndex > downIndex ? dropIndex - 1 : dropIndex;
                if (to != downIndex && MoveRequested != null) MoveRequested(m, to);
            }
            dragging = false;
            downIndex = dropIndex = -1;
            edgeScroll.Stop();
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            offset -= e.Delta * (RowHeight + Gap) / 120;
            ClampOffset();
            Invalidate();
            base.OnMouseWheel(e);
        }

        private void EdgeScroll()
        {
            if (!dragging) { edgeScroll.Stop(); return; }
            Point p = PointToClient(MousePosition);
            int step = p.Y < S(30) ? -S(14) : p.Y > Height - S(30) ? S(14) : 0;
            if (step == 0) return;
            offset += step;
            ClampOffset();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            ClampOffset();
            base.OnResize(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int i = selected == null ? -1 : view.IndexOf(selected), n = view.Count;
            if (n == 0) return;
            if (e.Control && e.KeyCode == Keys.A) { SelectAll(); e.Handled = true; return; }
            int page = Math.Max(1, Height / (RowHeight + Gap));
            int to = i;
            switch (e.KeyCode)
            {
                case Keys.Up:
                    if (e.Alt && selected != null && !Filtered && i > 0) { if (MoveRequested != null) MoveRequested(selected, i - 1); e.Handled = true; return; }
                    to = Math.Max(0, i - 1); break;
                case Keys.Down:
                    if (e.Alt && selected != null && !Filtered && i < n - 1) { if (MoveRequested != null) MoveRequested(selected, i + 1); e.Handled = true; return; }
                    to = Math.Min(n - 1, i + 1); break;
                case Keys.Home: to = 0; break;
                case Keys.End: to = n - 1; break;
                case Keys.PageUp: to = Math.Max(0, i - page); break;
                case Keys.PageDown: to = Math.Min(n - 1, i + page); break;
                case Keys.Space:
                    if (selected != null && ToggleRequested != null) ToggleRequested(selected);
                    e.Handled = true; return;
                case Keys.Delete:
                    if (selected != null && DeleteRequested != null) DeleteRequested(selected);
                    e.Handled = true; return;
                case Keys.Enter:
                    if (selected != null && OpenRequested != null) OpenRequested(selected);
                    e.Handled = true; return;
                default: base.OnKeyDown(e); return;
            }
            if (to >= 0 && to < n) Select(view[to], true);
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                edgeScroll.Dispose();
                foreach (Image img in thumbs.Values) if (img != null) img.Dispose();
                thumbs.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
