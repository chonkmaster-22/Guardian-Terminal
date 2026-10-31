using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal;
using GuardianTerminal.Formats;

// Splits a mod's pack into parts, to find which of its files a crash comes from.
//   PackPart list <mod pack> <game pack>               the files the mod's pack changes or adds, one per line
//   PackPart make <mod pack> <names file> <out pack>   a pack with only those of the mod's files (the manager merges
//                                                      it with the game's, which fills in the rest)
internal static class PackPart
{
    private static int Main(string[] args)
    {
        byte[] mod = Yaz0.DecompressIfNeeded(File.ReadAllBytes(args[1]));
        List<SarcEntry> entries = Sarc.Parse(mod);
        if (args[0] == "list")
        {
            byte[] game = Yaz0.DecompressIfNeeded(File.ReadAllBytes(args[2]));
            Dictionary<string, SarcEntry> g = Sarc.Parse(game).ToDictionary(e => e.Name);
            foreach (SarcEntry e in entries)
            {
                SarcEntry ge;
                if (!g.TryGetValue(e.Name, out ge) || !NativeMethods.BytesEqual(Sarc.Extract(mod, e), Sarc.Extract(game, ge)))
                    Console.WriteLine(e.Name);
            }
            return 0;
        }
        if (args[0] == "size")  // PackPart size <pack> <name>...: stored and unpacked sizes of files in a pack
        {
            foreach (string name in args.Skip(2))
            {
                SarcEntry e = entries.FirstOrDefault(x => x.Name == name);
                if (e == null) { Console.WriteLine("{0}: -", name); continue; }
                byte[] d = Sarc.Extract(mod, e);
                Console.WriteLine("{0}: {1} stored, {2} unpacked", name, d.Length, Yaz0.DecompressIfNeeded(d).Length);
            }
            return 0;
        }
        if (args[0] == "extract")  // PackPart extract <pack> <name> <out file>: one file of a pack, unpacked
        {
            SarcEntry e = entries.First(x => x.Name == args[2]);
            File.WriteAllBytes(args[3], Yaz0.DecompressIfNeeded(Sarc.Extract(mod, e)));
            return 0;
        }
        if (args[0] == "make")
        {
            HashSet<string> keep = new HashSet<string>(File.ReadAllLines(args[2]).Where(l => l.Length > 0));
            List<SarcFile> files = entries.Where(e => keep.Contains(e.Name))
                .Select(e => new SarcFile { Name = e.Name, Data = Sarc.Extract(mod, e), Alignment = SarcWriter.AlignmentOf(e.Offset) }).ToList();
            File.WriteAllBytes(args[3], SarcWriter.Write(files));
            Console.WriteLine("{0} of {1} files", files.Count, entries.Count);
            return 0;
        }
        return 1;
    }
}
