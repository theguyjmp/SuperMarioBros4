# Runs a SNES ROM headless in Mesen2 with a Lua test script; PNGs saved via save(name) land in -OutDir.
# Usage: powershell -File snes\tools\snesrun.ps1 -Rom build\smb4.sfc -Lua test.lua [-OutDir out] [-TimeoutSec 120]
param([Parameter(Mandatory)][string]$Rom, [Parameter(Mandatory)][string]$Lua, [string]$OutDir = '.', [int]$TimeoutSec = 120)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$Rom = (Resolve-Path $Rom).Path; $Lua = (Resolve-Path $Lua).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path
$combined = Join-Path $env:TEMP ("snesrun_" + [guid]::NewGuid().ToString('N') + ".lua")
(Get-Content "$here\prelude.lua" -Raw) + "`n" + (Get-Content $Lua -Raw) | Set-Content $combined -Encoding ascii
$job = Start-Job { param($m, $r, $l) & $m --testrunner --snes.port1.type=SnesController $r $l 2>&1 | Out-String; "EXITCODE:$LASTEXITCODE" } -ArgumentList "$here\mesen\Mesen.exe", $Rom, $combined
if (-not (Wait-Job $job -Timeout $TimeoutSec)) { Get-Process Mesen -ErrorAction SilentlyContinue | Stop-Process -Force; Write-Host "TIMEOUT"; Remove-Item $combined; exit 124 }
$out = Receive-Job $job; Remove-Item $combined
$code = 0
foreach ($line in ($out -split "`r?`n")) {
    if ($line -like 'PNG:*') {
        $p = $line.Split(':', 3); $hex = $p[2]
        $bytes = New-Object byte[] ($hex.Length / 2)
        for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
        $f = Join-Path $OutDir ($p[1] + '.png'); [IO.File]::WriteAllBytes($f, $bytes); Write-Host "saved $f"
    } elseif ($line -like 'EXITCODE:*') { $code = [int]$line.Substring(9) }
    elseif ($line.Trim() -ne '') { Write-Host $line }
}
exit $code
