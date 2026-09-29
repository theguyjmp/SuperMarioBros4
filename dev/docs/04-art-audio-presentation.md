# 04 — Art, Audio & Presentation

> The look and sound of an NES-era sequel to SMB3: the same hardware rules, the same
> theatrical charm, all-new material. Renderer and audio implementation details are in
> [02-engine-architecture.md](02-engine-architecture.md) §5–6.

---

## 1. Visual rules — "Authentic NES+"

The goal: a screenshot should pass for a real late-era NES game. We keep every rule that shapes the *look* and relax only the ones that caused flicker or tedium.

| Rule | Setting | Enforced by |
|---|---|---|
| Resolution | 256×240 logical, integer-scaled | Renderer |
| Master palette | The NES 2C02 64-color palette (default: a standard modern measured palette; alternates selectable in Options) | `gfxc` rejects off-palette pixels |
| Tiles | 8×8, 2 bits per pixel | `gfxc` |
| Background | 16×16 metatiles, **one 4-color palette per metatile** (color 0 = shared backdrop) | `gfxc` |
| Sprites | 8×16 hardware sprites, **3 colors + transparent** per palette | `gfxc` |
| Palettes on screen | 8 BG + 8 sprite (NES: 4 + 4) | Level/tileset data |
| Layers | 1 scrolling BG layer + sprites (+ fixed status bar). **No parallax, no alpha blending, no rotation or scaling.** | Renderer |
| Sprite flicker | Off by default; *Authentic flicker* option | Renderer |
| Animation | Integer tick counts; BG tile animation through bank swaps | Data |

### 1.1 SMB3 visual language to carry forward
- **The stage-play framing.** SMB3 is presented as a theater production: a curtain rises on the title, scenery is bolted to the backdrop with screws, platforms hang from rigging, and each level ends by running off into a dark "backstage" area. SMB4 continues this as "the next production". There's a curtain on the title and at the ending, act/scene naming on intro cards, and new props (painted flats, pulley-driven lifts, spotlights in castle levels).
- **Chunky readable shapes**: blocks and platforms with 2-tone shading and a **drop shadow** to the lower right; round bushes and hills; bold black outlines on characters.
- **One palette per form** for the player, as in SMB3: each power-up form has its own sprite palette and silhouette details (tail and ears, frog body, hammer helmet), so a glance tells you the current form.
- **Readable hazards:** anything that hurts on contact must read as dangerous at 1× scale. Spikes, munchers and lava get high-contrast palettes, and nothing harmful shares a palette with something harmless in the same area.

### 1.2 Grid and size conventions
- World grid: 16 px. Small player: 16×16. Big player: 16×32 (collision box is smaller; see 01 §10). Standard enemies: 16×16 (Koopa 16×24 walking / 16×16 shell). Giant variants: 32×32.
- Blocks, pipes and platforms snap to 16 px. Moving platforms move on sub-pixel paths but render at integer pixels.
- Pickups (coins, power-ups) are 16×16 and align to the grid when spawned from blocks.

---

## 2. Art production list

### 2.1 Player sprite sheets (Mario, then Luigi as a palette swap + distinct silhouette touches)

| Form | Required poses (animation frame count) |
|---|---|
| **Small** | stand, walk (2), run at P-speed (2, arms out), skid, jump, P-jump (arms out), kick, hold-stand, hold-walk (2), swim (3), climb (2), pipe/front-facing, slope-slide (sit), death, star somersault (uses flips of the jump frame) |
| **Super** | stand, walk (3), run P (3, arms out), skid, jump, P-jump, fall (if distinct), duck, kick, hold-stand, hold-walk (3), swim (4), climb (2), front-facing, slope-slide, grow/shrink in-between frame |
| **Fire** | Super set in the Fire palette + throw (1) |
| **Raccoon** | Super set with tail + tail-wag in air (3), tail-spin (4: side / front / other side / back), fly (arms out + wag), duck |
| **Tanooki** | Raccoon set in the Tanooki suit + statue (1) |
| **Frog** | stand, hop (3), swim (4 directional leg kicks), front-facing |
| **Hammer** | Super set with helmet and shell + throw (1) + shell-duck (1) |
| **Kuribo's Shoe** | in-shoe stand, in-shoe hop/squash (2) |
| **SMB4 new suits** | per 03 §4.3 (only for suits that pass the prototype gate) |

Frame *timing* (walk-cycle speed vs horizontal velocity, skid and turn frames) is specified in [01 §13](01-game-feel-and-physics.md), because it's part of the feel.

