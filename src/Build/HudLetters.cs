using System;
using System.Collections.Generic;
using System.Text;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    // Savage Wind's HUD button icons (Alpha 4 on; hud_letters_patch in runtime/services/fs.c), for a Pack/Bootup.pack
    // that comes from a mod: Savage Wind patches only the game's own pack, so a modded one would show the Switch's
    // A / B / X / Y letters around the HUD's prompts instead of the controller's buttons. The same patch is made here:
    // in Layout/Common.sblarc, the four PaDoCommand layouts get the icon font as a second font and their letter panes
    // the icon characters (U+E040..E043, which Savage Wind draws for the device in use); the patched layouts go at the
    // end of the archive, the archive is recompressed into the pack where it was, and what follows moves by a multiple
    // of 0x2000 (so every file keeps its alignment). Anything not as expected leaves the pack alone.
    internal static class HudLetters
    {
        private const float IconScale = 1.5f;
        private const string IconFont = "External_00.bffnt";
        private static readonly string[] Layouts = {
            "blyt/PaDoCommandA_00.bflyt", "blyt/PaDoCommandB_00.bflyt", "blyt/PaDoCommandX_00.bflyt", "blyt/PaDoCommandY_00.bflyt"
        };

        // The patched pack, or null (why says why); archiveGrowth: how much Layout/Common.sblarc grew, decompressed.
        public static byte[] Patch(byte[] pack, out long archiveGrowth, out string why)
        {
            archiveGrowth = 0;
            uint pdoff, ps, pe;
            int node = Find(pack, "Layout/Common.sblarc", out pdoff, out ps, out pe);
            if (node < 0) { why = "no Layout/Common.sblarc"; return null; }
            byte[] packed = new byte[pe - ps];
            Buffer.BlockCopy(pack, (int)(pdoff + ps), packed, 0, packed.Length);
            byte[] arc;
            try { arc = Yaz0.Decompress(packed); }
            catch (Exception) { why = "archive not readable"; return null; }

            int an = arc.Length;
            byte[] arc2 = new byte[an + 4 * (0x80 + 0x2000)];
            Buffer.BlockCopy(arc, 0, arc2, 0, an);
            int nn = an, panes = 0;
            foreach (string layout in Layouts)
            {
                uint adoff, las, lae;
                int li = Find(arc, layout, out adoff, out las, out lae);
                if (li < 0 || lae - las > 0x2000 - 64) { why = "layout not found"; return null; }
                nn = (nn + 0x7F) & ~0x7F;
                int ln = PatchLayout(arc, (int)(adoff + las), (int)(lae - las), arc2, nn, ref panes);
                if (ln == 0) { why = "layout not as expected"; return null; }
                PutU32(arc2, 0x20 + li * 16 + 8, (uint)(nn - adoff));
                PutU32(arc2, 0x20 + li * 16 + 12, (uint)(nn - adoff + ln));
                nn += ln;
            }
            if (panes != 12) { why = "letters not as expected"; return null; }
            PutU32(arc2, 8, (uint)nn);
            Array.Resize(ref arc2, nn);
            byte[] tail = new byte[8];
            Buffer.BlockCopy(packed, 8, tail, 0, 8);
            byte[] z2 = Yaz0Compressor.Compress(arc2, tail);

            // the pack: the new archive where the old one was, what follows moved by delta (a multiple of 0x2000)
            long old = pe - ps, delta = z2.Length > old ? (z2.Length - old + 0x1FFF) & ~0x1FFFL : 0;
            byte[] q = new byte[pack.Length + delta];
            Buffer.BlockCopy(pack, 0, q, 0, (int)(pdoff + ps));
            Buffer.BlockCopy(z2, 0, q, (int)(pdoff + ps), z2.Length);
            Buffer.BlockCopy(pack, (int)(pdoff + pe), q, (int)(pdoff + pe + delta), pack.Length - (int)(pdoff + pe));
            int cnt = pack[0x1A] | pack[0x1B] << 8;
            for (int i = 0; i < cnt; i++)
            {
                int at = 0x20 + i * 16;
                if (i == node) { PutU32(q, at + 12, (uint)(ps + z2.Length)); continue; }
                uint s = BitConverter.ToUInt32(q, at + 8), e = BitConverter.ToUInt32(q, at + 12);
                if (s >= pe) { s += (uint)delta; e += (uint)delta; }
                PutU32(q, at + 8, s);
                PutU32(q, at + 12, e);
            }
            PutU32(q, 8, (uint)q.Length);
            archiveGrowth = nn - an;
            why = null;
            return q;
        }

        // One layout (FLYT, little-endian, one font) patched into out at w0; returns its size, or 0.
        private static int PatchLayout(byte[] b, int at, int n, byte[] o, int w0, ref int panes)
        {
            if (n < 0x14 || b[at] != 'F' || b[at + 1] != 'L' || b[at + 2] != 'Y' || b[at + 3] != 'T' || b[at + 4] != 0xFF || b[at + 5] != 0xFE) return 0;
            int hs = b[at + 6] | b[at + 7] << 8, r = hs, w = hs;
            if (hs > n) return 0;
            Buffer.BlockCopy(b, at, o, w0, hs);
            bool fonts = false;
            while (r + 8 <= n)
            {
                int sz = (int)BitConverter.ToUInt32(b, at + r + 4);
                if (sz < 8 || r + sz > n) return 0;
                int sec = w0 + w;
                if (Magic(b, at + r, "fnl1"))
                {
                    int first = (int)BitConverter.ToUInt32(b, at + r + 12);
                    if ((b[at + r + 8] | b[at + r + 9] << 8) != 1 || 12 + first >= sz) return 0;
                    int name0 = at + r + 12 + first, max = sz - 12 - first, l0 = 0;
                    while (l0 < max && b[name0 + l0] != 0) l0++;
                    l0++;
                    int l1 = IconFont.Length + 1;
                    int total = (12 + 8 + l0 + l1 + 3) & ~3;
                    Array.Clear(o, sec, total);
                    o[sec] = (byte)'f'; o[sec + 1] = (byte)'n'; o[sec + 2] = (byte)'l'; o[sec + 3] = (byte)'1';
                    PutU32(o, sec + 4, (uint)total);
                    o[sec + 8] = 2;
                    PutU32(o, sec + 12, 8);
                    PutU32(o, sec + 16, (uint)(8 + l0));
                    Buffer.BlockCopy(b, name0, o, sec + 20, Math.Min(l0, max));
                    byte[] icon = Encoding.ASCII.GetBytes(IconFont);
                    Buffer.BlockCopy(icon, 0, o, sec + 20 + l0, icon.Length);
                    w += total;
                    r += sz;
                    fonts = true;
                    continue;
                }
                Buffer.BlockCopy(b, at + r, o, sec, sz);
                if (Magic(o, sec, "txt1") && sz >= 0x68)
                {
                    int toff = (int)BitConverter.ToUInt32(o, sec + 0x64);
                    if (toff != 0 && toff + 4 <= sz && o[sec + toff + 1] == 0 && o[sec + toff + 2] == 0 && o[sec + toff + 3] == 0)
                    {
                        int k = "ABXY".IndexOf((char)o[sec + toff]);
                        if (k >= 0)
                        {
                            o[sec + 0x5A] = 1;
                            o[sec + 0x5B] = 0;  // (the font: the second)
                            ushort c = (ushort)(0xE040 + k);
                            o[sec + toff] = (byte)c;
                            o[sec + toff + 1] = (byte)(c >> 8);
                            for (int f = 0; f < 2; f++)  // the font size: an icon fills less of its cell than a letter
                            {
                                float v = BitConverter.ToSingle(o, sec + 0x70 + f * 4) * IconScale;
                                Buffer.BlockCopy(BitConverter.GetBytes(v), 0, o, sec + 0x70 + f * 4, 4);
                            }
                            panes++;
                        }
                    }
                }
                w += sz;
                r += sz;
            }
            if (!fonts || w > n + 64) return 0;
            PutU32(o, w0 + 0xC, (uint)w);
            return w;
        }

        // SARC (little-endian): the node of `name` (its index; start and end in the data, which begins at dataOff), or -1
        private static int Find(byte[] d, string name, out uint dataOff, out uint start, out uint end)
        {
            dataOff = start = end = 0;
            if (d.Length < 0x20 || !Magic(d, 0, "SARC") || d[6] != 0xFF || d[7] != 0xFE || !Magic(d, 0x14, "SFAT")) return -1;
            uint doff = BitConverter.ToUInt32(d, 0xC);
            int cnt = d[0x1A] | d[0x1B] << 8;
            long names = 0x20 + (long)cnt * 16 + 8;
            if (names > d.Length || doff > d.Length) return -1;
            byte[] want = Encoding.ASCII.GetBytes(name);
            for (int i = 0; i < cnt; i++)
            {
                uint attr = BitConverter.ToUInt32(d, 0x20 + i * 16 + 4);
                uint s = BitConverter.ToUInt32(d, 0x20 + i * 16 + 8), e = BitConverter.ToUInt32(d, 0x20 + i * 16 + 12);
                long at = names + (long)(attr & 0xFFFFFF) * 4;
                if ((attr >> 24) == 0 || at + want.Length >= d.Length) continue;
                bool same = d[at + want.Length] == 0;
                for (int k = 0; same && k < want.Length; k++) same = d[at + k] == want[k];
                if (!same) continue;
                if (s > e || doff + (long)e > d.Length) return -1;
                dataOff = doff; start = s; end = e;
                return i;
            }
            return -1;
        }

        private static bool Magic(byte[] d, int at, string m)
        {
            return d[at] == m[0] && d[at + 1] == m[1] && d[at + 2] == m[2] && d[at + 3] == m[3];
        }

        private static void PutU32(byte[] d, int o, uint v)
        {
            d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24);
        }
    }
}
