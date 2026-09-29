# Super Mario Bros. 4 — Project Plan

A fan-made sequel to **Super Mario Bros. 3 (NES)**, built as a native Windows application.
**It must feel like SMB3 in the hands, frame for frame**, run with less input lag than the original
hardware, look and sound like a real late-era NES game, and bring eight new worlds.

This file is the overview. The detail lives in `docs/`:

| Doc | What it covers |
|---|---|
| [01 — Game feel & physics](docs/01-game-feel-and-physics.md) | **The core spec.** SMB3's movement rules and numbers from the disassembly: speeds, acceleration, skid, friction, jump table, gravity, P-meter, collision probes, stomps, power-up movement, camera, animation timing. The Classic/Modern leniency layer (coyote time, jump buffer). Level-design ruler and test invariants. |
| [02 — Engine architecture](docs/02-engine-architecture.md) | C++20 / SDL3 / Direct3D 11. Fixed 60 Hz integer simulation, frame pacing for your 240 Hz and 144 Hz displays, input-latency techniques, NES-style renderer, 2A03 sound engine, input system, tools, tests, packaging. |
| [03 — Game design](docs/03-game-design.md) | Power-ups and items, new suits, blocks and tiles, spawn rules, enemy roster and defeat matrix, bosses, the 9 worlds, level-design guidelines, scoring and lives, world-map systems, assists. |
| [04 — Art, audio & presentation](docs/04-art-audio-presentation.md) | "Authentic NES+" visual rules, sprite and tileset lists, status bar, menus, music list (~34 tracks), SFX list (~60) with channel priorities, accessibility. |
| [05 — Roadmap & testing](docs/05-roadmap-and-testing.md) | Milestones M0–M7 with exit criteria, the feel-parity harness, test suites, feel checklist, risks. |

