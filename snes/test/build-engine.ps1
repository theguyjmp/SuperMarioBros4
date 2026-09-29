# Engine test build (engine agent): like snes\build.ps1, but entity files owned by other agents (snes\src\ent_*.s) that
# don't assemble right now (mid-edit) are left out instead of failing the build. Everything is staged in -Stage.
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\build-engine.ps1 -Tools <smb4tools.exe> -Out <dir> [-NoExport]
#        [-Only ent_goomba.s,ent_koopa.s]   (-Only: foreign ent files to consider; default all)
param([string]$Tools = 'bin\smb4tools.exe', [string]$Out = 'snes\build-engine', [switch]$NoExport, [string]$Only = '',
      [string]$Stage = '')
$ErrorActionPreference = 'Stop'
$snes = Split-Path $PSScriptRoot -Parent
$root = Split-Path $snes -Parent
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$Tools = (Resolve-Path $Tools).Path
if ($Stage -eq '') { $Stage = Join-Path $Out 'stage' }
$engineEnts = @('ent_goomba.s', 'ent_koopa.s', 'ent_shell.s', 'ent_piranha.s', 'ent_items.s', 'ent_fx.s', 'ent_plproj.s', 'ent_lifts.s')
New-Item -ItemType Directory -Force "$Stage\src", "$Out\gen", "$Out\obj" | Out-Null
Remove-Item "$Stage\src\*" -Force -ErrorAction SilentlyContinue
if (-not $NoExport) {
    Push-Location $root
    & $Tools snes-export "$Out\gen" | Out-Null
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) { Write-Host "EXPORT FAILED" -ForegroundColor Red; exit 1 }
}
$ca65 = "$snes\tools\cc65\bin\ca65.exe"
$tmpo = Join-Path $Stage 'probe.o'
# probe include dir: gen files + an ent_ids.inc listing every marker (as build.ps1 will)
$pgen = Join-Path $Stage 'probegen'
New-Item -ItemType Directory -Force $pgen | Out-Null
Copy-Item "$Out\gen\*.inc" $pgen -Force
$names = @()
foreach ($m in (Select-String -Path "$snes\src\ent_*.s" -Pattern '^\s*;@entity\s+([A-Z0-9_]+)')) { $names += $m.Matches[0].Groups[1].Value }
$names = @($names | Sort-Object -Unique)
$ids = @('ET_NONE = 0'); for ($i = 0; $i -lt $names.Count; $i++) { $ids += ('ET_{0} = {1}' -f $names[$i], ($i + 1)) }
$ids += ('ET_COUNT = {0}' -f ($names.Count + 1))
Set-Content "$pgen\ent_ids.inc" $ids -Encoding ascii
$skipped = @()
foreach ($f in Get-ChildItem "$snes\src") {
    if ($f.Name -like 'ent_*.s' -and $engineEnts -notcontains $f.Name) {
        if ($Only -ne '' -and ($Only -split ',') -notcontains $f.Name) { $skipped += $f.Name; continue }
        # a first-pass probe needs gen\ent_ids.inc: generated below by build.ps1, so use the previous one if present
        cmd /c "`"$ca65`" --cpu 65816 -I `"$snes\src`" -I `"$pgen`" -I `"$Out\gen`" --bin-include-dir `"$Out\gen`" `"$($f.FullName)`" -o `"$tmpo`" >nul 2>nul"
        if ($LASTEXITCODE -ne 0) { $skipped += $f.Name; continue }
    }
    Copy-Item $f.FullName "$Stage\src\"
}
Copy-Item "$snes\build.ps1", "$snes\lorom.cfg" $Stage -Force
if (-not (Test-Path "$Stage\tools")) { cmd /c mklink /J "$Stage\tools" "$snes\tools" | Out-Null }
$ErrorActionPreference = 'Continue'
for ($try = 0; $try -lt 6; $try++) {
    Remove-Item "$Out\obj\*" -Force -ErrorAction SilentlyContinue
    $log = & powershell -ExecutionPolicy Bypass -File "$Stage\build.ps1" -Tools $Tools -Out $Out -NoExport 2>&1 | ForEach-Object { "$_" }
    $rc = $LASTEXITCODE
    if ($rc -eq 0) { break }
    # drop foreign entity files that break the link (unresolved imports) or assembly, then retry
    $bad = @()
    foreach ($l in $log) {
        foreach ($mm in [regex]::Matches($l, '(ent_[a-z0-9_]+)\.s')) { $n = $mm.Groups[1].Value + '.s'; if ($engineEnts -notcontains $n -and $n -ne 'ent_table.s') { $bad += $n } }
        # screens module (other agent): its src and generated files are optional for engine tests
        if ($l -match "scr_[a-z0-9_]+.(s|inc)") { $bad += "SCR" }
        foreach ($mm in [regex]::Matches($l, "([A-Z0-9_]+)_vt")) {
            $hit = Select-String -Path "$Stage\src\ent_*.s" -Pattern ('^\s*;@entity\s+' + $mm.Groups[1].Value + '\b') | Select-Object -First 1
            if ($hit -and $engineEnts -notcontains $hit.Filename) { $bad += $hit.Filename }
        }
    }
    $bad = @($bad | Sort-Object -Unique)
    if ($bad.Count -eq 0) { $log | Select-Object -Last 15 | Write-Host; break }
    foreach ($b in $bad) {
        if ($b -eq 'SCR') {
            Remove-Item "$Stage\src\scr_*.s", "$Out\gen\scr_*.s" -Force -ErrorAction SilentlyContinue
            Set-Content "$Out\gen\scr_ids.inc" '; screens left out of this engine test build (no SCR_HOOKS)' -Encoding ascii
            $skipped += 'scr_* (screens)'; continue
        }
        Remove-Item "$Stage\src\$b" -Force -ErrorAction SilentlyContinue; $skipped += $b
    }
}
if ($skipped.Count) { Write-Host ("skipped foreign entity files: " + (($skipped | Sort-Object -Unique) -join ', ')) -ForegroundColor Yellow }
$log | Where-Object { $_ -match 'Built|FAILED' } | Write-Host
exit $rc
