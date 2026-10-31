using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal.Formats;

// Checks the format code against real game files and prints what a mod's pack changes.
// usage: FormatProbe <vanilla romfs> <mod romfs>
internal static class FormatProbe
{
    private static int Main(string[] args)
    {
        string van = args[0], mod = args[1];

        // 1. ResourceSizeTable round trip and how paths are keyed
        string rstbPath = Path.Combine(van, @"System\Resource\ResourceSizeTable.product.srsizetable");
        byte[] raw = Yaz0.Decompress(File.ReadAllBytes(rstbPath));
        Rstb t = Rstb.Parse(raw);
        Console.WriteLine("RSTB: {0} crc entries, {1} name entries, {2} bytes decompressed", t.CrcCount, t.NameCount, raw.Length);
        Console.WriteLine("RSTB round trip identical: " + raw.SequenceEqual(t.ToBytes()));
        byte[] lit = Yaz0.CompressLiteral(raw);
        Console.WriteLine("Yaz0 literal round trip identical: " + Yaz0.Decompress(lit).SequenceEqual(raw));
        string[] probes = {
            "Actor/Pack/Enemy_Bokoblin_Junior.bactorpack", "Actor/Pack/Enemy_Bokoblin_Junior.sbactorpack",
            "Model/Armor_001.bfres", "Model/Armor_001.sbfres", "Model/Armor_001.Tex.bfres", "Model/Armor_001.Tex.sbfres",
            "Pack/TitleBG.pack", "Pack/Bootup.pack", "Actor/ActorLink/Enemy_Bokoblin_Junior.bxml",
            "Event/Demo034_0.beventpack", "Effect/GanonBeam.esetlist", "Sound/Resource/GuardianBgm.bars",
            "Map/MainField/A-1/A-1_Dynamic.mubin", "UI/StockItem/Weapon_Bow_024.bitemico",
            "Physics/StaticCompound/MainField/A-3-0.hksc", "System/Version.txt"
        };
        foreach (string p in probes)
        {
            uint s;
            Console.WriteLine("  {0,-55} {1}", p, t.TryGet(p, out s) ? s.ToString() : "-");
        }

        // 2. Inner names of a vanilla actor pack and whether they're in the table
        byte[] ap = Yaz0.Decompress(File.ReadAllBytes(Path.Combine(van, @"Actor\Pack\Enemy_Bokoblin_Junior.sbactorpack")));
        List<SarcEntry> apE = Sarc.Parse(ap);
        int inTable = 0;
        foreach (SarcEntry e in apE) { uint s; if (t.TryGet(KeyFor(e.Name, Sarc.Extract(ap, e)), out s)) inTable++; }
        Console.WriteLine("Actor pack entries: {0}, with a table entry: {1}; e.g. {2}", apE.Count, inTable, string.Join(", ", apE.Take(4).Select(e => e.Name)));

        // 3. Vanilla vs mod TitleBG.pack
        foreach (string pack in new[] { @"Pack\TitleBG.pack" })
        {
            string vp = Path.Combine(van, pack), mp = Path.Combine(mod, pack);
            if (!File.Exists(mp)) continue;
            byte[] vb = File.ReadAllBytes(vp), mb = File.ReadAllBytes(mp);
            List<SarcEntry> ve = Sarc.Parse(vb), me = Sarc.Parse(mb);
            Dictionary<string, SarcEntry> vmap = ve.ToDictionary(e => e.Name), mmap = me.ToDictionary(e => e.Name);
            Console.WriteLine("{0}: vanilla {1} entries, mod {2} entries; BE={3}/{4}", pack, ve.Count, me.Count, Sarc.IsBigEndian(vb), Sarc.IsBigEndian(mb));
            Console.WriteLine("  only in vanilla: " + string.Join(", ", ve.Where(e => !mmap.ContainsKey(e.Name)).Select(e => e.Name).Take(30)));
            Console.WriteLine("  only in mod:     " + string.Join(", ", me.Where(e => !vmap.ContainsKey(e.Name)).Select(e => e.Name).Take(30)));
            foreach (SarcEntry e in me)
            {
                SarcEntry v;
                if (!vmap.TryGetValue(e.Name, out v)) continue;
                byte[] a = Sarc.Extract(vb, v), b = Sarc.Extract(mb, e);
                if (a.SequenceEqual(b)) continue;
                long da = Yaz0.IsYaz0(a) ? (long)Yaz0.ReadDecompressedSize(a) : a.Length, db = Yaz0.IsYaz0(b) ? (long)Yaz0.ReadDecompressedSize(b) : b.Length;
                Console.WriteLine("  differs: {0,-60} vanilla {1,9} mod {2,9} (decompressed {3} -> {4})", e.Name, a.Length, b.Length, da, db);
                byte[] ad = Yaz0.DecompressIfNeeded(a), bd = Yaz0.DecompressIfNeeded(b);
                if (Sarc.IsSarc(ad) && Sarc.IsSarc(bd))
                {
                    Dictionary<string, SarcEntry> iv = Sarc.Parse(ad).ToDictionary(x => x.Name);
                    foreach (SarcEntry x in Sarc.Parse(bd))
                    {
                        SarcEntry y;
                        if (!iv.TryGetValue(x.Name, out y)) { Console.WriteLine("      new inner: " + x.Name); continue; }
                        if (!Sarc.Extract(ad, y).SequenceEqual(Sarc.Extract(bd, x))) Console.WriteLine("      inner differs: {0} {1} -> {2}", x.Name, y.Length, x.Length);
                    }
                }
            }

            // 4. Layout: per-extension alignment (largest power of two dividing each entry's absolute offset)
            uint dataOffset = BitConverter.ToUInt32(vb, 0x0C);
            Console.WriteLine("  vanilla data offset 0x{0:X}, file size field {1}", dataOffset, BitConverter.ToUInt32(vb, 8));
            bool inOrder = true;
            for (int i = 1; i < ve.Count; i++) if (ve[i].Offset < ve[i - 1].Offset) { inOrder = false; break; }
            Console.WriteLine("  data in node order: " + inOrder);
            foreach (var g in ve.GroupBy(e => Path.GetExtension(e.Name)).OrderBy(g => g.Key))
                Console.WriteLine("    {0,-14} n={1,5} min align 0x{2:X}", g.Key, g.Count(), g.Min(e => Pow2Divisor(e.Offset)));
        }
        return 0;
    }

    private static int Pow2Divisor(int v)
    {
        if (v == 0) return 0x10000;
        return v & -v;
    }

    // The table's key for a resource: compressed files are listed under their decompressed name (".sbfres" -> ".bfres")
    private static string KeyFor(string path, byte[] data)
    {
        string ext = Path.GetExtension(path);
        if (Yaz0.IsYaz0(data) && ext.StartsWith(".s")) return path.Substring(0, path.Length - ext.Length) + "." + ext.Substring(2);
        return path;
    }
}
