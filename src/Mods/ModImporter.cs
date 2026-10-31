using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Win32;
using GuardianTerminal.Build;
using GuardianTerminal.Formats;
using GuardianTerminal.Game;

namespace GuardianTerminal.Mods
{
    internal sealed class ImportException : Exception
    {
        public ImportException(string message) : base(message) { }
    }

    // What a mod's page says about it, for a mod installed from GameBanana
    internal sealed class ModOrigin
    {
        public string Name, Version, Description, Url, Author, FileDate;
        public GbMod Page;  // to save its preview picture as the mod's thumbnail
    }

    // Turns a mod download (.zip / .7z / .rar / .bnp, or a folder) into mods/<name>/: the base game's romfs in romfs/,
    // the DLC's (kept for when Savage Wind supports it) in dlc/romfs/, and mod.json describing what was found.
    internal static class ModImporter
    {
        // Bumped when Analyze learns something new: mods checked by an older one are checked again.
        public const int AnalyzerVersion = 6;

        public const string BaseTitle = "01007EF00011E000";
        public const string DlcTitle = "01007EF00011F001";
        private const string TotkPrefix = "0100F2C0115B";

        public static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar", ".bnp" };

        // Folders a romfs has at its top (any of them marks a folder that is a romfs without the name)
        private static readonly string[] RomfsTopFolders = {
            "Actor", "Effect", "Event", "Font", "Game", "Layout", "Map", "Model", "Movie", "NavMesh", "Pack",
            "Physics", "Sound", "System", "Terrain", "UI", "Voice", "Message", "Quest"
        };

        private static readonly string[] CorePackNames = { "Pack/Bootup.pack", "Pack/TitleBG.pack", "Pack/Title.pack" };

        // BCML's merge logs that only say what the package's own files are (used: rstb.json for the sizes); any other
        // log holds changes BCML applies by merging (gamedata.yml, texts, map edits...), which this manager can't
        private static readonly string[] BcmlFileLogs = { "rstb.json", "packs.json", "rstb.log", "packs.log" };

