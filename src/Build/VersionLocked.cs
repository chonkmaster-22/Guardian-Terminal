using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal.Formats;

namespace GuardianTerminal.Build
{
    // Files inside the game's packs that the game's code after 1.6.0 depends on in their new form: a mod's copy made
    // for 1.6.0 crashes 1.9.0 before its first frame (found with Relics of the Past and Waikuteru's randomizer, which
    // ship the 1.6.0 sound and effect link databases and HUD layout). With game files newer than 1.6.0, the merge
    // keeps the game's own of these; the rest of such a mod still loads.
    internal static class VersionLocked
    {
        private const string Bootup = "Pack/Bootup.pack";

        private static readonly Dictionary<string, string> InBootup = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "SLink2/SLink2DB.sbslnk", "sound links" },
            { "ELink2/ELink2DB.sbelnk", "effect links" },
            { "Layout/Common.sblarc", "HUD layout" }
        };

        // Whether game files of this version need it: anything newer than 1.6.0 (and an unreadable version, since
        // Savage Wind runs 1.9.0)
        public static bool AppliesTo(string gameVersion)
        {
            Version v;
            return !Version.TryParse(gameVersion ?? "", out v) || v > new Version(1, 6, 0);
        }

        // The locked files as the merge names them ("Pack/Bootup.pack/SLink2/SLink2DB.sbslnk"), or none
        public static HashSet<string> For(string gameVersion)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (AppliesTo(gameVersion))
                foreach (string name in InBootup.Keys) set.Add(Bootup + "/" + name);
            return set;
        }

        // "sound links" for "Pack/Bootup.pack/SLink2/SLink2DB.sbslnk"
        public static string Describe(string path)
        {
            string what;
            return path.StartsWith(Bootup + "/", StringComparison.Ordinal) && InBootup.TryGetValue(path.Substring(Bootup.Length + 1), out what) ? what : path;
        }

        // What of the locked files a mod's Bootup.pack changes (described), compared with the game's
        public static List<string> ChangedBy(string modBootup, string gameBootup)
        {
            List<string> changed = new List<string>();
            if (!File.Exists(modBootup) || !File.Exists(gameBootup)) return changed;
            try
            {
                byte[] mod = Yaz0.DecompressIfNeeded(File.ReadAllBytes(modBootup)), game = Yaz0.DecompressIfNeeded(File.ReadAllBytes(gameBootup));
                if (!Sarc.IsSarc(mod) || !Sarc.IsSarc(game)) return changed;
                Dictionary<string, SarcEntry> g = Sarc.Parse(game).ToDictionary(e => e.Name, StringComparer.Ordinal);
                foreach (SarcEntry e in Sarc.Parse(mod))
                {
                    SarcEntry ge;
                    if (InBootup.ContainsKey(e.Name) && g.TryGetValue(e.Name, out ge) && !NativeMethods.BytesEqual(Sarc.Extract(mod, e), Sarc.Extract(game, ge)))
                        changed.Add(InBootup[e.Name]);
                }
            }
            catch (Exception) { }
            return changed;
        }
    }
}
