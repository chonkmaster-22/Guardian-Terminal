using System;
using System.IO;
using System.Linq;
using GuardianTerminal.Build;
using GuardianTerminal.Formats;

// How the ResourceSizeTable names the DLC's files: top-level ones and those inside its packs.
// usage: DlcKeyProbe <game romfs> <dlc romfs>
internal static class DlcKeyProbe
{
    private static int Main(string[] args)
    {
        Rstb t = Rstb.Load(Path.Combine(args[0], @"System\Resource\ResourceSizeTable.product.srsizetable"));
        string dlc = args[1];
        int top = 0, topPre = 0, topPlain = 0;
        foreach (string f in Directory.EnumerateFiles(dlc, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(dlc.Length + 1).Replace('\\', '/');
            byte[] head = new byte[16];
            using (FileStream fs = File.OpenRead(f)) fs.Read(head, 0, 16);
            string key = RstbUpdater.KeyFor(rel, Yaz0.IsYaz0(head));
            uint v;
            top++;
            if (t.TryGet("Aoc/0010/" + key, out v)) topPre++;
            if (t.TryGet(key, out v)) topPlain++;
        }
        Console.WriteLine("DLC top-level files: {0}; with an Aoc/0010/ entry: {1}; with a plain entry: {2}", top, topPre, topPlain);
        foreach (string pack in Directory.GetFiles(Path.Combine(dlc, "Pack"), "*.pack"))
        {
            byte[] d = File.ReadAllBytes(pack);
            var es = Sarc.Parse(d);
            int pre = 0, plain = 0;
            foreach (var e in es)
            {
                byte[] raw = Sarc.Extract(d, e);
                string key = RstbUpdater.KeyFor(e.Name, Yaz0.IsYaz0(raw));
                uint v;
                if (t.TryGet("Aoc/0010/" + key, out v)) pre++;
                if (t.TryGet(key, out v)) plain++;
            }
            Console.WriteLine("{0}: {1} files (e.g. {2}); Aoc/0010/ entries {3}, plain entries {4}", Path.GetFileName(pack), es.Count,
                string.Join(", ", es.Take(2).Select(e => e.Name)), pre, plain);
        }
        return 0;
    }
}
