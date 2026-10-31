using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal.Formats;

// MapProbe <gameDlcRomfs> <modA dlc romfs> <modB dlc romfs>: how two mods change the map units (Objs / Rails by HashId)
internal static class MapProbe
{
    private static Dictionary<string, object> Load(string path)
    {
        return (Dictionary<string, object>)Byml.Parse(Yaz0.DecompressIfNeeded(File.ReadAllBytes(path)));
    }

    private static Dictionary<ulong, Dictionary<string, object>> ById(Dictionary<string, object> root, string list)
    {
        Dictionary<ulong, Dictionary<string, object>> d = new Dictionary<ulong, Dictionary<string, object>>();
        object l;
        if (!root.TryGetValue(list, out l)) return d;
        foreach (Dictionary<string, object> o in ((List<object>)l).OfType<Dictionary<string, object>>())
        {
            ulong id = Convert.ToUInt64(o["HashId"]);
            if (d.ContainsKey(id)) dupes++;
            d[id] = o;
        }
        return d;
    }

    private static int dupes;
    private static readonly List<string> psamples = new List<string>(), bsamples = new List<string>();

    private static Dictionary<string, object> Params(Dictionary<string, object> o)
    {
        object p;
        return o.TryGetValue("!Parameters", out p) && p is Dictionary<string, object> ? (Dictionary<string, object>)p : new Dictionary<string, object>();
    }

