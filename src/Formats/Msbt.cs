using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GuardianTerminal.Formats
{
    // MSBT (MsgStdBn), the game's text files (Message/Msg_XXxx.product.ssarc in Bootup_XXxx.pack): every message
    // has a label. The Switch's are little-endian, UTF-16, with three sections: LBL1 (the labels, in a hash table
    // of slots), ATR1 (attributes, none in this game: entry size 0) and TXT2 (the texts, with their control codes).
    // Texts are kept as their raw bytes, so nothing in them changes.
    internal sealed class Msbt
    {
        public sealed class Message
        {
            public string Label;
            public byte[] Attribute = new byte[0];
            public byte[] Text;
        }

        public readonly List<Message> Messages = new List<Message>();  // in index order
        public int Slots;           // LBL1's hash table size
        public byte Encoding = 1;   // 1: UTF-16
        public byte Version = 3;
        public int AttributeSize;   // ATR1's entry size
        public bool AttributeStrings;  // ATR1's entries are offsets to strings after them

        public static bool IsMsbt(byte[] d)
        {
            return d.Length >= 0x20 && System.Text.Encoding.ASCII.GetString(d, 0, 8) == "MsgStdBn";
        }

        // null when it isn't a little-endian MSBT with exactly LBL1, ATR1 and TXT2, a label for every message
        public static Msbt Parse(byte[] d)
        {
            if (!IsMsbt(d) || d[8] != 0xFF || d[9] != 0xFE) return null;
            Msbt m = new Msbt { Encoding = d[0xC], Version = d[0xD] };
            int sections = BitConverter.ToUInt16(d, 0xE);
            Dictionary<string, int> where = new Dictionary<string, int>(), sizes = new Dictionary<string, int>();
            int p = 0x20;
            for (int s = 0; s < sections; s++)
            {
                if (p + 16 > d.Length) return null;
                string magic = System.Text.Encoding.ASCII.GetString(d, p, 4);
                int size = (int)BitConverter.ToUInt32(d, p + 4);
                if (p + 16 + size > d.Length || where.ContainsKey(magic)) return null;
                where[magic] = p + 16;
                sizes[magic] = size;
                p = (p + 16 + size + 15) & ~15;
            }
            if (where.Count != 3 || !where.ContainsKey("LBL1") || !where.ContainsKey("ATR1") || !where.ContainsKey("TXT2")) return null;

            // TXT2: offsets, each text runs to the next one
            int txt = where["TXT2"], txtSize = sizes["TXT2"];
            int count = (int)BitConverter.ToUInt32(d, txt);
            if (count < 0 || 4 + 4L * count > txtSize) return null;
            for (int i = 0; i < count; i++)
            {
                int start = (int)BitConverter.ToUInt32(d, txt + 4 + 4 * i);
                int end = i + 1 < count ? (int)BitConverter.ToUInt32(d, txt + 8 + 4 * i) : txtSize;
                if (start < 4 + 4 * count || end < start || end > txtSize) return null;
                byte[] t = new byte[end - start];
                Buffer.BlockCopy(d, txt + start, t, 0, t.Length);
                m.Messages.Add(new Message { Text = t });
            }

            // ATR1: count and entry size, then the entries: none (size 0, most files), or (the dialogue files) each an
            // offset to its own UTF-16 string after them (the speaker), kept as the string's bytes with its end
            // (some mod tools write the count and size with no entries at all: read as no attributes)
            int atr = where["ATR1"], atrSize = sizes["ATR1"];
            if (BitConverter.ToUInt32(d, atr) != count) return null;
            m.AttributeSize = (int)BitConverter.ToUInt32(d, atr + 4);
            long table = 8 + (long)m.AttributeSize * count;
            m.AttributeStrings = m.AttributeSize == 4;
            bool none = table > atrSize && atrSize == 8;
            if (!none && (table > atrSize || table < atrSize && !m.AttributeStrings)) return null;
            for (int i = 0; i < count && m.AttributeSize > 0 && !none; i++)
            {
                byte[] a;
                if (m.AttributeStrings)
                {
                    int start = (int)BitConverter.ToUInt32(d, atr + 8 + 4 * i), end = start;
                    if (start < table || start >= atrSize) return null;
                    while (end + 1 < atrSize && (d[atr + end] != 0 || d[atr + end + 1] != 0)) end += 2;
                    end = Math.Min(end + 2, atrSize);
                    a = new byte[end - start];
                    Buffer.BlockCopy(d, atr + start, a, 0, a.Length);
                }
                else
                {
                    a = new byte[m.AttributeSize];
                    Buffer.BlockCopy(d, atr + 8 + i * m.AttributeSize, a, 0, a.Length);
                }
                m.Messages[i].Attribute = a;
            }

            // LBL1: slots of (label count, offset); each label: length, ASCII, message index
            int lbl = where["LBL1"];
            m.Slots = (int)BitConverter.ToUInt32(d, lbl);
            if (m.Slots <= 0 || 4 + 8L * m.Slots > sizes["LBL1"]) return null;
            for (int s = 0; s < m.Slots; s++)
            {
                int n = (int)BitConverter.ToUInt32(d, lbl + 4 + 8 * s), at = lbl + (int)BitConverter.ToUInt32(d, lbl + 8 + 8 * s);
                for (int k = 0; k < n; k++)
                {
                    int len = d[at];
                    string label = System.Text.Encoding.ASCII.GetString(d, at + 1, len);
                    int index = (int)BitConverter.ToUInt32(d, at + 1 + len);
                    if (index < 0 || index >= count || m.Messages[index].Label != null) return null;
                    m.Messages[index].Label = label;
                    at += 1 + len + 4;
                }
            }
            if (m.Messages.Any(x => x.Label == null)) return null;
            return m;
        }

        public static uint Hash(string label, int slots)
        {
            uint h = 0;
            foreach (char c in label) h = unchecked(h * 0x492 + c);
            return h % (uint)slots;
        }

        public byte[] Write()
        {
            int count = Messages.Count;
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(System.Text.Encoding.ASCII.GetBytes("MsgStdBn"));
            w.Write((byte)0xFF); w.Write((byte)0xFE);
            w.Write((ushort)0);
            w.Write(Encoding); w.Write(Version);
            w.Write((ushort)3);
            w.Write((ushort)0);
            w.Write(0u);  // file size, below
            w.Write(new byte[10]);

            // LBL1
            List<int>[] slots = new List<int>[Slots];
            for (int s = 0; s < Slots; s++) slots[s] = new List<int>();
            for (int i = 0; i < count; i++) slots[Hash(Messages[i].Label, Slots)].Add(i);
            MemoryStream lbl = new MemoryStream();
            BinaryWriter lw = new BinaryWriter(lbl);
            lw.Write((uint)Slots);
            int offset = 4 + 8 * Slots;
            foreach (List<int> slot in slots)
            {
                lw.Write((uint)slot.Count);
                lw.Write((uint)offset);
                foreach (int i in slot) offset += 1 + Messages[i].Label.Length + 4;
            }
            foreach (List<int> slot in slots)
                foreach (int i in slot)
                {
                    byte[] name = System.Text.Encoding.ASCII.GetBytes(Messages[i].Label);
                    lw.Write((byte)name.Length);
                    lw.Write(name);
                    lw.Write((uint)i);
                }
            Section(w, "LBL1", lbl.ToArray());

            // ATR1
            MemoryStream atr = new MemoryStream();
            BinaryWriter aw = new BinaryWriter(atr);
            aw.Write((uint)count);
            aw.Write((uint)AttributeSize);
            if (AttributeStrings)
            {
                int pos = 8 + 4 * count;
                foreach (Message m in Messages)
                {
                    aw.Write((uint)pos);
                    pos += (m.Attribute ?? new byte[0]).Length == 0 ? 2 : m.Attribute.Length;
                }
                foreach (Message m in Messages)
                    aw.Write((m.Attribute ?? new byte[0]).Length == 0 ? new byte[2] : m.Attribute);
            }
            else
                foreach (Message m in Messages)
                {
                    byte[] a = m.Attribute ?? new byte[0];
                    if (a.Length != AttributeSize) Array.Resize(ref a, AttributeSize);
                    aw.Write(a);
                }
            Section(w, "ATR1", atr.ToArray());

            // TXT2
            MemoryStream txt = new MemoryStream();
            BinaryWriter tw = new BinaryWriter(txt);
            tw.Write((uint)count);
            int at = 4 + 4 * count;
            foreach (Message m in Messages)
            {
                tw.Write((uint)at);
                at += m.Text.Length;
            }
            foreach (Message m in Messages) tw.Write(m.Text);
            Section(w, "TXT2", txt.ToArray());

            byte[] all = ms.ToArray();
            BitConverter.GetBytes((uint)all.Length).CopyTo(all, 0x12);
            return all;
        }

        private static void Section(BinaryWriter w, string magic, byte[] data)
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(magic));
            w.Write((uint)data.Length);
            w.Write(new byte[8]);
            w.Write(data);
            while (w.BaseStream.Position % 16 != 0) w.Write((byte)0xAB);
        }
    }
}
