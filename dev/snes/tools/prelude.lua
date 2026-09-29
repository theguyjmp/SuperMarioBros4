-- Prelude for snesrun.ps1 (Mesen2 --testrunner; no io/os available, stdout only).
-- save(name)            -> writes OUTDIR/name.png (hex-encoded through stdout)
-- pad(buttons)          -> set controller 1 for the next frame: {right=true, a=true, b=true, ...}
-- at(frame, fn)         -> run fn at the end of that frame
-- finish(code)          -> stop the emulator with an exit code
-- readb(addr) / readw(addr) -> read S-CPU memory (addr in $7E0000.. or $0000-$1FFF work RAM)
local jobs = {}
local frame = 0
local held = {}
function save(name)
  local png = emu.takeScreenshot()
  local t = {}
  for i = 1, #png do t[i] = string.format("%02x", string.byte(png, i)) end
  print("PNG:" .. name .. ":" .. table.concat(t))
end
function pad(b) held = b or {} end
function at(f, fn) jobs[#jobs + 1] = { f = f, fn = fn } end
function finish(code) emu.stop(code or 0) end
function readb(a) return emu.read(a, emu.memType.snesMemory, false) end
function readw(a) return emu.read16(a, emu.memType.snesMemory, false) end
function curframe() return frame end
emu.addEventCallback(function() emu.setInput(held, 0) end, emu.eventType.inputPolled)
emu.addEventCallback(function()
  frame = frame + 1
  for _, j in ipairs(jobs) do if j.f == frame then j.fn() end end
end, emu.eventType.endFrame)
