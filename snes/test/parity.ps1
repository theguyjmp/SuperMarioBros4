# Physics parity test (engine agent): C# World trace vs the ROM, frame by frame. See snes/TESTING.md.
# Usage: powershell -ExecutionPolicy Bypass -File snes\test\parity.ps1 -Tools <smb4tools.exe> -Out <build dir> [-Level 1-1] [-Script "..."] [-Runner snes\tools\snesrun.ps1]
param([string]$Tools = 'bin\smb4tools.exe', [string]$Out = 'snes\build', [string]$Level = '1-1',
      [string]$Script = 'W20,R180,W25,J12,W40,R60,W40,BR30,BRJ15,BR40,W20,BR40,BRJ20,BR60,BRJ20,BR30,BR60,BRJ26,BR20,BR60,BRJ26,BR20,BR40,BRJ26,BR20,BR60,BRJ26,BR20,BR60,BRJ26,BR20,BR20,BRJ26,BR20,BR40,BRJ26,BR20,BRJ20,BR10,W25,BRJ30,BR20,W25,BR40,BRJ20,W40,R20,RJ20,R20,W400',
      [string]$Runner = '', [int]$Offset = 1, [string]$Form = 'Small')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ($Runner -eq '') { $Runner = Join-Path $PSScriptRoot 'run.ps1' }   # snesrun.ps1 + a controller in port 1
$Out = (Resolve-Path $Out).Path
$trace = Join-Path $Out 'parity_trace.txt'
Push-Location $root
& $Tools trace $Level $trace $Script $Form | Out-Host
Pop-Location
# RAM symbols from the ca65/ld65 debug file (the label definition line)
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$lines = Get-Content $trace | Where-Object { $_.Trim() -ne '' }
$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine("local A={pxy=$(Sym 'p_x'),py=$(Sym 'p_y'),xv=$(Sym 'p_xvel'),yv=$(Sym 'p_yvel'),fr=$(Sym 'w_frame'),held=$(Sym 'pad_held'),mode=$(Sym 'g_mode')}")
[void]$sb.AppendLine("local T={")
foreach ($l in $lines) { $f = $l.Split(' '); [void]$sb.AppendLine("{$($f[1]),$($f[2]),$($f[3]),$($f[4]),$($f[5]),$($f[6])},") }
[void]$sb.AppendLine("}")
$formNum = @{ small = 0; big = 1; fire = 2; raccoon = 3; tanooki = 4; frog = 5; hammer = 6 }[$Form.ToLower()]
$inc = Get-Content (Join-Path $Out 'gen\levels.inc') -Raw
$lvlSym = 'LVL_' + $Level.ToUpper().Replace('-', '_')
if ($inc -notmatch "(?m)^$lvlSym = (\d+)") { throw "unknown level $Level" }
[void]$sb.AppendLine("local LEVEL=$($Matches[1])")
[void]$sb.AppendLine("local GSEL=$(Sym 'g_sel')")
[void]$sb.AppendLine("local OFF=$Offset")
[void]$sb.AppendLine("local FORM=$formNum")
[void]$sb.AppendLine("local PFORM=$(Sym 'p_form')")
[void]$sb.Append(@'
local function s16(v) if v >= 32768 then return v - 65536 end return v end
local function padfor(h)
  if not h then return {} end
  return {up=(h&1)~=0, down=(h&2)~=0, left=(h&4)~=0, right=(h&8)~=0, b=(h&16)~=0, y=(h&32)~=0}
end
local started=false local lastn=-1 local fails=0 local checked=0
-- the title needs a moment (sound driver upload): tap Start until the level runs
emu.addEventCallback(function()
  local f = curframe()
  local mode = readw(A.mode)
  if mode ~= 2 then
    if started then print("left the level at tick "..lastn); finish(4) end
    -- title -> Select (level select) -> pick LEVEL -> Start
    if mode == 1 then emu.write(GSEL, LEVEL, emu.memType.snesMemory) end
    if f % 8 < 2 then if mode == 1 then pad{start=true} else pad{select=true} end else pad{} end
    return
  end
  local n = readw(A.fr)
  if n == 0 and FORM > 0 then emu.write(PFORM, FORM, emu.memType.snesMemory) end -- start form (after pl_init, during the load)
  if n > 0 then started = true end
  if started and n ~= lastn then
    if n ~= lastn + 1 and lastn >= 0 then print("tick skip "..lastn.."->"..n) end
    lastn = n
    local t = T[n]
    if t then
      checked = n
      local x, y, xv, yv, h = readw(A.pxy), s16(readw(A.py)), s16(readw(A.xv)), s16(readw(A.yv)), readw(A.held)
      if h ~= t[1] and t[6] ~= 4 then print("INPUT DESYNC at tick "..n..": rom held "..h.." expected "..t[1]); finish(3) end
      if x ~= t[2] or y ~= t[3] or xv ~= t[4] or yv ~= t[5] then
        print(string.format("MISMATCH tick %d: rom x=%d y=%d xv=%d yv=%d | c# x=%d y=%d xv=%d yv=%d", n, x, y, xv, yv, t[2], t[3], t[4], t[5]))
        fails = fails + 1
        if fails >= 5 then finish(2) end
      end
    else
      if fails == 0 then print("PASS: "..checked.." ticks match exactly") else print("FAIL: "..fails.." mismatches") end
      finish(fails == 0 and 0 or 2)
    end
  end
  local nx = T[n + OFF]
  if nx then pad(padfor(nx[1])) else pad{} end
end, emu.eventType.endFrame)
'@)
$lua = Join-Path $Out 'parity_test.lua'
Set-Content $lua $sb.ToString() -Encoding ascii
& powershell -ExecutionPolicy Bypass -File $Runner -Rom (Join-Path $Out 'smb4.sfc') -Lua $lua -OutDir (Join-Path $Out 'parity') -TimeoutSec 300
exit $LASTEXITCODE
