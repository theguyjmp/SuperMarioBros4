# Builds the game exe (a libretro SNES player with the ROM embedded) and the developer tool smb4tools.exe with the
# C# compiler that ships with Windows (.NET Framework 4.8).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Debug] [-ToolsOnly] [-Out dir] [-Rom file.sfc]
#                     [-GameIni src\Frontend\game.ini] [-Icon src\app.ico] [-ExeName SuperMarioBros4]
#   -ToolsOnly rebuilds just smb4tools.exe (works while the game is running)
#   -Rom       ROM to embed; default bin\SuperMarioBros4.sfc, else snes\build\smb4.sfc
# The player needs the libretro core in bin\cores (tools\fetch-core.ps1 downloads it).
param([switch]$Debug, [switch]$ToolsOnly, [string]$Out = 'bin', [string]$Rom = '',
      [string]$GameIni = 'src\Frontend\game.ini', [string]$Icon = 'src\app.ico', [string]$ExeName = 'SuperMarioBros4')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

New-Item -ItemType Directory -Force $Out | Out-Null
$frontendDir = (Resolve-Path 'src\Frontend').Path

# smb4tools: everything except the player (the converter and reference tests use the legacy C# game in src\Game).
$toolSources = Get-ChildItem src -Recurse -Filter *.cs | Where-Object { -not $_.FullName.StartsWith($frontendDir) } | ForEach-Object { $_.FullName }

# game exe: the player only. src\Game (the retired C# game) is NOT part of it.
$gameSources = @()
$gameSources += Get-ChildItem src\Frontend -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
$gameSources += Get-ChildItem src\Platform -Filter *.cs | ForEach-Object { $_.FullName }
$gameSources += (Resolve-Path 'src\Engine\Ppu.cs').Path, (Resolve-Path 'src\Engine\Font.cs').Path

# Every file under data\ is embedded into smb4tools as a resource named by its relative path (e.g. levels/w1-1.lvl).
$resources = @()
if (Test-Path data) {
    $root = (Resolve-Path data).Path
    Get-ChildItem data -Recurse -File | Where-Object { $_.Extension -ne '.md' -and $_.Name -notlike '_*' } | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        $resources += "/resource:$($_.FullName),$rel"
    }
}

$common = @('/nologo', '/platform:x64', '/unsafe', '/langversion:5', '/warn:4', '/nowarn:1591,0649,0169,0414,0162',
          '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
if ($Debug) { $common += @('/debug+', '/define:DEBUG') } else { $common += @('/optimize+', '/debug-') }

if (-not $ToolsOnly) {
    if (-not $Rom) {
        foreach ($c in 'bin\SuperMarioBros4.sfc', 'snes\build\smb4.sfc') { if (Test-Path $c) { $Rom = $c; break } }
    }
    $gameRes = @("/resource:$((Resolve-Path $GameIni).Path),game.ini")
    if ($Rom) {
        $romFull = (Resolve-Path $Rom).Path
        $len = (Get-Item $romFull).Length
        if ($len -lt 32768 -or ($len % 1024) -ne 0) { throw "ROM $Rom looks incomplete ($len bytes)" }
        $gameRes += "/resource:$romFull,rom.sfc"
        Write-Host ("Embedding ROM {0} ({1:N0} KB)" -f $Rom, ($len / 1KB))
    } else { Write-Host "No ROM found to embed: the exe will need $ExeName.sfc (game.ini RomFile) next to it" -ForegroundColor Yellow }
    $game = $common + @('/target:winexe', '/main:SMB4.Frontend.PlayerProgram', "/out:$Out\$ExeName.exe")
    if ($Icon -and (Test-Path $Icon)) { $game += "/win32icon:$Icon" }
    & $csc @game @gameRes @gameSources
    if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }
}

$tools = $common + @('/target:exe', '/main:SMB4.Tools.ToolsProgram', "/out:$Out\smb4tools.exe")
& $csc @tools @resources @toolSources
if ($LASTEXITCODE -ne 0) { Write-Host "TOOLS BUILD FAILED" -ForegroundColor Red; exit 1 }

if ($ToolsOnly) { Write-Host "Built $Out\smb4tools.exe" -ForegroundColor Green }
else {
    $core = Get-ChildItem -Path "$Out\cores\*_libretro.dll", 'bin\cores\*_libretro.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $core) { Write-Host "Note: no libretro core found - run tools\fetch-core.ps1" -ForegroundColor Yellow }
    Write-Host ("Built $Out\$ExeName.exe ({0:N0} KB) + smb4tools.exe" -f ((Get-Item "$Out\$ExeName.exe").Length / 1KB)) -ForegroundColor Green
}
