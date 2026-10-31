using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using GuardianTerminal.UI;

// EmblemPreview <outDir>: a sheet per emblem style: the icon tile big, the emblem on the dark header and on white,
// and the icon at its real sizes (16-48 px), also shown 4x so the pixels can be judged.
internal static class EmblemPreview
{
    // export <dir>: small PNGs for the design mockups (each icon on its tile, and the header emblem)
    private static void Export(string dir)
    {
        foreach (EmblemStyle style in Enum.GetValues(typeof(EmblemStyle)))
        {
            string name = style.ToString().ToLowerInvariant();
            Save(Path.Combine(dir, "icon-" + name + ".png"), 128, (g, r) => Emblem.Draw(g, r, style, Color.Black, Color.White, true));
            Save(Path.Combine(dir, "head-" + name + ".png"), 56, (g, r) => Emblem.Draw(g, r, style, Color.White, Color.FromArgb(0x0B, 0x0F, 0x13), false));
            Save(Path.Combine(dir, "headlight-" + name + ".png"), 56, (g, r) => Emblem.Draw(g, r, style, Color.Black, Color.White, false));
        }
    }

    private static void Save(string path, int size, Action<Graphics, RectangleF> draw)
    {
        using (Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                draw(g, new RectangleF(0, 0, size, size));
            }
            b.Save(path, ImageFormat.Png);
        }
    }

    // options <file>: the three icons side by side, numbered, each with its real small sizes and the header use
    private static void Options(string file)
    {
        EmblemStyle[] styles = (EmblemStyle[])Enum.GetValues(typeof(EmblemStyle));
        using (Bitmap sheet = new Bitmap(1020, 520, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(sheet))
        using (Font title = new Font("Segoe UI Semibold", 15f))
        using (Font small = new Font("Segoe UI", 9.5f))
        using (SolidBrush dark = new SolidBrush(Color.FromArgb(0x0B, 0x0F, 0x13)))
        using (SolidBrush muted = new SolidBrush(Color.FromArgb(0x55, 0x5B, 0x63)))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            for (int i = 0; i < styles.Length; i++)
            {
                float x = 30 + i * 330;
                Emblem.Draw(g, new RectangleF(x + 20, 24, 250, 250), styles[i], Color.Black, Color.White, true);
                g.DrawString((i + 1) + "  " + styles[i], title, Brushes.Black, x + 20, 286);
                // its real sizes
                float sx = x + 20;
                foreach (int size in new[] { 48, 32, 24, 16 })
                {
                    using (Bitmap icon = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics ig = Graphics.FromImage(icon))
                        {
                            ig.Clear(Color.Transparent);
                            Emblem.Draw(ig, new RectangleF(0, 0, size, size), styles[i], Color.Black, Color.White, true);
                        }
                        g.DrawImageUnscaled(icon, (int)sx, 330 + (48 - size));
                    }
                    sx += size + 18;
                }
                g.DrawString("taskbar sizes", small, muted, x + 20, 384);
                // on the dark header
                g.FillRectangle(dark, x + 20, 410, 250, 84);
                Emblem.Draw(g, new RectangleF(x + 30, 418, 68, 68), styles[i], Color.White, Color.FromArgb(0x0B, 0x0F, 0x13), false);
                using (Font name = new Font("Segoe UI Semibold", 12.5f))
                    g.DrawString("Guardian Terminal", name, Brushes.White, x + 104, 440);
            }
            sheet.Save(file, ImageFormat.Png);
        }
    }

    private static int Main(string[] args)
    {
        if (args[0] == "export") { Directory.CreateDirectory(args[1]); Export(args[1]); return 0; }
        if (args[0] == "options") { Options(args[1]); return 0; }
        Directory.CreateDirectory(args[0]);
        foreach (EmblemStyle style in Enum.GetValues(typeof(EmblemStyle)))
        {
            using (Bitmap sheet = new Bitmap(1180, 560, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(0xF2, 0xF2, 0xF2));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // the icon, big
                Emblem.Draw(g, new RectangleF(30, 30, 380, 380), style, Color.Black, Color.White, true);
                // on the dark header, and on white, without its tile
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(0x0B, 0x0F, 0x13)))
                    g.FillRectangle(dark, 440, 30, 340, 250);
                Emblem.Draw(g, new RectangleF(495, 50, 230, 210), style, Color.White, Color.FromArgb(0x0B, 0x0F, 0x13), false);
                g.FillRectangle(Brushes.White, 800, 30, 350, 250);
                Emblem.Draw(g, new RectangleF(860, 50, 230, 210), style, Color.Black, Color.White, false);
                // the real icon sizes, and 4x
                int x = 440;
                foreach (int size in new[] { 16, 24, 32, 48 })
                {
                    using (Bitmap icon = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics ig = Graphics.FromImage(icon))
                        {
                            ig.Clear(Color.Transparent);
                            Emblem.Draw(ig, new RectangleF(0, 0, size, size), style, Color.Black, Color.White, true);
                        }
                        g.DrawImage(icon, x, 310);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(icon, new Rectangle(x, 370, size * 4, size * 4));
                        g.InterpolationMode = InterpolationMode.Default;
                        g.PixelOffsetMode = PixelOffsetMode.Default;
                        x += size * 4 + 28;
                    }
                }
                using (Font f = new Font("Segoe UI", 14f))
                    g.DrawString(style.ToString(), f, Brushes.Black, 30, 420);
                sheet.Save(Path.Combine(args[0], "emblem-" + style.ToString().ToLowerInvariant() + ".png"), ImageFormat.Png);
            }
        }
        return 0;
    }
}
