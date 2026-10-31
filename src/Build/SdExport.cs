using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianTerminal.Game;
using GuardianTerminal.Mods;

namespace GuardianTerminal.Build
{
    // "Export to SD card": the same merge, made for a modded Switch (Atmosphere's LayeredFS) instead of Savage Wind -
    // atmosphere/contents/01007EF00011E000/romfs (the game) and 01007EF00011F001/romfs (the DLC) on the card, after the
    // ones there are cleared (only those two folders: other games' mods stay). Made for the game version of the files it's
    // merged with, so the Switch must have the same (and the same DLC version).
    internal static class SdExport
    {
        // Drives that look like a Switch SD card (an atmosphere folder at the top), the likeliest first.
        public static List<string> FindCards()
        {
            List<string> cards = new List<string>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (Directory.Exists(Path.Combine(d.RootDirectory.FullName, "atmosphere"))) cards.Add(d.RootDirectory.FullName);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return cards;
        }

        // What an export replaces on that card (the folders that exist now)
        public static List<string> ExistingMods(string sdRoot)
        {
            BuildTarget t = BuildTarget.SdCard(sdRoot);
            return new[] { t.BaseDir, t.DlcDir }.Where(Directory.Exists).ToList();
        }

        public static BuildResult Export(string sdRoot, List<InstalledMod> enabled, SavageWindInstall sw, VanillaIndex van, VanillaIndex dlcVan, Action<string> progress)
        {
            BuildTarget t = BuildTarget.SdCard(sdRoot);
            progress(L.T("Clearing the Breath of the Wild mods on the SD card..."));
            foreach (string dir in new[] { t.BaseDir, t.DlcDir })
            {
                if (!Directory.Exists(dir)) continue;
                // (only atmosphere\contents\<BotW's title>\romfs itself)
                string expected = Path.GetFullPath(Path.Combine(sdRoot, "atmosphere", "contents"));
                if (!AppPaths.IsInside(dir, expected) || !Path.GetFileName(dir).Equals("romfs", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected folder: " + dir);
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            BuildResult r = OverlayBuilder.BuildInto(t, enabled, sw, van, dlcVan, progress);
            r.Report.Save(Path.Combine(AppPaths.Logs, "last-sd-export.txt"));
            r.OverlayDir = t.BaseDir;
            return r;
        }
    }
}
