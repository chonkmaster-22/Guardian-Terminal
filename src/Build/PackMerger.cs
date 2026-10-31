using System;
using System.Collections.Generic;
using System.Linq;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    internal sealed class ModVersion
    {
        public string ModName;
        public byte[] Raw;  // the file as the mod has it (maybe Yaz0)
    }

    internal enum MergeKind { SameAsGame, UseMod, NewBytes, NotAnArchive }

    internal sealed class MergeResult
    {
        public MergeKind Kind;
        public int ModIndex;  // UseMod: which of the versions passed in
        public byte[] Bytes;  // NewBytes
    }

    // Merges mods' versions of one of the game's SARC archives (a .pack, an actor pack, ...) into the game's own:
    // inside the archive, each file comes from the game unless a mod changed it; when several mods changed the same
    // inner file, nested archives are merged the same way and anything else goes to the later mod (a conflict).
    // Files a mod's copy lacks stay (so a pack from an older game version doesn't drop files 1.9.0 added), and so do
    // the game's own of the version-locked files (VersionLocked: mods' 1.6.0 copies of them crash 1.9.0).
    internal sealed class PackMerger
    {
        private readonly BuildReport report;
        private readonly HashSet<string> locked;

        public PackMerger(BuildReport report, HashSet<string> locked)
        {
            this.report = report;
            this.locked = locked;
        }

        private sealed class Archive
        {
            public byte[] Data;  // decompressed SARC bytes
            public Dictionary<string, SarcEntry> Entries;
            public List<string> Order;

            public static Archive Open(byte[] raw)
            {
                byte[] d;
                try { d = Yaz0.DecompressIfNeeded(raw); }
                catch (Exception) { return null; }
                if (!Sarc.IsSarc(d) || Sarc.IsBigEndian(d)) return null;
                List<SarcEntry> list;
                try { list = Sarc.Parse(d); }
                catch (Exception) { return null; }
                Archive a = new Archive();
                a.Data = d;
                a.Entries = new Dictionary<string, SarcEntry>(StringComparer.Ordinal);
                a.Order = new List<string>();
                foreach (SarcEntry e in list)
                {
                    if (a.Entries.ContainsKey(e.Name)) continue;
                    a.Entries[e.Name] = e;
                    a.Order.Add(e.Name);
                }
                return a;
            }
        }

        public static bool IsArchive(byte[] raw)
        {
            if (Sarc.IsSarc(raw)) return true;
            if (!Yaz0.IsYaz0(raw)) return false;
            byte[] head = Yaz0.DecompressPrefix(raw, 4);
            return head.Length == 4 && head[0] == 'S' && head[1] == 'A' && head[2] == 'R' && head[3] == 'C';
        }

        public MergeResult Merge(string path, byte[] gameRaw, List<ModVersion> mods)
        {
            Archive game = Archive.Open(gameRaw);
            if (game == null) return new MergeResult { Kind = MergeKind.NotAnArchive };
            List<Archive> versions = new List<Archive>();
            foreach (ModVersion m in mods)
            {
                Archive a = Archive.Open(m.Raw);
                if (a == null) return new MergeResult { Kind = MergeKind.NotAnArchive };
                versions.Add(a);
            }

            List<string> names = new List<string>(game.Order);
            HashSet<string> seen = new HashSet<string>(game.Order, StringComparer.Ordinal);
            foreach (Archive a in versions)
                foreach (string n in a.Order)
                    if (seen.Add(n)) names.Add(n);

            List<SarcFile> output = new List<SarcFile>(names.Count);
            HashSet<int> contributors = new HashSet<int>();
            bool rebuiltInside = false, keptLocked = false;
            int changedEntries = 0;
            List<int> flagMods = new List<int>();
            Dictionary<string, byte[]> flags = path == "Pack/Bootup.pack" ? MergeGameData(path, game, versions, mods, out flagMods) : null;
            foreach (string name in names)
            {
                SarcEntry ge;
                game.Entries.TryGetValue(name, out ge);
                byte[] merged;
                if (flags != null && ge != null && flags.TryGetValue(name, out merged))
                {
                    output.Add(new SarcFile { Name = name, Data = merged, Alignment = SarcWriter.AlignmentOf(ge.Offset) });
                    foreach (int i in flagMods) contributors.Add(i);
                    rebuiltInside = true;
                    changedEntries++;
                    continue;
                }
                List<int> changers = new List<int>();
                for (int i = 0; i < versions.Count; i++)
                {
                    SarcEntry me;
                    if (!versions[i].Entries.TryGetValue(name, out me)) continue;
                    if (ge == null || !SameContent(game.Data, ge, versions[i].Data, me)) changers.Add(i);
                }
                if (changers.Count > 0 && ge != null && locked.Contains(path + "/" + name))
                {
                    report.Notes.Add(string.Format("{0} > {1}: {2}'s copy left out, your game's own {3} kept (mods' copies made for game version 1.6.0 crash newer game files)",
                        path, name, string.Join(", ", changers.Select(i => mods[i].ModName)), VersionLocked.Describe(path + "/" + name)));
                    changers.Clear();
                    keptLocked = true;
                }

                SarcFile f = new SarcFile();
                f.Name = name;
                if (changers.Count == 0)
                {
                    f.Data = Sarc.Extract(game.Data, ge);
                    f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                }
                else if (changers.Count == 1)
                {
                    int who = changers[0];
                    SarcEntry me = versions[who].Entries[name];
                    f.Data = Sarc.Extract(versions[who].Data, me);
                    f.Alignment = SarcWriter.AlignmentOf(me.Offset);
                    // a nested archive: merged too, so files the game has in it and the mod's copy lacks stay; and a
                    // text file made for an older game version that lacks some of the game's messages gets them back
                    if (ge != null)
                    {
                        byte[] gameInner = Sarc.Extract(game.Data, ge);
                        byte[] texts = null;
                        if (name.EndsWith(".msbt", StringComparison.OrdinalIgnoreCase) && MsbtMerger.LacksGameMessages(gameInner, f.Data))
                            texts = report.MergeTexts(path + " > " + name, gameInner, new List<ModVersion> { new ModVersion { ModName = mods[who].ModName, Raw = f.Data } });
                        if (texts != null && ReferenceEquals(texts, gameInner))
                        {
                            // (it changes nothing: the game's own file)
                            f.Data = gameInner;
                            f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                            output.Add(f);
                            rebuiltInside = true;
                            continue;
                        }
                        if (texts != null)
                        {
                            f.Data = texts;
                            f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                            rebuiltInside = true;
                        }
                        else if (IsArchive(gameInner) && IsArchive(f.Data))
                        {
                            MergeResult inner = Merge(path + "/" + name, gameInner, new List<ModVersion> { new ModVersion { ModName = mods[who].ModName, Raw = f.Data } });
                            if (inner.Kind == MergeKind.NewBytes)
                            {
                                f.Data = inner.Bytes;
                                f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                                rebuiltInside = true;
                            }
                            else if (inner.Kind == MergeKind.SameAsGame)
                            {
                                f.Data = gameInner;
                                f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                                output.Add(f);
                                continue;
                            }
                        }
                    }
                    contributors.Add(who);
                    changedEntries++;
                }
                else
                {
                    // several mods changed it: merge it too if it is an archive, a BYML file (entry by entry) or a text
                    // file (message by message) the game has, else the later mod wins
                    MergeResult inner = null;
                    byte[] entries = null;
                    if (ge != null)
                    {
                        byte[] gameInner = Sarc.Extract(game.Data, ge);
                        List<ModVersion> innerVersions = changers.Select(i => new ModVersion { ModName = mods[i].ModName, Raw = Sarc.Extract(versions[i].Data, versions[i].Entries[name]) }).ToList();
                        if (IsArchive(gameInner)) inner = Merge(path + "/" + name, gameInner, innerVersions);
                        else if (name.EndsWith(".msbt", StringComparison.OrdinalIgnoreCase)) entries = report.MergeTexts(path + " > " + name, gameInner, innerVersions);
                        else entries = report.MergeEntries(path + " > " + name, gameInner, innerVersions);
                    }
                    if (entries != null)
                    {
                        f.Data = entries;
                        f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                        rebuiltInside = true;
                        foreach (int i in changers) contributors.Add(i);
                    }
                    else if (inner != null && inner.Kind == MergeKind.NewBytes)
                    {
                        f.Data = inner.Bytes;
                        f.Alignment = SarcWriter.AlignmentOf(ge.Offset);
                        rebuiltInside = true;
                        foreach (int i in changers) contributors.Add(i);
                    }
                    else if (inner != null && inner.Kind == MergeKind.UseMod)
                    {
                        int who = changers[inner.ModIndex];
                        SarcEntry me = versions[who].Entries[name];
                        f.Data = Sarc.Extract(versions[who].Data, me);
                        f.Alignment = SarcWriter.AlignmentOf(me.Offset);
                        contributors.Add(who);
                    }
                    else
                    {
                        int who = changers[changers.Count - 1];
                        SarcEntry me = versions[who].Entries[name];
                        f.Data = Sarc.Extract(versions[who].Data, me);
                        f.Alignment = SarcWriter.AlignmentOf(me.Offset);
                        contributors.Add(who);
                        report.AddConflict(path + " > " + name, changers.Select(i => mods[i].ModName), mods[who].ModName);
                    }
                    changedEntries++;
                }
                output.Add(f);
            }

            if (contributors.Count == 0) return new MergeResult { Kind = MergeKind.SameAsGame };
            // one mod changed it and its copy has everything the game's has: its own file can be used as it is
            if (contributors.Count == 1 && !rebuiltInside && !keptLocked)
            {
                int only = contributors.First();
                if (game.Order.All(n => versions[only].Entries.ContainsKey(n)))
                    return new MergeResult { Kind = MergeKind.UseMod, ModIndex = only };
            }

            byte[] sarc = SarcWriter.Write(output);
            byte[] raw = Yaz0.IsYaz0(gameRaw) ? Yaz0Compressor.Compress(sarc, Tail(gameRaw)) : sarc;
            int kept = game.Order.Count(n => !versions.Any(v => v.Entries.ContainsKey(n)));
            report.Merges.Add(string.Format("{0}: {1} of {2} files from {3}{4}", path, changedEntries, output.Count,
                string.Join(" + ", contributors.OrderBy(i => i).Select(i => mods[i].ModName)),
                kept > 0 ? string.Format("; kept {0} game file{1} the mod's copy didn't have", kept, kept == 1 ? "" : "s") : ""));
            return new MergeResult { Kind = MergeKind.NewBytes, Bytes = raw };
        }

        // Bootup.pack's game data flags, when mods change them: merged flag by flag (GameDataMerger), and the save
        // format made again to match. null: no mod changes them (or they couldn't be merged: then file by file).
        private Dictionary<string, byte[]> MergeGameData(string path, Archive game, List<Archive> versions, List<ModVersion> mods, out List<int> changers)
        {
            changers = new List<int>();
            SarcEntry gd, save;
            if (!game.Entries.TryGetValue(GameDataMerger.GameDataName, out gd) || !game.Entries.TryGetValue(GameDataMerger.SaveFormatName, out save)) return null;
            for (int i = 0; i < versions.Count; i++)
            {
                SarcEntry me;
                if (versions[i].Entries.TryGetValue(GameDataMerger.GameDataName, out me) && !SameContent(game.Data, gd, versions[i].Data, me)) changers.Add(i);
            }
            if (changers.Count == 0) return null;
            try
            {
                byte[] gdRaw = Sarc.Extract(game.Data, gd), saveRaw = Sarc.Extract(game.Data, save);
                GameDataMerger merger = new GameDataMerger(Yaz0.DecompressIfNeeded(gdRaw));
                foreach (int i in changers)
                    merger.Add(Yaz0.DecompressIfNeeded(Sarc.Extract(versions[i].Data, versions[i].Entries[GameDataMerger.GameDataName])));
                byte[] flags = merger.Write(), format = merger.WriteSaveFormat(Yaz0.DecompressIfNeeded(saveRaw));
                report.Notes.Add(string.Format("{0} > GameData: the game data flags of {1} merged into your game's own flag by flag ({2} flags changed, {3} added); the save format made again to match",
                    path, string.Join(", ", changers.Select(i => mods[i].ModName)), merger.Changed, merger.Added));
                return new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    { GameDataMerger.GameDataName, Yaz0.IsYaz0(gdRaw) ? Yaz0Compressor.Compress(flags, Tail(gdRaw)) : flags },
                    { GameDataMerger.SaveFormatName, Yaz0.IsYaz0(saveRaw) ? Yaz0Compressor.Compress(format, Tail(saveRaw)) : format }
                };
            }
            catch (Exception e)
            {
                report.Warnings.Add(path + " > GameData: the mods' game data flags couldn't be merged flag by flag (" + e.Message + "), so their files are used as they are.");
                changers.Clear();
                return null;
            }
        }

        // Same bytes, or (both Yaz0) the same once decompressed.
        private static bool SameContent(byte[] a, SarcEntry ae, byte[] b, SarcEntry be)
        {
            if (ae.Length == be.Length && NativeMethods.BytesEqual(a, ae.Offset, b, be.Offset, ae.Length)) return true;
            if (ae.Length < 16 || be.Length < 16) return false;
            byte[] x = Sarc.Extract(a, ae), y = Sarc.Extract(b, be);
            if (!Yaz0.IsYaz0(x) || !Yaz0.IsYaz0(y) || Yaz0.ReadDecompressedSize(x) != Yaz0.ReadDecompressedSize(y)) return false;
            try { return NativeMethods.BytesEqual(Yaz0.Decompress(x), Yaz0.Decompress(y)); }
            catch (Exception) { return false; }
        }

        private static byte[] Tail(byte[] yaz0)
        {
            byte[] t = new byte[8];
            Buffer.BlockCopy(yaz0, 8, t, 0, 8);
            return t;
        }
    }
}
