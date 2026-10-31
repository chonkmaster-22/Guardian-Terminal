using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    // Merges mods' game data flags (Bootup.pack's GameData/gamedata.ssarc) into the game's own, flag by flag, as BCML
    // does: mods made for 1.6.0 carry whole 1.6.0 flag files (missing thousands of 1.9.0's flags, some in another
    // layout), and taking their files as they are crashes 1.9.0 or drops another mod's flags. So every game flag stays,
    // with each mod's changes to it applied in load order (its name and hash stay the game's, its limits only widen),
    // and flags a mod adds go into files of their own. The save format (savedataformat.ssarc) is then made again from
    // the merged flags, so the new flags that are saved are saved.
    internal sealed class GameDataMerger
    {
        public const string GameDataName = "GameData/gamedata.ssarc", SaveFormatName = "GameData/savedataformat.ssarc";
        private const int FlagsPerFile = 4096, SavesPerFile = 8192;
        private static readonly HashSet<string> Identity = new HashSet<string>(StringComparer.Ordinal) { "DataName", "HashValue" };

        private sealed class FlagFile
        {
            public string Name;
            public string Type;  // its root key: bool_data, s32_data, ...
            public List<object> Flags;
            public int Version, Alignment;
        }

        private readonly List<FlagFile> files = new List<FlagFile>();
        // (type, name) -> the game's flags of that name (a few names are in the game twice), and as they were
        private readonly Dictionary<string, List<Dictionary<string, object>>> byName = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, object>> original = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, object>> added = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        private readonly List<string> addedOrder = new List<string>();
        public int Changed { get; private set; }
        public int Added { get { return added.Count; } }

        public GameDataMerger(byte[] gameSarc)
        {
            foreach (SarcEntry e in Sarc.Parse(gameSarc))
            {
                byte[] d = Sarc.Extract(gameSarc, e);
                Dictionary<string, object> root = Byml.Parse(d) as Dictionary<string, object>;
                if (root == null || root.Count != 1 || !(root.Values.First() is List<object>))
                    throw new InvalidOperationException("Unexpected game data file " + e.Name);
                FlagFile f = new FlagFile { Name = e.Name, Type = root.Keys.First(), Flags = (List<object>)root.Values.First(), Version = Byml.Version(d), Alignment = SarcWriter.AlignmentOf(e.Offset) };
                files.Add(f);
                foreach (Dictionary<string, object> flag in f.Flags.OfType<Dictionary<string, object>>())
                {
                    string id = Id(f.Type, flag);
                    if (id == null) continue;
                    List<Dictionary<string, object>> list;
                    if (!byName.TryGetValue(id, out list)) { byName[id] = list = new List<Dictionary<string, object>>(); original[id] = new Dictionary<string, object>(flag, StringComparer.Ordinal); }
                    list.Add(flag);
                }
            }
        }

        private static string Id(string type, Dictionary<string, object> flag)
        {
            object name;
            return flag.TryGetValue("DataName", out name) && name is string ? type + "/" + name : null;
        }

        // One mod's gamedata (unpacked SARC), on top of what's merged so far
        public void Add(byte[] modSarc)
        {
            foreach (SarcEntry e in Sarc.Parse(modSarc))
            {
                Dictionary<string, object> root;
                try { root = Byml.Parse(Sarc.Extract(modSarc, e)) as Dictionary<string, object>; }
                catch (Exception) { continue; }
                if (root == null) continue;
                foreach (KeyValuePair<string, object> kv in root)
                {
                    List<object> list = kv.Value as List<object>;
                    if (list == null) continue;
                    foreach (Dictionary<string, object> flag in list.OfType<Dictionary<string, object>>())
                    {
                        string id = Id(kv.Key, flag);
                        if (id == null) continue;
                        List<Dictionary<string, object>> mine;
                        if (byName.TryGetValue(id, out mine)) Apply(original[id], mine, flag);
                        else
                        {
                            if (!added.ContainsKey(id)) addedOrder.Add(id);
                            added[id] = flag;
                        }
                    }
                }
            }
        }

        // What the mod changed from the game's flag goes into the game's (all its copies); name and hash stay
        private void Apply(Dictionary<string, object> before, List<Dictionary<string, object>> targets, Dictionary<string, object> mod)
        {
            bool any = false;
            foreach (KeyValuePair<string, object> kv in mod)
            {
                if (Identity.Contains(kv.Key)) continue;
                object was;
                before.TryGetValue(kv.Key, out was);
                if (Same(was, kv.Value)) continue;
                foreach (Dictionary<string, object> t in targets)
                {
                    object now;
                    t.TryGetValue(kv.Key, out now);
                    if (kv.Key == "MinValue") t[kv.Key] = Wider(now, kv.Value, true);
                    else if (kv.Key == "MaxValue") t[kv.Key] = Wider(now, kv.Value, false);
                    else t[kv.Key] = kv.Value;
                }
                any = true;
            }
            if (any) Changed++;
        }

        // The lower (min) or higher (max) of two limits of the same kind; anything else keeps the game's
        private static object Wider(object game, object mod, bool min)
        {
            if (game is int && mod is int) return min ? Math.Min((int)game, (int)mod) : Math.Max((int)game, (int)mod);
            if (game is float && mod is float) return min ? Math.Min((float)game, (float)mod) : Math.Max((float)game, (float)mod);
            return game ?? mod;
        }

        private static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == b;
            List<object> la = a as List<object>, lb = b as List<object>;
            if (la != null && lb != null) return la.Count == lb.Count && la.Zip(lb, Same).All(x => x);
            Dictionary<string, object> da = a as Dictionary<string, object>, db = b as Dictionary<string, object>;
            if (da != null && db != null) return da.Count == db.Count && da.All(kv => db.ContainsKey(kv.Key) && Same(kv.Value, db[kv.Key]));
            return a.GetType() == b.GetType() && a.Equals(b);
        }

        // The merged gamedata (unpacked SARC): the game's files, changed, and files for the added flags
        public byte[] Write()
        {
            List<SarcFile> output = files.Select(f => new SarcFile { Name = f.Name, Alignment = f.Alignment, Data = Byml.Write(Root(f.Type, f.Flags), f.Version) }).ToList();
            foreach (IGrouping<string, string> type in addedOrder.GroupBy(id => id.Substring(0, id.IndexOf('/'))))
            {
                // next to the game's own of that type (not its "revival" ones): bool_data_3.bgdata after bool_data_0..2
                FlagFile like = files.Where(f => f.Type == type.Key && !f.Name.StartsWith("/revival_", StringComparison.Ordinal)).LastOrDefault()
                    ?? files.LastOrDefault(f => f.Type == type.Key);
                string prefix = like != null ? Regex.Replace(like.Name, @"_\d+\.bgdata$", "") : "/" + type.Key;
                int next = files.Concat(new FlagFile[0]).Where(f => f.Name.StartsWith(prefix + "_", StringComparison.Ordinal))
                    .Select(f => Index(f.Name)).DefaultIfEmpty(-1).Max() + 1;
                List<object> flags = type.Select(id => (object)added[id]).ToList();
                for (int i = 0; i < flags.Count; i += FlagsPerFile, next++)
                    output.Add(new SarcFile
                    {
                        Name = prefix + "_" + next + ".bgdata",
                        Alignment = like != null ? like.Alignment : 4,
                        Data = Byml.Write(Root(type.Key, flags.Skip(i).Take(FlagsPerFile).ToList()), like != null ? like.Version : 2)
                    });
            }
            return SarcWriter.Write(output);
        }

        private static Dictionary<string, object> Root(string type, List<object> flags)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal) { { type, flags } };
        }

        private static int Index(string name)
        {
            Match m = Regex.Match(name, @"_(\d+)\.\w+$");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        // The save format made again for the merged flags: game_data.sav lists every flag that is saved (IsSave), by
        // hash, 8192 to a file; the game's other save files (caption.sav, option.sav) after them as they are.
        public byte[] WriteSaveFormat(byte[] gameSaveSarc)
        {
            List<SarcEntry> entries = Sarc.Parse(gameSaveSarc).OrderBy(e => Index(e.Name)).ToList();
            List<Dictionary<string, object>> roots = entries.Select(e => (Dictionary<string, object>)Byml.Parse(Sarc.Extract(gameSaveSarc, e))).ToList();
            int version = Byml.Version(Sarc.Extract(gameSaveSarc, entries[0]));
            Func<Dictionary<string, object>, string> fileOf = r => (string)((Dictionary<string, object>)((List<object>)r["file_list"])[0])["file_name"];
            List<Dictionary<string, object>> gameData = roots.Where(r => fileOf(r) == "game_data.sav").ToList(), others = roots.Where(r => fileOf(r) != "game_data.sav").ToList();
            if (gameData.Count == 0) throw new InvalidOperationException("The game's save format has no game_data.sav part.");

            // the flags of the other save files aren't in game_data.sav
            HashSet<int> elsewhere = new HashSet<int>(others.SelectMany(r => Saves(r)).Select(s => (int)s["HashValue"]));
            SortedDictionary<int, Dictionary<string, object>> saved = new SortedDictionary<int, Dictionary<string, object>>();
            foreach (FlagFile f in files)
                foreach (Dictionary<string, object> flag in f.Flags.OfType<Dictionary<string, object>>()) AddSaved(saved, flag, elsewhere);
            foreach (string id in addedOrder) AddSaved(saved, added[id], elsewhere);

            List<object> all = saved.Values.Cast<object>().ToList();
            int parts = Math.Max(1, (all.Count + SavesPerFile - 1) / SavesPerFile), count = parts + others.Count;
            List<SarcFile> output = new List<SarcFile>();
            Dictionary<string, object> header = (Dictionary<string, object>)((List<object>)gameData[0]["file_list"])[0];
            for (int i = 0; i < parts; i++)
            {
                Dictionary<string, object> root = new Dictionary<string, object>(gameData[0], StringComparer.Ordinal);
                root["file_list"] = new List<object> { header, all.Skip(i * SavesPerFile).Take(SavesPerFile).ToList() };
                root["save_info"] = SaveInfo(gameData[0], count);
                output.Add(new SarcFile { Name = "/saveformat_" + i + ".bgsvdata", Alignment = SarcWriter.AlignmentOf(entries[0].Offset), Data = Byml.Write(root, version) });
            }
            for (int i = 0; i < others.Count; i++)
            {
                Dictionary<string, object> root = new Dictionary<string, object>(others[i], StringComparer.Ordinal);
                root["save_info"] = SaveInfo(others[i], count);
                output.Add(new SarcFile { Name = "/saveformat_" + (parts + i) + ".bgsvdata", Alignment = SarcWriter.AlignmentOf(entries[0].Offset), Data = Byml.Write(root, version) });
            }
            return SarcWriter.Write(output);
        }

        private static IEnumerable<Dictionary<string, object>> Saves(Dictionary<string, object> root)
        {
            return ((List<object>)((List<object>)root["file_list"])[1]).OfType<Dictionary<string, object>>();
        }

        private static void AddSaved(SortedDictionary<int, Dictionary<string, object>> saved, Dictionary<string, object> flag, HashSet<int> elsewhere)
        {
            object isSave, hash, name;
            if (!flag.TryGetValue("IsSave", out isSave) || !(isSave is bool) || !(bool)isSave) return;
            if (!flag.TryGetValue("HashValue", out hash) || !(hash is int) || !flag.TryGetValue("DataName", out name)) return;
            if (elsewhere.Contains((int)hash) || saved.ContainsKey((int)hash)) return;
            saved[(int)hash] = new Dictionary<string, object>(StringComparer.Ordinal) { { "DataName", name }, { "HashValue", hash } };
        }

        private static List<object> SaveInfo(Dictionary<string, object> like, int count)
        {
            Dictionary<string, object> info = new Dictionary<string, object>((Dictionary<string, object>)((List<object>)like["save_info"])[0], StringComparer.Ordinal);
            info["directory_num"] = count;
            return new List<object> { info };
        }
    }
}
