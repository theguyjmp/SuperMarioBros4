# 01 — Game Feel & Physics Specification

> **The single source of truth for how the player moves.** The *Classic* profile is SMB3 reproduced
> rule-for-rule and number-for-number. The *Modern* profile is Classic plus a thin, invisible leniency
> layer (§9). Engine constraints (fixed 60 Hz tick, integer math, input latching) are in
> [02-engine-architecture.md](02-engine-architecture.md).

**Tags:**
- **[V]** verified in the community SMB3 disassembly during research (Sept 2026).
- **[V\*]** logic read through a summarizer or a parallel disassembly; bytes not individually checked.
- **[S]** secondary source.
- **[D]** derived by simulation from [V] rules (script: §15.5).
- **[NEW]** SMB4 addition. **[TUNE]** tunable in the Feel Lab. **[?]** not yet verified; measure against the reference before implementing.

Numbers in `$hex` are the game's native units (§2). "Tick" = one 1/60 s simulation step = one NES frame.

---

## 1. The SMB3 feel fingerprint

These ten properties are what "feels like SMB3" means in this project. Every rule below serves one of them.

1. **Heavy, never floaty.** Normal gravity is +5/16 px per tick². The gentle +1/16 applies only while A is held **and** Mario is rising faster than 2 px/tick. Let go and the heavy gravity returns on the next tick. Falls reach the 4 px/tick terminal speed in 13 ticks. [V]
2. **Variable jump through a gravity switch, not a velocity cut.** A tap reaches 20.6 px and a full hold 70.7 px (3.4× range), with every tick of hold adding height (§6.4). [V/D]
3. **Speed buys height and distance.** Takeoff velocity grows with speed: −3.5 → −4.0 px/tick. A standing jump clears 4 blocks, a running jump 5, a P-speed jump 6. [V/D]
4. **Momentum you can steer.** Accelerating is moderate (~0.875/16 px/tick², walk speed in 27 ticks), but **pressing against your motion brakes 2.3× harder** (2/16 px/tick²), on the ground *and in the air*. Turnarounds are snappy, while letting go carries you. There's no air friction; you steer the air, you don't stop in it. [V]
5. **Three speed tiers and the P-meter.** Walk 1.5, run 2.5, P-speed 3.5 px/tick. The P-meter gains a bar every 8 ticks at run speed and loses one every 24. Holding speed is a skill that unlocks P-speed and flight. [V]
6. **A rigid camera.** A 16-px horizontal dead zone, no smoothing, no look-ahead. The vertical scroll stays locked unless you're P-jumping, flying or climbing. The screen is stable and readable. [V\*]
7. **Point-probe collision.** Two foot probes (x+4, x+11) mean you can hang far over a ledge. The head uses one probe at x+8, so you slip past block corners. Walls push you out 1 px per tick; landings settle 1–2 px per tick. [V]
8. **Instant feedback.** Sound and animation fire on the same tick as the action. The skid starts at |v| ≥ 2 with its sound and dust. [V]
9. **Freezes only for transformations** (23–47 ticks), never for stomps or hits, so the flow never stutters. [V]
10. **Everything is integer and deterministic**, down to the 16-tick acceleration dither that gives SMB3's speed-up its exact texture (§2). [V]

---

## 2. Units and the numeric model [V]

| Quantity | Representation | Notes |
|---|---|---|
| Position | 1/16 px (`int32`); 4 bits of subpixel | SMB3 keeps the subpixel in the upper nibble of a fraction byte; equivalent |
| Velocity | signed 1/16 px per tick (`int16`) — "4.4 fixed point" | `$10` = 1 px/tick = 60 px/s = 3.75 tiles/s |
| Acceleration | 8.8 value (`int8 whole`, `uint8 frac`) applied as **whole + carry(frac + CW)** | `CW` is a global dither byte, below |
| Dither `CW` | each tick: `CW = ((CW & $F0) − $90) & $FF` | Cycles through all 16 multiples of `$10`. Average accel is exactly `whole + frac/256`, deterministic per tick |

**Position update.** `pos += vel` every tick. X velocity is clamped to ±`$40` when applied. Fall speed is capped at `$40` when applied; the stored value can briefly read up to `$45`, and that's harmless.

**Direction handling [?].** The tables are written for rightward motion. Leftward motion must use exactly SMB3's mirroring of the 8.8 value, because the carry pattern of a negated fraction differs tick by tick. The parity suite runs every maneuver in both directions to lock this down.

| Speed | px/tick | px/s | tiles/s | Screen width (256 px) in |
|---|---|---|---|---|
| `$08` (enemy walk) | 0.5 | 30 | 1.9 | 8.5 s |
| `$18` walk | 1.5 | 90 | 5.6 | 2.8 s |
| `$28` run | 2.5 | 150 | 9.4 | 1.7 s |
| `$38` P-speed | 3.5 | 210 | 13.1 | 1.2 s |
| `$40` max / terminal fall | 4.0 | 240 | 15.0 | 1.1 s |

---

## 3. Per-tick order of operations [V]

Replicate this order exactly. It determines ledge-jump timing, landing timing and collision outcomes.

1. Advance `CW`; run death and pit checks.
2. **Player control**
   1. Duck / low-clearance / water / vine handling.
   2. X move: `XVel += SlideRate` → apply X velocity (clamp ±`$40`) → `XVel −= SlideRate`. Slope and conveyor pushes move the *position* only.
   3. Compute the move direction (sign of `XVel`) and |`XVel`|.
   4. **If `InAir` (from the previous tick):** apply Y velocity (fall capped at `$40`).
   5. Ground animations (skid detection, walk cycle).
   6. Horizontal control (§5).
   7. Jump / fly / flutter, including gravity (§6, §11.4).
   8. Tail-wag logic (flight speed limit, wag counter).
3. P-meter update (§7), then camera (§12).
4. **Tile collision:** sample the 4 probes (special-tile reactions such as bumps and coins happen here), then resolve **walls**, then **vertical** (§10). Then tail-attack block hits, then special tiles (conveyors, ice flag, quicksand).
5. Countdown timers (including the P-meter counter).
6. Objects update; object ↔ player contacts (stomps, damage, pickups, kicks).

