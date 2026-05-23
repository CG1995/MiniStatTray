$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\MiniStatTray.cs"
$dist = Join-Path $root "dist"
$out = Join-Path $dist "MiniStatTray.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Path $dist -Force | Out-Null

& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    /out:$out `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $src

Write-Host "Built $out"
