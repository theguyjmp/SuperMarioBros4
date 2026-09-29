# C# vs ROM entity parity (enemies-A agent): runs ent_dump.ps1 with INFO after every token and diffs the entity lists
# (type, Px, Py per tick). Example: ent_compare.ps1 -Level 6-4 -Name bro -Route "W1,@112:12,W10,W10,W10" -Out snes\build-x -Tools <smb4tools.exe>
param([string]$Level, [string]$Route, [string]$Name, [int]$SnapEvery = 6, [string]$Form = 'Small', [string]$Out = 'snes\build-enA', [string]$Tools = '.agentbin\snes-enA\smb4tools.exe', [string]$OutRoot = "$env:TEMP\ent_compare")
$s = $PSScriptRoot
$toks = $Route.Split(',')
$parts = @(); $k = 0
for ($q = 0; $q -lt $toks.Count; $q++) { $t = $toks[$q]; $parts += $t; if ($q + 1 -lt $toks.Count -and $toks[$q + 1].StartsWith('@')) { continue }; $parts += 'INFO'; $k++; if ($k % $SnapEvery -eq 0) { $parts += 'SNAP' } }
$sc = $parts -join ','
& powershell -ExecutionPolicy Bypass -File "$s\ent_dump.ps1" -Level $Level -Name $Name -Script $sc -Form $Form -Out $Out -Tools $Tools -OutRoot $OutRoot 2>&1 | Where-Object { $_ -notlike 'saved*' } | Out-File "$OutRoot\$Name.txt"
$txt = @(Get-Content "$OutRoot\$Name.txt")
$i = [array]::IndexOf($txt, '== ROM')
$cs = @($txt[1..($i - 1)] | Where-Object { $_ -notmatch '^player at' } | ForEach-Object { $_ -replace ' state=.*cam=', ' cam=' -replace ' behind$', '' })
$rom = @($txt[($i + 1)..($txt.Count - 1)])
$map = @{ GoalBox = 'GOAL_BOX'; ScorePopup = 'FX_POPUP'; Cheep = 'CHEEP'; Goomba = 'GOOMBA'; Koopa = 'KOOPA'; Mushroom = 'MUSHROOM';
    Puff = 'FX_PUFF'; Sparkle = 'FX_SPARKLE'; Splash = 'FX_SPLASH'; Blooper = 'BLOOPER'; BobOmb = 'BOBOMB'; Explosion = 'EXPLOSION';
    HammerBro = 'HAMMER_BRO'; EnemyHammer = 'ENEMY_HAMMER'; Buzzy = 'BUZZY'; Shell = 'SHELL'; Spiny = 'SPINY'; Lakitu = 'LAKITU';
    CoinPop = 'FX_COINPOP'; BumpBlock = 'FX_BUMP'; Debris = 'FX_DEBRIS'; Leaf = 'LEAF'; Flower = 'FLOWER'; Star = 'STAR'; Piranha = 'PIRANHA';
    EnemyFire = 'VENUS_FIRE'; Dust = 'FX_DUST'; MovingLift = 'LIFT'; DonutLift = 'DONUT_LIFT' }
$diff = 0
for ($k = 0; $k -lt $cs.Count; $k++) {
    $c = $cs[$k]
    foreach ($key in $map.Keys) { $c = $c -replace "^    $key at", "    $($map[$key]) at" }
    if ($k -ge $rom.Count -or $c -ne $rom[$k]) { "DIFF @line $k`n C#:  $c`n ROM: $($rom[$k])"; $diff = 1; break }
}
if ($diff -eq 0) { "MATCH: $($cs.Count) lines ($(($cs | Where-Object { $_ -like '  tick*' }).Count) dumps)" }
"last C#: " + (($cs | Where-Object { $_ -like '  tick*' }) | Select-Object -Last 1)



