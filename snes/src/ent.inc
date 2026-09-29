; Entity plug-in header (engine agent). Include from every snes/src/ent_*.s file:
;     .include "snes.inc"
;     .include "ent.inc"          ; pulls in eng.inc (player/world state), gen/ent_ids.inc (ET_*), spr_ids.inc, music.inc
; Full documentation: snes/ENTITIES.md. Summary:
;  * one slot = 16-bit fields in parallel arrays, index X = slot*2 (byte offset), e.g. `lda ent_x,x`.
;  * callbacks (JSL/RTL, from the NAME_vt table) are entered A8/XY16, X = slot*2, D = 0, DB = $80; they may return
;    with any register width. Use `ENT_ENTER` (rep #$30) as the first line.
;  * helpers `ent_*` are JSL/RTL, called and returned in AXY16, keep X (the slot) and DB, clobber Y, e_t0-e_t7,
;    e_p0-e_p2, e_tx, e_ty unless noted. `es0`-`es3` (zero page) are yours: helpers never touch them.
.include "eng.inc"
.include "ent_ids.inc"
.include "spr_ids.inc"
.include "music.inc"

; ------------------------------------------------------------------ slot fields (all 16-bit, index X = slot*2)
.global ent_type        ; ET_* id (0 = free slot)
.global ent_x, ent_y    ; position of the sprite box top-left in 1/16 px (C# X/Y); Px = X>>4 (unsigned), Py = Y>>4 (signed)
.global ent_xvel, ent_yvel ; 1/16 px per tick (signed)
.global ent_wd, ent_ht  ; sprite box size in px (default 16x16)
.global ent_hbx, ent_hby, ent_hbw, ent_hbh ; hitbox offset/size in px (default 2,3,12,13)
.global ent_fl          ; F_* flags
.global ent_fl2         ; F2_* flags (more flags)
.global ent_class       ; EC_* class
.global ent_facing      ; -1 / +1 (default -1)
.global ent_t, ent_state, ent_anim ; C# Timer / State / Anim (free for the type; engine only clears them)
.global ent_spawnidx    ; spawn-list index or $FFFF (engine-owned: keep it unless you hand it over, see ENTITIES.md)
.global ent_knock       ; KnockTimer (engine: knocked-off fall active)
.global ent_points      ; score for a knock-off, BCD ($0100 = 100)
.global ent_chain       ; ShellChain (moving shells)
.global ent_arg         ; init argument: level spawn char ('g'...) or the Y value passed to ent_spawn
.global ent_v0, ent_v1, ent_v2, ent_v3, ent_v4, ent_v5, ent_v6, ent_v7 ; per-type scratch (cleared at spawn)

; engine variables readable by entities
.global ent_dmg, ent_dir     ; hit/bump callback arguments: damage kind D_*, direction -1/0/+1
.global ent_other            ; projectile touch callback: slot*2 of the enemy it overlaps
.global ent_new_x, ent_new_y ; ent_spawn position in px (the new entity's Px/Py)
.global ent_cur              ; slot*2 being updated/dispatched
.global ent_n, ent_order     ; list (read-only): ent_order[i] = slot*2, i < ent_n
.globalzp es0, es1, es2, es3 ; entity scratch (zero page, 16-bit), never used by the engine
.global tt_x, tt_y, tt_w, tt_h ; rectangle for ent_overlaps_rect

MAX_ENT_SLOTS = MAX_ENTS

; ------------------------------------------------------------------ classes (C# EntClass)
EC_ENEMY = 0
EC_ITEM = 1
EC_PROJ = 2          ; player projectile: its touch callback is called with ent_other = enemy it overlaps
EC_EPROJ = 3         ; enemy projectile
EC_EFFECT = 4        ; drawn last, keeps updating during the course-clear
EC_SPECIAL = 5       ; never despawned
EC_PLATFORM = 6      ; set F_PON while the player stands on it (see ent_plat_begin/ent_plat_end)

; ------------------------------------------------------------------ flags (ent_fl)
F_REMOVE = $0001     ; removed at the end of the tick
F_KILLED = $0002     ; counts as killed: the spawn point stays dead for the level
F_DYING = $0004      ; knocked off / dying: no collisions
F_BEHIND = $0008     ; drawn behind high-priority BG1 tiles (pipes) and before everything else
F_HURTS = $0010      ; default touch hurts the player
F_STOMP = $0020      ; Stompable (ent_can_stomp)
F_GROUND = $0040     ; OnGround (set by ent_move_walker)
F_FIREIMM = $0080
F_SLOT = $0100       ; UsesSlot: counts toward the 5-enemy rule (class EC_ENEMY only)
F_SHELL = $0200      ; IsShell: kills enemies it overlaps (shell chain score), two moving shells kill each other
F_STARIMM = $0400
F_SHELLIMM = $0800
F_TAILIMM = $1000
F_HAMMERIMM = $2000
F_CARRY = $4000      ; Carryable (informational; pick-up is done by the type's touch: `sta p_carrying` = slot*2+2)
F_PON = $8000        ; platform: the player stands on it (C# Platform.Carrying)

; ent_fl2
F2_BOSS = $0001      ; C# "is Boss": slope-sliding players do not knock it away

; ------------------------------------------------------------------ damage kinds (C# Dmg) for the hit callback
D_STOMP = 0
D_FIRE = 1
D_TAIL = 2
D_SHELL = 3
D_STAR = 4
D_HAMMER = 5
D_BUMP = 6
D_STATUE = 7
D_LAVA = 8
D_BOOMERANG = 9
D_KICK = 16          ; engine request: the player lets go of this carried object (return carry = handled; else the
                     ; engine applies XVel = facing*$30, YVel = -$10)

; vtable offsets
VT_INIT = 0
VT_UPDATE = 3
VT_DRAW = 6
VT_HIT = 9
VT_BUMP = 12
VT_TOUCH = 15

; ------------------------------------------------------------------ helpers (JSL, AXY16; see ENTITIES.md)
.global ent_spawn, ent_remove, ent_kill, ent_knock_off, ent_default_hit, ent_update_knocked, ent_hit
.global ent_apply_vel, ent_gravity, ent_move_walker, ent_fell_out
.global ent_px, ent_py, ent_cx, ent_bottom, ent_face_player, ent_face_player_set, ent_player_dx
.global ent_tile_at, ent_props_at, ent_solid_at, ent_floor_at, ent_in_water
.global ent_player_overlap, ent_overlaps_rect, ent_overlaps_ent, ent_can_stomp, ent_stomp_bounce, ent_hurt_player
.global ent_score, ent_score_at, ent_puff, ent_puff_at, ent_sparkle_at, ent_popup
.global ent_draw_meta, ent_draw_setxy, ent_draw_face, ent_draw_knocked, ent_draw_offset
.global ent_sin, ent_mod, ent_div, ent_isqrt, ent_random, ent_count_class, ent_count_type, ent_find_type
.global ent_plat_begin, ent_plat_end, ent_carried, ent_offscreen
.global ent_sfx, ent_cb_none, ent_cb_hurt
.global eng_tables
.global spr_arg_x, spr_arg_y, spr_arg_flags, spr_arg_id, spr_meta   ; sprites module (draw position/flags, see SPRITES.md)
.global w_add_score_at, w_add_score, w_one_up_at, w_chain_score, pl_hurt, pl_bounce, pl_powerup, pl_get_star, pl_die

; first line of every callback
.macro ENT_ENTER
    rep #$30
    .a16
    .i16
.endmacro

; ENT_SFX "NAME": play SFX_NAME if the sound module defines it (A16/XY16, keeps X and Y)
.macro ENT_SFX name
.ifdef .ident(.concat("SFX_", name))
    lda #.ident(.concat("SFX_", name))
    jsl ent_sfx
.endif
.endmacro

; ENT_DRAW SPR_ID: draw that metasprite at spr_arg_x/y with spr_arg_flags if the sprites module defines it (keeps X)
.macro ENT_DRAW id
.ifdef id
    lda #id
    jsl ent_draw_meta
.endif
.endmacro

; declare a type's vtable: ENT_VTABLE NAME, init, update, draw, hit, bump, touch  (0 = engine default)
.macro ENT_VTABLE name, i, u, d, h, b, t
    .export .ident(.concat(.string(name), "_vt"))
.ident(.concat(.string(name), "_vt")):
    .faraddr i, u, d, h, b, t
.endmacro

; ENT_SIGN: A = sign(A) = -1 / 0 / +1 (A16 only). Label-free, so @cheap labels stay valid across it.
.macro ENT_SIGN
    cmp #0
    beq *+12
    bmi *+7
    lda #1
    bra *+5
    lda #$FFFF
.endmacro

; ENT_ASL4 / ENT_ASR4: A <<= 4 / A >>= 4 (arithmetic): px <-> 1/16 px
.macro ENT_ASL4
    asl a
    asl a
    asl a
    asl a
.endmacro
.define ENT_ASR4 ASR4
