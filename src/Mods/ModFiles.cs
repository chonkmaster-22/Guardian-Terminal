using System;
using System.Collections.Generic;
using System.IO;
using GuardianTerminal.Game;

namespace GuardianTerminal.Mods
{
    internal enum ModFileKind
    {
        Replace,    // the game has this file
        Add,        // a new file in a folder the game has
        SizeTable,  // the mod's own ResourceSizeTable (merged, not copied)
        Skipped,    // System/Version.txt: Savage Wind shows its own version there
        Ignored,    // not a game file type (readme, debug.log, .json, ...)
        NoFolder    // in a folder the game doesn't have: Savage Wind can't add it
    }

    internal sealed class ModFile
    {
        public string Rel;       // the game's spelling, '/'-separated
        public string FullPath;
        public ModFileKind Kind;
    }

    internal static class ModFiles
    {
        // dlc: the files are a DLC's (compared with the DLC's own RomFS): its version file is left alone, and a
        // ResourceSizeTable there is no game file (the game has one, in its own RomFS)
        public static List<ModFile> Scan(string romfsDir, VanillaIndex van, bool dlc = false)
        {
            List<ModFile> list = new List<ModFile>();
            if (!Directory.Exists(romfsDir)) return list;
            string root = Path.GetFullPath(romfsDir).TrimEnd('\\');
            foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                bool known, folderKnown;
                string canon = van.Canonicalize(rel, out known, out folderKnown);
                ModFile m = new ModFile();
                m.Rel = canon;
                m.FullPath = f;
                if (canon.Equals(SavageWindInstall.RstbRelPath, StringComparison.OrdinalIgnoreCase)) m.Kind = dlc ? ModFileKind.Ignored : ModFileKind.SizeTable;
                else if (canon.Equals("System/Version.txt", StringComparison.OrdinalIgnoreCase)
                    || canon.Equals("System/AocVersion.txt", StringComparison.OrdinalIgnoreCase)) m.Kind = ModFileKind.Skipped;
                else if (!van.KnownExtension(Path.GetExtension(canon))) m.Kind = ModFileKind.Ignored;
                else if (known) m.Kind = ModFileKind.Replace;
                else if (folderKnown) m.Kind = ModFileKind.Add;
                else m.Kind = ModFileKind.NoFolder;
                list.Add(m);
            }
            return list;
        }
    }
}
