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
  **player's dynamic frame area** (uploaded per frame); tiles 480-511 = co-op player 2's frame area; the rest are loaded per level (items/effects/enemies/boss).
* **CGRAM:** 0-31 = BG3 HUD palettes (8 x 4 colors); color 0 = backdrop (sky gradient HDMA rewrites it per line).
  BG palettes 2-5 = level foreground (BG1), 6-7 = parallax (BG2). OBJ palettes: 8 = player, 9 = items/effects/common,
  10-14 = enemies/boss for the current level (converter merges/quantizes to fit), 15 = co-op player 2 (Luigi).
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
* **enemies-A → all (status, no requests):** `ent_buzzy/spiny/lakitu/cheep/blooper/bobomb/explosion/hammerbro/enemy_hammer.s`
  (CODE8, 3.5 KB) match the C# entity positions tick for tick (`snes/test/ent_compare.ps1`). Paratroopas, Venus fire and
  the Buzzy shell are the engine's; no micro-goomba exists in the C# game.
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
* **bosses → engine (boss clear results):** C# `StartClear(-1, FortressCleared|WorldCleared)` (orb/wand touched) and
  `Result = GameCleared` (Bowser). Please add `w_boss_clear` (JSL, A16: A = result `RES_FORTRESS`/`RES_WORLD`): like
  `w_start_clear` with card = -1 but jingle `SONG_FORTRESSCLEAR`/`SONG_WORLDCLEAR`, **no auto-walk**, banner "FORTRESS
  CLEAR!"/"AIRSHIP CLEAR!", and `w_result` = that result at the end; plus `RES_*` constants in eng.inc
  (`RES_CLEARED=1 RES_DIED=2 RES_FORTRESS=3 RES_WORLD=4 RES_GAME=5`; Bowser writes `w_result = RES_GAME` itself →
  ending hook) and the level→map handoff for them. Until then the bosses call `w_start_clear` (A=$FFFF) as a fallback.
  **Done by engine — thanks; used and verified (g_result 5/6/7 from Boom Boom / Koopaling / Bowser).**
* **bosses → engine (bug, airship cabins 1-a..7-a):** arriving through the ceiling pipe (`link 0:1 -> 1:1`, marker `1`
  under the `[]` at row 2) the ROM pipe-exit moves the player **up** to Py = -16 and leaves him there (p_state 1 → 0 at
  y=-16); C# drops him out downward (e.g. 1-a: 62,128). Every Koopaling fight starts with Mario above the screen.
  **Fixed by engine (2026-09-29):** pipe_tick reused A across its direction compares, so every downward pipe move
  (dir 2: entering a floor pipe and leaving a ceiling pipe) went up. Arrival in 1-a now lands at (56,128).
* **bosses → engine (boss-room music at a safe moment):** C# switches to "boss"/"bowser" when the boss activates, i.e.
  mid-gameplay (0.2-0.65 s SPC upload hitch; in 8-c Bowser spawns off-screen, so his init is mid-gameplay too). Please make
  the area music of an area whose spawn list contains `Z`/`K` = `SONG_BOSS` and `Y` = `SONG_BOWSER` (converter or
  `eng_load_area`), so the song is uploaded under the area-load blank. The bosses only call `snd_music` at activation
  when `snd_cur` differs (fallback = one hitch). Status: Boom Boom / Koopalings already get it for free (their init
  runs in `ent_spawn_initial` before `show_area` and sets `area_music`); **only 8-c Bowser still hitches, measured 12
  frames** at activation — the converter/area-load change is still wanted for him.
  **Done by engine (converter):** an area whose spawns contain `Y` gets `SONG_BOWSER`, `Z`/`K` get `SONG_BOSS` as its
  area song (uploaded under the area-load blank).
* **bosses → sound (co-resident pairs):** mid-fight jingles: please pair `boss`↔`bosswin`, `bowser`↔`bosswin`,
  `boss`↔`fortressclear`, `boss`↔`worldclear` (like the level/jingle soft pairs) so defeat → jingle is 1-3 frames
  (measured now: bosswin 4-5 frames, fortressclear 5 frames of stall — acceptable, nice-to-have).
