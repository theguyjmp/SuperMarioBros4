# Builds snes\build\smb4.sfc (SNES LoROM FastROM) from snes\src\*.s plus the generated snes\build\gen\*.s.
# Usage: powershell -ExecutionPolicy Bypass -File snes\build.ps1 [-Tools <path to smb4tools.exe>] [-Out <dir>] [-NoExport]
#   -Tools   smb4tools.exe used for `snes-export` (default: bin\smb4tools.exe). Agents pass their private .agentbin copy.
#   -Out     output dir for objects + ROM (default snes\build). Agents use snes\build-<name> to avoid clobbering.
#   -NoExport  skip asset conversion (reuse <Out>\gen).
param([string]$Tools = '', [string]$Out = '', [switch]$NoExport)
$ErrorActionPreference = 'Stop'
if ($Tools -ne '') { $Tools = (Resolve-Path $Tools).Path }
$snes = $PSScriptRoot
$root = Split-Path $snes -Parent
if ($Tools -eq '') { $Tools = Join-Path $root 'bin\smb4tools.exe' }
if ($Out -eq '') { $Out = Join-Path $snes 'build' }
New-Item -ItemType Directory -Force $Out, "$Out\obj", "$Out\gen" | Out-Null
$ca65 = "$snes\tools\cc65\bin\ca65.exe"; $ld65 = "$snes\tools\cc65\bin\ld65.exe"
if (-not $NoExport) {
    Push-Location $root
    & $Tools snes-export "$Out\gen"
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) { Write-Host "EXPORT FAILED" -ForegroundColor Red; exit 1 }
}
$objs = @()
$srcs = @(Get-ChildItem "$snes\src" -Filter *.s) + @(Get-ChildItem "$Out\gen" -Filter *.s -ErrorAction SilentlyContinue)
foreach ($s in $srcs) {
    $o = "$Out\obj\" + $s.BaseName + '.o'
    if ($s.DirectoryName -ne "$snes\src") { $o = "$Out\obj\gen_" + $s.BaseName + '.o' }
    & $ca65 --cpu 65816 -I "$snes\src" -I "$Out\gen" --bin-include-dir "$Out\gen" -g $s.FullName -o $o
    if ($LASTEXITCODE -ne 0) { Write-Host "ASSEMBLY FAILED: $($s.Name)" -ForegroundColor Red; exit 1 }
    $objs += $o
}
$rom = "$Out\smb4.sfc"
& $ld65 -C "$snes\lorom.cfg" -m "$Out\smb4.map" --dbgfile "$Out\smb4.dbg" -o $rom @objs
if ($LASTEXITCODE -ne 0) { Write-Host "LINK FAILED" -ForegroundColor Red; exit 1 }
# fix the internal checksum (LoROM header at file offset $7FC0)
$b = [IO.File]::ReadAllBytes($rom)
$b[0x7FDC] = 0xFF; $b[0x7FDD] = 0xFF; $b[0x7FDE] = 0; $b[0x7FDF] = 0
$sum = 0; foreach ($x in $b) { $sum = ($sum + $x) -band 0xFFFF }
$b[0x7FDE] = $sum -band 0xFF; $b[0x7FDF] = ($sum -shr 8) -band 0xFF
$c = $sum -bxor 0xFFFF; $b[0x7FDC] = $c -band 0xFF; $b[0x7FDD] = ($c -shr 8) -band 0xFF
[IO.File]::WriteAllBytes($rom, $b)
Write-Host ("Built {0} ({1:N0} KB)" -f $rom, ($b.Length / 1KB)) -ForegroundColor Green