> **Implementation status.** The game is built and playable: `bin\SuperMarioBros4.exe` (see `README.md`).
> It was implemented in **C# (.NET Framework 4.8, WinForms + OpenGL 1.1, waveOut, XInput/WinMM)** instead of the
> C++20/SDL3/D3D11 stack in doc 02, because this PC has no C++ toolchain and the in-box C# compiler needs no
> install. The architecture otherwise follows doc 02: fixed 60 Hz integer simulation in SMB3 units, a 256×240
> virtual PPU, a 2A03-style synth, and lockstep/accumulator/VRR frame pacing. The physics follows doc 01 and is
> checked by `smb4tools selftest`. Content (8 worlds, 54 levels + a Hammer Bro battle arena) is plain text in
> `data\`, verified by `qa.ps1`.
> Doc 03 planned 9 worlds; the game ships with 8.

---

## 1. Pillars

1. **Feels exactly like SMB3.** SMB3's per-frame integer physics is reproduced rule for rule. The numbers come from the game's community disassembly, and parity is proven by comparing per-frame traces against the original (the *Classic* profile). The default *Modern* profile adds only invisible leniency.
2. **Less lag than the NES.** At most one queued frame, input sampled immediately before each tick, taps that can never be lost, and pacing modes for 60/120/240 Hz and VRR.
3. **A real NES look and sound.** 256×240, the NES palette and tile rules, a single scrolling layer, 2A03 audio with authentic sound-effect channel stealing.
4. **A true sequel.** SMB3's systems stay intact (world map, suits, cards, Hammer Bros, airships, Koopalings), plus 8 new worlds, a post-game "Encore" world, and a few new ideas that each have to pass a prototype gate.

## 2. Scope

**In v1:**
- 1P, 2P alternating, and the 2P battle game.
- 8 worlds + World 9, about 100 stages.
- All SMB3 power-ups plus up to 3 gated new ones.
- 3 save slots, full options and rebinding, Classic/Modern feel profiles, Time Attack with ghosts.

**Out of v1:** widescreen gameplay, a player-facing level editor, online play, localization, extra playable characters.

---

## 3. The feel on one page (Classic profile; details in 01)

| Aspect | Value |
|---|---|
| Simulation | 60 ticks/s fixed; positions in 1/16 px; all integer |
| Speeds | walk **1.5**, run (B) **2.5**, P-speed **3.5** px/tick; max 4.0 |
| Ground accel / friction / skid | +0.875 / −0.875 (Big) or −0.625 (Small) / **2.0** (1/16 px per tick²), with SMB3's 16-tick dither |
| Air control | no air friction; pressing backward brakes at the skid rate; turnaround is allowed midair |
| Time to walk / run speed | 27 / 45 ticks (0.45 / 0.75 s) |
| Jump velocity | −3.5 / −3.625 / −3.75 / −4.0 px/tick by speed tier |
| Gravity | **+1/16** while A is held *and* rising faster than 2 px/tick, otherwise **+5/16**; terminal 4 px/tick (reached in 13 ticks) |
| Jump heights | tap 20.6 px → full 70.7 px standing (4 blocks); 85 px running (5); 100 px at P-speed (6) |
| Max gaps (frame-perfect) | walk 5, run 10, P-speed 15 tiles |
| P-meter | +1 bar per 8 ticks at run speed on the ground; −1 per 24; full 49–72 ticks after reaching run speed |
| Raccoon | flight 256 ticks (4.3 s) climbing 1.5 px/tick; flutter caps the fall at 1 px/tick for 16 ticks per tap |
| Stomp | bounce −4 px/tick: 104 px holding A, 28 px without |
| Collision | 2 foot probes (x+4, x+11), 1 head probe (x+8), walls push out 1 px/tick |
| Camera | horizontal dead zone 112–128 px, no smoothing; vertical locked unless P-jumping, flying or climbing |
| Hit invincibility / Star / P-switch | 113 / 448 / 512 ticks |
| **Modern leniency** | **coyote 5 ticks** (reusing SMB3's own air-jump timer), **jump buffer 4 ticks** (not for flutter taps), always-on tap latching |

---

## 4. Key decisions

| # | Decision | Choice | Why | Doc |
|---|---|---|---|---|
| D1 | Engine | Custom **C++20 + SDL3 + Direct3D 11** | Full control of timing, latency and integer physics; tiny native exe | 02 §2 |
| D2 | Simulation | Fixed 60 Hz, integer fixed-point, deterministic | SMB3's feel is its per-frame integer math; enables replays, rewind and parity tests | 02 §1, 01 §2 |
| D3 | Physics source | SMB3's own rules and constants (disassembly research), verified by per-frame trace comparison against the original | "Close" isn't the goal; identical is | 01, 05 §3 |
| D4 | Leniency | Classic (exact) + **Modern (default)** adding only coyote time and a jump buffer; nothing floaty (no apex hang, no velocity cut, no camera smoothing) | Modern comfort without changing a single SMB3 trajectory | 01 §9 |
| D5 | Frame pacing | Present every Nth vblank on 60·N Hz displays; VRR when available; optional late-latch; adapter follows the monitor | Your 240 Hz panel (Intel iGPU) and 144 Hz display (RTX 4080) | 02 §4–5 |
| D6 | Resolution | 256×240, 192-px playfield + 48-px status bar, integer scaling; 4:3 gameplay | SMB3 layout exactly; widescreen would change spawn behavior and difficulty | 02 §5, 04 §3 |
| D7 | Rendering | CPU "virtual PPU" → indexed framebuffer → GPU palette and scale shader | Exact NES layering and priority (pipe entry, white-block secret) | 02 §5 |
| D8 | Audio | 2A03 APU emulation + music/SFX driver; FamiStudio pipeline | Authentic sound, including SFX channel stealing | 02 §6, 04 §4 |
| D9 | Content tools | Tiled (levels and maps), Aseprite/LibreSprite (art), FamiStudio (music), C++ converters, hot reload | Proven free tools; no custom editor needed for v1 | 02 §10 |
| D10 | Object limits | SMB3's exact slots (5 general + 3 special) and spawn/despawn windows | They're part of the difficulty and the feel | 03 §7.1 |
| D11 | Saves | 3 slots, autosave on the world map, atomic writes | Modern convenience; no mid-level saves (authentic) | 02 §11 |

---

## 5. Roadmap summary (full detail in 05)

| Milestone | Outcome | Gate |
|---|---|---|
| **M0** Foundations | Window, D3D11 presenter, pacing, input, APU test tone, dev overlay, CI | Independent flip, 0 missed vblanks, latency measured |
| **M1** Feel Lab | Mario in gray-box rooms with the full SMB3 movement set, P-meter, camera, Classic + Modern | **12 maneuvers match SMB3 traces exactly** + blind feel test |
| **M2** Vertical slice | One polished level: blocks, 4 power-ups, 6 enemies, shells, HUD, goal cards, audio | 10 playtesters, fuzz-clean |
| **M3** Core mechanics | Slopes, water, all suits, vines, pipes, P-switch, lifts, autoscroll, ice… | Mechanics Museum + replays |
| **M4** World map | Map, Hammer Bros, Toad houses, bonus games, inventory, saves, menus, 2P | World 1 playable power-on to clear |
| **M5** Bosses & roster | Boom Boom, Koopalings, Bowser, all enemies, new suits through the gate | Defeat-matrix tests |
| **M6** Content | Worlds 1–9 (~100 stages) | Per-level definition of done |
| **M7** Release | Polish, accessibility, performance, packaging | Release checklist |

**Rule: no level production before M1 passes.**

---

## 6. First steps on this PC

The project folder was empty, and there's no compiler, CMake or git installed yet.

1. Install **Visual Studio 2022 Community (or newer)** with the **Desktop development with C++** workload (includes MSVC, CMake, Ninja).
2. Install **Git for Windows**, then `git init` this folder and commit these docs.
3. Install the content and reference tools as needed: **Tiled**, **Aseprite** (or LibreSprite), **FamiStudio**, **Mesen2**, **PresentMon**.
4. Start **M0** (05 §1): CMake skeleton → SDL3 window → D3D11 presenter with the 256×240 test pattern → frame pacer → input latching → APU test tone → dev overlay.
5. For the M1 parity harness you need a dump of **your own** SMB3 cartridge to record reference traces in Mesen2. That's only for measurement; no Nintendo assets ship with the game.

---

## 7. Legal and IP (read before sharing anything)

"Super Mario Bros.", Mario, Luigi, Bowser, the Koopalings and the rest are **Nintendo trademarks and copyrighted characters**, and Nintendo routinely takes down public fan games. This plan keeps you on the safe side where it can:
- All art, music and sound are original (04 §6). Nothing is ripped from a ROM.
- The original game is used only as a measurement reference.

Even so, a public release under this name and with these characters could be taken down. The owner chose to publish it as a clearly-labelled, free, non-commercial fan project (see the README disclaimer); if a rights holder objects, rebrand (original hero, enemies, title) — the engine, feel, tools and level designs carry over unchanged.

---

## 8. Open decisions (defaults chosen; change any of them)

| Question | Default in this plan | Alternatives |
|---|---|---|
| Tech stack | C++20 / SDL3 / D3D11 | C# + MonoGame (easier, a bit less latency control); Godot 4 (editor, but fights the fixed-tick integer design) |
| Default feel profile | Modern (coyote 5, buffer 4) | Classic by default |
| Gameplay aspect | 4:3, authentic playfield | A widescreen mode later, with its own spawn and difficulty pass |
| New suits | Boomerang Suit, Penguin Suit, Lakitu's Cloud (each gated) | Any subset, or none |
| Luigi | Same physics as Mario (SMB3 NES) | Optional *Luigi Style* toggle post-game |
| Game-over rules | SMB3-faithful (cleared levels reset) | Softer rule in Modern |
| Sprite flicker | Off (Authentic option available) | On by default |

---

## 9. Where the numbers come from

Values in doc 01 are tagged by confidence. Almost all core movement values are **verified in the community's annotated SMB3 disassembly** (Southbird's), with secondary confirmation from the Data Crystal RAM map, TASVideos resources and Super Mario Wiki. Items tagged **[?]** are still unverified; they're measured in the reference game during M1–M3 before implementation:
- the ducking + L/R rule
- raccoon run-jump speed bleed
- quicksand rates
- the multi-coin block rule
- object-overlap edge inclusivity
- the dither-counter RAM address
- a few enemy defeat cells
