using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GuardianTerminal.Formats
{
    // BYML (little endian, as on Switch; versions 2-4): the game's data trees (game data flags, save formats, ...).
    // Values: Dictionary<string, object> (a hash), List<object> (an array), string, bool, int, float, uint, long,
    // ulong, double, byte[] (binary) and null.
    internal static class Byml
    {
        private const byte TString = 0xA0, TBinary = 0xA1, TArray = 0xC0, THash = 0xC1, TStringTable = 0xC2,
            TBool = 0xD0, TInt = 0xD1, TFloat = 0xD2, TUInt = 0xD3, TInt64 = 0xD4, TUInt64 = 0xD5, TDouble = 0xD6, TNull = 0xFF;

        public static bool IsByml(byte[] d)
        {
            return d.Length >= 16 && d[0] == 'Y' && d[1] == 'B';
        }

        public static int Version(byte[] d)
        {
            return BitConverter.ToUInt16(d, 2);
        }

        // ------------------------------------------------------------------ reading
        public static object Parse(byte[] d)
        {
            if (!IsByml(d)) throw new InvalidDataException("Not a little-endian BYML file.");
            string[] keys = StringTable(d, (int)BitConverter.ToUInt32(d, 4));
            string[] strings = StringTable(d, (int)BitConverter.ToUInt32(d, 8));
            int root = (int)BitConverter.ToUInt32(d, 12);
            if (root == 0) return null;
            return Node(d, d[root], (uint)root, keys, strings);
        }

        private static string[] StringTable(byte[] d, int at)
        {
            if (at == 0) return new string[0];
            if (d[at] != TStringTable) throw new InvalidDataException("Bad BYML string table.");
            int n = U24(d, at + 1);
            string[] s = new string[n];
            for (int i = 0; i < n; i++)
            {
                int start = at + (int)BitConverter.ToUInt32(d, at + 4 + 4 * i), end = start;
                while (d[end] != 0) end++;
                s[i] = Encoding.UTF8.GetString(d, start, end - start);
            }
            return s;
        }

        private static object Node(byte[] d, byte type, uint value, string[] keys, string[] strings)
        {
            switch (type)
            {
                case TString: return strings[value];
                case TBool: return value != 0;
                case TInt: return (int)value;
                case TFloat: return BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
                case TUInt: return value;
                case TInt64: return BitConverter.ToInt64(d, (int)value);
                case TUInt64: return BitConverter.ToUInt64(d, (int)value);
                case TDouble: return BitConverter.ToDouble(d, (int)value);
                case TNull: return null;
                case TBinary:
                {
                    int len = (int)BitConverter.ToUInt32(d, (int)value);
                    byte[] b = new byte[len];
                    Buffer.BlockCopy(d, (int)value + 4, b, 0, len);
                    return b;
                }
                case TArray:
                {
                    int at = (int)value, n = U24(d, at + 1);
                    int values = at + 4 + Align4(n);
                    List<object> list = new List<object>(n);
                    for (int i = 0; i < n; i++)
                        list.Add(Node(d, d[at + 4 + i], BitConverter.ToUInt32(d, values + 4 * i), keys, strings));
                    return list;
                }
                case THash:
                {
                    int at = (int)value, n = U24(d, at + 1);
                    Dictionary<string, object> hash = new Dictionary<string, object>(n, StringComparer.Ordinal);
                    for (int i = 0; i < n; i++)
                    {
                        int e = at + 4 + 8 * i;
                        hash[keys[U24(d, e)]] = Node(d, d[e + 3], BitConverter.ToUInt32(d, e + 4), keys, strings);
                    }
                    return hash;
                }
                default: throw new InvalidDataException(string.Format("Unknown BYML node type 0x{0:X2}.", type));
            }
        }

        private static int U24(byte[] d, int at)
        {
            return d[at] | d[at + 1] << 8 | d[at + 2] << 16;
        }

        private static int Align4(int n)
        {
            return (n + 3) & ~3;
        }

        // ------------------------------------------------------------------ writing
        // The tree as BYML of the given version; keys and strings sorted as the game's tools write them.
        public static byte[] Write(object root, int version)
        {
            SortedSet<string> keySet = new SortedSet<string>(StringComparer.Ordinal), stringSet = new SortedSet<string>(StringComparer.Ordinal);
            Collect(root, keySet, stringSet);
            List<string> keys = keySet.ToList(), strings = stringSet.ToList();
            Dictionary<string, int> keyIndex = new Dictionary<string, int>(StringComparer.Ordinal), stringIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++) keyIndex[keys[i]] = i;
            for (int i = 0; i < strings.Count; i++) stringIndex[strings[i]] = i;

            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write((byte)'Y'); w.Write((byte)'B'); w.Write((ushort)version);
            w.Write(0u); w.Write(0u); w.Write(0u);
            if (keys.Count > 0) { Patch(w, 4, (uint)ms.Position); WriteStringTable(w, keys); }
            if (strings.Count > 0) { Patch(w, 8, (uint)ms.Position); WriteStringTable(w, strings); }
            if (root != null && (root is Dictionary<string, object> || root is List<object>))
            {
                Patch(w, 12, (uint)ms.Position);
                WriteContainer(w, root, keyIndex, stringIndex);
            }
            Pad(w);
            return ms.ToArray();
        }

        private static void Collect(object o, SortedSet<string> keys, SortedSet<string> strings)
        {
            Dictionary<string, object> h = o as Dictionary<string, object>;
            if (h != null)
            {
                foreach (KeyValuePair<string, object> kv in h) { keys.Add(kv.Key); Collect(kv.Value, keys, strings); }
                return;
            }
            List<object> a = o as List<object>;
            if (a != null) { foreach (object x in a) Collect(x, keys, strings); return; }
            string s = o as string;
            if (s != null) strings.Add(s);
        }

        private static void WriteStringTable(BinaryWriter w, List<string> table)
        {
            long start = w.BaseStream.Position;
            w.Write((uint)(TStringTable | table.Count << 8));
            byte[][] bytes = table.Select(s => Encoding.UTF8.GetBytes(s)).ToArray();
            uint offset = (uint)(4 + 4 * (table.Count + 1));
            foreach (byte[] b in bytes) { w.Write(offset); offset += (uint)b.Length + 1; }
            w.Write(offset);
            foreach (byte[] b in bytes) { w.Write(b); w.Write((byte)0); }
            Pad(w);
        }

        // Writes a container at the current position; its child containers and 64-bit values after it.
        private static void WriteContainer(BinaryWriter w, object o, Dictionary<string, int> keyIndex, Dictionary<string, int> stringIndex)
        {
            Dictionary<string, object> h = o as Dictionary<string, object>;
            List<KeyValuePair<long, object>> later = new List<KeyValuePair<long, object>>();  // (where its offset goes, the value)
            if (h != null)
            {
                List<KeyValuePair<string, object>> items = h.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                w.Write((uint)(THash | items.Count << 8));
                foreach (KeyValuePair<string, object> kv in items)
                {
                    byte type = TypeOf(kv.Value);
                    w.Write((uint)(keyIndex[kv.Key] | type << 24));
                    WriteValue(w, kv.Value, type, stringIndex, later);
                }
            }
            else
            {
                List<object> a = (List<object>)o;
                w.Write((uint)(TArray | a.Count << 8));
                foreach (object x in a) w.Write(TypeOf(x));
                Pad(w);
                foreach (object x in a) WriteValue(w, x, TypeOf(x), stringIndex, later);
            }
            foreach (KeyValuePair<long, object> l in later)
            {
                Pad(w);
                Patch(w, l.Key, (uint)w.BaseStream.Position);
                object v = l.Value;
                if (v is Dictionary<string, object> || v is List<object>) WriteContainer(w, v, keyIndex, stringIndex);
                else if (v is long) w.Write((long)v);
                else if (v is ulong) w.Write((ulong)v);
                else if (v is double) w.Write((double)v);
                else { byte[] b = (byte[])v; w.Write((uint)b.Length); w.Write(b); }
            }
        }

        private static void WriteValue(BinaryWriter w, object v, byte type, Dictionary<string, int> stringIndex, List<KeyValuePair<long, object>> later)
        {
            switch (type)
            {
                case TString: w.Write((uint)stringIndex[(string)v]); break;
                case TBool: w.Write((bool)v ? 1u : 0u); break;
                case TInt: w.Write((int)v); break;
                case TFloat: w.Write((float)v); break;
                case TUInt: w.Write((uint)v); break;
                case TNull: w.Write(0u); break;
                default:
                    later.Add(new KeyValuePair<long, object>(w.BaseStream.Position, v));
                    w.Write(0u);
                    break;
            }
        }

        private static byte TypeOf(object v)
        {
            if (v == null) return TNull;
            if (v is string) return TString;
            if (v is bool) return TBool;
            if (v is int) return TInt;
            if (v is float) return TFloat;
            if (v is uint) return TUInt;
            if (v is long) return TInt64;
            if (v is ulong) return TUInt64;
            if (v is double) return TDouble;
            if (v is byte[]) return TBinary;
            if (v is Dictionary<string, object>) return THash;
            if (v is List<object>) return TArray;
            throw new InvalidDataException("A value BYML can't hold: " + v.GetType().Name);
        }

        private static void Patch(BinaryWriter w, long at, uint value)
        {
            long here = w.BaseStream.Position;
            w.BaseStream.Position = at;
            w.Write(value);
            w.BaseStream.Position = here;
        }

        private static void Pad(BinaryWriter w)
        {
            while (w.BaseStream.Position % 4 != 0) w.Write((byte)0);
        }
    }
}