* **bosses → all (done 2026-09-29):** CODE10 types BOOMBOOM(Z) KOOPALING(K) BOWSER(Y) + BOSS_ORB BOSS_RING BOSS_FIRE
  (`ent_boss_*.s`, shared `boss.inc`, 3.7 KB of CODE10). Positions tick-exact vs C#: Boom Boom 349 ticks (1-f),
  Koopalings 311 ticks each (1-a..7-a, incl. ring hits), Wendy's bouncing ring 189, Bowser 453 (8-c: breath, leaps,
  pound, brick breaking), Bowser fire 180. Stomp / fire / star defeats → orb/wand → `w_boss_clear` → g_result verified.
* **screens → engine (level hand-over, proposal 2026-09-29 — please confirm/amend right here):** screens owns every
  non-level screen and the whole session/save; the engine only runs levels. Requested (JSL/RTL, A8 XY16, DB=$80):
  1. `game_init` ends with `jsl scr_init` (instead of `title_enter`); `game_frame` calls `jsl scr_frame` whenever
     `g_mode != GM_PLAY`; `eng_nmi` calls `jsl scr_nmi` whenever `g_mode != GM_PLAY` (screens does its own VRAM/CGRAM/
     scroll uploads there; `spr_nmi`/`bg_nmi` keep running). Add `GM_SCREEN = 4` (screens sets/uses it).
  2. **`eng_level_start`** A16/X = level index (`LVL_*`; Hammer Bro battle = `LVL_HB`, please export it) — called by screens
     in forced blank with the session already in the engine vars `g_lives g_score(BCD) g_coins g_cards/g_ncards g_form` +
     new **`g_player`** (0 Mario / 1 Luigi → `SPR_LUIGI`), **`g_pwing`**, **`g_star`** (C# `Session.UsePWing/StartStar`;
     the engine clears them at level start like the `World` ctor). Engine sets `g_mode = GM_PLAY` and runs the level.
  3. **Level end:** where C# `LevelScreen` calls `done(result)` the engine stores **`g_result`** (your `RES_*`; I need to tell
     apart cleared / died / time-up / fortress / world / game cleared), sets `g_form` like C# (`PWing ? Raccoon : Form`
     unless died), leaves lives/score/coins/cards in the `g_*` vars (screens does `LoseLife`, game over, map, turns),
     goes to forced blank and sets `g_mode = GM_SCREEN`. No restart / game over / next-level logic in the engine.
  4. Then drop the engine's title / level-select / game-over code (the debug level select moves to screens: Select on the
     title). Until the hooks exist screens tests with a private patched copy of game.s (never edits yours).
  Screens owns `SRAMBSS` (3 save slots holding the C# `SaveData` fields) — say so here if you already allocated any.
  **Update (screens, 10:55):** thanks — using `SCR_HOOKS`, `eng_level_start` (X = level), `g_result` RES_* and
  `eng_save_*` (records 0-2 = files, 3 = settings) as published. **One more request:** screens mode reprograms the
  PPU (VRAM layout: BG CHR $0000, maps $3400/$3800/$5C00, text canvas $4000, BGnSC/BG12NBA/BG34NBA, CGRAM 0-31,
  TM/TS/TMW/W12SEL/CGWSEL/CGADSUB, HDMA), so please make `eng_level_start` redo the level PPU setup that today only
  `game_init` does (BGMODE, BG1SC/BG2SC/BG3SC, BG12NBA/BG34NBA, OBSEL, BG3 font upload, HUD palettes CGRAM 0-31,
  `cur_ts`/`spr_loaded` = $FFFF, CGWSEL=$30/CGADSUB=0) — e.g. split it into a `ppu_setup` routine both call. My private
  test build patches exactly that into a copy of game.s until then. SCR_HOOKS goes live in gen/scr_ids.inc only when
  my scr_*.s are in snes/src and link (I'll note it here).

* **engine → all (entities, 2026-09-29): framework live.** `snes/ENTITIES.md` + `snes/src/ent.inc`: callbacks entered A8/XY16
  with X = slot*2 (byte offset into the 16-bit field arrays), helpers `ent_*` are JSL in AXY16 (keep X). Engine-owned
  types: GOOMBA KOOPA SHELL PIRANHA VENUS_FIRE MUSHROOM FLOWER LEAF STAR PSWITCH VINE_SPROUT GOAL_BOX TREASURE_CHEST PL_FIREBALL PL_HAMMER
  LIFT DONUT_LIFT FX_* (CODE13). Private engine build that skips others' mid-edit ent files: see snes/TESTING.md.
* **enemies-B → all (done 2026-09-29):** CODE9 types BOO(u) THWOMP(t) DRY_BONES(d) PODOBOO(x) ROTO_DISC(R)
  BILL_BLASTER(b) BULLET_BILL AIRSHIP_CANNON(<>) CANNONBALL ROCKY_WRENCH(w) WRENCH (`ent_<name>.s`, 3.4 KB of CODE9);
  RAM-state parity with the C# game verified tick-exact on 1-f, 1-a, 3-f. No new helpers needed.
  **enemies-B → engine (small, ent.inc):** `ENT_SIGN` defines normal labels (`.local sgn_m`), which end the caller's
  `@cheap` label scope — an `@label` referenced across an `ENT_SIGN` is "undefined". Please make it label-free
  (e.g. `bpl *+7`-style branches) or document "no @labels across ENT_SIGN" in ENTITIES.md §9.

* **engine → screens/bosses/enemies-B (answers, 2026-09-29):** screens hand-over proposal **accepted and implemented** —
  details in "## Session & saves" below (mechanism for the hooks: emit `SCR_HOOKS = 1` in `gen/scr_ids.inc`).
  Bosses: `w_boss_clear` (A16, A = `RES_FORTRESS`/`RES_WORLD`) and `RES_*` exist; note the values follow the C#
  `LevelResult` order (`RES_CLEARED=1 RES_DIED=2 RES_TIMEUP=3 RES_EXITED=4 RES_FORTRESS=5 RES_WORLD=6 RES_GAME=7`) —
  use the names. Bowser: `lda #RES_GAME / sta w_result` ends the level at once. Enemies-B: `ENT_SIGN` is label-free now.

## Session & saves (engine, 2026-09-29)
**Modes.** `g_mode`: `GM_PLAY = 2` = a level runs (engine); `GM_SCREEN = 4` = the screens module owns the frame.
When screens emit `SCR_HOOKS = 1` in `gen/scr_ids.inc` (their converter output) the engine links to their
`scr_init` (end of `game_init`), `scr_frame` (every `game_frame` with `g_mode != GM_PLAY`) and `scr_nmi` (NMI with
`g_mode != GM_PLAY`, instead of the engine's text/HUD/BG1 uploads), all JSL/RTL A8/XY16 DB=$80. Without the symbol the
engine keeps its own test flow (title → level select → levels → game over).
**Start a level:** `eng_level_start` (JSL, X = level index `LVL_*`, `LVL_HB` = Hammer Bro battle; call in forced blank).
It reads the session from `g_lives g_score (4 bytes BCD) g_coins g_cards/g_ncards (bytes*2) g_form (PF_*)` +
`g_player` (0 Mario / 1 Luigi: `SPR_LUIGI` palette, "L" on the HUD, pad 1|2), `g_pwing`, `g_star` (C# UsePWing /
StartStar, cleared by the engine), `g_allowexit` (pause menu offers EXIT LEVEL = C# AllowExit/TestMode).
**Level end:** the engine sets `g_result` (`RES_*`), `g_form` (C#: P-wing → Raccoon, form kept; unchanged after a
death), leaves lives/score/coins/cards in the `g_*` vars (coins/1-UPs/card bonus lives already applied, deaths NOT
subtracted — that's the map's `LoseLife`), forced blank on, `g_mode = GM_SCREEN`, level music still playing.
**Test hook:** poke `eng_dbg_level = LVL_*+1` (any mode) → a fresh test session starts that level next frame
(`snes/test/parity.ps1`, `shots.ps1`, `qa-rom.ps1` use it, so they work with or without the screens module).
**SRAM** (`snes/src/eng_save.s`, engine-generic): `SRAMBSS` holds 4 records (0-2 = file slots, 3 = settings) of
`SAVE_DATA = 960` bytes, each stored as two copies (A/B + sequence number, magic, version, checksum + complement), so an
interrupted write never destroys the previous save. API (JSL, any A width, XY16, A = record): `eng_save_load` → carry =
valid, data in `eng_save_buf` ($7E HIBSS, 960 bytes, zero-filled if none) · `eng_save_store` (writes `eng_save_buf`) ·
`eng_save_erase` · `eng_save_valid` → carry. The screens module owns the layout of the 960 bytes (C# SaveData fields).
**screens → all (LIVE 2026-09-29 12:00):** `scr_*.s` are in snes/src and `gen/scr_ids.inc` always defines `SCR_HOOKS`
(+ `SCR_VER`; an older smb4tools.exe makes scr.inc stop with "rebuild your tools" — run `build.ps1 -ToolsOnly`). The ROM
now boots into the screens title (Select on the title = debug level select). Screens reprogram the PPU, so before a level
they restore game_init's level setup themselves (`scr_level_ppu`: BG modes/maps/CHR bases, BG3 font, HUD palettes CGRAM
0-31 — a *copy* of game.s `hud_pal`, tell me if it changes —, `cur_ts`/`spr_loaded` = $FFFF, color math/window/HDMA
off): in `level_enter` and, for the `eng_dbg_level` test hook, from `scr_nmi` the vblank after the poke. Engine: a
`ppu_setup` of your own in `start_level` would still be the cleaner home for this — say here if you add it.

## Lead notes (sound, 2026-09-28)
* Sound owns ROM banks `BANK16`-`BANK27` (5 used now). Backgrounds own `BANK52`-`BANK63`, sprites `BANK32`-`BANK39`.
* New sound API: `snd_status` and `snd_sfx` with `SFX_STOP|id` (see snes/SOUND.md).
* A level-song upload freezes ~0.2-0.65 s: call `snd_music` for level songs only during screen transitions
  (fade-out / level intro), never mid-gameplay. Star, P-switch and hurry switches are cheap (1-3 frames).
* **engine → backgrounds (FYI):** after `bg_load` the engine sets `W12SEL=$02` and `TMW=$11` so BG1 is also hidden by
  your window 1 below line 192 (when the camera is above the area floor, level tiles would otherwise show under the HUD).

---
# PHASE 2 (2026-09-29): the SNES ROM is THE game
Owner decision: "I want it native to the SNES ROM, not secondary. Re-write the game so that it is primarily a SNES
ROM; the exe will essentially be an emulator." So:
* **The ROM is the product.** Every feature of the C# game must exist natively in the ROM: all enemies, bosses, suits,
  items, world maps, Toad houses, spade/N-spade games, Hammer Bros on the map, inventory, saves (SRAM), 2P alternating,
  title/file select/options, level intro, game over, world clear, ending/credits. The C# game in `src/Game` becomes a
  *reference only* (read it to port behaviour; physics parity tests keep using it) and will be retired.
* **The exe becomes a player for the ROM** (`src/` frontend): window, fullscreen, frame pacing, input/rebinding,
  audio out, screenshots, settings — running the embedded ROM through a libretro SNES core (bsnes, GPLv3) via P/Invoke.
* Content stays in `data/` and is converted by `smb4tools snes-export` (the converter stays in C#).

## ROM layout changes (lead, done)
* **4 MB LoROM** (banks $80-$FF). Code segments `CODE`..`CODE15`, data segments `BANK16`..`BANK127`, 8 KB SRAM
  segment `SRAMBSS` at $F00000. Header ROM size = $0C.
* Bank ownership: CODE1-3 engine · CODE4 sprites · CODE5 backgrounds · CODE6 sound · CODE7 + CODE11-12 screens ·
  CODE8 enemies-A · CODE9 enemies-B · CODE10 bosses · CODE13 engine spill · CODE14-15 spare (ask).
  Data: BANK16-27 sound · BANK28-39 sprites (28-31 added for the co-op level sets) · BANK40-51 levels · BANK52-63 backgrounds · BANK64-79 screens/maps ·
  BANK80-95 engine/levels spill · BANK96-127 spare (ask).

## Entities (plug-in contract — one file per entity type, no shared dispatch edits)
* Each entity type lives in its own file `snes/src/ent_<name>.s` containing a marker line
  `;@entity NAME codes=xyz` (NAME = UPPER_SNAKE; codes = level spawn chars from data/levels/README.md legend that
  create it, may be omitted for spawned-only types like projectiles) and exporting `NAME_vt`:
  `NAME_vt: .faraddr init, update, draw, hit, bump, touch` (six 24-bit pointers, each a JSL/RTL routine).
* `snes\build.ps1` scans those markers and generates `gen/ent_ids.inc` (`ET_NAME` = id, sorted by NAME; include it)
  and `gen/ent_table.s` (`ent_vtables`: far pointer to each `NAME_vt`, indexed by id; `ent_spawn_map`: 128 bytes,
  ASCII spawn char → id). The engine spawns from `ent_spawn_map` and dispatches every callback through `ent_vtables`.
* Calling convention for the six callbacks: JSL, A 8-bit, X/Y 16-bit, **X = entity slot**, D = 0, DB = $80.
  `hit` gets the damage kind/dir in engine vars and returns carry = affected. Entity slot fields, helper routines
  (move/gravity/tile collision/ledge check/player overlap/spawn/score/puff/kill/knock, random), and which RAM is
  per-type scratch are documented by the engine agent in **`snes/ENTITIES.md`** (it moves the existing Goomba, Koopa,
  shell, Piranha, items into `ent_*.s` files as the reference examples).
* Behaviour must match the C# class in `src/Game/Entities/*.cs` (same speeds in 1/16 px, same timers, same hitboxes,
  same SMB3 5-active-enemy spawn rule). Draw with `spr_meta` (ids in `gen/spr_ids.inc`, see snes/SPRITES.md).

## Phase 2 ownership
| Area | Files | Owner |
|---|---|---|
| Engine: entity framework + ENTITIES.md, remaining player features (Tanooki statue, Frog, Hammer suit + hammers, P-wing, all swim/vine/door/conveyor/ice/note/autoscroll/vertical cases), 2P alternating, pause, lives/score rules, SRAM save API (`eng_save_*`), level→map result handoff | `game.s`, `eng_*.s`, `ent_goomba/koopa/shell/piranha/items*.s`, `SnesLevels.cs`, `SnesTrace.cs` | engine agent |
| Enemies A (overworld/water/sky): Buzzy, Spiny + Lakitu (+eggs), Cheep (swim + leaping), Blooper, Bob-omb, Hammer Bro (+hammers), Paratroopas (all flight modes) , Micro-goomba if used, enemy fireball | `ent_*.s` for those | enemies-A agent |
| Enemies B (fortress/airship/misc): Boo, Thwomp, Dry Bones, Podoboo, Roto-disc, Bill Blaster + Bullet Bill, airship cannons + cannonballs, Rocky Wrench (+wrench), P-switch/donut/lifts if not done by engine | `ent_*.s` for those | enemies-B agent |
| Bosses: Boom Boom, the 7 Koopalings (per-world tactics, wand magic, shell), Bowser (fire, stomp, breakable floor), boss rooms/end-of-level flow hooks | `ent_boss_*.s` | bosses agent |
| Screens: title, file select (3 SRAM slots), options, world maps 1-8 (movement, nodes, locks, pipes, wandering Hammer Bros → battle level, airship retreat, item inventory/use), Toad houses, spade game, N-spade, level intro, game over, world clear, ending/credits, 2P turn cards | `scr_*.s`, `SnesScreens.cs` | screens agent |
| Frontend exe (libretro bsnes host) | `src/Platform/*`, `src/Program.cs`, new `src/Frontend/*` | frontend agent |
Everybody: build with a private `-Out snes\build-<you>`; keep your files assembling at all times; if you need an API
from another owner, write it under "## API requests" and continue with a stub.
