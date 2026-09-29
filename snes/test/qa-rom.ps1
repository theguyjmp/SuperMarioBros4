# ROM QA (engine agent): boots every level headless (test hook eng_dbg_level), plays it with a blind "run right and
# jump" pad for -Ticks level ticks and fails on a crash or a hang:
#   * BRK/COP vector executed (the CPU ran into zeros / garbage),
#   * main loop hang: the level tick counter (w_frame) stalls for 180 frames while a level runs,
#   * NMI stall: frame_count stops,
#   * the level never starts.
# Screenshots of each level at the end of its run go to <Out>\qa\<level>.png (look at them!).
# Then runs the physics parity test(s) (snes\test\parity.ps1) unless -NoParity.
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\qa-rom.ps1 -Tools <smb4tools.exe> -Out <build dir>
#        [-Ticks 900] [-Levels 1-1,1-2] [-NoParity] [-NoShots]
param([string]$Tools = 'bin\smb4tools.exe', [string]$Out = 'snes\build', [int]$Ticks = 900, [string]$Levels = '',
      [switch]$NoParity, [switch]$NoShots, [string]$Runner = '')
$ErrorActionPreference = 'Stop'
if ($Runner -eq '') { $Runner = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\snesrun.ps1' }
$Out = (Resolve-Path $Out).Path
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) {
    if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) }
    throw "symbol $n not found"
}
$inc = Get-Content (Join-Path $Out 'gen\levels.inc')
$list = @()
foreach ($l in $inc) { if ($l -match '^LVL_([A-Z0-9_]+) = (\d+)') { $list += [pscustomobject]@{ Name = $Matches[1].Replace('_', '-').ToLower(); Idx = [int]$Matches[2] } } }
if ($Levels -ne '') { $want = $Levels.ToLower() -split ','; $list = @($list | Where-Object { $want -contains $_.Name }) }
if ($list.Count -eq 0) { throw 'no levels' }
$all = $list
$rc = 0
$Batch = 12   # Mesen's test runner exits after ~25k frames: one emulator run per 12 levels
for ($b0 = 0; $b0 -lt $all.Count; $b0 += $Batch) {
$list = @($all[$b0..([Math]::Min($all.Count, $b0 + $Batch) - 1)])
$names = ($list | ForEach-Object { "'" + $_.Name + "'" }) -join ','
$idxs = ($list | ForEach-Object { $_.Idx }) -join ','
$brk = Sym 'brk_handler'
$shots = if ($NoShots) { 'false' } else { 'true' }
$lua = @"
local MODE=$(Sym 'g_mode') local FR=$(Sym 'w_frame') local DBG=$(Sym 'eng_dbg_level') local FC=$(Sym 'frame_count')
local LEVEL=$(Sym 'g_level') local PSTATE=$(Sym 'p_state') local PX=$(Sym 'p_x')
local NAMES={$names} local IDX={$idxs} local TICKS=$Ticks local SHOTS=$shots
local cur=1 local phase='boot' local t0=0 local lastfr=-1 local stall=0 local lastfc=-1 local fcstall=0
local ticks=0 local fails=0 local brkhit=false local maxx=0 local deaths=0 local laststate=0 local results={}
emu.addMemoryCallback(function() brkhit=true end, emu.callbackType.exec, $brk, $brk)
local function report(msg, bad)
  local r = NAMES[cur]..': '..msg
  print((bad and 'FAIL ' or 'ok   ')..r)
  if bad then fails = fails + 1 end
end
local function nextlevel()
  if SHOTS then save('qa_'..NAMES[cur]) end
  cur = cur + 1 phase = 'start' t0 = curframe() lastfr = -1 stall = 0 ticks = 0 maxx = 0 deaths = 0 brkhit = false
  if cur > #NAMES then
    print('QA: '..(#NAMES - fails)..'/'..#NAMES..' levels ok')
    finish(fails == 0 and 0 or 2)
  end
end
emu.addEventCallback(function()
  local f = curframe()
  local fc = readw(FC)
  if fc == lastfc then fcstall = fcstall + 1 else fcstall = 0 end
  lastfc = fc
  if fcstall > 120 then report('NMI stalled (frame_count stuck)', true) finish(3) return end
  if brkhit then report('BRK/COP executed (crash) after '..ticks..' ticks', true) brkhit=false nextlevel() return end
  if phase == 'boot' then
    if f >= 90 then phase = 'start' t0 = f end
    pad{} return
  end
  local mode = readw(MODE)
  if phase == 'start' then
    if (f - t0) % 20 == 0 then emu.write(DBG, IDX[cur] + 1, emu.memType.snesMemory) end
    if mode == 2 and readw(LEVEL) == IDX[cur] and readw(DBG) == 0 then phase = 'run' lastfr = readw(FR) end
    if f - t0 > 600 then report('never started', true) nextlevel() end
    pad{} return
  end
  -- run
  if mode ~= 2 then report('left the level (mode '..mode..') after '..ticks..' ticks, deaths '..deaths, false) nextlevel() return end
  local n = readw(FR)
  if n == lastfr then stall = stall + 1 else stall = 0 ticks = ticks + 1 end
  lastfr = n
  if stall > 180 then report('HANG: level tick stalled at '..n, true) nextlevel() return end
  local st = readw(PSTATE)
  if st == 3 and laststate ~= 3 then deaths = deaths + 1 end
  laststate = st
  local x = readw(PX) // 16
  if x > maxx then maxx = x end
  if ticks >= TICKS then report('ran '..ticks..' ticks, max x '..maxx..' px, deaths '..deaths, false) nextlevel() return end
  -- blind player: run right, jump for 24 of every 64 ticks, sometimes a short hop, pause-free
  local b = {right=true, y=true}
  local k = n % 64
  if k < 24 then b.b = true end
  if n % 256 >= 200 and n % 256 < 216 then b.right = false b.left = true end
  pad(b)
end, emu.eventType.endFrame)
"@
$luaFile = Join-Path $Out 'qa_test.lua'
Set-Content $luaFile $lua -Encoding ascii
$timeout = [Math]::Max(300, $list.Count * ($Ticks / 60 + 20))
& powershell -ExecutionPolicy Bypass -File $Runner -Rom (Join-Path $Out 'smb4.sfc') -Lua $luaFile -OutDir (Join-Path $Out 'qa') -TimeoutSec $timeout
if ($LASTEXITCODE -ne 0) { $rc = $LASTEXITCODE }
}
if (-not $NoParity) {
    $p = Join-Path $PSScriptRoot 'parity.ps1'
    & powershell -ExecutionPolicy Bypass -File $p -Tools $Tools -Out $Out -Runner $Runner
    if ($LASTEXITCODE -ne 0) { $rc = 1 }
}
exit $rc
