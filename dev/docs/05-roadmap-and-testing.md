# 05 — Production Roadmap, Testing & Risks

> Order of work, what "done" means at each step, and how we prove the game feels like SMB3.
> **Principle: no content production until the Feel Lab milestone (M1) passes.** Levels built on
> physics that later change get redesigned; physics that is right from day one never forces that.

---

## 1. Milestones

Sizes are relative (S < M < L < XL). Content production (M6) is larger than everything else combined.

### M0 — Foundations · size S
**Goal:** an empty but correct shell. Right window, loop, timing, input and sound.
- Toolchain installed (02 §2.3), git repo, CMake presets (Debug / RelWithDebInfo / Release), CI on GitHub Actions.
- SDL3 window; D3D11 presenter showing a 256×240 test pattern (palette ramp + 1-px grid + moving sprite) at integer scale.
- `FramePacer` modes A (VSync×N) and B (VRR). Adapter-follows-monitor selection.
- Input: actions, default bindings, edge latching, SOCD, stick→d-pad, `settings.json`.
- Audio: APU pulse + triangle + noise producing a test melody through the blip buffer and drift control.
- Dev overlay (ImGui), frame-pacing HUD, latency-test square, headless mode, replay record/playback of raw input.

**Exit criteria**
- [ ] PresentMon shows *Hardware: Independent Flip* in borderless fullscreen on both monitors.
- [ ] 0 missed vblanks over 10 minutes at 240 Hz (VSync×4) and on the 144 Hz display in VRR mode.
- [ ] Latency-test click-to-photon measured and recorded in `docs/measurements.md`.
- [ ] A press+release shorter than one tick is registered 100/100 times (automated test with synthetic events).

### M1 — The Feel Lab · size M · the most important milestone
**Goal:** Mario moves *exactly* like SMB3 in gray-box rooms.
- Gray-box rooms: flat runway (for the P-meter), walls 1–6 blocks high, gaps 1–8 blocks, low ceilings, a 1-block tunnel, a staircase, semisolid platforms, ledges for walk-off tests, a "bonk ceiling" room.
- Tile collision with SMB3 detection points; small and big forms; duck; walk / run / P-speed / skid / turn / release-run; jump with speed-based velocity; variable height; gravity; bonk; horizontal camera.
- P-meter with HUD arrows and sound.
- Feel profiles: **Classic** (SMB3-exact) and **Modern** (coyote time, jump buffer, stomp leniency; see 01 §9).
- Debug overlay, tuning panel, trace export, frame advance, rewind.
- **Parity harness** (§3): Mesen2 Lua recorder + `tools/parity` diff tool + 12 canonical maneuvers.

**Exit criteria**
- [ ] All 12 canonical maneuvers match the SMB3 traces **exactly** (x, y, subpixels, velocities every frame) in the Classic profile.
- [ ] All physics invariants in 01 §15 are unit-tested and passing.
- [ ] Blind feel test: 3+ people who know SMB3 well play our gray box and an SMB3 gray-box area back to back; average "feels identical" score ≥ 4/5, and nobody names a specific difference that the traces don't explain.
- [ ] Modern-profile leniency passes its dedicated tests (01 §15.3) and is judged "invisible but helpful".

### M2 — Vertical slice: one polished level · size M
**Goal:** one level that could ship.
- Plains tileset and art, HUD, level timer, score, coins, lives.
- Blocks: `?`, brick, coin, multi-coin, invisible, note block. Bump-from-below kills enemies on top.
- Power-ups: Mushroom, Fire Flower, Super Leaf (tail attack, flutter, flight), Starman. Damage and transformation rules.
- Enemies: Goomba, Paragoomba, green/red Koopa, Paratroopa, Piranha Plant, Venus Fire Trap. Shells: carry, kick, chain kills.
- Spawner with SMB3 spawn/despawn/respawn windows.
- Pipe to a bonus room and back. Goal roulette, cards, course-clear walk-off, time-bonus tally.
- Music (plains, underground, star, course clear, death) and all SFX used.

**Exit criteria**
- [ ] 10 external playtesters finish it. Median completion time is recorded and < 60 % of the time limit.
- [ ] Fuzz: 10,000 random-input runs with no crash, no soft lock, no out-of-bounds.
- [ ] Replay regression suite for the level passes in CI.

