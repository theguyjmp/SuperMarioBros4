# Standalone backgrounds test ROM (backgrounds agent): snes\src\main.s + bg.s + bgtest_game.s (cycles through every
# theme variant, 120 frames each: A = floor camera x 0, B = 40 px up at x 300) + stubs for sprites/sound.
# Usage: powershell -ExecutionPolicy Bypass -File snes\bgtest\build.ps1 -Tools <smb4tools.exe> -Out <dir>
#   then snes\tools\snesrun.ps1 -Rom <dir>\bgtest.sfc -Lua snes\bgtest\shots.lua -OutDir <dir>\shots
#   Set SMB4_BGPREVIEW=<dir> before the export to also get the converter's simulation of the same frames
#   (<variant>_A.png / _B.png): the ROM screenshots must match them pixel for pixel (except the test sprite).
param([Parameter(Mandatory)][string]$Tools, [Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = 'Stop'
$t = $PSScriptRoot; $snes = Split-Path $t -Parent; $root = Split-Path $snes -Parent
$Tools = (Resolve-Path $Tools).Path
New-Item -ItemType Directory -Force $Out, "$Out\gen", "$Out\obj" | Out-Null
$Out = (Resolve-Path $Out).Path
Push-Location $root; & $Tools snes-export "$Out\gen" | Select-String "  bg |backgrounds"; Pop-Location
$ca65 = "$snes\tools\cc65\bin\ca65.exe"; $ld65 = "$snes\tools\cc65\bin\ld65.exe"
$objs = @()
foreach ($f in @("$snes\src\main.s", "$snes\src\bg.s", "$t\bgtest_game.s", "$t\bgtest_stubs.s", "$Out\gen\bg_themes.s")) {
  $o = "$Out\obj\" + [IO.Path]::GetFileNameWithoutExtension($f) + '.o'
  & $ca65 --cpu 65816 -I "$snes\src" -I "$Out\gen" --bin-include-dir "$Out\gen" -g $f -o $o
  if ($LASTEXITCODE -ne 0) { Write-Host "ASSEMBLY FAILED: $f" -ForegroundColor Red; exit 1 }
  $objs += $o
}
& $ld65 -C "$snes\lorom.cfg" -m "$Out\bgtest.map" --dbgfile "$Out\bgtest.dbg" -o "$Out\bgtest.sfc" @objs
if ($LASTEXITCODE -ne 0) { Write-Host "LINK FAILED" -ForegroundColor Red; exit 1 }
Write-Host "Built $Out\bgtest.sfc" -ForegroundColor Green
