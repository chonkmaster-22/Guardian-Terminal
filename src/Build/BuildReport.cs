using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GuardianTerminal.Build
{
    // A file several mods changed, of which only one mod's version could be used
    public sealed class ConflictRecord
    {
        public string Path { get; set; }
        public List<string> Mods { get; set; }  // every mod that changed it, in load order
        public string Winner { get; set; }
    }

    // A file several mods changed that was merged, so each mod's changes are in it
    public sealed class MergedRecord
    {
        public string Path { get; set; }
        public List<string> Mods { get; set; }
        public int Conflicts { get; set; }  // values both changed (the later mod's used)
    }

    // logs/last-build.json: what the window shows about the last build (conflicts by mod)
    public sealed class BuildSummaryFile
    {
        public string Profile { get; set; }
        public string Time { get; set; }
        public string Summary { get; set; }
        public List<string> Mods { get; set; }
        public List<ConflictRecord> Conflicts { get; set; }
        public List<MergedRecord> Merged { get; set; }
        public List<string> Warnings { get; set; }
    }

    // What a build did, for logs/last-build.txt and the status line. During a build each file has its own report
    // (files are merged at the same time) and they're put together in order at the end (Absorb).
    internal sealed class BuildReport
    {
        private const int MaxListed = 200;

        public int LinkedFiles, WrittenFiles, MergedPacks, SkippedSameAsGame, DlcFiles;
        public int SizeTableGrown, SizeTableAdded, SizeTableFromMods;
        public bool SizeTableWritten;
        public readonly List<string> Mods = new List<string>();
        public readonly List<string> Conflicts = new List<string>();
        public readonly List<ConflictRecord> ConflictRecords = new List<ConflictRecord>();
        public readonly List<MergedRecord> MergedRecords = new List<MergedRecord>();
        public readonly List<string> Merges = new List<string>();
        public readonly List<string> LeftOut = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> SizeChanges = new List<string>();
        public readonly List<string> EntryMerges = new List<string>();
        public int EntryConflicts;
        public TimeSpan Elapsed;

        public void AddConflict(string path, IEnumerable<string> mods, string winner)
        {
            List<string> all = mods.Distinct().ToList();
            if (!all.Contains(winner)) all.Add(winner);
            Conflicts.Add(string.Format("{0}: changed by {1}; using {2}", path, string.Join(", ", all), winner));
            ConflictRecords.Add(new ConflictRecord { Path = path, Mods = all, Winner = winner });
        }

        public void AddMerged(string path, IEnumerable<string> mods, int conflicts, string line)
        {
            EntryMerges.Add(line);
            EntryConflicts += conflicts;
            MergedRecords.Add(new MergedRecord { Path = path, Mods = mods.Distinct().ToList(), Conflicts = conflicts });
        }

        // Adds another (a file's own) report to this one
        public void Absorb(BuildReport o)
        {
            LinkedFiles += o.LinkedFiles; WrittenFiles += o.WrittenFiles; MergedPacks += o.MergedPacks;
            SkippedSameAsGame += o.SkippedSameAsGame; DlcFiles += o.DlcFiles;
            SizeTableGrown += o.SizeTableGrown; SizeTableAdded += o.SizeTableAdded; SizeTableFromMods += o.SizeTableFromMods;
            EntryConflicts += o.EntryConflicts;
            Conflicts.AddRange(o.Conflicts); ConflictRecords.AddRange(o.ConflictRecords); MergedRecords.AddRange(o.MergedRecords);
            Merges.AddRange(o.Merges); LeftOut.AddRange(o.LeftOut); Warnings.AddRange(o.Warnings); Notes.AddRange(o.Notes);
            SizeChanges.AddRange(o.SizeChanges); EntryMerges.AddRange(o.EntryMerges);
        }

        public void SaveSummary(string path, string profile)
        {
            Json.Write(path, new BuildSummaryFile
            {
                Profile = profile,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Summary = Summary(),
                Mods = Mods.ToList(),
                Conflicts = ConflictRecords.ToList(),
                Merged = MergedRecords.ToList(),
                Warnings = Warnings.ToList()
            });
        }

        // Several mods' versions of a BYML file merged entry by entry (BymlMerger): the new bytes, or null when it
        // isn't BYML (the caller then goes file by file)
        public byte[] MergeEntries(string label, byte[] gameRaw, List<ModVersion> mods)
        {
            BymlMerger merger;
            byte[] bytes;
            try { bytes = BymlMerger.TryMerge(gameRaw, mods.Select(m => m.Raw).ToList(), out merger); }
            catch (Exception e)
            {
                Warnings.Add(label + ": couldn't be merged entry by entry (" + e.Message + "), so the later mod's is used as it is.");
                return null;
            }
            if (bytes == null) return null;
            AddMerged(label, mods.Select(m => m.ModName), merger.Conflicts, string.Format("{0}: {1}{2}", label, string.Join(" + ", mods.Select(m => m.ModName)),
                merger.Conflicts == 0 ? "" : string.Format("; {0} value{1} both changed (later mod's used, larger spawn amounts), e.g. {2}",
                    merger.Conflicts, merger.Conflicts == 1 ? "" : "s", string.Join(", ", merger.ConflictPaths.Take(3)))));
            return bytes;
        }

        // Several mods' versions of a text file (.msbt) merged message by message (MsbtMerger): the new bytes, or
        // null when it can't be (the caller then goes file by file). When the mods change nothing in it (a copy from
        // an older game version that only lacks some of the game's messages), the game's own bytes, unreported.
        public byte[] MergeTexts(string label, byte[] gameRaw, List<ModVersion> mods)
        {
            MsbtMerger merger;
            byte[] bytes;
            try { bytes = MsbtMerger.TryMerge(gameRaw, mods.Select(m => m.Raw).ToList(), out merger); }
            catch (Exception e)
            {
                Warnings.Add(label + ": its texts couldn't be merged one by one (" + e.Message + "), so the later mod's file is used as it is.");
                return null;
            }
            if (bytes == null) return null;
            if (merger.Changed == 0 && merger.Added == 0 && merger.Conflicts == 0) return gameRaw;
            AddMerged(label, mods.Select(m => m.ModName), merger.Conflicts, string.Format("{0}: {1} ({2} text{3} changed, {4} added{5})",
                label, string.Join(" + ", mods.Select(m => m.ModName)), merger.Changed, merger.Changed == 1 ? "" : "s", merger.Added,
                merger.Conflicts == 0 ? "" : string.Format("; {0} changed by both, the later mod's used", merger.Conflicts)));
            return bytes;
        }

        public int OverlayFiles { get { return LinkedFiles + WrittenFiles; } }

        // In a few words, for the window (in the player's language; the report itself stays in English)
        public string Summary()
        {
            string s = L.P(Mods.Count, "{1} game files changed by {0} mod", "{1} game files changed by {0} mods", OverlayFiles);
            if (DlcFiles > 0) s += " " + L.F("({0} of them in the DLC)", DlcFiles);
            if (MergedPacks > 0) s += ", " + L.P(MergedPacks, "{0} pack merged", "{0} packs merged");
            if (EntryMerges.Count > 0) s += ", " + L.P(EntryMerges.Count, "{0} file merged entry by entry", "{0} files merged entry by entry");
            if (Conflicts.Count > 0) s += ", " + L.P(Conflicts.Count, "{0} conflict", "{0} conflicts");
            return s;
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Guardian Terminal - build report, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(string.Format("{0} game files changed by {1} mod(s) ({2} in the DLC), {3} pack(s) merged, {4} file(s) merged entry by entry, {5} conflict(s) (took {6:F1} s)",
                OverlayFiles, Mods.Count, DlcFiles, MergedPacks, EntryMerges.Count, Conflicts.Count, Elapsed.TotalSeconds));
            sb.AppendLine();
            sb.AppendLine("Mods, in load order (later ones win):");
            foreach (string m in Mods) sb.AppendLine("  " + m);
            sb.AppendLine();
            sb.AppendLine(string.Format("Files: {0} linked from mods, {1} written (merged packs), {2} left out because they're the same as the game's.",
                LinkedFiles, WrittenFiles, SkippedSameAsGame));
            sb.AppendLine(string.Format("ResourceSizeTable: {0}; {1} entries taken from mods' own tables, {2} grown for bigger files, {3} added for new files.",
                SizeTableWritten ? "written" : "unchanged (not needed)", SizeTableFromMods, SizeTableGrown, SizeTableAdded));
            Section(sb, "Warnings", Warnings);
            Section(sb, "Notes", Notes);
            Section(sb, "Conflicts (the same file changed by more than one mod; the later mod's version is used)", Conflicts, int.MaxValue);
            Section(sb, "Merged entry by entry (files changed by several mods: map objects, spawn lists, ... each mod's changes kept)", EntryMerges, int.MaxValue);
            Section(sb, "Merged packs (your game's pack, with the files the mods changed inside it)", Merges);
            Section(sb, "Left out", LeftOut);
            Section(sb, "Resource size changes", SizeChanges);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void Section(StringBuilder sb, string title, List<string> lines, int max = MaxListed)
        {
            if (lines.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine(title + ":");
            foreach (string l in lines.Take(max)) sb.AppendLine("  " + l);
            if (lines.Count > max) sb.AppendLine(string.Format("  ... and {0} more", lines.Count - max));
        }
    }
}
