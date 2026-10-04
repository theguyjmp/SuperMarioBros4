# Real menu path from power-on: title -> 1 or 2 PLAYER GAME -> file 1 -> map -> first level (1-1); runs right (both pads
# in 2P), saves PNGs at level ticks and counts lag frames (play frames where the level tick did not advance). With
# -Levels "1-1,1-3,4-1" the later levels are started from inside that session (eng_dbg_level) after -Ticks each.
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\menuflow.ps1 [-Out snes\build] [-Players 1] [-Ticks 600] [-Levels 1-1]
param([string]$Out = 'snes\build', [int]$Players = 1, [int]$Ticks = 600, [string]$Tag = '', [string]$Levels = '1-1')
$ErrorActionPreference = 'Stop'
$Out = (Resolve-Path $Out).Path
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$inc = Get-Content (Join-Path $Out 'gen\levels.inc') -Raw
$lv = @(); $names = @()
foreach ($l in ($Levels -split ',')) { if ($inc -notmatch ("(?m)^LVL_" + $l.ToUpper().Replace('-', '_') + " = (\d+)")) { throw "unknown level $l" }; $lv += $Matches[1]; $names += "'$l'" }
$coop = 0; try { $coop = Sym 'g_coop' } catch {}
$lua = @"
local MODE=$(Sym 'g_mode') local FR=$(Sym 'w_frame') local PAUSED=$(Sym 'g_paused') local COOP=$coop local DBG=$(Sym 'eng_dbg_level')
local P2=$Players local LAST=$Ticks local LV={$($lv -join ',')} local NAMES={$($names -join ',')}
local started=false local lastn=-1 local lag=0 local play=0 local li=1 local switching=false
local function w(a,v) emu.write(a, v % 256, emu.memType.snesMemory) emu.write(a+1, (v // 256) % 256, emu.memType.snesMemory) end
emu.addEventCallback(function()
  local f = curframe()
  local mode = readw(MODE)
  if mode ~= 2 then
    if started and not switching then print(NAMES[li]..': left the level at tick '..lastn..' lag frames '..lag) save('mf$Players$Tag'..'_'..NAMES[li]..'_exit') finish(0) end
    local b = {}
    if f > 150 then
      if P2 == 2 and f < 300 then if f >= 232 and f < 236 then b.down = true end if f >= 280 and f < 284 then b.start = true end
      elseif f % 40 == 0 and f < 900 then b.start = true elseif f % 40 == 20 then b.b = true elseif f > 900 and f % 40 >= 6 and f % 40 < 14 then b.right = true end
    end
    pad(b) pad2{}
    if f > 5000 then save('mf$Players$Tag'..'_stuck') print('never reached a level') finish(5) end
    return
  end
  local n = readw(FR)
  if switching then if n < 5 then switching = false lastn = -1 lag = 0 play = 0 else pad{} pad2{} return end end
  if not started then started = true print('level started at frame '..f..' coop='..(COOP>0 and readw(COOP) or -1)) end
  if n == lastn and readw(PAUSED) == 0 then lag = lag + 1 end
  play = play + 1
  if n ~= lastn then
    lastn = n
    if n == 150 or n == 400 then save('mf$Players$Tag'..'_'..NAMES[li]..'_'..n) end
    if n >= LAST then
      print(NAMES[li]..': ticks '..n..' lag frames '..lag)
      li = li + 1
      if li > #LV then finish(0) return end
      switching = true w(DBG, LV[li] + 1)
    end
  end
  local b = {right=true} if n % 50 < 16 then b.b = true end
  pad(b) if P2 == 2 then pad2{right=true, b=(n % 60 < 16)} else pad2{} end
end, emu.eventType.endFrame)
"@
$luaFile = Join-Path $Out "menuflow$Players.lua"
Set-Content $luaFile $lua -Encoding ascii
& powershell -ExecutionPolicy Bypass -File (Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\snesrun.ps1') -Rom (Join-Path $Out 'smb4.sfc') -Lua $luaFile -OutDir (Join-Path $Out 'menuflow') -TimeoutSec 400
exit $LASTEXITCODE
