# 2-player co-op test (engine): starts -Level with g_coop = 1 (Mario pad 1, Luigi pad 2) through the eng_dbg_level hook,
# drives both pads by -Mode, logs both players + lives, saves PNGs at -Shots into <Out>\coop\.
#   run    both run right (Mario jumps every 40 ticks, Luigi every 56)
#   lives  Mario waits, Luigi walks right into the enemies (deaths -> lives count down, respawn at Mario, out)
#   heads  Luigi is dropped onto Mario's head (stand), then from higher (bounce)
#   pipe   (1-1) Mario is put on the first pipe and goes down; Luigi rides along hidden and reappears with him
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\coop.ps1 [-Out snes\build] [-Level 1-1] [-Mode run] [-Ticks 600] [-Shots 60,300]
param([string]$Out = 'snes\build', [string]$Level = '1-1', [string]$Mode = 'run', [int]$Ticks = 600, [string]$Shots = '60,300',
      [int]$Form2 = 0, [string]$Runner = '', [int]$Coop = 1)
$ErrorActionPreference = 'Stop'
if ($Runner -eq '') { $Runner = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\snesrun.ps1' }
$Out = (Resolve-Path $Out).Path
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$inc = Get-Content (Join-Path $Out 'gen\levels.inc') -Raw
if ($inc -notmatch ("(?m)^LVL_" + $Level.ToUpper().Replace('-', '_') + " = (\d+)")) { throw "unknown level $Level" }
$lnum = $Matches[1]
$px = Sym 'p_x'
$lua = @"
local MODE=$(Sym 'g_mode') local DBG=$(Sym 'eng_dbg_level') local FR=$(Sym 'w_frame') local COOP=$(Sym 'g_coop')
local FORM2=$(Sym 'g_form2') local LIVES=$(Sym 'g_lives') local CUR=$(Sym 'co_cur') local CST=$(Sym 'co_st') local BLK=$(Sym 'co_blk')
local PX=$px local OY=$((Sym 'p_y') - $px) local OST=$((Sym 'p_state') - $px) local OYV=$((Sym 'p_yvel') - $px) local OIA=$((Sym 'p_inair') - $px)
local CAM=$(Sym 'cam_x') local RES=$(Sym 'w_result') local HIDE=$(Sym 'co_hide')
local LEVEL=$lnum local LAST=$Ticks local TEST='$Mode'
local want={} for _,s in ipairs({$Shots}) do want[s]=true end
local started=false local lastn=-1 local lag=0 local sl_sum=0 local sl_n=0 local sl_max=0
emu.addMemoryCallback(function() if started then local s=emu.getState()["ppu.scanline"] sl_sum=sl_sum+s sl_n=sl_n+1 if s>sl_max and s<225 then sl_max=s end end end, emu.callbackType.exec, $(Sym 'wait_nmi') % 65536 + 0x800000)
local function w(a,v) emu.write(a, v % 256, emu.memType.snesMemory) emu.write(a+1, (v // 256) % 256, emu.memType.snesMemory) end
local function s16(v) if v >= 32768 then return v - 65536 end return v end
-- base address of Mario's / Luigi's block
local function base(id) if readw(CUR) == id then return PX end return BLK end
local function st(id) local b=base(id) return string.format('x=%d y=%d st=%d yv=%d air=%d', readw(b)//16, s16(readw(b+OY))//16, readw(b+OST), s16(readw(b+OYV)), readw(b+OIA)) end
local function log(n) print(string.format('t=%d cur=%d lives=%d out=%d/%d hide=%d cam=%d | M %s | L %s', n, readw(CUR), readw(LIVES), readw(CST), readw(CST+2), readw(HIDE), readw(CAM), st(0), st(1))) end
emu.addEventCallback(function()
  local f = curframe()
  local mode = readw(MODE)
  if mode ~= 2 then
    if started then print('left the level (mode '..mode..', result '..readw(RES)..') at tick '..lastn..' lives '..readw(LIVES)); save('coop_$Mode'..'_exit'); finish(0) end
    if f >= 60 and f % 30 == 0 then w(COOP, $Coop) w(FORM2, $Form2) w(DBG, LEVEL + 1) end
    pad{} pad2{}
    if f > 1200 then print('never reached the level'); finish(5) end
    return
  end
  local n = readw(FR)
  if n > 0 then started = true end
  if started and n == lastn then lag = lag + 1 end
  local b1, b2 = {}, {}
  if TEST == 'run' then
    b1 = {right=true, y=true} if n % 40 < 18 then b1.b = true end
    b2 = {right=true} if n % 56 < 20 then b2.b = true end
  elseif TEST == 'lives' then
    b2 = {right=true}
  elseif TEST == 'pipe' then
    if n == 40 then local m = base(0) w(m, 1192*16) w(m+OY, 160*16) w(m+OYV, 0) end
    if n > 50 and n < 70 then b1 = {down=true} end
    if n > 200 then b1 = {right=true} b2 = {left=true} end
  elseif TEST == 'heads' then
    if n == 40 or n == 160 then
      local m = base(0) local l = base(1)
      w(l, readw(m)) w(l+OY, readw(m+OY) - (n == 40 and 40 or 96) * 16) w(l+OYV, 0) w(l+OIA, 1)
    end
  end
  pad(b1) pad2(b2)
  if n ~= lastn then
    lastn = n
    if want[n] then save('coop_$Mode'..'_'..n) end
    if (TEST == 'heads' and n >= 38 and n <= 260 and n % 4 == 0) or n % 60 == 0 then log(n) end
    if n >= LAST then log(n) print("lag frames "..lag.." avg-end-scanline "..(sl_n>0 and sl_sum//sl_n or 0).." max "..sl_max) finish(0) end
  end
  if f > 60*150 then print('timeout at tick '..n); finish(6) end
end, emu.eventType.endFrame)
"@
$luaFile = Join-Path $Out "coop_$Mode.lua"
Set-Content $luaFile $lua -Encoding ascii
& powershell -ExecutionPolicy Bypass -File $Runner -Rom (Join-Path $Out 'smb4.sfc') -Lua $luaFile -OutDir (Join-Path $Out 'coop') -TimeoutSec 300
exit $LASTEXITCODE
