using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace GuardianTerminal.UI
{
    // The emblem in the window (sidebar, welcome, About): assets\emblem.png when the build has it (a black and white
    // picture, built into the exe), else the drawn Guardian (Emblem.cs). The picture is used as a stencil: its light
    // parts take the colour asked for, its dark parts are see-through, so it sits on any background.
    internal static class Brand
    {
        private const string Resource = "GuardianTerminal.emblem.png";
        private static Bitmap stencil;
        private static bool loaded;
        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static bool HasPicture { get { return Stencil() != null; } }

        public static void Draw(Graphics g, Rectangle r, Color ink, Color background)
        {
            Bitmap s = Stencil();
            if (s == null)
            {
                Emblem.Draw(g, r, EmblemStyle.Sentinel, ink, background, false);
                return;
            }
            int size = Math.Min(r.Width, r.Height);
            g.DrawImage(Colored(size, ink), r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size);
        }

        // The emblem at a size, in a colour (cached)
        public static Bitmap Colored(int size, Color ink)
        {
            string key = size + "/" + ink.ToArgb();
            Bitmap b;
            if (cache.TryGetValue(key, out b)) return b;
            b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(Stencil(), new Rectangle(0, 0, size, size));
            }
            // its alpha now carries the shape: recolour every pixel to the ink
            BitmapData d = b.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int[] px = new int[size * size];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
            int rgb = ink.ToArgb() & 0xFFFFFF;
            for (int i = 0; i < px.Length; i++) px[i] = (int)((uint)px[i] & 0xFF000000) | rgb;
            System.Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length);
            b.UnlockBits(d);
            cache[key] = b;
            return b;
        }

        // The built-in picture as a white shape on transparent (light parts opaque; for a dark-on-light picture, dark
        // parts), or null
        private static Bitmap Stencil()
        {
            if (loaded) return stencil;
            loaded = true;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
                {
                    if (s == null) return null;
                    using (Bitmap src = new Bitmap(s))
                        stencil = ToStencil(src);
                }
            }
            catch (Exception) { stencil = null; }
            return stencil;
        }

        public static Bitmap ToStencil(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Bitmap o = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(o)) g.DrawImage(src, new Rectangle(0, 0, w, h));
            BitmapData d = o.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int[] px = new int[w * h];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
            // which way round: the corners are the background
            int corner = px[0];
            bool lightBackground = (corner >> 24 & 0xFF) > 128 && Luma(corner) > 128;
            for (int i = 0; i < px.Length; i++)
            {
                int a = px[i] >> 24 & 0xFF, l = Luma(px[i]);
                int shape = lightBackground ? 255 - l : l;
                px[i] = (a * shape / 255) << 24 | 0xFFFFFF;
            }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, d.Scan0, px.Length);
            o.UnlockBits(d);
            return o;
        }

        private static int Luma(int argb)
        {
            int r = argb >> 16 & 0xFF, g = argb >> 8 & 0xFF, b = argb & 0xFF;
            return (r * 299 + g * 587 + b * 114) / 1000;
        }
    }
}
