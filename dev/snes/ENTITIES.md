# SNES entities — plug-in framework (engine agent)

Framework: `snes/src/eng_obj.s` (slots, registry dispatch, spawner, collisions, despawn, draw order, helper library).
Header for entity authors: **`snes/src/ent.inc`** (fields, flags, classes, damage kinds, helper list, macros).
Reference examples (engine-owned, copy their style): `ent_goomba.s` (walker, stomp), `ent_koopa.s` (spawn-char variants,
turning into another type), `ent_shell.s` (carry/kick, D_KICK), `ent_piranha.s` (behind-BG, shooting a projectile,
generic `VENUS_FIRE`), `ent_items.s` (items, rising from blocks, goal box), `ent_fx.s` (effects), `ent_plproj.s`
(player fireball/hammer = `EC_PROJ` touch protocol), `ent_lifts.s` (platforms). The Hammer Bro battle (`hb`, kind
battle) drops `TREASURE_CHEST` once no enemy is left (engine: `ent_battle_check`).

## 1. The plug-in contract (recap of DESIGN.md)
* One file per type group `snes/src/ent_<name>.s`; each type has a marker line `;@entity NAME codes=xyz` (codes = level
  spawn chars, optional) and exports `NAME_vt`. Use the macro: `ENT_VTABLE NAME, init, update, draw, hit, bump, touch`
  (it exports `NAME_vt: .faraddr ...`). A `0` entry = engine default (see §4). Several types per file are fine.
* `snes\build.ps1` generates `gen/ent_ids.inc` (`ET_NAME`, sorted by name, **ids change when types are added** — never
  store raw numbers) and `gen/ent_table.s` (`ent_vtables`, `ent_spawn_map`). `ent.inc` includes `ent_ids.inc`.
* Put your code in your bank segment (CODE8 enemies-A, CODE9 enemies-B, CODE10 bosses; engine uses CODE13).

## 2. Calling conventions
* **Callbacks** are `JSL`ed with **A 8-bit, X/Y 16-bit, X = slot*2** (byte offset into the 16-bit field arrays),
  D = 0, DB = $80. Start every callback with `ENT_ENTER` (= `rep #$30`), end with `rtl`. You may return in any
  register width; the engine restores AXY16 and reloads X itself. Return values are in the carry flag.
* **Helpers** (`ent_*`, table in §5) are `JSL`, called **and** returned in **AXY16**, keep **X** (the slot) and DB,
  clobber Y, `e_t0`-`e_t7`, `e_p0`-`e_p2`, `e_tx`, `e_ty` unless noted. Results in A (and/or carry, or Y).
* Your private temporaries: zero-page `es0`-`es3` (16-bit) — no helper touches them. **But** `ent_spawn` runs the new
  entity's init, and `ent_hit` runs another entity's hit callback: if those use `es*` too, save yours on the stack.
  For anything that must survive a tick use your slot fields (`ent_v0`-`ent_v7`, `ent_t`, `ent_state`, `ent_anim`).
* **DB = $80:** all slot fields, player/world state (`p_*`, `w_*`, `cam_*`, `area_*`, `g_*`) live in low RAM ($0000-$1FFF)
  and are reachable with plain absolute addressing. **Tables in your own ROM bank need long addressing**
  (`lda f:mytable,x`; there is no long,Y mode — index with X, e.g. `txy` / `ldx #n` / `tyx`). HIBSS/EXBSS RAM
  (`$7E2000+`, `$7F0000+`) also needs `f:` long addressing (e.g. `lda f:lvl_cont,x`). If you ever change DB, restore $80.
* Never `jsr` into another bank; only `jsl` the helpers / engine routines.

