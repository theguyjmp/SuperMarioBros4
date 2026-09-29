# 02 — Engine & Technical Architecture

> How the game is built so that it *can* feel like SMB3: a deterministic 60 Hz simulation,
> integer physics, an NES-style renderer, a 2A03-style sound engine, and an input→photon path
> with as little latency as Windows allows.
> Physics rules and constants live in [01-game-feel-and-physics.md](01-game-feel-and-physics.md).

---

## 1. Non-negotiable technical requirements

| # | Requirement | Why it matters for feel |
|---|-------------|-------------------------|
| T1 | **Fixed 60 Hz simulation tick**, one tick = one NES frame. Every physics constant is "per tick". | SMB3's feel *is* its per-frame integer math. Variable timesteps make jumps inconsistent. |
| T2 | **Integer / fixed-point gameplay math only** (no floats anywhere in `game/`). | Bit-exact reproducibility of SMB3 arcs; deterministic replays; no drift. |
| T3 | **Input sampled immediately before the tick that consumes it**; presses shorter than a tick are never lost. | Low latency; tap-jumps always register. |
| T4 | **≤ 1 queued frame** between simulation and display (flip-model swapchain, max frame latency 1, waitable object). | Removes the 2–3 frames of hidden latency typical of default render loops. |
| T5 | **Zero hitches in gameplay**: no heap allocation, file I/O, shader compilation, or logging to disk during a level. | A single dropped frame during a jump is felt immediately. |
| T6 | **Pixel-exact 256×240 output**, palette-indexed, scaled on the GPU. | Authentic NES look; sprites never shimmer or blur. |
| T7 | **Deterministic `Game` core with no platform calls** — `tick(state, input) → state'`. | Replays, rewind, frame-advance, headless tests, trace comparison against SMB3. |

---

## 2. Technology choices

### 2.1 Decision

**C++20 · CMake · MSVC · SDL3 (platform) · Direct3D 11 (presentation) · custom software "virtual PPU" · custom 2A03 APU synth · Dear ImGui (dev builds only).**

| Concern | Choice | Notes |
|---|---|---|
| Language | C++20 (MSVC, `/W4 /permissive-`) | Full control of memory, timing and fixed-point math. |
| Build | CMake ≥ 3.28 + Ninja via `CMakePresets.json`; deps via CMake `FetchContent` pinned to tags | Visual Studio ships CMake + Ninja. No vcpkg needed. |
| Window/events/gamepads/audio device | **SDL3** (zlib license) | Hot-plug, broad controller support (Xbox, DualSense, Switch Pro, 8BitDo), scancodes, WASAPI audio, high-res timers. |
| GPU presentation | **Direct3D 11 + DXGI** (flip model), using the HWND from SDL | Needed for waitable swapchains, max-frame-latency 1, tearing/VRR control. ~400 lines. |
| Rendering of the game image | **CPU "virtual PPU"** writing a 256×240 8-bit indexed framebuffer | 61,440 pixels — trivially fast. Gives exact NES layering/priority/palette effects. |
| Audio synthesis | Custom **2A03 APU** emulation + band-limited step synthesis + music/SFX driver | Authentic sound and authentic SFX "channel stealing". |
| Dev UI | **Dear ImGui** (MIT) | Tuning panels, debug overlays, editor helpers. Compiled out of release. |
| Images (tools) | stb_image / stb_image_write (public domain) | Only in asset tools and dev hot-reload. |
| JSON (tools, settings) | nlohmann/json (MIT) | Runtime game data is binary. |
| Tests | doctest (MIT) | Unit + replay regression + parity traces. |
| Profiling | Tracy (BSD), dev only | Frame timing, pacing investigations. |

