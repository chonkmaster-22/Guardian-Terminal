using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuardianTerminal.UI
{
    internal enum ButtonKind { Primary, Secondary, Ghost, Danger }

    // A flat button drawn in the theme: an optional icon glyph, then the text; rounded; hover and press states. It's
    // a dialog button too (DialogResult; a form's AcceptButton / CancelButton).
    internal sealed class FlatButton : Control, IButtonControl
    {
        private bool hover, down;
        public ButtonKind Kind = ButtonKind.Secondary;
        public string Glyph;
        public bool Compact;  // smaller text, for tool rows

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        public DialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            Form f = FindForm();
            if (DialogResult != DialogResult.None && f != null) f.DialogResult = DialogResult;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back, fore, border = Color.Empty;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    back = !Enabled ? Theme.Button : down ? Theme.Accent : hover ? Theme.AccentHover : Theme.Accent;
                    fore = Enabled ? Theme.AccentText : Theme.Faint;
                    break;
                case ButtonKind.Danger:
                    back = hover ? Theme.BadSoft : Theme.Button;
                    fore = Enabled ? Theme.Bad : Theme.Faint;
                    break;
                case ButtonKind.Ghost:
                    back = !Enabled ? Color.Transparent : down ? Theme.Border : hover ? Theme.ButtonHover : Color.Transparent;
                    fore = Enabled ? Theme.TextSoft : Theme.Faint;
                    border = Enabled ? Theme.Border : Theme.Divider;
                    break;
                default:
                    back = !Enabled ? Theme.Surface : down ? Theme.Border : hover ? Theme.ButtonHover : Theme.Button;
                    fore = Enabled ? Theme.Text : Theme.Faint;
                    break;
            }
            float r = Math.Min(Height / 2f, 7 * DeviceDpi / 96f);
            RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (back.A > 0) Theme.FillRounded(g, back, box, r);
            if (border != Color.Empty) Theme.DrawRounded(g, border, 1, box, r);
            if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, 1.5f, new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), r);

            Font font = Kind == ButtonKind.Primary && !Compact ? Theme.Big : Compact ? Theme.Small : Theme.Body;
            Size text = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding);
            int iconW = string.IsNullOrEmpty(Glyph) ? 0 : (int)(18 * DeviceDpi / 96f) + (Text.Length > 0 ? (int)(6 * DeviceDpi / 96f) : 0);
            int x = (Width - text.Width - iconW) / 2;
            if (iconW > 0)
                Theme.DrawIcon(g, Glyph, Kind == ButtonKind.Primary && !Compact ? Theme.IconFont : Theme.IconSmall, new Rectangle(x, 0, (int)(18 * DeviceDpi / 96f), Height), fore);
            TextRenderer.DrawText(g, Text, font, new Rectangle(x + iconW, 0, text.Width + 4, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        // Its natural width for its text and icon, with padding
        public int PreferredWidth(int padding)
        {
            using (Graphics g = CreateGraphics())
            {
                Font font = Kind == ButtonKind.Primary && !Compact ? Theme.Big : Compact ? Theme.Small : Theme.Body;
                int w = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (!string.IsNullOrEmpty(Glyph)) w += (int)(24 * DeviceDpi / 96f);
                return w + 2 * padding;
            }
        }
    }

    // An on / off switch, drawn as a pill with a knob
    internal sealed class ToggleSwitch : Control
    {
        private bool on, hover;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(36, 20);
        }

        [DefaultValue(false)]
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        // Sets it without raising CheckedChanged (when showing a state, not changing it)
        public void Show(bool value) { on = value; Invalidate(); }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(1, 1, Width - 3, Height - 3);
            Color back = !Enabled ? Theme.Divider : on ? (hover ? Theme.AccentHover : Theme.Accent) : (hover ? Theme.ButtonHover : Theme.Border);
            Theme.FillRounded(g, back, track, track.Height / 2);
            float k = track.Height - 6;
            float x = on ? track.Right - 3 - k : track.X + 3;
            using (SolidBrush knob = new SolidBrush(Enabled ? Color.White : Theme.Muted))
                g.FillEllipse(knob, x, track.Y + 3, k, k);
            if (Focused && ShowFocusCues) Theme.DrawRounded(g, Theme.AccentHover, 1, track, track.Height / 2);
        }
    }

    // An item of the sidebar: an icon glyph and a name; the page that's open is highlighted
    internal sealed class NavItem : Control
    {
        private bool hover, selected;
        public string Glyph;
        public string Badge;  // a small count on the right (updates, ...), or null

        public NavItem()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
        }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Sidebar);
            float s = DeviceDpi / 96f;
            if (selected || hover)
                Theme.FillRounded(g, selected ? Theme.SurfaceSelected : Theme.Surface, new RectangleF(0, 0, Width - 1, Height - 1), 7 * s);
            if (selected)
                Theme.FillRounded(g, Theme.Accent, new RectangleF(0, Height * 0.25f, 3 * s, Height * 0.5f), 1.5f * s);
            Color fore = selected ? Color.White : Theme.TextSoft;
            Theme.DrawIcon(g, Glyph, Theme.IconFont, new Rectangle((int)(10 * s), 0, (int)(22 * s), Height), selected ? Theme.Accent : Theme.Muted);
            TextRenderer.DrawText(g, Text, selected ? Theme.BodyBold : Theme.Body, new Rectangle((int)(40 * s), 0, Width - (int)(44 * s), Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Badge))
            {
                Size sz = TextRenderer.MeasureText(Badge, Theme.SmallBold);
                RectangleF b = new RectangleF(Width - sz.Width - 14 * s, (Height - 18 * s) / 2, sz.Width + 6 * s, 18 * s);
                Theme.FillRounded(g, Theme.Accent, b, 9 * s);
                TextRenderer.DrawText(g, Badge, Theme.SmallBold, Rectangle.Round(b), Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // A search box: a magnifier, a text box without a border, a hint while it's empty
    internal sealed class SearchBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        private readonly string hint;
        public event EventHandler SearchChanged;

        public SearchBox(string hint)
        {
            this.hint = hint;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Window;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Surface;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Body;
            Box.TextChanged += delegate { Invalidate(); if (SearchChanged != null) SearchChanged(this, EventArgs.Empty); };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) { Box.Text = ""; e.SuppressKeyPress = true; } };
            Box.HandleCreated += delegate { NativeMethods.SetCueBanner(Box.Handle, hint); };
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
            Click += delegate { Box.Focus(); };
        }

        public string Query { get { return Box.Text.Trim(); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float s = DeviceDpi / 96f;
            Box.SetBounds((int)(32 * s), (Height - Box.PreferredHeight) / 2, Math.Max(10, Width - (int)(40 * s)), Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            Theme.FillRounded(g, Theme.Surface, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7 * s);
            Theme.DrawRounded(g, Box.Focused ? Theme.Accent : Theme.Border, 1, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7 * s);
            Theme.DrawIcon(g, Theme.IconSearch, Theme.IconSmall, new Rectangle((int)(8 * s), 0, (int)(20 * s), Height), Theme.Muted);
        }
    }

    // A rounded panel (a section of a page)
    internal class Card : Panel
    {
        public Color Fill = Theme.Surface, Outline = Color.Empty;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            Theme.FillRounded(g, Fill, new RectangleF(0, 0, Width - 1, Height - 1), 10 * s);
            if (Outline != Color.Empty) Theme.DrawRounded(g, Outline, 1, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 10 * s);
        }
    }

    // A ListBox drawn in the theme: each row has a title and a muted line under it (item: a Row)
    internal sealed class DarkList : ListBox
    {
        public sealed class Row
        {
            public string Title, Detail, Badge;
            public Color BadgeColor = Theme.Accent;
            public object Tag;
            public bool Checked;
            public override string ToString() { return Title; }
        }

        // CheckBoxes: each row has a box, toggled by clicking it (or the row) and with Space
        public bool CheckBoxes;
        public event EventHandler CheckedChanged;

        public DarkList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            IntegralHeight = false;
            ItemHeight = (int)(46 * DeviceDpi / 96f);
            HandleCreated += delegate { NativeMethods.SetWindowTheme(Handle, "DarkMode_Explorer", null); };
        }

        public System.Collections.Generic.List<Row> CheckedRows
        {
            get { System.Collections.Generic.List<Row> r = new System.Collections.Generic.List<Row>(); foreach (object o in Items) { Row x = o as Row; if (x != null && x.Checked) r.Add(x); } return r; }
        }

        public void SetAll(bool on)
        {
            foreach (object o in Items) { Row x = o as Row; if (x != null) x.Checked = on; }
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!CheckBoxes) return;
            int i = IndexFromPoint(e.Location);
            if (i < 0) return;
            Row r = Items[i] as Row;
            if (r == null) return;
            r.Checked = !r.Checked;
            Invalidate(GetItemRectangle(i));
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (CheckBoxes && e.KeyCode == Keys.Space && SelectedItem is Row)
            {
                Row r = (Row)SelectedItem;
                r.Checked = !r.Checked;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Row r = Items[e.Index] as Row;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? Theme.SurfaceSelected : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (SolidBrush b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
            float s = DeviceDpi / 96f;
            int pad = (int)(14 * s);
            if (CheckBoxes && r != null)
            {
                int box = (int)(18 * s);
                Rectangle cb = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + (e.Bounds.Height - box) / 2, box, box);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (r.Checked)
                {
                    Theme.FillRounded(e.Graphics, Theme.Accent, cb, 4 * s);
                    using (Pen p = new Pen(Theme.AccentText, 2 * s))
                        e.Graphics.DrawLines(p, new[] { new PointF(cb.X + box * 0.24f, cb.Y + box * 0.52f), new PointF(cb.X + box * 0.43f, cb.Y + box * 0.72f), new PointF(cb.X + box * 0.78f, cb.Y + box * 0.30f) });
                }
                else Theme.DrawRounded(e.Graphics, Theme.Muted, 1.2f * s, cb, 4 * s);
                pad += box + (int)(12 * s);
            }
            int badgeW = 0;
            if (r != null && !string.IsNullOrEmpty(r.Badge))
            {
                Size sz = TextRenderer.MeasureText(r.Badge, Theme.SmallBold);
                badgeW = sz.Width + (int)(12 * s);
                Rectangle br = new Rectangle(e.Bounds.Right - badgeW - pad, e.Bounds.Y + (e.Bounds.Height - (int)(20 * s)) / 2, badgeW, (int)(20 * s));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.FillRounded(e.Graphics, Theme.AccentSoft, br, 5 * s);
                TextRenderer.DrawText(e.Graphics, r.Badge, Theme.SmallBold, br, r.BadgeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Rectangle t = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + (int)(5 * s), e.Bounds.Width - 2 * pad - badgeW, e.Bounds.Height / 2);
            TextRenderer.DrawText(e.Graphics, r != null ? r.Title : Convert.ToString(Items[e.Index]), Theme.BodyBold, t, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (r != null && !string.IsNullOrEmpty(r.Detail))
            {
                Rectangle d = new Rectangle(t.X, e.Bounds.Y + e.Bounds.Height / 2, t.Width, e.Bounds.Height / 2 - (int)(5 * s));
                TextRenderer.DrawText(e.Graphics, r.Detail, Theme.Small, d, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            using (Pen p = new Pen(Theme.Divider)) e.Graphics.DrawLine(p, e.Bounds.X + pad, e.Bounds.Bottom - 1, e.Bounds.Right - pad, e.Bounds.Bottom - 1);
        }
    }
}
