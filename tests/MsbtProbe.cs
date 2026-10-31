using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GuardianTerminal.Formats;

// MsbtProbe <Bootup_XXxx.pack> [name filter]: the message archive inside it, and each .msbt's sections
// MsbtProbe extract <Bootup_XXxx.pack> <msbt name> <out>
internal static class MsbtProbe
{
    private static byte[] Inner(byte[] pack, out string name)
    {
        byte[] d = Yaz0.DecompressIfNeeded(pack);
        SarcEntry e = Sarc.Parse(d).First(x => x.Name.EndsWith(".ssarc"));
        name = e.Name;
        return Yaz0.DecompressIfNeeded(Sarc.Extract(d, e));
    }

    private static int Main(string[] args)
    {
        string msgName;
        if (args[0] == "verify")  // verify <game pack> <merged pack> <mod packs...>: every mod's message is in the merge
        {
            Func<string, Dictionary<string, GuardianTerminal.Formats.Msbt>> load = p =>
            {
                string n;
                byte[] arc = Inner(File.ReadAllBytes(p), out n);
                Dictionary<string, GuardianTerminal.Formats.Msbt> r = new Dictionary<string, GuardianTerminal.Formats.Msbt>();
                foreach (SarcEntry e in Sarc.Parse(arc).Where(x => x.Name.EndsWith(".msbt")))
                    r[e.Name] = GuardianTerminal.Formats.Msbt.Parse(Sarc.Extract(arc, e));
                return r;
            };
            var game = load(args[1]); var merged = load(args[2]);
            int missingFile = 0, missingLabel = 0, wrongText = 0, checkedLabels = 0, gameKept = 0, gameLost = 0, both = 0;
            List<Dictionary<string, GuardianTerminal.Formats.Msbt>> mods = args.Skip(3).Select(load).ToList();
            foreach (var kv in game)
            {
                GuardianTerminal.Formats.Msbt mm;
                if (!merged.TryGetValue(kv.Key, out mm)) { missingFile++; continue; }
                if (mm == null || kv.Value == null) { Console.WriteLine("unreadable: " + kv.Key + (mm == null ? " (merged)" : " (game)")); continue; }
                HashSet<string> have = new HashSet<string>(mm.Messages.Select(x => x.Label));
                foreach (var gm in kv.Value.Messages) if (have.Contains(gm.Label)) gameKept++; else gameLost++;
            }
            foreach (var mod in mods)
                foreach (var kv in mod)
                {
                    GuardianTerminal.Formats.Msbt mm;
                    if (kv.Value == null) continue;
                    if (!merged.TryGetValue(kv.Key, out mm)) { missingFile++; continue; }
                    if (mm == null) continue;
                    Dictionary<string, byte[]> mt = mm.Messages.ToDictionary(x => x.Label, x => x.Text);
                    GuardianTerminal.Formats.Msbt g;
                    game.TryGetValue(kv.Key, out g);
                    Dictionary<string, byte[]> gt = g == null ? new Dictionary<string, byte[]>() : g.Messages.ToDictionary(x => x.Label, x => x.Text);
                    foreach (var mg in kv.Value.Messages)
                    {
                        byte[] gtext;
                        if (gt.TryGetValue(mg.Label, out gtext) && gtext.SequenceEqual(mg.Text)) continue;  // unchanged
                        checkedLabels++;
                        byte[] got;
                        if (!mt.TryGetValue(mg.Label, out got)) { missingLabel++; continue; }
                        if (got.SequenceEqual(mg.Text)) continue;
                        // another mod changed it too?
                        bool other = mods.Where(o => o != mod).Any(o => o.ContainsKey(kv.Key) && o[kv.Key] != null && o[kv.Key].Messages.Any(x => x.Label == mg.Label && !x.Text.SequenceEqual(mg.Text) && !(gt.ContainsKey(mg.Label) && gt[mg.Label].SequenceEqual(x.Text))));
                        if (other) both++; else wrongText++;
                    }
                }
            Console.WriteLine("game messages kept {0}, lost {1}; mod changes checked {2}: missing {3}, wrong {4}, changed by both (later mod's) {5}; files missing {6}",
                gameKept, gameLost, checkedLabels, missingLabel, wrongText, both, missingFile);
            return 0;
        }
        if (args[0] == "roundtrip")  // roundtrip <Bootup_XXxx.pack>: every .msbt read and written back
        {
            byte[] arc = Inner(File.ReadAllBytes(args[1]), out msgName);
            int same = 0, differ = 0, unreadable = 0, hashOk = 0, hashBad = 0;
            foreach (SarcEntry e in Sarc.Parse(arc).Where(x => x.Name.EndsWith(".msbt")))
            {
                byte[] d = Sarc.Extract(arc, e);
                GuardianTerminal.Formats.Msbt m = GuardianTerminal.Formats.Msbt.Parse(d);
                if (m == null) { unreadable++; Console.WriteLine("unreadable " + e.Name); continue; }
                byte[] w = m.Write();
                if (w.Length == d.Length && w.SequenceEqual(d)) same++;
                else
                {
                    differ++;
                    if (differ <= 3)
                    {
                        int i = 0; while (i < Math.Min(w.Length, d.Length) && w[i] == d[i]) i++;
                        Console.WriteLine("differs {0}: lengths {1} / {2}, first difference at 0x{3:X}", e.Name, d.Length, w.Length, i);
                    }
                }
                GuardianTerminal.Formats.Msbt again = GuardianTerminal.Formats.Msbt.Parse(w);
                bool ok = again != null && again.Messages.Count == m.Messages.Count && again.Messages.Zip(m.Messages, (x, y) => x.Label == y.Label && x.Text.SequenceEqual(y.Text)).All(b => b);
                if (ok) hashOk++; else hashBad++;
            }
            Console.WriteLine("{0}: {1} identical, {2} different bytes, {3} unreadable; reread the same: {4}, not: {5}", msgName, same, differ, unreadable, hashOk, hashBad);
            return 0;
        }
        if (args[0] == "extract")
        {
            byte[] arc = Inner(File.ReadAllBytes(args[1]), out msgName);
            SarcEntry e = Sarc.Parse(arc).First(x => x.Name == args[2]);
            File.WriteAllBytes(args[3], Sarc.Extract(arc, e));
            return 0;
        }
        byte[] msg = Inner(File.ReadAllBytes(args[0]), out msgName);
        List<SarcEntry> entries = Sarc.Parse(msg);
        Console.WriteLine("{0}: {1} files", msgName, entries.Count);
        foreach (SarcEntry e in entries.Where(x => args.Length < 2 || x.Name.Contains(args[1])).Take(6))
        {
            byte[] m = Sarc.Extract(msg, e);
            Console.WriteLine("{0} ({1} bytes) magic {2} bom {3:X2}{4:X2} enc {5} ver {6} sections {7} size {8}", e.Name, m.Length,
                Encoding.ASCII.GetString(m, 0, 8), m[8], m[9], m[0xC], m[0xD], BitConverter.ToUInt16(m, 0xE), BitConverter.ToUInt32(m, 0x12));
            int p = 0x20;
            for (int s = 0; s < BitConverter.ToUInt16(m, 0xE) && p + 16 <= m.Length; s++)
            {
                string magic = Encoding.ASCII.GetString(m, p, 4);
                uint size = BitConverter.ToUInt32(m, p + 4);
                string extra = "";
                if (magic == "ATR1") extra = string.Format(" count {0} entry size {1}", BitConverter.ToUInt32(m, p + 16), BitConverter.ToUInt32(m, p + 20));
                if (magic == "TXT2" || magic == "LBL1") extra = string.Format(" count {0}", BitConverter.ToUInt32(m, p + 16));
                Console.WriteLine("   {0} size {1}{2}", magic, size, extra);
                p += 16 + (int)size;
                p = (p + 15) & ~15;
            }
        }
        return 0;
    }
}
