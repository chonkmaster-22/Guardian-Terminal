using System;
using System.Collections.Generic;
using System.Linq;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    // Merges mods' versions of one of the game's text files (.msbt) into the game's own, message by message: each
    // mod's changed and new messages are applied in load order (the later mod's wins where two change the same one),
    // and messages a mod's copy lacks stay (an older game version's file doesn't drop 1.9.0's texts). The game's
    // messages keep their places; new ones go at the end.
    internal sealed class MsbtMerger
    {
        public int Changed { get; private set; }
        public int Added { get; private set; }
        public int Conflicts { get; private set; }

        // null: one of them isn't an MSBT this can read
        public static byte[] TryMerge(byte[] game, List<byte[]> mods, out MsbtMerger merger)
        {
            merger = null;
            Msbt g = Msbt.Parse(game);
            if (g == null) return null;
            List<Msbt> ms = new List<Msbt>();
            foreach (byte[] raw in mods)
            {
                Msbt m = Msbt.Parse(raw);
                if (m == null || m.AttributeSize != g.AttributeSize || m.AttributeStrings != g.AttributeStrings) return null;
                ms.Add(m);
            }
            merger = new MsbtMerger();
            return merger.Merge(g, ms).Write();
        }

        // Whether a mod's copy lacks some of the game's messages (then even a single mod's file is merged)
        public static bool LacksGameMessages(byte[] game, byte[] mod)
        {
            Msbt g = Msbt.Parse(game), m = Msbt.Parse(mod);
            if (g == null || m == null) return false;
            HashSet<string> have = new HashSet<string>(m.Messages.Select(x => x.Label), StringComparer.Ordinal);
            return g.Messages.Any(x => !have.Contains(x.Label));
        }

        private Msbt Merge(Msbt game, List<Msbt> mods)
        {
            Dictionary<string, Msbt.Message> original = game.Messages.ToDictionary(m => m.Label, StringComparer.Ordinal);
            Dictionary<string, Msbt.Message> merged = game.Messages.ToDictionary(m => m.Label, m => new Msbt.Message { Label = m.Label, Attribute = m.Attribute, Text = m.Text }, StringComparer.Ordinal);
            HashSet<string> changedBy = new HashSet<string>(StringComparer.Ordinal);
            List<string> order = game.Messages.Select(m => m.Label).ToList();
            foreach (Msbt mod in mods)
                foreach (Msbt.Message m in mod.Messages)
                {
                    Msbt.Message o, cur;
                    if (original.TryGetValue(m.Label, out o))
                    {
                        if (Same(o, m)) continue;
                        cur = merged[m.Label];
                        if (changedBy.Contains(m.Label) && !Same(cur, m)) Conflicts++;
                        else if (!changedBy.Contains(m.Label)) Changed++;
                        changedBy.Add(m.Label);
                        merged[m.Label] = new Msbt.Message { Label = m.Label, Attribute = m.Attribute, Text = m.Text };
                    }
                    else if (merged.TryGetValue(m.Label, out cur))
                    {
                        if (!Same(cur, m)) Conflicts++;
                        merged[m.Label] = new Msbt.Message { Label = m.Label, Attribute = m.Attribute, Text = m.Text };
                    }
                    else
                    {
                        merged[m.Label] = new Msbt.Message { Label = m.Label, Attribute = m.Attribute, Text = m.Text };
                        order.Add(m.Label);
                        Added++;
                    }
                }
            Msbt result = new Msbt { Slots = game.Slots, Encoding = game.Encoding, Version = game.Version, AttributeSize = game.AttributeSize, AttributeStrings = game.AttributeStrings };
            foreach (string label in order) result.Messages.Add(merged[label]);
            return result;
        }

        private static bool Same(Msbt.Message a, Msbt.Message b)
        {
            return a.Text.Length == b.Text.Length && NativeMethods.BytesEqual(a.Text, b.Text)
                && (a.Attribute ?? new byte[0]).SequenceEqual(b.Attribute ?? new byte[0]);
        }
    }
}
