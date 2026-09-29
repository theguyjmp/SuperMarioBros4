# SNES homebrew kit — notes for the next game

Everything learned building Super Mario Bros. 4 as a native SNES ROM, written so the next project can start in
an hour instead of a day. Module agents append their own "lessons" sections at the bottom.

## 1. Toolchain (Windows, no installs beyond two zips)
* `snes/tools/setup.ps1` downloads **cc65** (ca65 assembles 65816 with `--cpu 65816`; ld65 links) and **Mesen2**
  (the most accurate SNES debugger/emulator; has a headless Lua test runner).
* Mesen2 gotchas (each cost real time):
  - First launch shows a setup wizard and blocks forever → put `{}` in `mesen\settings.json` (portable mode).
  - `--testrunner` needs **absolute** ROM/script paths.
  - Lua has **no io/os** in the runner → screenshots go out as hex on stdout (`save(name)` in `prelude.lua`),
    decoded by `snesrun.ps1`. `emu.takeScreenshot()` returns PNG bytes.
  - The runner has **no controller in port 1** unless you pass `--snes.port1.type=SnesController`.
  - Capture output with `2>&1 | Out-String` inside a PowerShell job; a bare `&` call can drop it.
* ca65 gotcha: labels named like opcodes (`cop:`, `brk:`) fail with a confusing parse error.

## 2. ROM layout that scales
* LoROM + FastROM, 4 MB (`lorom.cfg` is generated: one MEMORY area per 32 KB bank). **A LoROM bank is only
  $8000-$FFFF** — never let a blob cross a bank; give big assets their own `BANKnn` segment.
* One code bank per module (`CODE1..15`) → modules never fight over space; call across banks with JSL/RTL.
* RAM via ca65 segments (`ZEROPAGE`, `BSS`, `HIBSS` $7E2000+, `EXBSS` $7F0000+, `SRAMBSS`) — never hard-code.
* Boot must not DMA-clear the stack page it's running on (clear $0000-$1DFF, then $2000+).
* `build.ps1` fixes the header checksum (sum of all bytes; complement at $7FDC).

## 3. Architecture patterns that made parallel work possible
* **Design contract first** (`DESIGN.md`): fixed VRAM/CGRAM/OAM/DMA-channel/bank ownership, calling convention
  (JSL/RTL, A8/XY16, D=0, DB=$80), module API names — then stub every module so the ROM always links.
* **Plug-in registry instead of shared dispatch code**: each entity is one file with `;@entity NAME codes=...` and a
  `NAME_vt` vtable; the build script generates the id table + spawn map. N agents add N enemies with zero edits to
  shared files.
* **Private build dirs per agent** (`build.ps1 -Out`, `snes\build.ps1 -Out snes\build-<name>`) — no clobbered exes.
* **Converter in the reference language**: the PC version's own loaders render/convert the data (levels, art, songs),
  so the ROM data is exactly what the reference game sees. Generated files only go to `<Out>/gen/`.
* **Physics parity test**: the PC reference dumps per-frame X/Y/velocities for a scripted input; a Lua script feeds
  the same input to the ROM and compares frame by frame. Catches every porting slip immediately.
* HUD on BG3 (2bpp, high priority), level on BG1, parallax on BG2 with HDMA per-band scroll + a color-0 gradient;
  "behind" sprites (piranhas in pipes) = OBJ priority below BG1 high-priority tiles — free on SNES hardware.
* Sound: SPC700 driver + BRR samples generated from the PC synth's instruments; song uploads are slow (0.2-0.65 s),
  so switch level music only during screen transitions.

## 4. Art & audio direction that worked
* Author art as text (`.art`: palettes of up to 15 colors, `#RRGGBB`, pixels 1-9 a-f) so it diffs and converts.
* Review art at **4-6x zoom**, one frame at a time; small previews hide hunchbacks, 3/4-view faces on profile bodies
  and T-pose runs. Draw the stand pose first, derive walk/run by moving only limbs, keep torso x fixed.
* Player frames may exceed the hitbox (24x36 on a 16x32 box): center horizontally, bottom-align.
* Family-friendly check: no lone vertical protrusion (raised arm/fist) that reads as suggestive.
* Never copy Nintendo levels, melodies or sprites — the owner explicitly rejected level copies; audit for signature
  layouts (SMB1 1-1 block rows, end staircases) and famous jingles (the 1-up melody slipped in once).

