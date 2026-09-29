# SNES engine — testing (engine agent)

## Physics parity test (C# World vs ROM, frame by frame)
```
powershell -ExecutionPolicy Bypass -File build.ps1 -ToolsOnly -Out .agentbin\snes-engine
powershell -ExecutionPolicy Bypass -File snes\build.ps1 -Tools .agentbin\snes-engine\smb4tools.exe -Out snes\build-engine
powershell -ExecutionPolicy Bypass -File snes\test\parity.ps1 -Tools .agentbin\snes-engine\smb4tools.exe -Out snes\build-engine [-Level 1-1] [-Form Small] [-Script "..."] [-Runner <runner.ps1>]
```
1. `smb4tools trace LEVEL OUT.txt SCRIPT [FORM]` (src/Tools/Snes/SnesTrace.cs) runs the C# `World` and writes one line per
   tick: `tick held X Y XVel YVel state form` (X/Y/vel in the game's 1/16-px units, held = C# `Btn` bits).
   SCRIPT = the `shot` token syntax (`R30` right 30 ticks, `BR60` run right, `J`/`A` jump, `L` `U` `D`, `W30` wait);
   Pressed is derived continuously (like a real pad), the same rule the ROM applies to the SNES pad.
2. parity.ps1 turns the trace into `<Out>\parity_test.lua`: it picks the level through the level select (writes `g_sel`),
   sets the start form (`p_form`, poked while the level loads), feeds each tick's buttons (C# A = SNES B, C# B = SNES Y),
   and after every tick compares `p_x p_y p_xvel p_yvel` with the trace; it also checks the ROM saw the intended pad bits
   (`pad_held`, skipped while the course-clear auto-walk overrides it). Output: `PASS: n ticks match exactly` or the first
   mismatches.
3. The default script is a full 1-1 run (2004 ticks): ? block → mushroom → grow, stomps, getting hit (shrink), slopes,
   pits, skids, P-meter, goal box, auto-walk and time tally.

Results (2026-09-28): 1-1 default route **exact until the ROM leaves the level** (1925 ticks: the ROM ends the course
clear when the SPC reports the jingle finished; the C# tool never does, so its trace just continues); 1-1 as Fire
(fireballs) 597/597; 1-1 as Raccoon (tail, flutter, hurt -> poof) 698/698; 1-2 721/721; 1-4 (moving/donut lifts,
autoscroll) 736/736; 2-1 689/689; 7-1 481/481; 1-3 335/335 and 3-1 699/699 (then an enemy kills Mario in C#; in 3-1 it
is a Cheep Cheep, not ported in M1, so the runs diverge there).

**Runner note:** Mesen2's test runner has no controller in port 1 unless started with `--snes.port1.type=SnesController`
(requested for `snes/tools/snesrun.ps1`, see DESIGN.md API requests). parity.ps1 uses `snes/test/run.ps1` (snesrun.ps1 + that switch) unless `-Runner` is given.
The title waits for the sound driver upload (~45 frames), so tests tap Start/Select until `g_mode` changes.

## RAM symbols (from `<Out>\smb4.dbg`; addresses of the current build, they move when code changes)
| symbol | meaning |
|---|---|
| `p_x`, `p_y` | C# `Player.X/Y` (1/16 px; X unsigned, Y signed) — `eng_px`/`eng_py` = Px/Py after each tick |
| `p_xvel`, `p_yvel` | C# `XVel/YVel` (signed, 1/16 px per tick); aliases `eng_pxvel`/`eng_pyvel` |
| `p_form`, `p_state`, `p_power`, `p_inair` ... | every C# `Player` field as `p_<name>` (16-bit) |
| `w_frame` | C# `World.Frame` (ticks of the current level; tick n of the trace = `w_frame == n`) |
| `cam_x`, `cam_y` | C# `CamX/CamY` (exported as `eng_cam_x/eng_cam_y`) |
| `g_mode` | 0 title, 1 level select, 2 playing, 3 game over |
| `g_level`, `g_sel` | current level index (`LVL_*` in `gen/levels.inc`), level-select cursor |
| `pad_held`, `pad_pressed` | the tick's pad in C# `Btn` bits |
| `lvl_tiles`, `lvl_gfx` | $7F: runtime tile-type grid (C# `Tiles`) and BG1 metatile grid, `index = y*area_w + x` |

Example (current build): `p_x=$0907 p_y=$0909 p_xvel=$090B p_yvel=$090D w_frame=$0999 cam_x=$02A5 g_mode=$09E3`.

## Screenshots
Any Lua with `save(name)` through the runner; `snes\build-engine\parity_test.lua` can be edited to `save()` at chosen ticks.
