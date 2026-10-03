# Copy of snes/tools/snesrun.ps1 that plugs a SNES controller into port 1 (Mesen2 test runner default: none).
param([Parameter(Mandatory)][string]$Rom, [Parameter(Mandatory)][string]$Lua, [string]$OutDir = '.', [int]$TimeoutSec = 120)
$ErrorActionPreference = 'Stop'
$here = Join-Path (Split-Path $PSScriptRoot -Parent) "tools"
$Rom = (Resolve-Path $Rom).Path; $Lua = (Resolve-Path $Lua).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path
$combined = Join-Path $env:TEMP ("snesrun_" + [guid]::NewGuid().ToString('N') + ".lua")
(Get-Content "$here/prelude.lua" -Raw) + "`n" + $(if (Test-Path "$PSScriptRoot/sym.lua") { Get-Content "$PSScriptRoot/sym.lua" -Raw } else { "" }) + "`n" + (Get-Content $Lua -Raw) | Set-Content $combined -Encoding ascii
$job = Start-Job { param($m, $r, $l) & $m --testrunner $r $l --snes.port1.type=SnesController --snes.port2.type=SnesController 2>&1 | Out-String; "EXITCODE:$LASTEXITCODE" } -ArgumentList "$here/mesen/Mesen.exe", $Rom, $combined
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
