using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal.Formats;

// BymlDiff <game> <modA> <modB> [max]: every path where a mod differs from the game (lists index by index);
// "!!" marks paths both change differently
internal static class BymlDiff
{
    private static int shown, max = 60, onlyA, onlyB, sameBoth, conflict;

    private static object Load(string p) { return Byml.Parse(Yaz0.DecompressIfNeeded(File.ReadAllBytes(p))); }

    private static bool Same(object a, object b)
    {
        if (a == null || b == null) return a == b;
        List<object> la = a as List<object>, lb = b as List<object>;
        if (la != null && lb != null) return la.Count == lb.Count && la.Zip(lb, Same).All(x => x);
        Dictionary<string, object> da = a as Dictionary<string, object>, db = b as Dictionary<string, object>;
        if (da != null && db != null) return da.Count == db.Count && da.All(kv => db.ContainsKey(kv.Key) && Same(kv.Value, db[kv.Key]));
        return a.GetType() == b.GetType() && a.Equals(b);
    }

    private static string Show(object v)
    {
        if (v == null) return "-";
        if (v is List<object>) return "[" + ((List<object>)v).Count + "]";
        if (v is Dictionary<string, object>) return "{" + string.Join(",", ((Dictionary<string, object>)v).Keys.Take(4)) + "}";
        return v.ToString();
    }

    private static void Walk(string path, object g, object a, object b)
    {
        bool ca = !Same(g, a), cb = !Same(g, b);
        if (!ca && !cb) return;
        Dictionary<string, object> dg = g as Dictionary<string, object>, da = a as Dictionary<string, object>, db = b as Dictionary<string, object>;
        List<object> lg = g as List<object>, la = a as List<object>, lb = b as List<object>;
        if (dg != null && (da != null || !ca) && (db != null || !cb))
        {
            foreach (string k in dg.Keys.Union(da != null ? da.Keys : dg.Keys).Union(db != null ? db.Keys : dg.Keys))
            {
                object x, y, z;
                dg.TryGetValue(k, out x);
                if (da != null) da.TryGetValue(k, out y); else y = x;
                if (db != null) db.TryGetValue(k, out z); else z = x;
                Walk(path + "/" + k, x, y, z);
            }
            return;
        }
        if (lg != null && (!ca || (la != null && la.Count == lg.Count)) && (!cb || (lb != null && lb.Count == lg.Count)))
        {
            for (int i = 0; i < lg.Count; i++) Walk(path + "[" + i + "]", lg[i], ca ? la[i] : lg[i], cb ? lb[i] : lg[i]);
            return;
        }
        string tag;
        if (ca && cb) { if (Same(a, b)) { sameBoth++; tag = "=="; } else { conflict++; tag = "!!"; } }
        else if (ca) { onlyA++; tag = "A "; }
        else { onlyB++; tag = " B"; }
        if (shown++ < max) Console.WriteLine("{0} {1}: game {2} | A {3} | B {4}", tag, path, Show(g), ca ? Show(a) : "", cb ? Show(b) : "");
    }

    private static int Main(string[] args)
    {
        if (args[0] == "show")  // show <file> <list index>
        {
            object r = Load(args[1]);
            object v = r is List<object> ? ((List<object>)r)[int.Parse(args[2])] : ((Dictionary<string, object>)r)[args[2]];
            Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(v));
            return 0;
        }
        if (args.Length > 3) max = int.Parse(args[3]);
        Walk("", Load(args[0]), Load(args[1]), Load(args[2]));
        Console.WriteLine("only A {0}, only B {1}, same in both {2}, conflicting {3}", onlyA, onlyB, sameBoth, conflict);
        return 0;
    }
}