## 3. Slot fields (all 16-bit, `lda ent_x,x`)
| field | meaning (C# name) |
|---|---|
| `ent_type` | `ET_*` (0 = free) |
| `ent_x`, `ent_y` | position of the sprite box top-left in **1/16 px** (C# X/Y). Px = X>>4 (unsigned, `ent_px`), Py = Y>>4 (signed, `ent_py`) |
| `ent_xvel`, `ent_yvel` | 1/16 px per tick, signed |
| `ent_wd`, `ent_ht` | sprite box in px (default 16x16) |
| `ent_hbx`, `ent_hby`, `ent_hbw`, `ent_hbh` | hitbox offset/size in px (default 2, 3, 12, 13) |
| `ent_fl`, `ent_fl2` | flags `F_*`, `F2_*` (below) |
| `ent_class` | `EC_*` (below), default `EC_ENEMY` |
| `ent_facing` | -1 / +1 (default -1) |
| `ent_t`, `ent_state`, `ent_anim` | C# Timer/State/Anim — yours (cleared at spawn) |
| `ent_v0`-`ent_v7` | per-type scratch — yours (cleared at spawn) |
| `ent_arg` | init argument: the **level spawn char** (`'g'`...) for level objects, or the Y passed to `ent_spawn` |
| `ent_points` | knock-off score, **BCD** (`$0100` = 100, default) |
| `ent_chain` | ShellChain (engine uses it for `F_SHELL` objects) |
| `ent_spawnidx` | spawn-list index or `$FFFF` (engine-owned; see "turning into another type") |
| `ent_knock` | knocked-off fall active (engine-owned) |

Engine variables you may read: `ent_dmg`, `ent_dir` (hit/bump arguments), `ent_other` (projectile touch),
`ent_cur` (slot being dispatched), `ent_n`/`ent_order` (list: `ent_order[i]` = slot*2, i < ent_n), `w_frame` (tick
counter), `cam_x`/`cam_y` (px), `area_w`/`area_h` (tiles), `area_water` (water row or $FFFF), `w_pswitch`,
player `p_x`/`p_y` (1/16 px; player box is 16x32 at Px, Py), `p_xvel`, `p_yvel`, `p_facing`, `p_form` (`PF_*`),
`p_state` (`PS_*`), `p_inair`, `p_star`, `p_statue`, `p_carrying` (= slot*2+2 of the carried object, 0 = none).
Max **24** slots (`MAX_ENTS`); `ent_spawn` fails (carry clear) when full.

### Flags
`F_REMOVE` removed at end of tick · `F_KILLED` spawn point stays dead · `F_DYING` knocked off (no collisions) ·
`F_BEHIND` drawn first with OBJ priority 2 (hidden by pipes) · `F_HURTS` default touch hurts · `F_STOMP` Stompable ·
`F_GROUND` OnGround (set by `ent_move_walker`) · `F_SLOT` counts for the 5-enemy rule · `F_SHELL` kills enemies it
touches (chain score; two moving `F_SHELL`s kill each other) · immunities `F_FIREIMM F_STARIMM F_SHELLIMM F_TAILIMM
F_HAMMERIMM` (used by `ent_default_hit`) · `F_CARRY` carryable (informational) · `F_PON` platform: player on it.
`ent_fl2`: `F2_BOSS` (slope-sliding players don't knock it). Defaults at spawn: `F_HURTS|F_STOMP|F_SLOT`.

### Classes
`EC_ENEMY` (player collisions with star/statue/slide rules, shells & projectiles hit it) · `EC_ITEM` · `EC_PROJ` (player
projectile: see §4 touch) · `EC_EPROJ` (enemy projectile; despawns just off-screen) · `EC_EFFECT` (no collisions, drawn
last, keeps updating during the course-clear walk) · `EC_SPECIAL` (never despawned, drawn after enemies) ·
`EC_PLATFORM` (drawn before enemies; see `ent_plat_*`).

### Damage kinds (`ent_dmg`, C# `Dmg` order)
`D_STOMP 0, D_FIRE 1, D_TAIL 2, D_SHELL 3, D_STAR 4, D_HAMMER 5, D_BUMP 6, D_STATUE 7, D_LAVA 8, D_BOOMERANG 9`, plus the
engine request `D_KICK 16`: "the player let go of this carried object" (handle it and return carry set, e.g. the shell
kicks itself; otherwise the engine sets XVel = facing*$30, YVel = -$10). `ent_dir` = direction -1/0/+1 (0 = away from
facing).

## 4. The six callbacks
| # | callback | when | in | return |
|---|---|---|---|---|
| 0 | **init** | right after allocation (level spawn or `ent_spawn`) | defaults set, `ent_x/ent_y` = spawn px<<4 (level: tile*16), `ent_arg` | **carry set = keep**, clear = cancel (slot freed) |
| 3 | **update** | once per tick, list order, unless `F_REMOVE` | | – |
| 6 | **draw** | each frame, in C# painter order | `spr_arg_x/y` = Px-CamX, Py-CamY; `spr_arg_flags` = `SPR_BEHIND` if `F_BEHIND` else 0 | – |
| 9 | **hit** | C# TakeHit: fire/tail/shell/star/statue/hammer/bump/kick | `ent_dmg`, `ent_dir` | carry = affected (the caller then marks it killed + plays the kick sound) |
| 12 | **bump** | C# OnBumpBelow: block hit under it | `ent_dir` | – |
| 15 | **touch** | C# OnPlayerTouch: hitboxes overlap (not while star/statue/slide handled it) | | – (EC_PROJ: see below) |

Defaults for a `0` entry: init = keep; update/draw = nothing; **hit** = `ent_default_hit` (immunity flags, then
`ent_knock_off(ent_dir)`, carry set); **bump** = hit with `D_BUMP`; **touch** = `ent_hurt_player` if `F_HURTS`.
Fillers: `ent_cb_none` (= `clc; rtl`: "not affected / do nothing") and `ent_cb_hurt` (always hurts).
**EC_PROJ touch protocol:** for player projectiles the engine calls *touch* with `ent_other` = the enemy slot it
overlaps; do the damage with `ent_hit` (X = `ent_other`), return carry set to stop checking further enemies this tick.

Order of a tick (C# World.Tick): player → **Spawner** → **update** all → flush adds → **Collisions** (player vs
objects, projectiles vs enemies, moving shells/carried vs enemies) → **Despawn** → removal. New objects spawned during
the tick start updating next tick (C# `Add` queue).

## 5. Helpers (JSL, AXY16, keep X)
| helper | in → out |
|---|---|
| `ent_spawn` | A = `ET_*`, Y = arg, `ent_new_x`/`ent_new_y` = new Px/Py → carry set + **Y = new slot*2** (its init already ran) |
| `ent_remove` / `ent_kill` | Remove = true / MarkKilled (spawn point stays dead) |
| `ent_knock_off` | A = dir: flip + fall off-screen, adds `ent_points` (C# KnockOff) |
| `ent_update_knocked` | → carry set if in the knocked fall (then just `rtl`) — call first in update |
| `ent_default_hit` / `ent_hit` | C# Entity.TakeHit on X / TakeHit on X through its vtable (`ent_dmg`,`ent_dir`) → carry |
| `ent_apply_vel` | X += XVel, Y += YVel |
| `ent_gravity` | C# Gravity (+3 max $40; in water +1 max $10) |
| `ent_move_walker` | A = turnAtLedges, Y = turnAtWalls → carry = hit a wall. Full C# MoveWalker: walls, floors, semisolids, **slopes**, conveyors, ledge turns, head bumps, fell-out-of-level (Remove+Killed) |
| `ent_fell_out` | Py > level bottom + 32 → Remove + Killed, carry |
| `ent_px`, `ent_py`, `ent_cx`, `ent_bottom` | A = Px / Py / Px+Wd/2 / Py+Ht |
| `ent_player_dx` | A = P.CenterX - Cx (signed) |
| `ent_face_player` | A = FaceToward(P) (-1/+1) |
| `ent_face_player_set` | constructor rule `Facing = P.CenterX < Px+8 ? -1 : 1` → stored, A = facing |
| `ent_tile_at`, `ent_props_at` | A = px, Y = py → A = tile `T_*` / props `TP_*` (`TP_SOLID TP_FLOOR TP_SLOPE TP_HURTS TP_BUMP TP_LAVA TP_PIPE`) |
| `ent_solid_at`, `ent_floor_at` | A = px, Y = py → carry (C# SolidAt / FloorAt; floor includes semisolids) |
| `ent_in_water` | Y = py → carry |
| `ent_player_overlap` | → carry if the player hitbox overlaps ours |
| `ent_overlaps_rect` / `ent_overlaps_ent` | rect `tt_x tt_y tt_w tt_h` (px) / Y = other slot → carry |
| `ent_can_stomp` | C# World.CanStomp → carry |
| `ent_stomp_bounce` | C# StompBounce: bounce, KillTally++, chain score, sound |
| `ent_hurt_player` | p.Hurt() |
| `ent_score` | A = BCD points → score + popup at Px,Py |
| `ent_puff` / `ent_puff_at` | puff at Px,Py / at `ent_new_x/y` |
| `ent_sparkle_at`, `ent_popup` | sparkle / floating text (A = BCD pts or $FFFF "1UP") at `ent_new_x/y` |
| `ent_sfx` | A = `SFX_*` (or macro `ENT_SFX "NAME"`, skipped if the id doesn't exist) |
| `ent_draw_meta` | A = `SPR_*` id → `spr_meta` at spr_arg_x/y/flags (or macro `ENT_DRAW SPR_ID`, skipped if undefined) |
| `ent_draw_setxy`, `ent_draw_face`, `ent_draw_knocked`, `ent_draw_offset` | reset position from the entity / flags |= hflip if facing<0 / vflip+face / spr_arg_x += A, spr_arg_y += Y |
| `ent_sin` | A = binary angle (256 = full turn), Y = amplitude (-128..127) → A = sin*amp (cos = angle+64). Period P ticks: angle = t*256/P |
| `ent_div` / `ent_mod` | A (unsigned 16) / Y (1-255) → A = quotient, Y = remainder / A = remainder |
| `ent_isqrt` | `e_t1:e_t0` (32-bit) → A = floor(sqrt) |
| `ent_random` | A = pseudo-random 16-bit (the C# game has no randomness; avoid if parity matters) |
| `ent_count_class`, `ent_count_type`, `ent_find_type` | A = class/type → A = live count / carry + Y = first slot |
| `ent_carried` | carry if the player carries this one |
| `ent_offscreen` | carry if outside the despawn window, A = Px-CamX |
| `ent_plat_begin`, `ent_plat_end` | platform update bracket (see ent_lifts.s): carries the player by the platform's motion, landing, `F_PON`; end → carry = just landed (C# OnStood) |
| engine: `w_add_score_at` (A pts, X px, Y py; clobbers X), `w_one_up_at` (X px, Y py), `pl_bounce` (A = yvel), `pl_powerup` (A = PF_*), `pl_die` (A = 1 pit) | |
Macros: `ENT_ENTER`, `ENT_VTABLE`, `ENT_SFX`, `ENT_DRAW`, `ENT_SIGN` (A = sign(A)), `ENT_ASL4`, `ASR4`, `NEG16`.
Trig tables for exact C# motions: `eng_tables` (+0: sin(t*2π/180)*640, +360: sin(t*2π/240)*768, +840: cos>0?1:-1 (240),
+1320: sin(t*2π/240)*896, +1800: sin(i*2π/256)*32767) — words, index = t*2 (`lda f:eng_tables+360,x`).

## 6. The SMB3 5-enemy rule
Only **level spawns** are limited (C# TrySpawn): after init, if the new object has `F_SLOT` and class `EC_ENEMY` and
there are already **5** live `EC_ENEMY` objects (`ent_count_class`), the spawn silently fails (slot freed, the spawn
point is retried when its column scrolls in again). Objects created with `ent_spawn` (shells, projectiles, eggs)
are never refused by this rule. Clear `F_SLOT` in init for things that shouldn't count (C# `UsesSlot = false`: items,
effects, bosses, projectiles). Therefore **an init of a level-spawnable enemy must not spawn other entities** (do it on
the first update) — a refused spawn can't undo them.

## 7. Adding a new entity type (step by step)
1. Pick names: file `snes/src/ent_<name>.s`, type `NAME` (UPPER_SNAKE, unique project-wide), spawn chars from
   `data/levels/README.md` (a char may be claimed by one type only — the build fails otherwise).
2. Copy the template below; set the segment to your bank.
3. Port the C# class: constructor → init (read `ent_arg`, set size/hitbox/flags/class), `Update` → update,
   `Draw` → draw (ids from `gen/spr_ids.inc` / SPRITES.md), `TakeHit` → hit, `OnBumpBelow` → bump, `OnPlayerTouch` → touch.
4. Build with your private `-Out`; `gen/ent_ids.inc` shows your `ET_NAME`. Test in a level containing the spawn char
   (level select) and look at screenshots.

```asm
; My enemy (port of C# Foo). Owner: <you>.
;@entity FOO codes=Q
.p816
.smart
.macpack longbranch            ; only if you use jeq/jcc/...
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE FOO, foo_init, foo_update, foo_draw, 0, 0, foo_touch
.a16
.i16
foo_init:
    ENT_ENTER
    lda #4
    sta ent_hby,x               ; hitbox 2,4,12,12
    lda #12
    sta ent_hbh,x
    jsl ent_face_player_set     ; A = facing
    asl a
    asl a
    asl a
    sta ent_xvel,x              ; facing * 8
    sec                         ; keep
    rtl
foo_update:
    ENT_ENTER
    jsl ent_update_knocked      ; knocked off by fire/shell/tail? just fall
    bcc :+
    rtl
:   inc ent_anim,x
    lda #0                      ; turnAtLedges
    ldy #1                      ; turnAtWalls
    jsl ent_move_walker
    rtl
foo_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    jsl ent_kill
    jsl ent_remove              ; (or a squashed state)
    rtl
@hurt:
    jsl ent_hurt_player
    rtl
foo_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
:   ENT_DRAW SPR_FOO_1
    rtl
```

## 8. Recipes
* **Turning into another type** (Koopa → Shell): `ent_spawn` the new one, then hand the spawn point over:
  `lda ent_spawnidx,x / sta ent_spawnidx,y / lda #$FFFF / sta ent_spawnidx,x`, then `jsl ent_remove`.
  Shells: `ent_spawn(ET_SHELL, arg = kind | flipped<<8)`, kind 0 green, 1 red, **2 buzzy** (fire-immune; when it
  wakes up it spawns `ET_BUZZY` with arg 0 at the shell's Px/Py if that type exists).
* **Projectiles**: spawn with `ent_spawn`, then set `ent_xvel,y`/`ent_yvel,y`. Generic enemy fireball: `ET_VENUS_FIRE`
  (8x8, straight line, 400 ticks, `EC_EPROJ`, hurts, star-immune).
* **Carrying** (Bob-omb): in touch, `txa / inc a / inc a / sta p_carrying` (only if `p_carrying`, `p_statue`,
  `p_swimming` are 0 and `pad_held & BTN_B`). While carried the engine skips your collisions/despawn and draws you
  after the player; position yourself in update (see `ent_shell.s`). Release: hit callback with `D_KICK`.
* **Platforms**: class `EC_PLATFORM`, update = `jsl ent_plat_begin` / move / `jsl ent_plat_end` (see `ent_lifts.s`).
* **Behind pipes**: set `F_BEHIND` (and draw with `spr_arg_flags` bit 2 = `SPR_BEHIND`).
* **Bosses**: class `EC_ENEMY`, clear `F_SLOT`, set `F2_BOSS` in `ent_fl2`, `ent_points` = 0; end the level with
  `w_start_clear` (A = $FFFF: no card) or the level-result API (DESIGN.md "Session & saves").

## 9. Gotchas
* Callbacks enter **A8** — `ENT_ENTER` first, or your `lda #imm16` assembles/executes wrong.
* Keep `.a16/.i16` in sync with the real CPU state (`.smart` tracks `rep/sep`, but not across `jsl` returns: helpers
  return AXY16).
* `ent_spawn` returns the new slot in **Y**, not X; X stays yours.
* Positions are 1/16 px; sizes/hitboxes are px. `ent_new_x/y` are **px**.
* Never write `ent_type`/`ent_order`/`ent_n`; to delete use `ent_remove` (takes effect at end of tick).
* Don't call `snd_music` (level-song uploads stall ~0.5 s); SFX only.
* Sprite ids that don't exist in this build silently draw nothing (`ENT_DRAW` checks `.ifdef`).
