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
2. parity.ps1 turns the trace into `<Out>\parity_test.lua`: it starts the level through the test hook `eng_dbg_level`,
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

## Engine test tools (phase 2, 2026-09-29) — all in `snes/test/`
| script | what |
|---|---|
| `build-engine.ps1 -Tools <exe> -Out <dir> [-NoExport]` | like `snes\build.ps1`, but other agents' `ent_*.s` / `scr_*` files that don't assemble or link right now are left out (listed as "skipped") instead of failing the build |
| `parity.ps1 ... [-Level] [-Form] [-Script]` | C# trace vs ROM frame by frame (above) |
| `parity-suite.ps1 -Tools <exe> -Out <dir> [-Only 3-1]` | parity for one level per theme (plains 1-1, underground 1-2, sky 1-4/5-1, desert 2-1, sea 3-1, jungle 4-1, ice 6-1, machine 7-1, volcano 8-2, fortress 1-f, airship 1-a, castle 8-c) + Fire, Big swimming, Frog swimming, Raccoon P-run + flight, Tanooki statue |
| `qa-rom.ps1 -Tools <exe> -Out <dir> [-Ticks 900] [-Levels 1-1,hb] [-NoParity] [-NoShots]` | boots **every level** (55 incl. `hb`), plays it blind (run right + jump) and fails on BRK/COP (crash), main-loop hang (level tick stalls 180 frames), NMI stall, level never starting; PNG per level in `<Out>\qa\`; then the 1-1 parity test. Batches of 12 levels per Mesen run (the test runner quits after ~25k frames) |
| `shots.ps1 -Out <dir> -Level 3-3 [-Form 5] [-Hold right,up] [-JumpEvery 20] [-TapY 16] [-Shots 60,160] [-Poke "p_star=500"] [-Tag _x]` | screenshots at given level ticks with scripted input |

All of them start levels through the **test hook `eng_dbg_level`** (poke `LVL_*+1`, any mode; the engine starts
that level with a fresh session next frame), so they don't depend on the title/level-select/screens code.
Results 2026-09-29: parity suite 17/17 (1-1 full route 1925 ticks; others until the script ends or Mario dies at
the same tick in both); QA 55/55 levels (no crash/hang).

## 2-player co-op (2026-10-03)
`snes\test\coop.ps1 [-Level 1-1] -Mode run|lives|heads|pipe [-Ticks 600] [-Shots 60,300]` starts a level with `g_coop = 1`
(Mario pad 1, Luigi pad 2; the runners now plug a controller into port 2 too, `pad2{...}` in Lua) and logs both players,
`co_cur` (who is in the `p_*` slot), shared `g_lives` and `co_st` (out flags). `lives` walks Luigi into the enemies:
4 -> 3 -> 2 -> 1 -> 0 (out), then Mario's death ends the level (the map takes that last life). Mesen2 quirk: controller 2
is `emu.setInput(t, 0, 1)` in this build, not `(t, 1)`.
`snes\test\menuflow.ps1 -Players 1|2` plays the real path from power-on (title -> 1/2 PLAYER GAME -> file 1 -> map -> first
level) and prints lag frames; `coop.ps1 -Coop 0|1` also prints lag frames and the average scanline where the frame's work
ended (CPU headroom). 2026-10-03: 1P identical to d455e7a (1-3: 5 lag / 600 ticks, avg line 83); 2P 1-3: 29 lag, avg 145.
Sprite-draw speedup (2026-10-03): unrolled OAM clear/pack, one-sweep entity draw order. `prof.ps1` splits the frame by
routine entry (cycles). 1-3 run, 600 ticks: 1P 5 lag / avg end line 70 (d455e7a: 5 / 83); 2P 13 / 132. Menu path
(`menuflow.ps1 -Levels 1-1,1-3,4-1 -Ticks 500`, mostly load/transition stalls): d455e7a 41/25/34, now 1P 41/26/35, 2P 41/25/34.
