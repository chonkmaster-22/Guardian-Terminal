using System.Collections.Generic;

namespace GuardianTerminal.Mods
{
    public static class ModStatus
    {
        public const string Ok = "ok";
        public const string Warning = "warning";
        public const string NeedsDlc = "needs-dlc";
        public const string WiiU = "wii-u";
        public const string Empty = "empty";
    }

    // mod.json in each mod's folder: what the mod is and what the import found in it.
    public sealed class ModInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public string SourceUrl { get; set; }       // its GameBanana page, when installed from there
        public string Author { get; set; }
        public string Thumbnail { get; set; }       // a picture of it (its GameBanana preview), in thumbnails\
        public string FileDate { get; set; }        // when the GameBanana file it came from was uploaded (yyyy-MM-dd HH:mm)
        public string NewerFileDate { get; set; }   // a newer file on its GameBanana page, found by the update check
        public List<string> BcmlLogs { get; set; }  // a BCML package's merge logs this manager can't apply (gamedata.yml...)
        public string Imported { get; set; }
        public string Status { get; set; }
        public int ReplacedFiles { get; set; }
        public int NewFiles { get; set; }
        public int UnloadableFiles { get; set; }
        public int IgnoredFiles { get; set; }
        public int DlcFiles { get; set; }
        public int DlcReplacedFiles { get; set; }  // (counted only when Savage Wind has the DLC set up)
        public int DlcNewFiles { get; set; }
        public bool AnalyzedWithDlc { get; set; }
        public int SameAsGameFiles { get; set; }
        public bool HasSizeTable { get; set; }
        public bool HasCodePatches { get; set; }
        public bool IsBcmlPackage { get; set; }
        public int AnalyzerVersion { get; set; }
        public List<string> CorePacks { get; set; }
        public List<string> Notes { get; set; }

        public ModInfo()
        {
            CorePacks = new List<string>();
            Notes = new List<string>();
            BcmlLogs = new List<string>();
        }

        public int BaseUsableFiles { get { return ReplacedFiles + NewFiles; } }
        public int DlcUsableFiles { get { return DlcReplacedFiles + DlcNewFiles; } }
        public int UsableFiles { get { return BaseUsableFiles + DlcUsableFiles; } }
    }
}
