using System;
using System.Collections.Generic;
using System.IO;

namespace GuardianTerminal.Mods
{
    // The mods installed from GameBanana, looked up there again: a file newer than the one installed means an update
    // (shown on the mod; installing it is the player's choice), and a mod without its picture gets it.
    internal static class ModUpdates
    {
        // How many have an update
        public static int Check(List<InstalledMod> mods, Action<string> progress)
        {
            int updates = 0;
            foreach (InstalledMod m in mods)
            {
                long id = GameBanana.IdOf(m.Info.SourceUrl);
                if (id == 0) continue;
                progress(L.F("Checking {0} on GameBanana...", m.DisplayName));
                GbMod page;
                try { page = GameBanana.GetMod(id); }
                catch (Exception) { continue; }
                bool changed = false;
                DateTime have;
                string mine = m.Info.FileDate ?? m.Info.Imported;
                string newer = null;
                if (DateTime.TryParse(mine, out have) && page.NewestFile > have.AddMinutes(1)) newer = page.NewestFile.ToString("yyyy-MM-dd HH:mm");
                if (newer != m.Info.NewerFileDate) { m.Info.NewerFileDate = newer; changed = true; }
                if (newer != null) updates++;
                if (string.IsNullOrEmpty(m.Info.Thumbnail) || !File.Exists(Path.Combine(AppPaths.Thumbnails, m.Info.Thumbnail)))
                {
                    try
                    {
                        string thumb = GameBanana.SaveThumbnail(page, m.Folder);
                        if (thumb != null) { m.Info.Thumbnail = thumb; changed = true; }
                    }
                    catch (Exception) { }
                }
                if (string.IsNullOrEmpty(m.Info.Author) && !string.IsNullOrEmpty(page.Submitter)) { m.Info.Author = page.Submitter; changed = true; }
                if (changed) m.SaveInfo();
            }
            return updates;
        }
    }
}
