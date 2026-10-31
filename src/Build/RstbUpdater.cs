using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal;
using GuardianTerminal.Formats;
using GuardianTerminal.Game;

namespace GuardianTerminal.Build
{
    // Keeps the ResourceSizeTable in step with the modded files. The game sets aside memory for each resource by its
    // entry (heap = max(entry, file size + a fixed part)), so a modded file bigger than the game's needs a bigger
    // entry or the game runs out of memory loading it; bfres and esetlist resources need an entry at all.
    //   - Mods' own tables: every entry bigger than ours is taken.
    //   - A file bigger than the game's: its entry grows by the extra size times the entry's own size/file ratio
    //     (1..3: models need ~1.8x their size, textures ~1x), plus 32 (as Savage Wind grows voice banks).
    //   - Files inside archives are compared the same way, by their own paths.
    //   - A new bfres / esetlist with no entry gets an estimate; other new files are sized by the game itself.
    // Compressed files are listed under their decompressed name (Model/X.sbfres -> Model/X.bfres).
    internal sealed class RstbUpdater
    {
        private readonly Rstb game;
        private readonly Rstb table;
        private readonly BuildReport report;
        private readonly byte[] headerTail = new byte[8];

        public RstbUpdater(string gameRstbPath, BuildReport report)
        {
            byte[] raw = File.ReadAllBytes(gameRstbPath);
            if (Yaz0.IsYaz0(raw)) Buffer.BlockCopy(raw, 8, headerTail, 0, 8);
            game = Rstb.Parse(Yaz0.DecompressIfNeeded(raw));
            table = game.Clone();
            this.report = report;
        }

        public bool Changed { get { return table.CountDifferences(game) > 0; } }

        public void TakeModTable(string modName, string path)
        {
            try
            {
                int n = table.MergeMax(Rstb.Load(path));
                report.SizeTableFromMods += n;
            }
            catch (Exception e)
            {
                report.Warnings.Add(modName + ": its ResourceSizeTable couldn't be read (" + e.Message + "), so it was left out.");
            }
        }

        // A BCML package's own sizes (its logs/rstb.json: {"Model/Armor_043.bfres": 2642766, ...}, worked out by BCML
        // for its files): each bigger than ours is taken. (0 or less: BCML's "no entry", left alone.)
        public void TakeBcmlSizes(string modName, string path)
        {
            Dictionary<string, object> sizes = Json.ReadObject(path);
            if (sizes == null)
            {
                report.Warnings.Add(modName + ": its BCML size list couldn't be read, so it was left out.");
                return;
            }
            int n = 0;
            foreach (KeyValuePair<string, object> kv in sizes)
            {
                long v;
                try { v = Convert.ToInt64(kv.Value); }
                catch (Exception) { continue; }
                if (v <= 0 || v > uint.MaxValue) continue;
                uint current;
                if (table.TryGet(kv.Key, out current) && current >= v) continue;
                table.Set(kv.Key, (uint)v);
                n++;
            }
            report.SizeTableFromMods += n;
        }

        // newRaw: the file as it goes into the overlay; gameRaw: the game's own, or null for a new file.
        // prefix: "Aoc/0010/" for a file of the DLC (the game asks for those by that name), else "".
        // (Several files are compared at the same time during a build: the table and the report are locked.)
        public void Compare(string path, byte[] newRaw, byte[] gameRaw, string prefix)
        {
            Compare(path, newRaw, gameRaw, prefix, true);
        }

        // The same for files on disk (a mod's file, linked as it is): only an archive is read whole (the files in it
        // are compared too); for anything else its length, or its Yaz0 header's size, is all that's needed.
        public void CompareFiles(string path, string newFile, string gameFile, string prefix)
        {
            if (gameFile != null && FileCompare.Same(newFile, gameFile)) return;
            if (FileCompare.IsArchive(newFile))
            {
                Compare(path, File.ReadAllBytes(newFile), gameFile == null ? null : File.ReadAllBytes(gameFile), prefix, true);
                return;
            }
            bool compressed, gameCompressed;
            long newSize = Yaz0.PeekDecompressedSize(newFile, out compressed);
            long oldSize = gameFile == null ? -1 : Yaz0.PeekDecompressedSize(gameFile, out gameCompressed);
            Sizes(path, newSize, compressed, oldSize, prefix, true);
        }

        private readonly object sync = new object();

        private void Compare(string path, byte[] newRaw, byte[] gameRaw, string prefix, bool topLevel)
        {
            if (gameRaw != null && NativeMethods.BytesEqual(newRaw, gameRaw)) return;
            bool compressed = Yaz0.IsYaz0(newRaw);
            long newSize = compressed ? (long)Yaz0.ReadDecompressedSize(newRaw) : newRaw.Length;
            long oldSize = gameRaw == null ? -1 : Yaz0.IsYaz0(gameRaw) ? (long)Yaz0.ReadDecompressedSize(gameRaw) : gameRaw.Length;
            Sizes(path, newSize, compressed, oldSize, prefix, topLevel);
            if (!PackMerger.IsArchive(newRaw)) return;
            Inner(path, newRaw, gameRaw, prefix);
        }