### 2.2 Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Godot 4** | Editor, fast content iteration | Would ignore its physics anyway; less control over frame pacing/latency; float-based transforms | Viable, but fights T1–T4, T7 |
| **MonoGame (C#)** | Productive, mature | GC pauses must be managed; less control of swapchain latency | Viable second choice |
| **Unity** | Tooling | Heavy, float physics, latency tuning is opaque | Not recommended |
| **SDL3 renderer only (no D3D11)** | Less code | No waitable swapchain / explicit frame-latency control | Acceptable for Milestone 0 only |

### 2.3 Machine setup (this PC currently has no compiler, CMake or git)

1. **Visual Studio 2022 Community (or newer)** → workload **"Desktop development with C++"** (includes MSVC, Windows SDK, CMake, Ninja).
2. **Git for Windows**; create the repo and make the first commit (the project folder is currently empty and not a git repo).
3. Content tools (all free unless noted): **Tiled** (levels/world maps), **Aseprite** (paid) or **LibreSprite** (free) for pixel art, **FamiStudio** (music/SFX), **Mesen2** (reference emulator with Lua scripting, for feel-parity traces), **RenderDoc** + **PresentMon** (GPU/frame-pacing diagnostics).
4. Optional: VS Code + CMake Tools if you prefer it over the Visual Studio IDE.

> **This laptop specifically:** it has an RTX 4080 Laptop GPU plus an Intel iGPU. Windows reports
> the 2560×1600 **240 Hz** internal panel on the Intel adapter and a 2560×1440 **144 Hz** display on the
> NVIDIA adapter. The presenter must render on **the adapter that owns the monitor the window is on**
> (see §5.6); cross-adapter copies add latency and can break "independent flip". The game's GPU load
> is < 1 ms, so the iGPU is more than enough.

---

## 3. Runtime architecture

```
┌──────────────────────────── game/  (pure, deterministic, integer-only) ───────────────────────────┐
│ ModeStack: Boot → Curtain/Title → FileSelect → WorldMap ⇄ Level → CourseClear → … → Ending       │
│ Level:  TileMap · Collision · Players · ObjectPool · Spawner · Camera · Blocks · Timer · HUD      │
│ Map:    Nodes · Paths · MapObjects (Hammer Bros, airship…) · Inventory · Events                  │
└─────────────▲──────────────────────────────────────────────┬─────────────────────┬───────────────┘
   InputFrame │                                              │ PPU state (OAM,      │ SoundCmds
┌─────────────┴────────────┐                ┌────────────────▼───────────┐  ┌──────▼──────────────┐
│ input/  actions, edges,  │                │ ppu/  CPU compositor →      │  │ audio/ driver (music│
│ SOCD, stick→dpad, remap, │                │ 256×240 indexed frame,      │  │ + SFX) → APU → blip │
│ replay record/playback   │                │ palettes, effects           │  │ → ring buffer        │
└─────────────▲────────────┘                └────────────────┬───────────┘  └──────┬──────────────┘
┌─────────────┴────────────────────────────────────────────────▼─────────────────────▼─────────────┐
│ platform/  SDL3 window + events + gamepads + audio device · D3D11 presenter · FramePacer · timers │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
          dev/ (dev builds only): ImGui overlay, tuning panel, rewind, frame-advance, trace export
```

**Rules**
- `game/` never includes SDL, Windows, D3D, `<chrono>`, `<random>`, or floating point. It receives an `InputFrame` per player and emits: PPU state (background scroll, OAM-like sprite list, palettes, effects) and a list of `SoundCmd`s.
- All `game/` state lives in one POD arena → a snapshot is a `memcpy` (used for rewind, save-states, and replay seeking).
- Single-threaded game + render on the main thread. SDL's audio thread only drains a ring buffer. A dev-only file-watcher thread posts hot-reload requests.

---

## 4. Main loop, frame pacing and input latency

### 4.1 The loop

```cpp
// platform/main_win.cpp (sketch)
while (running) {
    pacer.waitUntilSampleTime();          // §4.3 — block on swapchain waitable / VRR timer / late-latch
    platform.pumpEvents();                // SDL_PumpEvents(): keyboard + gamepads, captures edges
    const bool doTick = pacer.consumeTick();  // exactly 60 ticks per second of wall time
    if (doTick) {
        InputFrame in[2] = { input.sample(0), input.sample(1) };  // latched edges + current held state
        game.tick(in);                    // one 1/60 s step, pure
        audio.renderTick(game.soundCmds());   // exactly 800 samples @ 48 kHz
        ppu.render(game.ppuState(), frame);   // CPU, ~0.1–0.3 ms
        presenter.upload(frame, palette);
    }
    presenter.present(pacer.syncInterval(), pacer.presentFlags());
    pacer.onPresented(presenter.frameStats());
}
```

### 4.2 Pacing modes (auto-selected, overridable in Options → Video)

| Mode | When | How | Result |
|---|---|---|---|
| **A. VSync ×N** | Display refresh within 0.5 % of 60·N (60, 120, 180, 240 Hz) | VSync on; `Present(N, 0)` (sync interval N) — one tick per present | Perfect 60 fps cadence. At 240 Hz (your internal panel) scan-out is 4× faster, so latency is lower than at 60 Hz. |
| **B. VRR** | G-Sync / FreeSync active (e.g. your 144 Hz monitor if it supports it) | Tearing allowed (`DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING`), `Present(0, DXGI_PRESENT_ALLOW_TEARING)`, ticks paced by a high-resolution waitable timer at exactly 16.667 ms | Display refreshes exactly when we present → perfect 60 Hz, minimal latency. |
| **C. Fixed, non-multiple** (e.g. 144 Hz without VRR) | Fallback | Tick on wall-clock accumulation; present each refresh | 2-3-2-3 refresh cadence → visible judder while scrolling. The Options screen explains this and offers **Exclusive fullscreen at 120 Hz**, which returns to Mode A. |
| **D. Smooth (optional, late milestone)** | Opt-in for Mode C | Render at display rate; interpolate camera and object positions between the last two ticks, rasterize at the integer upscale | Smooth, but adds ~½ tick of average latency and breaks the strict NES pixel grid. Off by default. |

The NES runs at 60.0988 Hz; we tick at exactly 60.000 Hz (0.16 % slower — imperceptible, and keeps music tempos integral).

### 4.3 Latency techniques

1. **Flip-model swapchain, 2 buffers, `SetMaximumFrameLatency(1)`, frame-latency waitable object.** The CPU blocks *before* sampling input until the GPU can accept a new frame, so input is never sampled early and then left in a queue.
2. **Edge latching.** Key/button *down* and *up* events are processed as they arrive. Each action keeps `held`, `pressedSinceLastTick` and `releasedSinceLastTick`. A tap that is pressed *and* released between two ticks still produces a press (and a minimum-height jump). Polling only the current state loses these taps.
3. **Borderless fullscreen by default.** With the flip model, Windows promotes a borderless-fullscreen window to *independent flip*, so frames go straight to scan-out without DWM composition latency.
4. **Late latch ("Ultra" latency mode, optional).** After the waitable object signals (just after a vblank), sleep until `nextVblank − budget`, where `budget` = p99 of measured sample→present CPU+GPU time + 1 ms of margin (auto-calibrated, typically 2–3 ms). Only then sample input, tick, render and present. This cuts about 10–13 ms of average latency at 60 Hz. If more than 1 in 500 frames misses its vblank, it disables itself and says so.
5. **High-resolution sleeping.** Use `CreateWaitableTimerExW(..., CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, ...)` (Windows 10 1803+) and busy-wait the last ~0.3 ms. Measure with QPC (`SDL_GetPerformanceCounter`).
6. **No render-thread queue.** Rendering happens right after the tick on the same thread; nothing buffers frames ahead.

### 4.4 Latency budget (targets)

| Stage | 60 Hz VSync | 240 Hz VSync×4 or VRR + late latch |
|---|---|---|
| Device → OS (USB polling) | 1–8 ms | 1–8 ms |
| OS → sampled by game | avg ~8 ms (0–16.7) | avg ~1–2 ms |
| Tick + PPU + present (CPU+GPU) | < 2 ms | < 2 ms |
| Wait for flip | ≤ 16.7 ms (overlaps with the row above) | ≤ 4.2 ms / ~0 (VRR) |
| Scan-out to mid-screen | ~8 ms | ~2 ms (240 Hz) |
| **Total (excluding panel processing)** | **~25–35 ms** | **~10–18 ms** |

For comparison, SMB3 on an NES + CRT reads the pad once per frame and shows the result the next frame: roughly 1–2 frames (17–33 ms) plus polling jitter. **Target: equal to or better than the NES in every pacing mode.**

### 4.5 Measuring it (dev builds + optional release toggle)

- **Latency test mode:** pressing A draws a full-white 32×32 square in the top-left on the *same tick*. Use a phone slow-motion camera (240 fps) or an LDAT/Reflex analyzer to measure click-to-photon.
- **Frame-pacing HUD:** present-to-present histogram, missed-vblank counter, current pacing mode, measured refresh (from DXGI frame statistics).
- **PresentMon** captures to confirm the presentation mode is `Hardware: Independent Flip` (not `Composed`).

---

## 5. Virtual PPU (renderer)

### 5.1 Model: "Authentic NES+"
Behaves like the NES PPU, with a few limits relaxed. The rules are in [04-art-audio-presentation.md](04-art-audio-presentation.md) §1.

| Feature | NES | SMB4 |
|---|---|---|
| Framebuffer | 256×240 | 256×240, 8-bit index into 64-color master palette |
| Tiles | 8×8, 2bpp | same (all graphics stored as CHR-like 2bpp tiles) |
| BG palettes / sprite palettes | 4 / 4 | **8 / 8** |
| Colors per palette | 3 + shared backdrop / 3 + transparent | same |
| BG palette granularity | 16×16 (attribute table) | 16×16 metatile (identical in practice) |
| Sprites | 64, 8×16 mode, 8 per scanline | 128 (8×16), **no per-scanline limit by default**; "Authentic flicker" option enforces 8/line with rotation |
| Sprite priority vs BG | per-sprite "behind BG" bit | identical semantics, including the OAM-order priority quirk |
| Scroll | 1 BG layer + raster splits | 1 BG layer + fixed status-bar split; extra raster splits allowed on title/map screens only |

### 5.2 Composition per pixel (exact NES rules)
1. Start with the backdrop color (BG palette 0, color 0).
2. If the BG pixel's 2-bit value ≠ 0, the BG color replaces it.
3. Find the **first opaque sprite pixel in OAM order**. If none, done.
4. If that sprite has the *behind* bit and the BG pixel is opaque, keep BG; otherwise draw the sprite.
   (Step 3 happens before step 4, so a lower-index "behind" sprite also hides higher-index "front" sprites. SMB3-style pipe entry, power-ups rising out of blocks and the white-block "drop behind the scenery" secret depend on this.)

### 5.3 Data
- **CHR banks**: tile graphics in 2bpp. Animated BG tiles (question blocks, coins, munchers, water, conveyor belts) swap their bank on a global animation clock, the way SMB3 swaps CHR banks. Default cadence is 8 ticks per animation frame; confirm against the reference.
- **Metatiles**: 16×16 = four 8×8 tile indices, a palette index and a collision class (see 03 §6).
- **Metasprites**: lists of `(dx, dy, tile, palette, flipH, flipV, behind)` in data; the player is 2×1 (small, 16×16) or 2×2 (big, 16×32) 8×16 sprites.
- **Status bar**: a separate fixed "nametable" drawn below the split line and never scrolled. It is exactly SMB3's layout: **playfield = scanlines 0–191 (12 metatile rows), status bar = scanlines 192–239 (48 px: border, 2 text rows, border, padding)**, where SMB3's MMC3 IRQ switches to the status bar.
- **Left-column mask:** SMB3 blanks the leftmost 8 px to hide scroll artifacts (and still shows attribute-color glitches at the right edge). Our renderer has neither problem, so the mask is an *Authentic* option, off by default.

### 5.4 Effects (all palette- or scroll-based, like the NES)
- Fade to or from black in 4 NES-style steps (subtract `$10` from each palette entry per step).
- Starman palette cycling; invincibility blink (sprite hidden on alternating frames); P-switch music.
- Screen shake by vertical scroll offset (Thwomp / Sledge Bro landings, Bowser ground pound).
- Iris/curtain transitions on the map (raster-style masks computed on the CPU).

### 5.5 GPU post-processing (D3D11)
- Upload the 256×240 `R8_UINT` index texture and a 64-entry palette texture. The pixel shader maps index → sRGB color, then scales.
- **Scaling modes:** *Integer* (default; e.g. ×6 at 1440p = 1536×1440 exactly, ×9 at 4K), *Sharp-bilinear fit* (fills the screen without shimmer), *Stretch* (discouraged).
- **Pixel aspect:** Square 1:1 (default) or NES 8:7 (CRT-accurate, horizontally 1.143×, uses sharp-bilinear horizontally).
- **Filters (optional):** scanlines, a lightweight CRT (mask + bloom + curvature off by default), NTSC composite artifacts.
- **Overscan:** show all 240 lines (default) or crop 8 px top and bottom (TV-accurate). Gameplay-critical content never sits in those lines.
- **Border:** black, or themed per-world border art in the pillarbox area.

### 5.6 Swapchain and device details
- `DXGI_SWAP_EFFECT_FLIP_DISCARD`, `BufferCount = 2`, `DXGI_FORMAT_B8G8R8A8_UNORM` (sRGB view for the shader output).
- Flags: `DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT`, plus `ALLOW_TEARING` when `IDXGIFactory5::CheckFeatureSupport(DXGI_FEATURE_PRESENT_ALLOW_TEARING)` succeeds.
- `IDXGISwapChain2::SetMaximumFrameLatency(1)`; wait on `GetFrameLatencyWaitableObject()` in `pacer.waitUntilSampleTime()`.
- **Adapter selection:** enumerate DXGI adapters and outputs and pick the adapter whose output's `HMONITOR` matches `MonitorFromWindow(hwnd)`. Re-create the device if the window moves to a monitor on another adapter. Options → Video offers *Render GPU: Auto / High performance / Power saving*.
- Handle `DXGI_ERROR_DEVICE_REMOVED` by rebuilding the device (the game state is unaffected).
- Exclusive fullscreen (`SetFullscreenState`) is only for the "force 120 Hz" option. Borderless is the default.

---

## 6. Audio engine

### 6.1 Signal chain
```
SoundCmds (per tick) → Driver @60 Hz (music sequencer + SFX player, channel arbitration)
  → APU register writes (timestamped within the tick) → 2A03 APU model
  → band-limited step synthesis (BLEP/"blip" buffer) → NES non-linear mixer
  → NES output filters (HPF 90 Hz, HPF 440 Hz, LPF 14 kHz; "Clean" profile = HPF 37 Hz only)
  → 48 kHz stereo ring buffer → SDL3 audio stream (WASAPI shared)
```

### 6.2 APU model
- **Pulse ×2** (duty 12.5/25/50/75 %, envelope, sweep, length counter), **Triangle** (linear counter), **Noise** (15-bit LFSR, long and short mode), **DMC** (1-bit delta samples, used for drums/timpani like SMB3).
- **Mixer** (NESdev formulas): `pulse = 95.88 / (8128 / (p1 + p2) + 100)`, `tnd = 159.79 / (1 / (t/8227 + n/12241 + d/22638) + 100)`.
- **Synthesis:** output amplitude changes are placed as band-limited steps at their exact APU-clock timestamps and resampled to 48 kHz. This gives no aliasing and costs almost nothing.

### 6.3 Driver, channel stealing, volume sliders
- Music and SFX are both "driver programs" that run once per tick, like an NES sound engine.
- **Channel stealing (authentic):** each SFX declares the channels it uses (e.g. jump on Pulse 1, stomp on Noise). While it plays, the music keeps sequencing that channel silently; when the SFX ends, the music resumes mid-note. That is the SMB3 sound.
- **Separate Music/SFX volume sliders:** run **two APU instances** (music APU with stolen channels muted; SFX APU). Mix them with independent gains. This is effectively identical to a single APU at 100/100.
- **Timing:** 48 000 / 60 = **exactly 800 samples per tick**. Each tick's audio is generated on the game thread and pushed into the stream.
- **Drift control:** the audio device clock and the display clock differ slightly. Keep the queue near **2 ticks (~33 ms)** by nudging `SDL_SetAudioStreamFrequencyRatio` within ±0.5 %; this is inaudible. When the game is paused, or on a hitch, never block: drop or pad samples.

### 6.4 Music authoring pipeline
- Compose in **FamiStudio** (2A03 + DPCM only; no expansion chips, for authenticity) → export **FamiStudio Text** → `musicc` tool → compact binary song (per-channel event streams + instruments).
- Supported feature subset (the composer's contract): instruments with volume / arpeggio / pitch / duty envelopes (loop + release points), notes, release, note cut, note delay, volume column, vibrato, pitch slide / portamento, speed & tempo, pattern jump/break, DPCM samples with pitch.
- **Hurry-up:** when the timer drops below 100, play the jingle, then restart the level music at a faster speed (a per-song "fast" speed value, or speed − N).

---

## 7. Input system

### 7.1 Actions
`Left, Right, Up, Down, A (Jump), B (Run / Action), Start, Select` per player (the NES pad), plus UI and dev actions.

### 7.2 Default bindings

| Action | Keyboard (primary / alt) | Gamepad "Modern" preset | Gamepad "NES" preset |
|---|---|---|---|
| D-pad | Arrow keys / WASD | D-pad + left stick | D-pad + left stick |
| A (Jump) | X / K / Space | South (Xbox A) | East (Xbox B) |
| B (Run) | Z / J / Left Shift | West (Xbox X) **and** RT | South (Xbox A) and RT |
| Start | Enter | Menu | Menu |
| Select | Right Shift / Tab | View | View |

All actions can be rebound: up to 3 bindings per action per device class, with conflict warnings. Bindings are stored in `settings.json`.

### 7.3 Processing rules
- **Scancodes** (layout-independent) for the keyboard.
- **Edge latching** (§4.3). The `InputFrame` passed to the game contains `held`, `pressed`, `released` bitmasks.
- **SOCD (opposite directions held together):** Left+Right → **last pressed wins** (options: neutral / first wins). Up+Down → **neutral**. This prevents keyboard-only glitches (moonwalking, simultaneous duck+climb).
- **Analog stick → d-pad:** radial deadzone 0.40 with 0.05 hysteresis. Horizontal registers when |x| > deadzone and the angle is within ±67.5° of horizontal. **Down (duck/pipe) registers only within ±30° of straight down.** Accidental ducking mid-run is the #1 stick complaint in Mario games; this rule prevents it. Up uses the same ±30° cone. All values are configurable.
- **Focus loss or controller disconnect** → auto-pause (option).
- **Players:** P1/P2 device assignment screen. In SMB3-style alternating 2P, only one player's input is active at a time; the battle mini-game uses both.

### 7.4 Replays
Each tick stores `held` bits for 8 buttons × 2 players, plus edge bits where a sub-tick tap occurred. Replays are RLE-compressed (an hour is well under 100 KB). The header holds `{game version, physics profile, seed, start state}`.

---

## 8. Determinism, fixed-point, RNG

- **Units** (exact definitions in 01 §2): 1 pixel = 16 subpixels; velocities in subpixels/tick; accelerations carry an 8-bit fractional accumulator where SMB3 uses one. Types: `int32` positions, `int16` velocities, `uint8` accumulators, wrapped in strong types (`SubPx`, `SubVel`) so px/subpx can't be mixed up.
- **RNG:** a 16-bit LFSR advanced once per tick (like SMB3's), plus on-demand draws. Seeded per level entry from the save's master seed.
- **Iteration order:** object slots are updated in index order; spawning fills the lowest free slot; the list is never sorted.
- **Snapshots:** the whole `game/` state is POD in a fixed arena, well under 1 MB. Rewind uses a ring of snapshots every 4 ticks plus re-simulation.
- **Headless:** `SMB4.exe --headless --replay <file> [--trace out.csv] [--expect <hash>]` for CI and parity tests.

---

## 9. Game code structure

- **Mode stack:** `Boot`, `Curtain` (SMB3-style theater curtain rising on the title), `Title` (1P/2P, Options), `FileSelect` (3 slots), `WorldMap`, `LevelIntro` (the "WORLD 1-1" card), `Level`, `CourseClear`, `MapEvent` (locks opening, airship flying), `BonusGame`, `Battle2P`, `Ending`, `Credits`. Transitions are modes too (iris, fade, curtain).
- **Level runtime:**
  - `TileMap`: `u16` metatile IDs. SMB3's limits are horizontal areas 16 metatiles per screen × **27 rows (432 px)** × ≤ **15 screens**, and vertical areas 1 screen wide × 15 rows per screen × ≤ **16 screens**. The engine allows up to 32 screens; level guidelines keep SMB3's limits.
  - `Collision`: point probes, slope heights, semisolid rules, bump requests.
  - `Player[2]`: state machine + power-up + P-meter (see 01).
  - `ObjectPool`: **SMB3's exact pools** (03 §7.1): 8 object slots (0–4 general, 5–7 reserved for special objects such as the goal card), 8 special objects (enemy projectiles/effects), 8 cannon/generator slots, 2 player projectiles, 5 score pop-ups. Visual-only effects (debris, puffs, sparkles) get their own pool of 32 so they never steal gameplay slots. The slot limits are part of the difficulty and the feel.
  - `Spawner`: the object list sorted by position; spawn/despawn/respawn windows relative to the camera, as in SMB3.
  - `Camera`: see 01 §12.
  - `LevelTimer`, `PSwitch`, `Autoscroll`, `CoinTally`, `GoalRoulette`.
- **Object behaviors:** a table indexed by object type with function pointers `{init, update, draw, onPlayerContact, onHit(DamageKind), onBumpedFromBelow}`, POD state per slot (a union of per-type fields), and data flags (stompable, fire-proof, tail-proof, shell-proof, star-proof, hammer-proof, carryable, …). No ECS; SMB3's per-object state machines map directly onto this.
- **Events** emitted per tick: `SoundCmd`, `ScorePopup`, `ScreenShake`, `SpawnEffect`, `MusicChange`.

---

## 10. Data formats and asset pipeline

| Asset | Source (in `assets/`) | Tool | Runtime (in `data/`) |
|---|---|---|---|
| Levels | Tiled `.tmj` (JSON): tile layer `Main`; object layer `Objects` (enemies/items with properties); object layer `Zones` (camera locks, water, autoscroll path, pipe/door links, spawn modifiers); tilesets `.tsj` with per-tile properties (collision class, bump behavior, contents, animation) | `levelc` | `.lvl` (RLE tiles + sorted object list + zone records) |
| World maps | Tiled: tile layer + objects (nodes, paths, locks, map objects, events) | `mapc` | `.map` |
| Graphics | Aseprite (`.aseprite`) → CLI export PNG sheet + JSON tags | `gfxc` (validates NES+ rules: palette membership, ≤ 3 colors + transparent per sprite tile, ≤ 4 per BG metatile) | `.chr` banks, metasprite + animation tables |
| Music/SFX | FamiStudio `.fms` → FamiStudio Text export | `musicc` | `.snd` |
| DPCM | WAV | `dpcmc` | inside `.snd` |
| Text/font | UTF-8 strings + 8×8 font sheet | `textc` | string table |
| Everything | — | `pak` | `data.pak` (TOC + blobs) for release |

- The tools are **C++ command-line programs in the same CMake project**, so the enums and formats are shared with the runtime and no Python dependency is needed.
- **Dev hot-reload:** a file watcher rebuilds the changed asset. The running level reloads in place, keeping the player's position and state ("live level editing"). *Play from cursor:* run `SMB4.exe --level w1-3 --at 1234,160`, or use the dev overlay's warp.

---

## 11. Save data and settings

- Location: `%APPDATA%\SuperMarioBros4\` → `settings.json`, `save1.dat` … `save3.dat`, `replays\`, `screenshots\`, `logs\`, `crashes\`.
- The save includes: version, per-world progress (cleared nodes, opened locks, map-object states and positions, airship location), lives, score, coins, both players' power-ups, inventory, goal cards, secrets found, unlocked worlds, statistics, play time.
- **When:** autosave on the world map after every level or map event; "Save & Quit" from the map pause menu. There's no mid-level saving (authentic); an optional *suspend* on quit restores exactly once.
- **Robustness:** write `saveN.tmp` → flush → atomic rename; CRC32 + format version; keep the previous file as `.bak`; migrate old versions forward.

---

## 12. Dev tooling (dev builds; some also as release options)

| Tool | Key | Purpose |
|---|---|---|
| Debug overlay | F1 | Hitboxes, tile probes, velocity (hex and px/tick), P-meter raw value, player state name, coyote/buffer timers, camera zones, spawn windows, object slot usage |
| Tuning panel | F2 | Every physics constant live-editable; save/load `physics_profile.json` (release builds compile the constants in) |
| Frame advance | F5 pause / F6 step | Inspect every tick |
| Slow motion | F7 | ½, ¼, ⅛ speed |
| Rewind | Hold Backspace | 10 s snapshot ring |
| Save/load state | F9 / F10 | Instant retries while tuning |
| Input display | F3 | On-screen NES pad (release option for streamers) |
| Trace export | F4 | CSV per tick: inputs, x, y, subpixels, velocities, state, P-meter — used for parity comparison |
| Warp / cheats | F8 | Level select, give power-up, fill P-meter, god mode |
| Latency test | Ctrl+F12 | §4.5 |

---

## 13. Testing and CI

1. **Unit tests** (doctest): fixed-point helpers, probe collision, slope height tables, spawn windows, and the physics invariants listed in 01 §15 (exact jump apexes, frames-to-top-speed, skid distances, P-meter fill time).
2. **Replay regression:** curated input replays for each mechanic and each level, with a golden hash of the game state every 60 ticks. Any unintended change fails CI; intentional changes are re-blessed with a note.
3. **Feel-parity traces:** per-tick traces recorded from the original SMB3 in Mesen2 (from your own cartridge dump, via a Lua script) compared against our engine running the same inputs in an equivalent test room. See 01 §15.
4. **Fuzzing:** headless random/biased input replays over every level (thousands per minute) to catch crashes, soft-locks (no progress for N seconds), out-of-bounds positions, and slot exhaustion.
5. **Performance gates:** p99 tick < 1 ms, p99 PPU < 0.5 ms, zero allocations after level load (checked via a counting allocator in debug).
6. **CI:** GitHub Actions `windows-latest` → configure, build Debug/Release, run unit + replay + fuzz-smoke headless.

---

## 14. Packaging and release

- Release: `/O2 /GL`, LTO, static CRT (`CMAKE_MSVC_RUNTIME_LIBRARY = MultiThreaded`), SDL3 statically linked. Output: `SMB4.exe` + `data.pak` (+ `LICENSES.txt`).
- Distribute as a portable zip. An installer (Inno Setup) is optional. Unsigned builds trigger SmartScreen; code signing is optional.
- Crash handling: unhandled-exception filter → `MiniDumpWriteDump` to `crashes\`, plus the last 10 s of input as a replay, so crashes reproduce deterministically.
- Windows integration: DPI-aware (per-monitor v2), `ES_DISPLAY_REQUIRED` while playing, no Game Bar or overlay dependencies.

---

## 15. Repository layout

```
SuperMarioBros4/
├─ CMakeLists.txt · CMakePresets.json · .gitignore · .clang-format
├─ docs/                       ← this plan
├─ src/
│  ├─ platform/   main_win.cpp, window.cpp, presenter_d3d11.cpp, pacer.cpp, audio_device.cpp, input_devices.cpp
│  ├─ core/       fixed.h, rng.h, arena.h, assert.h, log.cpp, hash.h, fs.cpp
│  ├─ ppu/        ppu.cpp, palette.cpp, chr.cpp, oam.h, effects.cpp
│  ├─ audio/      apu.cpp, blip.cpp, driver.cpp, music.cpp, sfx.cpp, mixer.cpp
│  ├─ input/      actions.h, bindings.cpp, socd.cpp, stick.cpp, replay.cpp
│  ├─ game/
│  │  ├─ game.cpp, modes/ (title, map, level, bonus, battle, ending…)
│  │  ├─ player/  player.cpp, physics.cpp, states.cpp, powerups.cpp, pmeter.cpp, leniency.cpp
│  │  ├─ level/   tilemap.cpp, collision.cpp, slopes.cpp, blocks.cpp, camera.cpp, spawner.cpp, zones.cpp
│  │  ├─ objects/ registry.cpp, goomba.cpp, koopa.cpp, … (one file per family)
│  │  ├─ map/     worldmap.cpp, mapobjects.cpp, inventory.cpp
│  │  └─ ui/      hud.cpp, menus.cpp, text.cpp
│  └─ dev/        overlay.cpp, tuning.cpp, rewind.cpp, trace.cpp, hotreload.cpp
├─ tools/         levelc/, mapc/, gfxc/, musicc/, dpcmc/, textc/, pak/, parity/ (trace diff)
├─ assets/        gfx/, levels/, maps/, music/, sfx/, text/
├─ data/          (generated; git-ignored)
└─ tests/         unit/, replays/, parity/, fuzz/
```
