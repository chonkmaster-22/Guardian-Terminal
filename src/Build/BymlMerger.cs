using System;
using System.Collections.Generic;
using System.Linq;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    // Merges several mods' versions of one of the game's BYML files (map units, spawn tables, event lists, ...) into
    // the game's own, entry by entry, as BCML merges maps: each mod's differences from the game are applied, so two
    // mods that change different things in the same file both get them. Hashes merge key by key; arrays of entries
    // that carry an ID (map objects by HashId, areas by AreaNumber, spawn lists by name, ...) entry by entry, other
    // arrays item by item when the lengths match. Where mods disagree:
    //  - an entry one mod removes from an array (a map object), or a whole hash or array it removes from a hash (an
    //    object's !Parameters), is removed, even if another mod changed it;
    //  - a single value one mod removes from a hash and another sets is kept (removing a setting means its default);
    //  - two different values: the later mod's, except spawn amounts ("num"), which take the larger.
    internal sealed class BymlMerger
    {
        private static readonly object Missing = new object();
        private static readonly string[] IdKeys = { "HashId", "AreaNumber", "DataName", "folder_name", "name", "Name" };

        public int Conflicts { get; private set; }
        public readonly List<string> ConflictPaths = new List<string>();

        // The merged file (Yaz0 compressed as the game's is), or null when the game's or a mod's copy isn't BYML.
        public static byte[] TryMerge(byte[] gameRaw, List<byte[]> modRaws, out BymlMerger merger)
        {
            merger = null;
            byte[] game;
            object g;
            List<object> mods = new List<object>();
            try
            {
                game = Yaz0.DecompressIfNeeded(gameRaw);
                if (!Byml.IsByml(game)) return null;
                g = Byml.Parse(game);
                foreach (byte[] raw in modRaws)
                {
                    byte[] m = Yaz0.DecompressIfNeeded(raw);
                    if (!Byml.IsByml(m)) return null;
                    mods.Add(Byml.Parse(m));
                }
            }
            catch (Exception)
            {
                return null;
            }
            merger = new BymlMerger();
            object merged = merger.Merge(g, mods, false, "");
            if (merged == Missing || !(merged is Dictionary<string, object> || merged is List<object>)) return null;
            byte[] bytes = Byml.Write(merged, Byml.Version(game));
            if (!Yaz0.IsYaz0(gameRaw)) return bytes;
            byte[] tail = new byte[8];
            Buffer.BlockCopy(gameRaw, 8, tail, 0, 8);
            return Yaz0Compressor.Compress(bytes, tail);
        }

        // g: the game's value (or Missing); values: each mod's (or Missing); element: g is an entry of an array
        private object Merge(object g, List<object> values, bool element, string path)
        {
            List<int> changed = Enumerable.Range(0, values.Count).Where(i => !Same(values[i], g)).ToList();
            if (changed.Count == 0) return g;
            if (changed.All(i => Same(values[i], values[changed[0]]))) return values[changed[0]];
            if (changed.Any(i => values[i] == Missing))
            {
                if (element || g is Dictionary<string, object> || g is List<object>) return Missing;
                changed = changed.Where(i => values[i] != Missing).ToList();
                if (changed.All(i => Same(values[i], values[changed[0]]))) return values[changed[0]];
            }

            Dictionary<string, object> gh = g as Dictionary<string, object>;
            if ((g == Missing || gh != null) && changed.All(i => values[i] is Dictionary<string, object>)
                && !(g == Missing && changed.Any(i => ((Dictionary<string, object>)values[i]).ContainsKey("HashId"))))
            {
                if (gh == null) gh = new Dictionary<string, object>(StringComparer.Ordinal);
                List<string> keys = new List<string>(gh.Keys);
                HashSet<string> seen = new HashSet<string>(keys, StringComparer.Ordinal);
                foreach (int i in changed)
                    foreach (string k in ((Dictionary<string, object>)values[i]).Keys)
                        if (seen.Add(k)) keys.Add(k);
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (string k in keys)
                {
                    object gv;
                    if (!gh.TryGetValue(k, out gv)) gv = Missing;
                    List<object> vs = new List<object>(values.Count);
                    for (int i = 0; i < values.Count; i++)
                    {
                        object v = gv;
                        if (changed.Contains(i) && !((Dictionary<string, object>)values[i]).TryGetValue(k, out v)) v = Missing;
                        vs.Add(v);
                    }
                    object r = Merge(gv, vs, false, path + "/" + k);
                    if (r != Missing) result[k] = r;
                }
                return result;
            }

            List<object> gl = g as List<object>;
            if ((g == Missing || gl != null) && changed.All(i => values[i] is List<object>))
            {
                object r = MergeList(gl ?? new List<object>(), values.Select((v, i) => changed.Contains(i) ? (List<object>)v : null).ToList(), path);
                if (r != null) return r;
            }

            // two different values
            Conflicts++;
            if (ConflictPaths.Count < 5) ConflictPaths.Add(path);
            if (path.EndsWith("/num", StringComparison.Ordinal) && changed.All(i => IsNumber(values[i])))
                return values[changed.OrderBy(i => Convert.ToDouble(values[i])).Last()];
            return values[changed[changed.Count - 1]];
        }

        // lists: each mod's array, or null where it's the game's; null result: can't be merged
        private object MergeList(List<object> gl, List<List<object>> lists, string path)
        {
            List<List<object>> all = new List<List<object>> { gl };
            all.AddRange(lists.Where(l => l != null));
            string idKey = IdKeys.FirstOrDefault(k => all.All(l => l.All(e =>
            {
                Dictionary<string, object> d = e as Dictionary<string, object>;
                object v;
                return d != null && d.TryGetValue(k, out v) && v != null && !(v is Dictionary<string, object>) && !(v is List<object>);
            })));
            if (idKey != null && all.Any(l => l.Count > 0))
            {
                List<KeyValuePair<string, object>> game = Keyed(gl, idKey);
                Dictionary<string, object> gameById = game.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                List<Dictionary<string, object>> mods = lists.Select(l => l == null ? null : Keyed(l, idKey).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)).ToList();
                List<string> ids = game.Select(kv => kv.Key).ToList();
                HashSet<string> seen = new HashSet<string>(ids, StringComparer.Ordinal);
                foreach (List<object> l in lists)
                    if (l != null)
                        foreach (KeyValuePair<string, object> kv in Keyed(l, idKey))
                            if (seen.Add(kv.Key)) ids.Add(kv.Key);
                List<object> result = new List<object>(ids.Count);
                foreach (string id in ids)
                {
                    object gv;
                    if (!gameById.TryGetValue(id, out gv)) gv = Missing;
                    List<object> vs = mods.Select(m =>
                    {
                        if (m == null) return gv;
                        object v;
                        return m.TryGetValue(id, out v) ? v : Missing;
                    }).ToList();
                    object r = Merge(gv, vs, true, path + "[" + id + "]");
                    if (r != Missing) result.Add(r);
                }
                // the game keeps some arrays sorted by their ID (map objects by HashId): so does the merge
                if (gl.Count > 1 && IsSorted(gl, idKey)) result = result.OrderBy(e => ((Dictionary<string, object>)e)[idKey], IdComparer.Instance).ToList();
                return result;
            }
            if (lists.All(l => l == null || l.Count == gl.Count))
            {
                List<object> result = new List<object>(gl.Count);
                for (int j = 0; j < gl.Count; j++)
                {
                    object r = Merge(gl[j], lists.Select(l => l == null ? gl[j] : l[j]).ToList(), false, path + "[" + j + "]");
                    if (r != Missing) result.Add(r);
                }
                return result;
            }
            if (all.All(l => l.All(e => !(e is Dictionary<string, object>) && !(e is List<object>))))
            {
                // a set of plain values: the game's, less those a mod removed, plus those a mod added
                List<object> result = gl.Where(e => lists.All(l => l == null || l.Any(x => Same(x, e)))).ToList();
                foreach (List<object> l in lists)
                    if (l != null)
                        foreach (object e in l)
                            if (!result.Any(x => Same(x, e))) result.Add(e);
                return result;
            }
            return null;
        }

        // entries by ID (a repeated ID gets #2, #3, ...)
        private static List<KeyValuePair<string, object>> Keyed(List<object> list, string idKey)
        {
            Dictionary<string, int> count = new Dictionary<string, int>(StringComparer.Ordinal);
            List<KeyValuePair<string, object>> r = new List<KeyValuePair<string, object>>(list.Count);
            foreach (object e in list)
            {
                string id = IdText(((Dictionary<string, object>)e)[idKey]);
                int n;
                count.TryGetValue(id, out n);
                count[id] = ++n;
                r.Add(new KeyValuePair<string, object>(n == 1 ? id : id + "#" + n, e));
            }
            return r;
        }

        private static string IdText(object v)
        {
            return IsInteger(v) ? Convert.ToDecimal(v).ToString(System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool IsSorted(List<object> list, string idKey)
        {
            for (int i = 1; i < list.Count; i++)
                if (IdComparer.Instance.Compare(((Dictionary<string, object>)list[i - 1])[idKey], ((Dictionary<string, object>)list[i])[idKey]) > 0) return false;
            return true;
        }

        private sealed class IdComparer : IComparer<object>
        {
            public static readonly IdComparer Instance = new IdComparer();

            public int Compare(object a, object b)
            {
                if (IsNumber(a) && IsNumber(b)) return Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b));
                return string.CompareOrdinal(Convert.ToString(a), Convert.ToString(b));
            }
        }

        private static bool IsInteger(object v)
        {
            return v is int || v is uint || v is long || v is ulong;
        }

        private static bool IsNumber(object v)
        {
            return IsInteger(v) || v is float || v is double;
        }

        // The same value (numbers: the same number, whatever their BYML type)
        private static bool Same(object a, object b)
        {
            if (a == b) return true;
            if (a == null || b == null || a == Missing || b == Missing) return false;
            List<object> la = a as List<object>, lb = b as List<object>;
            if (la != null || lb != null)
            {
                if (la == null || lb == null || la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++) if (!Same(la[i], lb[i])) return false;
                return true;
            }
            Dictionary<string, object> da = a as Dictionary<string, object>, db = b as Dictionary<string, object>;
            if (da != null || db != null)
            {
                if (da == null || db == null || da.Count != db.Count) return false;
                foreach (KeyValuePair<string, object> kv in da)
                {
                    object v;
                    if (!db.TryGetValue(kv.Key, out v) || !Same(kv.Value, v)) return false;
                }
                return true;
            }
            if (IsInteger(a) && IsInteger(b)) return Convert.ToDecimal(a) == Convert.ToDecimal(b);
            if (IsNumber(a) && IsNumber(b) && !IsInteger(a) && !IsInteger(b)) return Convert.ToDouble(a) == Convert.ToDouble(b);
            byte[] ba = a as byte[], bb = b as byte[];
            if (ba != null && bb != null) return ba.SequenceEqual(bb);
            return a.GetType() == b.GetType() && a.Equals(b);
        }
    }
}