## 5. The exe as a ROM player
The Windows exe hosts a libretro SNES core (bsnes) and keeps the good PC features (pacing, low latency, rebinding,
scaling). See the "Frontend player" section below for how to reuse it for another `.sfc`.

---
<!-- module agents: append "## <Module> lessons" sections below -->

## Frontend player
The exe (`src/Frontend/*` + the shared `src/Platform/*` + `src/Engine/{Ppu,Font}.cs`) is a **generic libretro SNES
player** with one ROM embedded. Nothing in it is game-specific except `src/Frontend/game.ini`.

**Reuse it for another game**
1. Copy `src/Frontend`, `src/Platform`, `src/Engine/Ppu.cs`, `src/Engine/Font.cs`, `build.ps1` (drop the smb4tools part
   if you have no tools) and `tools/fetch-core.ps1`; run `tools\fetch-core.ps1` (→ `bin\cores\bsnes_libretro.dll`).
2. Write a `game.ini`: `Title`, `AppFolder` (the `%APPDATA%` folder), `RomFile` (sidecar override name), `SaveFile`,
   `Core`, `ScreenshotFolder`, `ScreenshotPrefix`, optional `CoreOption.<key>=<value>`.
3. `build.ps1 -Rom mygame.sfc -GameIni my\game.ini -Icon my\app.ico -ExeName MyGame -Out dist` → `dist\MyGame.exe`.
   Ship it with `cores\bsnes_libretro.dll` next to it (plus LICENSE-THIRD-PARTY.md: bsnes is GPLv3).
4. Check it headless: `MyGame.exe --selftest --out x` → `x\selftest.txt` (PASS/FAIL), `selftest.png`, `shot_*.png`,
   `selftest.wav`. `--rom other.sfc`, `--frames N`, `--script "100-105:START;240-300:RIGHT+Y"`, `--no-runahead`.
   Other switches: `--rom x.sfc` (play any ROM), `--core x.dll`, `--savedir d`, `--mute`.

**What it does (and the traps found building it)**
* Core loaded with LoadLibrary + GetProcAddress; all callbacks are cdecl delegates held in fields (GC!). The variadic
  log callback is declared `(int level, IntPtr fmt)` — safe on x64. Core options: `SET_VARIABLES` is parsed (we
  answer `GET_CORE_OPTIONS_VERSION` = unsupported so cores fall back to it); users change them in Options > Emulator core.