### 2.2 Everything else
- **Enemies:** the per-enemy frame lists live with each enemy spec in 03 §7 (typically a 2-frame walk + squished/flipped/shell frames).
- **Effects:** smoke puff (suit loss, tail kill, enemy poof — 3 frames), brick debris (4 pieces, 1 frame each, flipped), coin spin (4) + sparkle, score pop-ups (100, 200, 400, 800, 1000, 2000, 4000, 8000, 1UP), skid dust (2), water splash (3), bubbles, fireball (4) + fireball puff, star sparkle, "hit" flash star (shell impacts), P-switch pressed, vine sprout.
- **Tilesets (by theme):** plains, underground, desert/canyon, beach/water, jungle, sky/clouds, ice, machine/pipe, volcanic/dark, fortress, castle, airship/tank/battleship, bonus rooms (Toad house, spade game, coin ship), goal "backstage" area with the roulette box.
- **World maps:** 16×16 map tiles with animated water, flowers and bobbing hills (SMB3's map has lots of idle motion), path dots and segments, numbered level panels (2 states: open / cleared "M"/"L"), Toad houses, spade panels, fortresses (intact/destroyed), locks and gates, bridges, rocks, pipes, castles, the airship (animated), wandering Hammer Bros, and per-world landmark set pieces.
- **Bosses:** Boom Boom (fortress), 7 Koopalings (unique poses + wand magic), Bowser (large multi-sprite, fire breath, ground-pound squash).

---

## 3. HUD and UI

### 3.1 Status bar (SMB3 layout, bottom of the screen, never scrolls)
The playfield is scanlines 0–191. The status bar is scanlines **192–239 (48 px = 6 tile rows: border, 2 text rows, border, black padding)**.
```
┌────────────────────────────────────────────────┐ ┌────┐┌────┐┌────┐
│ WORLD 1  ▶▶▶▶▶▶(P)                   $ 12   │ │ 🍄 ││ ✿ ││ ★ │   ← goal cards (3)
│ Ⓜ × 4   0012340                      ⏱ 294  │ │    ││    ││    │
└────────────────────────────────────────────────┘ └────┘└────┘└────┘
```
- **Fields (SMB3 parity):** world number; **P-meter = 8 tiles** (6 arrows + a 2-tile "P" badge); coins (2 digits); M/L badge + lives (2 digits, max 99); **score** (6 digits + a fixed trailing 0); **time** (3 digits); **3 card slots**.
- **P-meter:** arrows fill as the meter charges; while it's full the "P" flashes and the P-meter beep loops (see 01 §7).
- **Timer:** turns to the hurry-up state below 100; counts down in the time-bonus tally at level end.
- **Cards:** up to 3; the bonus fanfare plays on a 3-card set (rules in 03 §10).
- The same bar is used on the world map.

### 3.2 Screens and menus
- **Title:** the curtain rises on a short attract scene (SMB3 did an animated vignette), then shows *1 PLAYER GAME / 2 PLAYER GAME / OPTIONS*.
- **File select:** 3 slots showing world, lives, and the collected set of secrets.
- **Options:** Video (window mode, scaling, pixel aspect, filters, pacing mode, latency mode, render GPU), Audio (master/music/SFX volume, "Authentic/Clean" APU filter), Controls (rebinding, stick deadzone and down-cone, SOCD policy), **Gameplay (Feel profile: Classic / Modern; see 01 §9)**, Accessibility (§5).
- **World intro card:** "WORLD 1" with the player's icon and lives. Level intro: short iris-in.
- **Pause (in level):** a "PAUSE" box, as in SMB3. The Modern profile adds *Resume / Exit level* (exit only for cleared levels, like SMB3's Start+Select rule).
- **Map item menu:** SMB3-style item panel (scrollable rows of item icons, cursor, "use" confirmation).
- **Text boxes:** Toad houses, the kings' rooms after each airship (with the "letter" and item reward), ending.
- **Font:** 8×8 NES-style uppercase letters, digits and symbols (coin, clock, ×, arrows, card icons).

---

## 4. Audio direction

### 4.1 Rules
- **2A03 only** (2 pulses, triangle, noise, DPCM). No expansion chips, as in the US SMB3 cartridge.
- Typical roles: Pulse 1 = lead, Pulse 2 = harmony/counter-melody, Triangle = bass, Noise + DPCM = drums (SMB3's distinctive DPCM timpani/kick/bongo sounds).
- **Every player action has a sound.** SFX steal channels from the music for their duration (02 §6.3), which is what gives NES SFX their punch.
- Tempo is expressed in ticks per row (60 Hz) and must survive the hurry-up speed-up.

### 4.2 Music list (~34 tracks, all original)

| Group | Tracks |
|---|---|
| Front end | Title/curtain, File select, Game over, Ending, Credits, "Letter from the Princess" |
| World maps (9) | One theme per world, W1–W8, plus World 9 |
| Level themes | Plains/athletic, Athletic 2 (sky), Underground, Underwater, Desert, Jungle, Ice, Machine, Fortress, Airship, Castle/Bowser's keep, Bonus room/Toad house, Coin ship |
| Situational | Starman, P-switch, Hammer Bro encounter, Boss (Boom Boom), Koopaling, Bowser (2 phases), Bonus game (spade / memory) |
| Jingles | Course clear, 3-card bonus fanfare, Player down, Hurry up, World clear/king saved, Airship escape, Fortress lock opens, Item get |

### 4.3 Sound effects (≈ 60), with channel and priority

| SFX | Channel(s) | Priority | Notes |
|---|---|---|---|
| Jump | Pulse 1 | 3 | Same for all forms; the star somersault uses the same sound |
| Stomp | Noise + Pulse 2 | 4 | Chain stomps keep the same sound; the score pop-up changes |
| Kick (shell) | Noise | 4 | |
| Bump (block from below) | Pulse 2 | 3 | Also used for bonking hard blocks |
| Brick break | Noise | 5 | |
| Coin | Pulse 2 | 2 | Very frequent, so low priority |
| Power-up sprouts | Pulse 2 | 4 | |
| Mushroom / flower / star get | Pulse 1+2 | 6 | |
| Leaf get | Pulse 1 | 6 | Distinct whoosh |
| Suit transform poof | Noise | 6 | |
| Damage / pipe travel | Pulse 1 | 7 | Same sound for both, as in SMB3 |
| 1-UP | Pulse 1+2 | 8 | |
| Fireball / hammer throw | Pulse 1 | 3 | |
| Fireball hits wall | Noise | 2 | |
| Tail swipe | Noise | 3 | |
| Tail wag / fly flap | Pulse 1 | 3 | Repeats per wag |
| **P-meter full (loop)** | Pulse 2 | 2 | Loops while P is full on the ground; stops in the air (see 01 §7) |
| Skid | Noise | 1 | The low tick while skidding |
| Swim stroke | Pulse 1 | 3 | |
| Note/jump block bounce, spring | Pulse 2 | 3 | |
| Door | Noise | 5 | |
| Vine sprout | Pulse 2 | 4 | |
| P-switch pressed | Pulse 1+2 | 7 | Then the P-switch music starts |
| Shell ricochet on wall | Pulse 2 | 2 | |
| Cannon fire / Thwomp land / Sledge land | Noise (+DPCM) | 5 | |
| Boss hit / Koopaling magic / Bowser fire | various | 6 | |
| Pause | Pulse 1 | 9 | |
| Timer tick (time-bonus tally) | Pulse 2 | 5 | |
| Card roulette stop | Pulse 1+2 | 7 | |
| Map step, enter level, item use, Hammer Bro engage, lock open, airship moves | various | 5 | Map-only |

Priority rules: a new SFX preempts an active one on the same channel only if its priority is ≥ the active one's. Jingles (death, course clear) stop the music outright.

### 4.4 Loudness
- Default mix: Music 70 %, SFX 100 %.
- The master bus has a soft limiter only as a safety net. The NES mixer curve already compresses naturally, so tuning shouldn't depend on it.

---

## 5. Accessibility (presentation side)

| Option | Default | Effect |
|---|---|---|
| Reduce flashing | Off | Slows star palette cycling, replaces the P-switch/lightning flashes with a gentle pulse, and halves the damage-blink rate (the blink stays visible) |
| Screen shake | On | Toggle |
| Authentic flicker | Off | Enforces 8 sprites per scanline |
| HUD scale / high-contrast HUD | Off | Draws the status bar with a larger font outside the game viewport (pillarbox area) |
| Input display | Off | On-screen pad |
| Color filters | Off | Alternative palettes for common color-vision deficiencies. Gameplay-critical items are distinguishable by shape, not color |

---

## 6. Originality

All art, music and SFX are **original works in the SMB3 style**. No ripped sprites, tiles, samples, or transcribed melodies from Nintendo games. That keeps the art style coherent with the new content and removes the most obvious copyright problem. Trademark and fan-game considerations are covered in [PLAN.md](../PLAN.md) §9.
