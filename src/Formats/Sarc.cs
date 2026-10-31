using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GuardianTerminal.Formats
{
    internal sealed class SarcEntry
    {
        public string Name;
        public int Offset;  // in the archive's (decompressed) bytes
        public int Length;
    }

    // SARC: the archive format of BOTW's .pack / .sbactorpack / .sbeventpack / ... files.
    //   header "SARC" (0x14 bytes: u16 header size, u16 BOM, u32 file size, u32 data offset, u16 version, u16 pad),
    //   "SFAT" (u16 header size, u16 node count, u32 hash key) + 16-byte nodes {hash, attributes, data start, data end},
    //   "SFNT" (u16 header size, u16 pad) + the names, 4-byte aligned. A node's name is at (attributes & 0xFFFF) * 4.
    // The BOM is FF FE (little-endian) on Switch and FE FF (big-endian) on Wii U.
    internal static class Sarc
    {
        public static bool IsSarc(byte[] d)
        {
            return d != null && d.Length >= 0x20 && d[0] == 'S' && d[1] == 'A' && d[2] == 'R' && d[3] == 'C';
        }

        public static bool IsBigEndian(byte[] d)
        {
            return d[6] == 0xFE && d[7] == 0xFF;
        }

        public static List<SarcEntry> Parse(byte[] d)
        {
            if (!IsSarc(d)) throw new InvalidDataException("Not a SARC archive");
            bool be = IsBigEndian(d);
            int headerSize = U16(d, 4, be);
            uint dataOffset = U32(d, 0x0C, be);
            int sfat = headerSize;
            if (d[sfat] != 'S' || d[sfat + 1] != 'F' || d[sfat + 2] != 'A' || d[sfat + 3] != 'T')
                throw new InvalidDataException("SARC without an SFAT table");
            int sfatHeader = U16(d, sfat + 4, be), count = U16(d, sfat + 6, be);
            int nodes = sfat + sfatHeader;
            int sfnt = nodes + count * 16;
            if (d[sfnt] != 'S' || d[sfnt + 1] != 'F' || d[sfnt + 2] != 'N' || d[sfnt + 3] != 'T')
                throw new InvalidDataException("SARC without an SFNT table");
            int names = sfnt + U16(d, sfnt + 4, be);
            List<SarcEntry> list = new List<SarcEntry>(count);
            for (int i = 0; i < count; i++)
            {
                int n = nodes + i * 16;
                uint hash = U32(d, n, be), attr = U32(d, n + 4, be);
                uint start = U32(d, n + 8, be), end = U32(d, n + 12, be);
                string name;
                if ((attr & 0x01000000u) != 0)
                {
                    int at = names + (int)(attr & 0xFFFF) * 4, len = 0;
                    while (at + len < d.Length && d[at + len] != 0) len++;
                    name = Encoding.UTF8.GetString(d, at, len);
                }
                else name = string.Format("0x{0:X8}", hash);
                SarcEntry e = new SarcEntry();
                e.Name = name;
                e.Offset = (int)(dataOffset + start);
                e.Length = (int)(end - start);
                list.Add(e);
            }
            return list;
        }

        public static byte[] Extract(byte[] archive, SarcEntry e)
        {
            byte[] b = new byte[e.Length];
            Buffer.BlockCopy(archive, e.Offset, b, 0, e.Length);
            return b;
        }

        private static int U16(byte[] d, int o, bool be)
        {
            return be ? d[o] << 8 | d[o + 1] : d[o] | d[o + 1] << 8;
        }

        private static uint U32(byte[] d, int o, bool be)
        {
            return be
                ? (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3])
                : (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);
        }
    }
}
