using System;
using System.IO;

namespace GuardianTerminal.Formats
{
    // Looks at files on disk without reading them whole: big mods carry files of hundreds of MB (movies, models,
    // sound banks), and most checks only need their length or their first bytes.
    internal static class FileCompare
    {
        private const int Chunk = 1 << 20;

        // The same bytes: lengths first, then 1 MB at a time.
        public static bool Same(string a, string b)
        {
            FileInfo fa = new FileInfo(a), fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            if (string.Equals(fa.FullName, fb.FullName, StringComparison.OrdinalIgnoreCase)) return true;
            byte[] x = new byte[Chunk], y = new byte[Chunk];
            using (FileStream sa = Open(a))
            using (FileStream sb = Open(b))
            {
                for (;;)
                {
                    int n = ReadFull(sa, x), m = ReadFull(sb, y);
                    if (n != m) return false;
                    if (n == 0) return true;
                    if (!NativeMethods.BytesEqual(x, 0, y, 0, n)) return false;
                }
            }
        }

        // The file's first bytes (fewer when it's shorter)
        public static byte[] Head(string path, int count)
        {
            using (FileStream s = Open(path))
            {
                byte[] b = new byte[(int)Math.Min(count, s.Length)];
                ReadFull(s, b);
                return b;
            }
        }

        // The first bytes of its contents: decompressed when it's Yaz0 (enough to tell a SARC or a BYML apart)
        public static byte[] ContentHead(string path, int count)
        {
            byte[] head = Head(path, 512);
            return Yaz0.IsYaz0(head) ? Yaz0.DecompressPrefix(head, count) : head;
        }

        public static bool IsArchive(string path)
        {
            byte[] h = ContentHead(path, 4);
            return h.Length >= 4 && h[0] == 'S' && h[1] == 'A' && h[2] == 'R' && h[3] == 'C';
        }

        public static bool IsByml(string path)
        {
            byte[] h = ContentHead(path, 2);
            return h.Length >= 2 && h[0] == 'Y' && h[1] == 'B';
        }

        public static FileStream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Chunk, FileOptions.SequentialScan);
        }

        private static int ReadFull(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer, total, buffer.Length - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }
    }
}