        // oldSize: the game's file's (decompressed) size, or -1 for a new file
        private void Sizes(string path, long newSize, bool compressed, long oldSize, string prefix, bool topLevel)
        {
            lock (sync)
            {
                List<string> keys = KeysFor(prefix, KeyFor(path, compressed), topLevel);
                string key = keys[0];
                if (oldSize >= 0)
                {
                    foreach (string k in keys)
                    {
                        uint current, original;
                        if (newSize <= oldSize || !table.TryGet(k, out current)) continue;
                        if (!game.TryGet(k, out original)) original = current;
                        uint grown = Grow(original, oldSize, newSize);
                        if (grown > current)
                        {
                            table.Set(k, grown);
                            report.SizeTableGrown++;
                            report.SizeChanges.Add(string.Format("{0}: {1} -> {2} (file {3} -> {4} bytes)", k, current, grown, oldSize, newSize));
                        }
                    }
                }
                else
                {
                    uint current;
                    if (!keys.Any(k => table.TryGet(k, out current)) && NeedsEntry(key))
                    {
                        uint estimate = Estimate(key, newSize);
                        table.Set(key, estimate);
                        report.SizeTableAdded++;
                        report.SizeChanges.Add(string.Format("{0}: new, {1} (file {2} bytes)", key, estimate, newSize));
                    }
                }
            }
        }

        // the files inside an archive, each by its own path
        private void Inner(string path, byte[] newRaw, byte[] gameRaw, string prefix)
        {
            List<SarcEntry> newEntries, gameEntries = null;
            byte[] newData, gameData = null;
            try
            {
                newData = Yaz0.DecompressIfNeeded(newRaw);
                newEntries = Sarc.Parse(newData);
                if (gameRaw != null && PackMerger.IsArchive(gameRaw))
                {
                    gameData = Yaz0.DecompressIfNeeded(gameRaw);
                    gameEntries = Sarc.Parse(gameData);
                }
            }
            catch (Exception e)
            {
                lock (sync) report.Warnings.Add(path + ": couldn't be read as an archive (" + e.Message + ")");
                return;
            }
            Dictionary<string, SarcEntry> gameMap = new Dictionary<string, SarcEntry>(StringComparer.Ordinal);
            if (gameEntries != null) foreach (SarcEntry e in gameEntries) gameMap[e.Name] = e;
            foreach (SarcEntry e in newEntries)
            {
                SarcEntry ge;
                bool inGame = gameMap.TryGetValue(e.Name, out ge);
                if (inGame && ge.Length == e.Length && NativeMethods.BytesEqual(newData, e.Offset, gameData, ge.Offset, e.Length)) continue;
                Compare(e.Name, Sarc.Extract(newData, e), inGame ? Sarc.Extract(gameData, ge) : null, prefix, false);
            }
        }

        // The table's names for a file: a DLC file's own is "Aoc/0010/<path>"; the files inside the DLC's packs are listed
        // under that, under their plain name, or both (all of AocMainField.pack's; the dungeons' map files only prefixed,
        // the rest of a dungeon pack only plain), so each name the table has is kept in step. The first is the one a new
        // entry gets.
        private List<string> KeysFor(string prefix, string key, bool topLevel)
        {
            List<string> keys = new List<string> { prefix + key };
            uint v;
            if (prefix.Length > 0 && !topLevel && table.TryGet(key, out v)) keys.Add(key);
            return keys;
        }

        // Savage Wind (Alpha 4 on) puts the button icons in the HUD's prompts by rewriting four layouts inside
        // Pack/Bootup.pack's Layout/Common.sblarc (hud_letters_patch, runtime/services/fs.c): the patched copies go at
        // the end of the archive, which grows by up to 0x80 + the layout + 64 bytes for each, and the archive's entry
        // (Layout/Common.blarc) grows by that, rounded up to 32, plus 32 - but not when the overlay has its own table.
        // So when ours replaces it (and the overlay leaves Bootup.pack to the game, which is when it's patched), ours
        // makes the same room. (Room to spare costs nothing; an older Savage Wind just doesn't use it.)
        public void AddHudLettersRoom(VanillaIndex van)
        {
            const string key = "Layout/Common.blarc";
            string[] layouts = { "blyt/PaDoCommandA_00.bflyt", "blyt/PaDoCommandB_00.bflyt", "blyt/PaDoCommandX_00.bflyt", "blyt/PaDoCommandY_00.bflyt" };
            uint current;
            if (!van.Has("Pack/Bootup.pack") || !table.TryGet(key, out current)) return;
            try
            {
                byte[] pack = File.ReadAllBytes(van.FullPath("Pack/Bootup.pack"));
                SarcEntry arcEntry = Sarc.Parse(pack).FirstOrDefault(e => e.Name == "Layout/Common.sblarc");
                if (arcEntry == null) return;
                byte[] arc = Yaz0.DecompressIfNeeded(Sarc.Extract(pack, arcEntry));
                Dictionary<string, SarcEntry> inner = Sarc.Parse(arc).ToDictionary(e => e.Name);
                long grow = 0;
                foreach (string l in layouts)
                {
                    SarcEntry e;
                    if (!inner.TryGetValue(l, out e)) return;
                    grow += 0x80 + e.Length + 64;
                }
                uint add = (uint)(((grow + 31) & ~31L) + 32);
                table.Set(key, current + add);
                report.SizeChanges.Add(string.Format("{0}: {1} -> {2} (room for Savage Wind's HUD button icons)", key, current, current + add));
            }
            catch (Exception e)
            {
                report.Warnings.Add("Couldn't make room for Savage Wind's HUD button icons in the size table: " + e.Message);
            }
        }

