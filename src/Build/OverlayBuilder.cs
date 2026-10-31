using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GuardianTerminal.Formats;
using GuardianTerminal.Game;
using GuardianTerminal.Mods;

namespace GuardianTerminal.Build
{
    internal sealed class BuildResult
    {
        public string OverlayDir;  // null: nothing to load (play unmodded)
        public BuildReport Report;
        public bool UpToDate;      // the last build was still current
        public string Summary;     // what the output holds, for the status line
    }

    // Where a merge goes: Savage Wind's overlay folder (the DLC's files in its aoc/0010/, plus Savage Wind's own
    // adjustments that an overlay table replaces), or a Switch's SD card (Atmosphere's LayeredFS: the game's and the
    // DLC's title folders; the game there makes no such adjustments).
    internal sealed class BuildTarget
    {
        public const string GameTitle = "01007EF00011E000";
        public const string DlcTitle = "01007EF00011F001";

        public string BaseDir, DlcDir;
        public bool ForSavageWind;

        public static BuildTarget Overlay()
        {
            return new BuildTarget
            {
                BaseDir = AppPaths.Overlay,
                DlcDir = Path.Combine(AppPaths.Overlay, SavageWindInstall.OverlayDlcFolder.Replace('/', '\\').TrimEnd('\\')),
                ForSavageWind = true
            };
        }

        public static BuildTarget SdCard(string sdRoot)
        {
            string contents = Path.Combine(sdRoot, "atmosphere", "contents");
            return new BuildTarget
            {
                BaseDir = Path.Combine(contents, GameTitle, "romfs"),
                DlcDir = Path.Combine(contents, DlcTitle, "romfs"),
                ForSavageWind = false
            };
        }
    }

    // Merges the enabled mods, in load order, into a target: plain files are hard-linked from the mods (no copies; a
    // copy on another drive), the game's archives are merged (PackMerger), the files several mods change are merged
    // entry by entry where the format allows (BYML, texts, the actor list, the game data flags), files the same as
    // the game's are left out, and the ResourceSizeTable is updated (RstbUpdater). Mods' DLC files go with the DLC's
    // when it's set up. The overlay is rebuilt only when the mods, their order, the game files, the DLC or the voice
    // setting changed.
    //
    // Big mods: a file is read whole only when it's merged; otherwise only its length and first bytes are looked at
    // (comparing two files of the same length goes 1 MB at a time). Files are processed several at once, the biggest
    // first, each with its own report; the reports are put together in path order, so a build reads the same each time.
    internal static class OverlayBuilder
    {
        private const string BuilderVersion = "7";

