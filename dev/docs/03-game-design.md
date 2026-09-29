# 03 — Game Design

> What the player does, meets, and explores. SMB3's systems are kept intact as the foundation
> (items marked **SMB3** are parity requirements); items marked **NEW** are SMB4's additions,
> each gated by prototyping. Movement physics is in [01](01-game-feel-and-physics.md).
> Values marked *(verify)* get confirmed against the reference game during M1–M3.

---

## 1. Premise (draft)

After his defeat in Dark Land, Bowser rebuilt his fleet and stole the **Sky Lanterns**, the eight lights that guide airships, clouds and travelers between the kingdoms of the Mushroom World. With the lanterns dark, each kingdom is cut off, and a Koopaling rules it from a flagship overhead. Mario and Luigi must cross all eight kingdoms, board each flagship, relight each lantern, and storm Bowser's volcanic keep, where Princess Toadstool is held.

**Tone:** SMB3's storybook stage-play charm. The theater framing continues (curtain title, backstage goals, "props" bolted to the scenery), and each world is a new "act". After each airship, the rescued king's room holds a letter from the Princess with an item, as in SMB3.

---

## 2. Structure and scope

| | Target |
|---|---|
| Worlds | **8 + World 9 (post-game "Encore")** |
| Stages | ~100 total (per §8): 61 numbered levels + 2 special (pyramid, tower), 14 fortresses, 7 airships, 6 W8 special stages (tank, fleet, air force, 3 hand traps), Bowser's castle, 9 W9 stages. SMB3 has about 90; World 9 is the first thing to cut if needed. |
| Bonus locations | Toad houses, spade panels, N-spade, white Toad houses, coin ships, Hammer Bro battles, warp zone |
| Session shape | Levels of 1–3 minutes; a world is 30–60 minutes for a first run |
| Full run | ~6–8 h for a first clear; ~12 h for 100 % |
| Modes | 1 Player; 2 Player alternating (SMB3 rules) + Mario-vs-Luigi battle; Time Attack on cleared levels (NEW, uses deterministic replays for ghosts) |

**Characters:** Mario (P1) and Luigi (P2) with **identical physics**, as in the NES SMB3. A post-game toggle, *Luigi Style* (higher jump, less traction), is an optional extra and never the default.

---

## 3. Two-player rules (SMB3 parity)
- Players alternate turns on the shared world map. **The turn passes when the active player finishes a level or loses a life.**
- Each player has their own lives, score, coins, power-up form, cards, **28-slot inventory** and completion flags. Cleared panels show **M** or **L** depending on who cleared them.
- **Battle mini-game:** pressing A while standing on the other player's map square starts a single-screen, arcade Mario Bros.-style arena. Spinies, Sidesteppers and Fighter Flies come out of the pipes, and the POW block works 3 times. You win by getting the most kills out of 5, or by making your rival touch an enemy. Hits steal cards, and the winner takes the next turn. There are 12 rotating arena variants, including coin rounds.
- On game over in 2P, choosing END lets the other player continue alone.

---

## 4. Power-ups and items

### 4.1 Player forms — SMB3 parity

| Form | Abilities | Hit → becomes | Source |
|---|---|---|---|
| **Small** | Run, jump; can't break bricks, can't duck | Dies | Default |
| **Super** (Mushroom) | Break bricks by bumping, duck, duck-slide | Small | `?` blocks, Toad houses, map item |
| **Fire** (Fire Flower) | B throws bouncing fireballs (2 on screen max) | **Super** | blocks, items |
| **Raccoon** (Super Leaf) | Tail spin (B) hits enemies and blocks at your sides; tail-wag to slow falling; **flight** with a full P-meter | **Super** (with a puff) | blocks, items |
| **Tanooki** (Tanooki Suit) | Raccoon abilities + **statue** (Down + B): invulnerable, immobile, crushes enemies when landing on them | **Super** | Toad houses, items |
| **Frog** (Frog Suit) | Superb swimming (8-way, fast); hops on land (slow, awkward) | **Super** | Toad houses, items |
| **Hammer** (Hammer Suit) | B throws hammers in an arc (defeat almost anything); duck into the shell = immune to fire | **Super** | Hammer Bros on the map, items |
| **Starman** | Invincible for a limited time, somersault jumps, kills on contact | (overlay on any form) | blocks |
| **Kuribo's Shoe** | Level-specific vehicle: hop, stomp spiny/fiery enemies and munchers safely | Lose the shoe | Stomp a shoe-riding Goomba |
| **P-Wing** (item) | Raccoon + unlimited flight for one level; lost on hit | Super | Map item |

