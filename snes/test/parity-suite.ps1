# Physics parity suite (engine agent): runs snes\test\parity.ps1 (C# trace vs ROM, frame by frame) for one level per
# theme plus the Raccoon-flight and swimming scripts. Prints one line per case; exit 0 only if all pass.
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\parity-suite.ps1 -Tools <smb4tools.exe> -Out <build dir> [-Only 1-2,3-1]
param([string]$Tools = 'bin\smb4tools.exe', [string]$Out = 'snes\build', [string]$Only = '', [string]$Runner = '')
$ErrorActionPreference = 'Continue'
if ($Runner -eq '') { $Runner = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\snesrun.ps1' }
$run = 'W20,BR90,BRJ20,BR40,BRJ20,BR60,BRJ26,BR30,W20,BR60,BRJ20,BR60,BRJ26,BR40'
$cases = @(
    @{ Name = '1-1 full route (plains)'; Level = '1-1'; Form = 'Small'; Script = '' }
    @{ Name = '1-1 fire'; Level = '1-1'; Form = 'Fire'; Script = '' }
    @{ Name = '1-2 underground'; Level = '1-2'; Form = 'Small'; Script = $run }
    @{ Name = '1-4 sky autoscroll + lifts'; Level = '1-4'; Form = 'Big'; Script = $run }
    @{ Name = '2-1 desert'; Level = '2-1'; Form = 'Small'; Script = $run }
    @{ Name = '3-1 sea (swimming)'; Level = '3-1'; Form = 'Big'; Script = 'W10,R60,RJ10,R20,J6,W10,J6,W10,J6,R40,J6,R30,J6,W20,RJ8,R30,J6,R40,J6,L20,J6,R60' }
    @{ Name = '3-1 frog swim'; Level = '3-1'; Form = 'Frog'; Script = 'W10,R60,RJ10,R20,J6,W10,UR40,DR40,R40,UJ20,R60' }
    @{ Name = '4-1 jungle'; Level = '4-1'; Form = 'Small'; Script = $run }
    @{ Name = '5-1 sky'; Level = '5-1'; Form = 'Small'; Script = $run }
    @{ Name = '6-1 ice'; Level = '6-1'; Form = 'Big'; Script = $run }
    @{ Name = '7-1 machine'; Level = '7-1'; Form = 'Small'; Script = $run }
    @{ Name = '8-2 volcano'; Level = '8-2'; Form = 'Big'; Script = $run }
    @{ Name = '1-f fortress'; Level = '1-f'; Form = 'Big'; Script = $run }
    @{ Name = '1-a airship'; Level = '1-a'; Form = 'Big'; Script = $run }
    @{ Name = '8-c castle'; Level = '8-c'; Form = 'Big'; Script = $run }
    @{ Name = '1-1 raccoon P-run + flight'; Level = '1-1'; Form = 'Raccoon'; Script = 'W20,BR160,BRJ12,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR4,BRJ4,BR60,W60' }
    @{ Name = '1-1 tanooki statue'; Level = '1-1'; Form = 'Tanooki'; Script = 'W20,R60,J4,W30,DB2,D60,W20,R40' }
)
$fail = 0
foreach ($c in $cases) {
    if ($Only -ne '' -and ($Only -split ',') -notcontains $c.Level) { continue }
    $args2 = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'parity.ps1'), '-Tools', $Tools, '-Out', $Out, '-Runner', $Runner, '-Level', $c.Level, '-Form', $c.Form)
    if ($c.Script -ne '') { $args2 += @('-Script', $c.Script) }
    $o = & powershell @args2 2>&1 | ForEach-Object { "$_" }
    $res = ($o | Where-Object { $_ -match '^(PASS|FAIL|MISMATCH|INPUT|left|tick skip)' } | Select-Object -First 3) -join ' | '
    $ok = ($o -match '^PASS').Count -gt 0
    if (-not $ok) { $fail++ }
    Write-Host ("{0,-4} {1,-32} {2}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $c.Name, $res)
}
Write-Host ("parity suite: {0} failing case(s)" -f $fail)
exit $(if ($fail -eq 0) { 0 } else { 1 })