        // A download (archive or folder) with several versions of the game files in it (a mod's options) asks
        // chooseRoots which to install: it gets their paths in the download and returns the ones picked (each
        // becomes a mod of its own), or null / none to cancel. dlcFolder: where the mod's DLC files are when they're
        // apart from it (an emulator's DLC mod folder of the same name), else null. origin: what GameBanana says about
        // it, when it was installed from there (name, version, page), else null.
        public static List<InstalledMod> Import(string source, ModLibrary library, VanillaIndex van, VanillaIndex dlcVan,
            Func<List<string>, List<string>> chooseRoots, Action<string> progress, ModOrigin origin = null, string dlcFolder = null)
        {
            string work = null;
            try
            {
                bool fromFolder = Directory.Exists(source);
                string root;
                if (fromFolder) root = Path.GetFullPath(source).TrimEnd('\\');
                else
                {
                    if (!File.Exists(source)) throw new ImportException(L.F("The file isn't there anymore: {0}", source));
                    work = Path.Combine(AppPaths.Staging, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(work);
                    progress(L.F("Unpacking {0}...", Path.GetFileName(source)));
                    Extract(source, work, progress);
                    root = work;
                }

                progress(L.T("Looking for the mod's game files..."));
                List<string> bases, dlcs, romfsDirs;
                FindRomfs(root, out romfsDirs, out bases, out dlcs);
                // archives inside the download (a zip holding a 7z, or a version per file): unpacked, looked in again
                if (bases.Count == 0 && dlcs.Count == 0 && work != null && UnpackInner(root, progress) > 0)
                    FindRomfs(root, out romfsDirs, out bases, out dlcs);
                if (bases.Count == 0 && dlcs.Count == 0 && romfsDirs.Any(d => HasSegmentPrefix(d, TotkPrefix)))
                    throw new ImportException(L.T("This is a Tears of the Kingdom mod, not a Breath of the Wild one."));
                if (bases.Count == 0 && dlcs.Count == 0)
                {
                    string loose = FindLooseRomfs(root);
                    if (loose != null) bases.Add(loose);
                    else if (Directory.GetDirectories(root, "content", SearchOption.AllDirectories).Any())
                        throw new ImportException(L.T("This is a Wii U (Cemu) mod. Savage Wind runs the Switch version of the game, so it needs Switch mods (look for a romfs folder)."));
                    else
                        throw new ImportException(L.T("No game files were found in it (no romfs folder). It may not be a Breath of the Wild Switch mod."));
                }
                if (dlcFolder != null)
                {
                    string extra = Path.GetFileName(dlcFolder).Equals("romfs", StringComparison.OrdinalIgnoreCase) ? dlcFolder : Path.Combine(dlcFolder, "romfs");
                    if (Directory.Exists(extra)) dlcs.Add(extra);
                }

                List<string> picked;
                if (bases.Count <= 1) picked = bases.ToList();
                else
                {
                    List<string> rel = bases.Select(b => Relative(root, b)).ToList();
                    List<string> chosen = chooseRoots(rel);
                    if (chosen == null || chosen.Count == 0) return new List<InstalledMod>();
                    picked = chosen.Select(c => bases[rel.IndexOf(c)]).ToList();
                }
                if (picked.Count == 0) picked.Add(null);  // (DLC files only)

                List<InstalledMod> result = new List<InstalledMod>();
                foreach (string baseDir in picked)
                    result.Add(ImportOne(source, root, fromFolder, baseDir, dlcs, picked.Count > 1, library, van, dlcVan, progress, origin));
                return result;
            }
            finally
            {
                if (work != null)
                {
                    try { AppPaths.DeleteFolderInside(work, AppPaths.Root); }
                    catch (Exception) { }
                }
            }
        }

        private static void FindRomfs(string root, out List<string> romfsDirs, out List<string> bases, out List<string> dlcs)
        {
            romfsDirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Where(d => Path.GetFileName(d).Equals("romfs", StringComparison.OrdinalIgnoreCase)).ToList();
            if (Path.GetFileName(root).Equals("romfs", StringComparison.OrdinalIgnoreCase)) romfsDirs.Add(root);
            bases = romfsDirs.Where(d => !HasSegment(d, DlcTitle) && !HasSegmentPrefix(d, TotkPrefix)).ToList();
            dlcs = romfsDirs.Where(d => HasSegment(d, DlcTitle)).ToList();
        }

        // Archives in an unpacked download, each unpacked into a folder named after it; how many
        private static int UnpackInner(string root, Action<string> progress)
        {
            int n = 0;
            foreach (string inner in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => ArchiveExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList())
            {
                string into = Path.Combine(Path.GetDirectoryName(inner), Path.GetFileNameWithoutExtension(inner) + " (unpacked)");
                try
                {
                    Directory.CreateDirectory(into);
                    Extract(inner, into, progress);
                    n++;
                }
                catch (ImportException) { }
            }
            return n;
        }

        // One mod from one version of the game files in a download (baseDir, or null for DLC files only)
        private static InstalledMod ImportOne(string source, string root, bool fromFolder, string baseDir, List<string> dlcs, bool variant,
            ModLibrary library, VanillaIndex van, VanillaIndex dlcVan, Action<string> progress, ModOrigin origin)
        {
            {
                string dlcDir = dlcs.OrderByDescending(d => baseDir == null ? 0 : CommonPrefix(d, baseDir)).FirstOrDefault();
                string anchor = baseDir ?? dlcDir;

                // name, version, description: BCML's info.json if there is one, else the file / folder name
                Dictionary<string, object> meta = FindInfoJson(anchor, root);
                string name = Json.GetString(meta, "name");
                if (string.IsNullOrWhiteSpace(name) && !fromFolder) name = TopFolderName(root);
                if (string.IsNullOrWhiteSpace(name)) name = CleanName(fromFolder ? Path.GetFileName(root) : Path.GetFileNameWithoutExtension(source));

                ModInfo info = new ModInfo();
                info.Name = (origin != null && !string.IsNullOrWhiteSpace(origin.Name) ? origin.Name : name).Trim();
                // one of several versions in the download: named after the folder it's in ("Mod (60 FPS)")
                if (variant && baseDir != null)
                {
                    string label = VariantLabel(root, baseDir);
                    if (label != null && !info.Name.EndsWith("(" + label + ")", StringComparison.OrdinalIgnoreCase)) info.Name += " (" + label + ")";
                }
                info.Version = Json.GetString(meta, "version");
                if (origin != null && !string.IsNullOrWhiteSpace(origin.Version)) info.Version = origin.Version;
                info.Description = Json.GetString(meta, "desc") ?? Json.GetString(meta, "description");
                if (string.IsNullOrWhiteSpace(info.Description) && origin != null) info.Description = origin.Description;
                info.Source = Path.GetFileName(source.TrimEnd('\\'));
                info.SourceUrl = origin != null ? origin.Url : null;
                info.Author = origin != null && !string.IsNullOrWhiteSpace(origin.Author) ? origin.Author : Json.GetString(meta, "author");
                info.FileDate = origin != null ? origin.FileDate : null;
                info.Imported = DateTime.Now.ToString("yyyy-MM-dd");

                info.HasCodePatches = Directory.GetDirectories(root, "exefs", SearchOption.AllDirectories).Any()
                    || Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Any(f => f.EndsWith(".ips", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pchtxt", StringComparison.OrdinalIgnoreCase));
                // BCML's logs folder (a .bnp's, next to its title folders): its own sizes are kept, other logs noted
                string logs = Directory.GetDirectories(root, "logs", SearchOption.AllDirectories)
                    .FirstOrDefault(d => Directory.EnumerateFiles(d).Any(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
                info.IsBcmlPackage = logs != null;
                if (logs != null)
                    info.BcmlLogs = Directory.GetFiles(logs, "*", SearchOption.AllDirectories).Select(f => f.Substring(logs.Length + 1).Replace('\\', '/'))
                        .Where(n => !BcmlFileLogs.Contains(n, StringComparer.OrdinalIgnoreCase)).OrderBy(n => n).ToList();

                InstalledMod mod = new InstalledMod();
                mod.Folder = library.NewFolderName(info.Name);
                mod.Info = info;
                Directory.CreateDirectory(mod.Dir);
                progress(L.F("Copying {0}...", info.Name));
                // (a DLC folder that several versions share is copied, not moved, so each gets it)
                if (baseDir != null) Place(baseDir, mod.RomfsDir, !fromFolder);
                else Directory.CreateDirectory(mod.RomfsDir);
                if (dlcDir != null && Directory.Exists(dlcDir)) Place(dlcDir, Path.Combine(mod.Dir, "dlc", "romfs"), !fromFolder && !variant && AppPaths.IsInside(dlcDir, root));
                if (logs != null && File.Exists(Path.Combine(logs, "rstb.json"))) File.Copy(Path.Combine(logs, "rstb.json"), mod.BcmlSizesFile, true);

                if (origin != null && origin.Page != null)
                {
                    try { info.Thumbnail = GameBanana.SaveThumbnail(origin.Page, mod.Folder); }
                    catch (Exception) { }  // (a mod without its picture still installs)
                }

                progress(L.F("Checking {0} against your game files...", info.Name));
                Analyze(mod, van, dlcVan);
                mod.SaveInfo();
                mod.Enabled = info.Status == ModStatus.Ok || info.Status == ModStatus.Warning;
                return mod;
            }
        }

        // What tells one version from another: the nearest folder above its romfs that isn't a title id or a
        // generic name ("Options/60 FPS/01007EF00011E000/romfs" -> "60 FPS")
        private static string VariantLabel(string root, string baseDir)
        {
            string[] generic = { "romfs", "contents", "atmosphere", "options", "switch", "mods", BaseTitle.ToLowerInvariant(), DlcTitle.ToLowerInvariant() };
            for (string d = Path.GetDirectoryName(baseDir); d != null && d.Length > root.Length; d = Path.GetDirectoryName(d))
            {
                string n = Path.GetFileName(d);
                if (!generic.Contains(n.ToLowerInvariant()) && !n.StartsWith("0100", StringComparison.OrdinalIgnoreCase)) return n.Trim();
            }
            return null;
        }

        // Fills in the counts, the status and the notes. dlcVan: the DLC's own RomFS when Savage Wind has the DLC set
        // up (a mod's DLC files are then compared with it and load), else null (they're kept, unused).
        public static void Analyze(InstalledMod mod, VanillaIndex van, VanillaIndex dlcVan)
        {
            ModInfo info = mod.Info;
            // (a BCML package whose logs only list its own files and sizes loses nothing here)
            bool codePatches = info.HasCodePatches, bcmlLogs = info.IsBcmlPackage && (info.BcmlLogs == null || info.BcmlLogs.Count > 0);
            List<ModFile> files = ModFiles.Scan(mod.RomfsDir, van);
            info.AnalyzerVersion = AnalyzerVersion;
            info.AnalyzedWithDlc = dlcVan != null;
            // mods often ship untouched copies of game files: those don't count (and are left out when playing)
            info.SameAsGameFiles = files.Count(f => f.Kind == ModFileKind.Replace && SameAsGame(f, van));
            info.ReplacedFiles = files.Count(f => f.Kind == ModFileKind.Replace) - info.SameAsGameFiles;
            info.NewFiles = files.Count(f => f.Kind == ModFileKind.Add);
            info.UnloadableFiles = files.Count(f => f.Kind == ModFileKind.NoFolder);
            info.IgnoredFiles = files.Count(f => f.Kind == ModFileKind.Ignored || f.Kind == ModFileKind.Skipped);
            info.HasSizeTable = files.Any(f => f.Kind == ModFileKind.SizeTable);

            List<string> dlcFiles = Directory.Exists(mod.DlcRomfsDir) ? Directory.GetFiles(mod.DlcRomfsDir, "*", SearchOption.AllDirectories).ToList() : new List<string>();
            info.DlcFiles = dlcFiles.Count;
            info.DlcReplacedFiles = info.DlcNewFiles = 0;
            bool dlcWorld = dlcFiles.Any(f => f.Replace('\\', '/').IndexOf("/Map/MainField/", StringComparison.OrdinalIgnoreCase) >= 0);
            List<ModFile> dfiles = dlcVan != null && info.DlcFiles > 0 ? ModFiles.Scan(mod.DlcRomfsDir, dlcVan, true) : new List<ModFile>();
            if (dfiles.Count > 0)
            {
                int same = dfiles.Count(f => f.Kind == ModFileKind.Replace && SameAsGame(f, dlcVan));
                info.DlcReplacedFiles = dfiles.Count(f => f.Kind == ModFileKind.Replace) - same;
                info.DlcNewFiles = dfiles.Count(f => f.Kind == ModFileKind.Add);
                info.SameAsGameFiles += same;
                info.UnloadableFiles += dfiles.Count(f => f.Kind == ModFileKind.NoFolder);
                info.IgnoredFiles += dfiles.Count(f => f.Kind == ModFileKind.Ignored || f.Kind == ModFileKind.Skipped);
            }
            info.CorePacks = files.Where(f => f.Kind == ModFileKind.Replace && (CorePackNames.Contains(f.Rel) || f.Rel.StartsWith("Pack/Bootup_", StringComparison.Ordinal)))
                .Select(f => f.Rel.Substring(5)).ToList();
            bool wiiU = LooksWiiU(files.Concat(dfiles).ToList());
            bool mostlyDlc = dlcWorld || info.DlcFiles >= Math.Max(1, info.BaseUsableFiles) / 3;

            // (notes are kept as their English template and values, L.MakeNote, and shown in the player's language)
            info.Notes.Clear();
            if (wiiU)
                info.Notes.Add(L.MakeNote("Its files are in the Wii U format. Savage Wind runs the Switch version, so this mod won't work. Look for a Switch version of it."));
            if (info.DlcFiles > 0 && dlcVan != null)
                info.Notes.Add(dlcWorld
                    ? L.MakeNote("{0} of its files change the DLC, including its world map. Your DLC is set up in Savage Wind, so they load too.", info.DlcUsableFiles)
                    : L.MakeNote("{0} of its files change the DLC. Your DLC is set up in Savage Wind, so they load too.", info.DlcUsableFiles));
            else if (info.DlcFiles > 0 && mostlyDlc)
                info.Notes.Add(dlcWorld
                    ? L.MakeNote("Most of this mod is made for the DLC ({0} DLC files, including its world map changes). The DLC isn't set up in this Savage Wind, so only its {1} base-game files would load, which can cause bugs or crashes. Set up both DLC in Savage Wind (Alpha 4 or later) to use it; the DLC files are kept until then.", info.DlcFiles, info.BaseUsableFiles)
                    : L.MakeNote("Most of this mod is made for the DLC ({0} DLC files). The DLC isn't set up in this Savage Wind, so only its {1} base-game files would load, which can cause bugs or crashes. Set up both DLC in Savage Wind (Alpha 4 or later) to use it; the DLC files are kept until then.", info.DlcFiles, info.BaseUsableFiles));
            else if (info.DlcFiles > 0)
                info.Notes.Add(L.MakeNote("{0} DLC files are kept but not used: the DLC isn't set up in this Savage Wind.", info.DlcFiles));
            if (info.UnloadableFiles > 0)
                info.Notes.Add(L.MakeNote("{0} files are in folders the game doesn't have, and Savage Wind can only add files to folders that exist. They're left out.", info.UnloadableFiles));
            if (info.CorePacks.Count > 0)
                info.Notes.Add(L.MakeNote("Changes core game packs ({0}). They're merged into your own game files, so 1.9.0 content stays, but mods made for older game versions can still cause problems.", string.Join(", ", info.CorePacks)));
            string gameVersion = van.Has("System/Version.txt") ? File.ReadAllText(van.FullPath("System/Version.txt")).Trim() : null;
            List<string> locked = VersionLocked.AppliesTo(gameVersion) && files.Any(f => f.Rel == "Pack/Bootup.pack" && f.Kind == ModFileKind.Replace)
                ? VersionLocked.ChangedBy(Path.Combine(mod.RomfsDir, "Pack", "Bootup.pack"), van.FullPath("Pack/Bootup.pack")) : new List<string>();
            if (locked.Count > 0)
                info.Notes.Add(L.MakeNote("Made for an older game version: its {0} would crash your {1} game files, so your game's own are used instead. " +
                    "Everything else in it loads, but some of its sounds, effects or HUD changes may be missing.", string.Join(", ", locked), gameVersion ?? "1.9.0"));
            if (codePatches)
                info.Notes.Add(L.MakeNote("Includes code patches (exefs / .ips), which Savage Wind doesn't support. They're ignored."));
            if (bcmlLogs)
                info.Notes.Add(info.BcmlLogs != null && info.BcmlLogs.Count > 0
                    ? L.MakeNote("This BCML package keeps some of its changes in BCML's merge logs ({0}), which this manager can't apply yet: those parts won't show in the game. Its files themselves load.",
                        string.Join(", ", info.BcmlLogs.Take(6)) + (info.BcmlLogs.Count > 6 ? ", ..." : ""))
                    : L.MakeNote("This BCML package keeps some of its changes in BCML's merge logs, which this manager can't apply yet: those parts won't show in the game. Its files themselves load."));
            else if (info.IsBcmlPackage)
                info.Notes.Add(L.MakeNote("BCML package: everything in it loads (its own resource sizes are used too)."));
            if (info.SameAsGameFiles > 0)
                info.Notes.Add(L.MakeNote("{0} of its files are exactly the same as your game's, so they're skipped.", info.SameAsGameFiles));
            if (info.IgnoredFiles > 0)
                info.Notes.Add(L.MakeNote("{0} non-game files (readme, logs, Version.txt...) are ignored.", info.IgnoredFiles));

            if (wiiU) info.Status = ModStatus.WiiU;
            else if (info.DlcFiles > 0 && dlcVan == null && (info.BaseUsableFiles == 0 || mostlyDlc)) info.Status = ModStatus.NeedsDlc;
            else if (info.UsableFiles == 0) info.Status = ModStatus.Empty;
            else if (info.DlcFiles > 0 && dlcVan == null || info.UnloadableFiles > 0 || info.CorePacks.Count > 0 || codePatches || bcmlLogs) info.Status = ModStatus.Warning;
            else info.Status = ModStatus.Ok;
            if (info.Status == ModStatus.Empty)
                info.Notes.Insert(0, L.MakeNote("Nothing in it can be loaded by Savage Wind."));
        }

        private static bool SameAsGame(ModFile f, VanillaIndex van)
        {
            try { return FileCompare.Same(f.FullPath, van.FullPath(f.Rel)); }
            catch (IOException) { return false; }
        }

        // Switch files are little-endian: a SARC's byte-order mark reads FF FE, a BFRES has "FRES    ".
        private static bool LooksWiiU(List<ModFile> files)
        {
            int checkedFiles = 0, wiiu = 0;
            foreach (ModFile f in files)
            {
                if (checkedFiles >= 12) break;
                if (f.Kind != ModFileKind.Replace && f.Kind != ModFileKind.Add) continue;
                FileInfo fi = new FileInfo(f.FullPath);
                if (fi.Length < 32 || fi.Length > 64L << 20) continue;
                byte[] d;
                try { d = Yaz0.DecompressIfNeeded(File.ReadAllBytes(f.FullPath)); }
                catch (Exception) { continue; }
                if (Sarc.IsSarc(d)) { checkedFiles++; if (Sarc.IsBigEndian(d)) wiiu++; }
                else if (d.Length >= 8 && d[0] == 'F' && d[1] == 'R' && d[2] == 'E' && d[3] == 'S') { checkedFiles++; if (d[4] != ' ') wiiu++; }
            }
            return checkedFiles > 0 && wiiu * 2 > checkedFiles;
        }

        // Unpacks with 7-Zip when it's installed (zip, 7z, rar, bnp), else zips with Windows' own; first checks there's
        // room for it (big mods unpack to several GB), and shows how far it got.
        private static void Extract(string archive, string dest, Action<string> progress)
        {
            string name = Path.GetFileName(archive);
            string sevenZip = FindSevenZip();
            long unpacked = sevenZip != null ? SevenZipSize(sevenZip, archive) : ZipSize(archive);
            CheckRoom(dest, unpacked > 0 ? unpacked : new FileInfo(archive).Length * 3, name);
            if (sevenZip != null)
            {
                ProcessStartInfo psi = new ProcessStartInfo(sevenZip, "x -y -bso0 -bsp1 \"-o" + dest + "\" \"" + archive + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    System.Text.StringBuilder err = new System.Text.StringBuilder();
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                    p.BeginErrorReadLine();
                    // -bsp1: "  42% 1234 - Actor/..." progress lines, rewritten with backspaces
                    int last = -1;
                    char[] buffer = new char[256];
                    System.Text.StringBuilder line = new System.Text.StringBuilder();
                    int n;
                    while ((n = p.StandardOutput.Read(buffer, 0, buffer.Length)) > 0)
                        for (int i = 0; i < n; i++)
                        {
                            char c = buffer[i];
                            if (c == '\b' || c == '\r' || c == '\n') { line.Length = 0; continue; }
                            line.Append(c);
                            if (c != '%') continue;
                            int pct;
                            if (int.TryParse(line.ToString().Trim().TrimEnd('%'), out pct) && pct != last)
                            {
                                last = pct;
                                progress(L.F("Unpacking {0}... {1}%", name, pct));
                            }
                        }
                    p.WaitForExit();
                    if (p.ExitCode > 1)  // 1 = warnings
                    {
                        string e = err.ToString().Trim();
                        throw new ImportException(L.F("7-Zip couldn't unpack {0}: {1}", name, e.Length > 0 ? e : L.F("exit code {0}", p.ExitCode)));
                    }
                }
                return;
            }
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || archive.EndsWith(".bnp", StringComparison.OrdinalIgnoreCase) && ZipSize(archive) > 0)
            {
                try
                {
                    using (ZipArchive zip = ZipFile.OpenRead(archive))
                    {
                        int done = 0, total = zip.Entries.Count;
                        string root = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
                        foreach (ZipArchiveEntry e in zip.Entries)
                        {
                            string to = Path.GetFullPath(Path.Combine(dest, e.FullName));
                            if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;  // (no paths out of the folder)
                            if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) Directory.CreateDirectory(to);
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(to));
                                e.ExtractToFile(to, true);
                            }
                            if (++done % 50 == 0) progress(L.F("Unpacking {0}... {1}%", name, done * 100 / Math.Max(1, total)));
                        }
                    }
                }
                catch (InvalidDataException e) { throw new ImportException(L.F("The zip file couldn't be read: {0}", e.Message)); }
                return;
            }
            // without 7-Zip: Windows' own tar (bsdtar, Windows 10 1803 and later) reads 7z and rar too
            string tar = WindowsTar();
            if (tar != null)
            {
                ProcessStartInfo psi = new ProcessStartInfo(tar, "-xvf \"" + archive + "\" -C \"" + dest + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    System.Text.StringBuilder err = new System.Text.StringBuilder();
                    int files = 0;
                    p.OutputDataReceived += (s, e) => { };
                    p.BeginOutputReadLine();
                    string line;
                    // -v: "x path" for each file, on stderr
                    while ((line = p.StandardError.ReadLine()) != null)
                    {
                        if (line.StartsWith("x ", StringComparison.Ordinal)) { if (++files % 40 == 0) progress(L.F("Unpacking {0}... {1} files", name, files)); }
                        else if (err.Length < 2000) err.AppendLine(line);
                    }
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                        throw new ImportException(L.F("{0} couldn't be unpacked ({1}). Installing 7-Zip (free, 7-zip.org) may help.", name, err.ToString().Trim().Length > 0 ? err.ToString().Trim() : L.F("exit code {0}", p.ExitCode)));
                }
                return;
            }
            throw new ImportException(L.F("Unpacking {0} files needs 7-Zip (free, 7-zip.org). Install it, or unpack the mod yourself and use Add mod folder.", Path.GetExtension(archive)));
        }

        private static string WindowsTar()
        {
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            return File.Exists(tar) ? tar : null;
        }

        // The archive's unpacked size from 7-Zip's listing (its last line: "<date> <time> <size> <packed> N files"), or 0
        private static long SevenZipSize(string sevenZip, string archive)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(sevenZip, "l \"" + archive + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (Process p = Process.Start(psi))
                {
                    string last = null, line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                        if (line.Trim().Length > 0) last = line;
                    p.WaitForExit();
                    if (last == null) return 0;
                    foreach (string part in last.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        long v;
                        if (part.Length > 3 && long.TryParse(part, out v)) return v;
                    }
                }
            }
            catch (Exception) { }
            return 0;
        }

