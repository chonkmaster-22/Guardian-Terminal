using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal;
using GuardianTerminal.Build;
using GuardianTerminal.Formats;

// Looks inside a gamedata.ssarc / savedataformat.ssarc (unpacked SARC): each file's root keys, flag counts, how the
// flags are ordered, and whether the BYML writer gives back the same bytes.
//   GameDataProbe <sarc> [<other sarc> to compare flags by name]
internal static class GameDataProbe
{
    private static int Main(string[] args)
    {
        if (args[0] == "merge")  // GameDataProbe merge <game gd> <game save> <out gd> <out save> [<mod gd>...]
        {
            GameDataMerger m = new GameDataMerger(File.ReadAllBytes(args[1]));
            foreach (string mod in args.Skip(5)) m.Add(File.ReadAllBytes(mod));
            File.WriteAllBytes(args[3], m.Write());
            File.WriteAllBytes(args[4], m.WriteSaveFormat(File.ReadAllBytes(args[2])));
            Console.WriteLine("merged: {0} flags changed, {1} added", m.Changed, m.Added);
            return 0;
        }
        if (args[0] == "same")  // GameDataProbe same <sarc a> <sarc b>: file by file, the same trees?
        {
            byte[] a = File.ReadAllBytes(args[1]), b = File.ReadAllBytes(args[2]);
            Dictionary<string, SarcEntry> eb = Sarc.Parse(b).ToDictionary(e => e.Name);
            foreach (SarcEntry e in Sarc.Parse(a).OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                SarcEntry o;
                if (!eb.TryGetValue(e.Name, out o)) { Console.WriteLine(e.Name + ": only in the first"); continue; }
                byte[] x = Sarc.Extract(a, e), y = Sarc.Extract(b, o);
                string dx = Describe(Byml.Parse(x)), dy = Describe(Byml.Parse(y));
                Console.WriteLine("{0}: {1}", e.Name, NativeMethods.BytesEqual(x, y) ? "same bytes" : dx == dy ? "same tree, other bytes" : "DIFFERENT");
                if (dx != dy)
                {
                    int at = 0;
                    while (at < Math.Min(dx.Length, dy.Length) && dx[at] == dy[at]) at++;
                    int from = Math.Max(0, at - 150);
                    Console.WriteLine("   first: ..." + dx.Substring(from, Math.Min(300, dx.Length - from)));
                    Console.WriteLine("   second: ..." + dy.Substring(from, Math.Min(300, dy.Length - from)));
                }
                else if (!NativeMethods.BytesEqual(x, y))
                {
                    int at = 0;
                    while (at < Math.Min(x.Length, y.Length) && x[at] == y[at]) at++;
                    Console.WriteLine("   bytes {0} / {1}, first difference at {2} (key table at {3}, strings at {4}, root at {5})", x.Length, y.Length, at,
                        BitConverter.ToUInt32(x, 4), BitConverter.ToUInt32(x, 8), BitConverter.ToUInt32(x, 12));
                }
            }
            foreach (string n in eb.Keys.Where(n => !Sarc.Parse(a).Any(e => e.Name == n))) Console.WriteLine(n + ": only in the second");
            return 0;
        }
        if (args[0] == "actors")  // GameDataProbe actors <game ActorInfo> <mod ActorInfo>...: shape, order, differences
        {
            Dictionary<string, Dictionary<string, object>> game = null;
            foreach (string path in args.Skip(1))
            {
                byte[] d = Yaz0.DecompressIfNeeded(File.ReadAllBytes(path));
                Dictionary<string, object> root = (Dictionary<string, object>)Byml.Parse(d);
                List<object> actors = (List<object>)root["Actors"], hashes = (List<object>)root["Hashes"];
                Console.WriteLine("{0}: v{1}, keys [{2}], {3} actors, {4} hashes ({5}); first actor keys: {6}", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path)))),
                    Byml.Version(d), string.Join(", ", root.Keys), actors.Count, hashes.Count, string.Join("/", hashes.Take(3).Select(h => h.GetType().Name + " " + h)),
                    string.Join(",", ((Dictionary<string, object>)actors[0]).Keys.Take(12)));
                bool sorted = true;
                for (int i = 1; i < hashes.Count; i++) if (Convert.ToUInt32(hashes[i - 1]) >= Convert.ToUInt32(hashes[i])) sorted = false;
                Console.WriteLine("  hashes sorted as unsigned: {0}; hash = crc32(name) for actor 0: {1}", sorted,
                    Convert.ToUInt32(hashes[0]) == Crc32.Compute((string)((Dictionary<string, object>)actors[0])["name"]));
                Dictionary<string, Dictionary<string, object>> byName = actors.Cast<Dictionary<string, object>>().ToDictionary(a => (string)a["name"]);
                if (game == null) { game = byName; continue; }
                int changed = byName.Count(kv => game.ContainsKey(kv.Key) && Describe(kv.Value) != Describe(game[kv.Key]));
                Console.WriteLine("  vs game: {0} new actors, {1} game actors missing, {2} changed", byName.Keys.Count(k => !game.ContainsKey(k)), game.Keys.Count(k => !byName.ContainsKey(k)), changed);
                Console.WriteLine("  missing e.g.: " + string.Join(", ", game.Keys.Where(k => !byName.ContainsKey(k)).Take(6)));
                Dictionary<string, int> fields = new Dictionary<string, int>();
                Dictionary<string, string> example = new Dictionary<string, string>();
                foreach (KeyValuePair<string, Dictionary<string, object>> kv in byName.Where(kv => game.ContainsKey(kv.Key)))
                    foreach (string k in kv.Value.Keys.Union(game[kv.Key].Keys))
                    {
                        object a, b;
                        kv.Value.TryGetValue(k, out a); game[kv.Key].TryGetValue(k, out b);
                        if (Describe(a) == Describe(b)) continue;
                        if (!fields.ContainsKey(k)) { fields[k] = 0; example[k] = kv.Key + ": game " + Short(Describe(b)) + " / mod " + Short(Describe(a)); }
                        fields[k]++;
                    }
                foreach (KeyValuePair<string, int> f in fields.OrderByDescending(x => x.Value).Take(12)) Console.WriteLine("    {0,5}  {1}   e.g. {2}", f.Value, f.Key, example[f.Key]);
            }
            return 0;
        }
        if (args[0] == "shape")  // GameDataProbe shape <sarc>: each file's tree, a few levels deep
        {
            byte[] s = File.ReadAllBytes(args[1]);
            foreach (SarcEntry e in Sarc.Parse(s)) Console.WriteLine(e.Name + "  " + Shape(Byml.Parse(Sarc.Extract(s, e)), 4));
            return 0;
        }
        byte[] sarc = File.ReadAllBytes(args[0]);
        int same = 0, total = 0;
        Dictionary<string, Dictionary<string, object>> flags = Flags(sarc);
        foreach (SarcEntry e in Sarc.Parse(sarc).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            byte[] d = Sarc.Extract(sarc, e);
            object root = Byml.Parse(d);
            byte[] again = Byml.Write(root, Byml.Version(d));
            total++;
            if (NativeMethods.BytesEqual(d, again)) same++;
            Dictionary<string, object> h = root as Dictionary<string, object>;
            string desc = h == null ? root.GetType().Name : string.Join(", ", h.Select(kv => kv.Key + (kv.Value is List<object> ? "[" + ((List<object>)kv.Value).Count + "]" : "")));
            string order = "";
            if (h != null && h.Count == 1 && h.Values.First() is List<object>)
            {
                List<int> hashes = ((List<object>)h.Values.First()).OfType<Dictionary<string, object>>().Where(f => f.ContainsKey("HashValue")).Select(f => (int)f["HashValue"]).ToList();
                if (hashes.Count > 1)
                    order = string.Format("  sorted signed {0}, unsigned {1}; first {2:X8} last {3:X8}",
                        hashes.Zip(hashes.Skip(1), (a, b) => a <= b).All(x => x), hashes.Zip(hashes.Skip(1), (a, b) => (uint)a <= (uint)b).All(x => x), hashes[0], hashes[hashes.Count - 1]);
            }
            Console.WriteLine("{0}  v{1}  {2} bytes  {3}{4}", e.Name, Byml.Version(d), d.Length, desc, order);
        }
        Console.WriteLine("BYML round trip byte-identical: {0} of {1}", same, total);
        Console.WriteLine("duplicate flags in this file: {0}; ResetType by file kind: {1}", Dupes, string.Join(", ", RevivalReset.Select(kv => kv.Key + ":" + kv.Value)));
        foreach (SarcEntry e in Sarc.Parse(sarc).Take(1))
        {
            byte[] d = Sarc.Extract(sarc, e), again = Byml.Write(Byml.Parse(d), Byml.Version(d));
            int at = 0; while (at < Math.Min(d.Length, again.Length) && d[at] == again[at]) at++;
            Console.WriteLine("first difference in {0}: byte {1} of {2} / {3}", e.Name, at, d.Length, again.Length);
        }
        if (args.Length > 1)
        {
            Dupes = 0;
            Dictionary<string, Dictionary<string, object>> other = Flags(File.ReadAllBytes(args[1]));
            Console.WriteLine("duplicate flags there: {0}", Dupes);
            int changed = 0, onlyHere = 0, onlyThere = 0;
            Dictionary<string, int> fields = new Dictionary<string, int>();
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in flags)
            {
                Dictionary<string, object> o;
                if (!other.TryGetValue(kv.Key, out o)) { onlyHere++; continue; }
                if (SameFlag(kv.Value, o)) continue;
                changed++;
                foreach (string k in kv.Value.Keys.Union(o.Keys))
                {
                    object x, y;
                    kv.Value.TryGetValue(k, out x); o.TryGetValue(k, out y);
                    if (Describe(x) == Describe(y)) continue;
                    if (!fields.ContainsKey(k)) fields[k] = 0;
                    fields[k]++;
                }
            }
            Console.WriteLine("changed fields: " + string.Join(", ", fields.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in flags.Where(kv => other.ContainsKey(kv.Key) && !SameFlag(kv.Value, other[kv.Key])
                && Describe(kv.Value["MinValue"]) == Describe(other[kv.Key]["MinValue"])).Take(6))
                Console.WriteLine("  changed: " + kv.Key + "  " + Describe(kv.Value) + "  vs  " + Describe(other[kv.Key]));
            onlyThere = other.Keys.Count(k => !flags.ContainsKey(k));
            Console.WriteLine("flags: {0} here, {1} there; {2} only here, {3} only there, {4} changed", flags.Count, other.Count, onlyHere, onlyThere, changed);
            foreach (string k in flags.Keys.Where(k => !other.ContainsKey(k)).Take(8)) Console.WriteLine("  only here: " + k);
            foreach (string k in other.Keys.Where(k => !flags.ContainsKey(k)).Take(8)) Console.WriteLine("  only there: " + k);
        }
        return 0;
    }

    private static string Shape(object o, int depth)
    {
        Dictionary<string, object> h = o as Dictionary<string, object>;
        List<object> a = o as List<object>;
        if (h != null) return "{" + string.Join(", ", h.Select(kv => kv.Key + ": " + (depth > 0 ? Shape(kv.Value, depth - 1) : "..."))) + "}";
        if (a != null) return "[" + a.Count + "x " + (a.Count > 0 && depth > 0 ? Shape(a[0], depth - 1) : "") + (a.Count > 1 && depth > 0 ? " | " + Shape(a[1], depth - 1) : "") + "]";
        return o == null ? "null" : o.GetType().Name + "(" + o + ")";
    }

    private static string Short(string s)
    {
        return s.Length > 60 ? s.Substring(0, 60) + "..." : s;
    }

    private static int Dupes;
    private static readonly Dictionary<string, int> RevivalReset = new Dictionary<string, int>();

    private static bool Revival(string name)
    {
        return name.StartsWith("/revival_", StringComparison.Ordinal);
    }

    // "<type key>/<DataName>" -> the flag, for every flag in every file
    private static Dictionary<string, Dictionary<string, object>> Flags(byte[] sarc)
    {
        Dictionary<string, Dictionary<string, object>> all = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (SarcEntry e in Sarc.Parse(sarc))
        {
            Dictionary<string, object> h = Byml.Parse(Sarc.Extract(sarc, e)) as Dictionary<string, object>;
            if (h == null) continue;
            foreach (KeyValuePair<string, object> kv in h)
            {
                List<object> list = kv.Value as List<object>;
                if (list == null) continue;
                foreach (Dictionary<string, object> f in list.OfType<Dictionary<string, object>>())
                    if (f.ContainsKey("DataName"))
                    {
                        string id = kv.Key + "/" + f["DataName"];
                        if (all.ContainsKey(id)) Dupes++;
                        all[id] = f;
                        if (Revival(e.Name) && !RevivalReset.ContainsKey(f["ResetType"].ToString())) RevivalReset[f["ResetType"].ToString()] = 0;
                        if (Revival(e.Name)) RevivalReset[f["ResetType"].ToString()]++;
                        else { string k = "plain " + f["ResetType"]; if (!RevivalReset.ContainsKey(k)) RevivalReset[k] = 0; RevivalReset[k]++; }
                    }
            }
        }
        return all;
    }

    private static bool SameFlag(Dictionary<string, object> a, Dictionary<string, object> b)
    {
        return Describe(a) == Describe(b);
    }

    private static string Describe(object o)
    {
        Dictionary<string, object> h = o as Dictionary<string, object>;
        if (h != null) return "{" + string.Join(",", h.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + Describe(kv.Value))) + "}";
        List<object> a = o as List<object>;
        if (a != null) return "[" + string.Join(",", a.Select(Describe)) + "]";
        return o == null ? "null" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
    }
}