Getting hit as any suit sends you to **Super**, not Small. This SMB3 rule makes suits feel safe to use. Collecting a power-up you already have (or a lesser one) awards points without changing form. Collecting a new one replaces the current form (the swap freezes the game briefly; timing in 01 §11).

### 4.2 Inventory items (world map) — SMB3 parity
Capacity **28 per player** (4 rows × 7). Used from the map item panel, on the map only. SMB3 has 13 item types. What SMB3 does when the inventory is full is unverified; SMB4 rule: the new item replaces nothing and a "FULL" message shows, with the item given back at the next Toad house.

| Item | Effect |
|---|---|
| Super Mushroom, Fire Flower, Super Leaf, Frog Suit, Tanooki Suit, Hammer Suit | Start the next level in that form |
| Starman | Start the next level invincible |
| P-Wing | Raccoon form with infinite flight for one level |
| Jugem's Cloud | Skip one level (placed on a node, lets you pass it) |
| Hammer | Smash map rocks, or break a lock to open a shortcut |
| Music Box | Puts wandering map enemies to sleep for a few turns |
| Anchor | Prevents the airship from flying away when you fail it |
| Warp Whistle | Go to the Warp Zone (3 in the game) |
| **NEW (optional):** Stopwatch | Freezes wandering map enemies for 5 moves |
| **NEW (optional):** Lantern Map | Reveals the current world's hidden paths and white-Toad-house conditions |

### 4.3 NEW suits for SMB4 — candidates behind a prototype gate

**Gate (all must pass, or the suit is cut):**
1. It adds a **new verb**, not a stat tweak.
2. It works inside NES constraints: 3-color sprite palette, 8×16 sprites, no new rendering features.
3. It has a **natural source** in the world (an enemy or place it comes from, like the Hammer Suit from Hammer Bros).
4. Playtesters prefer levels with it available, and it doesn't break existing levels (e.g., bypassing whole sections).

| Candidate | New verb | Rules sketch | Home |
|---|---|---|---|
| **Boomerang Suit** | Ranged *retrieval* attack | B throws a boomerang that flies ~5 tiles forward on a shallow arc and returns (1 on screen). It defeats most enemies, including fire-immune Buzzy Beetles; **collects coins and power-ups it touches** and carries them back; **bumps `?` blocks from the side**. Duck = no special defense. | Boomerang Bros (W2 map) |
| **Penguin Suit** | Belly-slide + ice mastery | Full traction on ice. At run speed or faster, Down starts a **belly slide**: low friction, fits 1-tile gaps, defeats enemies on contact, jump to exit. Swims with momentum (between Super and Frog), with a leap out of the water at the surface. | W6 (Frostbite Peaks) |
| **Lakitu's Cloud** (vehicle) | Free vertical movement for a short time | Stomp a Lakitu to take its cloud. Up/Down to rise/sink slowly, ~10 s before it dissipates (with a flashing warning). Immune from below; can't duck or run. | W5 (Cloudtop) |

---

## 5. Blocks, tiles and level objects

### 5.1 Tile collision classes (metatile property)