* bsnes 115 has `need_fullpath=true`: the embedded ROM is extracted to `%APPDATA%\<AppFolder>\rom\` and passed by path.
* **bsnes keeps SRAM private** (`retro_get_memory_size(SAVE_RAM)` = 0, no memory maps) and writes `<rom>.srm` into
  the save directory only on unload. The player gives it `...\core\` as save dir, copies `save.srm` there before
  loading, finds the SRAM inside the serialized state once (the header's SRAM size; the bytes it just loaded), checks
  that region every 60 frames and writes `save.srm` on change, then adopts the core's own file after unload.
  Cores that do expose SRAM are compared directly every 30 frames.
* **bsnes resets its audio stream on every `retro_unserialize`**, so classic frontend run-ahead (run, save, run, load)
  gives silence. The player uses a core's own run-ahead option when it declares one (`bsnes_run_ahead_frames`), and
  the save/load method only for other cores. The self-test proves the shown frame is N+1 (34/34 moving frames).
  Changing that option between a state save and load breaks determinism for one frame — settle a frame first.
* Audio: core rate (bsnes: 48000) → cubic resampler → ring → waveOut 48 kHz stereo. Dynamic rate control (±0.5 %)
  holds the ring at half the chosen buffer (LOW 48 / NORMAL 64 / SAFE 96 ms); after pause it rebuffers silently.
* Pacing: 60·N Hz displays lock (swap interval N, one retro_run per present, 0.16 % slow vs 60.0988 — DRC absorbs it);
  other rates vsync + wall clock at the core's fps; VRR = high-res timer. Input is polled right before retro_run.
* Menu: Esc, or hold SELECT+START 0.5 s on a pad. Drawn in the old game's 16-bit style into a 256-wide Ppu and
  composited over the dimmed frame (transparent index 0xFFFF). F5/F9 state, F11/Alt+Enter fullscreen, F12 shot, Pause.
* Default pad mapping is positional (Xbox A = SNES B, X = Y, B = A, Y = X, LB/LT = L, RB = R); "by label" preset in
  Options. Keyboard: arrows/WASD, X/K/Space = B, Z/J/LShift = Y, C/L = A, V/I = X, Q = L, E = R, Enter = Start,
  RShift/Tab = Select.

## Enemies-B lessons (fortress/airship enemies)
* **Test RAM, not pixels, for timing.** Mesen2 `--testrunner`'s `emu.takeScreenshot()` only refreshes every ~4 frames,
  so a screenshot can show a state several ticks old. Dump entity fields (type/x/y/state/vel) at exact ticks from Lua and
  diff them against the reference's per-tick object list (`smb4tools shot LEVEL out.png "W10,@x:y,R30,INFO,SNAP"`);
  use screenshots only for "does it look right" (flips, palettes, behind-BG priority).
* **Teleport tests need identical spawn semantics.** A mid-level teleport only spawns the 4 columns entering at the
  camera edge (C# `Spawner`), in both games — write `cam_x` (and the autoscroll accumulator) exactly as the reference's
  `CenterCamera` does, or the spawn windows differ. Rule of thumb: teleport to `x = spawnCol - 9` to get that column.
* **Reproduce the reference's float math with tables built by the reference's own float code.** C# `Math.Cos` on an
  accumulated `double` angle truncated with `(int)` was reproduced by generating the table in PowerShell (same .NET
  doubles); only the exact ±0.5 boundaries drift on later turns. `(uint)(t * 2654435761u) % 120` = 16x16 shift-add
  multiply (low 32 bits) + `((hi % n) * (65536 % n) + lo % n) % n` with the 16/8 hardware divider.
* **ca65:** any normal label (including `.local` ones inside a macro) ends the `@cheap` label scope — don't reference
  `@labels` across a macro that defines labels; long state machines need `jmp` hops (branch range ±127).
* **Plug-in init order matters:** `ent_spawn` runs the new object's init immediately, which may clobber the shared
  zero-page scratch — recompute scratch values after spawning.
* Invisible "controller" objects (cannons, blasters) = class SPECIAL with no draw callback; the BG tiles carry the art
  and the high-priority tile bit hides a just-fired "behind" projectile while it leaves the barrel.

## Enemies-A lessons (Buzzy, Spiny/egg, Lakitu, Cheep swim/leap, Blooper, Bob-omb + explosion, Hammer Bro + hammer)
* **Verify behaviour by RAM, not pictures.** `snes/test/ent_compare.ps1 -Level L -Name n -Route "tokens"` runs the same
  input in the C# World (`smb4tools shot ... INFO`) and in the ROM (Lua reads `ent_order/ent_type/ent_x/ent_y`) and diffs
  type + Px/Py of every entity after every token. All eight types matched the C# exactly (hundreds of ticks each,
  incl. stomp → shell, eggs hatching, leaps, pushes, fuse → carry → kick → ricochet → explosion, 80 hammer throws).
* `@x:y` teleports work on both sides (Lua writes p_x/p_y/p_inair/p_yvel + cam/prev_cam like C# `CenterCamera`), so an
  enemy deep in a level can be tested without a route; the spawner then only spawns columns cr-3..cr, so place the
  teleport so the enemy's column is one of those 4. For long routes a greedy C# search (extend by the candidate
  segment that survives and gets furthest right) finds inputs in ~1 min.
* **Pitfall:** `smb4tools shot` re-derives Pressed at every token (a jump token right after another jump token jumps
  again), the ROM pad doesn't → end jump segments with a non-jump token (`RJ20,R1`).
* **Pitfall:** Mesen `--testrunner` screenshots lag the RAM state by a variable ~5-12 frames (async video). Fine for
  looking at art; never use them to judge timing.
* Keep C# `t % P` / `sin(t*2π/P)` exact forever with a separate phase counter per period (0..P-1) instead of `t % P`
  on a 16-bit t (wraps after 18 min); precompute the C# `(int)(Math.Sin(...)*A)` values as a table (truncation
  toward zero matters; `ent_sin` rounds differently).
* C# `(uint)(t * 2246822519u) % 60` = low 32 bits of a 16x32 product, then 32-bit mod 60 as four byte steps
  `r = (r*256 + b) % 60` with the hardware divider (`ent_mod`, divisor ≤ 255).
* A custom `hit` callback must return carry clear for `D_KICK` (release of a carried object) unless it handles it;
  otherwise the default "knock off" path kills your carried enemy. Types with no custom hit get this for free.
* Tables in your code bank need `lda f:table,x` (DB = $80); `.byte <-n` for signed byte tables and sign-extend
  after the 16-bit load. Guard optional cross-type references with `.ifdef ET_X` / `.ifdef SPR_X` so the file
  assembles before the other type or art exists.

## Bosses lessons (bosses agent, 2026-09-29)
* **Assemble a new file on its own before it lands in `src/`** — the shared build links every `ent_*.s`, so one broken
  file stops every agent. A macro that defines labels (even `.local`) ends the caller's `@cheap` label scope:
  `@x` referenced across it is "undefined". Use `:+`/`:-` or label-free macros inside routines with `@labels`.
* Code against an API that doesn't exist yet with `.ifdef` on the owner's *constants* (`.ifdef RES_FORTRESS` → call
  `w_boss_clear`, else a fallback); the file keeps assembling and switches over by itself when the API appears.
* Song changes are the SNES boss-fight trap: a level-song SPC upload stalls 0.2-0.65 s. Pick the song where the screen
  is already blank — a boss init that runs during the area's initial spawn can set `area_music` before the engine
  plays it; only bosses spawned later (scrolled in) still hitch. Detect stalls in tests: `w_frame` unchanged for ≥3
  emulator frames while playing.
* Exact port of C# `(int)(dx / Math.Sqrt(dx*dx+dy*dy) * sp)` without floats: |v| = the largest v with
  v²·d² ≤ (|dx|·sp)² (32-bit integer compares); start from `n / isqrt(d²)` and step down (1-2 steps).
* Boss parity test that needs no C# changes: `smb4tools shot LEVEL out.png "@x:y,W30,U12,W1,INFO,W1,INFO,..."` prints
  every entity per tick; a Lua script prints the same from RAM; align both on the boss's first move and search a small
  offset (a mid-fight upload stall shifts where the Lua endFrame samples land). Teleport through the real door/pipe
  (C# `@` into another area skips the initial spawn); poke hit counters (`v1 = 2`) to reach defeat quickly.
* Values the engine writes and consumes in the same frame (`w_result`) are invisible to endFrame polling: use
  `emu.addMemoryCallback(fn, emu.callbackType.write, addr + 0x800000)` — FastROM code writes through the bank-$80
  mirror, so the plain `$00xxxx` address never fires.
* Lua 5.4 `>>` is a logical 64-bit shift: `s16(v) >> 4` of a negative Y prints 1152921504606846960; use `// 16`.
* A 5-digit score popup (10000) = two 4-digit BCD popups ("1000" + "0" 20 px right); add the score as 2 × $5000 BCD.


