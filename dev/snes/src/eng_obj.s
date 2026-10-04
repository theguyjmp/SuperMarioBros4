; Engine: entity framework (engine agent). Slot storage, the plug-in registry dispatch (gen/ent_table.s:
; ent_vtables / ent_spawn_map from `;@entity` markers in snes/src/ent_*.s), World object loop (Spawner, updates,
; adds, Collisions, Despawn), draw passes, the JSL helper library for entity code, and the engine-side entry points
; (block releases, bumps, effects, carried objects, tail/statue hits, platforms).
; Port of src/Game/Entities/Entity.cs + the object parts of World.cs / WorldPlay.cs. Documentation: snes/ENTITIES.md.
; Internal convention: AXY16, D = 0, DB = $80. Callbacks are invoked A8/XY16 (DESIGN.md contract), X = slot*2.
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.import ent_vtables, ent_spawn_map, eng_tlog_reset
.global spr_meta, spr_arg_x, spr_arg_y, spr_arg_flags, spr_arg_id
.global pl_hitbox, w_bump_above_l, w_break_brick, w_hit_block

N = MAX_ENTS

.segment "ZEROPAGE"
es0: .res 2
es1: .res 2
es2: .res 2
es3: .res 2
e_vt: .res 3                ; vtable pointer of the type being dispatched
e_fn: .res 3                ; callback being invoked (JML [e_fn] reads bank 0 = this zero page)
dl_c: .res 10               ; ent_draw: per-pass counts -> ends (bytes)
dl_t: .res 2
dl_i: .res 2
dl_e: .res 2
dl_built: .res 2
dl_list: .res 2*MAX_ENTS     ; drawable entities sorted by pass

.segment "BSS"
ent_type: .res 2*N
ent_x: .res 2*N
ent_y: .res 2*N
ent_xvel: .res 2*N
ent_yvel: .res 2*N
ent_wd: .res 2*N
ent_ht: .res 2*N
ent_hbx: .res 2*N
ent_hby: .res 2*N
ent_hbw: .res 2*N
ent_hbh: .res 2*N
ent_fl: .res 2*N
ent_fl2: .res 2*N
ent_class: .res 2*N
ent_facing: .res 2*N
ent_t: .res 2*N
ent_state: .res 2*N
ent_anim: .res 2*N
ent_spawnidx: .res 2*N
ent_knock: .res 2*N
ent_points: .res 2*N
ent_chain: .res 2*N
ent_arg: .res 2*N
ent_v0: .res 2*N
ent_v1: .res 2*N
ent_v2: .res 2*N
ent_v3: .res 2*N
ent_v4: .res 2*N
ent_v5: .res 2*N
ent_v6: .res 2*N
ent_v7: .res 2*N
ent_order: .res 2*N         ; slot*2 in list order
ent_n: .res 2
ent_addq: .res 2*N
ent_nadd: .res 2
ent_cur: .res 2             ; slot*2 being processed
ent_other: .res 2
ent_dmg: .res 2
ent_dir: .res 2
ent_new_x: .res 2
ent_new_y: .res 2
spn_lastR: .res 2
spn_lastL: .res 2
spn_lastD: .res 2
spn_lastU: .res 2
spn_i: .res 2
spn_col: .res 2
spn_idx: .res 2
o_i: .res 2
o_j: .res 2
o_tmp: .res 2
o_dir: .res 2
o_d: .res 2
o_k: .res 2
bump_final: .res 2
bump_dir: .res 2
pl_prevx: .res 2
pl_prevy: .res 2
pl_wason: .res 2
pl_dx: .res 2
pl_dy: .res 2
pl_feet: .res 2
ea_type: .res 2
db_x: .res 2
db_y: .res 2
sh_n: .res 2
sh_base: .res 2
rng_seed: .res 2
col_i: .res 2
col_j: .res 2
w_battlewon: .res 2
col_plonly: .res 2          ; co-op: collisions for the second player only (no shell pass)
pl_near2: .res 2            ; co-op: the other player is near the platform being updated
pl_wason2: .res 2