### M3 — Core mechanics complete · size L
Slopes (22.5° and 45°) and slope sliding; water, swimming and currents; Frog, Tanooki and Hammer suits; Kuribo's Shoe; P-Wing; vines; doors; multi-area levels; P-switch coin↔brick swap; note, jump and wood blocks; springboards; lifts (falling, donut, rotating, path-following, line-riding); autoscroll; vertical levels; conveyors; ice; quicksand; munchers/spikes/lava; the camera's vertical rules; all projectiles (fireballs, hammers, boomerangs); the white-block "behind the scenery" secret.

**Exit:** a "Mechanics Museum" (one room per mechanic), each room with a replay regression test and, where SMB3 has an equivalent, a parity trace.

### M4 — World map and meta-game · size L
World-map engine (nodes, paths, locks, pipes, bridges, map events); wandering Hammer Bros and battles; Toad houses; spade (slot) and N-spade (memory) games; inventory and item effects; fortress locks; airship behavior (flies away when you fail it; the Anchor stops it); kings' rooms and letters; save system; title, file select, options; 2-player alternating mode and the battle mini-game.

**Exit:** World 1 is fully playable from power-on to world clear, with saves, in both 1P and 2P.

### M5 — Bosses and full enemy roster · size L
Boom Boom (fortress), 7 Koopalings, Bowser; the rest of the SMB3 roster; SMB4's new enemies (03 §7.3); new suits through their prototype gate (03 §4.3).

**Exit:** every enemy and boss is in the museum. An automated **defeat-matrix test** (stomp / fire / tail / shell / star / hammer / statue / boomerang against every enemy) matches the table in 03 §7.

### M6 — Content production: Worlds 1–9 · size XL
Per world: map layout → level paper designs → gray-box → playtest → art pass → music → polish. Each world ships with a new theme gimmick, new enemies and a new map feature (03 §8).

**Per-level definition of done**
- [ ] Teaches its idea safely before testing it (03 §9.1).
- [ ] Passes gap/height "ruler" checks (all required jumps within the budget for the speed tier the level assumes; see 03 §9.3).
- [ ] Median playtest completion < 60 % of the time limit; no mandatory blind jumps.
- [ ] Fuzz clean; replay regression recorded; secrets logged in the secrets registry.

### M7 — Polish, accessibility, release · size M
All options and accessibility, controller polish, credits and ending, performance gates, crash reporting, packaging, a final bug bash.

---

## 2. Working agreements

1. **Physics constants live in exactly one place** (`src/game/player/constants.h`, mirrored in 01). Any change updates 01, re-blesses the affected traces, and notes *why* in the commit.
2. **Classic is the reference.** Modern leniency is a thin layer on top (01 §9). It must never change behavior when the leniency paths don't fire.
3. **No floats in `game/`**; a CI grep enforces it.
4. **Every bug fix in gameplay gets a replay test** that reproduces it.
5. **Playtest every week once M2 starts**, with fresh players whenever possible, recording inputs so feel complaints can be replayed.

---

## 3. Feel-parity harness (how "feels just like SMB3" becomes measurable)

1. **Reference traces:** in Mesen2, with a dump of **your own** SMB3 cartridge, a Lua script records per frame: input, player X/Y (pixel + subpixel), X/Y velocity, P-meter, in-air flag, power-up state (RAM addresses in 01 §16). Input comes from a TAS-style movie file so it's reproducible.
2. **Equivalent rooms:** each canonical maneuver runs in an SMB3 location with flat ground and enough runway (for example the start of World 1-1), and in our engine in a gray-box room with the same geometry relative to the start position.
3. **Diff:** `tools/parity` runs our engine headless with the same inputs, aligns frame 0 on the first input, and reports the first diverging frame and field. **Target: zero divergence** in the Classic profile.
4. **The 12 canonical maneuvers:**
   1. Standing tap jump (A held 1 frame)
   2. Standing full jump (A held until landing)
   3. Walk right to top speed, then release
   4. Run (B) right to top run speed, then release B and keep holding right
   5. Charge the P-meter to full, P-speed jump, hold A
   6. Skid: at top run speed, press left until stopped
   7. Turnaround jump mid-skid
   8. Walk off a ledge (no jump)
   9. Jump into a 1-block ceiling (bonk)
   10. Jump, then reverse direction in midair
   11. Duck-slide at run speed (big)
   12. Stomp an enemy with and without A held (bounce heights)

   M3 adds slopes (walk up/down, slope-slide), water (strokes, sinking), raccoon (flutter, flight), ice, and note blocks.

