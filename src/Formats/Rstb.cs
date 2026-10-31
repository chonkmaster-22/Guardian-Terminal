using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GuardianTerminal.Formats
{
    // System/Resource/ResourceSizeTable.product.srsizetable (the "RSTB"): how much memory the game sets aside for
    // each resource, by path. Decompressed layout (little-endian on Switch):
    //   "RSTB", u32 crc count, u32 name count,
    //   crc entries {u32 crc32(path), u32 size} sorted by crc (the game binary-searches them),
    //   name entries {char[128] path, u32 size} for paths whose crc32 collides with another's (checked first).
    internal sealed class Rstb
    {
        private const int NameLength = 128;

        private readonly Dictionary<uint, uint> crcSizes = new Dictionary<uint, uint>();
        private readonly List<string> names = new List<string>();
        private readonly Dictionary<string, uint> nameSizes = new Dictionary<string, uint>(StringComparer.Ordinal);

        public int CrcCount { get { return crcSizes.Count; } }
        public int NameCount { get { return names.Count; } }

        public static Rstb Load(string path)
        {
            return Parse(Yaz0.DecompressIfNeeded(File.ReadAllBytes(path)));
        }

        public static Rstb Parse(byte[] d)
        {
            if (d.Length < 12 || d[0] != 'R' || d[1] != 'S' || d[2] != 'T' || d[3] != 'B')
                throw new InvalidDataException("Not a ResourceSizeTable (no RSTB header)");
            uint ncrc = BitConverter.ToUInt32(d, 4), nname = BitConverter.ToUInt32(d, 8);
            if (12 + (long)ncrc * 8 + (long)nname * (NameLength + 4) > d.Length)
            {
                // A big-endian (Wii U) table reads as absurd counts here.
                throw new InvalidDataException("ResourceSizeTable counts don't fit the file (a Wii U table?)");
            }
            Rstb t = new Rstb();
            int o = 12;
            for (uint i = 0; i < ncrc; i++, o += 8) t.crcSizes[BitConverter.ToUInt32(d, o)] = BitConverter.ToUInt32(d, o + 4);
            for (uint i = 0; i < nname; i++, o += NameLength + 4)
            {
                int len = 0;
                while (len < NameLength && d[o + len] != 0) len++;
                string name = Encoding.UTF8.GetString(d, o, len);
                if (!t.nameSizes.ContainsKey(name)) t.names.Add(name);
                t.nameSizes[name] = BitConverter.ToUInt32(d, o + NameLength);
            }
            return t;
        }

        public Rstb Clone()
        {
            Rstb t = new Rstb();
            foreach (KeyValuePair<uint, uint> kv in crcSizes) t.crcSizes[kv.Key] = kv.Value;
            foreach (string n in names) { t.names.Add(n); t.nameSizes[n] = nameSizes[n]; }
            return t;
        }

        public bool TryGet(string path, out uint size)
        {
            if (nameSizes.TryGetValue(path, out size)) return true;
            return crcSizes.TryGetValue(Crc32.Compute(path), out size);
        }

        public void Set(string path, uint size)
        {
            if (nameSizes.ContainsKey(path)) nameSizes[path] = size;
            else crcSizes[Crc32.Compute(path)] = size;
        }

        // Takes every entry of another table that is bigger than ours (or that we don't have).
        public int MergeMax(Rstb other)
        {
            int changed = 0;
            foreach (KeyValuePair<uint, uint> kv in other.crcSizes)
            {
                uint mine;
                if (!crcSizes.TryGetValue(kv.Key, out mine) || kv.Value > mine) { crcSizes[kv.Key] = kv.Value; changed++; }
            }
            foreach (string n in other.names)
            {
                uint theirs = other.nameSizes[n], mine;
                if (!nameSizes.TryGetValue(n, out mine)) { names.Add(n); nameSizes[n] = theirs; changed++; }
                else if (theirs > mine) { nameSizes[n] = theirs; changed++; }
            }
            return changed;
        }

        // Number of entries that differ from another table (added or different size).
        public int CountDifferences(Rstb other)
        {
            int n = 0;
            foreach (KeyValuePair<uint, uint> kv in crcSizes)
            {
                uint v;
                if (!other.crcSizes.TryGetValue(kv.Key, out v) || v != kv.Value) n++;
            }
            foreach (string name in names)
            {
                uint v;
                if (!other.nameSizes.TryGetValue(name, out v) || v != nameSizes[name]) n++;
            }
            return n;
        }

        public byte[] ToBytes()
        {
            List<uint> keys = new List<uint>(crcSizes.Keys);
            keys.Sort();
            byte[] d = new byte[12 + keys.Count * 8 + names.Count * (NameLength + 4)];
            d[0] = (byte)'R'; d[1] = (byte)'S'; d[2] = (byte)'T'; d[3] = (byte)'B';
            PutU32(d, 4, (uint)keys.Count);
            PutU32(d, 8, (uint)names.Count);
            int o = 12;
            foreach (uint k in keys) { PutU32(d, o, k); PutU32(d, o + 4, crcSizes[k]); o += 8; }
            foreach (string n in names)
            {
                byte[] nb = Encoding.UTF8.GetBytes(n);
                Buffer.BlockCopy(nb, 0, d, o, Math.Min(nb.Length, NameLength - 1));
                PutU32(d, o + NameLength, nameSizes[n]);
                o += NameLength + 4;
            }
            return d;
        }

        private static void PutU32(byte[] d, int o, uint v)
        {
            d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24);
        }
    }
}
