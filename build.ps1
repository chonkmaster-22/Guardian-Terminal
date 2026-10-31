# Builds GuardianTerminal.exe with the C# compiler that ships with Windows (.NET Framework 4.8): nothing to install.
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Out <folder>]
# The emblem: assets\emblem.png when it's there (built into the exe, and the app icon is made from it), else the
# drawn Guardian of src\UI\Emblem.cs.
param([string]$Out = (Join-Path $PSScriptRoot 'bin'))

$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "The .NET Framework 4.x C# compiler wasn't found at $csc" }
New-Item -ItemType Directory -Force $Out | Out-Null
$obj = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null

$emblem = Join-Path $PSScriptRoot 'assets\emblem.png'
$haveEmblem = Test-Path $emblem

# the icon, made by tools\MakeIcon.cs (again whenever what it's made from changed)
$icon = Join-Path $obj 'app.ico'
$inputs = @((Join-Path $PSScriptRoot 'tools\MakeIcon.cs'), (Join-Path $PSScriptRoot 'src\UI\Emblem.cs'), (Join-Path $PSScriptRoot 'src\UI\Brand.cs'))
$watch = $inputs
if ($haveEmblem) { $watch = $inputs + $emblem }
$stale = -not (Test-Path $icon)
if (-not $stale) { $stale = @($watch | Where-Object { (Get-Item $_).LastWriteTimeUtc -gt (Get-Item $icon).LastWriteTimeUtc }).Count -gt 0 }
if ($stale) {
    $mk = Join-Path $obj 'MakeIcon.exe'
    & $csc /nologo /target:exe "/out:$mk" /r:System.Drawing.dll $inputs
    if ($LASTEXITCODE -ne 0) { throw 'MakeIcon failed to build' }
    $from = 'none'  # (an empty argument would be dropped on the way)
    if ($haveEmblem) { $from = $emblem }
    & $mk $icon $from (Join-Path $obj 'app-icon.png')
    if ($LASTEXITCODE -ne 0) { throw 'MakeIcon failed' }
}

$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.cs | ForEach-Object FullName
$exe = Join-Path $Out 'GuardianTerminal.exe'
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
$resources = @()
if ($haveEmblem) { $resources += "/resource:$emblem,GuardianTerminal.emblem.png" }
& $csc /nologo /optimize+ /unsafe /target:winexe /platform:anycpu /langversion:5 `
    "/out:$exe" "/win32icon:$icon" "/win32manifest:$manifest" @resources `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built $exe"