    private static string Show(Dictionary<string, object> p, string k)
    {
        object v;
        return p.TryGetValue(k, out v) ? (v == null ? "null" : v.ToString()) : "-";
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

    private static List<string> Diff(Dictionary<string, object> a, Dictionary<string, object> b)
    {
        return a.Keys.Union(b.Keys).Where(k => { object x, y; a.TryGetValue(k, out x); b.TryGetValue(k, out y); return !Same(x, y); }).ToList();
    }

    private static Dictionary<string, object> Without(Dictionary<string, object> o, string param)
    {
        Dictionary<string, object> c = new Dictionary<string, object>(o);
        object p;
        if (c.TryGetValue("!Parameters", out p) && p is Dictionary<string, object>)
        {
            Dictionary<string, object> pc = new Dictionary<string, object>((Dictionary<string, object>)p);
            pc.Remove(param);
            c["!Parameters"] = pc;
        }
        return c;
    }

    // check <game> <A> <B>: merges every map both change and checks A's changes and B's additions are all there
    private static int Check(string game, string ma, string mb)
    {
        int files = 0, bad = 0, lostA = 0, lostB = 0, keptDelA = 0, same = 0, conflicts = 0;
        long gameSize = 0, mergedSize = 0;
        foreach (string fa in Directory.GetFiles(Path.Combine(ma, "Map"), "*.smubin", SearchOption.AllDirectories))
        {
            string rel = fa.Substring(ma.Length + 1), fb = Path.Combine(mb, rel), fg = Path.Combine(game, rel);
            if (!File.Exists(fb) || !File.Exists(fg)) continue;
            files++;
            byte[] graw = File.ReadAllBytes(fg);
            GuardianTerminal.Build.BymlMerger m;
            byte[] self = GuardianTerminal.Build.BymlMerger.TryMerge(graw, new List<byte[]> { graw }, out m);
            if (!Same(Byml.Parse(Yaz0.Decompress(self)), Byml.Parse(Yaz0.Decompress(graw)))) bad++; else same++;
            byte[] merged = GuardianTerminal.Build.BymlMerger.TryMerge(graw, new List<byte[]> { File.ReadAllBytes(fa), File.ReadAllBytes(fb) }, out m);
            conflicts += m.Conflicts;
            gameSize += graw.Length; mergedSize += merged.Length;
            Dictionary<string, object> mr = (Dictionary<string, object>)Byml.Parse(Yaz0.Decompress(merged));
            Dictionary<string, object> g = Load(fg), a = Load(fa), b = Load(fb);
            foreach (string list in new[] { "Objs", "Rails" })
            {
                var G = ById(g, list); var A = ById(a, list); var B = ById(b, list); var M = ById(mr, list);
                foreach (var kv in A)
                {
                    Dictionary<string, object> go;
                    if (G.TryGetValue(kv.Key, out go) && Same(go, kv.Value)) continue;
                    Dictionary<string, object> mo;
                    bool collide = !G.ContainsKey(kv.Key) && B.ContainsKey(kv.Key);
                    if (collide) continue;
                    if (!M.TryGetValue(kv.Key, out mo) || !Same(Without(mo, "DropTable"), Without(kv.Value, "DropTable")))
                    {
                        lostA++;
                        Console.WriteLine("lost {0} {1} {2}: {3}", rel, kv.Key, kv.Value["UnitConfigName"], mo == null ? "missing" : string.Join(",", Diff(mo, kv.Value)));
                    }
                }
                foreach (var kv in G) if (!A.ContainsKey(kv.Key) && M.ContainsKey(kv.Key)) keptDelA++;
                foreach (var kv in B) if (!G.ContainsKey(kv.Key) && !A.ContainsKey(kv.Key) && !M.ContainsKey(kv.Key)) lostB++;
            }
        }
        Console.WriteLine("files {0}: game-only merge same as game {1}, different {2}", files, same, bad);
        Console.WriteLine("A changes lost {0}, A removals undone {1}, B additions lost {2}, values both changed {3}", lostA, keptDelA, lostB, conflicts);
        Console.WriteLine("size: game {0:N0} bytes, merged {1:N0} bytes", gameSize, mergedSize);
        return bad + lostA + keptDelA + lostB == 0 ? 0 : 1;
    }

    private static int Main(string[] args)
    {
        if (args[0] == "check") return Check(args[1], args[2], args[3]);
        string game = args[0], ma = args[1], mb = args[2];
        Dictionary<string, int> fieldsA = new Dictionary<string, int>(), fieldsB = new Dictionary<string, int>(), both = new Dictionary<string, int>();
        int addA = 0, addB = 0, delA = 0, delB = 0, chA = 0, chB = 0, chBoth = 0, collide = 0, delVsCh = 0, rootDiff = 0, files = 0, noGame = 0;
        int nameBoth = 0;
        List<string> samples = new List<string>();
        foreach (string fa in Directory.GetFiles(Path.Combine(ma, "Map"), "*.smubin", SearchOption.AllDirectories))
        {
            string rel = fa.Substring(ma.Length + 1);
            string fb = Path.Combine(mb, rel), fg = Path.Combine(game, rel);
            if (!File.Exists(fb)) continue;
            if (!File.Exists(fg)) { noGame++; continue; }
            files++;
            Dictionary<string, object> g = Load(fg), a = Load(fa), b = Load(fb);
            foreach (string k in g.Keys.Union(a.Keys).Union(b.Keys))
                if (k != "Objs" && k != "Rails") { object x, y, z; g.TryGetValue(k, out x); a.TryGetValue(k, out y); b.TryGetValue(k, out z); if (!Same(x, y) || !Same(x, z)) rootDiff++; }
            foreach (string list in new[] { "Objs", "Rails" })
            {
                var G = ById(g, list); var A = ById(a, list); var B = ById(b, list);
                foreach (var kv in A) if (!G.ContainsKey(kv.Key)) { addA++; Dictionary<string, object> o; if (B.TryGetValue(kv.Key, out o) && !G.ContainsKey(kv.Key)) { if (!Same(o, kv.Value)) collide++; } }
                foreach (var kv in B) if (!G.ContainsKey(kv.Key)) addB++;
                foreach (var kv in G)
                {
                    Dictionary<string, object> oa, ob;
                    bool ia = A.TryGetValue(kv.Key, out oa), ib = B.TryGetValue(kv.Key, out ob);
                    if (!ia) delA++;
                    if (!ib) delB++;
                    List<string> da = ia ? Diff(kv.Value, oa) : null, db = ib ? Diff(kv.Value, ob) : null;
                    if (da != null && da.Count > 0) { chA++; foreach (string f in da) fieldsA[f] = (fieldsA.ContainsKey(f) ? fieldsA[f] : 0) + 1; }
                    if (db != null && db.Count > 0) { chB++; foreach (string f in db) fieldsB[f] = (fieldsB.ContainsKey(f) ? fieldsB[f] : 0) + 1; }
                    if (da != null && da.Contains("!Parameters")) foreach (string f in Diff(Params(kv.Value), Params(oa))) fieldsA["P:" + f] = (fieldsA.ContainsKey("P:" + f) ? fieldsA["P:" + f] : 0) + 1;
                    if (db != null && db.Contains("!Parameters"))
                    {
                        List<string> kb = Diff(Params(kv.Value), Params(ob));
                        foreach (string f in kb) fieldsB["P:" + f] = (fieldsB.ContainsKey("P:" + f) ? fieldsB["P:" + f] : 0) + 1;
                        if (bsamples.Count < 6) bsamples.Add(string.Format("{0} {1} {2}: {3}", rel, kv.Key, kv.Value["UnitConfigName"], string.Join("; ", kb.Select(f => f + " " + Show(Params(kv.Value), f) + " -> " + Show(Params(ob), f)))));
                    }
                    if ((!ia && db != null && db.Count > 0) || (!ib && da != null && da.Count > 0)) delVsCh++;
                    if (da != null && db != null && da.Count > 0 && db.Count > 0)
                    {
                        chBoth++;
                        foreach (string f in da.Intersect(db)) { both[f] = (both.ContainsKey(f) ? both[f] : 0) + 1; }
                        if (da.Contains("!Parameters") && db.Contains("!Parameters"))
                        {
                            Dictionary<string, object> pg = Params(kv.Value), pa = Params(oa), pb = Params(ob);
                            List<string> ka = Diff(pg, pa), kb = Diff(pg, pb);
                            foreach (string f in ka.Intersect(kb))
                            {
                                both["P:" + f] = (both.ContainsKey("P:" + f) ? both["P:" + f] : 0) + 1;
                                object x, y; pa.TryGetValue(f, out x); pb.TryGetValue(f, out y);
                                if (Same(x, y)) both["P=" + f] = (both.ContainsKey("P=" + f) ? both["P=" + f] : 0) + 1;
                                else if (psamples.Count < 12) psamples.Add(string.Format("{0} {1} {2}.{3}: game {4}, A {5}, B {6}", rel, kv.Key, kv.Value["UnitConfigName"], f, Show(pg, f), Show(pa, f), Show(pb, f)));
                            }
                        }
                        if (da.Contains("UnitConfigName") && db.Contains("UnitConfigName"))
                        {
                            nameBoth++;
                            if (samples.Count < 25) samples.Add(string.Format("{0} {1}: game {2}, A {3}, B {4}", rel, kv.Key, kv.Value["UnitConfigName"], oa["UnitConfigName"], ob["UnitConfigName"]));
                        }
                    }
                }
            }
        }
        Console.WriteLine("files {0} (no game copy {1}), root-field diffs {2}, duplicate ids {3}", files, noGame, rootDiff, dupes);
        Console.WriteLine("A: added {0}, removed {1}, changed {2}", addA, delA, chA);
        Console.WriteLine("B: added {0}, removed {1}, changed {2}", addB, delB, chB);
        Console.WriteLine("changed by both {0} (both renamed {1}), added by both with different content {2}, removed by one / changed by other {3}", chBoth, nameBoth, collide, delVsCh);
        Console.WriteLine("A fields: " + string.Join(", ", fieldsA.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value)));
        Console.WriteLine("B fields: " + string.Join(", ", fieldsB.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value)));
        Console.WriteLine("both fields: " + string.Join(", ", both.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value)));
        foreach (string s in samples) Console.WriteLine("  " + s);
        foreach (string s in psamples) Console.WriteLine("  " + s);
        foreach (string s in bsamples) Console.WriteLine("  B " + s);
        return 0;
    }
}
