# Builds bin\SuperMarioBros4.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Debug] [-ToolsOnly] [-Out dir]
#   -ToolsOnly rebuilds just bin\smb4tools.exe (works while the game is running)
param([switch]$Debug, [switch]$ToolsOnly, [string]$Out = 'bin')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

New-Item -ItemType Directory -Force $Out | Out-Null
$sources = Get-ChildItem src -Recurse -Filter *.cs | ForEach-Object { $_.FullName }

# Every file under data\ is embedded as a resource named by its relative path (e.g. levels/w1-1.lvl).
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
    $game = $common + @('/target:winexe', '/main:SMB4.Program', "/out:$Out\SuperMarioBros4.exe")
    if (Test-Path 'src\app.ico') { $game += '/win32icon:src\app.ico' }
    & $csc @game @resources @sources
    if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }
}

$tools = $common + @('/target:exe', '/main:SMB4.Tools.ToolsProgram', "/out:$Out\smb4tools.exe")
& $csc @tools @resources @sources
if ($LASTEXITCODE -ne 0) { Write-Host "TOOLS BUILD FAILED" -ForegroundColor Red; exit 1 }

if ($ToolsOnly) { Write-Host "Built $Out\smb4tools.exe" -ForegroundColor Green }
else { Write-Host ("Built $Out\SuperMarioBros4.exe ({0:N0} KB) + smb4tools.exe" -f ((Get-Item "$Out\SuperMarioBros4.exe").Length / 1KB)) -ForegroundColor Green }
