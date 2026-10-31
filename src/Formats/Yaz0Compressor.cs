using System;

namespace GuardianTerminal.Formats
{
    // Yaz0 compression: LZ77 with a 4 KB window, matches of 3..0x111 bytes, in groups of 8 items behind a flag byte
    // (bit set: one literal byte; bit clear: a 2-byte or 3-byte back-reference). Hash chains over 3-byte prefixes
    // keep it fast; the results come within a few percent of Nintendo's own.
    internal static class Yaz0Compressor
    {
        private const int Window = 0x1000;
        private const int MinMatch = 3;
        private const int MaxMatch = 0x111;
        private const int MaxChain = 96;
        private const int HashBits = 15;

        // headerTail: bytes 8..15 of the header (on Switch, bytes 8..11 hold the data alignment), or null for zeros
        public static byte[] Compress(byte[] src, byte[] headerTail)
        {
            int n = src.Length;
            byte[] dst = new byte[16 + n + n / 8 + 16];
            dst[0] = (byte)'Y'; dst[1] = (byte)'a'; dst[2] = (byte)'z'; dst[3] = (byte)'0';
            dst[4] = (byte)(n >> 24); dst[5] = (byte)(n >> 16); dst[6] = (byte)(n >> 8); dst[7] = (byte)n;
            if (headerTail != null) Buffer.BlockCopy(headerTail, 0, dst, 8, 8);

            int[] head = new int[1 << HashBits];
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            int[] prev = new int[n];

            int w = 16, s = 0;
            int flagPos = -1, bit = 8;
            while (s < n)
            {
                if (bit == 8) { flagPos = w++; dst[flagPos] = 0; bit = 0; }

                int bestLen = 0, bestPos = 0;
                FindMatch(src, s, head, prev, out bestLen, out bestPos);
                // one step of lazy matching: a longer match at the next byte is worth a literal here
                if (bestLen >= MinMatch && bestLen < MaxMatch && s + 1 < n)
                {
                    Insert(src, s, head, prev);
                    int nextLen, nextPos;
                    FindMatch(src, s + 1, head, prev, out nextLen, out nextPos);
                    if (nextLen > bestLen + 1) bestLen = 0;  // emit a literal now, match next round
                    else Unlink(src, s, head, prev);
                }

                if (bestLen >= MinMatch)
                {
                    int dist = s - bestPos - 1;
                    if (bestLen >= 0x12)
                    {
                        dst[w++] = (byte)(dist >> 8);
                        dst[w++] = (byte)dist;
                        dst[w++] = (byte)(bestLen - 0x12);
                    }
                    else
                    {
                        dst[w++] = (byte)((bestLen - 2) << 4 | dist >> 8);
                        dst[w++] = (byte)dist;
                    }
                    for (int k = 0; k < bestLen; k++) Insert(src, s + k, head, prev);
                    s += bestLen;
                }
                else
                {
                    dst[flagPos] |= (byte)(0x80 >> bit);
                    if (s + MinMatch <= n && head[Hash(src, s)] != s) Insert(src, s, head, prev);
                    dst[w++] = src[s++];
                }
                bit++;
            }
            Array.Resize(ref dst, w);
            return dst;
        }

        private static int Hash(byte[] d, int i)
        {
            int v = d[i] << 16 | d[i + 1] << 8 | d[i + 2];
            return (int)((uint)(v * 0x9E3779B1) >> (32 - HashBits));
        }

        private static void Insert(byte[] d, int i, int[] head, int[] prev)
        {
            if (i + MinMatch > d.Length) return;
            int h = Hash(d, i);
            if (head[h] == i) return;
            prev[i] = head[h];
            head[h] = i;
        }

        // Undo an Insert of position i (it is the newest entry of its chain).
        private static void Unlink(byte[] d, int i, int[] head, int[] prev)
        {
            if (i + MinMatch > d.Length) return;
            int h = Hash(d, i);
            if (head[h] == i) head[h] = prev[i];
        }

        private static void FindMatch(byte[] d, int s, int[] head, int[] prev, out int bestLen, out int bestPos)
        {
            bestLen = 0; bestPos = 0;
            int n = d.Length;
            if (s + MinMatch > n) return;
            int max = Math.Min(MaxMatch, n - s), limit = s - Window, chain = 0;
            for (int p = head[Hash(d, s)]; p >= 0 && p >= limit && chain < MaxChain; p = prev[p], chain++)
            {
                if (p >= s) continue;
                if (d[p + bestLen] != d[s + bestLen] || d[p] != d[s] || d[p + 1] != d[s + 1]) continue;
                int len = 0;
                while (len < max && d[p + len] == d[s + len]) len++;
                if (len > bestLen)
                {
                    bestLen = len; bestPos = p;
                    if (len == max) break;
                }
            }
            if (bestLen < MinMatch) bestLen = 0;
        }
    }
}
