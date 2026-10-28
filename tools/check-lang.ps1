# Checks the translations: every UI string in src\*.cs (each Tr.S("...")) must be in every lang\<culture>.txt.
# Lists what is missing and what is no longer used, and exits 1 when something is missing.
# -Update also rewrites lang\_source.txt, the list of strings translators work from: new ones at the end.
param([switch]$Update)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)

function New-StringSet([string[]]$items) { $s = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal); foreach ($i in $items) { [void]$s.Add($i) }; ,$s }

$src = (Get-ChildItem src\*.cs | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
$used = [regex]::Matches($src, 'Tr\.S\("((?:\\.|[^"\\])*)"') | ForEach-Object { $_.Groups[1].Value.Replace('\"', '"') } | Select-Object -Unique
$usedSet = New-StringSet $used

if ($Update) {
    $old = [IO.File]::ReadAllLines("$PWD\lang\_source.txt") | Where-Object { $usedSet.Contains($_) }
    $oldSet = New-StringSet $old
    $new = $used | Where-Object { -not $oldSet.Contains($_) }
    [IO.File]::WriteAllLines("$PWD\lang\_source.txt", [string[]](@($old) + @($new)), (New-Object Text.UTF8Encoding $false))
    if ($new) { Write-Host "Added to lang\_source.txt:"; $new | ForEach-Object { Write-Host "  $_" } }
}

$bad = $false
foreach ($f in Get-ChildItem lang\*.txt | Where-Object Name -NotLike '_*') {
    $have = [IO.File]::ReadAllLines($f.FullName) | Where-Object { $_.Contains("`t") } | ForEach-Object { $_.Split("`t")[0] }
    $haveSet = New-StringSet $have
    $missing = $used | Where-Object { -not $haveSet.Contains($_) }
    $unused = $have | Where-Object { -not $usedSet.Contains($_) }
    if ($missing) { $bad = $true; Write-Host "$($f.BaseName): $(@($missing).Count) missing"; $missing | ForEach-Object { Write-Host "    $_" } }
    if ($unused) { Write-Host "$($f.BaseName): $(@($unused).Count) unused"; $unused | ForEach-Object { Write-Host "    $_" } }
}
if ($bad) { exit 1 }
Write-Host "All $(@($used).Count) strings are translated in every language."
