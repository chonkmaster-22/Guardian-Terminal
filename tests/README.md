# tests

Command-line tools used to check the engine against real game files (they need a Savage Wind install with the
game files set up, and mods to test with). Each is built from its file plus `src\` (all but `Program.cs`):

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Get-ChildItem src -Recurse -Filter *.cs | ? Name -ne 'Program.cs' | % FullName
& $csc /nologo /unsafe /langversion:5 /out:EngineTest.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll tests\EngineTest.cs $src
```

- `EngineTest`: import, build (merge), verify, export, run from a folder of its own (like the app's).
- `MsbtProbe`: text files: `roundtrip` (every game text read and written back), `verify` (a merge kept each mod's texts).
- `MapProbe`, `BymlDiff`, `GameDataProbe`: map units, BYML files, game data flags.
- `PackPart`, `FormatProbe`, `WriterProbe`, `DlcKeyProbe`: packs and formats.
- `FakeGame`, `LaunchProbe`: a stand-in savagewind.exe and the loading screen, without the game.
