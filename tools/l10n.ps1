# The English texts Guardian Terminal shows (the keys of the translation tables), taken from the source:
# L.T("..."), L.F("...", ...), L.P(n, "...", "...", ...) and L.MakeNote("...", ...).
#   powershell -ExecutionPolicy Bypass -File tools\l10n.ps1            writes src\Lang\Keys.cs (new texts go at the end)
#   powershell -ExecutionPolicy Bypass -File tools\l10n.ps1 -Check     only says what's new, unused or mistranslated
# Each language (src\Lang\<Language>.cs) has one translation per key, in the same order. A translation whose {0}, {1}...
# don't match its English is left out (English is shown instead), and the check lists it.
param([switch]$Check)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$utf8 = New-Object System.Text.UTF8Encoding($false)
$lit = '"(?:[^"\\]|\\.)*"'
$joined = $lit + '(?:\s*\+\s*' + $lit + ')*'

function Inner([string]$s) {
    # "a" + "b" (as written in the source, escapes kept) -> a b
    ([regex]::Matches($s, $lit) | ForEach-Object { $_.Value.Substring(1, $_.Value.Length - 2) }) -join ''
}

$found = New-Object System.Collections.Generic.List[string]
$seen = @{}
function Add([string]$k) { if (-not $seen.ContainsKey($k)) { $seen[$k] = $true; $found.Add($k) } }

$files = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\Lang\\' } | Sort-Object FullName
foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName, $utf8)
    foreach ($m in [regex]::Matches($text, 'L\.(?:T|F|MakeNote)\(\s*(' + $joined + ')')) { Add (Inner $m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($text, 'L\.P\([^";]*?,\s*(' + $joined + ')\s*,\s*(' + $joined + ')')) { Add (Inner $m.Groups[1].Value); Add (Inner $m.Groups[2].Value) }
}
# values that reach the screen inside a note (VersionLocked's names, in "Made for an older game version: its {0}...")
foreach ($k in @('sound links', 'effect links', 'HUD layout')) { Add $k }

# the keys so far, in their order (a language's translations line up with them)
$keysFile = Join-Path $root 'src\Lang\Keys.cs'
$old = New-Object System.Collections.Generic.List[string]
if (Test-Path $keysFile) {
    foreach ($m in [regex]::Matches([IO.File]::ReadAllText($keysFile, $utf8), '(?m)^\s*(' + $lit + '),')) { $old.Add((Inner $m.Groups[1].Value)) }
}
$new = @($found | Where-Object { -not $old.Contains($_) })
$unused = @($old | Where-Object { -not $seen.ContainsKey($_) })
"{0} texts in the source; {1} new, {2} no longer used" -f $found.Count, $new.Count, $unused.Count
foreach ($k in $new) { "  new: $k" }
foreach ($k in $unused) { "  unused: $k" }

$all = @($old) + $new
if (-not $Check) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('namespace GuardianTerminal.Lang')
    [void]$sb.AppendLine('{')
    [void]$sb.AppendLine('    // Every English text Guardian Terminal shows, made by tools\l10n.ps1 from the source (new ones at the end, so')
    [void]$sb.AppendLine('    // the translations, one per line in this order, stay lined up). Don''t edit by hand.')
    [void]$sb.AppendLine('    internal static class Keys')
    [void]$sb.AppendLine('    {')
    [void]$sb.AppendLine('        public static readonly string[] All =')
    [void]$sb.AppendLine('        {')
    $i = 0
    foreach ($k in $all) { [void]$sb.AppendLine(('            "{0}",  // {1}' -f $k, $i)); $i++ }
    [void]$sb.AppendLine('        };')
    [void]$sb.AppendLine('    }')
    [void]$sb.AppendLine('}')
    [IO.File]::WriteAllText($keysFile, $sb.ToString(), $utf8)
    "Wrote $keysFile ($($all.Count) keys)"
}

# each language: how many lines, and the ones whose placeholders don't match
function Holes([string]$s) { (([regex]::Matches($s, '\{(\d+)(?:[:,][^}]*)?\}') | ForEach-Object { $_.Groups[1].Value }) | Sort-Object -Unique) -join ',' }
foreach ($lang in Get-ChildItem (Join-Path $root 'src\Lang') -Filter *.cs | Where-Object { $_.Name -ne 'Keys.cs' }) {
    $lines = @([regex]::Matches([IO.File]::ReadAllText($lang.FullName, $utf8), '(?m)^\s*(' + $lit + '|null),[ \t]*(?://[ \t]*(\d+))?'))
    $t = @($lines | ForEach-Object { $_.Groups[1].Value })
    $bad = 0
    for ($i = 0; $i -lt [Math]::Min($t.Count, $all.Count); $i++) {
        # (a line marked // n must be the n-th)
        if ($lines[$i].Groups[2].Success -and [int]$lines[$i].Groups[2].Value -ne $i) { "  {0}: the line marked {1} is number {2}: lines are out of order from there" -f $lang.BaseName, $lines[$i].Groups[2].Value, $i; $bad++; break }
        if ($t[$i] -eq 'null') { continue }
        $tr = Inner $t[$i]
        if ((Holes $tr) -ne (Holes $all[$i])) { "  {0} #{1}: {{}} don't match: {2}  <>  {3}" -f $lang.BaseName, $i, $all[$i], $tr; $bad++ }
    }
    "{0}: {1} of {2} translated{3}" -f $lang.BaseName, [Math]::Min($t.Count, $all.Count), $all.Count, $(if ($t.Count -gt $all.Count) { " (MORE LINES THAN KEYS: they're out of line)" } elseif ($bad -gt 0) { ", $bad with wrong placeholders" } else { '' })
}