        // A game RomFS with the DLC's files in it (a mod that put Pack/AocMainField.pack there): Savage Wind then grows
        // each path's entry to its "Aoc/0010/<path>" entry when that one is bigger (romfs_has_dlc_merged, rstb_patch),
        // which it skips when the overlay has its own table; so it's done here. The names: as Savage Wind's rstb_name
        // (an extension starting with 's' loses it, but .sarc).
        public void GrowToDlcEntries(VanillaIndex van)
        {
            int n = 0;
            foreach (string path in van.AllFiles())
            {
                string name = path;
                int slash = name.LastIndexOf('/'), dot = name.LastIndexOf('.');
                if (dot > slash && dot + 1 < name.Length && name[dot + 1] == 's' && name.Substring(dot) != ".sarc")
                    name = name.Substring(0, dot + 1) + name.Substring(dot + 2);
                uint baseSize, dlcSize;
                if (!table.TryGet(name, out baseSize) || !table.TryGet(SavageWindInstall.RstbDlcPrefix + name, out dlcSize) || dlcSize <= baseSize) continue;
                table.Set(name, dlcSize);
                n++;
            }
            if (n > 0) report.SizeChanges.Add(string.Format("{0} entries grown to their DLC entries (a mod puts DLC files in the game's own folders)", n));
        }

        // Savage Wind grows these entries itself when voices are redirected to another language, but not when the
        // overlay has its own table; so with a table of ours, the same growth goes in it (runtime/services/fs.c,
        // voice_redirect + rstb_patch): every Voice/<other>/<path> whose <chosen> file is bigger grows by the
        // difference, rounded up to 32, plus 32.
        public void AddVoiceRedirect(VanillaIndex van, string chosen)
        {
            if (chosen == null) return;
            string prefix = "Voice/" + chosen + "/";
            Dictionary<string, long> chosenSizes = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (string f in van.FilesUnder("Voice/" + chosen))
                chosenSizes[f.Substring(prefix.Length)] = new FileInfo(van.FullPath(f)).Length;
            int n = 0;
            foreach (string f in van.FilesUnder("Voice"))
            {
                if (f.StartsWith(prefix, StringComparison.Ordinal) || f.StartsWith("Voice/Common/", StringComparison.Ordinal)) continue;
                int slash = f.IndexOf('/', 6);
                if (slash < 0) continue;
                long size;
                if (!chosenSizes.TryGetValue(f.Substring(slash + 1), out size)) continue;
                long own = new FileInfo(van.FullPath(f)).Length;
                uint entry;
                if (size <= own || !table.TryGet(f, out entry)) continue;
                table.Set(f, entry + (uint)(((size - own) + 31) & ~31L) + 32);
                n++;
            }
            if (n > 0) report.SizeChanges.Add(string.Format("Voices in {0}: {1} entries grown, as Savage Wind does for voice_language = {0}", chosen, n));
        }

        public byte[] ToFile()
        {
            return Yaz0Compressor.Compress(table.ToBytes(), headerTail);
        }

        public static string KeyFor(string path, bool compressed)
        {
            int slash = path.LastIndexOf('/'), dot = path.LastIndexOf('.');
            if (compressed && dot > slash && dot + 2 < path.Length && path[dot + 1] == 's')
                return path.Substring(0, dot + 1) + path.Substring(dot + 2);
            return path;
        }

        private static uint Grow(uint entry, long oldSize, long newSize)
        {
            double ratio = oldSize > 0 ? (double)entry / oldSize : 1.0;
            ratio = Math.Max(1.0, Math.Min(3.0, ratio));
            long extra = (long)Math.Ceiling((newSize - oldSize) * ratio);
            long grown = entry + ((extra + 31) & ~31L) + 32;
            return (uint)Math.Min(grown, uint.MaxValue);
        }

        private static bool NeedsEntry(string key)
        {
            return key.EndsWith(".bfres", StringComparison.Ordinal) || key.EndsWith(".esetlist", StringComparison.Ordinal);
        }

        // From the game's own tables: textures need ~1.0-1.12x their size, models ~1.2-2.5x (90% of them).
        private static uint Estimate(string key, long size)
        {
            double factor = key.EndsWith(".Tex.bfres", StringComparison.Ordinal) || key.Contains(".Tex1") || key.Contains(".Tex2") ? 1.15
                : key.EndsWith(".bfres", StringComparison.Ordinal) ? 2.6 : 1.5;
            long v = (((long)Math.Ceiling(size * factor) + 31) & ~31L) + 0x1000;
            return (uint)Math.Min(v, uint.MaxValue);
        }
    }
}