| Class | Behavior |
|---|---|
| `EMPTY` | Nothing |
| `SOLID` | Solid from all sides |
| `SEMISOLID` | Solid only from above when falling onto it (thin platforms, and the tops of SMB3's big colored bolted blocks, which you can pass through from below and the sides). SMB3 implements this with two per-tileset thresholds per palette quadrant of the tile index: "solid on top only" and "solid on all sides". |
| `SLOPE_*` | 45° and 22.5° up/down, floor and ceiling variants (movement 01 §5.6, collision 01 §10.2) |
| `WATER` / `WATER_CURRENT_*` | Swimming zone; currents push |
| `HAZARD_HURT` | Munchers, spikes: damage on contact (like an enemy) |
| `HAZARD_KILL` | Lava, bottomless pit: death |
| `ICE` | Slippery floor (01 §5.5) |
| `CONVEYOR_L/R` | Moves the player standing on it |
| `QUICKSAND` | Slow sinking with a reduced jump; death only when the player sinks to the bottom of the screen (SMB3). Sink and jump numbers are in 01 §5.8. |
| `CLIMBABLE` | Vines and ladders (Up to grab) |
| `DOOR`, `PIPE_ENTRY_*` | Transitions (01 §11) |
| `COIN` | Collected on overlap |
| `BUMPABLE` | Block that reacts when hit from below or by a tail/shell/boomerang (see 5.2) |
| `WIND_*` (**NEW**) | Horizontal wind zone (W2, W5); adds a small constant X acceleration (01 §5.7) |

### 5.2 Blocks

| Block | Behavior |
|---|---|
| `?` block | Releases its contents (coin / power-up / 1-UP / vine / star / P-switch), then becomes a used block. Power-up contents depend on form: Small gets a Mushroom, anyone else gets the block's "big" item (Flower or Leaf). |
| Brick | Small: bumps. Super+: breaks into 4 debris pieces (10 pts; verify). May hide contents (then it acts like `?`). |
| Multi-coin brick ("10-coin") | Gives a coin on each hit, then turns used. Whether SMB3 caps it by count or by a time window is unverified; check in the reference game during M2. SMB4 default: up to 10 coins within ~4 s of the first hit. |
| Big `?` block | 32×32 block (Giant Land homage); one variant holds a 3-UP |
| Invisible block | Appears only when hit from below |
| Note block | Bounces the player; pressing A at the bounce gives a big bounce (01 §6.5). Some hold items; some are invisible (white) and lead to coin heavens. |
| Wood block | Solid. Bumped from below it bounces and may release an item; the tail can hit it |
| Ice block (throwable) | A frozen coin or enemy; fireballs melt it; can be carried and thrown like a shell |
| Jump block | Springs the player higher than a note block (01 §6.5) |
| P-switch | Stomped: a counter starts at `$80` and drops every 4 ticks, so the effect lasts **512 ticks (8.5 s)**. Coins ↔ bricks swap, still munchers become coins, background tile animation and conveyors stop, and some switches reveal a **P-door**. Special music plays. |
| Donut lift | Shakes, then falls a short time after being stood on. **SMB3: it does not come back.** |
| White block (secret) | Duck on it for **~6 s** to set the "behind" state: the player's sprites draw *behind the scenery*. The behind timer counts down every 2nd frame, and the player only returns to the foreground when standing in an empty (sky) tile. SMB3 hid a Warp Whistle this way (1-3). |

**Bump-from-below rule (SMB3, verified):** a bump from below (by the head, or a kicked shell/tail hitting the block) broadcasts a "bumped" block event. Enemies flagged *bumpable* that are standing on that block are flipped or killed (Goombas, Koopas, Paragoombas, Buzzy Beetles, Spinies, the Bros, Boom Boom and others). **Coins on top pop out** (verify in the reference). **Items on top** (mushrooms, stars) get knocked away from the hit side (SMB4 rule; SMB3 behavior unverified).

### 5.3 Platforms and gadgets
Falling platforms (drop on contact), path-following lifts, line-riding lifts, bolt lifts, tilting platforms, rotating platforms, seesaws/scales (linked pulley platforms), Para-Beetle rides (flying beetles you stand on), conveyor belts, pipes that shoot the player (cannon pipes, airship), cloud platforms (semisolid). **NEW:** carryable springboards. NES SMB3 has none; physics sketch in 01 §6.5.

### 5.4 Hazards
Bottomless pits, lava (Podoboos jump out), spikes, munchers, quicksand, rising/falling water (NEW W3), wind (NEW W2/W5), autoscroll crush, Rotodiscs, cannons, burners (airship flame jets), Thwomps, falling icicles (NEW W6), avalanche snowballs (NEW W6).

---

## 6. Camera and scrolling behavior (design-level)
Camera rules must match SMB3 exactly (01 §12). The design consequences:
- Normal horizontal levels scroll both ways. The screen **does not follow normal jumps upward**. It scrolls up only when flying, climbing, or in areas flagged free-vertical. Put coins and landmarks where a player can see them.
- **Vertical levels** scroll up and down freely; horizontal screen-wrap is not used.
- **Autoscroll** levels move the camera along an authored path at an authored speed; the left edge pushes the player and crushes against walls.
- Camera **lock zones** (boss rooms, bonus rooms) are authored as rectangles.

---

## 7. Enemies

### 7.1 Spawning and object slots — SMB3 parity (verified in the disassembly)
**Both the windows and the slot counts are part of the feel.** They decide when things appear, how crowded a screen gets, and what backtracking does, so we copy them exactly.

**Slots and pools**

| Pool | Count | Use |
|---|---|---|
| Object slots | **8** | Slots 0–4: ordinary enemies and items (**a spawn silently fails if all 5 are busy**). Slots 5–7: reserved for special objects (the goal card always uses slot 5; the note-block bounce object uses a reserved slot) |
| Special objects | 8 | Enemy projectiles and effects (hammers, wand rings, debris…) |
| Cannon fire | 8 | Launchers/generators; they persist off-screen and are overwritten in circular order |
| Player projectiles | **2** | Fireballs and hammers (max 2 on screen) |
| Score pop-ups | 5 | Floating 100/200/…/1UP numbers |

**Level object list:** entries are `(type, x column, y row)`. Types from a reserved high range are triggers and generators (autoscroll, cannons, Cheep Cheep/Bullet generators), not actors. Up to **48 entries per area**, each with a "spawned" flag.

**Spawn window (horizontal areas):** each tick, the spawner looks at the column **16 px beyond the right edge** when the camera is scrolling right (camera X + 272), or **32 px beyond the left edge** when scrolling left (camera X − 32). Any unspawned entry in that 16-px column spawns. Vertical areas use camera Y − 32 (scrolling up) and camera Y + 288 (scrolling down). Entering an area clears all flags and spawns everything on the first screen.

**Despawn window:** each object checks one side every other tick (alternating by slot index + frame counter) and is removed when outside its window relative to camera X:

| Window | Range (relative to camera X) | Used by |
|---|---|---|
| Short | −48 … +288 | Small, simple objects |
| N4 | −80 … +320 | Medium |
| **N2** (default) | **−128 … +384** | Most enemies |
| Vertical areas | Y −80 … +320 | All |

Objects that fall far below the level are also removed.

**Respawn rule:**
- An object removed **for being off-screen** clears its "spawned" flag. It comes back (as its *original* type, e.g. a Koopa whose shell flew away) the next time its column reaches the spawn window.
- An object that is **killed** (stomped flat, poofed, knocked off) keeps its flag set and does not return until the area is reloaded.

**Sprite overload:** SMB3 rotates each slot's sprite order every frame, so overloads flicker instead of dropping sprites. SMB4 has no sprite limit by default (02 §5.1), but keeps the rotation logic for the *Authentic flicker* option.

### 7.2 SMB3 roster (parity set) and defeat matrix
Legend: ✔ defeats · ✖ immune (or it hurts you) · ↺ turns into a shell or stuns · — not applicable · **?** unverified, check in the reference game.
**Stomp and fire immunities come from SMB3's per-object attribute flags (verified in the disassembly)**; the other columns are from secondary sources.

| Enemy | Behavior (short) | Stomp | Fire | Tail | Shell | Star | Hammer |
|---|---|---|---|---|---|---|---|
| Goomba | Walks; starts moving toward the player | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Paragoomba | Hops; the brown variant flies over and drops Micro-Goombas | loses wings | ✔ | ✔ | ✔ | ✔ | ✔ |
| Micro-Goomba | Clings to the player, reducing jump height; shaken off by jumping | ✖ (can't be stomped) | ? | ? | ? | ✔ | ? |
| Pile Driver Micro-Goomba | Disguised as a brick; hops toward the player | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Giant Goomba / Grand Goomba | Large walkers (Giant Land homage) | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Goomba in Kuribo's Shoe | Hops; stomp it to take the shoe | ✔ | ✖ | ? | ✔ | ✔ | ✔ |
| Koopa Troopa (green / red, giant) | Green walks off ledges; red turns around at them | ↺ | ✔ | ↺ (flipped) | ✔ | ✔ | ✔ |
| Koopa Paratroopa | Green hops; red flies vertical/horizontal paths | → Koopa | ✔ | ↺ | ✔ | ✔ | ✔ |
| Buzzy Beetle (floor, and ceiling droppers) | Walks, or drops from the ceiling; fireproof | ↺ | ✖ | ↺ | ✔ | ✔ | ✔ |
| Spiny / Spiny Egg | Spiky walker; eggs thrown by Lakitu | ✖ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Lakitu | Tracks the player from its cloud, throws Spiny Eggs | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Piranha / Venus Fire Trap / Ptooie / big / sideways plants | Rise from pipes (not while the player stands next to the pipe); Venus aims fireballs; Ptooie juggles a spiked ball | ✖ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Nipper Plant (3 kinds) | Snaps; hops toward the player; spits fire | ✖ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Muncher | Invincible; hurts on contact; Star or Kuribo's Shoe make it safe to touch; P-switch turns it into a coin | ✖ | ✖ | ✖ | ✖ | ✖ | ✖ |
| Bullet Bill / Missile Bill (homing) | Fired from Bill Blasters | ✔ | ✖ | ✔ | ✔ | ✔ | ✔ |
| Bob-omb | Walks; a stomp stuns it (then it can be carried); explodes | stun | ✖ | ✔ | ✔ | ✔ | ✔ |
| Rocky Wrench | Pops out of airship hatches, throws wrenches | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Chain Chomp | Lunges on its chain; breaks free after a while | ✖ | ✖ | ✖ | ✔ | ✔ | ✔ (+ Tanooki statue) |
| Fire Chomp | Floats, spits fire, explodes | ? | ? | ? | ? | ✔ | ? |
| Hot Foot | Candle flame that walks when you look away | ✖ | ✖ | ✖ | ? | ✔ | ✔ |
| Fire Snake | Hops, trailing fire | ✖ | ✖ | ✔ | ✔ | ✔ | ✔ |
| Angry Sun | Swoops at the player | ? | ✖ | ? | ✔ ? | ✔ ? | ? |
| Boo Diddly | Chases when you look away | ✖ | ✖ | ✖ | ✔ | ✔ | ✔ |
| Stretch | Boo that rises out of white platforms | ✖ | ✖ | ✖ | ? | ✔ | ✔ |
| Thwomp (6 kinds) | Slams down or slides when the player comes close | ✖ | ✖ | ✖ | ✖ | ✔ | ✔ (+ Tanooki statue) |
| Podoboo (incl. ceiling) | Leaps from lava | ✖ | ✖ | ✖ | ? | ? | ? |
| Rotodisc (5 kinds) | Orbiting fire orb; immune to every weapon | ✖ | ✖ | ✖ | ✖ | ✖ | ✖ |
| Dry Bones | Collapses when stomped, then reassembles; only Star/hammer kill it permanently | collapse | ✖ | ✖ | ? | ✔ | ✔ |
| Hammer / Boomerang / Fire / Sledge Bro | Jump between tiers and throw; Sledge Bro landings stun a grounded player. **1000 pts** | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Cheep Cheep (all kinds) | Swim patterns; leapers arc out of the water; pool-to-pool jumpers | ✖ (most) | ✔ | ✔ | ✔ | ✔ | ✔ |
| Spiny Cheep | Chases underwater | ✖ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Boss Bass (Big Bertha) | Patrols under the water surface and swallows the player whole | ✖ | ✔ | ✖ | ✔ | ✔ | ✔ |
| Blooper / Blooper Nanny | Pulsing chase; the Nanny releases babies | ✖ | ✔ | — | ✔ | ✔ | ✔ |
| Lava Lotus | Underwater plant releasing fire spores | ✖ | ✖ | ✖ | ? | ✔ ? | ✔ ? |
| Jelectro | Stationary electric jellyfish; invincible | ✖ | ✖ | — | ✖ | ✖ | ✖ |
| Buster Beetle | Picks up blocks and throws them | ✔ | ? | ? | ✔ | ✔ | ✔ |
| Spike | Coughs up spiked balls and throws them | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Para-Beetle | Flying beetle you ride (a platform) | ride | ? | ? | ? | ? | ? |
| Tornado, Bowser-statue lasers | Hazards | ✖ | ✖ | ✖ | ✖ | ✖ | ✖ |

Every row gets a defeat-matrix test in M5 (05 §1). All **?** cells are resolved in the reference game before that enemy is implemented.

### 7.3 NEW enemies (SMB4 draft; each prototyped before being placed in levels)

| Enemy | World | Behavior | Defeat notes |
|---|---|---|---|
| **Dune Wiggler** | W2 | Arcs out of quicksand like a leaping Cheep Cheep, then dives | Stomp the head; fire/tail/shell kill |
| **Tumble Beetle** | W2 | Rolls with the wind; changes direction when the wind flips | Shell-like: stomp stops it and it can be kicked |
| **Sidestepper** | W3 | Crab from the arcade Mario Bros. (already in SMB3's battle game; promoted to a level enemy): the first hit (stomp or bump from below) makes it angry and faster, the second flips it | Stomp ×2; bump from below flips it instantly |
| **Snapvine** | W4 | Piranha that slides along vine tracks toward the player | Fire/tail/shell/hammer |
| **Gust Lakitu** | W5 | Blows wind puffs that push the player (reuses the wind physics) | Stomp; stealing its cloud gives the Lakitu's Cloud vehicle |
| **Chilly Koopa** | W6 | Belly-slides across ice and bounces off walls | Stomp → shell; fire kills |
| **Snowball Bro** | W6 | Throws snowballs that roll along the ground and grow | Like Hammer Bros |
| **Wind-up Goomba** | W7 | Clockwork walker; sprints, stops to rewind (can be picked up while rewinding) | Stomp, carry, throw |
| **Ember Chomp** | W8 | Chain-Chomp anchored to a lava geyser that lunges with the eruptions | Star/hammer only |

### 7.4 Bosses
- **Boom Boom** (every fortress, SMB3 parity): charges and jumps, with jumping and flying variants. Defeated by **3 stomps** or **5 fireball/hammer hits** (SMB3's hit counter starts at 37 and he dies at 32). On defeat he drops the **`?` orb** that clears the fortress.
- **Koopalings** (airships, one per world): a small cabin arena, wand shots aimed at the player, jumping, shell-spin after being stomped. **3 stomps**; SMB3 also keeps a separate hit counter initialised to 10 (probably fireballs; verify). SMB3's per-Koopaling twists: Wendy's bouncing rings, Lemmy's balls, Roy and Ludwig's stunning ground-pounds, Iggy's faster fire. In SMB4 they're reassigned to new worlds (W1 Lemmy, W2 Roy, W3 Iggy, W4 Wendy, W5 Morton, W6 Larry, W7 Ludwig), and each learns one **NEW** move (e.g., Wendy's rings bounce off the floor *and* ceiling; Morton's landings crack the floor).
- **Bowser** (SMB3 parity + NEW phase): phase 1 as in SMB3 (fire breath at different heights; jumps and ground-pounds that break the floor bricks; he's defeated when he falls through the floor, and the ~35-fireball alternative is unverified). NEW phase 2: the chase up the collapsing volcano tower, an autoscrolling vertical escape with lava rising.

---

## 8. The worlds of SMB4

| # | World | Theme gimmick | New mechanics & enemies | World-map feature | Stages (levels + fortresses + special) | Airship |
|---|---|---|---|---|---|---|
| 1 | **Mushroom Meadows** | Fundamentals; first leaf and flight | Classic roster | Simple branching paths, Toad houses, a spade panel, a hidden path opened with a hammer | 6 + 1 fortress | Lemmy's |
| 2 | **Sandstone Canyon** | Quicksand, pyramids, **wind** | Dune Wiggler, Tumble Beetle, Fire Snakes, an Angry Sun-style chaser; **Boomerang Suit** (from Boomerang Bros) | Pyramid interior stage; sandstorms that hide map paths until a fortress falls | 7 + 1 fortress + pyramid | Roy's |
| 3 | **Coral Coast** | Swimming, islands, **tides** (water level rises/falls within levels) | Sidestepper, Boss Bass, Bloopers, Cheep variety | Canoe travel; the tide goes out after the first fortress, opening new routes | 9 + 2 fortresses | Iggy's |
| 4 | **Jungle Jamboree** | Vines, canopy vertical levels, bounce mushrooms, Piranha jungle | Snapvine, Chain Chomps, Ptooies, Nippers | Overgrown paths (hammer), a canopy layer reachable via vines on the map | 7 + 2 fortresses | Wendy's |
| 5 | **Cloudtop Kingdom** | Sky, vertical levels, flight routes, Para-Beetles | Gust Lakitu, **Lakitu's Cloud** vehicle | Two-tier map: ground → spiral tower → sky layer (SMB3 W5 homage) | 9 + 2 fortresses + tower | Morton's |
| 6 | **Frostbite Peaks** | Ice physics, frozen blocks, avalanches, falling icicles | Chilly Koopa, Snowball Bro, **Penguin Suit** | Ice floes that drift between turns as map "platforms" | 10 + 3 fortresses | Larry's |
| 7 | **Gearworks** | Machines: conveyors, piston crushers, tracks, pipe mazes | Wind-up Goomba, Bob-omb & Rocky Wrench squads | Pipe network map + conveyor paths that move the player automatically | 9 + 2 fortresses | Ludwig's |
| 8 | **Bowser's Volcano** | Lava, darkness, tanks, battleships, eruptions | Ember Chomp, everything at full difficulty | A dark map revealed around the player (SMB3 W8 homage); hand traps; tank/fleet/air-force stages that roll across the map | 4 + tank + fleet + air force + 3 hand traps + fortress + castle | — (Bowser) |
| 9 | **Encore** (post-game) | Remixed expert stages, one per world, plus a finale | All | Theater-stage map; unlocked after the ending | 8 + finale | — |

**Warp Zone:** 3 Warp Whistles hidden in W1–W4, leading to a warp map, as in SMB3. The whistle locations are new, each taught by an environmental hint.

---

## 9. Level design guidelines

### 9.1 Teaching structure
Every level has one **core idea** developed in four beats: **introduce** it safely (no pit under the first encounter) → **develop** it with a complication → **twist** it (combine with an earlier idea, or invert it) → **conclude** with a short victory lap to the goal. Enemies are introduced alone before they appear in groups.

### 9.2 Screen and camera rules
- Design for the SMB3 playfield: **256×192 px = 16×12 metatiles** above the status bar (about 11.5 rows show on a TV that crops the top 8 lines). Nothing lethal may enter from off-screen faster than the player can react: at least **~48 px** of warning at the player's current speed (tune in playtests). Enemies appear 16 px beyond the right edge (03 §7.1), so an enemy walking left at a Goomba's speed is visible for a long time, while a Bullet Bill fired near the edge gives far less warning.
- **No blind jumps:** if the landing isn't visible, coins or scenery must point to it.
- Place enemy spawn points knowing the spawn windows (01 §14), so enemies appear ahead of the player, not on top of them.
- P-meter runways: flight secrets need a runway long enough to charge the P-meter from a standstill (length computed from 01 §15.1). The runway comes *before* the thing it rewards.

### 9.3 The jump "ruler"
Physics defines what's possible. The ruler (generated from the physics constants and exported to Tiled as a stamp overlay) lists, for each speed tier (standing, walk, run, P-speed) and form: max jump height, max gap from a running start, max gap with the jump-up at the end, and flutter/flight extensions. Levels assume **walk** for mandatory jumps in W1–2, **run** from W3 onward, and use **P-speed only for optional routes**, as SMB3 does. Tables are in 01 §15.1.

### 9.4 Secrets (density targets)
- Every level: at least 1 secret (hidden block, 1-UP, coin cache, P-switch path).
- One level in three: a significant secret (Warp Whistle, a white-Toad-house condition, the white-block drop, a secret exit).
- **NEW, used sparingly:** a few secret exits (hidden doors) that open alternate map paths. There are at most 1–2 per world, so the SMB3 structure stays linear-with-branches.

### 9.5 Stage types and templates

| Type | Scroll | Notes |
|---|---|---|
| Overworld / athletic | Horizontal, free left/right | The workhorse |
| Underground | Horizontal | Ceiling at the top of the screen |
| Underwater | Horizontal (+ vertical in some) | Swimming physics |
| Vertical | Vertical | Climbing, cloud/sky, tower |
| Autoscroll | Path-driven | Sky, airship, some special stages; crush rules |
| Fortress | Horizontal with doors | Boom Boom room at the end |
| Airship | Autoscroll → cabin | Pipe into the Koopaling cabin |
| Pipe maze (W7) | Horizontal areas linked by pipes | |
| Bonus room | Single screen | Coin heaven, P-switch coins |
| Goal area | "Backstage" dark area with the roulette box | Every regular level |

**Time limits (SMB3 parity):** each area header picks **200, 300, 400 or unlimited**. SMB3 uses 300 for most levels, 200 for short levels and Hammer Bro battle stages, and 400 for a few long ones (6-5, the W8 fortress, Bowser's castle). One timer unit is **41 ticks (≈0.68 s)**: the tick counter reloads to 40 and fires on underflow (confirm by parity trace). So 300 units ≈ 205 s of real time. The timer pauses during pipe travel, and the hurry-up triggers at exactly 100.

**Area header fields (SMB3 parity, used by `levelc`):** width, horizontal/vertical flag, start position (preset Y and X starts), **vertical-scroll mode (lock low / free / lock at start)**, pipe-exit flag, starting action (none / sliding / exiting a pipe / airship intro), music track, time limit, tileset and palette.

---

## 10. Rules: scoring, lives, cards, timer — SMB3 parity

Confidence: **V** = verified in the disassembly, **S** = secondary source, **?** = unverified (check during M2).

| Event | Points / effect |
|---|---|
| Stomp chain without landing | 100 → 200 → 400 → 800 → 1000 → 2000 → 4000 → 8000 → 1-UP, then 1-UP for each further enemy. The chain resets on landing. **V** |
| Kicked shell kills | Each shell keeps its own chain (reset when kicked), with the same sequence **V** |
| Fireball / tail kill | 100 **S** |
| Hammer, Boomerang, Fire, Sledge Bro | 1000 **S** |
| Star kills | ? |
| Coin | 50 **S** |
| Power-up collected | 1000 ? |
| Brick broken | 10 ? |
| Time bonus | 50 per timer unit left **S** (end-of-level treasure chests also trigger the tally **V**) |
| 100 coins | 1-UP, then the counter resets **V** |
| **Goal cards** | The roulette box gives a Mushroom / Flower / Star card. Three of a kind: **3 Mushrooms = 2-UP, 3 Flowers = 3-UP, 3 Stars = 5-UP**; a mixed set = 1-UP **S** |
| Score display | 6 digits plus a fixed trailing 0 (the score is stored ÷10) **V** |
| Lives | Start with **4** (5 tries) per player; maximum 99 **V** |

**Game over (SMB3 parity, S):** choosing *Continue* returns you to the START of the current world with 4 lives and a score of 0, and **the cards are lost**. **Cleared levels, Toad houses and spade panels reset**, but **fortresses, map-enemy battle courses and W8 tank/ship stages stay cleared, opened locks stay open, and the inventory is kept.** SMB4 keeps these rules in both feel profiles, and the assist options (§12) are the escape valve.

---

## 11. World map systems — SMB3 parity

- **Movement (V):** one d-pad press moves the player one path segment (2 map tiles) to the next stop. From an **uncleared** panel you may only leave the way you came (unless Jugem's Cloud is active). Cleared panels (M/L) are free to cross and can't be replayed; SMB4's Time Attack mode replays them outside the map.
- **Nodes:** stages, Toad houses (single use), spade panels (single use), fortresses, pipes (map shortcuts), drawbridges (W3 toggles them), canoe docks, rocks (Hammer), locks, castles, and W8's hand traps (50 % chance of pulling you into a stage).
- **Fortress effects (V):** clearing a fortress runs that world's scripted effect (remove a lock, add a bridge, …). SMB4 authors these as map events.
- **Wandering map enemies (V):** they move **after the player clears a stage or loses a life** (not after every step), 2 tiles per step. The direction comes from the RNG (right/left/down/up); if blocked they try the other directions and only reverse as a last resort. They never stop on cleared panels, pipes, spade panels, spirals, fortress rubble, START, or dancing plants, and Hammer Bros also avoid Toad houses and the castle base. Touching one starts a battle course; winning opens a chest with the item it carries. The **Music Box** puts them to sleep until you clear a stage or lose a life.
- **Airship (V):** failing it makes it **fly away**. Each world defines 3 alternative routes of 6 waypoints (one picked semi-randomly when the map loads); each failure advances it one waypoint. The **Anchor** freezes it.
- **Toad house (S):** pick 1 of 3 chests; single use. SMB3 picks the item at random when the chest opens; SMB4 uses authored pools per house.
- **Spade panel (V):** a three-reel picture match (top, middle and bottom of a Mushroom / Flower / Star picture); a match gives 2 / 3 / 5 lives. Single use.
- **N-Spade (V/S):** appears every **80,000 points** (never in W8). An 18-card memory game (pairs: Mushroom ×2, Flower ×2, Star ×2, 1-UP, 10 coins, 20 coins) with **2 misses allowed**; matched cards persist between visits. SMB4 fixes SMB3's shuffle bug, which only allowed 8 layouts, and uses a real shuffle.
- **White Toad houses (S):** in SMB3, collecting a set number of coins in one designated stage per world (counted per level) makes a white Toad house appear with a P-Wing or an Anchor. SMB4 keeps the idea with new stages and counts, and (Modern profile only) the Lantern Map item reveals the conditions.
- **Coin ship (S):** in SMB3 this was a hidden condition (finish a stage with a coin count that's a nonzero multiple of 11 while the score's tens digit matches, with a map Hammer Bro present). SMB4 keeps a hidden coin ship per world with a new, *discoverable* condition hinted by a Toad.
- **Warp Whistles (S):** SMB3 hid 3 (1-3 via the white-block trick, the 1-fortress ceiling, the W2 Fire Bro). Using one goes to the Warp Zone. SMB4 hides 3 new ones (§8).
- **Map events:** fortress falls → locks open or bridges appear; world-specific events (tide, sandstorm, ice floes); the king's room after the airship.

---

## 12. Difficulty, assists, and profiles

| Setting | Default | Effect |
|---|---|---|
| **Feel profile** | **Modern** | Modern = SMB3 physics + subtle leniency (coyote time, jump buffer, stomp leniency). Classic = exact SMB3 (01 §9). |
| Lives | Standard | Standard / Infinite (assist) |
| Assist: Safety Shroom | Off | Start every level as Super |
| Assist: Skip | Off | After 5 failures, offer a free Jugem's Cloud |
| Game speed | 100 % | 100 / 90 / 75 % (assist; the physics still runs per tick, the tick rate is lowered) |
| Autorun | Off | Accessibility: toggle-run instead of hold B (off by default because it changes the feel) |

---

## 13. Out of scope for v1 (tracked for later)
Widescreen gameplay (it changes spawn windows and difficulty, so it needs its own design pass), a level editor for players, online play (the deterministic core makes rollback netcode possible later), localization beyond English, extra characters (Toad/Princess).
