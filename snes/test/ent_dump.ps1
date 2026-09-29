# Entity comparison harness (enemies-A agent): runs the same scripted input in the C# World (smb4tools shot/trace) and in the ROM,
# dumps the entity list (type, Px, Py) at every INFO token and screenshots at every SNAP token on both sides.
# Script tokens = smb4tools shot syntax (R L U D J/A B + count, W30, @x:y teleport, INFO, SNAP); don't put two jump
# tokens back to back (shot re-presses at each token, the ROM pad doesn't): end jump segments with e.g. R1.
# Usage: ent_dump.ps1 -Level 1-2 -Script "W120,INFO,SNAP,W60,INFO" [-Form Small] [-Out snes\build-enA] [-Name x]
param([string]$Level = '1-1', [string]$Script = 'W60,INFO', [string]$Form = 'Small', [string]$Out = 'snes\build-enA',
      [string]$Name = '', [string]$Tools = '.agentbin\snes-enA\smb4tools.exe', [string]$OutRoot = "$env:TEMP\ent_compare")
$ErrorActionPreference = 'Stop'
$root = 'Z:\Documents\AppDevelopment\SuperMarioBros4'
Set-Location $root
$scr = $PSScriptRoot
if ($Name -eq '') { $Name = $Level }
$od = Join-Path $OutRoot $Name
New-Item -ItemType Directory -Force $od | Out-Null
$Out = (Resolve-Path $Out).Path
# tick numbers of INFO / SNAP / @x:y tokens; held bits per tick (C# Btn: U1 D2 L4 R8 A16 B32)
$tick = 0; $infos = @(); $snaps = @(); $held = @{}; $tps = @()
foreach ($t in $Script.Split(',')) {
    $u = $t.Trim().ToUpperInvariant()
    if ($u -eq 'INFO') { $infos += $tick; continue }
    if ($u -eq 'SNAP') { $snaps += $tick; continue }
    if ($u.StartsWith('@')) { $p = $u.Substring(1).Split(':'); $tps += "[$tick]={$([double]$p[0]),$([int]$p[1])}"; continue }
    $i = 0; $h = 0
    while ($i -lt $u.Length -and -not [char]::IsDigit($u[$i])) {
        switch ($u[$i]) { 'R' { $h = $h -bor 8 } 'L' { $h = $h -bor 4 } 'U' { $h = $h -bor 1 } 'D' { $h = $h -bor 2 } 'J' { $h = $h -bor 16 } 'A' { $h = $h -bor 16 } 'B' { $h = $h -bor 32 } }
        $i++
    }
    $n = 1; if ($i -lt $u.Length) { $n = [int]$u.Substring($i) }
    for ($k = 1; $k -le $n; $k++) { $held[$tick + $k] = $h }
    $tick += $n
}Write-Host "== C#"
& $Tools shot $Level (Join-Path $od 'cs.png') $Script $Form
$dbg = Get-Content (Join-Path $Out 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$names = @{}
foreach ($l in Get-Content (Join-Path $Out 'gen\ent_ids.inc')) { if ($l -match '^ET_(\w+) = (\d+)') { $names[[int]$Matches[2]] = $Matches[1] } }
$inc = Get-Content (Join-Path $Out 'gen\levels.inc') -Raw
if ($inc -notmatch ("(?m)^LVL_" + $Level.ToUpper().Replace('-', '_') + " = (\d+)")) { throw "unknown level" }
$lvl = $Matches[1]
$formNum = @{ small = 0; big = 1; fire = 2; raccoon = 3; tanooki = 4; frog = 5; hammer = 6 }[$Form.ToLower()]
$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine("local FR=$(Sym 'w_frame') local MODE=$(Sym 'g_mode') local GSEL=$(Sym 'g_sel') local PFORM=$(Sym 'p_form')")
[void]$sb.AppendLine("local ETYPE=$(Sym 'ent_type') local EX=$(Sym 'ent_x') local EY=$(Sym 'ent_y') local EN=$(Sym 'ent_n') local EO=$(Sym 'ent_order')")
[void]$sb.AppendLine("local PX=$(Sym 'p_x') local PY=$(Sym 'p_y') local CX=$(Sym 'cam_x') local CY=$(Sym 'cam_y')")
[void]$sb.AppendLine("local LEVEL=$lvl local FORM=$formNum")
[void]$sb.Append("local NAMES={")
foreach ($k in $names.Keys) { [void]$sb.Append("[$k]='$($names[$k])',") }
[void]$sb.AppendLine("}")
[void]$sb.AppendLine("local INFO={" + (($infos | ForEach-Object { "[$_]=true" }) -join ',') + "}")
[void]$sb.AppendLine("local SNAP={" + (($snaps | ForEach-Object { "[$_]=true" }) -join ',') + "}")
[void]$sb.AppendLine("local LAST=$tick")
[void]$sb.AppendLine("local T={" + (($held.Keys | Sort-Object | ForEach-Object { "[$_]=$($held[$_])" }) -join ',') + "}")
[void]$sb.AppendLine("local TP={" + ($tps -join ',') + "}")
[void]$sb.AppendLine("local INAIR=$(Sym 'p_inair') local PYV=$(Sym 'p_yvel') local AW=$(Sym 'area_w') local AH=$(Sym 'area_h') local PCX=$(Sym 'prev_cam_x') local PCY=$(Sym 'prev_cam_y')")
[void]$sb.Append(@'
local function s16(v) if v >= 32768 then return v - 65536 end return v end
local function padfor(h)
  if not h then return {} end
  return {up=(h&1)~=0, down=(h&2)~=0, left=(h&4)~=0, right=(h&8)~=0, b=(h&16)~=0, y=(h&32)~=0}
end
local started=false local lastn=-1 local nsnap=0
local function dump(n)
  print(string.format("  tick %d: player %d,%d cam=%d,%d", n, readw(PX)>>4, s16(readw(PY))>>4, readw(CX), s16(readw(CY))))
  local cnt = readw(EN)
  for i = 0, cnt - 1 do
    local s = readw(EO + 2*i)
    local ty = readw(ETYPE + s)
    print(string.format("    %s at %d,%d", NAMES[ty] or tostring(ty), readw(EX + s) >> 4, s16(readw(EY + s)) >> 4))
  end
end
emu.addEventCallback(function()
  local f = curframe()
  local mode = readw(MODE)
  if mode ~= 2 then
    if started then print("left the level at tick "..lastn); finish(4) end
    if mode == 1 then emu.write(GSEL, LEVEL, emu.memType.snesMemory) end
    if f % 8 < 2 then if mode == 1 then pad{start=true} else pad{select=true} end else pad{} end
    if f > 3000 then finish(5) end
    return
  end
  local n = readw(FR)
  if n == 0 and FORM > 0 then emu.write(PFORM, FORM, emu.memType.snesMemory) end
  if n > 0 then started = true end
  if started and n ~= lastn then
    lastn = n
    local tp = TP[n]
    if tp then
      local px = math.floor(tp[1] * 16)
      emu.write16(PX, px * 16, emu.memType.snesMemory)
      emu.write16(PY, (((tp[2] + 1) * 16 - 32) * 16) & 0xFFFF, emu.memType.snesMemory)
      emu.write16(INAIR, 1, emu.memType.snesMemory)
      emu.write16(PYV, 0, emu.memType.snesMemory)
      local cx = px + 8 - 128
      local mx = readw(AW) * 16 - 256; if mx < 0 then mx = 0 end
      if cx > mx then cx = mx end
      if cx < 0 then cx = 0 end
      local cy = (readw(AH) * 16 - 192) & 0xFFFF
      emu.write16(CX, cx, emu.memType.snesMemory) emu.write16(PCX, cx, emu.memType.snesMemory)
      emu.write16(CY, cy, emu.memType.snesMemory) emu.write16(PCY, cy, emu.memType.snesMemory)
    end
    if INFO[n] then dump(n) end
    if SNAP[n] then save("rom_"..nsnap); nsnap = nsnap + 1 end
    if n >= LAST then save("rom"); dump(n); finish(0) end
  end
  pad(padfor(T[n + 1]))
end, emu.eventType.endFrame)
'@)
$lua = Join-Path $od 'test.lua'
Set-Content $lua $sb.ToString() -Encoding ascii
Write-Host "== ROM"
& powershell -ExecutionPolicy Bypass -File "$root\snes\test\run.ps1" -Rom (Join-Path $Out 'smb4.sfc') -Lua $lua -OutDir $od -TimeoutSec 300


