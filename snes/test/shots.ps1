# Screenshot helper (engine agent): boots the ROM, picks -Level through the level select, plays scripted input and saves
# PNGs at the given level ticks into <Out>\shots\<level>_<tick>.png.
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\shots.ps1 -Out snes\build-engine [-Level 1-1] [-Shots 30,120]
#        [-Hold right,y] [-JumpEvery 40] [-Form 0] [-Poke "p_star=500"] [-Ticks 400]
param([string]$Out = 'snes\build', [string]$Level = '1-1', [string]$Shots = '30,120,240', [string]$Hold = 'right',
      [int]$JumpEvery = 0, [int]$TapY = 0, [int]$Form = -1, [string]$Poke = '', [int]$Ticks = 0, [string]$Runner = '', [string]$Tag = '')
$ErrorActionPreference = 'Stop'
if ($Runner -eq '') { $Runner = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\snesrun.ps1' }
$Out = (Resolve-Path $Out).Path
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$inc = Get-Content (Join-Path $Out 'gen\levels.inc') -Raw
$lvlSym = 'LVL_' + $Level.ToUpper().Replace('-', '_')
if ($inc -notmatch "(?m)^$lvlSym = (\d+)") { throw "unknown level $Level" }
$lnum = $Matches[1]
$shotList = ($Shots -split ',' | ForEach-Object { [int]$_ })
$last = ($shotList | Measure-Object -Maximum).Maximum
if ($Ticks -gt $last) { $last = $Ticks }
$held = ($Hold -split ',' | Where-Object { $_ } | ForEach-Object { "$_=true" }) -join ','
$pokes = ''
foreach ($p in ($Poke -split ';' | Where-Object { $_ })) { $kv = $p.Split('='); $pokes += "emu.write($(Sym $kv[0]), $($kv[1]) % 256, emu.memType.snesMemory) emu.write($(Sym $kv[0])+1, $($kv[1]) // 256, emu.memType.snesMemory) " }
$lua = @"
local MODE=$(Sym 'g_mode') local DBG=$(Sym 'eng_dbg_level') local FR=$(Sym 'w_frame') local PFORM=$(Sym 'p_form')
local LEVEL=$lnum local FORM=$Form local LAST=$last local JE=$JumpEvery local TY=$TapY
local SHOTS={$($shotList -join ',')}
local want={} for _,s in ipairs(SHOTS) do want[s]=true end
local started=false local lastn=-1 local poked=false local ticks=0
emu.addEventCallback(function()
  local f = curframe()
  local mode = readw(MODE)
  if mode ~= 2 then
    if started then print('left the level (mode '..mode..') at tick '..lastn); save('$Level$Tag'..'_exit'); finish(0) end
    if f >= 60 and f % 30 == 0 then emu.write(DBG, LEVEL + 1, emu.memType.snesMemory) end
    pad{}
    if f > 1200 then print('never reached the level'); finish(5) end
    return
  end
  local n = readw(FR)
  if n == 0 and FORM >= 0 then emu.write(PFORM, FORM, emu.memType.snesMemory) end
  if n > 0 then started = true end
  if started and not poked then $pokes poked = true end
  if n ~= lastn then
    lastn = n ticks = ticks + 1
    if want[n] then save('$Level$Tag'..'_'..n) end
    if n >= LAST then finish(0) end
  end
  local b = {$held}
  if JE > 0 and (n % JE) < JE/2 then b.b = true end
  if TY > 0 and (n % TY) < TY/2 then b.y = true end
  pad(b)
  if f > 60*120 then print('timeout at tick '..n); finish(6) end
end, emu.eventType.endFrame)
"@
$luaFile = Join-Path $Out "shots_$Level.lua"
Set-Content $luaFile $lua -Encoding ascii
& powershell -ExecutionPolicy Bypass -File $Runner -Rom (Join-Path $Out 'smb4.sfc') -Lua $luaFile -OutDir (Join-Path $Out 'shots') -TimeoutSec 300
exit $LASTEXITCODE
