# Makes the release download players get from GitHub: dist\GuardianTerminal-v<version>-win-x64.zip, holding the
# program, a quick-start README.txt and the license, and nothing else (no mods, no settings, no game files), with
# its SHA-256 checksum next to it.
#   powershell -ExecutionPolicy Bypass -File package.ps1 [-Version 0.4.0]
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Version) {
    $info = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
    $Version = ([regex]::Match($info, 'AssemblyInformationalVersion\("([^"]+)"\)')).Groups[1].Value
}
if (-not $Version) { throw 'No version: pass -Version or set AssemblyInformationalVersion in src\AssemblyInfo.cs' }

& (Join-Path $root 'build.ps1') | Out-Host

$dist = Join-Path $root 'dist'
$name = "GuardianTerminal-v$Version"
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $root 'bin\GuardianTerminal.exe') $stage
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'docs\README-release.txt') (Join-Path $stage 'README.txt')

$zip = Join-Path $dist "$name-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# (the zip holds one folder, GuardianTerminal-v<version>, so extracting it makes a folder of its own; its paths use
# '/', as zips should: .NET Framework's CreateFromDirectory writes '\', which some unzip tools get wrong)
$stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem $stage -File) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, "$name/" + $f.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$archive.Dispose()
$stream.Dispose()
Remove-Item $stage -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding Ascii
Write-Output "Made $zip"
Write-Output "SHA-256: $hash"
