using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GuardianTerminal;
using GuardianTerminal.Build;
using GuardianTerminal.Formats;
using GuardianTerminal.Game;
using GuardianTerminal.Mods;

// Drives the engine without the window, from the manager's own folder (AppPaths.Root = this exe's folder).
//   EngineTest import <savagewind.exe> <archive>...     imports mods into the library (--no7zip: as without 7-Zip)
//   EngineTest importgb <savagewind.exe> <id> <archive> imports a mod with its GameBanana page's name and link
//   EngineTest sources [<folder>]                        the mods "Import mods" finds: emulators, SD cards, a folder
//   EngineTest build <on-folder,...> [--force]           builds with exactly those mods on (in library order)
//   EngineTest verify [<base dir> <dlc dir>]             checks the overlay (or an SD export) against the game files
//   EngineTest sdexport <sd root>                        exports the mods that are on as for a Switch SD card
//   EngineTest clear                                     empties the merge output
//   EngineTest profiles                                  exercises profiles (and puts things back as they were)
internal static class EngineTest
{
    private static int Main(string[] args)
    {
        AppPaths.EnsureDirectories();
        ModLibrary lib = ModLibrary.Load();
        Stopwatch t = Stopwatch.StartNew();
        string problem;
        if (args.Contains("--no7zip"))
        {
            ModImporter.IgnoreSevenZip = true;
            args = args.Where(a => a != "--no7zip").ToArray();
        }
        if (args[0] == "sources")
        {
            foreach (ModSource s in ModSources.Find())
            {
                Console.WriteLine("SOURCE {0} ({1}): {2} mod(s)", s.Name, s.Folder, s.Mods.Count);
                foreach (FoundMod f in s.Mods) Console.WriteLine("   {0}  <- {1}{2}", f.Name, f.Path, f.DlcFolder != null ? "  + DLC " + f.DlcFolder : "");
            }
            if (args.Length > 1)
            {
                List<FoundMod> inside = ModSources.InFolder(args[1]);
                Console.WriteLine("FOLDER {0}: {1} mod(s)", args[1], inside.Count);
                foreach (FoundMod f in inside) Console.WriteLine("   {0}  <- {1}", f.Name, f.Path);
            }
            return 0;
        }
        if (args[0] == "import" || args[0] == "importgb")
        {
            lib.Config.SavageWindExe = args[1];
            lib.Config.Save();
            SavageWindInstall sw = SavageWindInstall.Open(args[1], out problem);
            if (sw == null) { Console.WriteLine("open failed: " + problem); return 1; }
            VanillaIndex van = VanillaIndex.Build(sw.RomfsDir);
            VanillaIndex dlc = sw.HasDlc ? VanillaIndex.Build(sw.DlcRomfsDir) : null;
            Console.WriteLine("game files: {0} files, version {1}, DLC {2} ({3} ms)", van.FileCount, sw.GameVersion, dlc == null ? "none" : dlc.FileCount + " files, " + sw.DlcVersion, t.ElapsedMilliseconds);
            ModOrigin origin = null;
            IEnumerable<string> archives = args.Skip(2);
            if (args[0] == "importgb")
            {
                GbMod gb = GameBanana.GetMod(long.Parse(args[2]));
                Console.WriteLine("GAMEBANANA {0} \"{1}\" v{2} for {3} ({4}), {5} file(s): {6}", gb.Id, gb.Name, gb.Version, gb.Game, gb.IsBotwSwitch ? "BotW Switch" : "NOT BotW Switch",
                    gb.Files.Count, string.Join(", ", gb.Files.Select(f => f.Name + " [" + f.Size + " bytes, scan " + f.Scan + "]")));
                origin = new ModOrigin { Name = gb.Name, Version = gb.Version, Description = gb.Description, Url = gb.PageUrl };
                archives = args.Skip(3);
            }
            foreach (string a in archives)
            {
                t.Restart();
                List<InstalledMod> mods = ModImporter.Import(a, lib, van, dlc, opts => { Console.WriteLine("  choose: " + string.Join(" | ", opts)); return opts.Take(1).ToList(); }, s => Console.WriteLine("  " + s), origin);
                foreach (InstalledMod m in mods)
                {
                    lib.Add(m);
                    ModInfo i = m.Info;
                    Console.WriteLine("IMPORTED {0} -> mods\\{1} in {2} ms: status {3}, enabled {4}, replaced {5}, new {6}, no-folder {7}, ignored {8}, dlc {9} (usable {10}), size table {11}, core packs [{12}], bcml sizes {13}, bcml logs [{14}], url {15}",
                        i.Name, m.Folder, t.ElapsedMilliseconds, i.Status, m.Enabled, i.ReplacedFiles, i.NewFiles, i.UnloadableFiles, i.IgnoredFiles, i.DlcFiles, i.DlcUsableFiles, i.HasSizeTable,
                        string.Join(", ", i.CorePacks), File.Exists(m.BcmlSizesFile), string.Join(", ", i.BcmlLogs), i.SourceUrl);
                    foreach (string n in i.Notes) Console.WriteLine("   - " + L.Note(n));
                }
            }
            return 0;
        }
        if (args[0] == "clear")
        {
            OverlayBuilder.ClearOverlay();
            Console.WriteLine("CLEARED: overlay exists {0}", Directory.Exists(AppPaths.Overlay));
            return 0;
        }
        if (args[0] == "profiles") return Profiles(lib);
        SavageWindInstall s2 = SavageWindInstall.Open(lib.Config.SavageWindExe, out problem);
        VanillaIndex v2 = VanillaIndex.Build(s2.RomfsDir);
        VanillaIndex d2 = s2.HasDlc ? VanillaIndex.Build(s2.DlcRomfsDir) : null;
        if (args[0] == "reanalyze")
        {
            foreach (InstalledMod m in lib.Mods)
            {
                ModImporter.Analyze(m, v2, d2);
                m.SaveInfo();
                Console.WriteLine("{0}: status {1}, usable {2} (dlc {3}); {4}", m.DisplayName, m.Info.Status, m.Info.UsableFiles, m.Info.DlcUsableFiles, string.Join(" | ", m.Info.Notes.Select(L.Note)));
            }
            return 0;
        }
        if (args[0] == "build")
        {
            HashSet<string> on = new HashSet<string>(args.Length > 1 ? args[1].Split(',') : new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (InstalledMod m in lib.Mods) m.Enabled = on.Contains(m.Folder);
            lib.Save();
            t.Restart();
            BuildResult r = OverlayBuilder.Build(lib.EnabledInOrder(), s2, v2, d2, lib.Config, args.Contains("--force"), x => { });
            Console.WriteLine("BUILD {0} ms, up to date {1}, overlay {2}: {3}", t.ElapsedMilliseconds, r.UpToDate, r.OverlayDir ?? "(none)", r.Report.Summary());
            Console.WriteLine(File.ReadAllText(AppPaths.BuildReport));
            return 0;
        }
        if (args[0] == "rstb")
        {
            Rstb game = Rstb.Load(Path.Combine(s2.RomfsDir, SavageWindInstall.RstbRelPath.Replace('/', '\\')));
            string mine = Path.Combine(AppPaths.Overlay, SavageWindInstall.RstbRelPath.Replace('/', '\\'));
            Rstb over = File.Exists(mine) ? Rstb.Load(mine) : null;
            foreach (string k in args.Skip(1))
            {
                uint g, o;
                Console.WriteLine("{0}: game {1}, overlay {2}", k, game.TryGet(k, out g) ? g.ToString() : "-", over != null && over.TryGet(k, out o) ? o.ToString() : "-");
            }
            return 0;
        }
        if (args[0] == "sdexport")
        {
            t.Restart();
            BuildResult r = SdExport.Export(args[1], lib.EnabledInOrder(), s2, v2, d2, x => { });
            Console.WriteLine("SDEXPORT {0} ms to {1}: {2}", t.ElapsedMilliseconds, r.OverlayDir, r.Report.Summary());
            Console.WriteLine(File.ReadAllText(Path.Combine(AppPaths.Logs, "last-sd-export.txt")));
            return 0;
        }
        if (args[0] == "verify")
        {
            int bad = 0, files = 0;
            string root = args.Length > 2 ? args[1] : AppPaths.Overlay;
            string dlcRoot = args.Length > 2 ? args[2] : Path.Combine(AppPaths.Overlay, SavageWindInstall.OverlayDlcFolder.TrimEnd('/').Replace('/', '\\'));
            List<string> all = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList() : new List<string>();
            if (Directory.Exists(dlcRoot) && !AppPaths.IsInside(dlcRoot, root)) all.AddRange(Directory.EnumerateFiles(dlcRoot, "*", SearchOption.AllDirectories));
            foreach (string f in all)
            {
                bool inDlc = AppPaths.IsInside(f, dlcRoot);
                string rel = f.Substring((inDlc ? dlcRoot : root).TrimEnd('\\').Length + 1).Replace('\\', '/');
                if (rel.StartsWith(".")) continue;
                files++;
                VanillaIndex vi = v2;
                if (inDlc)
                {
                    if (d2 == null) { Console.WriteLine("BAD DLC file without the DLC set up: " + rel); bad++; continue; }
                    vi = d2;
                }
                bool known, folder;
                string c = vi.Canonicalize(rel, out known, out folder);
                if (!folder || c != rel) { Console.WriteLine("BAD path (folder missing or case differs): " + rel); bad++; }
                byte[] raw = File.ReadAllBytes(f);
                if (PackMerger.IsArchive(raw))
                {
                    byte[] d = Yaz0.DecompressIfNeeded(raw);
                    List<SarcEntry> es = Sarc.Parse(d);
                    for (int i = 1; i < es.Count; i++)
                        if (SarcWriter.NameHash(es[i - 1].Name) > SarcWriter.NameHash(es[i].Name)) { Console.WriteLine("BAD hash order in " + rel); bad++; break; }
                    if (known)
                    {
                        bad += Lost(rel, d, Yaz0.DecompressIfNeeded(File.ReadAllBytes(vi.FullPath(rel))));
                        if (rel == "Pack/TitleBG.pack")
                            Console.WriteLine("TitleBG.pack: {0} entries; has EventFlow/SignalFlowchart.bfevfl: {1}", es.Count, es.Any(e => e.Name == "EventFlow/SignalFlowchart.bfevfl"));
                    }
                }
            }
            string rstbPath = Path.Combine(root, SavageWindInstall.RstbRelPath.Replace('/', '\\'));
            if (File.Exists(rstbPath))
            {
                Rstb mine = Rstb.Load(rstbPath), game = Rstb.Load(Path.Combine(s2.RomfsDir, SavageWindInstall.RstbRelPath.Replace('/', '\\')));
                Console.WriteLine("overlay RSTB: {0} crc + {1} name entries, {2} differ from the game's", mine.CrcCount, mine.NameCount, mine.CountDifferences(game));
            }
            else Console.WriteLine("overlay RSTB: none (not needed)");
            Console.WriteLine("verify: {0} files, {1} problems", files, bad);
            return bad == 0 ? 0 : 2;
        }
        return 1;
    }

    // New / duplicate / rename / switch / delete, checked through a reload of config.json each time.
    private static int Profiles(ModLibrary lib)
    {
        int bad = 0;
        string start = lib.ProfileName;
        Action<bool, string> check = (ok, what) => { Console.WriteLine((ok ? "ok   " : "BAD  ") + what); if (!ok) bad++; };
        Func<ModLibrary> reload = () => ModLibrary.Load();
        Console.WriteLine("profiles [{0}], current {1}, mods [{2}]", string.Join(", ", lib.ProfileNames), start,
            string.Join(", ", lib.Mods.Select(m => m.Folder + (m.Enabled ? " (on)" : ""))));
        HashSet<string> onAtStart = new HashSet<string>(lib.Mods.Where(m => m.Enabled).Select(m => m.Folder));

        lib.CreateProfile("Test empty", false);
        ModLibrary l = reload();
        check(l.ProfileName == "Test empty", "new profile is the current one after a reload");
        check(l.Mods.Count == lib.Mods.Count && l.Mods.All(m => !m.Enabled), "new profile lists every installed mod, all off");
        if (l.Mods.Count > 0)
        {
            l.Mods[0].Enabled = true;
            l.Move(l.Mods[0], 1);
            l.Save();
        }
        l = reload();
        check(l.Mods.Count < 2 || l.Mods[1].Enabled && !l.Mods[0].Enabled, "on / off and order are kept per profile");

        l.SwitchProfile(start);
        l = reload();
        check(l.ProfileName == start && new HashSet<string>(l.Mods.Where(m => m.Enabled).Select(m => m.Folder)).SetEquals(onAtStart), "switching back restores the first profile untouched");

        l.CreateProfile("Test copy", true);
        l = reload();
        check(new HashSet<string>(l.Mods.Where(m => m.Enabled).Select(m => m.Folder)).SetEquals(onAtStart), "duplicate copies on / off");
        l.RenameProfile("Test renamed");
        l = reload();
        check(l.ProfileName == "Test renamed" && !l.ProfileExists("Test copy"), "rename");
        check(l.ProfileExists("TEST EMPTY"), "names compare without case");

        check(l.DeleteProfile(), "delete Test renamed");
        l = reload();
        l.SwitchProfile("Test empty");
        check(l.DeleteProfile(), "delete Test empty");
        l = reload();
        l.SwitchProfile(start);
        l = reload();
        check(l.ProfileNames.Count == 1 && l.ProfileName == start, "only the first profile is left, and current");
        check(!l.DeleteProfile(), "the last profile can't be deleted");
        Console.WriteLine("profiles: {0} problems", bad);
        return bad == 0 ? 0 : 2;
    }

    // Game files missing from a merged archive, at any depth (nested archives compared the same way).
    private static int Lost(string path, byte[] mine, byte[] game)
    {
        List<SarcEntry> me = Sarc.Parse(mine), ge = Sarc.Parse(game);
        Dictionary<string, SarcEntry> map = me.ToDictionary(e => e.Name);
        int bad = 0;
        List<string> lost = ge.Where(e => !map.ContainsKey(e.Name)).Select(e => e.Name).ToList();
        if (lost.Count > 0) { Console.WriteLine("BAD {0} lost game files: {1}", path, string.Join(", ", lost)); bad++; }
        foreach (SarcEntry g in ge)
        {
            SarcEntry m;
            if (!map.TryGetValue(g.Name, out m)) continue;
            byte[] mr = Sarc.Extract(mine, m), gr = Sarc.Extract(game, g);
            if (NativeMethods.BytesEqual(mr, gr) || !PackMerger.IsArchive(mr) || !PackMerger.IsArchive(gr)) continue;
            bad += Lost(path + "/" + g.Name, Yaz0.DecompressIfNeeded(mr), Yaz0.DecompressIfNeeded(gr));
        }
        return bad;
    }
}