        // Files merged at the same time: one per core, leaving one for the window, at most 8 (each may hold a big pack
        // and its mods' copies in memory)
        private static int Workers { get { return Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1)); } }

        // One file of the output and the mods' versions of it, in load order
        private sealed class Target
        {
            public string GameRel;     // its path in the game's (or the DLC's) RomFS
            public bool Dlc;
            public long Size;          // its biggest version's, to start the big ones first
            public readonly List<KeyValuePair<InstalledMod, ModFile>> Versions = new List<KeyValuePair<InstalledMod, ModFile>>();
            public string Key { get { return (Dlc ? SavageWindInstall.OverlayDlcFolder : "") + GameRel; } }
            public string Label { get { return Dlc ? "DLC " + GameRel : GameRel; } }
        }

        // dlcVan: the DLC's RomFS (extracted/aoc1) when Savage Wind has the DLC set up, else null
        public static BuildResult Build(List<InstalledMod> enabled, SavageWindInstall sw, VanillaIndex van, VanillaIndex dlcVan,
            AppConfig config, bool force, Action<string> progress)
        {
            if (enabled.Count == 0)
            {
                ClearOverlay();
                config.LastBuildSignature = null;
                config.LastBuildSummary = null;
                config.Save();
                BuildResult none = new BuildResult { Report = new BuildReport() };
                none.Summary = L.T("no mods are on");
                return none;
            }

            progress(L.T("Checking your mods..."));
            string signature = Signature(enabled, sw, dlcVan != null);
            if (!force && signature == config.LastBuildSignature && File.Exists(AppPaths.OverlayMarker))
            {
                BuildResult cached = new BuildResult { Report = new BuildReport(), UpToDate = true };
                cached.OverlayDir = Directory.EnumerateFileSystemEntries(AppPaths.Overlay).Any(e => !AppPaths.IsOverlayMarker(e)) ? AppPaths.Overlay : null;
                cached.Summary = config.LastBuildSummary ?? L.T("mods ready");
                return cached;
            }

            config.LastBuildSignature = null;  // (a build that stops halfway is never taken as current)
            config.Save();
            ClearOverlay();
            BuildResult result = BuildInto(BuildTarget.Overlay(), enabled, sw, van, dlcVan, progress);
            File.WriteAllText(AppPaths.OverlayMarker, "Built by Guardian Terminal. This whole folder is rebuilt from mods\\ when you merge or press Play; don't put files here.\n");
            result.Report.Save(AppPaths.BuildReport);
            try { result.Report.SaveSummary(AppPaths.BuildSummary, config.CurrentProfile); }
            catch (Exception) { }
            result.OverlayDir = result.Report.OverlayFiles > 0 || result.Report.SizeTableWritten ? AppPaths.Overlay : null;
            config.LastBuildSignature = signature;
            config.LastBuildSummary = result.Summary;
            config.Save();
            return result;
        }

        // The merge itself, into an empty target.
        public static BuildResult BuildInto(BuildTarget output, List<InstalledMod> enabled, SavageWindInstall sw, VanillaIndex van,
            VanillaIndex dlcVan, Action<string> progress)
        {
            Stopwatch clock = Stopwatch.StartNew();
            BuildResult result = new BuildResult();
            BuildReport report = new BuildReport();
            result.Report = report;
            foreach (InstalledMod m in enabled) report.Mods.Add(m.DisplayName + (string.IsNullOrEmpty(m.Info.Version) ? "" : " (" + m.Info.Version + ")"));
            Directory.CreateDirectory(output.BaseDir);

            // every file of every enabled mod, by where it goes, in load order
            Dictionary<string, Target> targets = new Dictionary<string, Target>(StringComparer.Ordinal);
            RstbUpdater rstb = new RstbUpdater(Path.Combine(sw.RomfsDir, SavageWindInstall.RstbRelPath.Replace('/', '\\')), report);
            foreach (InstalledMod m in enabled)
            {
                progress(L.F("Reading {0}...", m.DisplayName));
                Collect(m, ModFiles.Scan(m.RomfsDir, van), false, targets, rstb, report);
                if (File.Exists(m.BcmlSizesFile)) rstb.TakeBcmlSizes(m.DisplayName, m.BcmlSizesFile);
                if (!Directory.Exists(m.DlcRomfsDir)) continue;
                if (dlcVan != null) Collect(m, ModFiles.Scan(m.DlcRomfsDir, dlcVan, true), true, targets, rstb, report);
                else
                {
                    int n = Directory.GetFiles(m.DlcRomfsDir, "*", SearchOption.AllDirectories).Length;
                    if (n > 0) report.LeftOut.Add(string.Format("{0}: its {1} DLC files (the DLC isn't set up in Savage Wind, or it's switched off)", m.DisplayName, n));
                }
            }

            HashSet<string> locked = VersionLocked.For(sw.GameVersion);
            List<Target> work = targets.Values.OrderByDescending(t => t.Size).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
            BuildReport[] parts = new BuildReport[work.Count];
            int done = 0;
            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = Workers };
            MemoryGate gate = new MemoryGate(MemoryGate.Budget());
            Parallel.ForEach(Partitioner.Create(Enumerable.Range(0, work.Count), EnumerablePartitionerOptions.NoBuffering), options, i =>
            {
                BuildReport part = new BuildReport();
                // what merging it may hold in memory: every version and the game's copy, decompressed (about 3x);
                // plain files that are only linked need next to nothing
                long cost = work[i].Versions.Count > 1 || work[i].Versions.Count == 1 && work[i].Size > 0 && IsMergeable(work[i])
                    ? work[i].Versions.Sum(v => SafeLength(v.Value.FullPath)) * 3 + work[i].Size * 3 : 0;
                gate.Enter(cost);
                try { Process(work[i], output, sw, van, dlcVan, locked, rstb, part); }
                catch (Exception e)
                {
                    // whatever went wrong with this one file: its last mod's copy as it is, so the build goes on
                    part = new BuildReport();
                    part.Warnings.Add(work[i].Label + ": couldn't be merged (" + e.Message + "), so the last mod's file is used as it is.");
                    try { UseLastAsIs(work[i], output, dlcVan, van, rstb, part); }
                    catch (Exception e2) { part.Warnings.Add(work[i].Label + ": left out (" + e2.Message + ")."); }
                }
                finally { gate.Leave(cost); }
                parts[i] = part;
                int n = Interlocked.Increment(ref done);
                if (n % 20 == 0 || n == work.Count) progress(L.F("Merging mod files... {0}/{1} ({2}%)", n, work.Count, n * 100 / Math.Max(1, work.Count)));
            });
            foreach (int i in Enumerable.Range(0, work.Count).OrderBy(i => work[i].Key, StringComparer.Ordinal)) report.Absorb(parts[i]);

            progress(L.T("Updating the resource size table..."));
            if (rstb.Changed)
            {
                if (output.ForSavageWind)
                {
                    // the overlay's table replaces the one Savage Wind would adjust itself, so its adjustments go in ours
                    rstb.AddVoiceRedirect(van, sw.VoiceRedirectFolder());
                    if (!targets.ContainsKey("Pack/Bootup.pack") && sw.HasHudLettersPatch) rstb.AddHudLettersRoom(van);
                    if (targets.ContainsKey("Pack/AocMainField.pack")) rstb.GrowToDlcEntries(van);
                }
                Write(Path.Combine(output.BaseDir, SavageWindInstall.RstbRelPath.Replace('/', '\\')), rstb.ToFile());
                report.SizeTableWritten = true;
            }

            report.Elapsed = clock.Elapsed;
            result.Summary = report.Summary();
            return result;
        }

        // One file of the output: decided, written (or linked) and sized in the table. Reports into its own report.
        private static void Process(Target t, BuildTarget output, SavageWindInstall sw, VanillaIndex van, VanillaIndex dlcVan,
            HashSet<string> locked, RstbUpdater rstb, BuildReport report)
        {
            VanillaIndex index = t.Dlc ? dlcVan : van;
            string label = t.Label;
            string rstbPrefix = t.Dlc ? SavageWindInstall.RstbDlcPrefix : "";
            string target = Path.Combine(t.Dlc ? output.DlcDir : output.BaseDir, t.GameRel.Replace('/', '\\'));
            string gamePath = index.Has(t.GameRel) ? index.FullPath(t.GameRel) : null;
            byte[] gameRaw = null;  // read when it's needed
            Func<byte[]> game = () => gameRaw ?? (gameRaw = File.ReadAllBytes(gamePath));
            KeyValuePair<InstalledMod, ModFile> last = t.Versions[t.Versions.Count - 1];
            string linkFrom = null;  // the result: a mod's file as it is (hard-linked) ...
            byte[] bytes = null;     // ... or new bytes (a merged pack, a merged file)
            bool decided = false;
            // an empty file is a mod switching the game's off (Relics' DLC Pack/AocMainField.pack): as the Switch's
            // LayeredFS does, it's served as it is, empty, not merged
            bool emptied = new FileInfo(last.Value.FullPath).Length == 0;
            if (emptied && gamePath != null)
                report.Notes.Add(string.Format("{0}: emptied by {1} on purpose (the game gets an empty file there, as on a Switch)", label, last.Key.DisplayName));

            // the actor list: merged actor by actor (a mod's whole 1.6.0 list would drop 1.9.0's actors, or another mod's)
            if (!emptied && gamePath != null && !t.Dlc && t.GameRel == ActorInfoMerger.Path)
            {
                try
                {
                    ActorInfoMerger am = new ActorInfoMerger(Yaz0.DecompressIfNeeded(game()));
                    foreach (KeyValuePair<InstalledMod, ModFile> v in t.Versions) am.Add(Yaz0.DecompressIfNeeded(File.ReadAllBytes(v.Value.FullPath)));
                    byte[] list = am.Write();
                    bytes = Yaz0.IsYaz0(game()) ? Yaz0Compressor.Compress(list, Yaz0Tail(game())) : list;
                    decided = true;
                    report.Notes.Add(string.Format("{0}: the actor lists of {1} merged into your game's own actor by actor ({2} actors changed, {3} added)",
                        label, string.Join(", ", t.Versions.Select(v => v.Key.DisplayName)), am.Changed, am.Added));
                }
                catch (Exception e)
                {
                    report.Warnings.Add(label + ": the mods' actor lists couldn't be merged actor by actor (" + e.Message + "), so the last mod's is used as it is.");
                }
            }

            // the game's archives: merged file by file
            if (!decided && !emptied && gamePath != null && FileCompare.IsArchive(gamePath))
            {
                PackMerger merger = new PackMerger(report, locked);
                List<ModVersion> mv = t.Versions.Select(v => new ModVersion { ModName = v.Key.DisplayName, Raw = File.ReadAllBytes(v.Value.FullPath) }).ToList();
                MergeResult r = merger.Merge(label, game(), mv);
                switch (r.Kind)
                {
                    case MergeKind.SameAsGame:
                        report.SkippedSameAsGame++;
                        return;
                    case MergeKind.UseMod:
                        linkFrom = t.Versions[r.ModIndex].Value.FullPath;
                        decided = true;
                        break;
                    case MergeKind.NewBytes:
                        bytes = r.Bytes;
                        report.MergedPacks++;
                        decided = true;
                        break;
                    default:
                        report.Warnings.Add(label + ": a mod's copy isn't a Switch archive, so it replaces the game's whole.");
                        break;
                }
            }

            // a file several mods changed (the map units, where the mods place their enemies; texts): merged entry by
            // entry, so each mod's changes stay
            if (!decided && !emptied && gamePath != null && t.Versions.Count > 1)
            {
                List<KeyValuePair<InstalledMod, ModFile>> changing = t.Versions.Where(v => !FileCompare.Same(v.Value.FullPath, gamePath)).ToList();
                if (changing.Count > 1 && changing.Skip(1).Any(v => !FileCompare.Same(v.Value.FullPath, changing[0].Value.FullPath)))
                {
                    bool texts = t.GameRel.EndsWith(".msbt", StringComparison.OrdinalIgnoreCase);
                    if (texts || FileCompare.IsByml(gamePath))
                    {
                        List<ModVersion> mv = changing.Select(v => new ModVersion { ModName = v.Key.DisplayName, Raw = File.ReadAllBytes(v.Value.FullPath) }).ToList();
                        bytes = texts ? report.MergeTexts(label, game(), mv) : report.MergeEntries(label, game(), mv);
                        decided = bytes != null;
                    }
                }
            }

            if (!decided)
            {
                UseLastAsIs(t, output, dlcVan, van, rstb, report);
                return;
            }

            // a modded Bootup.pack gets the HUD button icons Savage Wind gives the game's own (HudLetters)
            if (output.ForSavageWind && !t.Dlc && t.GameRel == "Pack/Bootup.pack" && sw.HasHudLettersPatch)
            {
                long growth;
                string why;
                byte[] patched = HudLetters.Patch(bytes ?? File.ReadAllBytes(linkFrom), out growth, out why);
                if (patched != null)
                {
                    bytes = patched;
                    linkFrom = null;
                    report.SizeChanges.Add(string.Format("Pack/Bootup.pack: HUD button icons put in (Layout/Common.sblarc +{0} bytes)", growth));
                }
                else report.Warnings.Add("Pack/Bootup.pack: the HUD button icons couldn't be put in (" + why + "), so the HUD shows the Switch's letters.");
            }

            if (linkFrom != null)
            {
                Link(linkFrom, target);
                report.LinkedFiles++;
                rstb.CompareFiles(t.GameRel, linkFrom, gamePath, rstbPrefix);
            }
            else
            {
                Write(target, bytes);
                report.WrittenFiles++;
                rstb.Compare(t.GameRel, bytes, gamePath == null ? null : game(), rstbPrefix);
            }
            if (t.Dlc) report.DlcFiles++;
        }

        // Files that get merged in memory (archives, BYML, texts) rather than linked as they are
        private static bool IsMergeable(Target t)
        {
            string ext = Path.GetExtension(t.GameRel).ToLowerInvariant();
            return ext.EndsWith("pack") || ext.EndsWith("sarc") || ext.EndsWith("byml") || ext.EndsWith("mubin") || ext == ".msbt" || ext.EndsWith("blarc");
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (IOException) { return 0; }
        }

        // Anything else: the last mod's file, as it is (a conflict when other mods changed it too)
        private static void UseLastAsIs(Target t, BuildTarget output, VanillaIndex dlcVan, VanillaIndex van, RstbUpdater rstb, BuildReport report)
        {
            VanillaIndex index = t.Dlc ? dlcVan : van;
            string gamePath = index.Has(t.GameRel) ? index.FullPath(t.GameRel) : null;
            KeyValuePair<InstalledMod, ModFile> winner = t.Versions[t.Versions.Count - 1];
            string winnerPath = winner.Value.FullPath;
            if (t.Versions.Count > 1)
            {
                List<string> differing = t.Versions.Take(t.Versions.Count - 1)
                    .Where(v => !FileCompare.Same(v.Value.FullPath, winnerPath) && (gamePath == null || !FileCompare.Same(v.Value.FullPath, gamePath)))
                    .Select(v => v.Key.DisplayName).ToList();
                if (differing.Count > 0) report.AddConflict(t.Label, differing, winner.Key.DisplayName);
            }
            if (gamePath != null && FileCompare.Same(winnerPath, gamePath))
            {
                report.SkippedSameAsGame++;
                return;
            }
            string target = Path.Combine(t.Dlc ? output.DlcDir : output.BaseDir, t.GameRel.Replace('/', '\\'));
            Link(winnerPath, target);
            report.LinkedFiles++;
            rstb.CompareFiles(t.GameRel, winnerPath, gamePath, t.Dlc ? SavageWindInstall.RstbDlcPrefix : "");
            if (t.Dlc) report.DlcFiles++;
        }

        private static void Collect(InstalledMod m, List<ModFile> files, bool dlc, Dictionary<string, Target> targets, RstbUpdater rstb, BuildReport report)
        {
            foreach (ModFile f in files)
            {
                switch (f.Kind)
                {
                    case ModFileKind.SizeTable:
                        rstb.TakeModTable(m.DisplayName, f.FullPath);
                        continue;
                    case ModFileKind.Skipped:
                    case ModFileKind.Ignored:
                        continue;
                    case ModFileKind.NoFolder:
                        report.LeftOut.Add(m.DisplayName + ": " + (dlc ? "DLC " : "") + f.Rel + " (Savage Wind can't add files to a folder the game doesn't have)");
                        continue;
                }
                Target t = new Target { GameRel = f.Rel, Dlc = dlc };
                Target have;
                if (targets.TryGetValue(t.Key, out have)) t = have;
                else targets[t.Key] = t;
                t.Versions.Add(new KeyValuePair<InstalledMod, ModFile>(m, f));
                long size = new FileInfo(f.FullPath).Length;
                if (size > t.Size) t.Size = size;
            }
        }

        // Empties the overlay ("Empty merge output"): the next Play or merge builds it again.
        public static void ClearOverlay()
        {
            if (!Directory.Exists(AppPaths.Overlay)) return;
            // (deleting a hard link removes only the link: the mod's own file stays)
            AppPaths.DeleteFolderInside(AppPaths.Overlay, AppPaths.Root);
        }

        // Yaz0 header bytes 8-15 (the alignment the game's file asks for), to compress new bytes the same way
        private static byte[] Yaz0Tail(byte[] yaz0)
        {
            byte[] t = new byte[8];
            Buffer.BlockCopy(yaz0, 8, t, 0, 8);
            return t;
        }

        // Hard link (same drive: instant, no space), else a copy.
        private static void Link(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target)) File.Delete(target);
            if (!NativeMethods.CreateHardLink(target, source, IntPtr.Zero)) File.Copy(source, target);
        }

        private static void Write(string target, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target)) File.Delete(target);
            File.WriteAllBytes(target, data);
        }

        // What a build depends on: the mods (files, sizes, times, order), the game's table, the DLC, the voice setting.
        private static string Signature(List<InstalledMod> enabled, SavageWindInstall sw, bool dlc)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(BuilderVersion).Append('|').Append(sw.RomfsDir).Append('|');
            FileInfo rstb = new FileInfo(Path.Combine(sw.RomfsDir, SavageWindInstall.RstbRelPath.Replace('/', '\\')));
            sb.Append(rstb.Length).Append(',').Append(rstb.LastWriteTimeUtc.Ticks).Append('|').Append(sw.VoiceRedirectFolder());
            sb.Append("|dlc:").Append(dlc ? sw.DlcVersion ?? "?" : "none").Append('\n');
            foreach (InstalledMod m in enabled)
            {
                sb.Append(m.Folder).Append('\n');
                foreach (string dir in new[] { m.RomfsDir, m.DlcRomfsDir })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                    {
                        FileInfo fi = new FileInfo(f);
                        sb.Append(f.Substring(m.Dir.Length)).Append(',').Append(fi.Length).Append(',').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
                    }
                }
                if (File.Exists(m.BcmlSizesFile)) sb.Append("bcml:").Append(new FileInfo(m.BcmlSizesFile).Length).Append('\n');
            }
            using (SHA1 sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }
    }
}
