using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GuardianTerminal.Formats;

// Round-trips the writer code on real game files and prints table-size ratios by file type.
// usage: WriterProbe <vanilla romfs>
internal static class WriterProbe
{
    private static int Main(string[] args)
    {
        string van = args[0];
        Rstb t = Rstb.Load(Path.Combine(van, @"System\Resource\ResourceSizeTable.product.srsizetable"));

        // 1. Yaz0 compressor: round trip + size vs Nintendo's
        string[] samples = {
            @"Actor\Pack\Enemy_Bokoblin_Junior.sbactorpack", @"Actor\Pack\GameROMPlayer.sbactorpack",
            @"Model\Armor_001.sbfres", @"Model\Link.Tex.sbfres", @"Event\Demo034_0.sbeventpack"
        };
        foreach (string s in samples)
        {
            string p = Path.Combine(van, s);
            if (!File.Exists(p)) { Console.WriteLine("missing " + s); continue; }
            byte[] orig = File.ReadAllBytes(p), dec = Yaz0.Decompress(orig);
            Stopwatch sw = Stopwatch.StartNew();
            byte[] mine = Yaz0Compressor.Compress(dec, orig.Skip(8).Take(8).ToArray());
            sw.Stop();
            bool ok = Yaz0.Decompress(mine).SequenceEqual(dec);
            Console.WriteLine("Yaz0 {0,-48} round trip {1}, size {2,9} vs Nintendo {3,9} ({4:P1}), {5} ms, header tail {6}",
                s, ok, mine.Length, orig.Length, (double)mine.Length / orig.Length, sw.ElapsedMilliseconds, BitConverter.ToString(orig, 8, 8));
        }

        // 2. SARC writer: rebuild packs from their own entries, check entries and alignment survive
        foreach (string s in new[] { @"Pack\TitleBG.pack", @"Pack\Bootup.pack", @"Actor\Pack\Enemy_Bokoblin_Junior.sbactorpack" })
        {
            if (!File.Exists(Path.Combine(van, s))) { Console.WriteLine("missing " + s); continue; }
            byte[] raw = File.ReadAllBytes(Path.Combine(van, s));
            byte[] sarc = Yaz0.DecompressIfNeeded(raw);
            List<SarcEntry> entries = Sarc.Parse(sarc);
            List<SarcFile> files = entries.Select(e => new SarcFile { Name = e.Name, Data = Sarc.Extract(sarc, e), Alignment = SarcWriter.AlignmentOf(e.Offset) }).ToList();
            Stopwatch sw = Stopwatch.StartNew();
            byte[] rebuilt = SarcWriter.Write(files);
            sw.Stop();
            List<SarcEntry> back = Sarc.Parse(rebuilt);
            Dictionary<string, SarcEntry> bmap = back.ToDictionary(e => e.Name);
            bool same = back.Count == entries.Count && entries.All(e => bmap.ContainsKey(e.Name) && Sarc.Extract(rebuilt, bmap[e.Name]).SequenceEqual(Sarc.Extract(sarc, e)));
            bool aligned = files.All(f => bmap[f.Name].Offset % f.Alignment == 0);
            bool hashOrder = true;
            for (int i = 1; i < back.Count; i++) if (SarcWriter.NameHash(back[i - 1].Name) > SarcWriter.NameHash(back[i].Name)) hashOrder = false;
            bool nodeOrderSame = entries.Select(e => e.Name).SequenceEqual(back.Select(e => e.Name));
            Console.WriteLine("SARC {0,-40} entries {1}, contents identical {2}, aligned {3}, hash-sorted {4}, same node order as Nintendo {5}, bytes identical {6}, size {7} vs {8}, {9} ms",
                s, entries.Count, same, aligned, hashOrder, nodeOrderSame, rebuilt.SequenceEqual(sarc), rebuilt.Length, sarc.Length, sw.ElapsedMilliseconds);
        }

        // 3. Table size / decompressed size, by type, for top-level files
        Dictionary<string, List<double>> ratios = new Dictionary<string, List<double>>();
        foreach (string f in Directory.EnumerateFiles(Path.Combine(van, "Model")).Take(1500))
        {
            bool comp;
            long size = Yaz0.PeekDecompressedSize(f, out comp);
            string rel = "Model/" + Path.GetFileName(f);
            string key = comp ? rel.Substring(0, rel.Length - Path.GetExtension(rel).Length) + "." + Path.GetExtension(rel).Substring(2) : rel;
            uint e;
            if (!t.TryGet(key, out e) || size == 0) continue;
            string kind = rel.Contains(".Tex") ? "Tex.bfres" : "bfres";
            if (!ratios.ContainsKey(kind)) ratios[kind] = new List<double>();
            ratios[kind].Add((double)e / size);
        }
        foreach (KeyValuePair<string, List<double>> kv in ratios)
        {
            List<double> r = kv.Value.OrderBy(x => x).ToList();
            Console.WriteLine("ratio {0,-10} n={1} min {2:F2} p10 {3:F2} median {4:F2} p90 {5:F2} max {6:F2}", kv.Key, r.Count, r[0], r[r.Count / 10], r[r.Count / 2], r[r.Count * 9 / 10], r[r.Count - 1]);
        }
        return 0;
    }
}