**Consequences to preserve**
- Position always moves by the **previous tick's** velocity.
- The jump check uses the **previous tick's** `InAir`, and control runs *before* collision. **Pressing A on the tick your feet leave a ledge still jumps**: SMB3's built-in 1-tick grace.
- Gravity is applied **on the jump tick itself** (after the jump velocity is set).
- Stomp and note-block bounce velocities are set in step 6, so the **first bounce movement is the full velocity** (no same-tick gravity).

---

## 4. Input semantics

| Action | SMB3 input rule [V] |
|---|---|
| Jump | **New** A press (`Pad_Input`), with the ground rule in §6.1 |
| Variable height | A **held** (`Pad_Holding`) |
| Run | B **held** |
| Fireball / hammer / tail spin | **New** B press |
| Kick a carried object | B **released** |
| Duck | Down held (+ conditions, §5.9) |
| Slope slide | Down on a slope (§5.6) |
| Door | **New** Up press on the ground |
| Vine | Up (from the ground) / Up or Down (in the air) on a vine tile |
| Pipe | Direction held into the pipe mouth |
| Facing | Follows whichever of L/R is held, **including midair**, except during a tail spin |

SMB4 additions (both profiles): **edge latching** (sub-tick taps are never lost), SOCD rules and stick-to-d-pad cones (02 §7.3). They improve input *quality*, not physics.

---

## 5. Horizontal movement [V]

### 5.1 Speed caps (identical on the ground and in the air)

| Condition | Cap | px/tick |
|---|---|---|
| B not held | `$18` | 1.5 |
| B held | `$28` | 2.5 |
| B held **and** P-meter full (`Power = $7F`) | `$38` | 3.5 |
| Uphill, without / with B | `$0D` / `$16` | 0.81 / 1.38 (no P-meter charging uphill) |
| Underwater: on the floor / swimming | `$08` / `$18` | 0.5 / 1.5 |
| Absolute clamp | `$40` | 4.0 (reachable only via slopes) |

### 5.2 Acceleration table (velocity units per tick)
Rows are chosen by the suit. B changes only the *cap*, not the rate. Ice rows replace the suit row while grounded in ice levels.

| Row | Friction (no L/R, on ground) | Accelerate (toward cap) | Skid (L/R against motion) |
|---|---|---|---|
| Small, Tanooki, Hammer | −0.625 (`$FF.60`) | +0.875 (`$00.E0`) | +2.0 |
| Big, Fire, Raccoon | −0.875 (`$FF.20`) | +0.875 | +2.0 |
| Frog | −1.0 | +2.0 | +2.0 |
| Ice 1 (snow ground) | −0.375 | +0.875 | +0.75 (+1.125 with B) |
| Ice 2 (ice blocks, frozen coins/munchers) | −0.1875 | +0.875 | +0.375 (+0.75 with B) |
| Underwater, on floor / swimming | −0.8125 / −0.125 | +1.0 / +0.1875 | +1.0 / +0.5 |

### 5.3 Algorithm (step 2.6 of §3)
```text
dir     = held L/R after SOCD (−1, 0, +1)
moveDir = sign(XVel)
if dir == 0:
    if grounded and XVel != 0: move XVel toward 0 by the friction rate (never past 0)
    # airborne: no change at all (no air friction)
elif moveDir != 0 and dir != moveDir:                 # pressing against motion
    XVel += dir * skidRate                            # on the ground AND in the air
else:                                                 # pressing along motion, or from rest
    cap = capFor(B held, Power, uphill, water)
    if   |XVel| < cap: XVel += dir * accelRate        # a 0/1-per-tick rate can't overshoot; Frog's 2 can
    elif |XVel| > cap and grounded: move |XVel| toward cap by the friction rate
    # airborne and above cap: keep the speed (a run-jump keeps run speed after you let go of B)
```

**Resulting behavior [D]**

| Maneuver | Ticks | Distance |
|---|---|---|
| Rest → walk `$18` | 27–28 | 19.0–20.5 px |
| Rest → run `$28` | 45–46 | 54.5–57.0 px |
| Walk → run | 18–19 | 35–38 px |
| Run → P `$38` (P-meter full) | 18–19 | 53–57 px |
| Let go of B at run speed, still holding forward → walk | ≈18 (Big) / ≈26 (Small) | — |

(Ranges come from the 16 possible dither phases.)

### 5.4 Stopping and skidding [D]

| From | Skid (hold opposite) | Friction, Big/Fire/Raccoon | Friction, Small/Tanooki/Hammer |
|---|---|---|---|
| Walk `$18` | 12 ticks, 9.8 px | 27–28 ticks, 20.5–22.3 px | 36–39 ticks, 27.8–31.3 px |
| Run `$28` | 20 ticks, 26.3 px | 45–46 ticks, 57–60 px | 63–64 ticks, 78–84 px |
| P `$38` | 28 ticks, 50.8 px | — | — |

**Skid feedback [V]:** the skid frame and skid sound start on any tick where the player is grounded, not in water, pressing against the motion, with |`XVel`| ≥ 2. Once `XVel` crosses zero, the normal acceleration takes over in the new direction.

### 5.5 Ice [V]
The slippery state is recomputed **only while grounded** and only in ice levels. In the air the normal suit row applies, so jumping is an escape from ice. Stopping distances from walk / run: Ice 1 → 46–52 / 130–140 px; Ice 2 → 91–103 / 258–278 px [D]. The Penguin Suit (§11.13) replaces the ice rows with the normal rows.

