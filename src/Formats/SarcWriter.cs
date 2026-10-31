using System;
using System.Collections.Generic;
using System.Text;

namespace GuardianTerminal.Formats
{
    internal sealed class SarcFile
    {
        public string Name;
        public byte[] Data;
        public int Alignment;  // of the file's absolute offset in the archive (a power of two)
    }

    // Writes a little-endian (Switch) SARC: nodes sorted by name hash (the game binary-searches them), names 4-byte
    // aligned, each file's data at an offset aligned to its Alignment, in node order.
    internal static class SarcWriter
    {
        private const uint HashKey = 0x65;

        public static uint NameHash(string name)
        {
            uint h = 0;
            foreach (byte b in Encoding.UTF8.GetBytes(name)) h = h * HashKey + (uint)(sbyte)b;
            return h;
        }

        // Alignment a file had in an archive: the largest power of two dividing its offset (at least 4, at most 0x2000).
        public static int AlignmentOf(int absoluteOffset)
        {
            if (absoluteOffset <= 0) return 0x2000;
            int a = absoluteOffset & -absoluteOffset;
            return Math.Max(4, Math.Min(0x2000, a));
        }

        public static byte[] Write(List<SarcFile> files)
        {
            List<SarcFile> sorted = new List<SarcFile>(files);
            sorted.Sort(delegate(SarcFile a, SarcFile b)
            {
                int c = NameHash(a.Name).CompareTo(NameHash(b.Name));
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });

            // names table
            List<byte> names = new List<byte>();
            int[] nameOffsets = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                nameOffsets[i] = names.Count;
                names.AddRange(Encoding.UTF8.GetBytes(sorted[i].Name));
                names.Add(0);
                while (names.Count % 4 != 0) names.Add(0);
            }

            int sfat = 0x14, nodes = sfat + 0x0C, sfnt = nodes + sorted.Count * 16, namesAt = sfnt + 8;
            int headerEnd = namesAt + names.Count;
            int maxAlign = 4;
            foreach (SarcFile f in sorted) maxAlign = Math.Max(maxAlign, f.Alignment);
            int dataOffset = Align(headerEnd, maxAlign);

            // lay out the data
            int[] starts = new int[sorted.Count];
            int pos = dataOffset;
            for (int i = 0; i < sorted.Count; i++)
            {
                pos = Align(pos, Math.Max(4, sorted[i].Alignment));
                starts[i] = pos;
                pos += sorted[i].Data.Length;
            }
            int total = pos;

            byte[] o = new byte[total];
            o[0] = (byte)'S'; o[1] = (byte)'A'; o[2] = (byte)'R'; o[3] = (byte)'C';
            PutU16(o, 4, 0x14);
            o[6] = 0xFF; o[7] = 0xFE;  // little-endian BOM
            PutU32(o, 8, (uint)total);
            PutU32(o, 0x0C, (uint)dataOffset);
            PutU16(o, 0x10, 0x0100);

            o[sfat] = (byte)'S'; o[sfat + 1] = (byte)'F'; o[sfat + 2] = (byte)'A'; o[sfat + 3] = (byte)'T';
            PutU16(o, sfat + 4, 0x0C);
            PutU16(o, sfat + 6, (ushort)sorted.Count);
            PutU32(o, sfat + 8, HashKey);

            uint lastHash = 0;
            int collision = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                uint h = NameHash(sorted[i].Name);
                collision = i > 0 && h == lastHash ? collision + 1 : 1;
                lastHash = h;
                int n = nodes + i * 16;
                PutU32(o, n, h);
                PutU32(o, n + 4, (uint)(collision << 24 | nameOffsets[i] / 4));
                PutU32(o, n + 8, (uint)(starts[i] - dataOffset));
                PutU32(o, n + 12, (uint)(starts[i] - dataOffset + sorted[i].Data.Length));
            }

            o[sfnt] = (byte)'S'; o[sfnt + 1] = (byte)'F'; o[sfnt + 2] = (byte)'N'; o[sfnt + 3] = (byte)'T';
            PutU16(o, sfnt + 4, 8);
            names.CopyTo(o, namesAt);

            for (int i = 0; i < sorted.Count; i++) Buffer.BlockCopy(sorted[i].Data, 0, o, starts[i], sorted[i].Data.Length);
            return o;
        }

        private static int Align(int v, int a)
        {
            return (v + a - 1) & ~(a - 1);
        }

        private static void PutU16(byte[] d, int o, int v)
        {
            d[o] = (byte)v; d[o + 1] = (byte)(v >> 8);
        }

        private static void PutU32(byte[] d, int o, uint v)
        {
            d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24);
        }
    }
}