.segment "HIBSS"
ent_pon2: .res 2*N          ; co-op: Luigi stands on this platform (F_PON is Mario's)
dl_pass: .res 2*N           ; ent_draw: pass*2 per list position

.import CO_X, CO_Y, CO_CARRY, CO_STATE, CO_INAIR

.segment "EXBSS"
spawned: .res 512           ; per spawn index (byte)
killed: .res 8*512          ; per area, per spawn index (C# areaKilled)

.segment "CODE3"
.a16
.i16

tile_props3: .byte TILE_PROPS

.macro PROPS3
    tax
    lda f:tile_props3,x
    and #$00FF
.endmacro

; ================================================================== registry dispatch
; X = slot*2, Y = VT_* offset -> e_fn = callback, Z set if the type has none (0). Keeps X.
cb_fetch:
    lda ent_type,x
    sta e_fn
    asl a
    clc
    adc e_fn                    ; *3
    phx
    tax
    lda f:ent_vtables,x
    sta e_vt
    lda f:ent_vtables+1,x
    sta e_vt+1
    plx
    lda [e_vt],y
    sta e_fn
    iny
    lda [e_vt],y
    sta e_fn+1
    ora e_fn
    rts

; call e_fn with A8/XY16 (contract), X = slot*2, DB = $80 -> AXY16, carry = the callback's
cb_invoke:
    sep #$20
    jsl cb_jml
    rep #$30
    rts
cb_jml:
    jml [e_fn]

; X = slot*2, Y = VT_* -> call it if present (carry = result, clear if absent)
cb_call:
    jsr cb_fetch
    beq @no
    jmp cb_invoke
@no: clc
    rts

; TakeHit(e = X, kind = o_d, dir = o_dir) -> carry = affected
take_hit:
    lda o_d
    sta ent_dmg
    lda o_dir
    sta ent_dir
    ldy #VT_HIT
    jsr cb_fetch
    beq @def
    jmp cb_invoke
@def: jsl ent_default_hit
    rts

; OnBumpBelow(dir = o_dir): default = TakeHit(Bump, dir)
bump_below:
    lda o_dir
    sta ent_dir
    ldy #VT_BUMP
    jsr cb_fetch
    beq @def
    jmp cb_invoke
@def: lda #D_BUMP
    sta o_d
    jmp take_hit

; OnPlayerTouch: default = Hurts -> p.Hurt()
touch:
    ldy #VT_TOUCH
    jsr cb_fetch
    beq @def
    jmp cb_invoke
@def: lda ent_fl,x
    and #F_HURTS
    beq :+
    jsl pl_hurt
:   rts

; vtable fillers: "nothing / not affected" and "hurts the player"
ent_cb_none:
    clc
    rtl
ent_cb_hurt:
    rep #$30
    jsl pl_hurt
    clc
    rtl

; ================================================================== list management
ent_init:
    ldx #0
@c: stz ent_type,x
    inx
    inx
    cpx #2*N
    bcc @c
    stz ent_n
    stz ent_nadd
    rtl

; A = type -> X = free slot*2 with the type set (carry clear if the table is full). Keeps Y.
alloc_slot:
    sta ea_type
    ldx #0
@f: lda ent_type,x
    beq @got
    inx
    inx
    cpx #2*N
    bcc @f
    clc
    rts
@got: lda #0
    sta f:ent_pon2,x
    lda ea_type
    sta ent_type,x
    ; C# Entity defaults
    lda #EC_ENEMY
    sta ent_class,x
    stz ent_xvel,x
    stz ent_yvel,x
    lda #16
    sta ent_wd,x
    sta ent_ht,x
    lda #2
    sta ent_hbx,x
    lda #3
    sta ent_hby,x
    lda #12
    sta ent_hbw,x
    lda #13
    sta ent_hbh,x
    lda #(F_HURTS|F_STOMP|F_SLOT)
    sta ent_fl,x
    stz ent_fl2,x
    lda #$FFFF
    sta ent_facing,x
    sta ent_spawnidx,x
    stz ent_t,x
    stz ent_state,x
    stz ent_anim,x
    stz ent_knock,x
    lda #$0100
    sta ent_points,x
    stz ent_chain,x
    stz ent_v0,x
    stz ent_v1,x
    stz ent_v2,x
    stz ent_v3,x
    stz ent_v4,x
    stz ent_v5,x
    stz ent_v6,x
    stz ent_v7,x
    sec
    rts

; A = type, Y = arg, ent_new_x/y = px -> X = slot, initialised but not queued (carry clear: none / init refused)
make_ent:
    jsr alloc_slot
    bcc @r
    tya
    sta ent_arg,x
    lda ent_new_x
    asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    lda ent_new_y
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    ldy #VT_INIT
    jsr cb_fetch
    beq @ok
    jsr cb_invoke
    bcs @ok
    stz ent_type,x
    clc
@r: rts
@ok: sec
    rts

; X = slot*2 -> appended to the add queue (C# World.Add)
enqueue:
    phx
    lda ent_nadd
    asl a
    tay
    txa
    sta ent_addq,y
    inc ent_nadd
    plx
    rts

; ent_spawn (helper): A = ET_* type, Y = init argument (ent_arg), ent_new_x/ent_new_y = the new entity's Px/Py
; -> carry set, Y = new slot*2 (init already ran; the entity updates from the next tick). X is kept.
ent_spawn:
    phx
    jsr make_ent
    bcc @fail
    jsr enqueue
    txy
    plx
    sec
    rtl
@fail: plx
    clc
    rtl

; FlushAdds
flush_adds:
    ldy #0
@l: cpy ent_nadd
    beq @d
    phy
    tya
    asl a
    tay
    lda ent_addq,y
    pha
    lda ent_n
    asl a
    tay
    pla
    sta ent_order,y
    inc ent_n
    ply
    iny
    bra @l
@d: stz ent_nadd
    rts

; RemoveAll(e => e.Remove): compact the order list, free slots
remove_all:
    ldy #0                      ; read
    stz o_i                     ; write
@l: cpy ent_n
    beq @d
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #F_REMOVE
    beq @keep
    stz ent_type,x
    ; C# Despawn's second loop: removed + killed objects stay dead
    lda ent_fl,x
    and #F_KILLED
    beq :+
    lda ent_spawnidx,x
    bmi :+
    jsr killed_index
    phx
    tax
    sep #$20
    lda #1
    sta f:killed,x
    rep #$20
    plx
:   ; the carried object never gets removed while carried; if it does, drop the reference
    txa
    inc a
    inc a
    cmp p_carrying
    bne @nx
    stz p_carrying
    bra @nx
@keep:
    lda o_i
    asl a
    tay
    txa
    sta ent_order,y
    inc o_i
@nx: ply
    iny
    bra @l
@d: lda o_i
    sta ent_n
    rts

; ent_count_class: A = class -> A = count (list + adds, not removed). JSL, keeps X.
ent_count_class:
    phx
    sta o_tmp
    stz o_d
    ldy #0
@l: cpy ent_n
    beq @a
    phy
    tya
    asl a
    tay
    ldx ent_order,y
    lda ent_class,x
    cmp o_tmp
    bne :+
    lda ent_fl,x
    and #F_REMOVE
    bne :+
    inc o_d
:   ply
    iny
    bra @l
@a: ldy #0
@l2: cpy ent_nadd
    beq @d
    phy
    tya
    asl a
    tay
    ldx ent_addq,y
    lda ent_class,x
    cmp o_tmp
    bne :+
    inc o_d
:   ply
    iny
    bra @l2
@d: plx
    lda o_d
    rtl

; ent_count_type: A = ET_* -> A = live entities of that type (list not removed + queued). Keeps X.
ent_count_type:
    phx
    sta o_tmp
    stz o_d
    ldx #0
@l: lda ent_type,x
    cmp o_tmp
    bne :+
    lda ent_fl,x
    and #F_REMOVE
    bne :+
    inc o_d
:   inx
    inx
    cpx #2*N
    bcc @l
    plx
    lda o_d
    rtl

; ent_find_type: A = ET_* -> carry set + Y = slot*2 of the first live one in list order. Keeps X.
ent_find_type:
    sta o_tmp
    ldy #0
@l: cpy ent_n
    beq @no
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tay
    lda ent_type,y
    cmp o_tmp
    bne @n
    lda ent_fl,y
    and #F_REMOVE
    bne @n
    pla
    sec
    rtl
@n: ply
    iny
    bra @l
@no: clc
    rtl

; MarkKilled(e = X) (also exported as ent_kill)
w_mark_killed:
ent_kill:
    lda ent_fl,x
    ora #F_KILLED
    sta ent_fl,x
    lda ent_spawnidx,x
    bmi @r
    jsr killed_index
    phx
    tax
    sep #$20
    lda #1
    sta f:killed,x
    rep #$20
    plx
@r: rtl

; A = spawn index -> A = index into killed[] for the current area
killed_index:
    sta o_tmp
    lda area_idx
    and #7
    xba
    asl a                       ; *512
    clc
    adc o_tmp
    rts

; ================================================================== spawning (World.SpawnInitial / Spawner / TrySpawn)
.export ent_area_reset, ent_level_reset, ent_coop_collide, ent_pon_any
ent_level_reset:
    stz w_battlewon
    jsl eng_tlog_reset          ; new level visit: forget collected tiles
    ldx #0
    lda #0
@c: sta f:killed,x
    inx
    inx
    cpx #8*512
    bcc @c
    rtl

; clear per-area spawn state (called on LoadArea); killed[] survives for the level (C# areaKilled)
ent_area_reset:
    jsl ent_init
    ldx #0
    lda #0
@c: sta f:spawned,x
    inx
    inx
    cpx #512
    bcc @c
    ; GoalBox (added directly to the list, before any spawn)
    lda area_goalx
    bmi @nog
    asl a
    asl a
    asl a
    asl a
    sta ent_new_x
    lda area_goaly
    asl a
    asl a
    asl a
    asl a
    sta ent_new_y
    lda #ET_GOAL_BOX
    ldy #0
    jsl ent_spawn
    jsr flush_adds
@nog:
    rtl

; ent_spawn_initial: after the camera is set
ent_spawn_initial:
    ; c0 = FloorDiv(CamX - 32, 16), c1 = FloorDiv(CamX + 272, 16)
    lda cam_x
    sec
    sbc #32
    ASR4
    sta spn_lastL
    lda cam_x
    clc
    adc #272
    ASR4
    sta spn_lastR
    lda cam_y
    sec
    sbc #32
    ASR4
    sta spn_lastU
    lda cam_y
    clc
    adc #208
    ASR4
    sta spn_lastD
    stz spn_i
@l: lda spn_i
    cmp area_nspawn
    bcs @d
    jsr spawn_xy                ; e_t0 = x, e_t1 = y
    lda e_t0
    sec
    sbc spn_lastL
    bmi @n
    lda spn_lastR
    sec
    sbc e_t0
    bmi @n
    lda area_scroll
    cmp #SCROLL_VERTICAL
    bne @y
    lda e_t1
    sec
    sbc spn_lastU
    bmi @n
    lda spn_lastD
    sec
    sbc e_t1
    bmi @n
@y: lda spn_i
    jsr try_spawn
@n: inc spn_i
    bra @l
@d: jsr flush_adds
    rtl

; A = spawn index -> e_t0 = x, e_t1 = y, e_t2 = code
spawn_xy:
    sta e_t2
    asl a
    adc e_t2
    tay
    lda area_spawn_ptr
    sta e_p2
    lda area_spawn_ptr+1
    sta e_p2+1
    lda [e_p2],y
    and #$00FF
    sta e_t2
    iny
    lda [e_p2],y
    and #$00FF
    sta e_t0
    iny
    lda [e_p2],y
    and #$00FF
    sta e_t1
    rts

spawner:
    lda area_scroll
    cmp #SCROLL_VERTICAL
    jne @horiz
    ; rows: rd = FloorDiv(CamY + 288 - 96, 16), ru = FloorDiv(CamY - 32, 16)
    lda cam_y
    clc
    adc #192
    ASR4
    sta o_i                     ; rd
    lda cam_y
    sec
    sbc #32
    ASR4
    sta o_j                     ; ru
    lda cam_y
    cmp prev_cam_y
    beq :+
    bmi :+
    bra @dn
:   lda o_i
    sec
    sbc spn_lastD
    bmi @up
    beq @up
@dn: lda spn_lastD
    inc a
    sta o_dir
    lda o_i
    dec a
    dec a
    sec
    sbc o_dir
    bmi :+
    lda o_i
    dec a
    dec a
    sta o_dir
:
@dl: lda o_i
    sec
    sbc o_dir
    bmi @dd
    lda o_dir
    jsr spawn_row
    inc o_dir
    bra @dl
@dd: lda o_i
    sec
    sbc spn_lastD
    bmi @up
    lda o_i
    sta spn_lastD
@up:
    lda cam_y
    sec
    sbc prev_cam_y
    bmi @u2
    lda o_j
    sec
    sbc spn_lastU
    bpl @fin
@u2: lda spn_lastU
    dec a
    sta o_dir
    lda o_j
    inc a
    inc a
    sec
    sbc o_dir
    bpl :+
    lda o_j
    inc a
    inc a
    sta o_dir
:
@ul: lda o_dir
    sec
    sbc o_j
    bmi @ud
    lda o_dir
    jsr spawn_row
    dec o_dir
    bra @ul
@ud: lda spn_lastU
    sec
    sbc o_j
    bmi @fin
    lda o_j
    sta spn_lastU
@fin:
    ; lastRowD = min(lastRowD, rd + 1); lastRowU = max(lastRowU, ru - 1)
    lda o_i
    inc a
    sec
    sbc spn_lastD
    bpl :+
    lda o_i
    inc a
    sta spn_lastD
:   lda o_j
    dec a
    sec
    sbc spn_lastU
    bmi :+
    lda o_j
    dec a
    sta spn_lastU
:   rts
@horiz:
    lda cam_x
    clc
    adc #272
    ASR4
    sta o_i                     ; cr
    lda cam_x
    sec
    sbc #32
    ASR4
    sta o_j                     ; cl
    lda o_i
    sec
    sbc spn_lastR
    bmi @nr
    beq @nr
    ; for c = max(lastR+1, cr-3) .. cr
    lda spn_lastR
    inc a
    sta o_dir
    lda o_i
    sec
    sbc #3
    sec
    sbc o_dir
    bmi :+
    lda o_i
    sec
    sbc #3
    sta o_dir
:
@rl: lda o_i
    sec
    sbc o_dir
    bmi @rd
    lda o_dir
    jsr spawn_col
    inc o_dir
    bra @rl
@rd: lda o_i
    sta spn_lastR
@nr:
    lda o_j
    sec
    sbc spn_lastL
    bpl @nl
    ; for c = min(lastL-1, cl+3) down to cl
    lda spn_lastL
    dec a
    sta o_dir
    lda o_j
    clc
    adc #3
    sec
    sbc o_dir
    bpl :+
    lda o_j
    clc
    adc #3
    sta o_dir
:
@ll: lda o_dir
    sec
    sbc o_j
    bmi @ld
    lda o_dir
    jsr spawn_col
    dec o_dir
    bra @ll
@ld: lda o_j
    sta spn_lastL
@nl:
    lda o_i
    sec
    sbc spn_lastR
    bpl :+
    lda o_i
    sta spn_lastR
:   lda o_j
    sec
    sbc spn_lastL
    bmi :+
    beq :+
    lda o_j
    sta spn_lastL
:   rts

; SpawnColumn(A = col) / SpawnRow(A = row). The spawner's own o_* loop state is saved around TrySpawn
; (entity init callbacks may use the helpers, which use o_*).
spawn_col:
    ldx #0
    bra spawn_line
spawn_row:
    ldx #2
spawn_line:
    sta spn_col
    lda o_i
    pha
    lda o_j
    pha
    lda o_dir
    pha
    stx o_k
    lda #0
@l: sta spn_i
    cmp area_nspawn
    bcs @d
    jsr spawn_xy
    ldx o_k
    lda e_t0,x                  ; e_t0 = x, e_t1 = y
    cmp spn_col
    bne :+
    lda o_k
    pha
    lda spn_col
    pha
    lda spn_i
    pha
    jsr try_spawn
    pla
    sta spn_i
    pla
    sta spn_col
    pla
    sta o_k
:   lda spn_i
    inc a
    bra @l
@d: pla
    sta o_dir
    pla
    sta o_j
    pla
    sta o_i
    rts

; TrySpawn(A = spawn index)
try_spawn:
    sta spn_idx
    tax
    lda f:spawned,x
    and #$00FF
    bne @r
    lda spn_idx
    jsr killed_index
    tax
    lda f:killed,x
    and #$00FF
    bne @r
    lda spn_idx
    jsr spawn_xy
    ; EntityFactory.Create through the registry: spawn char -> ET_* (0 = null)
    lda e_t2
    and #$007F
    tax
    lda f:ent_spawn_map,x
    and #$00FF
    beq @null
    pha
    lda e_t0
    asl a
    asl a
    asl a
    asl a
    sta ent_new_x
    lda e_t1
    asl a
    asl a
    asl a
    asl a
    sta ent_new_y
    ldy e_t2
    pla
    jsr make_ent                ; X = slot
    bcs @made
@null:
    ldx spn_idx
    sep #$20
    lda #1
    sta f:spawned,x
    rep #$20
@r: rts
@made:
    ; SMB3: 5 enemy slots; the spawn silently fails (retried later)
    lda ent_fl,x
    and #F_SLOT
    beq @ok
    lda ent_class,x
    bne @ok
    lda #EC_ENEMY
    jsl ent_count_class
    cmp #5
    bcc @ok
    stz ent_type,x              ; undo (never queued)
    rts
@ok:
    lda spn_idx
    sta ent_spawnidx,x
    jsr enqueue
    ldx spn_idx
    sep #$20
    lda #1
    sta f:spawned,x
    rep #$20
    rts

; ================================================================== World object loop
ent_tick:
    jsr spawner
    ; updates in list order
    ldy #0
@u: cpy ent_n
    beq @ud
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    stx ent_cur
    lda ent_fl,x
    and #F_REMOVE
    bne :+
    lda g_coop
    beq @upd
    txa                         ; co-op: an object the other player carries updates with that player in the slot
    inc a
    inc a
    cmp f:co_blk+CO_CARRY
    bne @upd
    jsl co_swap
    ldy #VT_UPDATE
    jsr cb_call
    jsl co_swap
    bra :+
@upd:
    ldy #VT_UPDATE
    jsr cb_call
:   ply
    iny
    bra @u
@ud:
    jsr flush_adds
    jsr collisions
    jsr despawn
    jsr remove_all
    rtl

; only effects (EndTick)
ent_effects_tick:
    ldy #0
@u: cpy ent_n
    beq @ud
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    stx ent_cur
    lda ent_class,x
    cmp #EC_EFFECT
    bne :+
    lda ent_fl,x
    and #F_REMOVE
    bne :+
    ldy #VT_UPDATE
    jsr cb_call
:   ply
    iny
    bra @u
@ud:
    jsr flush_adds
    jsr remove_all
    rtl

; Overlaps(e = X, rect tt_x/tt_y/tt_w/tt_h) -> carry
overlaps_rect:
    lda ent_x,x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc ent_hbx,x
    sta e_t0                    ; ex
    lda ent_y,x
    ASR4
    clc
    adc ent_hby,x
    sta e_t1                    ; ey
    ; ex < x + w
    lda tt_x
    clc
    adc tt_w
    sec
    sbc e_t0
    bmi @no
    beq @no
    ; ex + HbW > x
    lda e_t0
    clc
    adc ent_hbw,x
    sec
    sbc tt_x
    bmi @no
    beq @no
    lda tt_y
    clc
    adc tt_h
    sec
    sbc e_t1
    bmi @no
    beq @no
    lda e_t1
    clc
    adc ent_hbh,x
    sec
    sbc tt_y
    bmi @no
    beq @no
    sec
    rts
@no: clc
    rts

; set tt_* rect from entity Y's hitbox (keeps X)
rect_of:
    phx
    tyx
    lda ent_x,x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc ent_hbx,x
    sta tt_x
    lda ent_y,x
    ASR4
    clc
    adc ent_hby,x
    sta tt_y
    lda ent_hbw,x
    sta tt_w
    lda ent_hbh,x
    sta tt_h
    plx
    rts

; is X the carried object? -> Z set if yes
is_carried:
    txa
    inc a
    inc a
    cmp p_carrying
    beq @r
    pha
    lda g_coop                  ; co-op: also carried by the other player
    beq @no
    pla
    cmp f:co_blk+CO_CARRY
    rts
@no: pla                        ; nonzero -> Z clear
@r: rts

; ent_coop_collide: co-op, player-vs-entity collisions for the second player (in the p_* slot). JSL.
ent_coop_collide:
    lda #1
    sta col_plonly
    jsr collisions
    stz col_plonly
    rtl

collisions:
    lda p_state
    beq @go
    cmp #PS_VINE
    beq @go
    cmp #PS_AUTOWALK
    beq @go
    rts
@go:
    stz col_i
@l: lda col_i
    cmp ent_n
    bne :+
    jmp @shells
:   asl a
    tay
    lda ent_order,y
    tax
    stx ent_cur
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    jne @next
    jsr is_carried
    jeq @next
    lda ent_class,x
    cmp #EC_PROJ
    bne @notproj
    jsr proj_vs_enemies
    brl @next
@notproj:
    cmp #EC_EFFECT
    jeq @next
    jsl pl_hitbox
    jsr overlaps_rect
    jcc @next
    ldx ent_cur
    lda ent_class,x
    jne @touch                  ; only enemies get star / statue / slide hits
    lda p_star
    beq @nostar
    lda ent_fl,x
    and #F_STARIMM
    bne @simm
    lda #D_STAR
    sta o_d
    lda p_facing
    sta o_dir
    jsr take_hit
    bcc @simm
    inc p_killtally
    ENT_SFX "KICK"
    ldx ent_cur
    jsl w_mark_killed
    jmp @next
@simm:
    ldx ent_cur
    lda ent_fl,x
    and #F_STARIMM
    jeq @next
    lda ent_fl,x
    and #F_HURTS
    jne @next
    jsr touch
    jmp @next
@nostar:
    lda p_statue
    beq @nostatue
    lda p_yvel
    jmi @next
    jeq @next
    lda #D_STATUE
    sta o_d
    lda p_facing
    sta o_dir
    jsr take_hit
    jcc @next
    ldx ent_cur
    jsl w_mark_killed
    ENT_SFX "KICK"
    jmp @next
@nostatue:
    lda p_sliding
    beq @touch
    lda ent_fl,x
    and #F_SHELLIMM
    bne @touch
    lda ent_fl2,x
    and #F2_BOSS
    bne @touch
    lda #D_SHELL
    sta o_d
    lda p_xvel
    jsr sign16
    bne :+
    lda p_facing
:   sta o_dir
    jsr take_hit
    bcc @next
    ldx ent_cur
    jsl w_mark_killed
    inc p_killtally
    ENT_SFX "KICK"
    bra @next
@touch:
    jsr touch
    ; level-placed items collected by touch stay collected for this level visit (re-entering a room)
    ldx ent_cur
    lda ent_class,x
    cmp #EC_ITEM
    bne @next
    lda ent_fl,x
    and #F_REMOVE
    beq @next
    lda ent_spawnidx,x
    bmi @next
    lda ent_fl,x
    ora #F_KILLED
    sta ent_fl,x
@next:
    inc col_i
    jmp @l
@shells:
    lda col_plonly
    beq :+
    rts
:   ; moving shells and the carried object vs other enemies
    stz col_i
@s: lda col_i
    cmp ent_n
    bne :+
    rts
:   asl a
    tay
    lda ent_order,y
    tax
    stx ent_cur
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    bne @snext
    jsr is_carried
    beq :+
    lda ent_fl,x
    and #F_SHELL
    beq @snext
:   stz col_j
@o: lda col_j
    cmp ent_n
    beq @snext
    asl a
    tay
    lda ent_order,y
    tay
    sty ent_other
    cpy ent_cur
    beq @onext
    lda ent_fl,y
    and #(F_REMOVE|F_DYING)
    bne @onext
    lda ent_class,y
    bne @onext
    tya
    inc a
    inc a
    cmp p_carrying
    beq @onext
    ; s.Overlaps(o)
    jsr rect_of
    ldx ent_cur
    jsr overlaps_rect
    bcc @onext
    jsr shell_vs
    bcs @snext                  ; break
@onext:
    inc col_j
    bra @o
@snext:
    inc col_i
    jmp @s

; s = ent_cur, o = ent_other -> carry set = break
shell_vs:
    ldx ent_cur
    jsr is_carried
    bne @notcarry
    ; carried object hitting an enemy: both die
    ldx ent_other
    lda #D_SHELL
    sta o_d
    lda p_facing
    sta o_dir
    jsr take_hit
    bcc :+
    ldx ent_other
    jsl w_mark_killed
:   ldx ent_cur
    lda p_facing
    NEG16
    jsl ent_knock_off
    jsl w_mark_killed
    stz p_carrying
    ENT_SFX "KICK"
    sec
    rts
@notcarry:
    ldy ent_other
    lda ent_fl,y
    and #F_SHELL
    beq @notshell
    lda ent_xvel,y
    beq @notshell
    ; two moving shells: both die
    lda ent_xvel,x
    jsr sign16
    jsl ent_knock_off
    jsl w_mark_killed
    ldx ent_other
    lda ent_xvel,x
    jsr sign16
    jsl ent_knock_off
    jsl w_mark_killed
    ENT_SFX "KICK"
    sec
    rts
@notshell:
    lda ent_fl,y
    and #F_SHELLIMM
    beq :+
    clc
    rts
:   ; chain points: 100, 200, 400 ... 8000, then 1UPs
    lda ent_chain,x
    inc a
    sta sh_n
    lda ent_points,y
    sta sh_base
    lda sh_n
    cmp #9
    bcc :+
    lda #0
    bra :++
:   dec a
    asl a
    phx
    tax
    lda f:chain_tbl,x
    plx
:   sta ent_points,y
    lda #D_SHELL
    sta o_d
    lda ent_xvel,x
    jsr sign16
    sta o_dir
    ldx ent_other
    jsr take_hit
    bcc @nohit
    ldx ent_cur
    lda sh_n
    sta ent_chain,x
    cmp #9
    bcc :+
    ldx ent_other
    jsl ent_px
    pha
    jsl ent_py
    tay
    plx
    jsl w_one_up_at
:   ldx ent_other
    jsl w_mark_killed
    ENT_SFX "KICK"
    clc
    rts
@nohit:
    ldx ent_other
    lda sh_base
    sta ent_points,x
    clc
    rts
chain_tbl: .word $0100, $0200, $0400, $0800, $1000, $2000, $4000, $8000

; projectile ent_cur vs enemies: its touch callback gets ent_other; carry set = stop
proj_vs_enemies:
    stz col_j
@l: lda col_j
    cmp ent_n
    beq @d
    asl a
    tay
    lda ent_order,y
    tay
    sty ent_other
    lda ent_fl,y
    and #(F_REMOVE|F_DYING)
    bne @n
    lda ent_class,y
    bne @n
    ldx ent_cur
    jsr rect_of
    jsr overlaps_rect
    bcc @n
    ldx ent_cur
    ldy #VT_TOUCH
    jsr cb_call
    bcs @d
@n: inc col_j
    bra @l
@d: rts

; Despawn
despawn:
    ldy #0
@l: cpy ent_n
    bne :+
    rts
:   phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #F_REMOVE
    jne @n
    lda ent_class,x
    cmp #EC_EFFECT
    jeq @n
    cmp #EC_SPECIAL
    jeq @n
    cmp #EC_PLATFORM
    bne :+
    lda ent_spawnidx,x
    jmi @n
:   jsr is_carried
    jeq @n
    lda ent_class,x
    cmp #EC_PROJ
    beq @proj
    cmp #EC_EPROJ
    beq @proj
    lda area_scroll
    cmp #SCROLL_VERTICAL
    beq @vert
    ; off = Px < CamX - 128 || Px > CamX + 384
    jsl ent_px
    sec
    sbc cam_x
    cmp #$10000-128
    bcs @n                      ; -128..-1 : on
    cmp #385
    bcc @n
    bra @off
@vert:
    jsl ent_py
    sec
    sbc cam_y
    clc
    adc #80
    bmi @off
    cmp #321+80
    bcs @off
    bra @n
@proj:
    ; Px < CamX - 32 || Px > CamX + 288 || Py > CamY + 240 || Py < CamY - 128
    jsl ent_px
    sec
    sbc cam_x
    clc
    adc #32
    bmi @off
    cmp #321
    bcs @off
    jsl ent_py
    sec
    sbc cam_y
    clc
    adc #128
    bmi @off
    cmp #369
    bcs @off
    bra @n
@off:
    lda ent_fl,x
    ora #F_REMOVE
    sta ent_fl,x
    lda ent_spawnidx,x
    bmi @n
    lda ent_fl,x
    and #F_KILLED
    beq @resp
    lda ent_spawnidx,x
    jsr killed_index
    phx
    tax
    sep #$20
    lda #1
    sta f:killed,x
    rep #$20
    plx
    bra @n
@resp:
    lda ent_spawnidx,x
    phx
    tax
    sep #$20
    lda #0
    sta f:spawned,x
    rep #$20
    plx
@n: ply
    iny
    brl @l

; ================================================================== engine entry points (called by the world/player)
; BumpAbove(e_tx, e_ty): entities standing on the block get OnBumpBelow
bump_above_x:
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta db_y                    ; top
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta db_x                    ; left
    lda e_tx
    pha
    lda e_ty
    pha
    stz col_j
@l: lda col_j
    cmp ent_n
    beq @d
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    bne @n
    ; e.Bottom >= top - 2 && e.Bottom <= top + 4
    jsl ent_bottom
    sec
    sbc db_y
    clc
    adc #2
    bmi @n
    cmp #7
    bcs @n
    ; e.Px + e.Wd > tx*16 && e.Px < tx*16 + 16
    jsl ent_px
    clc
    adc ent_wd,x
    sec
    sbc db_x
    bmi @n
    beq @n
    jsl ent_px
    sec
    sbc db_x
    cmp #16
    bpl @n
    ; d = e.Cx < tx*16 + 8 ? -1 : 1
    jsl ent_cx
    sec
    sbc db_x
    sec
    sbc #8
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   sta o_dir
    lda db_x
    pha
    lda db_y
    pha
    lda col_j
    pha
    jsr bump_below
    pla
    sta col_j
    pla
    sta db_y
    pla
    sta db_x
@n: inc col_j
    bra @l
@d: pla
    sta e_ty
    pla
    sta e_tx
    rtl

; KickCarried
ent_kick_carried:
    lda p_carrying
    bne :+
    rtl
:   dec a
    dec a
    tax
    stz p_carrying
    ; px = P.Px + (facing > 0 ? 12 : -12) ; Y = P.Py + (Big ? 16 : 18)
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #12
    bra :++
:   sec
    sbc #12
:   asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    lda #18
    ldy p_form
    beq :+
    lda #16
:   sta o_tmp
    jsr ppy
    clc
    adc o_tmp
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    ; kicked into a wall: dies
    jsl ent_py
    clc
    adc #8
    tay
    jsl ent_cx
    jsl ent_solid_at
    bcc @ok
    lda p_facing
    jsl ent_knock_off
    jsl w_mark_killed
    ENT_SFX "KICK"
    rtl
@ok:
    ; the type may handle the kick (Shell.Kick); otherwise XVel = facing*$30, YVel = -$10
    stx ent_cur
    lda #D_KICK
    sta ent_dmg
    lda p_facing
    sta ent_dir
    ldy #VT_HIT
    jsr cb_call
    bcs :+
    ldx ent_cur
    lda p_facing
    asl a
    clc
    adc p_facing                ; *3
    asl a
    asl a
    asl a
    asl a                       ; *$30
    sta ent_xvel,x
    lda #$10000-$10
    sta ent_yvel,x
:   lda #$0C
    sta p_kickpose
    ENT_SFX "KICK"
    rtl

ent_drop_carried:
    lda p_carrying
    bne :+
    rtl
:   dec a
    dec a
    tax
    stz p_carrying
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #10
    bra :++
:   sec
    sbc #10
:   asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    stz ent_xvel,x
    rtl

ent_remove_carried:
    lda p_carrying
    beq :+
    dec a
    dec a
    tax
    jsl ent_remove
    stz p_carrying
:   rtl

; TailHit (timer in p_tailattack)
tail_hit_x:
    stz col_j
@l: ; bx = Facing > 0 ? Px - 10 : Px + 17 ; by = Py + 16 ; rect 10x15 (re-set each time: callbacks may use tt_*)
    ; SMB3-like reach: 18x20 box from the body edge (was 10x15 at Py+16)
    jsr ppx
    ldy p_facing
    bmi :+
    sec
    sbc #17
    bra :++
:   clc
    adc #15
:   sta tt_x
    jsr ppy
    clc
    adc #12
    sta tt_y
    lda #18
    sta tt_w
    lda #20
    sta tt_h
    lda col_j
    cmp ent_n
    beq @d
    asl a
    tay
    lda ent_order,y
    tax
    stx ent_cur
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    bne @n
    lda ent_class,x
    bne @n
    jsr overlaps_rect
    bcc @n
    lda #D_TAIL
    sta o_d
    ; dir = -Facing * -1 * (Facing > 0 ? -1 : 1) = -1 for both facings
    lda #$FFFF
    sta o_dir
    jsr take_hit
    bcc @n
    ldx ent_cur
    jsl w_mark_killed
    ENT_SFX "KICK"
    jsl ent_cx
    sec
    sbc #4
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    jsl ent_sparkle_at
@n: inc col_j
    jmp @l
@d:
    lda p_tailattack
    cmp #9
    bne @r
    ; block hit at (Facing > 0 ? Px - 6 : Px + 21, Py + 28)
    jsr ppx
    ldy p_facing
    bmi :+
    sec
    sbc #6
    bra :++
:   clc
    adc #21
:   ASR4
    sta e_tx
    jsr ppy
    clc
    adc #28
    ASR4
    sta e_ty
    jsl eng_tile_at
    cmp #T_BRICK
    bne @nb
    lda f:lvl_cont,x
    and #$00FF
    bne @nb
    jsl w_break_brick
    rtl
@nb:
    jsl eng_tile_at
    PROPS3
    and #TP_BUMP
    beq @r
    lda #1
    ldx #1
    jsl w_hit_block
@r: rtl

; StatueLanded: enemies under the statue's feet (Px + 2, Py + 28, 12x8) take a Statue hit
statue_landed_x:
    stz col_j
@l: jsr ppx
    clc
    adc #2
    sta tt_x
    jsr ppy
    clc
    adc #28
    sta tt_y
    lda #12
    sta tt_w
    lda #8
    sta tt_h
    lda col_j
    cmp ent_n
    beq @d
    asl a
    tay
    lda ent_order,y
    tax
    stx ent_cur
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    bne @n
    lda ent_class,x
    bne @n
    jsr overlaps_rect
    bcc @n
    lda #D_STATUE
    sta o_d
    lda p_facing
    sta o_dir
    jsr take_hit
    bcc @n
    ldx ent_cur
    jsl w_mark_killed
@n: inc col_j
    bra @l
@d: rtl

; PlatformSupport: any platform carrying the player (F_PON) -> carry
ent_platform_support:
    ldy #0
@l: cpy ent_n
    beq @no
    phy
    tya
    asl a
    tay
    ldx ent_order,y
    ply
    lda ent_class,x
    cmp #EC_PLATFORM
    bne @n
    jsr pon_get
    beq @n
    sec
    rtl
@n: iny
    bra @l
@no: clc
    rtl

; ent_release: A = content, X = big, e_tx/e_ty = block (World.Release for item contents)
ent_release:
    sta o_tmp
    stx o_d
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta ent_new_x
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta ent_new_y
    ldy #0
    lda o_tmp
    cmp #CT_FLOWER
    bne @nf
    lda o_d
    beq @mush
    lda #ET_FLOWER
    bra @sprout
@nf: cmp #CT_LEAF
    bne @nl
    lda o_d
    beq @mush
    lda #ET_LEAF
    bra @sprout
@nl: cmp #CT_STAR
    bne @ns
    lda #ET_STAR
    bra @sprout
@ns: cmp #CT_ONEUP
    bne @n1
    ldy #1
    lda #ET_MUSHROOM
    bra @sprout
@n1: cmp #CT_VINE
    bne @nv
    lda #ET_VINE_SPROUT
    jsl ent_spawn
    ENT_SFX "VINE"
    rtl
@nv: cmp #CT_PSWITCH
    bne @np
    lda ent_new_y
    sec
    sbc #16
    sta ent_new_y
    lda #ET_PSWITCH
    bra @sprout
@np: ENT_SFX "BUMP"
    rtl
@mush:
    lda #ET_MUSHROOM
@sprout:
    jsl ent_spawn
    ENT_SFX "SPROUT"
    rtl

; ------------------------------------------------------------------ effects and block visuals
; A = type, Y = arg, at (e_t3, e_t4) px -> carry, Y = slot
fx_at:
    pha
    lda e_t3
    sta ent_new_x
    lda e_t4
    sta ent_new_y
    pla
    jsl ent_spawn
    rts

ent_add_fx_dust:
    ; Dust(Px + (Facing > 0 ? 10 : -2), Py + 26)
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #10
    bra :++
:   sec
    sbc #2
:   sta e_t3
    jsr ppy
    clc
    adc #26
    sta e_t4
    lda #ET_FX_DUST
    jsr fx_at
    rtl

ent_add_fx_splash:
    ; Splash(Px, wr*16 - 8)
    jsr ppx
    sta e_t3
    lda area_water
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #8
    sta e_t4
    lda #ET_FX_SPLASH
    jsr fx_at
    rtl

ent_add_fx_puff_player:
    jsr ppx
    sta e_t3
    jsr ppy
    clc
    adc #8
    sta e_t4
    lda #ET_FX_PUFF
    ldy #0
    jsr fx_at
    rtl

; X = px, Y = py
ent_add_sparkle:
    stx e_t3
    sty e_t4
    lda #ET_FX_SPARKLE
    jsr fx_at
    rtl

ent_add_coinpop:
    stx e_t3
    sty e_t4
    lda #ET_FX_COINPOP
    jsr fx_at
    rtl

; ScorePopup: A = BCD points ($FFFF = 1UP), X = px, Y = py
ent_add_popup:
    stx e_t3
    sty e_t4
    tay
    lda #ET_FX_POPUP
    jsr fx_at
    rtl

; BumpBlock(e_tx, e_ty, dir = A, final = X)
ent_add_bump:
    sta bump_dir
    stx bump_final
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta ent_new_x
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta ent_new_y
    lda #ET_FX_BUMP
    ldy bump_final
    jsl ent_spawn
    bcc @r
    lda e_tx
    sta ent_v0,y
    lda e_ty
    sta ent_v1,y
    lda bump_dir
    sta ent_v2,y
    lda bump_final
    sta ent_v3,y
    lda bump_final
    jsl eng_set_tile
    jsl eng_hide_tile
@r: rtl

ent_add_debris4:
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta db_x
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta db_y
    lda #0
    ldx #$10000-$10
    ldy #$10000-$40
    jsr debris
    lda #8
    ldx #$10
    ldy #$10000-$40
    jsr debris
    lda #$0800
    ldx #$10000-$10
    ldy #$10000-$28
    jsr debris
    lda #$0808
    ldx #$10
    ldy #$10000-$28
    jsr debris
    rtl
; A = offsets (lo = dx, hi = dy), X = xvel, Y = yvel
debris:
    stx o_i
    sty o_j
    pha
    and #$00FF
    clc
    adc db_x
    sta ent_new_x
    pla
    xba
    and #$00FF
    clc
    adc db_y
    sta ent_new_y
    lda #ET_FX_DEBRIS
    jsl ent_spawn
    bcc :+
    lda o_j
    sta ent_yvel,y
    lda o_i
    sta ent_xvel,y
:   rts

; ------------------------------------------------------------------ player projectiles
ent_throw_fireball:
    lda #EC_PROJ
    jsl ent_count_class
    cmp #2
    bcc :+
    rtl
:   lda p_form
    cmp #PF_HAMMER
    beq @hammer
    ; Fireball(Px + (Facing > 0 ? 8 : 0), Py + 14, Facing)
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #8
:   sta ent_new_x
    jsr ppy
    clc
    adc #14
    sta ent_new_y
    lda #ET_PL_FIREBALL
    ldy p_facing
    jsl ent_spawn
    bcc @r
    lda #$0B
    sta p_throwpose
    ENT_SFX "FIREBALL"
@r: rtl
@hammer:
    ; PlayerHammer(Px + (Facing > 0 ? 6 : 2), Py + 6, Facing, XVel)
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #4
:   inc a
    inc a
    sta ent_new_x
    jsr ppy
    clc
    adc #6
    sta ent_new_y
    lda #ET_PL_HAMMER
    ldy p_facing
    jsl ent_spawn
    bcc @r
    lda #$0B
    sta p_throwpose
    ENT_SFX "HAMMER"
    rtl

; ================================================================== helpers for entity code (JSL, AXY16, keep X)
; player position helpers (internal)
ppx:
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    rts
ppy:
    lda p_y
    ASR4
    rts
pcx:
    jsr ppx
    clc
    adc #8
    rts

sign16:
    cmp #0
    beq @z
    bmi @m
    lda #1
    rts
@m: lda #$FFFF
@z: rts

; ent_px / ent_py: A = Px (X >> 4, unsigned) / Py (Y >> 4, signed)
ent_px:
    lda ent_x,x
    lsr a
    lsr a
    lsr a
    lsr a
    rtl
ent_py:
    lda ent_y,x
    ASR4
    rtl
; ent_cx: A = Cx = Px + Wd/2
ent_cx:
    lda ent_wd,x
    lsr a
    sta o_d
    lda ent_x,x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc o_d
    rtl
; ent_bottom: A = Py + Ht
ent_bottom:
    lda ent_y,x
    ASR4
    clc
    adc ent_ht,x
    rtl
; ent_player_dx: A = P.CenterX - Cx (signed)
ent_player_dx:
    jsl ent_cx
    sta o_d
    jsr pcx
    sec
    sbc o_d
    rtl
; ent_face_player: A = FaceToward(P) = P.CenterX < Cx ? -1 : 1
ent_face_player:
    jsl ent_player_dx
    bmi @l
    lda #1
    rtl
@l: lda #$FFFF
    rtl
; ent_face_player_set: constructor rule Facing = P.CenterX < Px + 8 ? -1 : 1 (stored), A = Facing
ent_face_player_set:
    jsl ent_px
    clc
    adc #8
    sta o_d
    jsr pcx
    sec
    sbc o_d
    bmi @l
    lda #1
    sta ent_facing,x
    rtl
@l: lda #$FFFF
    sta ent_facing,x
    rtl

; ent_tile_at: A = px, Y = py (level pixels) -> A = tile type T_* ; ent_props_at -> A = TP_* bits
ent_tile_at:
    phx
    jsl eng_tile_at_px
    plx
    and #$00FF
    rtl
ent_props_at:
    phx
    jsl eng_tile_at_px
    PROPS3
    plx
    and #$00FF
    rtl
; ent_solid_at / ent_floor_at: A = px, Y = py -> carry set if solid (C# SolidAt) / floor (FloorAt)
ent_solid_at:
    phx
    jsl eng_tile_at_px
    PROPS3
    plx
    and #TP_SOLID
    beq @no
    sec
    rtl
@no: clc
    rtl
ent_floor_at:
    phx
    jsl eng_tile_at_px
    PROPS3
    plx
    and #TP_FLOOR
    beq @no
    sec
    rtl
@no: clc
    rtl
; ent_in_water: Y = py -> carry set if FloorDiv(py,16) >= water row (C# W.InWater; x is irrelevant)
ent_in_water:
    lda area_water
    bmi @no
    sta o_d
    tya
    ASR4
    cmp o_d
    bmi @no
    sec
    rtl
@no: clc
    rtl

; ent_remove: Remove = true
ent_remove:
    lda ent_fl,x
    ora #F_REMOVE
    sta ent_fl,x
    rtl

; ent_apply_vel: X += XVel, Y += YVel
ent_apply_vel:
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    rtl

; ent_update_knocked (C# UpdateKnocked): carry set if the entity is in its knocked-off fall (then just rtl)
ent_update_knocked:
    lda ent_fl,x
    and #F_DYING
    beq @no
    lda ent_knock,x
    beq @no
    jsl ent_apply_vel
    lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    lda cam_y
    clc
    adc #260
    sta o_tmp
    jsl ent_py
    sec
    sbc o_tmp
    bmi :+
    beq :+
    jsl ent_remove
:   sec
    rtl
@no: clc
    rtl

; ent_knock_off: A = dir (-1/0/+1) (C# KnockOff: flips, falls off-screen, adds Points)
ent_knock_off:
    sta o_dir
    lda ent_fl,x
    and #F_DYING
    beq :+
    rtl
:   lda ent_fl,x
    ora #(F_DYING|F_KILLED)
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda #$10000-$30
    sta ent_yvel,x
    lda o_dir
    bne :+
    lda ent_facing,x
    NEG16
:   asl a
    asl a
    asl a
    sta ent_xvel,x
    lda #1
    sta ent_knock,x
    lda ent_points,x
    jsl ent_score
    rtl

; ent_default_hit: C# Entity.TakeHit (immunity flags, then KnockOff(ent_dir)) -> carry = affected
ent_default_hit:
    lda ent_dmg
    cmp #D_KICK
    beq @no
    ldy #F_FIREIMM
    cmp #D_FIRE
    beq @chk
    ldy #F_TAILIMM
    cmp #D_TAIL
    beq @chk
    ldy #F_SHELLIMM
    cmp #D_SHELL
    beq @chk
    ldy #F_STARIMM
    cmp #D_STAR
    beq @chk
    ldy #F_HAMMERIMM
    cmp #D_HAMMER
    bne @hit
@chk: tya
    and ent_fl,x
    bne @no
@hit: lda ent_dir
    jsl ent_knock_off
    sec
    rtl
@no: clc
    rtl

; ent_score: A = BCD points -> score + popup at (Px, Py). Keeps X.
ent_score:
    phx
    pha
    jsl ent_py
    tay
    jsl ent_px
    tax
    pla
    jsl w_add_score_at
    plx
    rtl

; ent_puff: Puff effect at (Px, Py) (C# W.Puff). ent_puff_at: at (ent_new_x, ent_new_y).
ent_puff:
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
ent_puff_at:
    phx
    lda #ET_FX_PUFF
    ldy #0
    jsl ent_spawn
    plx
    rtl
; ent_sparkle_at: Sparkle effect at (ent_new_x, ent_new_y)
ent_sparkle_at:
    phx
    lda #ET_FX_SPARKLE
    ldy #0
    jsl ent_spawn
    plx
    rtl
; ent_popup: A = BCD points ($FFFF = "1UP") floating text at (ent_new_x, ent_new_y), no score added
ent_popup:
    phx
    tay
    lda #ET_FX_POPUP
    jsl ent_spawn
    plx
    rtl

; ent_sfx: A = SFX_* id (16-bit). Keeps X and Y.
ent_sfx:
    phx
    phy
    sep #$20
    jsl snd_sfx
    rep #$30
    ply
    plx
    rtl

; ent_hurt_player: p.Hurt(). Keeps X.
ent_hurt_player:
    phx
    jsl pl_hurt
    plx
    rtl

; Gravity (C# Entity.Gravity): +3 (max $40), in water +1 (max $10)
ent_gravity:
    lda area_water
    bmi @dry
    lda ent_ht,x
    lsr a
    sta o_tmp
    jsl ent_py
    clc
    adc o_tmp
    ASR4
    cmp area_water
    bmi @dry
    lda ent_yvel,x
    inc a
    bmi :+
    cmp #$11
    bcc :+
    lda #$10
:   sta ent_yvel,x
    rtl
@dry:
    lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    rtl

; e_solid / e_floor (internal): A = px, Y = py -> Z clear if solid/floor. Keeps X.
e_solid:
    phx
    jsl eng_tile_at_px
    PROPS3
    and #TP_SOLID
    plx
    cmp #0
    rts
e_floor:
    phx
    jsl eng_tile_at_px
    PROPS3
    and #TP_FLOOR
    plx
    cmp #0
    rts
e_tile:
    phx
    jsl eng_tile_at_px
    plx
    cmp #0
    rts

; ent_move_walker (C# MoveWalker): A = turnAtLedges (0/1), Y = turnAtWalls (0/1) -> carry set if it hit a wall
; (tile collision incl. walls, floors, semisolids, slopes, conveyors, ledge turns, falling out of the level)
ent_move_walker:
    sta o_i
    sty o_j
    stz o_k                     ; hitWall
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    ; midY = py + Ht - 8
    jsl ent_py
    clc
    adc ent_ht,x
    sec
    sbc #8
    sta e_t5
    lda ent_xvel,x
    beq @nowall
    bmi @wl
    ; right: SolidAt(px + Wd - 2, midY)
    jsl ent_px
    clc
    adc ent_wd,x
    dec a
    dec a
    sta e_t6
    ldy e_t5
    jsr e_solid
    beq @nowall
    ; X = ((((px + Wd - 2) >> 4) << 4) - Wd + 1) << 4
    lda e_t6
    and #$FFF0
    sec
    sbc ent_wd,x
    inc a
    asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    inc o_k
    bra @nowall
@wl: jsl ent_px
    inc a
    sta e_t6
    ldy e_t5
    jsr e_solid
    beq @nowall
    ; X = (((((px + 1) >> 4) + 1) << 4) - 1) << 4
    lda e_t6
    and #$FFF0
    clc
    adc #15
    asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    inc o_k
@nowall:
    lda o_k
    beq :+
    lda o_j
    beq :+
    lda ent_xvel,x
    NEG16
    sta ent_xvel,x
    beq :+
    jsr sign16
    sta ent_facing,x
:   jsl ent_gravity
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    jsl ent_bottom
    sta e_t5                    ; feet
    ; slopes: walkers follow the surface under their center
    lda ent_yvel,x
    jmi @noslope
    lda area_slopes
    jeq @noslope
    jsl ent_cx
    sta e_t6                    ; cxp
    ASR4
    sta e_tx
    stz e_t7                    ; k
@sk: lda e_t7
    cmp #3
    jcs @noslope
    ; yy = k==0 ? feet-16 : k==1 ? feet : feet+8
    lda e_t5
    ldy e_t7
    bne :+
    sec
    sbc #16
    bra :++
:   cpy #2
    bne :+
    clc
    adc #8
:   ASR4
    sta e_ty
    phx
    jsl eng_tile_at
    plx
    sta e_t2
    cmp #T_SLOPEUP
    beq :+
    cmp #T_SLOPEDOWN
    jne @snext
:   ; s = SlopeSurface
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta o_tmp
    lda e_t6
    sec
    sbc o_tmp
    bpl :+
    lda #0
:   cmp #16
    bcc :+
    lda #15
:   ldy e_t2
    cpy #T_SLOPEUP
    bne :+
    eor #$FFFF
    clc
    adc #16                     ; 15 - lx
:   sta o_tmp
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    clc
    adc o_tmp
    sta e_t2                    ; s
    ; if (feet >= s - (OnGround ? 8 : 0) && feet - s < 12)
    lda ent_fl,x
    and #F_GROUND
    beq :+
    lda e_t2
    sec
    sbc #8
    bra :++
:   lda e_t2
:   sta o_tmp
    lda e_t5
    sec
    sbc o_tmp
    bmi @snext
    lda e_t5
    sec
    sbc e_t2
    cmp #12
    bpl @snext
    lda e_t2
    sec
    sbc ent_ht,x
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    stz ent_yvel,x
    lda ent_fl,x
    ora #F_GROUND
    sta ent_fl,x
    jsl ent_fell_out
    lda o_k
    cmp #1
    rtl
@snext:
    inc e_t7
    jmp @sk
@noslope:
    lda ent_yvel,x
    jmi @rising
    ; f = FloorAt(px + 3, feet) || FloorAt(px + Wd - 4, feet)
    jsl ent_px
    clc
    adc #3
    ldy e_t5
    jsr e_floor
    bne @fl
    jsl ent_px
    clc
    adc ent_wd,x
    sec
    sbc #4
    ldy e_t5
    jsr e_floor
    beq @air
@fl: lda e_t5
    and #15
    cmp #8
    bcs @air
    lda e_t5
    and #$FFF0
    sec
    sbc ent_ht,x
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    stz ent_yvel,x
    lda ent_fl,x
    ora #F_GROUND
    sta ent_fl,x
    ; conveyors
    lda w_pswitch
    bne @ledge
    jsl ent_cx
    ldy e_t5
    jsr e_tile
    cmp #T_CONVEYORL
    bne :+
    lda ent_x,x
    sec
    sbc #16
    sta ent_x,x
    bra @ledge
:   cmp #T_CONVEYORR
    bne @ledge
    lda ent_x,x
    clc
    adc #16
    sta ent_x,x
    bra @ledge
@air:
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
    bra @ledge
@rising:
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
    ; head: SolidAt(px + Wd/2, py + 2)
    jsl ent_py
    inc a
    inc a
    sta o_tmp
    tay
    jsl ent_cx
    jsr e_solid
    beq @ledge
    stz ent_yvel,x
    ; Y = ((((py + 2) >> 4) + 1) << 4) - 2 << 4
    lda o_tmp
    and #$FFF0
    clc
    adc #14
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
@ledge:
    lda ent_fl,x
    and #F_GROUND
    beq @done
    lda o_i
    beq @done
    ; ahead = XVel > 0 ? px + Wd : px - 1 ; !FloorAt(ahead, Py + Ht + 2) -> turn
    jsl ent_bottom
    inc a
    inc a
    tay
    lda ent_xvel,x
    beq :+
    bpl :++
:   jsl ent_px
    dec a
    bra :++
:   jsl ent_px
    clc
    adc ent_wd,x
:   jsr e_floor
    bne @done
    lda ent_xvel,x
    NEG16
    sta ent_xvel,x
    lda ent_facing,x
    NEG16
    sta ent_facing,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
@done:
    jsl ent_fell_out
    lda o_k
    cmp #1
    rtl

; ent_fell_out: Py > LevelPxH + 32 -> Remove + Killed (carry set if so)
ent_fell_out:
    lda area_h
    asl a
    asl a
    asl a
    asl a
    clc
    adc #32
    sta o_tmp
    jsl ent_py
    sec
    sbc o_tmp
    bmi @no
    beq @no
    lda ent_fl,x
    ora #(F_REMOVE|F_KILLED)
    sta ent_fl,x
    sec
    rtl
@no: clc
    rtl

; ------------------------------------------------------------------ player interaction
; ent_player_overlap: carry set if the player's hitbox overlaps this entity's hitbox
ent_player_overlap:
    jsl pl_hitbox
    jsr overlaps_rect
    rtl
; ent_overlaps_rect: carry set if the hitbox overlaps tt_x/tt_y/tt_w/tt_h (px)
ent_overlaps_rect:
    jsr overlaps_rect
    rtl
; ent_overlaps_ent: Y = other slot*2 -> carry set if the two hitboxes overlap
ent_overlaps_ent:
    jsr rect_of
    jsr overlaps_rect
    rtl

; CanStomp(p, e = X) -> carry (C# World.CanStomp; also w_can_stomp)
ent_can_stomp:
w_can_stomp:
    lda p_swimming
    bne @no
    lda ent_fl,x
    and #F_STOMP
    beq @no
    ; rising && FlyTime == 0 && KillTally == 0 -> no
    lda p_inair
    beq @gr
    lda p_yvel
    bpl @chk
    lda p_flytime
    ora p_killtally
    beq @no
    bra @chk
@gr: ; on the ground: only if the enemy is falling slowly onto the player
    lda ent_yvel,x
    bmi @no
    beq @no
    cmp #$0A
    bcs @no
    lda ent_fl,x
    and #F_GROUND
    bne @no
@chk:
    ; p.Py <= (Bottom - 16) - StompH (19)
    jsl ent_bottom
    sec
    sbc #16+19
    sta o_tmp
    jsr ppy
    sec
    sbc o_tmp
    beq @yes
    bpl @no
@yes: sec
    rtl
@no: clc
    rtl

; StompBounce (C# World.StompBounce): bounce, KillTally++, chain score at the entity, stomp sound. Keeps X.
ent_stomp_bounce:
w_stomp_bounce:
    lda #$10000-$40
    jsl pl_bounce
    inc p_killtally
    phx
    jsl ent_py
    tay
    jsl ent_px
    tax
    lda p_killtally
    jsl w_chain_score
    plx
    ENT_SFX "STOMP"
    rtl

; ent_carried: carry set if the player carries this entity
ent_carried:
    jsr is_carried
    bne @no
    sec
    rtl
@no: clc
    rtl

; ent_offscreen: A = Px - CamX (signed); carry set if outside the C# despawn window (-128..384 / vertical -80..320)
ent_offscreen:
    lda area_scroll
    cmp #SCROLL_VERTICAL
    beq @v
    jsl ent_px
    sec
    sbc cam_x
    cmp #$10000-128
    bcs @in
    cmp #385
    bcc @in
    sec
    rtl
@v: jsl ent_py
    sec
    sbc cam_y
    pha
    clc
    adc #80
    bmi @out
    cmp #321+80
    bcs @out
    pla
@in: clc
    rtl
@out: pla
    sec
    rtl

; ------------------------------------------------------------------ platforms (C# Platform.Update split in two)
; ent_plat_begin: call first in a platform's update (saves prevX/prevY and wasOn)
ent_plat_begin:
    lda ent_x,x
    sta pl_prevx
    lda ent_y,x
    sta pl_prevy
    ; wasOn = playerOn && p.State == Normal && !p.InAir
    stz pl_wason
    jsr pon_get
    beq :+
    lda p_state
    ora p_inair
    bne :+
    inc pl_wason
:   stz pl_near2
    lda g_coop
    beq @r
    ; co-op: the other player (co_blk) -- same test, only when it is on or near this platform
    stz pl_wason2
    jsr pon_other
    beq @near
    inc pl_near2
    lda f:co_blk+CO_STATE
    ora f:co_blk+CO_INAIR
    bne @r
    inc pl_wason2
@r: rtl
@near:
    lda co_hide
    bne @r
    lda f:co_blk+CO_STATE
    bne @r
    ; |other.Px + 8 - (ent.Px + Wd/2)| < Wd/2 + 24 and other.Py + 32 within ent.Py -40..+24
    lda f:co_blk+CO_X
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc #8
    sta o_tmp
    lda ent_wd,x
    lsr a
    sta pl_feet
    jsl ent_px
    clc
    adc pl_feet
    sec
    sbc o_tmp
    bpl :+
    NEG16
:   sec
    sbc pl_feet
    cmp #24
    bpl @r
    lda f:co_blk+CO_Y
    ASR4
    clc
    adc #32
    sta o_tmp
    jsl ent_py
    sec
    sbc o_tmp
    clc
    adc #24
    cmp #64
    bcs @r
    inc pl_near2
    rtl

; slot player's / other player's "stands on platform X" flag (Mario: F_PON in ent_fl, Luigi: ent_pon2) -> Z
pon_get:
    lda co_cur
    bne :+
    lda ent_fl,x
    and #F_PON
    rts
:   lda f:ent_pon2,x
    rts
pon_other:
    lda co_cur
    beq :+
    lda ent_fl,x
    and #F_PON
    rts
:   lda f:ent_pon2,x
    rts
pon_clr:
    lda co_cur
    bne :+
    lda ent_fl,x
    and #$FFFF^F_PON
    sta ent_fl,x
    rts
:   lda #0
    sta f:ent_pon2,x
    rts
pon_set:
    lda co_cur
    bne :+
    lda ent_fl,x
    ora #F_PON
    sta ent_fl,x
    rts
:   lda #1
    sta f:ent_pon2,x
    rts
; ent_pon_any: X = platform -> A nonzero (Z clear) if any player stands on it. JSL.
ent_pon_any:
    lda f:ent_pon2,x
    bne :+
    lda ent_fl,x
    and #F_PON
:   rtl

; ent_plat_end: call after moving the platform: carries the player by (dx, dy), handles landing, sets F_PON.
; Carry set if the player just stood on it this tick (C# OnStood).
ent_plat_end:
    lda ent_x,x
    sec
    sbc pl_prevx
    sta pl_dx
    lda ent_y,x
    sec
    sbc pl_prevy
    sta pl_dy
    lda pl_near2
    beq @one
    jsl co_swap                 ; co-op: the other player first
    lda pl_wason
    pha
    lda pl_wason2
    sta pl_wason
    jsr plat_core
    pla
    sta pl_wason
    jsl co_swap
@one:
    jsr plat_core
    rtl

plat_core:
    lda pl_wason
    beq @land
    lda p_x
    clc
    adc pl_dx
    sta p_x
    lda p_y
    clc
    adc pl_dy
    sta p_y
    ; still on? (p.Px + 12 < Px || p.Px + 4 > Px + Wd) -> off
    jsr ppx
    clc
    adc #12
    sta o_tmp
    jsl ent_px
    sec
    sbc o_tmp
    beq :+
    bpl @land
:   jsl ent_px
    clc
    adc ent_wd,x
    sta o_tmp
    jsr ppx
    clc
    adc #4
    sec
    sbc o_tmp
    beq @keep
    bpl @land
@keep:
    jsl ent_py
    sec
    sbc #32
    asl a
    asl a
    asl a
    asl a
    sta p_y
    stz p_inair
    stz p_yvel
@land:
    ; landing on the platform
    jsr pon_clr
    lda p_state
    jne @no
    lda p_yvel
    jmi @no
    jsr ppx
    clc
    adc #12
    sta o_tmp
    jsl ent_px
    sec
    sbc o_tmp
    beq :+
    jpl @no
:   jsl ent_px
    clc
    adc ent_wd,x
    sta o_tmp
    jsr ppx
    clc
    adc #4
    sec
    sbc o_tmp
    beq :+
    bpl @no
:   ; feet >= Py && feet <= Py + 6 + max(0, dy >> 4)
    jsr ppy
    clc
    adc #32
    sta pl_feet
    jsl ent_py
    sta o_tmp
    lda pl_feet
    sec
    sbc o_tmp
    bmi @no
    lda pl_dy
    ASR4
    bpl :+
    lda #0
:   clc
    adc #6
    adc o_tmp
    sec
    sbc pl_feet
    bmi @no
    lda o_tmp
    sec
    sbc #32
    asl a
    asl a
    asl a
    asl a
    sta p_y
    lda p_inair
    beq :+
    stz p_inair
    stz p_killtally
:   stz p_yvel
    jsr pon_set
    sec
    rts
@no: clc
    rts

; ------------------------------------------------------------------ math
; ent_sin: A = binary angle (0-255 = one turn; high byte ignored), Y = amplitude (-128..127)
;   -> A = sin(angle) * amplitude (signed 16-bit, rounded toward -inf). Uses the PPU multiplier (M7A/M7B).
;   cos(a) = ent_sin(a + 64).
M7A = $211B
M7B = $211C
MPYL = $2134
ent_sin:
    and #$00FF
    asl a
    phx
    tax
    lda f:eng_tables+1800,x
    plx
    sep #$20
    .a8
    sta M7A
    xba
    sta M7A
    tya
    sta M7B
    rep #$20
    .a16
    lda MPYL+1                  ; (sin*32767*amp) >> 8
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a                       ; >> 15 total
    rtl

; ent_div: A (unsigned 16-bit) / Y (1-255) -> A = quotient, Y = remainder. ent_mod: A = remainder.
ent_div:
    sta WRDIVL
    sep #$20
    .a8
    tya
    sta WRDIVB
    rep #$20
    .a16
    nop                         ; 16 cycles for the divider
    nop
    nop
    nop
    nop
    nop
    nop
    nop
    lda RDMPYL
    tay
    lda RDDIVL
    rtl
ent_mod:
    jsl ent_div
    tya
    rtl

; ent_isqrt: e_t1:e_t0 = unsigned 32-bit value -> A = floor(sqrt) (C# (int)Math.Sqrt)
ent_isqrt:
    stz o_tmp                   ; result
    lda #$8000
    sta o_d                     ; bit
@l: lda o_tmp
    ora o_d
    sta o_i                     ; candidate
    ; candidate^2 <= value ? (32-bit compare using the hardware 8x8 multiplier would be slow: do 16x16 by shifts)
    jsr sq32                    ; e_t2:e_t3 = o_i^2
    lda e_t3
    cmp e_t1
    bcc @take
    bne @skip
    lda e_t2
    cmp e_t0
    beq @take
    bcs @skip
@take: lda o_i
    sta o_tmp
@skip: lsr o_d
    bne @l
    lda o_tmp
    rtl
; e_t3:e_t2 = o_i * o_i (shift-and-add)
sq32:
    stz e_t2
    stz e_t3
    lda o_i
    sta e_t4                    ; multiplier bits
    sta e_t5                    ; multiplicand low
    stz e_t6                    ; multiplicand high
@m: lsr e_t4
    bcc @s
    lda e_t2
    clc
    adc e_t5
    sta e_t2
    lda e_t3
    adc e_t6
    sta e_t3
@s: asl e_t5
    rol e_t6
    lda e_t4
    bne @m
    rts

; ent_random: A = next pseudo-random 16-bit value (xorshift; the C# game has no randomness in its entities)
ent_random:
    lda rng_seed
    bne :+
    lda #$ACE1
:   sta o_tmp
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    eor o_tmp
    sta o_tmp
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    eor o_tmp
    sta o_tmp
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    eor o_tmp
    sta rng_seed
    rtl

; ================================================================== drawing
; ent_draw_setxy: spr_arg_x/y = Px - CamX, Py - CamY; spr_arg_flags = SPR_BEHIND if F_BEHIND else 0
ent_draw_setxy:
    jsl ent_px
    sec
    sbc cam_x
    sta spr_arg_x
    jsl ent_py
    sec
    sbc cam_y
    sta spr_arg_y
    stz spr_arg_flags
    lda ent_fl,x
    and #F_BEHIND
    beq :+
    lda #4
    sta spr_arg_flags
:   rtl

; ent_draw_meta: A = metasprite id -> spr_meta at spr_arg_x/y with spr_arg_flags. Keeps X and the spr_arg_*.
ent_draw_meta:
    sta spr_arg_id
    phx
    phy
    sep #$20
    jsl spr_meta
    rep #$30
    ply
    plx
    rtl

; ent_draw_face: spr_arg_flags |= hflip if Facing < 0
ent_draw_face:
    lda ent_facing,x
    bpl :+
    lda spr_arg_flags
    ora #1
    sta spr_arg_flags
:   rtl
; ent_draw_knocked: vflip + face (C# DrawKnocked)
ent_draw_knocked:
    lda spr_arg_flags
    ora #2
    sta spr_arg_flags
    jml ent_draw_face
; ent_draw_offset: spr_arg_x += A, spr_arg_y += Y
ent_draw_offset:
    clc
    adc spr_arg_x
    sta spr_arg_x
    tya
    clc
    adc spr_arg_y
    sta spr_arg_y
    rtl

; C# WorldRender order: behind-BG objects, platforms, normal objects, specials | player | carried, effects
; Entity draw order (C# painter's order): pass 0 behind, 1 platforms, 2 normal, 3 specials, then the player, then
; 4 effects. One classification sweep + counting sort into dl_list instead of 5 sweeps over all entities.
ent_draw:
    stz dl_c
    stz dl_c+2
    stz dl_c+4
    stz dl_c+6
    stz dl_c+8
    stz o_i
@c: lda o_i
    cmp ent_n
    beq @cd
    asl a
    tay
    lda ent_order,y
    tax
    jsr dl_class                ; A = pass*2, 10 = not drawn in a pass
    tyx
    sta f:dl_pass,x
    cmp #10
    beq :+
    tax
    inc dl_c,x
    inc dl_c,x
:   inc o_i
    bra @c
@cd:
    ; counts -> start offsets
    lda #0
    ldx dl_c
    sta dl_c
    stx dl_t
    clc
    adc dl_t
    ldx dl_c+2
    sta dl_c+2
    stx dl_t
    clc
    adc dl_t
    ldx dl_c+4
    sta dl_c+4
    stx dl_t
    clc
    adc dl_t
    ldx dl_c+6
    sta dl_c+6
    stx dl_t
    clc
    adc dl_t
    sta dl_c+8
    ; fill (stable: list order inside each pass)
    stz o_i
@f: lda o_i
    cmp ent_n
    beq @fd
    asl a
    tay
    tyx
    lda f:dl_pass,x
    cmp #10
    beq @fn
    tax
    lda dl_c,x
    inc dl_c,x
    inc dl_c,x
    pha
    lda ent_order,y
    plx
    sta dl_list,x
@fn: inc o_i
    bra @f
@fd:
    ; now dl_c+2*p = end of pass p: draw passes 0-3, keep pass 4 for ent_draw_effects
    stz dl_i
    lda dl_c+6
    sta dl_e
    jsr dl_draw
    lda #1
    sta dl_built
    rtl

dl_class:
    lda ent_fl,x
    bit #F_REMOVE
    bne @skip
    bit #F_BEHIND
    beq @nb
    jsr is_carried
    beq @skip
    lda #0
    rts
@nb: lda ent_class,x
    cmp #EC_PLATFORM
    bne :+
    lda #2
    rts
:   cmp #EC_SPECIAL
    bne :+
    lda #6
    rts
:   cmp #EC_EFFECT
    bne :+
    lda #8
    rts
:   jsr is_carried
    beq @skip
    lda #4
    rts
@skip:
    lda #10
    rts

dl_draw:
@l: lda dl_i
    cmp dl_e
    bcs @r
    tax
    lda dl_list,x
    tax
    lda dl_i
    pha
    lda dl_e
    pha
    jsr draw_one
    pla
    sta dl_e
    pla
    sta dl_i
    inc dl_i
    inc dl_i
    bra @l
@r: rts

.export ent_draw_effects
ent_draw_effects:
    lda p_carrying
    beq :+
    dec a
    dec a
    tax
    jsr draw_one
:   lda g_coop                  ; co-op: the partner's carried object
    beq :+
    lda f:co_blk+CO_CARRY
    beq :+
    dec a
    dec a
    tax
    jsr draw_one
:   lda dl_built
    beq @old
    stz dl_built
    lda dl_c+6
    sta dl_i
    lda dl_c+8
    sta dl_e
    jsr dl_draw
    rtl
@old:
    lda #4
    jsr draw_pass
    rtl

; A = pass: 0 behind, 1 platforms, 2 normal, 3 specials, 4 effects
draw_pass:
    sta o_j
    stz o_i
@l: lda o_i
    cmp ent_n
    jeq @d
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #F_REMOVE
    bne @n
    lda o_j
    bne @notbehind
    lda ent_fl,x
    and #F_BEHIND
    beq @n
    jsr is_carried
    beq @n
    bra @draw
@notbehind:
    lda ent_fl,x
    and #F_BEHIND
    bne @n
    lda ent_class,x
    ldy o_j
    cpy #1
    bne :+
    cmp #EC_PLATFORM
    beq @draw
    bra @n
:   cpy #3
    bne :+
    cmp #EC_SPECIAL
    beq @draw
    bra @n
:   cpy #4
    bne :+
    cmp #EC_EFFECT
    beq @draw
    bra @n
:   cmp #EC_EFFECT
    beq @n
    cmp #EC_PLATFORM
    beq @n
    cmp #EC_SPECIAL
    beq @n
    jsr is_carried
    beq @n
@draw:
    lda o_i
    pha
    lda o_j
    pha
    jsr draw_one
    pla
    sta o_j
    pla
    sta o_i
@n: inc o_i
    jmp @l
@d: rts

draw_one:
    jsl ent_draw_setxy
    ldy #VT_DRAW
    jmp cb_call

; wrappers: keep the collision loop counters intact when called from inside a callback chain
.macro KEEPCOLS target
    lda col_i
    pha
    lda col_j
    pha
    jsl target
    pla
    sta col_j
    pla
    sta col_i
    rtl
.endmacro
ent_bump_above:
    KEEPCOLS bump_above_x
ent_tail_hit:
    KEEPCOLS tail_hit_x
ent_statue_landed:
    KEEPCOLS statue_landed_x

; ent_hit: TakeHit on entity X through its vtable (hit callback or the default), args ent_dmg / ent_dir
;   -> carry = affected. Keeps X.
ent_hit:
    phx
    lda ent_dmg
    sta o_d
    lda ent_dir
    sta o_dir
    jsr take_hit
    plx
    rtl

; ------------------------------------------------------------------ Hammer Bro battle (C# World.Tick, kind "battle")
; after the timers: once no live enemy is left (and Frame > 60) a treasure chest drops at (CamX + 120, CamY + 16)
ent_battle_check:
    lda lvl_kind
    cmp #LK_BATTLE
    bne @r
    lda w_battlewon
    bne @r
    lda w_frame
    cmp #61
    bcc @r
    lda w_clearing
    bne @r
    ldy #0
@l: cpy ent_n
    beq @none
    phy
    tya
    asl a
    tay
    ldx ent_order,y
    ply
    lda ent_class,x
    bne @n
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    beq @r                      ; a live enemy: not won yet
@n: iny
    bra @l
@none:
    lda #1
    sta w_battlewon
    lda cam_x
    clc
    adc #120
    sta ent_new_x
    lda cam_y
    clc
    adc #16
    sta ent_new_y
    lda #ET_TREASURE_CHEST
    ldy #0
    jsl ent_spawn
    MUSIC "BOSSWIN"
@r: rtl