        private static long ZipSize(string archive)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(archive))
                    return zip.Entries.Sum(e => e.Length);
            }
            catch (Exception) { return 0; }
        }

        // Enough free space for the unpacked files and the copy kept in mods\ (on the same drive the copy is a move)
        private static void CheckRoom(string dest, long needed, string name)
        {
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dest)));
                long margin = 512L << 20;
                if (drive.AvailableFreeSpace < needed + margin)
                    throw new ImportException(L.F("There isn't enough free space on {0} to unpack {1}: it needs about {2:N1} GB and {3:N1} GB is free. Free up some space and try again.",
                        drive.Name, name, (needed + margin) / 1073741824.0, drive.AvailableFreeSpace / 1073741824.0));
            }
            catch (ArgumentException) { }
            catch (IOException) { }
        }

        // (tests: unpack as a PC without 7-Zip does)
        internal static bool IgnoreSevenZip;

        public static string FindSevenZip()
        {
            if (IgnoreSevenZip) return null;
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\7-Zip"))
                    {
                        string p = k == null ? null : k.GetValue("Path") as string;
                        if (p != null && File.Exists(Path.Combine(p, "7z.exe"))) return Path.Combine(p, "7z.exe");
                    }
                }
                catch (Exception) { }
            }
            foreach (string p in new[] { @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" })
                if (File.Exists(p)) return p;
            return null;
        }

        // Moves (same drive, from our own staging folder) or copies (the user's own folder) a romfs into place.
        private static void Place(string from, string to, bool move)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (move && string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(from, to);
                return;
            }
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length + 1)));
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(to, f.Substring(from.Length + 1)), true);
        }

        // A folder laid out like a romfs without being called one (Actor/, Model/, Pack/ ... at its top), shallowest first
        private static string FindLooseRomfs(string root)
        {
            List<string> all = new List<string> { root };
            all.AddRange(Directory.GetDirectories(root, "*", SearchOption.AllDirectories));
            foreach (string d in all.OrderBy(x => x.Length))
            {
                if (HasSegment(d, "content") || HasSegment(d, "aoc")) continue;
                if (Directory.GetDirectories(d).Any(s => RomfsTopFolders.Contains(Path.GetFileName(s), StringComparer.OrdinalIgnoreCase))) return d;
            }
            return null;
        }

        private static Dictionary<string, object> FindInfoJson(string from, string root)
        {
            for (string d = from; d != null && d.Length >= root.Length; d = Path.GetDirectoryName(d))
            {
                Dictionary<string, object> o = Json.ReadObject(Path.Combine(d, "info.json"));
                if (o != null) return o;
            }
            return null;
        }

        // An archive holding a single folder is usually named by it ("Link Black hair & Brown eyes BOTW SWITCH/...")
        private static string TopFolderName(string root)
        {
            string[] dirs = Directory.GetDirectories(root), files = Directory.GetFiles(root);
            if (dirs.Length != 1 || files.Length > 0) return null;
            string n = Path.GetFileName(dirs[0]);
            if (n.Equals("romfs", StringComparison.OrdinalIgnoreCase) || n.Equals("atmosphere", StringComparison.OrdinalIgnoreCase)
                || n.Equals("contents", StringComparison.OrdinalIgnoreCase) || n.StartsWith("0100", StringComparison.OrdinalIgnoreCase)
                || RomfsTopFolders.Contains(n, StringComparer.OrdinalIgnoreCase)) return null;
            return n.Trim();
        }

        private static string CleanName(string s)
        {
            s = s.Replace('_', ' ').Trim();
            return s.Length == 0 ? "Mod" : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static bool HasSegment(string path, string segment)
        {
            return path.Split('\\', '/').Any(p => p.Equals(segment, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasSegmentPrefix(string path, string prefix)
        {
            return path.Split('\\', '/').Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static string Relative(string root, string path)
        {
            return path.Length > root.Length ? path.Substring(root.Length + 1) : ".";
        }

        private static int CommonPrefix(string a, string b)
        {
            int n = 0;
            while (n < a.Length && n < b.Length && char.ToLowerInvariant(a[n]) == char.ToLowerInvariant(b[n])) n++;
            return n;
        }
    }
}
