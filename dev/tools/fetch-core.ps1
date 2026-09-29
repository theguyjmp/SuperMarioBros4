# Downloads the bsnes libretro core (GPLv3, https://github.com/libretro/bsnes) that the game exe uses to run the ROM.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\fetch-core.ps1 [-Core bsnes] [-Dest bin\cores] [-Force]
# Any other libretro SNES core from the buildbot works too (e.g. -Core snes9x), set Core= in src\Frontend\game.ini.
param([string]$Core = 'bsnes', [string]$Dest = '', [switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Dest) { $Dest = Join-Path $root 'bin\cores' }
New-Item -ItemType Directory -Force $Dest | Out-Null
$dll = Join-Path $Dest "$($Core)_libretro.dll"
if ((Test-Path $dll) -and -not $Force) { Write-Host "$dll already present (use -Force to update)"; exit 0 }
$url = "https://buildbot.libretro.com/nightly/windows/x86_64/latest/$($Core)_libretro.dll.zip"
$zip = Join-Path $env:TEMP "$($Core)_libretro.dll.zip"
Write-Host "Downloading $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
Expand-Archive -Path $zip -DestinationPath $Dest -Force
Remove-Item $zip -ErrorAction SilentlyContinue
if (-not (Test-Path $dll)) { throw "download did not contain $($Core)_libretro.dll" }
Write-Host ("Installed {0} ({1:N0} KB)" -f $dll, ((Get-Item $dll).Length / 1KB)) -ForegroundColor Green