### 5.6 Slopes [V]
- **Types:** 45° (push ±3) and 22.5° (push ±2), floor and ceiling variants; the push points downhill.
- **Walking:** `SlideRate = push`. It is added to *position only* each tick, so running downhill is slightly faster and uphill slower without touching `XVel`. The uphill caps are in §5.1.
- **Slope slide:** starts when **Down** is pressed on a slope and the form isn't Frog or Hammer (and not in Kuribo's Shoe). Each tick: `Slide += push` → clamp |`Slide`| ≤ `$40` → move `Slide` 1 toward 0 → `XVel = Slide`.
  - Net **+2/tick on 45°, +1/tick on 22.5°, −1/tick on flat ground**; max `$3F`.
  - The slide starts from ~0: the incoming `XVel` is discarded (replicate this; it's part of the feel).
  - It ends when airborne, in water, against a wall, or when L/R/Up is pressed. Sliding into enemies defeats them [S].
- Slope collision uses the sloped probe set (§10.2) and snaps Y to the slope surface, so walking down never "bounces".

### 5.7 Wind [NEW] [TUNE]
Wind zones reuse the conveyor mechanism: a constant `SlideRate` push of ±8 or ±16 (0.5–1 px/tick) applied to position only. Because `XVel` is untouched, every SMB3 control rule still holds inside wind. Gusts ramp by ±4 per 8 ticks and are telegraphed (leaves and sand particles) for ≥ 30 ticks before they change.

### 5.8 Conveyors and quicksand
- **Conveyor [V]:** `SlideRate` ±16, a 1 px/tick push on position only. Stopped while a P-switch is active.
- **Quicksand [S/?]:** slow sinking with a reduced jump; death only at the bottom of the screen. The sink rate and jump strength are **measured from the reference in M3** (not yet extracted).

### 5.9 Ducking and the duck-slide [V]
- **Duck** when Down is held, grounded, Big or larger (Small can't duck), not Frog, not carrying, not sliding, not in the shoe. In slope levels, also only when `SlideRate = 0`.
- **Duck-slide:** while ducking with no L/R held, the suit's friction applies. What SMB3 does with Down + L/R held is **[?]**; confirm with parity maneuver 11 before implementing.
- **Ducking persists through a jump** (the duck-jump fits through 1-tile gaps) and uses the small/ducking probes and hitbox.
- **Low-clearance rule:** if Big Mario is grounded under a solid tile at (x+8, y+10) (e.g. after standing up in a 1-tile tunnel), then `XVel = 0`, jumping is disabled and X moves +1 px per tick, **always to the right**. It's a quirk, but levels depend on it; keep it.

---

## 6. Vertical movement [V]

### 6.1 Jump start
- **Condition:** a **new** A press AND (`InAir == 0` on the previous tick **or** `AllowAirJump > 0`).
- **Velocity:** `YVel = −$38 − SpeedJumpInc[|XVel| >> 4]`:

| \|XVel\| at takeoff | Jump YVel | px/tick |
|---|---|---|
| `$00–$0F` | −`$38` | −3.5 |
| `$10–$1F` | −`$3A` | −3.625 |
| `$20–$2F` | −`$3C` | −3.75 |
| `$30–$3F` | −`$40` | −4.0 |
| `$40` (slopes only) | −`$38` | SMB3 table-overrun quirk; keep it in Classic |

- Also on the jump tick: jump SFX, tail-suit `WagCount = $10`, **flight arm** (`FlyTime = $80` when `Power = $7F` and `FlyTime = 0`, for any suit), and the star somersault when applicable (§11.2).
- `AllowAirJump` is a countdown SMB3 sets from certain platforms: bolt lift 16 ticks, Para-Beetle 5, tilting platforms 8/16. It's SMB3's own coyote mechanism, and Modern reuses it (§9.2).

### 6.2 Gravity
Every airborne tick, **after** the position update (including the jump tick):
```text
if YVel < −$20 and A is held and no Micro-Goomba is attached:  YVel += 1
else:                                                          YVel += 5
fall speed capped at $40 when applied (§2)
```
- There is **no velocity cut** when A is released. The switch to +5 *is* the variable jump.
- A clinging **Micro-Goomba** disables the +1 branch, capping jumps near tap height. Note blocks set the same flag to ignore A during their launch (§6.5).

### 6.3 Jump arcs [D]

| Takeoff | YVel | Full hold: apex (tick) | Airtime | Tap: apex (tick) | Tap airtime | Horizontal distance (full) | Max gap* | Max ledge |
|---|---|---|---|---|---|---|---|---|
| Standing | −`$38` | **70.7 px** (30) | 54 | 20.6 px (11) | 23 | 0 | — | **4 tiles** |
| Walk `$18` | −`$3A` | **77.8 px** (32) | 58 | 22.1 px (12) | 24 | 87 px | **5 tiles** | 4 tiles |
| Run `$28` | −`$3C` | **85.1 px** (34) | 62 | 23.6 px (12) | 25 | 155 px | **10 tiles** | **5 tiles** |
| P `$38` | −`$40` | **100.4 px** (38) | 70 | 26.8 px (13) | 27 | 245 px | **15 tiles** | **6 tiles** |

\* Max gap = horizontal distance + 6 px, because takeoff can happen with the rear foot probe (x+4) at the edge and landing needs only the front probe (x+11) over ground. These are frame-perfect limits; the level-design "comfortable" limits are in §15.1.

### 6.4 Variable-height control (standing jump) [D]

| A held (ticks) | 1 | 2 | 4 | 6 | 8 | 10 | 12 | 14 | 16 | 18 | 20 | 22 | ≥24 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Apex (px) | 20.6 | 23.4 | 28.7 | 33.8 | 38.7 | 43.4 | 47.9 | 52.2 | 56.3 | 60.2 | 63.9 | 67.4 | 70.7 |
| Airtime (ticks) | 23 | 25 | 28 | 31 | 34 | 36 | 39 | 42 | 44 | 47 | 50 | 52 | 54 |

Height responds to hold length almost linearly (≈2.3 px per tick) across the whole range. That near-linear response is why SMB3's jump feels precisely controllable.

### 6.5 Bounces [V/D]

| Bounce | Velocity | Apex | Notes |
|---|---|---|---|
| Stomp, A held | −`$40` | **104.4 px** | Holding A only enables the +1 gravity; pressing it a few ticks late still helps |
| Stomp, no A | −`$40` | 27.6 px | |
| Note block (plain launch) | −`$38`, A ignored | 21.4 px | Mario rides the block ~10 ticks first (velocity follows the bounce curve `$40…0…−$40`) |
| Note block, **new A during the ride** | −`$70`, A ignored | **81.9 px** | Also the white "coin heaven" note block |
| Note block hit from below | +`$20` | — | Pushes Mario down |
| Water exit (Up + A at the surface) | −`$34` | 18.6 px | §11.6 |
| Kuribo's Shoe hop | −`$20` | 7.4 px | §11.8 |
| Wood block | — | — | Only side hits react; no bounce from the top |
| Springboard | — | — | **NES SMB3 has none.** SMB4 may add one [NEW]: −`$50` normal, −`$78` with a timed A; reuses the note-block ride logic |

### 6.6 Ceilings [V]
While rising, a solid tile at the **head probe (x+8)** stops the rise: `YVel = 0` (in autoscroll levels it matches the scroll's vertical velocity instead). There's no downward push and no freeze, and the block is bumped (special-tile reaction). Because the head probe is a single central point, **clipping a block's corner with the edge of your head doesn't bonk** (~7 px of forgiveness on each side). That's SMB3's natural corner correction.

### 6.7 Free fall from rest [D]
Distance fallen: 4 ticks 1.9 px · 8 ticks 8.8 px · 12 ticks 20.6 px · 16 ticks 36.4 px · 20 ticks 52.4 px. Terminal speed (4 px/tick) is reached after **13 ticks**.

---

## 7. The P-meter [V]

| Element | Rule |
|---|---|
| Storage | `Power`: 7 bits = 6 arrows + P; full = `$7F`. `PMeterCnt`: counts down every tick. |
| "Running" | Grounded **and** B held **and** not sliding **and** \|`XVel`\| ≥ `$28` |
| Holding full | If running with `Power = $7F` and `FlyTime = 0`: `PMeterCnt = $10` (it stays full) |
| Charge | When `PMeterCnt` hits 0 while running: `Power = (Power << 1) | 1`, reload **8** |
| Decay | When `PMeterCnt` hits 0 while not running: `Power >>= 1`, reload **`$18` (24)** |
| In the air | Counts as "not running", so the meter decays, **except** during a P-jump (`FlyTime ≠ 0`), where P stays full |
| Full P unlocks | The `$38` cap (with B), flight arm on jump (tail suits fly; for other suits `FlyTime` clears on landing, so P stays full through the whole P-jump), the P-run pose, the "P" flash and beep |

**Timing [D]**
- The first bar arrives 1–24 ticks after reaching run speed (it depends on where the idle counter is), then one bar every 8 ticks: **full P 49–72 ticks after reaching run speed**.
- After you stop running at full P, the first bar is lost 16 ticks later, then one every 24.
- **Runway from a standstill to full P:** 94–117 ticks (1.6–2.0 s) over **178–235 px (11–15 tiles)**. Levels that expect flight provide **≥ 16 tiles** of flat runway (§15.1).

**Flight timer:** `FlyTime = $80` on a P-jump and decrements every 2nd tick, giving **256 ticks (4.27 s)**. P stays full during flight; when `FlyTime` reaches 0, `Power` is zeroed instantly. **P-Wing:** the level starts with `Power = $7F` and `FlyTime = $FF`, which never decrements.

---

## 8. Player state model

**Movement modes** (exactly one at a time): `Ground` (stand, walk, run, skid, duck, duck-slide), `SlopeSlide`, `Air` (rising, falling; flutter/fly sub-states), `Swim`, `FrogSwim`, `Climb`, `Statue`, `Shoe` (ground/air), `PipeTransit`, `DoorTransit`, `Transform` (game frozen), `Dying`, `AutoWalk` (course clear), `Cutscene`.

**Overlays** (combinable): `Carrying`, `HurtInvincible` (blink), `Star`, `TailSpin`, `KickPose`, `ThrowPose`, `BehindScenery` (white-block secret), `MicroGoombaAttached`.

| From → To | Trigger |
|---|---|
| Ground → Air | Jump (§6.1); or **walk-off**: no floor under either foot probe → `InAir = 1`, `YVel = 0` (Modern starts coyote time here, §9.2) |
| Air → Ground | Falling (`YVel ≥ 0`) with a foot probe inside a floor tile less than 6 px deep (§10.3) |
| Ground → Ground/duck | Down + conditions (§5.9) |
| Ground → SlopeSlide | Down on a slope, not Frog/Hammer/Shoe |
| any → Swim / FrogSwim | Entering water (Frog Suit → FrogSwim) |
| Ground/Air → Climb | Up on a vine (Up or Down in the air), not in water, carrying or shoe |
| Ground → PipeTransit | Direction held into a pipe mouth with X inside the 10-px window |
| Ground → DoorTransit | New Up on a door tile at x+8 |
| Ground/Air → Statue | Tanooki: Down + new B |
| any → Transform | Power-up collected, or damage while not Small |
| any → Dying | Damage while Small; pit; lava; crushed; timer reaches 0 |

---

## 9. Leniency: Classic vs Modern

### 9.1 What SMB3 already forgives (both profiles)
- **1-tick ledge grace**: A on the tick your feet leave still jumps (§3).
- **Ledge support**: you stay up while *either* foot probe (x+4 or x+11) is over ground. Mario can stand with 11 of his 16 px hanging over the edge.
- **Corner slip**: the single head probe at x+8 (§6.6).
- **Generous stomps**: a stomp counts whenever Mario's feet are no more than ~13 px below a 16-px enemy's top and he isn't rising (§10.6).
- **Late-A stomp bounces**: the gravity rule checks A every tick, so a late press still adds height.
- **Platform grace**: `AllowAirJump` windows on specific platforms (§6.1).

### 9.2 Modern-only additions [NEW] [TUNE]

| Feature | Classic | Modern (default) | Exact rule |
|---|---|---|---|
| **Coyote time** | 1 tick (inherent) | **5 ticks** (range 3–7) | On a **walk-off** (§8), set `AllowAirJump = 5`, reusing SMB3's own mechanism. The window is cancelled by a jump, any upward velocity (bounces), water, vines, pipes or damage. A coyote jump is identical to a ground jump in every way: speed-based velocity, flight arm with full P, sound. Gravity runs normally during the window (no hovering). After 5 ticks Mario has dropped only 3.1 px, so the jump is visually indistinguishable. |
| **Jump buffer** | none | **4 ticks** (range 2–6) | A new A press that had **no airborne effect** is remembered for 4 ticks. On the first tick the player is grounded (`InAir == 0`) within that window, it counts as a new press, which is the earliest tick SMB3 allows a re-jump. **Presses that caused a flutter or flight lift are not buffered**, so tail-suit players get SMB3 landings. Holding A through a landing never jumps. Height still follows the held state. Any bounce (stomp, note block) consumes the buffer. |
| Note-block super bounce | new A during the ride | + a buffered press from up to 4 ticks *before* touching the block | Same −`$70` result |
| Edge latching | on | on | Input quality, not leniency (02 §4.3) |
| Pipe entry window | 10 px | 10 px | Already generous; unchanged |

**Invisibility rule:** Modern must produce **identical traces to Classic** on every parity maneuver that doesn't hit a leniency path (enforced in CI, 05 §4). Players should never notice Modern until they switch it off.

### 9.3 Deliberately **not** added (in either profile)
No apex hang-time or half-gravity at the peak (floaty). No jump-release velocity cut (the gravity switch already does this, better). No extra corner correction beyond the head probe. No ledge-grab or auto-mantle. No camera smoothing or look-ahead. No input smoothing on digital inputs. No acceleration curves beyond SMB3's tables.

---

## 10. Collision [V]

### 10.1 Frame of reference
The player's `(X, Y)` is the **top-left of a 16×32 box for every form**. Small Mario's graphics occupy the lower 16 px. All probe and hitbox offsets are relative to this corner.

### 10.2 Tile probes (x, y)
Four probes are sampled per tick: **feet** when `YVel ≥ 0` or **head** when `YVel < 0`, plus **one side pair**, chosen by which half of a tile the player is in (`X & 15`).

| Probe | Big | Small / ducking |
|---|---|---|
| Feet (`YVel ≥ 0`) | (4, 32), (11, 32) | same |
| Head (`YVel < 0`) | (8, 6) | (8, 16) |
| Sides when `X & 15 < 8` | (14, 27), (14, 14) | (13, 27), (13, 20) |
| Sides when `X & 15 ≥ 8` | (1, 27), (1, 14) | (2, 27), (2, 20) |
| **Slope levels:** feet / head / sides | (8, 32) / (8, 5) / x 13 or 3 at y 24 and 12 | (8, 32) / (8, 18) / x 13 or 3 at y 23 |

### 10.3 Resolution rules
1. **Sample** all probes; tiles react (bump, coin, special) during sampling.
2. **Walls:** a solid side probe pushes the player **1 px per tick** toward the free side. It never snaps, except in SMB3's alignment case (`(X + 2/14 big, 3/13 small) & 15 == 0`), which produces SMB3's wall-jump quirk; keep it in both profiles. `XVel` is zeroed only if moving into the wall.
3. **Floor** (tiles solid-on-top, checked when falling or grounded):
   - If the foot probe is **≥ 6 px deep** in the tile, the floor is **ignored** (fall through). This is also what makes one-way platforms passable from below.
   - Otherwise push up **1 px** (1 px deep) or **2 px** (2–5 px deep) per tick, and set `InAir = 0`, `YVel = 0`, and reset the stomp chain. Hard landings therefore settle over 1–2 ticks; don't "fix" this.
   - Grounded with **no floor under either foot**: `InAir = 1`, `YVel = 0` (walk-off).
4. **Ceiling:** §6.6.
5. **Slopes:** snap Y to the slope's height at the feet probe.

### 10.4 One-way and semisolid
Handled entirely by the rule in 10.3 and the per-tileset "solid on top / solid on all sides" thresholds (03 §5.1). Jumping up through a semisolid never snags, because the feet probes aren't checked while rising.

### 10.5 Object hitboxes (relative to the player corner)

| Form | x offset | width | y offset | height |
|---|---|---|---|---|
| Small / ducking | 4 | 8 | 17 | 13 |
| Big | 3 | 10 | 5 | 25 |

Whether the overlap test counts touching edges as overlapping is **[?]**; the parity suite settles it with a pixel-exact "touch" test.

### 10.6 Stomp vs. hurt (exact)
A contact is a **stomp** if `Player.Y ≤ Enemy.Y − H`, where **H = 19** by default, 17 for the Pile Driver Micro-Goomba and the hopping Cheep Cheep, and 8 for giant enemies. The following also apply:
- If Mario is **rising**, a stomp only counts when `FlyTime ≠ 0` or a stomp chain is active.
- If Mario is **grounded**, only when the enemy is airborne with `YVel < $0A` (it fell onto his head zone).
- **Never underwater.**

Otherwise the contact hurts Mario (or kicks a shell). On a stomp: `YVel = −$40`, and the chain counter advances (03 §10).
For a 16-px-tall enemy, this means Mario's feet may be up to 13 px below its top edge and the stomp still counts.

### 10.7 Damage, invincibility, death [V]
- **No damage while** any of these is active: hurt-invincibility, statue, star, suit-poof, game halted.
- **After a hit:** `$71` = **113 ticks (1.88 s)** of invincibility; the sprite blinks 2 ticks on / 2 off.
- **Degrade:** Shoe → lose the shoe only. Fire/Raccoon/Frog/Tanooki/Hammer → Big (23-tick poof). Big → Small (47-tick shrink). Small → death.
- **Death:** `XVel = 0`, `YVel = −$40`, frozen **48 ticks**, then falls with +2/tick gravity. 64 ticks after leaving the playfield → the map. Pit death uses a `$C0` countdown. **Crushed** when pushed to screen X ≥ `$F8` by autoscroll. The player can't rise more than 128 px above the level top.

---

## 11. Power-ups and special movement

### 11.1 Transformations (the game freezes during each) [V/V\*]

| Event | Duration |
|---|---|
| Mushroom (grow) / damage (shrink) | 47 ticks (0.78 s) |
| Gain or lose a suit (smoke "poof") | 23 ticks (0.38 s) |
| Fire Flower when already Big | 31 ticks (palette flash) |

### 11.2 Starman [V]
`$E0`, decremented every 2nd tick: **448 ticks (7.47 s)**. The level music returns for the last 64 ticks as a warning. **Somersault jump** when jumping while starred and not Small or Frog, not carrying, and `Power ≠ $7F`.

### 11.3 Fire [V\*]
Fireballs use **whole-pixel velocities**:
- X is ±3 px/tick, constant.
- Y starts at +3 px/tick (downward), gains +1 every 4 ticks, and is capped at 4.
- Floor bounce: −3 (−5 or −2 on slopes).
- A wall makes the fireball "poof" with a bump sound. Fireballs are deleted off-screen and have no lifetime timer.
- **Max 2 on screen** (the slots are shared with hammers). The throw pose lasts 11 ticks.

### 11.4 Raccoon / Tanooki [V]
- **Flutter:** each new airborne A press sets `WagCount = $10` (16 ticks; a new press refreshes it). While it's > 0 and not flying, a falling `YVel ≥ $10` is **clamped to `$10` (1 px/tick)** after gravity. Rising is unaffected.
- **Flight:** armed by a P-jump (§6.1). While `WagCount > 0` and `YVel ≥ −$18`:
  - `FlyTime ≥ $0F`: `YVel = −$18` (rise 1.5 px/tick).
  - `FlyTime < $0F`: sputter (`−$10` when `FlyTime & 8`, else 0).
  - `FlyTime = 0`: flight is over; only the flutter remains.
  - Tapping A continuously can climb **~356 px (≈22 tiles)** [D]: 228 ticks of full lift, then 28 ticks of sputter.
- **Air-speed limit:** while `FlyTime ≠ 0` or `WagCount ≠ 0`, if |`XVel`| ≥ `$18`, then |`XVel`| −1 per tick, giving an effective **`$17` (1.44 px/tick)** while flying or fluttering. The jump press itself sets `WagCount`, so **a raccoon's run-jump bleeds speed during its first 16 ticks [?]**. Confirm with parity maneuver R2 before relying on it.
- **Tail spin [V\*]:** a new B press without Down, when the timer is 0, gives **18 ticks**. It works in the air, and facing flips at ticks 11 and 3 (remaining).
  - Enemies are hit only when the timer reads 12 and 9, in a 10×15 box at x+17 or x−10 (the side opposite the current facing, so both sides get covered), y+16.
  - Blocks are hit on tick 9: one tile at (x−6 or x+21, y+28).
  - No cooldown beyond the 18 ticks.
- **Tanooki statue [V]:** Down + new B → 23-tick poof → statue **192 ticks (3.2 s)**. Releasing Down ends it early; it flashes for the last 96 ticks, and another 23-tick poof ends it.
  - Physics: `XVel = 0`; airborne `YVel += 7` per tick (fall capped at `$40`).
  - It can't be damaged, crushes "spiky" enemies it lands on, and kicks any shell it touches.

### 11.5 Frog [V]
- **On land:** no vertical motion (the hop is animation only). A 32-tick hop cycle; `XVel` resets to 0 at each hop start, then accelerates by +2/tick toward the normal caps.
- **In water:** the d-pad sets velocity directly: ±`$10` per axis, ±`$20` while A is held. With no input, velocity decays by 1 per tick toward 0. No sinking.

### 11.6 Swimming (other forms) [V]
- **Stroke:** new A from the floor → `YVel = −$20`; mid-water → `YVel −= $20`. Y is clamped to ±`$20`. No normal gravity.
- **Sinking:** +1 per tick while rising; while descending, +1 on 2 of every 4 ticks. More than 8 px above the screen top → +`$10`.
- **Horizontal:** caps `$18` swimming / `$08` on the floor; rates from §5.2.
- **Surface:** head out, feet in, rising → `YVel ≥ −$0C` and A/Up are ignored (Mario bobs). **Up + A → `YVel = −$34`** leaps out (18.6 px).

### 11.7 Hammer Suit [V\*/S]
Hammers fly at X ±`$10` plus Mario's `XVel` if he's moving the way he faces. Y starts at −3 px/tick and gains +1 every 8 ticks. They share the 2 projectile slots with fireballs. **Duck = shell**, immune to fire [S; the code wasn't located, so verify].

### 11.8 Kuribo's Shoe [V\*]
Normal ground and air control, and A-jumps. Pressing L/R on the ground starts a **hop** (`YVel = −$20`, 7.4 px, ~14 ticks). A new A mid-hop gives `YVel = −$38` (no speed bonus). It can stomp enemies flagged "hurts when stomped" (spiny, fiery) and munchers. A hit only removes the shoe. No ducking, vines or sliding.

### 11.9 Climbing [V\*]
Grab with Up (from the ground) or Up/Down (in the air); not in water, carrying or in the shoe. Up −`$10` (only if the vine continues above), down +`$10`, left/right ±`$10`. Velocity is 0 whenever nothing is held.

### 11.10 Pipes and doors [V/V\*]
- **Pipes:** direction held into the mouth, with X inside a **10-px window**. Entry lasts 60 ticks at 0.5 px/tick with the game frozen. Exit timers: up 63, down 7, sideways 32 ticks. The level timer pauses in pipes.
- **Doors:** a new Up press, grounded, with the door tile at x+8. `XVel = 0` and X snaps to the 16-px grid.

### 11.11 Carrying and kicking [V/V\*]
- **Pick up:** touch a stunned or shelled object while holding B (not already carrying, not in the shoe or statue). Touching it **without** B kicks it.
- **Kick** (release B): object X = ±`$30`, plus **half of Mario's `XVel`** if he's moving the same way (max `$4C`). The kick pose lasts 12 ticks.
- **Released inside a wall:** the object dies (100 pts, `YVel = −$40`).
- **Kicked shell:**
  - Hitting a wall bumps the block there (if on-screen), plays the bump sound and reverses direction.
  - If it lands with no X speed, it gets ±`$18`.
  - It kills enemies (victims fly with `YVel −$30`, `XVel ±$08`), with its own score chain.
  - Two shells destroy each other, and a carried object that touches an enemy kills both.
- There is no upward throw.

### 11.12 Timers summary [V/V\*]

| Timer | Value | Duration |
|---|---|---|
| Hurt invincibility | `$71` | 113 ticks (blink 2/2) |
| Star | `$E0`, −1 every 2nd tick | 448 ticks |
| Flight | `$80`, −1 every 2nd tick | 256 ticks |
| Flutter (per press) | `$10` | 16 ticks |
| Tail spin | `$12` | 18 ticks |
| Statue | `$C0` | 192 ticks |
| Kick / throw pose | `$0C` / `$0B` | 12 / 11 ticks |
| P-switch | `$80`, −1 every 4th tick | 512 ticks (8.5 s) |
| Pipe entry | `$3C` | 60 ticks |
| Death freeze | `$30` | 48 ticks |
| Level timer unit | reload 40, fires on underflow | **41 ticks** (confirm by trace) |

### 11.13 New suits [NEW] [TUNE]: physics sketches for prototyping
- **Boomerang Suit:** the boomerang launches at X ±`$30`, decelerates by 1 per tick (reverses after ~48 ticks, ≈5 tiles out) with a slight vertical sine arc (±8 px), then homes back toward the player at up to `$30`. One on screen. It collects coins and power-ups it passes through and carries them back, and hits `?` blocks from the side like a kicked shell. Otherwise Mario moves exactly as Big Mario.
- **Penguin Suit:**
  - The normal suit rows are used on ice (full traction).
  - **Belly slide:** starts with Down while grounded and |`XVel`| ≥ `$28`, beginning at the current `XVel`. Friction −0.125 per tick on flat ground; slope push as in §5.6; cap `$40`. The hitbox is the ducking box, and it defeats enemies on contact. A jumps out using the normal speed table.
  - **Swimming:** normal strokes, a `$20` horizontal cap with the "swimming" rates, and a leap out of the surface with −`$40`.
- **Lakitu's Cloud:** Up/Down set `YVel` to ∓`$10` (0 with neither); X uses the Big-Mario rows with the walk cap only. It lasts 600 ticks with a flashing warning over the final 120. No ducking, sliding or running.

---

## 12. Camera [V\*]

| Rule | Value |
|---|---|
| Horizontal | Keep the player's screen X in **[`$70`, `$80`] = [112, 128]**. Past 128, the camera moves right so it equals 128; below 112, it moves left so it equals 112. **No smoothing, no look-ahead**; left scrolling is free; clamped to the level bounds. |
| Level edges | The player's screen X is clamped to [16, 232]. Pushing against it drops the whole-pixel part of `XVel` (SMB3 quirk; replicate). |
| Vertical, mode 0 (normal horizontal levels) | **Locked to the bottom** of the level unless `FlyTime ≠ 0` (any P-jump) or climbing. Once scrolled up, it keeps the player's Y between **48 and 88 px** from the screen top: up at ≤ 3 px/tick, down uncapped, until it's back at the bottom. |
| Vertical, modes 1 / 2 | 1 = free (same 48–88 window); 2 = frozen |
| Vertical levels | Same 48–88 window; up ≤ 3 px/tick, down ≤ 4 px/tick |
| Autoscroll | Camera follows an authored path and speed. If the player's screen X < 16 or ≥ 224 and they're moving outward, `XVel` = scroll speed (pushed). Crushed at screen X ≥ `$F8`. |
| Shake | Vertical offset only (Thwomp, Sledge Bro, Bowser); gameplay coordinates never shake |

The camera never moves for any reason not in this table. That rigidity is one of the fingerprint traits (§1.6).

---

## 13. Animation and feedback timing [V]

| Element | Rule |
|---|---|
| Walk/run cycle | 4-frame cycle (idle = frame 2). Ticks per animation frame by \|`XVel`\| >> 3: **7, 6, 5, 4, 3, 2, 1, 1, 1** (so walking is 4 ticks per frame, run 2, P-speed 1). Uphill and ice add ticks. |
| Skid | Grounded, not in water, pressing against motion, \|`XVel`\| ≥ 2 → skid frame + skid SFX (+ dust every 4 ticks [NEW visual]) |
| P-run pose (arms out) | \|`XVel`\| ≥ `$37`, `FlyTime = 0`, not ducking, climbing, sliding or in the shoe |
| Facing | Follows held L/R every tick, including midair, except during a tail spin |
| Jump / fall frame | One jump frame while airborne (P-jump uses the arms-out frame) |
| Hurt blink | 2 ticks visible / 2 hidden |
| Kick / throw poses | 12 / 11 ticks |
| Sound | Every SFX is triggered on the same tick as its cause |

---

## 14. Objects and enemies: physics [V/V\*]
Spawn, despawn and respawn windows and slot limits are in [03 §7.1](03-game-design.md).

| Object | Rule |
|---|---|
| Goomba, Koopa (green/red), Buzzy Beetle, Spiny | X ±`$08` (0.5 px/tick), constant |
| Object gravity | +`$03`/tick, max `$40`. In water: +`$01`, max `$10`, max rise −`$18` |
| Paragoomba | X accel ±1 up to ±`$14`; hops −`$20` (small), −`$30` (big) [V\*/?] |
| Green hopping Paratroopa | Bounce −`$30` [?] |
| Flying Paratroopa | Accel ±1, limit ±`$10` |
| Bullet Bill | Accel ±1 up to ±`$18` |
| Cheep Cheep | Accel ±1, limit ±`$10` (axis [?]) |
| Kicked shell | §11.11 |
| Stomped/killed enemy knock-off | `YVel −$30`, `XVel ±$08` (shell kills) |

---

## 15. Verification

### 15.1 Level-design ruler [D]
**Max values are frame-perfect limits. Mandatory jumps must stay within the "comfortable" column.**

| Speed tier | Max gap | Comfortable gap | Max ledge | Comfortable ledge |
|---|---|---|---|---|
| Standing | — | — | 4 tiles (70.7 px) | 3 tiles |
| Walk | 5 tiles | 3 tiles | 4 tiles | 3 tiles |
| Run | 10 tiles | 6 tiles | 5 tiles | 4 tiles |
| P-speed | 15 tiles | 10 tiles (optional routes only) | 6 tiles | 5 tiles |
| Raccoon flight | ~22 tiles of climb in 4.27 s | — | — | — |

**Other design numbers**
- P-meter runway from a standstill: **16 tiles** (worst case 14.7).
- Flutter descent: 1 px/tick while tapping.
- Stomp bounce with A: 6.5 tiles; without A: 1.7 tiles.
- Note-block super bounce: 5.1 tiles.

`levelc` exports these as a Tiled overlay stamp (03 §9.3).

### 15.2 Invariant unit tests (Classic profile; must be exact)
1. Standing full jump apex = 1131/16 px at tick 30; lands at tick 54.
2. Tap jump apex = 330/16 px at tick 11.
3. Takeoff YVel matches the §6.1 table at |`XVel`| = `$0F`, `$10`, `$1F`, `$20`, `$2F`, `$30`, `$3F`, `$40`.
4. Rest → `$18` in 27 or 28 ticks for every dither phase; → `$28` in 45–46.
5. Skid from `$28` stops in exactly 20 ticks / 420 subpixel units (26.25 px).
6. P-meter: first bar within 1–24 ticks of `RunFlag`, then exactly 8-tick steps; decay 16 then 24-tick steps.
7. Flight lasts exactly 256 ticks; `Power` is 0 on the following tick.
8. Stomp bounces are exactly 1671/16 px (A held) and 442/16 px (no A).
9. Low-clearance push: +1 px/tick to the right, jump disabled.
10. Walk-off: `InAir = 1` and `YVel = 0` on the first tick with neither foot supported.
11. Foot probe at 6 px depth → falls through; at 5 px → pushed up 2.
12. Camera: from player screen X 128 moving right at `$28`, the camera moves 2 or 3 px per tick and never lags.

### 15.3 Leniency tests (Modern)
1. Walk off a ledge and press A on ticks 1…5 after the walk-off → a jump with the correct speed-based velocity. On tick 6 → no jump.
2. Press A 1…4 ticks before landing → a jump on the first grounded tick. At 5 ticks → none.
3. Hold A continuously through a landing → no jump.
4. A raccoon flutter tap 2 ticks before landing → **no** buffered jump.
5. Every Classic parity maneuver produces identical traces in Modern.

### 15.4 Parity maneuvers against the reference
The 12 canonical maneuvers are listed in [05 §3](05-roadmap-and-testing.md). Additional ones for this spec:
- **R1** raccoon flutter descent
- **R2** raccoon run-jump (confirms the §11.4 speed bleed)
- **R3** full flight
- **S1** slope walk up and down
- **S2** slope slide
- **W1** swim strokes, sinking and surface exit
- **I1** ice start and stop
- **N1** note block plain and super
- **K1** kick a shell at `$28` (speed `$44`)
- **D1** both directions of every maneuver above (dither mirroring, §2)

### 15.5 Derivation script
The tables marked [D] were produced by a small simulation of these exact rules. Port it to `tests/unit/physics_tables_test.cpp` so the doc and the engine can never disagree.

---

## 16. Reference RAM map (for the Mesen2 trace recorder)

| Value | Address |
|---|---|
| Player X (pixel / high / subpixel in upper nibble) | `$0090` / `$0075` / `$074D` |
| Player Y (pixel / high / subpixel) | `$00A2` / `$0087` / `$075F` |
| X velocity / Y velocity | `$00BD` / `$00CF` |
| In-air flag | `$00D8` |
| Facing (`FlipBits`, `$40` = right) | `$00EF` |
| P-meter `Power` / `PMeterCnt` | `$03DD` / `$0515` |
| `FlyTime` / ducking flag / suit | `$056E` / `$056F` / `$00ED` |
| Frame counter / pad held / pad new presses | `$0015` / `$0017` / `$0018` |
| Dither `CW` (Counter_Wiggly) | **unknown** — find it in Mesen2's memory search (a byte that steps by −`$90` in its high nibble every frame) and log it, so traces can align the dither phase |

---

## 17. Constants (`src/game/player/constants.h`, sketch)

```cpp
namespace phys {               // velocities: 1/16 px per tick
inline constexpr int16_t kWalkCap = 0x18, kRunCap = 0x28, kPCap = 0x38, kAbsCap = 0x40;
inline constexpr int16_t kUphillCap = 0x0D, kUphillRunCap = 0x16;
inline constexpr int16_t kWaterFloorCap = 0x08, kSwimCap = 0x18;

struct Accel { int8_t whole; uint8_t frac; };           // applied as whole + carry(frac + wiggly)
struct AccelRow { Accel friction, accel, skid; };        // friction is negative (toward 0)
// rows: Small/Tanooki/Hammer, Big/Fire/Raccoon, Frog, Ice1, Ice2, WaterFloor, Swim (values in §5.2)

inline constexpr int16_t kJumpBase = -0x38;
inline constexpr int16_t kJumpSpeedBonus[5] = { 0, 2, 4, 8, 0 }; // index |XVel|>>4; [4] = SMB3 overrun quirk
inline constexpr int16_t kGravLow = 1, kGravHigh = 5, kLowGravBelow = -0x20, kFallCap = 0x40;
inline constexpr int16_t kStompBounce = -0x40, kNoteLaunch = -0x38, kNoteSuper = -0x70;

inline constexpr uint8_t kPowerFull = 0x7F, kPChargeReload = 8, kPDecayReload = 0x18, kPHoldReload = 0x10;
inline constexpr uint8_t kFlyTimeArm = 0x80, kFlyTimePWing = 0xFF, kWagCount = 0x10;
inline constexpr int16_t kFlyLift = -0x18, kFlutterFallCap = 0x10, kFlightAirCap = 0x17;

inline constexpr uint8_t kHurtInvTicks = 0x71, kStarTimer = 0xE0, kGrowTicks = 0x2F, kPoofTicks = 0x17;
inline constexpr uint8_t kTailSpinTicks = 0x12, kStatueTicks = 0xC0, kKickPose = 0x0C, kThrowPose = 0x0B;

// Modern-only leniency (Classic = 0)
inline constexpr uint8_t kCoyoteTicks = 5, kJumpBufferTicks = 4;
}
```

Classic values are **locked**: the F2 tuning panel shows them read-only. Only the [TUNE] and [NEW] values are editable, and they save to `physics_profile.json`.
