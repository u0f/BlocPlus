# Builds dist\BlocPlus-<version>.msix for the Microsoft Store (the Store signs it on upload).
# Needs makeappx.exe from the Windows SDK, or from the Microsoft.Windows.SDK.BuildTools NuGet
# package unzipped into packaging\tools.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
cmd /c .\build.cmd
if ($LASTEXITCODE) { exit 1 }

$makeappx = Get-ChildItem "packaging\tools", "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } | Sort-Object FullName | Select-Object -Last 1
if (!$makeappx) { throw 'makeappx.exe not found: install the Windows SDK or unzip Microsoft.Windows.SDK.BuildTools into packaging\tools' }

$version = (Get-Item dist\BlocPlus.exe).VersionInfo.FileVersion  # x.y.z.0, as the Store requires
$stage = 'packaging\stage'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item "$stage\Assets" -ItemType Directory | Out-Null
Copy-Item dist\BlocPlus.exe $stage

# Tile and Start menu logos, drawn from assets\logo.png.
Add-Type -AssemblyName System.Drawing
$logo = [Drawing.Image]::FromFile("$PWD\assets\logo.png")
foreach ($a in @(@('StoreLogo', 50, 50, 50), @('Square44x44Logo', 44, 44, 44), @('Square150x150Logo', 150, 150, 120), @('Wide310x150Logo', 310, 150, 120))) {
    $bmp = New-Object Drawing.Bitmap $a[1], $a[2]
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($logo, [int](($a[1] - $a[3]) / 2), [int](($a[2] - $a[3]) / 2), $a[3], $a[3])
    $g.Dispose()
    $bmp.Save("$PWD\$stage\Assets\$($a[0]).png")
    $bmp.Dispose()
}
$logo.Dispose()

# Languages: Spanish plus one per lang\*.txt. File types: the ones the GitHub installer registers.
$langs = @('es') + (Get-ChildItem lang\*.txt | Where-Object Name -NotLike '_*' | ForEach-Object BaseName)
$exts = [regex]::Match((Get-Content BlocPlus.cs -Raw), 'string\[\] Exts = \("([^"]+)" \+\s*"([^"]+)"\)')
$exts = ($exts.Groups[1].Value + $exts.Groups[2].Value).Split(' ', [StringSplitOptions]::RemoveEmptyEntries) |
    Where-Object { $_ -notin 'bat', 'cmd' }  # packages may not claim these
(Get-Content packaging\AppxManifest.xml -Raw).
    Replace('{VERSION}', $version).
    Replace('{LANGUAGES}', (($langs | ForEach-Object { "    <Resource Language=`"$_`" />" }) -join "`r`n")).
    Replace('{FILETYPES}', (($exts | ForEach-Object { "              <uap:FileType>.$_</uap:FileType>" }) -join "`r`n")) |
    Set-Content "$stage\AppxManifest.xml" -Encoding UTF8

$out = "dist\BlocPlus-$version.msix"
& $makeappx.FullName pack /d $stage /p $out /o
if ($LASTEXITCODE) { exit 1 }
Write-Host "Built $out"
