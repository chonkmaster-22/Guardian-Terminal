using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using GuardianTerminal.UI;

// Draws the app icon and writes it as a multi-size .ico with PNG frames: the emblem picture (assets\emblem.png, white
// on a black rounded tile) when there is one, else the drawn Guardian (src\UI\Emblem.cs).
// usage: MakeIcon <out.ico> [<emblem.png> | none] [<preview.png>]
internal static class MakeIcon
{
    private static int Main(string[] args)
    {
        Bitmap stencil = args.Length > 1 && File.Exists(args[1]) ? Brand.ToStencil(new Bitmap(args[1])) : null;
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int s in sizes) pngs.Add(Png(Render(s, stencil)));
        using (FileStream fs = File.Create(args[0]))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }
        if (args.Length > 2) File.WriteAllBytes(args[2], Png(Render(512, stencil)));
        return 0;
    }

    private static Bitmap Render(int size, Bitmap stencil)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            if (stencil == null)
            {
                Emblem.Draw(g, new RectangleF(0, 0, size, size), EmblemStyle.Sentinel, Color.Black, Color.White, true);
                return bmp;
            }
            float k = size / 256f;
            using (GraphicsPath tile = Rounded(new RectangleF(8 * k, 8 * k, 240 * k, 240 * k), 52 * k))
                g.FillPath(Brushes.Black, tile);
            // the emblem in white, a little inside the tile (more of the tile at the small sizes, so it reads)
            float inset = size <= 24 ? 0.10f : 0.16f;
            RectangleF r = new RectangleF(size * inset, size * inset, size * (1 - 2 * inset), size * (1 - 2 * inset));
            g.DrawImage(stencil, r);
        }
        return bmp;
    }

    private static byte[] Png(Bitmap b)
    {
        using (b)
        using (MemoryStream ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
