using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuardianTerminal.UI
{
    // Guardian Terminal's look: dark slate surfaces, Sheikah blue for what's on and what to press, amber and red for
    // warnings and problems. Icons are glyphs of Windows' own icon font (Segoe MDL2 Assets).
    internal static class Theme
    {
        public static readonly Color Window = Color.FromArgb(0x11, 0x16, 0x1C);
        public static readonly Color Sidebar = Color.FromArgb(0x0B, 0x0F, 0x13);
        public static readonly Color Header = Color.FromArgb(0x0B, 0x0F, 0x13);
        public static readonly Color Footer = Color.FromArgb(0x0E, 0x13, 0x18);
        public static readonly Color Surface = Color.FromArgb(0x17, 0x1E, 0x26);
        public static readonly Color SurfaceHover = Color.FromArgb(0x1C, 0x25, 0x2F);
        public static readonly Color SurfaceSelected = Color.FromArgb(0x1D, 0x28, 0x33);
        public static readonly Color SurfaceHeader = Color.FromArgb(0x1D, 0x25, 0x2F);
        public static readonly Color Sunken = Color.FromArgb(0x0E, 0x13, 0x18);
        public static readonly Color Border = Color.FromArgb(0x2A, 0x34, 0x40);
        public static readonly Color Divider = Color.FromArgb(0x22, 0x2C, 0x37);
        public static readonly Color Text = Color.FromArgb(0xE6, 0xED, 0xF3);
        public static readonly Color TextSoft = Color.FromArgb(0xAE, 0xB9, 0xC4);
        public static readonly Color Muted = Color.FromArgb(0x8B, 0x98, 0xA5);
        public static readonly Color Faint = Color.FromArgb(0x5C, 0x69, 0x76);
        public static readonly Color Accent = Color.FromArgb(0x2C, 0xC3, 0xD3);
        public static readonly Color AccentHover = Color.FromArgb(0x4F, 0xD6, 0xE3);
        public static readonly Color AccentText = Color.FromArgb(0x06, 0x1A, 0x1E);
        public static readonly Color AccentSoft = Color.FromArgb(0x14, 0x30, 0x3A);
        public static readonly Color AccentSoftText = Color.FromArgb(0x7F, 0xD8, 0xE3);
        public static readonly Color Button = Color.FromArgb(0x22, 0x2B, 0x35);
        public static readonly Color ButtonHover = Color.FromArgb(0x2D, 0x39, 0x46);
        public static readonly Color Good = Color.FromArgb(0x4C, 0xC3, 0x8A);
        public static readonly Color Warn = Color.FromArgb(0xE3, 0xB3, 0x41);
        public static readonly Color WarnSoft = Color.FromArgb(0x3A, 0x2F, 0x14);
        public static readonly Color Bad = Color.FromArgb(0xF0, 0x71, 0x67);
        public static readonly Color BadSoft = Color.FromArgb(0x3D, 0x1C, 0x1A);

        public static readonly Font Body = new Font("Segoe UI", 9.75F);
        public static readonly Font BodyBold = new Font("Segoe UI Semibold", 9.75F);
        public static readonly Font Small = new Font("Segoe UI", 8.75F);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.75F);
        public static readonly Font Title = new Font("Segoe UI Semibold", 15F);
        public static readonly Font PageTitle = new Font("Segoe UI Semibold", 17F);
        public static readonly Font Big = new Font("Segoe UI Semibold", 12F);
        public static readonly Font Brand = new Font("Segoe UI Semibold", 11.5F);

        // Segoe MDL2 Assets glyphs
        public const string IconMods = "", IconProfiles = "", IconBrowse = "", IconSaves = "", IconTools = "",
            IconSettings = "", IconAbout = "", IconSearch = "", IconAdd = "", IconPlay = "", IconMerge = "",
            IconFolder = "", IconDelete = "", IconUpdate = "", IconLink = "", IconCards = "", IconRows = "",
            IconGame = "", IconHistory = "", IconCheck = "", IconClose = "", IconDown = "", IconWarn = "",
            IconFile = "", IconSort = "", IconShare = "", IconOpen = "";

        private static Font iconFont, iconSmall, iconBig;
        public static Font IconFont { get { return iconFont ?? (iconFont = new Font("Segoe MDL2 Assets", 11F)); } }
        public static Font IconSmall { get { return iconSmall ?? (iconSmall = new Font("Segoe MDL2 Assets", 9F)); } }
        public static Font IconBig { get { return iconBig ?? (iconBig = new Font("Segoe MDL2 Assets", 20F)); } }

        // A glyph, centred in r
        public static void DrawIcon(Graphics g, string glyph, Font font, Rectangle r, Color color)
        {
            TextRenderer.DrawText(g, glyph, font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, Color c, RectangleF r, float radius)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRounded(Graphics g, Color c, float width, RectangleF r, float radius)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (Pen pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }

        public static FlatButton MakeButton(string text, bool primary)
        {
            return new FlatButton { Text = text, Kind = primary ? ButtonKind.Primary : ButtonKind.Secondary };
        }

        public static ToolStripRenderer MenuRenderer()
        {
            return new ToolStripProfessionalRenderer(new DarkMenuColors());
        }

        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip { Renderer = MenuRenderer(), ForeColor = Text, ShowImageMargin = false, Font = Body };
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return ButtonHover; } }
            public override Color MenuItemSelected { get { return ButtonHover; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
            public override Color CheckBackground { get { return Accent; } }
            public override Color CheckSelectedBackground { get { return AccentHover; } }
            public override Color CheckPressedBackground { get { return Accent; } }
        }
    }
}
