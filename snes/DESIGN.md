# SUPER MARIO BROS. 4 — SNES ROM port (design contract)

Owner request 2026-09-28: "build it as a ROM so I can play it in my emulator" → a **SNES** ROM (the game is now
16-bit; NES can't show it). The Windows C# game in `src/` is the **reference implementation**: the ROM must play
the same (same SMB3 per-frame integer physics, same levels, same rules). Port logic, don't redesign it.

## Toolchain (already installed, don't reinstall)
* `snes/tools/cc65/bin/ca65.exe` (`--cpu 65816`) + `ld65.exe`; linker config `snes/lorom.cfg` (LoROM, FastROM, 2 MB,
  8 KB SRAM). Build: `powershell -ExecutionPolicy Bypass -File snes\build.ps1 -Tools <smb4tools.exe> -Out snes\build-<you>`.
  It runs `smb4tools snes-export <Out>\gen` (C# converter) then assembles `snes/src/*.s` + `<Out>/gen/*.s`, links, fixes the checksum.
* Build your private C# tools first: `powershell -ExecutionPolicy Bypass -File build.ps1 -ToolsOnly -Out .agentbin\snes-<you>`
  (never build into `bin\`). If a compile/assemble error is in a file you don't own, another agent is mid-edit: wait and retry.
* Headless emulator tests: `powershell -ExecutionPolicy Bypass -File snes\tools\snesrun.ps1 -Rom <sfc> -Lua <script> -OutDir <dir>`
  (Mesen2 test runner; see `snes/tools/prelude.lua` for `save(name)`, `pad{right=true,a=true,b=true,...}`, `at(frame,fn)`,
  `finish(code)`, `readb/readw(addr)`; `print()` goes to the console). No io/os in Lua. Look at saved PNGs with Read.
  Mesen2 Lua API reference: https://www.mesen.ca/snes (emu.read, emu.setInput, emu.getState, callbacks...).

## Machine layout (fixed — every module relies on it)
* **Mode 1**. BG1 = level foreground (4bpp), BG2 = parallax background (4bpp), BG3 = HUD (2bpp, high priority, `BGMODE=$09`),
  OBJ = sprites. Screen 256x224: **playfield = scanlines 0-191, HUD = 192-223** (32 px, BG3; sprites must not be drawn below 192).
* **VRAM (word addresses):** BG1 CHR `$0000-$2FFF` (768 tiles) · BG2 CHR `$3000-$3FFF` (256 tiles) · BG1 map `$4000` (64x32)
  · BG2 map `$4800` (32x64, same 2048 words) · BG3 CHR `$5000-$53FF` (2bpp, 128 tiles) · BG3 map `$5400` (32x32) · OBJ CHR `$6000-$7FFF`
  (`OBSEL`: base $6000, sizes 8x8/16x16 → `OBSEL=$03`; name table 2 at $7000). OBJ tiles 0-31 (first 2 tile rows) are the
  **player's dynamic frame area** (uploaded per frame); the rest are loaded per level (items/effects/enemies/boss).
* **CGRAM:** 0-31 = BG3 HUD palettes (8 x 4 colors); color 0 = backdrop (sky gradient HDMA rewrites it per line).
  BG palettes 2-5 = level foreground (BG1), 6-7 = parallax (BG2). OBJ palettes: 8 = player, 9 = items/effects/common,
  10-15 = enemies/boss for the current level (converter merges/quantizes to fit).
* **DMA channels:** 0-1 general (NMI uploads, owned by whoever runs inside NMI, sequentially), 5-7 HDMA (backgrounds module
  owns 5-7: 5 = HUD window (hides OBJ below line 192), 6 = BG2 scroll table, 7 = color-0 gradient).
* **RAM:** allocate with ca65 segments (`ZEROPAGE`, `BSS` = $0100-$1DFF, `HIBSS` = $7E2000+, `EXBSS` = $7F0000+) — the
  linker places everything; never hard-code RAM addresses. Level grids live in `EXBSS`.
* **Calling convention:** public routines are `JSL`/`RTL`, entered/exited with **A 8-bit, X/Y 16-bit**, D = 0, DB = $80.
  Save/restore anything else you change (DB when touching other banks). Code segments: `CODE`(bank $80, boot/NMI, small),
  `CODE1-3` engine+entities, `CODE4` sprites, `CODE5` backgrounds, `CODE6` sound, `CODE7` screens; data in `DATA8-15`,
  `BANK16-63` (one 32 KB LoROM bank each — a blob must never cross a bank boundary; big assets get their own bank).
* **NMI** (`main.s`, fixed): if the main loop finished the frame (`nmi_ready`), calls `spr_nmi` → `eng_nmi` → `bg_nmi`, then
  reads pads into `pad1/pad1_new` (JOY1 layout, `PAD_*` in `snes.inc`). Keep NMI work under ~2200 bytes of DMA per frame
  total (spr ~800, eng ~1000, bg ~300).
* Controls: **B = jump (A-button role), Y = run/fire/tail (B-button role)**, A also jumps, X also runs, Start = pause,
  Select = item reserve (unused v1).

## Ownership (one owner per file; talk through this doc, not by editing others' files)
| Area | Files | Owner |
|---|---|---|
| Boot/NMI/linker/build | `snes/src/main.s`, `snes.inc`, `lorom.cfg`, `build.ps1`, `tools/*` | lead (ask) |
| **Engine**: level loading, grids, BG1 streaming, camera, player physics/collision (port of `Player.cs`, `World*.cs`, `Tiles.cs`), blocks/coins/bumps, HUD (BG3), pause, death/restart, entity framework + enemies/items (port of `src/Game/Entities`), level flow | `snes/src/game.s` + any `snes/src/eng_*.s`, `src/Tools/Snes/SnesLevels.cs` | engine agent |
| **Sprites**: OAM buffer, metasprite tables (all enemy/item/boss/effect images), per-level OBJ CHR + palette sets, player frames pre-composited per form/pose (from `PlayerDraw`) + dynamic upload | `snes/src/spr.s`, `snes/src/spr_*.s`, `src/Tools/Snes/SnesSprites.cs` | sprites agent |
| **Backgrounds**: per-theme sky gradient (HDMA on color 0) + parallax (the `para.THEME.N` layers composited onto BG2, HDMA per-band horizontal scroll for depth) + underwater layers | `snes/src/bg.s`, `bg_*.s`, `src/Tools/Snes/SnesBackgrounds.cs` | backgrounds agent |
| **Sound**: SPC700 driver (8 voices, BRR samples, ADSR, echo), music + SFX converted from `data/music` / `src/Audio` | `snes/src/snd.s`, `snd_*.s`, `snes/spc/*`, `src/Tools/Snes/SnesAudio.cs` | sound agent |
| **Screens**: title, file select/SRAM saves, world map, level intro, game over, ending | `snes/src/scr_*.s`, `src/Tools/Snes/SnesScreens.cs` | screens agent (phase 2) |

Generated files go only to `<Out>/gen/` (e.g. `gen/levels.s`, `gen/spr_meta.s`, `gen/bg_themes.s`, `gen/music.s` and `.bin`
blobs included with `.incbin`). Prefix every exported symbol with your module (`eng_`, `spr_`, `bg_`, `snd_`, `scr_`).

## Module APIs (stable names; stubs exist — replace the bodies)
* **Sprites** (`spr.s`):
  `spr_nmi` (NMI: OAM DMA, dynamic player CHR) · `spr_begin` (clear OAM buffer at frame start) ·
  `spr_meta` — draw metasprite: A16 in `spr_arg_id` (metasprite id from `gen/spr_ids.inc`, e.g. `SPR_GOOMBA_1`),
  `spr_arg_x`/`spr_arg_y` (16-bit signed screen coords of the image's top-left as in the C# `ppu.Spr(img, x, y)`),
  `spr_arg_flags` (bit0 hflip, bit1 vflip, bit2 behind-BG = OBJ priority 1 so high-priority BG1 tiles cover it,
  bits 4-7 palette override 0 = default) · `spr_player` — A = form id, X = pose id (from `gen/spr_ids.inc`),
  `spr_arg_x/y`, flags → uploads that frame to OBJ tiles 0-31 in NMI and draws it · `spr_level_load` — A = level index
  (from `gen/levels.inc`) → loads that level's OBJ CHR + palettes 9-15 (called by engine during forced blank) ·
  `spr_end` (hide unused OAM entries). Metasprite ids and `spr_arg_*` variables are exported from `spr.s`.
* **Backgrounds** (`bg.s`, details in `snes/BACKGROUNDS.md`): `bg_load` — A = theme id (`gen/themes.inc`, `THEME_PLAINS`...),
  X = 1 if the area has `bg=` (flat backdrop, no parallax; store its BGR555 in `bg_flat_color` first, default 0 = black)
  → sets BG2SC ($4A: map $4800 **32x64**, same 4 KB footprint) / BG12NBA ($30) / WOBJSEL / TMW, uploads BG2 CHR/map/palettes
  6-7 (forced blank) · `bg_set_water` — X = level pixel row where underwater starts = `WaterRow*16 + (WaterRow>0 ? 16 : 0)`
  (the C# clip line below the surface tile row), $FFFF = none; call it **before** `bg_load` (if called after, it re-uploads
  the right variant, forced blank only) · **`bg_area_h`** (16-bit, exported) = area height in px (`H*16`), set before
  `bg_update`: the vertical parallax anchor is the area floor · `bg_update` — once per frame with `bg_cam_x`/`bg_cam_y`
  (16-bit, exported) = the camera · `bg_nmi` (swaps/enables HDMA 5-7) · **`bg_hdma_off`** — call before any forced-blank
  CGRAM/VRAM upload outside NMI (HDMA rewrites CGADD every line and would corrupt it); the next bg_update+NMI re-enables.
  `bg_hdma_other` (byte) = HDMAEN bits of other modules' channels (bg_nmi writes `HDMAEN = $E0 | bg_hdma_other`).
  The playfield/HUD split and the backdrop color below line 192 are the backgrounds module's HDMA job (HUD area backdrop = black;
  BG2 transparent there; OBJ hidden on lines 192-223 by window 1 via HDMA ch5 — engine: don't change WOBJSEL/TMW/WH0/WH1).
  **Engine notes:** leave the water *body* cells (rows below the surface row) transparent on BG1 — the underwater gradient and
  `para.THEME.w*` layers live on BG2/backdrop and a BG1 water tile would hide them; draw only the surface row (`water.top`).
  TM must include BG2 (bit 1). Bank claim: BANK52-BANK63 = backgrounds data.
* **Sound** (`snd.s`): `snd_init` (upload driver + samples, called once at boot) · `snd_music` — A = song id
  (`gen/music.inc`, `SONG_OVERWORLD`...; 0 = stop) · `snd_sfx` — A = sfx id (`gen/music.inc`, `SFX_JUMP`...) ·
  `snd_tempo` — A = 0 normal / 1 hurry · `snd_fade` (fade out music) · `snd_pause` — A = 1 pause/0 resume.
* **Engine** (`game.s`): `game_init`, `game_frame`, `eng_nmi`; exports `eng_cam_x/eng_cam_y` and the player state for tests:
  `eng_px, eng_py` (pixel position of the 16x32 player box as in C# `Player.Px/Py`), `eng_pxvel, eng_pyvel` (C# units:
  1/16 px per tick), documented in `snes/TESTING.md` with their RAM addresses (read them from `<Out>/smb4.map` / `.dbg`).

## Level data contract (converter → engine)
The converter (`SnesLevels.cs`) must load levels through the C# `LevelLoader`/`World` code so the runtime grids are
exactly what the Windows game sees (tile types `T`, `Content`, `Var`, hidden blocks, decor, entity spawns, links,
water row, scroll mode, theme, music, start). Per area: width/height, a **collision byte grid** (T), a content grid,
a **graphics metatile grid** (includes decor in empty cells; metatile table per theme = 4 BG1 tilemap words each with
palette + priority bit: priority 1 for solid foreground tiles such as pipes/ground/blocks so "behind" sprites like
piranhas are hidden only by them, priority 0 for decor), a spawn list, link table. Animated tiles (? block, coin,
water/lava surface, muncher, note, conveyor) animate by re-uploading CHR frames. Compress (RLE or LZ) if ROM gets tight.

## Milestones
1. **M1 (now):** boots to World 1-1, plays the whole level with Mario (physics parity with C# verified by a trace test),
   blocks, coins, power-ups (mushroom, flower + fireballs, leaf + tail/flight), Goombas, Koopas + shells, Piranhas,
   parallax + gradient, HUD, music + SFX, death/restart, goal card → next level. All 54 levels selectable via a level
   select screen (Select on title).
2. **M2:** all enemies/bosses, fortresses/airships/castle, world maps, Toad houses, saves.

## Verification each agent must do
Build the ROM, run it headless with a Lua script, and look at screenshots. The engine agent adds `smb4tools trace
<level> <script>` (C# per-frame player X/Y/XVel/YVel dump for a scripted input) and a Lua script that feeds the same
input to the ROM and compares frame by frame — **physics must match exactly** for at least 600 frames of running,
jumping and skidding on 1-1.

## API requests
* **engine → all (ROM banks):** the engine's converter (`gen/levels.s`, `gen/eng_tiles_*.s`) places level grids and per-theme BG1
  CHR/metatiles only in **`BANK40`-`BANK51`**. Please keep your data out of that range (or tell me which banks you use).
* **engine → sprites (ids in `gen/spr_ids.inc`)** — the engine draws with `spr_meta` using these names (it assembles with
  `.ifdef`, so missing ids just don't draw). One id per image **with its default palette baked in**, name = `SPR_` + art
  name uppercased, `.`→`_`; a second palette gets a suffix: `SPR_GOOMBA_1 SPR_GOOMBA_FLAT SPR_WING_1 SPR_WING_2
  SPR_KOOPA_1 SPR_KOOPA_2` (green) `SPR_KOOPA_1_RED SPR_KOOPA_2_RED SPR_SHELL_1..4` (green) `SPR_SHELL_1_RED..4_RED
  SPR_PIRANHA_1 SPR_PIRANHA_2 SPR_VENUS_1 SPR_VENUS_2 SPR_FIREBALL_1..4 SPR_MUSHROOM SPR_MUSHROOM_ONEUP SPR_FLOWER_1
  SPR_LEAF SPR_STAR SPR_COIN_1..4 SPR_PUFF_1..3 SPR_SPARKLE_1 SPR_SPARKLE_2 SPR_DEBRIS SPR_DUST SPR_TINY_0..9 SPR_TINY_UP
  SPR_GOAL_BOX SPR_CARD_MUSHROOM SPR_CARD_FLOWER SPR_CARD_STAR SPR_VINE_SPROUT SPR_SPLASH_1 SPR_SPLASH_2`.
  **Bumped blocks** (drawn as sprites for 8 ticks while the BG tile is blanked, like the C# `BumpBlock`): `SPR_BUMP_BRICK
  SPR_BUMP_USED SPR_BUMP_NOTE SPR_BUMP_WOOD` in the current level's theme palette (loaded by `spr_level_load`).
* **engine → sprites (player):** `spr_player` A = form (`FORM_SMALL=0 BIG FIRE RACCOON TANOOKI FROG HAMMER`, the C# enum
  order), X = pose id `POSE_<frame>` using the PlayerDraw frame name without the `ms.`/`mb.` prefix (`POSE_STAND POSE_WALK
  POSE_WALK1 POSE_WALK2 POSE_RUN1 POSE_RUN2 POSE_RUN3 POSE_JUMP POSE_PJUMP POSE_SKID POSE_DUCK POSE_FRONT POSE_KICK POSE_HOLD1
  POSE_HOLD2 POSE_THROW POSE_SWIM1..3 POSE_CLIMB1 POSE_CLIMB2 POSE_SLIDE POSE_SPINFRONT POSE_SPINBACK POSE_DEATH POSE_STATUE`);
  the engine passes `spr_arg_x/y` = top-left of the **16x32 player box** (C# `Px-camX, Py-camY`; you add the +16 for small
  frames) and `spr_arg_flags` bit0 = face left, bit1 = vflip, bit2 = behind, **bits 8-9 = tail pose (0 down, 1 mid, 2 up,
  3 spin-side)**, bit 10 = palette flash (star/fire flash: engine sets bits 11-12 = flash palette 0-3 as in `BodyPalette`).
* **engine → sprites (draw order):** the engine calls `spr_meta`/`spr_player` in the C# painter's order (back to front:
  behind-BG objects, enemies/items, player, effects). Please make **later calls appear in front** (e.g. fill OAM from
  the end, or reverse at `spr_end`).
* **engine → lead (snesrun.ps1):** Mesen2's test runner starts with **no controller in port 1**, so `pad{...}` does
  nothing. Adding `--snes.port1.type=SnesController` to the Mesen command line fixes it (verified; the engine uses a
  private copy of the runner with that switch until snesrun.ps1 has it).
* **sprites → all (ROM banks):** sprite data (`gen/spr_data.s`) uses only **`BANK32`-`BANK39`** (currently 35-39). Note:
  levels say 40-51/55 and backgrounds 52-59 — please keep those two ranges from overlapping.
* **sprites → engine (done / answers):** all the ids above exist (`gen/spr_ids.inc`, list in `snes/SPRITES.md`); later calls are
  drawn in front; player flags as requested with **`SPR_PFLASH = $2000` (bit 13, as game.s uses)** and `SPR_LUIGI = $4000`.
  Frog poses: `POSE_FROG_STAND/HOP1/HOP2/SWIM1-3/FRONT`. Level index = `LVL_*` (engine order), `hb` = 54.
  **Request:** call `jsl spr_area` (A = current area index) after `spr_level_load` / whenever the area changes, so the
  theme-colored ids (`SPR_BUMP_*`, `SPR_DEBRIS`, `SPR_SPLASH_*`, `SPR_GOAL_BOX`, `SPR_SEMI_*`) use that area's theme
  (default after load = the level's start area).
* **sprites → lead (priority correction):** in mode 1 the order is OBJ3 > BG1hi > BG2hi > OBJ2 > BG1lo > BG2lo > OBJ1, so normal
  sprites use **OBJ priority 3** and `SPR_BEHIND` uses **priority 2** (hidden only by high-priority BG1 tiles; still in front of
  decor and parallax) — priority 2/1 as written above would hide every sprite behind ground/pipes.
* **sprites → backgrounds (HUD):** `spr_meta` culls pieces starting at y ≥ 192, but a 16x16 piece starting at y 177-191 still
  reaches into the HUD. Please clear OBJ from `TM` (bit 4) for scanlines 192-223 in your playfield/HUD split HDMA.

## Lead notes (sound, 2026-09-28)
* Sound owns ROM banks `BANK16`-`BANK27` (5 used now). Backgrounds own `BANK52`-`BANK63`, sprites `BANK32`-`BANK39`.
* New sound API: `snd_status` and `snd_sfx` with `SFX_STOP|id` (see snes/SOUND.md).
* A level-song upload freezes ~0.2-0.65 s: call `snd_music` for level songs only during screen transitions
  (fade-out / level intro), never mid-gameplay. Star, P-switch and hurry switches are cheap (1-3 frames).
* **engine → backgrounds (FYI):** after `bg_load` the engine sets `W12SEL=$02` and `TMW=$11` so BG1 is also hidden by
  your window 1 below line 192 (when the camera is above the area floor, level tiles would otherwise show under the HUD).
