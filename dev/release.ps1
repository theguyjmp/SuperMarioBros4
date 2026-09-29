# Builds everything and puts the player-facing files at the repository's top level:
#   SuperMarioBros4.sfc  (the SNES ROM)   and   SuperMarioBros4.exe  (Windows player with ROM + emulator core embedded)
# Usage: powershell -ExecutionPolicy Bypass -File dev\release.ps1 [-NoTests]
# First time on a new PC: dev\snes\tools\setup.ps1 (assembler + test emulator) and dev\tools\fetch-core.ps1 (bsnes core).
param([switch]$NoTests)
$ErrorActionPreference = 'Stop'
$dev = $PSScriptRoot; $top = Split-Path $dev -Parent
Set-Location $dev
function Run($what, $file, [string[]]$a = @()) {
    Write-Host "== $what" -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File $file @a
    if ($LASTEXITCODE -ne 0) { throw "$what failed" }
}
Run 'tools'   "$dev\build.ps1" @('-ToolsOnly')
Run 'ROM'     "$dev\snes\build.ps1"
if (-not $NoTests) {
    Run 'ROM QA (every level boots and runs)' "$dev\snes\test\qa-rom.ps1" @('-NoShots')
    Run 'physics parity'                    "$dev\snes\test\parity-suite.ps1"
}
Run 'player exe' "$dev\build.ps1" @('-Rom', "$dev\snes\build\smb4.sfc")
& "$dev\bin\SuperMarioBros4.exe" --selftest --out "$dev\bin\selftest" | Select-String 'SELFTEST'
Copy-Item "$dev\snes\build\smb4.sfc" "$top\SuperMarioBros4.sfc" -Force
Copy-Item "$dev\bin\SuperMarioBros4.exe" "$top\SuperMarioBros4.exe" -Force
Write-Host "Release files updated: $top\SuperMarioBros4.exe, $top\SuperMarioBros4.sfc" -ForegroundColor Green
