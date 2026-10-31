using System;
using System.Collections.Generic;
using System.Linq;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    // Merges mods' Actor/ActorInfo.product.sbyml (every actor's settings) into the game's own, actor by actor, as BCML
    // does. A mod made for 1.6.0 carries the whole 1.6.0 list: taken as it is, it drops 1.9.0's actors, and with two
    // mods the second drops the first's new actors. So every game actor stays, with each mod's changes applied in load
    // order; the sizes the game reserves (instSize, the bounding boxes) only grow, since most of their differences are
    // just 1.6.0's values; actors a mod adds are added, and the list is sorted by name hash again, as the game reads it.
    internal sealed class ActorInfoMerger
    {
        public const string Path = "Actor/ActorInfo.product.sbyml";

        private readonly Dictionary<string, object> root;
        private readonly int version;
        private readonly Dictionary<string, Dictionary<string, object>> actors = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, object>> original = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        public int Changed { get; private set; }
        public int Added { get; private set; }

        public ActorInfoMerger(byte[] game)
        {
            version = Byml.Version(game);
            root = (Dictionary<string, object>)Byml.Parse(game);
            foreach (Dictionary<string, object> a in ((List<object>)root["Actors"]).OfType<Dictionary<string, object>>())
            {
                string name = a["name"] as string;
                if (name == null || actors.ContainsKey(name)) continue;
                actors[name] = a;
                original[name] = new Dictionary<string, object>(a, StringComparer.Ordinal);
            }
        }

        public void Add(byte[] mod)
        {
            Dictionary<string, object> r = Byml.Parse(mod) as Dictionary<string, object>;
            object list;
            if (r == null || !r.TryGetValue("Actors", out list) || !(list is List<object>)) throw new InvalidOperationException("not an actor list");
            foreach (Dictionary<string, object> a in ((List<object>)list).OfType<Dictionary<string, object>>())
            {
                string name = a["name"] as string;
                if (name == null) continue;
                Dictionary<string, object> mine;
                if (!actors.TryGetValue(name, out mine))
                {
                    actors[name] = a;
                    original[name] = new Dictionary<string, object>(a, StringComparer.Ordinal);
                    Added++;
                    continue;
                }
                Dictionary<string, object> before = original[name];
                bool any = false;
                foreach (KeyValuePair<string, object> kv in a)
                {
                    object was;
                    before.TryGetValue(kv.Key, out was);
                    if (Same(was, kv.Value)) continue;
                    object now;
                    mine.TryGetValue(kv.Key, out now);
                    object merged = Grow(kv.Key, now, kv.Value);
                    if (Same(merged, now)) continue;
                    mine[kv.Key] = merged;
                    if (!Sizes.Contains(kv.Key)) any = true;
                }
                if (any) Changed++;
            }
        }

        private static readonly HashSet<string> Sizes = new HashSet<string>(StringComparer.Ordinal) { "instSize", "boundingForTraverse", "aabbMin", "aabbMax" };

        // Sizes take the larger (a box: the one holding both); anything else, the mod's
        private static object Grow(string key, object game, object mod)
        {
            if (game == null) return mod;
            switch (key)
            {
                case "instSize":
                case "boundingForTraverse":
                    if (game is int && mod is int) return Math.Max((int)game, (int)mod);
                    if (game is float && mod is float) return Math.Max((float)game, (float)mod);
                    return game;
                case "aabbMin":
                case "aabbMax":
                    Dictionary<string, object> g = game as Dictionary<string, object>, m = mod as Dictionary<string, object>;
                    if (g == null || m == null) return game;
                    Dictionary<string, object> box = new Dictionary<string, object>(g, StringComparer.Ordinal);
                    foreach (string axis in g.Keys.ToList())
                        if (g[axis] is float && m.ContainsKey(axis) && m[axis] is float)
                            box[axis] = key == "aabbMin" ? Math.Min((float)g[axis], (float)m[axis]) : Math.Max((float)g[axis], (float)m[axis]);
                    return box;
                default:
                    return mod;
            }
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

        // The merged list: actors and their hashes sorted by hash (crc32 of the name), as the game looks them up
        public byte[] Write()
        {
            List<KeyValuePair<uint, Dictionary<string, object>>> sorted = actors
                .Select(kv => new KeyValuePair<uint, Dictionary<string, object>>(Crc32.Compute(kv.Key), kv.Value))
                .OrderBy(kv => kv.Key).ToList();
            Dictionary<string, object> output = new Dictionary<string, object>(root, StringComparer.Ordinal);
            output["Actors"] = sorted.Select(kv => (object)kv.Value).ToList();
            output["Hashes"] = sorted.Select(kv => kv.Key <= int.MaxValue ? (object)(int)kv.Key : (object)kv.Key).ToList();
            return Byml.Write(output, version);
        }
    }
}
