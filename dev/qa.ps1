# Full content QA: validation, physics self-test, then reachability (Big + Small) and fuzzing for every level.
# Usage:  powershell -ExecutionPolicy Bypass -File qa.ps1 [-Fuzz 20]
param([int]$Fuzz = 20)
Set-Location $PSScriptRoot
$tool = "bin\smb4tools.exe"
$fail = 0
"== validate"; & $tool validate; if ($LASTEXITCODE -ne 0) { $fail++ }
"== selftest"; & $tool selftest | Select-Object -Last 1; if ($LASTEXITCODE -ne 0) { $fail++ }
"== map progression"; & $tool progress; if ($LASTEXITCODE -ne 0) { $fail++ }
"== levels"
$rows = @()
foreach ($f in Get-ChildItem data\levels -Filter *.lvl | Sort-Object Name) {
    $id = $f.BaseName
    if ($id.StartsWith('_')) { continue }   # tool-only test levels
    $big = (& $tool reach $id Big) -join ' '
    $small = (& $tool reach $id Small) -join ' '
    $fz = (& $tool fuzz $id $Fuzz) -join ' '
    $crash = if ($fz -match 'crashes=(\d+)') { [int]$Matches[1] } else { -1 }
    $okB = $big -match 'REACHABLE'; $okS = $small -match 'REACHABLE'
    if (-not $okB -or -not $okS -or $crash -ne 0) { $fail++ }
    $rows += [pscustomobject]@{ Level = $id; Big = $(if ($okB) { 'ok' } else { 'NO' }); Small = $(if ($okS) { 'ok' } else { 'NO' }); Crashes = $crash; Note = $(if (-not $okB) { $big } elseif (-not $okS) { $small } else { '' }) }
}
$rows | Format-Table -AutoSize | Out-String -Width 220
if ($fail -eq 0) { "QA PASSED" } else { "QA: $fail problem(s)" }
