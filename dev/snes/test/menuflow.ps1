# Real menu path from power-on: title -> 1 or 2 PLAYER GAME -> file 1 -> map -> first level; runs right (both pads in 2P),
# saves PNGs at level ticks and counts lag frames (frames in play mode where the level tick did not advance).
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\menuflow.ps1 [-Out snes\build] [-Players 1] [-Ticks 600] [-Tag x]
param([string]$Out = 'snes\build', [int]$Players = 1, [int]$Ticks = 600, [string]$Tag = '', [string]$Rom = '')
$ErrorActionPreference = 'Stop'
$Out = (Resolve-Path $Out).Path
if ($Rom -eq '') { $Rom = Join-Path $Out 'smb4.sfc' }
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$coop = 0; try { $coop = Sym 'g_coop' } catch {}
$lua = @"
local MODE=$(Sym 'g_mode') local FR=$(Sym 'w_frame') local PAUSED=$(Sym 'g_paused') local COOP=$coop
local P2=$Players local LAST=$Ticks
local started=false local lastn=-1 local lag=0 local play=0 local navt=0
emu.addEventCallback(function()
  local f = curframe()
  local mode = readw(MODE)
  if mode ~= 2 then
    if started then print('left the level at tick '..lastn) save('mf$Players$Tag'..'_exit') finish(0) end
    -- menus: at the title pick the entry, then confirm everything
    local k = f % 40
    local b = {}
    if f > 150 then
      if P2 == 2 and f < 300 then if f >= 232 and f < 236 then b.down = true end if f >= 280 and f < 284 then b.start = true end
      elseif k == 0 then b.start = true elseif k == 20 then b.b = true end
    end

    pad(b) pad2{}
    if f > 4000 then save('mf$Players$Tag'..'_stuck') print('never reached a level') finish(5) end
    return
  end
  local n = readw(FR)
  if not started then started = true print('level started at frame '..f..' coop='..(COOP>0 and readw(COOP) or -1)) end
  if n == lastn and readw(PAUSED) == 0 then lag = lag + 1 end
  play = play + 1
  if n ~= lastn then
    lastn = n
    if n == 100 or n == 300 or n == 500 then save('mf$Players$Tag'..'_'..n) end
    if n >= LAST then print('ticks '..n..' frames '..play..' lag '..lag) finish(0) end
  end
  local b = {right=true} if n % 50 < 16 then b.b = true end
  pad(b) if P2 == 2 then pad2{right=true, b=(n % 60 < 16)} else pad2{} end
end, emu.eventType.endFrame)
"@
$luaFile = Join-Path $Out "menuflow$Players.lua"
Set-Content $luaFile $lua -Encoding ascii
& powershell -ExecutionPolicy Bypass -File (Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\snesrun.ps1') -Rom $Rom -Lua $luaFile -OutDir (Join-Path $Out 'menuflow') -TimeoutSec 300
exit $LASTEXITCODE
