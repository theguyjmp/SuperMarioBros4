# CPU profiler on top of coop.ps1 (-Coop 0 = 1P): -Events = routines whose entry splits the frame (average cycles between
# consecutive events; the frame ends at wait_nmi), -Count = routines whose calls are counted.
# Usage: powershell -File snes\test\prof.ps1 -Level 1-3 -Coop 0 -Events "draw_sprites,ent_draw" -Count "spr_meta"
param([string]$Out = 'snes\build', [string]$Level = '1-3', [int]$Coop = 0, [int]$Ticks = 600,
      [string]$Events = 'draw_sprites,ent_draw,ent_draw_effects,spr_end', [string]$Count = 'spr_meta,draw_one')
$OutP = (Resolve-Path $Out).Path
$dbg = Get-Content (Join-Path $OutP 'smb4.dbg') -Raw
function Sym($n) { if ($dbg -match "(?m)^sym\s+id=\d+,name=""$n"",[^\r\n]*?val=0x([0-9A-F]+),seg=\d+,type=lab") { return [Convert]::ToInt32($Matches[1], 16) } throw "symbol $n not found" }
$ev = (($Events + ',wait_nmi') -split ',' | ? { $_ } | % { "$_=$(Sym $_)" }) -join ', '
$cn = ($Count -split ',' | ? { $_ } | % { "$_=$(Sym $_)" }) -join ', '
$lua = @"
local PEV = {$ev}
local PCNT = {$cn}
local last_ev, last_c = nil, 0
local acc, cnt, calls = {}, {}, {}
for name, addr in pairs(PEV) do
  emu.addMemoryCallback(function()
    if not started then return end
    local c = emu.getState()["cpu.cycleCount"]
    if last_ev then local k = last_ev.."->"..name acc[k] = (acc[k] or 0) + (c - last_c) cnt[k] = (cnt[k] or 0) + 1 end
    last_ev, last_c = name, c
    if name == "wait_nmi" then last_ev = nil end
  end, emu.callbackType.exec, addr)
end
for name, addr in pairs(PCNT) do
  emu.addMemoryCallback(function() if started then calls[name] = (calls[name] or 0) + 1 end end, emu.callbackType.exec, addr)
end
function profdump()
  for k, v in pairs(acc) do print(string.format("%-34s avg %6d  n=%d", k, v // cnt[k], cnt[k])) end
  for k, v in pairs(calls) do print(string.format("calls %-20s %.1f per tick", k, v / LAST)) end
end
"@
$b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($lua))
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'coop.ps1') -Out $Out -Level $Level -Coop $Coop -Mode run -Ticks $Ticks -Shots 99999 -Inject $b64