---

## 4. Test suite overview

| Layer | Tool | Runs | Examples |
|---|---|---|---|
| Unit | doctest | every build | fixed-point math, probe collision, slope height tables, spawn windows, P-meter charge/decay, the physics invariants (01 §15) |
| Replay regression | headless `--replay --expect` | every commit (CI) | one per mechanic, one per level, one per fixed gameplay bug |
| Parity | `tools/parity` | nightly, and on physics changes | 12 canonical maneuvers (+ M3 set) |
| Fuzz | headless random/biased input | nightly (10k runs/level) | crashes, soft locks, OOB, slot exhaustion, timer-never-ends |
| Performance | headless timing + counting allocator | every commit | p99 tick < 1 ms; 0 allocations after load |
| Latency/pacing | PresentMon + latency test square | per release candidate | independent flip, 0 missed vblanks, click-to-photon |
| Human feel | blind A/B + checklist (§5) | per milestone | see §5 |

---

## 5. Manual feel checklist (run on the real build, controller *and* keyboard)

**Responsiveness**
- [ ] Jump starts on the first tick after the press (frame-advance check); tapping is never "eaten".
- [ ] Short hop vs full jump is controllable by tap length alone, with no hesitation at the apex.
- [ ] Releasing A mid-rise stops the rise immediately (no floaty carry).
- [ ] Skid → turnaround feels quick; skid dust and sound appear on the first skid tick.

**Momentum**
- [ ] Walk start is quick but not instant; stopping from a walk takes a few pixels, not a slide.
- [ ] Letting go of B while running bleeds speed smoothly back to walk speed.
- [ ] The P-meter fills in the same time as SMB3; the sound and arrows sync with the tick it fills.
- [ ] Ice is slippery enough to be a challenge, never uncontrollable.

**Air**
- [ ] Midair direction reversal feels like SMB3: responsive, not instant.
- [ ] Falling speed caps quickly; long falls feel heavy, not floaty.
- [ ] Raccoon flutter slows the fall only while A is tapped; flight takes off only from a full P-meter.

**Leniency (Modern profile)**
- [ ] Walking off a ledge and pressing jump a moment late still jumps (coyote time), and it never allows a double jump.
- [ ] Pressing jump slightly before landing jumps on the landing tick (buffer); holding A through a landing does **not** auto-jump.
- [ ] Nobody notices leniency is there until it is switched off.

**Camera**
- [ ] The camera never moves unless SMB3's would: no smoothing, no look-ahead drift, no vertical bob on normal jumps.

---

## 6. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Feel is "close but not quite" | High (the default outcome) | Critical | M1 parity harness with exact trace matching; Classic profile as ground truth; no content until M1 exits |
| Content scale (~100 stages + 9 maps) overwhelms a small team | High | High | Tiled + hot reload + play-from-cursor; a museum of reusable set pieces; per-world production template; cut World 9 first if needed |
| Music volume (≈34 tracks, 60 SFX) | Medium | Medium | FamiStudio; placeholder tracks allowed until M6; commission if needed |
| Display/latency variety (VRR, 144 Hz, hybrid GPUs) | Medium | Medium | Pacing modes A–D, adapter selection, on-screen diagnostics, measurements log |
| New suits dilute the SMB3 feel | Medium | Medium | Prototype gate (03 §4.3): each must add a verb, fit NES constraints, and pass playtest, or it's cut |
| Nintendo IP (name, characters) | High if distributed publicly | High | See [PLAN.md](../PLAN.md) §9: keep it private, or rebrand with original characters before any public release; never ship ripped assets |
| Scope creep (widescreen, online, editor-for-players) | Medium | Medium | Explicitly out of scope for v1 (PLAN.md §3); tracked as post-release ideas |
| Solo-dev burnout | Medium | High | Milestones each end in something playable; weekly playtests keep momentum visible |