## Engine lessons (engine agent, phase 2)
**Reusable as-is for another SNES game (engine-generic):**
* `eng_obj.s` framework half: 16-bit parallel slot arrays (`ent_*`, X = slot*2), add queue + list order (C#-style
  `World.Add`/`FlushAdds`/`RemoveAll`), column/row spawn windows with spawned/killed bitmaps per area, the plug-in
  dispatch (`cb_fetch`: type*3 into the generated `ent_vtables`, `JML [e_fn]` trampoline so callbacks in any bank are
  JSL'd), default callbacks, draw passes, despawn windows. Only the collision rules (star/statue/slide/shell chain) and
  the block/item entry points are Mario-specific.
* `ent.inc` + `ENTITIES.md`: the plug-in contract (`;@entity NAME codes=...`, `ENT_VTABLE`, `ENT_ENTER`, `ENT_DRAW`,
  `ENT_SFX`) — 5 agents wrote ~40 entity types against it in parallel without touching shared files.
* `eng_save.s`: SRAM record store (N records × 2 copies, sequence number, magic/version, rotate-add checksum +
  complement; a torn write never loses the previous save). Payload layout belongs to the caller.
* `snes/test/`: `build-engine.ps1` (build that drops other agents' broken plug-in files and retries — keeps you
  testing while others are mid-edit), `parity.ps1` (reference-trace vs ROM per frame), `qa-rom.ps1` (every level,
  blind input, BRK/COP exec callback + hang/NMI-stall detection), `shots.ps1`, and the **test hook RAM var**
  (`eng_dbg_level`: tests start any level directly, independent of menus owned by other modules).
* Module hand-over without link-time coupling: the engine only links to the screens hooks when *their* generated
  include defines `SCR_HOOKS` (`.scope x / .include "gen.inc" / .endscope`, `.ifdef x::SCR_HOOKS`) — the ROM links
  before and after the other module exists. (ld65 also supports `type = weak` symbols in the config SYMBOLS section.)
**Mario-specific:** `eng_player.s` (SMB3 physics), `eng_world.s` (blocks, pipes, goal), `game.s` HUD/pause/flow,
all `ent_*.s`.
**Lessons / bugs that cost time:**
* A reused across a compare chain: `lda dir / cmp #2 / bne :+ / lda #8 / sta dy / : cmp #8 ...` — the `lda #8`
  changes A so the next `cmp` matches too (every downward pipe moved up). Load constants into X/Y, keep A for the chain.
* `ldx pad / txa / and #BTN / beq :+ / lda #hi / : sta v` stores the masked pad, not the default: when a default
  must survive a test, keep it in another register.
* DB = $80 always: tables in your own code bank need `lda f:table,x` (no long,Y mode — index with X).
* Macros that define labels (even `.local`) end the caller's `@cheap` label scope; make helper macros label-free
  (`beq *+12`) or say so.
* Clearing a debug/test RAM var in `game_init` races with a test that pokes it early (boot waits for the SPC upload)
  — tests poke repeatedly until the level runs.
* Mesen2 test runner: no `emu.write16` (write two bytes); the runner quits after ~25k emulated frames in one run
  (batch long QA runs); `emu.addMemoryCallback(fn, emu.callbackType.exec, a, a)` on the BRK/COP handler = crash detector.
* PowerShell 5: with `$ErrorActionPreference = 'Stop'`, any stderr line of a native tool (`ca65 2>$null`) throws —
  set `'Continue'` around native calls or go through `cmd /c "... 2>nul"`.
* Parity scripts: the reference trace keeps going after the ROM leaves the level (restart/next level); compare only
  while the ROM's tick counter follows the trace.

## Screens lessons / reusable parts (screens agent)
Files: `snes/src/scr_*.s` (+ `scr.inc`), converter `src/Tools/Snes/SnesScreens.cs` (scenes) + `SnesScreensGfx.cs`
(generic image → SNES converter, ROM blob packer). Every non-level screen of the PC game, natively.
**Reusable parts**
* **"Draw it with the PC code" converter** (`ScrGfx`): render any screen/layer with the PC game's own UI code into its
  indexed framebuffer, cleared to a key color = transparent (`ScrGfx.Draw`/`FromPpu`); private helpers are reached by
  reflection. Layers → one **4bpp tile pool per scene** (`BgPool`: BG1 + BG2 + small "block" maps share tiles and
  palettes; dedupe with H/V flips) with **joint palette packing**: greedy pack of every tile's color set into N×15
  colors; while it fails, merge the globally cheapest color pair (distance × smaller pixel count). Exact where colors
  fit, graceful banding where they don't. Previews: `SMB4_SCR_PREVIEW=1` writes `gen/scr_preview/*.png`.
* **Sprite sets** (`ObjSet`): images → 16x16 cells (front) + 8x8 singles (back) of OBJ VRAM, packed palettes; an image
  with **palette variants** (Mario/Luigi, lantern glow, blinking frames) is indexed by *color tuples* across variants so
  one tile set serves all and `scr_obj_pal(id, variant)` recolors at runtime (tuples > 15 get merged).
* **Scene record** (`scr_scene_load`): one call sets the whole PPU for a screen — BG CHR/palettes, OBJ set, text
  palettes, initial BG1/BG2 maps, HDMA tables. Maps/blocks are `MAP_*` ids → `scr_map_now/put/put_at` (queued).
* **Text canvas** (`scr_text.s`): BG3 as a 256x224 2bpp *pixel* canvas (896 tiles, identity tilemap) so text lands on
  any pixel like PC text: glyphs pre-baked with a 1-px drop shadow (value 1), light top rows (2) and body (3); palette
  per 8x8 cell (last writer wins); dirty tile rows uploaded by NMI within a byte budget; `PRINT/PRINTC/SB` macros,
  string builder (`sb_dec`, `sb_dec_pad`, `sb_bcd7` for BCD scores), pixel-exact `txt_clear` (DMA fast path).
  Speed ≈ 1-2k cycles/char after making it single-pass (a glyph's shadow only reaches right/down, so
  "shadow then body per glyph" = "all shadows then all glyphs").
* **Backdrops for free**: vertical gradients = HDMA on CGRAM 0; a patterned layer whose color changes per line = a
  one-color BG layer + a second HDMA writing *that* palette entry per line; drifting pattern = BG scroll.
* **Popups** (menus, messages, inventory) = pre-rendered window maps on BG2 with the priority bit: above OBJ priority 2
  (map sprites), below OBJ 3 (cursor/icons) and BG3 text. Curtain = tall (32x64) BG2 scrolled; credits = a 512-px
  pre-rendered BG2 page scrolled, clipped by HDMA on TM.
* `scr_core.s`: screen state machine with PC-style fades (INIDISP brightness 15/11/7/3/0, 3 ticks/step), menu key
  repeat (18 ticks, then every 5), lag-proof press detection (held vs last *game* frame, not NMI edges), VRAM/CGRAM job
  queues with a per-NMI byte budget (OAM 544 + ≤3.5 KB), own OAM buffer (later calls in front).
* Save slots: session struct = the payload of the engine's record store (`eng_save.s`); peek/select/new/erase.
**Lessons / bugs that cost time**
* `php … plp; rtl` restores the caller's flags: a routine that *returns* a value must set flags after `plp`
  (`cmp #0`) or every `jsl f / beq` silently tests garbage.
* Code appended at the end of a file after a `.segment "BSS"` block lands in BSS — end helper blocks with the code
  segment again (`.segment "CODEn"` + `.a16/.i16`) or check with a label/segment scan.
* NMI code must never use the main loop's temps; helpers that return values should not share temps with callers
  (a `bro_at` that wrote the caller's loop counter made a loop endless).
* The generated `.s/.inc` of your converter is assembled into *everyone's* ROM: it must assemble at all times (a
  duplicated constant broke the build for all agents for a few minutes). Gate big switches (`SCR_HOOKS`) behind an env
  var until your runtime is in the shared tree.
* The PPU shows BG row VOFS+1 on line 0 → write `y - 1` to BGnVOFS.
* ca65: macro parameter names can't be register names (`a`, `x`, `y`); `.macpack longbranch` + an auto-fix script that
  turns "Range error" branches into `jeq/jne/...` saves a lot of hand edits; no long,Y addressing (index with X).
* Mesen keeps battery RAM between test runs: wipe `emu.memType.snesSaveRam` in the test prelude. Drive UI tests with
  *conditions* (screen id, fade done, level running = `w_frame` > 20), not fixed frame numbers.
* Going live without link-time breakage: the converter emits `SCR_HOOKS` + `SCR_VER`; `scr.inc` stops with
  "rebuild your tools" when `gen/scr_ids.inc` is older than the asm needs (stale per-agent exes are the usual cause).
* Handing the PPU back: screens own a different VRAM/register layout, so `scr_level_ppu` restores the level setup
  before `eng_level_start` — and from `scr_nmi` when the engine's `eng_dbg_level` test hook is poked (the vblank
  between the poke and the next game frame), so other agents' level tests keep working from the screens title.
* Measure the NMI: an exec callback on the NMI's final `rtl` reading `emu.getState()["ppu.scanline"]` showed the
  screens NMI ending at line 256-259 of 261 with a 3.5 KB budget → budget cut to 3 KB.

## Lead lessons (bugs that reached the owner)
* `ply`/`plx`/`pla` set N/Z from the pulled value — never branch on a routine's result (`bmi`, `beq`) after restoring
  registers; re-test A first (`cmp #$8000` / `cmp #0`). This let Mario walk off the world map.
* Shared scratch variables (`mp_tmp`) across helper calls silently clobber callers' state — give lookups private
  scratch. Reproduce owner bugs with a headless Lua script that prints the relevant RAM, then fix.
* "Special" player states (goal auto-walk) must still run animation; check every state in anim/draw code.
