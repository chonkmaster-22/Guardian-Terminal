using System;
using System.Collections.Generic;
using System.IO;

namespace GuardianTerminal.Game
{
    // The game's own RomFS (Savage Wind's extracted/romfs): which files and folders exist, with their exact case.
    // Savage Wind's overlay matches replaced files by path, but only adds new files into folders the game has, and
    // compares names case-sensitively there, so mod paths get the game's spelling.
    internal sealed class VanillaIndex
    {
        public readonly string Root;
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> dirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private VanillaIndex(string root)
        {
            Root = root;
        }

        public int FileCount { get { return files.Count; } }

        public static VanillaIndex Build(string romfs)
        {
            VanillaIndex v = new VanillaIndex(Path.GetFullPath(romfs).TrimEnd('\\'));
            int cut = v.Root.Length + 1;
            v.dirs[""] = "";
            foreach (string d in Directory.EnumerateDirectories(v.Root, "*", SearchOption.AllDirectories))
            {
                string rel = d.Substring(cut).Replace('\\', '/');
                v.dirs[rel] = rel;
            }
            foreach (string f in Directory.EnumerateFiles(v.Root, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(cut).Replace('\\', '/');
                v.files[rel] = rel;
                v.extensions.Add(Path.GetExtension(rel));
            }
            return v;
        }

        public bool KnownExtension(string ext)
        {
            return extensions.Contains(ext);
        }

        // The game's spelling of a mod path. known: the game has this file; folderKnown: the game has its folder.
        public string Canonicalize(string rel, out bool known, out bool folderKnown)
        {
            string c;
            if (files.TryGetValue(rel, out c)) { known = true; folderKnown = true; return c; }
            known = false;
            int slash = rel.LastIndexOf('/');
            string dir = slash < 0 ? "" : rel.Substring(0, slash), name = rel.Substring(slash + 1);
            string cd;
            folderKnown = dirs.TryGetValue(dir, out cd);
            if (!folderKnown) return rel;
            return cd.Length == 0 ? name : cd + "/" + name;
        }

        public bool Has(string canonicalRel)
        {
            return files.ContainsKey(canonicalRel);
        }

        public string FullPath(string rel)
        {
            return Path.Combine(Root, rel.Replace('/', '\\'));
        }

        public IEnumerable<string> AllFiles()
        {
            return files.Values;
        }

        public IEnumerable<string> FilesUnder(string relDir)
        {
            string prefix = relDir.TrimEnd('/') + "/";
            foreach (string f in files.Values)
                if (f.StartsWith(prefix, StringComparison.Ordinal)) yield return f;
        }
    }
}
