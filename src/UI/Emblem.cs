using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GuardianTerminal.UI
{
    internal enum EmblemStyle { Sentinel, Watcher, Glyph }

    // The emblem: a Guardian from Breath of the Wild, our own drawing in black and white (lines, curves and circles,
    // no image files), for the app icon, the header and the welcome screen. Its parts, as on the Guardian Stalker:
    // a small head with a knob on top and the single ringed eye; a big shell of a body under it with Sheikah spiral
    // lines; six ribbed legs arching out of the body and down to clawed feet. Laid out on a 200 x 200 grid; below 64
    // pixels it drops the fine detail, so it still reads at 16 pixels.
    //  - Sentinel: the whole Guardian, its eye firing, on a disc.
    //  - Watcher: the head and shell close up, the legs towering behind, on a rounded square.
    //  - Glyph: the whole Guardian as a plain silhouette, in a hexagon.
    internal static class Emblem
    {
        // badge: the emblem on its tile (in ink) with the Guardian in paper; otherwise the Guardian in ink on a
        // background of paper's colour (paper is used where the drawing cuts into itself)
        public static void Draw(Graphics g, RectangleF r, EmblemStyle style, Color ink, Color paper, bool badge)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = Math.Min(r.Width, r.Height) / 200f;
            g.TranslateTransform(r.X + (r.Width - 200 * s) / 2, r.Y + (r.Height - 200 * s) / 2);
            g.ScaleTransform(s, s);
            int detail = 200 * s >= 64 ? 2 : 200 * s >= 28 ? 1 : 0;
            Color fg = ink, bg = paper;
            if (badge)
            {
                using (SolidBrush b = new SolidBrush(ink))
                using (GraphicsPath tile = Tile(style))
                {
                    g.FillPath(b, tile);
                    g.SetClip(tile, CombineMode.Intersect);  // (legs and the laser run off the tile)
                }
                fg = paper;
                bg = ink;
            }
            switch (style)
            {
                case EmblemStyle.Sentinel:
                    Shrink(g, badge ? 0.74f : 0.9f, badge ? -4 : -6);
                    Guardian(g, fg, bg, detail, true, true);
                    break;
                case EmblemStyle.Watcher:
                    Watcher(g, fg, bg, detail);
                    break;
                default:
                    if (!badge)
                        using (Pen hex = new Pen(fg, detail == 2 ? 7f : 11f) { LineJoin = LineJoin.Miter })
                            g.DrawPolygon(hex, Hexagon(100, 100, 93));
                    Shrink(g, 0.7f, 6);
                    Guardian(g, fg, bg, detail, false, false);
                    break;
            }
            g.Restore(state);
        }

        public static GraphicsPath Tile(EmblemStyle style)
        {
            GraphicsPath p = new GraphicsPath();
            switch (style)
            {
                case EmblemStyle.Sentinel: p.AddEllipse(4, 4, 192, 192); break;
                case EmblemStyle.Watcher: RoundRect(p, new RectangleF(6, 6, 188, 188), 42); break;
                default: p.AddPolygon(Hexagon(100, 100, 98)); break;
            }
            return p;
        }

        // ------------------------------------------------------------------ the Guardian, front view
        // Its legs, left side (the right ones mirrored): hip, two curve controls, foot. The back pair arches
        // highest, the middle pair widest, the front pair comes down in front of the body.
        private static PointF[][] Legs(int detail)
        {
            PointF[] back = { P(70, 96), P(52, 30), P(14, 26), P(14, 100) };
            PointF[] middle = { P(60, 114), P(30, 64), P(-4, 92), P(6, 166) };
            PointF[] front = { P(74, 134), P(58, 144), P(40, 150), P(40, 186) };
            return detail == 0 ? new[] { middle, front } : new[] { back, middle, front };
        }

        // a leg: a ribbed tube along its curve, ending in a claw; halo: a gap of the background around it
        private static void Leg(Graphics g, PointF[] l, float width, Color fg, Color bg, int detail, float halo)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddBezier(l[0], l[1], l[2], l[3]);
                if (halo > 0)
                    using (Pen h = new Pen(bg, width + halo) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawPath(h, p);
                using (Pen tube = new Pen(fg, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawPath(tube, p);
                if (detail == 2)
                {
                    // the ribs: a thin cut across the tube every few units along it
                    p.Flatten(new Matrix(), 0.2f);
                    PointF[] pts = p.PathPoints;
                    float run = 0, step = width * 0.75f;
                    using (Pen rib = new Pen(bg, Math.Max(1.6f, width * 0.17f)))
                        for (int i = 1; i < pts.Length - 1; i++)
                        {
                            float dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (len < 0.0001f) continue;
                            run += len;
                            if (run < step || i < 3) continue;
                            run = 0;
                            float nx = -dy / len * (width / 2 + 0.6f), ny = dx / len * (width / 2 + 0.6f);
                            g.DrawLine(rib, pts[i].X - nx, pts[i].Y - ny, pts[i].X + nx, pts[i].Y + ny);
                        }
                }
            }
            // the claw: two sharp points spreading from the foot, along the way the leg arrives
            float ax = l[3].X - l[2].X, ay = l[3].Y - l[2].Y, al = (float)Math.Sqrt(ax * ax + ay * ay);
            ax /= al; ay /= al;
            foreach (float deg in new[] { -30f, 30f })
            {
                double a = deg * Math.PI / 180;
                float dx = (float)(ax * Math.Cos(a) - ay * Math.Sin(a)), dy = (float)(ax * Math.Sin(a) + ay * Math.Cos(a));
                PointF tip = new PointF(l[3].X + dx * width * 1.25f, l[3].Y + dy * width * 1.25f);
                PointF[] spike = Taper(l[3], tip, width * 0.62f);
                using (SolidBrush b = new SolidBrush(fg))
                    g.FillPolygon(b, spike);
            }
        }

        // a spike from a (width w) to a point at b
        private static PointF[] Taper(PointF a, PointF b, float w)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / len * w / 2, ny = dx / len * w / 2;
            return new[] { new PointF(a.X + nx, a.Y + ny), b, new PointF(a.X - nx, a.Y - ny) };
        }

        private const float BodyTop = 84, BodyBottom = 148, HeadBase = 92, HeadTop = 42;
        private static readonly PointF Eye = new PointF(100, 69);

        // laser: the eye firing, off to the upper right; detailed: the Sheikah lines on the shell
        private static void Guardian(Graphics g, Color fg, Color bg, int detail, bool laser, bool detailed)
        {
            float halo = detail == 2 ? 5 : 7;
            float legWidth = detail == 2 ? 11 : detail == 1 ? 15 : 19;
            using (SolidBrush ink = new SolidBrush(fg))
            using (SolidBrush paper = new SolidBrush(bg))
            using (Pen gap = new Pen(bg, halo) { LineJoin = LineJoin.Round })
            {
                foreach (PointF[] leg in Legs(detail))
                {
                    Leg(g, leg, legWidth, fg, bg, detail, halo);
                    Leg(g, Mirror(leg), legWidth, fg, bg, detail, halo);
                }

                // the shell: a broad rounded body
                using (GraphicsPath body = Shell())
                {
                    g.DrawPath(gap, body);
                    g.FillPath(ink, body);
                }
                if (detailed && detail >= 1)
                    using (Pen line = new Pen(bg, detail == 2 ? 3.2f : 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        // a band round the shell, and from it a line down each side curling outward
                        g.DrawBezier(line, P(58, 108), P(80, 116), P(120, 116), P(142, 108));
                        if (detail == 2)
                            foreach (int side in new[] { -1, 1 })
                                using (GraphicsPath p = new GraphicsPath())
                                {
                                    float x = 100 + side * 30;
                                    p.AddLine(x, 113, x, 126);
                                    p.AddLines(Spiral(x, 126, 7, 1.05f, side, false));
                                    g.DrawPath(line, p);
                                }
                    }

                // the head on it: a dome with a rim and a knob on top
                using (GraphicsPath head = Dome(66, HeadBase, 134, HeadTop))
                {
                    g.DrawPath(gap, head);
                    g.FillPath(ink, head);
                }
                RectangleF rim = new RectangleF(60, HeadBase - 6, 80, 12);
                g.DrawEllipse(gap, rim);
                g.FillEllipse(ink, rim);
                using (GraphicsPath knob = new GraphicsPath())
                {
                    RoundRect(knob, new RectangleF(91, HeadTop - 9, 18, 13), 5);
                    g.DrawPath(gap, knob);
                    g.FillPath(ink, knob);
                }

                // the eye: a socket, a ring, the lens
                float socket = detail == 0 ? 17 : 16;
                g.FillEllipse(paper, Eye.X - socket, Eye.Y - socket, 2 * socket, 2 * socket);
                if (detail >= 1)
                {
                    using (Pen ring = new Pen(fg, detail == 2 ? 4.5f : 6f))
                        g.DrawEllipse(ring, Eye.X - 10, Eye.Y - 10, 20, 20);
                    g.FillEllipse(ink, Eye.X - 4, Eye.Y - 4, 8, 8);
                }
                else g.FillEllipse(ink, Eye.X - 8, Eye.Y - 8, 16, 16);

                if (laser && detail == 2)
                    using (Pen beam = new Pen(fg, 3.5f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                    {
                        // the beam, from the edge of the socket up and away
                        float bx = 214 - Eye.X, by = -2 - Eye.Y, bl = (float)Math.Sqrt(bx * bx + by * by);
                        g.DrawLine(beam, Eye.X + bx / bl * (socket + 3), Eye.Y + by / bl * (socket + 3), 214, -2);
                    }
            }
        }

        private static GraphicsPath Shell()
        {
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(52, 118, 52, 94, 74, BodyTop, 100, BodyTop);
            p.AddBezier(100, BodyTop, 126, BodyTop, 148, 94, 148, 118);
            p.AddBezier(148, 118, 148, 140, 128, BodyBottom, 100, BodyBottom);
            p.AddBezier(100, BodyBottom, 72, BodyBottom, 52, 140, 52, 118);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------ Watcher: close up
        private static void Watcher(Graphics g, Color fg, Color bg, int detail)
        {
            // the same Guardian, close up on its head: the shell runs off the bottom, the legs off the sides
            using (GraphicsPath tile = Tile(EmblemStyle.Watcher))
                g.SetClip(tile, CombineMode.Intersect);
            g.TranslateTransform(100, 104);
            g.ScaleTransform(1.38f, 1.38f);
            g.TranslateTransform(-100, -84);
            Guardian(g, fg, bg, detail, false, true);
        }

        // A curl that carries on from a line arriving at (x, y) going outward (side -1: to the left): it turns up
        // from the bottom of its circle (up) or down from the top, round, and in towards its centre
        private static PointF[] Spiral(float x, float y, float r, float turns, int side, bool up)
        {
            float cy = up ? y - r : y + r;
            double start = up ? Math.PI / 2 : -Math.PI / 2, dir = up ? -side : side;
            List<PointF> pts = new List<PointF>();
            int n = (int)(48 * turns);
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                double a = start + dir * t * turns * 2 * Math.PI;
                float rr = r * (1 - 0.75f * t);
                pts.Add(new PointF(x + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr));
            }
            return pts.ToArray();
        }

        // ------------------------------------------------------------------ shapes
        // a dome from (left, baseY) over (center, top) to (right, baseY), closed along its base
        private static GraphicsPath Dome(float left, float baseY, float right, float top)
        {
            float c = (left + right) / 2, half = (right - left) / 2, h = baseY - top;
            GraphicsPath p = new GraphicsPath();
            p.AddBezier(left, baseY, left, baseY - h * 0.6f, c - half * 0.56f, top, c, top);
            p.AddBezier(c, top, c + half * 0.56f, top, right, baseY - h * 0.6f, right, baseY);
            p.CloseFigure();
            return p;
        }

        private static PointF[] Hexagon(float cx, float cy, float radius)
        {
            PointF[] pts = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 180 * (60 * i - 90);
                pts[i] = new PointF(cx + radius * (float)Math.Cos(a), cy + radius * (float)Math.Sin(a));
            }
            return pts;
        }

        public static void RoundRect(GraphicsPath p, RectangleF r, float radius)
        {
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
        }

        // scales what follows about the grid's centre, then moves it down by dy
        private static void Shrink(Graphics g, float k, float dy)
        {
            g.TranslateTransform(100, 100 + dy);
            g.ScaleTransform(k, k);
            g.TranslateTransform(-100, -100);
        }

        private static PointF P(float x, float y) { return new PointF(x, y); }

        private static PointF[] Mirror(PointF[] pts)
        {
            PointF[] m = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++) m[i] = new PointF(200 - pts[i].X, pts[i].Y);
            return m;
        }
    }
}
