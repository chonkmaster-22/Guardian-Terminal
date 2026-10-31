using System;
using System.IO;

namespace GuardianTerminal.Formats
{
    // Yaz0: Nintendo's LZ compression. BOTW stores most files compressed this way (the ".s" extensions).
    // Header: "Yaz0", u32 big-endian decompressed size, 8 reserved bytes, then the compressed stream.
    internal static class Yaz0
    {
        public static bool IsYaz0(byte[] data)
        {
            return data != null && data.Length >= 16 && data[0] == 'Y' && data[1] == 'a' && data[2] == 'z' && data[3] == '0';
        }

        public static uint ReadDecompressedSize(byte[] header)
        {
            return (uint)(header[4] << 24 | header[5] << 16 | header[6] << 8 | header[7]);
        }

        // Size of a file's contents once decompressed: the Yaz0 header's size, or the file's own length.
        public static long PeekDecompressedSize(string path, out bool compressed)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] head = new byte[16];
                int n = fs.Read(head, 0, 16);
                compressed = n == 16 && IsYaz0(head);
                return compressed ? ReadDecompressedSize(head) : fs.Length;
            }
        }

        public static byte[] Decompress(byte[] src)
        {
            return Decompress(src, int.MaxValue);
        }

        // Only the first `count` bytes (to look at a header without decompressing everything).
        public static byte[] DecompressPrefix(byte[] src, int count)
        {
            try { return Decompress(src, count); }
            catch (InvalidDataException) { return new byte[0]; }
            catch (IndexOutOfRangeException) { return new byte[0]; }
        }

        private static byte[] Decompress(byte[] src, int limit)
        {
            if (!IsYaz0(src)) throw new InvalidDataException("Not Yaz0 data");
            int size = (int)Math.Min(ReadDecompressedSize(src), (uint)limit);
            byte[] dst = new byte[size];
            int s = 16, d = 0;
            while (d < size)
            {
                if (s >= src.Length) throw new InvalidDataException("Yaz0 data ends early");
                byte code = src[s++];
                for (int bit = 0; bit < 8 && d < size; bit++)
                {
                    if ((code & (0x80 >> bit)) != 0)
                    {
                        dst[d++] = src[s++];
                        continue;
                    }
                    int b1 = src[s++], b2 = src[s++];
                    int dist = ((b1 & 0x0F) << 8 | b2) + 1;
                    int count = b1 >> 4;
                    count = count == 0 ? src[s++] + 0x12 : count + 2;
                    if (dist > d) throw new InvalidDataException("Yaz0 back-reference before the start");
                    for (int i = 0; i < count && d < size; i++, d++) dst[d] = dst[d - dist];
                }
            }
            return dst;
        }

        // Decompresses when the data is Yaz0, otherwise returns it unchanged.
        public static byte[] DecompressIfNeeded(byte[] data)
        {
            return IsYaz0(data) ? Decompress(data) : data;
        }

        // Yaz0 made of literal bytes only (a 0xFF flag before every 8 bytes): bigger than real compression but valid
        // for the game's decoder, and what Savage Wind itself serves its patched ResourceSizeTable as.
        public static byte[] CompressLiteral(byte[] data)
        {
            int n = data.Length;
            byte[] o = new byte[16 + n + (n + 7) / 8];
            o[0] = (byte)'Y'; o[1] = (byte)'a'; o[2] = (byte)'z'; o[3] = (byte)'0';
            o[4] = (byte)(n >> 24); o[5] = (byte)(n >> 16); o[6] = (byte)(n >> 8); o[7] = (byte)n;
            int w = 16;
            for (int i = 0; i < n; i += 8)
            {
                int k = Math.Min(8, n - i);
                o[w++] = 0xFF;
                Buffer.BlockCopy(data, i, o, w, k);
                w += k;
            }
            if (w != o.Length) Array.Resize(ref o, w);
            return o;
        }
    }
}
