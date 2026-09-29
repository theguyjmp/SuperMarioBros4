; Engine: objects (port of src/Game/Entities: Entity, Goomba, Koopa, Shell, Piranha, items, effects, goal) and the
; World object loop (Spawner, updates, adds, Collisions, Despawn). Owner: engine agent. A16/XY16, JSL/RTL.
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"
.include "spr_ids.inc"
.include "music.inc"

.import eng_tables
.global spr_meta, spr_player, spr_arg_x, spr_arg_y, spr_arg_flags, spr_arg_id
.global pl_hitbox, tt_x, tt_y, tt_w, tt_h, w_bump_above_l

; entity types
ET_GOOMBA = 1
ET_KOOPA = 2
ET_SHELL = 3
ET_PIRANHA = 4
ET_EFIRE = 5
ET_MUSHROOM = 6
ET_FLOWER = 7
ET_LEAF = 8
ET_STAR = 9
ET_COINPOP = 10
ET_BUMP = 11
ET_FIREBALL = 12
ET_POPUP = 13
ET_PUFF = 14
ET_SPARKLE = 15
ET_DEBRIS = 16
ET_DUST = 17
ET_GOAL = 18
ET_CARDFLY = 19
ET_VINE = 20
ET_PSWITCH = 21
ET_SPLASH = 22
ET_LIFT = 23
ET_DONUT = 24

; classes
EC_ENEMY = 0
EC_ITEM = 1
EC_PROJ = 2
EC_EPROJ = 3
EC_EFFECT = 4
EC_SPECIAL = 5
EC_PLATFORM = 6

; flags
F_REMOVE = $0001
F_KILLED = $0002
F_DYING = $0004
F_BEHIND = $0008
F_HURTS = $0010
F_STOMP = $0020             ; Stompable
F_GROUND = $0040            ; OnGround
F_FIREIMM = $0080
F_SLOT = $0100              ; UsesSlot
F_SHELL = $0200             ; IsShell (moving shell kills enemies)
F_STARIMM = $0400
F_SHELLIMM = $0800

; damage kinds
D_STOMP = 0
D_FIRE = 1
D_TAIL = 2
D_SHELL = 3
D_STAR = 4
D_BUMP = 6

N = MAX_ENTS

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
ent_class: .res 2*N
ent_facing: .res 2*N
ent_t: .res 2*N
ent_state: .res 2*N
ent_anim: .res 2*N
ent_spawn: .res 2*N
ent_knock: .res 2*N
ent_points: .res 2*N
ent_chain: .res 2*N
ent_v0: .res 2*N
ent_v1: .res 2*N
ent_v2: .res 2*N
ent_v3: .res 2*N
ent_order: .res 2*N         ; slot*2 in list order
ent_n: .res 2
ent_addq: .res 2*N
ent_nadd: .res 2
ent_cur: .res 2             ; current slot*2 being processed
ent_other: .res 2
spn_lastR: .res 2
spn_lastL: .res 2
spn_lastD: .res 2
spn_lastU: .res 2
o_i: .res 2
o_j: .res 2
o_tmp: .res 2
o_dir: .res 2
o_d: .res 2
kill_areas: .res 2          ; (unused)
bump_final: .res 2
pl_prevx: .res 2
pl_prevy: .res 2
pl_wason: .res 2
pl_dx: .res 2
pl_dy: .res 2
pl_feet: .res 2
ea_type: .res 2

.segment "EXBSS"
spawned: .res 512           ; per spawn index (byte)
killed: .res 8*512          ; per area, per spawn index (C# areaKilled)

.segment "CODE3"
.a16
.i16

tile_props3: .byte TILE_PROPS

.macro DRAW id
.ifdef id
    lda #id
    jsr draw_meta
.endif
.endmacro

.macro PROPS3
    tax
    lda f:tile_props3,x
    and #$00FF
.endmacro

; ------------------------------------------------------------------ small helpers (X = slot*2 preserved unless noted)
; Px/Py of entity X
epx:
    lda ent_x,x
    lsr a
    lsr a
    lsr a
    lsr a
    rts
epy:
    lda ent_y,x
    ASR4
    rts

; is tile at (A = px, Y = py) solid? -> Z clear if solid. Preserves X.
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

sign16:
    cmp #0
    beq @z
    bmi @m
    lda #1
    rts
@m: lda #$FFFF
@z: rts

; player center x
pcx:
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc #8
    rts
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

; allocate a slot: A = type, class in Y -> X = slot*2 (carry clear on failure). Resets all fields; added to
; the add queue (C# World.Add).
ent_add:
    sta ea_type
    ldx #0
@f: lda ent_type,x
    beq @got
    inx
    inx
    cpx #2*N
    bcc @f
    clc
    rtl
@got:
    lda ea_type
    sta ent_type,x
    tya
    sta ent_class,x
    stz ent_x,x
    stz ent_y,x
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
    lda #$FFFF
    sta ent_facing,x
    sta ent_spawn,x
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
    phx
    lda ent_nadd
    asl a
    tay
    txa
    sta ent_addq,y
    inc ent_nadd
    plx
    sec
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
    lda ent_spawn,x
    bmi :+
    jsr killed_index
    phx
    tax
    sep #$20
    lda #1
    sta f:killed,x
    rep #$20
    plx
:
    ; the carried object never gets removed while carried; if it does, drop the reference
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

; CountClass: A = class -> A = count (list + adds, not removed)
ent_count_class:
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
@d: lda o_d
    rtl

; MarkKilled(e = X)
w_mark_killed:
    lda ent_fl,x
    ora #F_KILLED
    sta ent_fl,x
    lda ent_spawn,x
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
; clear per-area spawn state (called on LoadArea); killed[] survives for the level (C# areaKilled)
.export ent_area_reset, ent_level_reset
ent_level_reset:
    ldx #0
    lda #0
@c: sta f:killed,x
    inx
    inx
    cpx #8*512
    bcc @c
    rtl

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
    lda #ET_GOAL
    ldy #EC_SPECIAL
    jsl ent_add
    bcc @nog
    lda area_goalx
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    lda area_goaly
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    lda #32
    sta ent_wd,x
    sta ent_ht,x
    lda #4
    sta ent_hbx,x
    sta ent_hby,x
    lda #24
    sta ent_hbw,x
    sta ent_hbh,x
    lda #0
    sta ent_fl,x
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
    stz o_i
@l: lda o_i
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
@y: lda o_i
    jsr try_spawn
@n: inc o_i
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

; SpawnColumn(A = col)
spawn_col:
    sta e_t5
    lda o_i
    pha
    lda #0
@l: sta o_i
    cmp area_nspawn
    bcs @d
    jsr spawn_xy
    lda e_t0
    cmp e_t5
    bne :+
    lda o_i
    jsr try_spawn
:   lda o_i
    inc a
    bra @l
@d: pla
    sta o_i
    rts

; SpawnRow(A = row)
spawn_row:
    sta e_t5
    lda o_i
    pha
    lda #0
@l: sta o_i
    cmp area_nspawn
    bcs @d
    jsr spawn_xy
    lda e_t1
    cmp e_t5
    bne :+
    lda o_i
    jsr try_spawn
:   lda o_i
    inc a
    bra @l
@d: pla
    sta o_i
    rts

; TrySpawn(A = spawn index)
try_spawn:
    sta e_t6
    tax
    lda f:spawned,x
    and #$00FF
    bne @r
    lda e_t6
    jsr killed_index
    tax
    lda f:killed,x
    and #$00FF
    bne @r
    lda e_t6
    jsr spawn_xy
    jsr create                  ; X = slot*2 (carry set) or carry clear = null
    bcs @made
    ldx e_t6
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
    phx
    lda #EC_ENEMY
    jsl ent_count_class
    plx
    cmp #6                      ; includes the one just created
    bcc @ok
    ; undo
    stz ent_type,x
    dec ent_nadd
    rts
@ok:
    lda e_t6
    sta ent_spawn,x
    phx
    ldx e_t6
    sep #$20
    lda #1
    sta f:spawned,x
    rep #$20
    plx
    rts

; EntityFactory.Create: e_t2 = code, e_t0 = x, e_t1 = y (tiles) -> carry set, X = slot*2
create:
    lda e_t0
    asl a
    asl a
    asl a
    asl a
    sta e_t3                    ; px
    lda e_t1
    asl a
    asl a
    asl a
    asl a
    sta e_t4                    ; py
    lda e_t2
    cmp #'g'
    bne :+
    lda #0
    jmp new_goomba
:   cmp #'p'
    bne :+
    lda #1
    jmp new_goomba
:   cmp #'k'
    bne :+
    lda #0
    ldy #0
    jmp new_koopa
:   cmp #'r'
    bne :+
    lda #1
    ldy #0
    jmp new_koopa
:   cmp #'j'
    bne :+
    lda #0
    ldy #1
    jmp new_koopa
:   cmp #'f'
    bne :+
    lda #1
    ldy #2
    jmp new_koopa
:   cmp #'n'
    bne :+
    lda #1
    ldy #3
    jmp new_koopa
:   cmp #'e'
    bne :+
    lda #0
    jmp new_piranha
:   cmp #'v'
    bne :+
    lda #1
    jmp new_piranha
:   cmp #'m'
    bne :+
    lda e_t4
    clc
    adc #16
    sta e_t4
    lda #0
    jmp new_mushroom
:   cmp #'-'
    bne :+
    jmp new_donut
:   cmp #'_'
    bne :+
    lda #0
    jmp new_lift
:   cmp #':'
    bne :+
    lda #1
    jmp new_lift
:   cmp #'P'
    bne :+
    jmp new_pswitch
:   clc
    rts

; set X = e_t3<<4, Y = e_t4<<4
set_pos:
    lda e_t3
    asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    lda e_t4
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    rts

; facing toward the player at creation: Facing = P.CenterX < px + 8 ? -1 : 1
face_player_at_create:
    lda e_t3
    clc
    adc #8
    sta o_tmp
    jsr pcx
    sec
    sbc o_tmp
    bmi @l
    lda #1
    sta ent_facing,x
    rts
@l: lda #$FFFF
    sta ent_facing,x
    rts

; ------------------------------------------------------------------ Goomba / Paragoomba
new_goomba:
    pha
    lda #ET_GOOMBA
    ldy #EC_ENEMY
    jsl ent_add
    pla
    bcs :+
    rts
:   sta ent_v0,x                ; winged
    jsr set_pos
    jsr face_player_at_create
    lda ent_facing,x
    asl a
    asl a
    asl a                       ; *8
    sta ent_xvel,x
    lda #2
    sta ent_hbx,x
    lda #4
    sta ent_hby,x
    lda #12
    sta ent_hbw,x
    sta ent_hbh,x
    sec
    rts

goomba_update:
    jsr update_knocked
    bcc :+
    rts
:   lda ent_v1,x                ; flat
    beq @live
    lda ent_v2,x
    inc a
    sta ent_v2,x
    cmp #31
    bcc :+
    jsr set_remove
:   rts
@live:
    inc ent_anim,x
    lda ent_v0,x
    beq @nowing
    lda ent_fl,x
    and #F_GROUND
    beq @steer
    ; ++hopT > (hops < 3 ? 8 : 40)
    lda ent_v3,x
    inc a
    sta ent_v3,x
    sta o_tmp
    lda #8
    ldy ent_state,x
    cpy #3
    bcc :+
    lda #40
:   cmp o_tmp
    bcs @steer
    stz ent_v3,x
    lda ent_state,x
    inc a
    and #3
    sta ent_state,x
    cmp #3
    bne :+
    lda #$10000-$30
    bra :++
:   lda #$10000-$20
:   sta ent_yvel,x
    jsr face_toward
    sta ent_facing,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
@steer:
    ; target = FaceToward(P) * $14 ; XVel moves 1 toward it
    jsr face_toward
    bmi :+
    lda #$14
    bra :++
:   lda #$10000-$14
:   sta o_tmp
    lda ent_xvel,x
    sec
    sbc o_tmp
    beq @nowing
    bpl :+
    inc ent_xvel,x
    bra @nowing
:   dec ent_xvel,x
@nowing:
    lda #0
    ldy #1
    jsr move_walker
    lda ent_xvel,x
    beq :+
    jsr sign16
    sta ent_facing,x
:   rts

; FaceToward(p) -> A = -1/1 (p.CenterX < Cx ? -1 : 1)
face_toward:
    jsr cx
    sta o_tmp
    jsr pcx
    sec
    sbc o_tmp
    bmi @l
    lda #1
    rts
@l: lda #$FFFF
    rts

; Cx = Px + Wd/2
cx:
    lda ent_wd,x
    lsr a
    sta o_d
    jsr epx
    clc
    adc o_d
    rts

goomba_touch:
    lda ent_v1,x
    bne @r
    jsr can_stomp
    bcc @hurt
    jsr stomp_bounce
    lda ent_v0,x
    beq @flat
    stz ent_v0,x
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
@r: rts
@flat:
    lda #1
    sta ent_v1,x
    lda ent_fl,x
    and #$FFFF^F_HURTS
    sta ent_fl,x
    jsl w_mark_killed
    rts
@hurt:
    jsl pl_hurt
    rts

goomba_hit:
    lda ent_v1,x
    beq :+
    clc
    rts
:   lda o_dir
    jsr knock_off
    sec
    rts

; ------------------------------------------------------------------ Koopa (A = red, Y = wingMode)
new_koopa:
    pha
    phy
    lda #ET_KOOPA
    ldy #EC_ENEMY
    jsl ent_add
    ply
    pla
    bcs :+
    rts
:   sta ent_v0,x
    tya
    sta ent_v1,x
    lda e_t4
    sec
    sbc #8
    sta e_t4
    jsr set_pos
    lda #24
    sta ent_ht,x
    lda #2
    sta ent_hbx,x
    lda #10
    sta ent_hby,x
    lda #12
    sta ent_hbw,x
    lda #14
    sta ent_hbh,x
    lda e_t3                    ; facing uses the original px
    jsr face_player_at_create
    stz ent_xvel,x
    lda ent_v1,x
    cmp #2
    beq :+
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
:   lda ent_x,x
    sta ent_v2,x                ; ox
    lda ent_y,x
    sta ent_v3,x                ; oy
    sec
    rts

koopa_update:
    jsr update_knocked
    bcc :+
    rts
:   inc ent_anim,x
    inc ent_t,x
    lda ent_v1,x
    cmp #2
    bne @n2
    ; red paratroopa: Y = oy + sin(t*2pi/180)*40*16
    lda ent_t,x
    ldy #180
    jsr mod_a
    asl a
    phx
    tax
    lda f:eng_tables,x
    plx
    clc
    adc ent_v3,x
    sta ent_y,x
    jsr face_toward
    sta ent_facing,x
    rts
@n2: cmp #3
    bne @n3
    lda ent_t,x
    ldy #240
    jsr mod_a
    asl a
    phx
    tax
    lda f:eng_tables+840,x
    sta o_tmp
    lda f:eng_tables+360,x
    plx
    clc
    adc ent_v2,x
    sta ent_x,x
    lda o_tmp
    sta ent_facing,x
    rts
@n3: cmp #1
    bne @walk
    lda ent_fl,x
    and #F_GROUND
    beq @walk
    lda #$10000-$30
    sta ent_yvel,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
@walk:
    ; MoveWalker(Red && wingMode == 0)
    lda #0
    ldy ent_v0,x
    beq :+
    ldy ent_v1,x
    bne :+
    lda #1
:   ldy #1
    jsr move_walker
    lda ent_xvel,x
    beq :+
    jsr sign16
    sta ent_facing,x
:   rts

; A mod Y (A unsigned) -> A
mod_a:
    sty o_d
@l: cmp o_d
    bcc @r
    sec
    sbc o_d
    bra @l
@r: rts

koopa_touch:
    jsr can_stomp
    bcs :+
    jsl pl_hurt
    rts
:   jsr stomp_bounce
    lda ent_v1,x
    beq @shell
    stz ent_v1,x
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
    stz ent_yvel,x
    rts
@shell:
    ; new Shell(Px, Py + 8, kind, false), inherits the spawn index
    lda #0
    jsr koopa_to_shell
    jsr set_remove
    rts

; A = flipped -> creates the shell for koopa X (returns Y = shell slot*2). Keeps X.
koopa_to_shell:
    sta o_tmp
    phx
    jsr epx
    sta e_t3
    jsr epy
    clc
    adc #8
    sta e_t4
    lda ent_v0,x
    pha
    lda ent_spawn,x
    pha
    lda #$FFFF
    sta ent_spawn,x
    lda o_tmp
    pha
    lda #ET_SHELL
    ldy #EC_ENEMY
    jsl ent_add
    pla
    sta o_tmp
    pla
    sta o_d                     ; spawn index
    pla                         ; red
    bcc @fail
    sta ent_v0,x
    lda o_tmp
    sta ent_v1,x
    jsr init_shell
    lda o_d
    sta ent_spawn,x
    txy
    plx
    rts
@fail:
    ldy #$FFFF
    plx
    rts

init_shell:
    jsr set_pos
    lda #2
    sta ent_hbx,x
    lda #3
    sta ent_hby,x
    lda #12
    sta ent_hbw,x
    lda #13
    sta ent_hbh,x
    lda #(F_STOMP|F_SLOT)
    sta ent_fl,x                ; Hurts = false
    lda #$0100
    sta ent_points,x
    rts

koopa_hit:
    lda o_d                     ; damage kind
    cmp #D_TAIL
    beq @shell
    cmp #D_BUMP
    beq @shell
    lda o_dir
    jsr knock_off
    sec
    rts
@shell:
    lda #1
    jsr koopa_to_shell
    cpy #$FFFF
    beq :+
    lda #$10000-$30
    sta ent_yvel,y
    lda o_dir
    asl a
    asl a
    asl a
    sta ent_xvel,y
:   jsr set_remove
    jsr epx
    sta e_t3
    jsr epy
    tay
    phx
    ldx e_t3
    lda #$0100
    jsl w_add_score_at
    plx
    sec
    rts

; ------------------------------------------------------------------ Shell
; v0 = red, v1 = flipped, v2 = wakeT, v3 = kickGrace
shell_moving:
    ; Moving = XVel != 0 && P.Carrying != this
    lda ent_xvel,x
    beq @no
    txa
    inc a
    inc a
    cmp p_carrying
    beq @no
    lda #1
    rts
@no: lda #0
    rts

; Kick(dir = A, playerXVel = Y)
shell_kick:
    sta o_dir
    sty o_tmp
    asl a
    clc
    adc o_dir                   ; *3
    asl a
    asl a
    asl a
    asl a                       ; *$30
    sta o_d
    lda o_tmp
    jsr sign16
    cmp o_dir
    bne :+
    lda o_tmp
    ASR16                       ; playerXVel / 2 (C# int division truncates toward zero)
    bpl :++
    lda o_tmp
    NEG16
    lsr a
    NEG16
    bra :++
:   lda #0
:   clc
    adc o_d
    sta ent_xvel,x
    lda ent_fl,x
    ora #(F_SHELL|F_HURTS)
    sta ent_fl,x
    stz ent_chain,x
    lda #16
    sta ent_v3,x
    stz ent_v2,x
    rts

shell_update:
    jsr update_knocked
    bcc :+
    rts
:   inc ent_anim,x
    lda ent_v3,x
    beq :+
    dec ent_v3,x
:   txa
    inc a
    inc a
    cmp p_carrying
    bne @free
    ; carried: X = (p.Px + (facing > 0 ? 11 : -11)) << 4 ; Y = (p.Py + (Big && !Ducking ? 13 : 17)) << 4
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #11
    bra :++
:   sec
    sbc #11
:   asl a
    asl a
    asl a
    asl a
    sta ent_x,x
    lda #17
    ldy p_form
    beq :+
    ldy p_ducking
    bne :+
    lda #13
:   sta o_tmp
    jsr ppy
    clc
    adc o_tmp
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    stz ent_xvel,x
    stz ent_yvel,x
    lda ent_v2,x
    inc a
    sta ent_v2,x
    cmp #421
    bcc :+
    lda #1
    jsr shell_wake
:   rts
@free:
    lda ent_xvel,x
    bne @mv
    lda ent_fl,x
    and #$FFFF^(F_SHELL|F_HURTS)
    sta ent_fl,x
    lda ent_v2,x
    inc a
    sta ent_v2,x
    cmp #421
    bcc @mv
    lda #0
    jmp shell_wake
@mv:
    lda #0
    ldy #1
    jsr move_walker             ; carry = hit wall
    jcc @r
    lda ent_xvel,x
    jeq @r
    SFX "RICOCHET"
    ; the wall we hit is on the other side (XVel already reversed)
    lda ent_xvel,x
    bmi :+
    jsr epx
    sec
    sbc #3
    bra :++
:   jsr epx
    clc
    adc ent_wd,x
    inc a
    inc a
:   ASR4
    sta e_tx
    jsr epy
    clc
    adc #8
    ASR4
    sta e_ty
    ; only near the screen
    jsr epx
    clc
    adc #8
    sec
    sbc cam_x
    sec
    sbc #$10000-16
    bmi @r
    beq @r
    jsr epx
    sec
    sbc cam_x
    sec
    sbc #272
    bpl @r
    phx
    jsl eng_tile_at
    PROPS3
    and #TP_BUMP
    beq :+
    lda #1
    ldx #0
    jsl w_hit_block
:   plx
@r: rts

; WakeUp(A = carried)
shell_wake:
    sta o_tmp
    phx
    jsr epx
    sta e_t3
    jsr epy
    sta e_t4
    lda o_tmp
    pha
    lda ent_spawn,x
    pha
    lda ent_v0,x
    ldy #0
    jsr new_koopa_px            ; new Koopa(Px, Py, red, 0) (Py is the shell's; Koopa subtracts 8)
    pla
    bcc :+
    sta ent_spawn,x
:   pla
    sta o_tmp
    plx
    lda #$FFFF
    sta ent_spawn,x
    jsr set_remove
    lda o_tmp
    beq :+
    stz p_carrying
    jsl pl_hurt
:   rts

; new koopa at exact pixel position (e_t3, e_t4) (A = red, Y = wingMode)
new_koopa_px:
    jmp new_koopa

shell_touch:
    txa
    inc a
    inc a
    cmp p_carrying
    bne :+
    rts
:   jsr shell_moving
    bne @moving
    ; pick up with B held
    lda pad_held
    and #BTN_B
    beq @kick
    lda p_carrying
    ora p_statue
    ora p_swimming
    bne @kick
    txa
    inc a
    inc a
    sta p_carrying
    lda ent_v2,x
    cmp #300
    bcc :+
    lda #300
    sta ent_v2,x
:   rts
@kick:
    jsr can_stomp
    lda #0
    rol a
    sta o_i                     ; fromAbove
    ; dir = P.CenterX < Cx ? 1 : -1
    jsr face_toward
    NEG16
    ldy #0
    jsr shell_kick
    lda #$0C
    sta p_kickpose
    lda o_i
    beq :+
    lda #$10000-$40
    jsl pl_bounce
:   SFX "KICK"
    phx
    jsr epy
    tay
    jsr epx
    tax
    lda #$0100
    jsl w_add_score_at
    plx
    rts
@moving:
    lda ent_v3,x
    beq :+
    rts
:   jsr can_stomp
    bcc @hurt
    jsr stomp_bounce
    stz ent_xvel,x
    lda ent_fl,x
    and #$FFFF^(F_SHELL|F_HURTS)
    sta ent_fl,x
    stz ent_v2,x
    rts
@hurt:
    jsl pl_hurt
    rts

shell_hit:
    lda o_d
    cmp #D_TAIL
    beq @flip
    cmp #D_BUMP
    beq @flip
    lda o_dir
    jsr knock_off
    sec
    rts
@flip:
    lda ent_v1,x
    eor #1
    sta ent_v1,x
    lda #$10000-$30
    sta ent_yvel,x
    stz ent_xvel,x
    lda ent_fl,x
    and #$FFFF^F_SHELL
    sta ent_fl,x
    stz ent_v2,x
    sec
    rts

; ------------------------------------------------------------------ Piranha / Venus (A = venus)
; v0 venus, v1 homeY (px), v2 height ; state = phase ; t
new_piranha:
    pha
    lda #ET_PIRANHA
    ldy #EC_ENEMY
    jsl ent_add
    pla
    bcs :+
    rts
:   sta ent_v0,x
    lda e_t4
    clc
    adc #16                     ; pipe top = spawn py + 16
    sta ent_v1,x
    sta e_t4
    lda e_t3
    clc
    adc #8
    sta e_t3
    jsr set_pos
    lda #24
    ldy ent_v0,x
    beq :+
    lda #32
:   sta ent_v2,x
    sta ent_ht,x
    sec
    sbc #4
    sta ent_hbh,x
    lda #3
    sta ent_hbx,x
    lda #2
    sta ent_hby,x
    lda #10
    sta ent_hbw,x
    lda #(F_HURTS|F_BEHIND|F_SLOT)
    sta ent_fl,x
    sec
    rts

piranha_update:
    jsr update_knocked
    bcc :+
    rts
:   inc ent_t,x
    inc ent_anim,x
    lda ent_state,x
    bne @p1
    ; wait: t > 60 && |P.CenterX - Cx| > 24
    lda ent_t,x
    cmp #61
    jcc @hurts
    jsr cx
    sta o_tmp
    jsr pcx
    sec
    sbc o_tmp
    bpl :+
    NEG16
:   cmp #25
    jcc @hurts
    lda #1
    sta ent_state,x
    stz ent_t,x
    brl @hurts
@p1: cmp #1
    bne @p2
    lda ent_y,x
    sec
    sbc #16
    sta ent_y,x
    lda ent_v1,x
    sec
    sbc ent_v2,x
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    beq :+
    bpl @hurts
:   lda o_tmp
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    lda #2
    sta ent_state,x
    stz ent_t,x
    bra @hurts
@p2: cmp #2
    bne @p3
    lda ent_v0,x
    beq :+
    lda ent_t,x
    cmp #30
    bne :+
    jsr venus_shoot
:   lda ent_t,x
    cmp #61
    bcc @hurts
    lda #3
    sta ent_state,x
    stz ent_t,x
    bra @hurts
@p3: lda ent_y,x
    clc
    adc #16
    sta ent_y,x
    jsr epy
    sec
    sbc ent_v1,x
    bmi @hurts
    lda ent_v1,x
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    stz ent_state,x
    stz ent_t,x
@hurts:
    ; Hurts = Py < homeY - 4
    lda ent_fl,x
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda ent_v1,x
    sec
    sbc #4
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    bpl :+
    lda ent_fl,x
    ora #F_HURTS
    sta ent_fl,x
:   rts

venus_shoot:
    phx
    ; dx = P.CenterX - Cx ; dy = P.Py + 16 - (Py + 8)
    jsr cx
    sta e_t3
    jsr pcx
    sec
    sbc e_t3
    sta e_t0                    ; dx
    jsr epy
    clc
    adc #8
    sta o_tmp
    jsr ppy
    clc
    adc #16
    sec
    sbc o_tmp
    sta e_t1                    ; dy
    lda #$10
    ldy e_t0
    bpl :+
    lda #$10000-$10
:   sta e_t5                    ; vx
    lda e_t1
    bpl :+
    NEG16
:   cmp #24
    bcs :+
    lda #0
    bra @vy
:   lda #$0C
    ldy e_t1
    bpl @vy
    lda #$10000-$0C
@vy: sta e_t6
    jsr epy
    clc
    adc #6
    sta e_t4
    lda e_t3
    sec
    sbc #4
    sta e_t3
    lda #ET_EFIRE
    ldy #EC_EPROJ
    jsl ent_add
    bcc @r
    jsr set_pos
    lda e_t5
    sta ent_xvel,x
    lda e_t6
    sta ent_yvel,x
    lda #8
    sta ent_wd,x
    sta ent_ht,x
    lda #1
    sta ent_hbx,x
    sta ent_hby,x
    lda #6
    sta ent_hbw,x
    sta ent_hbh,x
    lda #(F_HURTS|F_STARIMM)
    sta ent_fl,x
@r: plx
    rts

piranha_hit:
    lda ent_v1,x
    dec a
    dec a
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    bmi :+
    clc
    rts
:   lda ent_fl,x
    ora #(F_REMOVE|F_KILLED)
    sta ent_fl,x
    jsr epx
    sta e_t3
    jsr epy
    sta e_t4
    jsr add_puff
    phx
    jsr epy
    tay
    jsr epx
    tax
    lda #$0100
    jsl w_add_score_at
    plx
    sec
    rts

efire_update:
    inc ent_t,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_t,x
    cmp #401
    bcc :+
    jsr set_remove
:   rts

; ------------------------------------------------------------------ items
; Rising: rise = 16 (v1), Behind until out
rising_emerging:
    lda ent_v1,x
    bne :+
    lda ent_fl,x
    and #$FFFF^F_BEHIND
    sta ent_fl,x
    clc
    rts
:   lda w_frame
    and #1
    bne :+
    lda ent_y,x
    sec
    sbc #16
    sta ent_y,x
    dec ent_v1,x
:   sec
    rts

; A = oneUp, position e_t3/e_t4
new_mushroom:
    pha
    lda #ET_MUSHROOM
    ldy #EC_ITEM
    jsl ent_add
    pla
    bcs :+
    rts
:   sta ent_v0,x
    jsr init_rising
    lda #1
    sta ent_facing,x
    sec
    rts

init_rising:
    jsr set_pos
    lda #16
    sta ent_v1,x
    lda #(F_BEHIND|F_STOMP)
    sta ent_fl,x
    rts

mushroom_update:
    jsr rising_emerging
    bcc :+
    rts
:   lda ent_xvel,x
    bne :+
    lda ent_facing,x
    asl a
    asl a
    asl a
    asl a
    sta ent_xvel,x
:   lda #0
    ldy #1
    jsr move_walker
    lda ent_xvel,x
    beq :+
    jsr sign16
    sta ent_facing,x
:   rts

mushroom_touch:
    lda ent_v1,x
    cmp #9
    bcc :+
    rts
:   jsr set_remove
    lda ent_v0,x
    beq @big
    phx
    jsr epy
    tay
    jsr epx
    tax
    jsl w_one_up_at
    plx
    rts
@big: phx
    lda #PF_BIG
    jsl pl_powerup
    plx
    rts

mushroom_bump:
    lda #$10000-$30
    sta ent_yvel,x
    lda o_dir
    sta ent_facing,x
    asl a
    asl a
    asl a
    asl a
    sta ent_xvel,x
    rts

new_flower:
    lda #ET_FLOWER
    ldy #EC_ITEM
    jsl ent_add
    bcs :+
    rts
:   jsr init_rising
    sec
    rts
flower_update:
    jsr rising_emerging
    rts
flower_touch:
    lda ent_v1,x
    cmp #9
    bcc :+
    rts
:   jsr set_remove
    phx
    lda #PF_FIRE
    jsl pl_powerup
    plx
    rts

new_star:
    lda #ET_STAR
    ldy #EC_ITEM
    jsl ent_add
    bcs :+
    rts
:   jsr init_rising
    lda #1
    sta ent_facing,x
    sec
    rts
star_update:
    jsr rising_emerging
    bcc :+
    rts
:   lda ent_facing,x
    bmi :+
    lda #$18
    bra :++
:   lda #$10000-$18
:   sta ent_xvel,x
    clc
    adc ent_x,x
    sta ent_x,x
    ; turn at walls
    jsr epy
    clc
    adc #8
    tay
    lda ent_xvel,x
    bmi :+
    jsr epx
    clc
    adc #14
    bra :++
:   jsr epx
    inc a
:   jsr e_solid
    beq :+
    lda ent_facing,x
    NEG16
    sta ent_facing,x
:   lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    clc
    adc ent_y,x
    sta ent_y,x
    lda ent_yvel,x
    bmi @up
    beq @up
    jsr epy
    clc
    adc #16
    sta o_tmp
    tay
    jsr epx
    clc
    adc #4
    jsr e_floor
    bne @bounce
    ldy o_tmp
    jsr epx
    clc
    adc #11
    jsr e_floor
    beq @up
@bounce:
    lda o_tmp
    and #$FFF0
    sec
    sbc #16
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    lda #$10000-$48
    sta ent_yvel,x
@up:
    lda ent_yvel,x
    bpl @fall
    jsr epy
    tay
    jsr epx
    clc
    adc #8
    jsr e_solid
    beq @fall
    stz ent_yvel,x
@fall:
    lda area_h
    asl a
    asl a
    asl a
    asl a
    clc
    adc #16
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    bmi :+
    beq :+
    jsr set_remove
:   rts
star_touch:
    lda ent_v1,x
    cmp #9
    bcc :+
    rts
:   jsr set_remove
    phx
    jsl pl_get_star
    plx
    rts
star_bump:
    lda #$10000-$40
    sta ent_yvel,x
    lda o_dir
    sta ent_facing,x
    rts

; Leaf: v0 = swayDir
new_leaf:
    lda #ET_LEAF
    ldy #EC_ITEM
    jsl ent_add
    bcs :+
    rts
:   lda e_t4
    sec
    sbc #8
    sta e_t4
    jsr set_pos
    lda #0
    sta ent_fl,x
    lda #$10000-$40
    sta ent_yvel,x
    lda #1
    sta ent_v0,x
    sec
    rts
leaf_update:
    inc ent_t,x
    lda ent_yvel,x
    bpl @sway
    clc
    adc ent_y,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #3
    sta ent_yvel,x
    rts
@sway:
    lda ent_t,x
    and #63
    sta o_tmp
    bne :+
    lda ent_v0,x
    NEG16
    sta ent_v0,x
:   lda #$14
    ldy o_tmp
    cpy #32
    bcc :+
    lda #$08
:   ldy ent_v0,x
    bpl :+
    NEG16
:   sta ent_xvel,x
    clc
    adc ent_x,x
    sta ent_x,x
    lda #6
    ldy o_tmp
    cpy #32
    bcc :+
    lda #10
:   clc
    adc ent_y,x
    sta ent_y,x
    lda ent_v0,x
    sta ent_facing,x
    jsr epy
    sec
    sbc cam_y
    sec
    sbc #241
    bmi :+
    jsr set_remove
:   rts
leaf_touch:
    jsr set_remove
    phx
    lda #PF_RACCOON
    jsl pl_powerup
    plx
    rts

; P-switch: v0 pressed
new_pswitch:
    lda #ET_PSWITCH
    ldy #EC_ITEM
    jsl ent_add
    bcs :+
    rts
:   jsr set_pos
    lda #4
    sta ent_hby,x
    lda #12
    sta ent_hbh,x
    lda #0
    sta ent_fl,x
    sec
    rts
pswitch_update:
    lda ent_v0,x
    beq :+
    inc ent_t,x
    lda ent_t,x
    cmp #91
    bcc :+
    jsr set_remove
:   rts
pswitch_touch:
    lda ent_v0,x
    beq :+
    rts
:   ; pressed from above while falling
    lda p_yvel
    bmi @side
    lda p_inair
    beq @side
    jsr epy
    clc
    adc #10
    sta o_tmp
    jsr ppy
    clc
    adc #32
    sec
    sbc o_tmp
    beq :+
    bpl @side
:   lda #1
    sta ent_v0,x
    lda #$10000-$20
    jsl pl_bounce
    phx
    jsl w_start_pswitch
    plx
    rts
@side:
    jsr cx
    sta o_tmp
    jsr pcx
    sec
    sbc o_tmp
    bpl @right
    lda p_xvel
    bmi :+
    beq :+
    stz p_xvel
:   rts
@right:
    lda p_xvel
    bpl :+
    stz p_xvel
:   rts

; ent_release: A = content, X = big, e_tx/e_ty = block (World.Release for item contents)
ent_release:
    sta o_tmp
    stx o_d
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta e_t3
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta e_t4
    lda o_tmp
    cmp #CT_FLOWER
    bne @nf
    lda o_d
    beq @mush
    jsr new_flower
    bra @sprout
@nf: cmp #CT_LEAF
    bne @nl
    lda o_d
    beq @mush
    jsr new_leaf
    bra @sprout
@nl: cmp #CT_STAR
    bne @ns
    jsr new_star
    bra @sprout
@ns: cmp #CT_ONEUP
    bne @n1
    lda #1
    jsr new_mushroom
    bra @sprout
@n1: cmp #CT_VINE
    bne @nv
    jsr new_vine
    SFX "VINE"
    rtl
@nv: cmp #CT_PSWITCH
    bne @np
    lda e_t4
    sec
    sbc #16
    sta e_t4
    jsr new_pswitch
    bra @sprout
@np: SFX "BUMP"
    rtl
@mush:
    lda #0
    jsr new_mushroom
@sprout:
    SFX "SPROUT"
    rtl

; VineSprout(tx, ty): v0 tx, v1 ty (row being grown)
new_vine:
    lda #ET_VINE
    ldy #EC_SPECIAL
    jsl ent_add
    bcs :+
    rts
:   lda e_t4
    sec
    sbc #16
    sta e_t4
    jsr set_pos
    lda e_tx
    sta ent_v0,x
    lda e_ty
    dec a
    sta ent_v1,x
    lda #0
    sta ent_fl,x
    rts
vine_update:
    inc ent_t,x
    lda ent_t,x
    and #7
    beq :+
    rts
:   lda ent_v0,x
    sta e_tx
    lda ent_v1,x
    sta e_ty
    bmi @rem
    phx
    jsl eng_tile_at
    plx
    cmp #T_EMPTY
    beq :+
    cmp #T_VINE
    bne @rem
:   phx
    lda #T_VINE
    jsl eng_set_tile
    plx
    dec ent_v1,x
    lda ent_v1,x
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    lda ent_v1,x
    cmp #$FFFF
    bpl :+
@rem: jsr set_remove
:   rts

; ------------------------------------------------------------------ effects
; A = type, e_t3/e_t4 = px/py -> X = slot (carry)
new_fx:
    ldy #EC_EFFECT
    jsl ent_add
    bcc :+
    jsr set_pos
    lda #0
    sta ent_fl,x
    sec
:   rts

add_puff:
    phx
    lda #ET_PUFF
    jsr new_fx
    plx
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
    lda #ET_DUST
    jsr new_fx
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
    lda #ET_SPLASH
    jsr new_fx
    rtl

ent_add_fx_puff_player:
    jsr ppx
    sta e_t3
    jsr ppy
    clc
    adc #8
    sta e_t4
    lda #ET_PUFF
    jsr new_fx
    rtl

; X = px, Y = py
ent_add_sparkle:
    stx e_t3
    sty e_t4
    lda #ET_SPARKLE
    jsr new_fx
    rtl

ent_add_coinpop:
    stx e_t3
    sty e_t4
    lda #ET_COINPOP
    jsr new_fx
    bcc :+
    lda #$10000-$50
    sta ent_yvel,x
:   rtl

; ScorePopup: A = BCD points ($FFFF = 1UP), X = px, Y = py
ent_add_popup:
    stx e_t3
    sty e_t4
    pha
    lda #ET_POPUP
    jsr new_fx
    pla
    bcc :+
    sta ent_v0,x
:   rtl

; BumpBlock(e_tx, e_ty, dir = A, final = X)
ent_add_bump:
    sta o_dir
    stx bump_final
    lda #ET_BUMP
    ldy #EC_SPECIAL
    jsl ent_add
    bcc @r
    lda e_tx
    sta ent_v0,x
    lda e_ty
    sta ent_v1,x
    lda o_dir
    sta ent_v2,x
    lda bump_final
    sta ent_v3,x
    lda #0
    sta ent_fl,x
    phx
    lda bump_final
    jsl eng_set_tile
    jsl eng_hide_tile
    plx
@r: rtl

ent_add_debris4:
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta e_t5
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta e_t6
    lda #0
    ldy #0
    ldx #$10000-$10
    lda #$10000-$40
    jsr debris
    lda #8
    ldy #0
    ldx #$10
    lda #$10000-$40
    ; (px + 8, py)
    pha
    lda e_t5
    clc
    adc #8
    sta e_t3
    lda e_t6
    sta e_t4
    pla
    jsr debris2
    lda e_t5
    sta e_t3
    lda e_t6
    clc
    adc #8
    sta e_t4
    ldx #$10000-$10
    lda #$10000-$28
    jsr debris2
    lda e_t5
    clc
    adc #8
    sta e_t3
    lda e_t6
    clc
    adc #8
    sta e_t4
    ldx #$10
    lda #$10000-$28
    jsr debris2
    rtl
debris:
    pha
    lda e_t5
    sta e_t3
    lda e_t6
    sta e_t4
    pla
debris2:
    sta o_i
    stx o_j
    lda #ET_DEBRIS
    jsr new_fx
    bcc :+
    lda o_i
    sta ent_yvel,x
    lda o_j
    sta ent_xvel,x
:   rts

; ------------------------------------------------------------------ player projectiles
ent_throw_fireball:
    lda #2
    jsl ent_count_class         ; EC_PROJ
    cmp #2
    bcc :+
    rtl
:   ; Fireball(Px + (Facing > 0 ? 8 : 0), Py + 14, Facing)
    jsr ppx
    ldy p_facing
    bmi :+
    clc
    adc #8
:   sta e_t3
    jsr ppy
    clc
    adc #14
    sta e_t4
    lda #ET_FIREBALL
    ldy #EC_PROJ
    jsl ent_add
    bcc @r
    jsr set_pos
    lda #8
    sta ent_wd,x
    sta ent_ht,x
    sta ent_hbw,x
    sta ent_hbh,x
    stz ent_hbx,x
    stz ent_hby,x
    lda #0
    sta ent_fl,x
    lda p_facing
    sta ent_facing,x
    bmi :+
    lda #$30
    bra :++
:   lda #$10000-$30
:   sta ent_xvel,x
    lda #$30
    sta ent_yvel,x
    lda #$0B
    sta p_throwpose
    SFX "FIREBALL"
@r: rtl

fireball_update:
    inc ent_t,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    jsr epx
    ldy ent_xvel,x
    bmi :+
    beq :+
    clc
    adc #7
:   pha
    jsr epy
    clc
    adc #4
    tay
    pla
    jsr e_solid
    beq :+
    jmp fireball_poof
:   lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_t,x
    and #3
    bne :+
    lda ent_yvel,x
    cmp #$40
    bpl :+
    clc
    adc #16
    sta ent_yvel,x
:   lda ent_yvel,x
    bmi @nob
    beq @nob
    jsr epy
    clc
    adc #8
    sta o_tmp
    tay
    jsr epx
    clc
    adc #4
    jsr e_floor
    beq @nob
    lda o_tmp
    and #15
    cmp #6
    bcs @nob
    lda o_tmp
    and #$FFF0
    sec
    sbc #8
    asl a
    asl a
    asl a
    asl a
    sta ent_y,x
    lda #$10000-$30
    sta ent_yvel,x
@nob:
    jsr epy
    clc
    adc #4
    tay
    jsr epx
    clc
    adc #4
    phx
    jsl eng_tile_at_px
    PROPS3
    plx
    and #TP_LAVA
    beq :+
    jmp fireball_poof
:   rts

fireball_poof:
    jsr set_remove
    jsr epx
    sec
    sbc #4
    sta e_t3
    jsr epy
    sec
    sbc #4
    sta e_t4
    phx
    lda #ET_PUFF
    jsr new_fx
    bcc :+
    lda #1
    sta ent_v0,x                ; small puff (sparkle)
:   plx
    SFX "FIREBALLHIT"
    rts

; HitEnemy(e = ent_other): returns (always) true
fireball_hit_enemy:
    ldy ent_other
    lda ent_fl,y
    and #F_FIREIMM
    bne @poof
    phx
    tyx
    lda #D_FIRE
    sta o_d
    ldy ent_cur
    lda ent_facing,y
    sta o_dir
    jsr take_hit
    bcc :+
    jsl w_mark_killed
    SFX "KICK"
:   plx
@poof:
    jmp fireball_poof

; ================================================================== shared entity physics
; UpdateKnocked -> carry set if knocked (handled)
update_knocked:
    lda ent_fl,x
    and #F_DYING
    beq @no
    lda ent_knock,x
    beq @no
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
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
    jsr epy
    sec
    sbc o_tmp
    bmi :+
    beq :+
    jsr set_remove
:   sec
    rts
@no: clc
    rts

; KnockOff(dir = A)
knock_off:
    sta o_dir
    lda ent_fl,x
    and #F_DYING
    beq :+
    rts
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
    phx
    jsr epy
    tay
    jsr epx
    pha
    lda ent_points,x
    plx
    jsl w_add_score_at
    plx
    rts

set_remove:
    lda ent_fl,x
    ora #F_REMOVE
    sta ent_fl,x
    rts

; Gravity: +3 (max $40), in water +1 (max $10)
gravity:
    lda area_water
    bmi @dry
    lda ent_ht,x
    lsr a
    sta o_tmp
    jsr epy
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
    rts
@dry:
    lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    rts

; MoveWalker(turnAtLedges = A, turnAtWalls = Y) -> carry set if it hit a wall
move_walker:
    sta o_i
    sty o_j
    stz o_d                     ; hitWall
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    ; midY = py + Ht - 8
    jsr epy
    clc
    adc ent_ht,x
    sec
    sbc #8
    sta e_t5
    lda ent_xvel,x
    beq @nowall
    bmi @wl
    ; right: SolidAt(px + Wd - 2, midY)
    jsr epx
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
    inc o_d
    bra @nowall
@wl: jsr epx
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
    inc o_d
@nowall:
    lda o_d
    beq :+
    lda o_j
    beq :+
    lda ent_xvel,x
    NEG16
    sta ent_xvel,x
    beq :+
    jsr sign16
    sta ent_facing,x
:   jsr gravity
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    jsr epy
    clc
    adc ent_ht,x
    sta e_t5                    ; feet
    ; slopes: walkers follow the surface under their center
    lda ent_yvel,x
    jmi @noslope
    lda area_slopes
    jeq @noslope
    jsr cx
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
    lda e_t2
    ldy ent_fl,x
    pha
    tya
    and #F_GROUND
    beq :+
    pla
    sec
    sbc #8
    bra :++
:   pla
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
    jsr fell_out
    lda o_d
    cmp #1
    rts
@snext:
    inc e_t7
    jmp @sk
@noslope:
    lda ent_yvel,x
    jmi @rising
    ; f = FloorAt(px + 3, feet) || FloorAt(px + Wd - 4, feet)
    jsr epx
    clc
    adc #3
    ldy e_t5
    jsr e_floor
    bne @fl
    jsr epx
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
    jsr cx
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
    jsr epy
    inc a
    inc a
    sta o_tmp
    tay
    jsr cx
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
    jsr epy
    clc
    adc ent_ht,x
    inc a
    inc a
    tay
    lda ent_xvel,x
    beq :+
    bpl :++
:   jsr epx
    dec a
    bra :++
:   jsr epx
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
    jsr fell_out
    lda o_d
    cmp #1
    rts

; Py > LevelPxH + 32 -> removed + killed
fell_out:
    lda area_h
    asl a
    asl a
    asl a
    asl a
    clc
    adc #32
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    bmi :+
    beq :+
    lda ent_fl,x
    ora #(F_REMOVE|F_KILLED)
    sta ent_fl,x
:   rts

; ================================================================== stomps
; CanStomp(p, e = X) -> carry
can_stomp:
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
    jsr epy
    clc
    adc ent_ht,x
    sec
    sbc #16+19
    sta o_tmp
    jsr ppy
    sec
    sbc o_tmp
    beq @yes
    bpl @no
@yes: sec
    rts
@no: clc
    rts
w_can_stomp:
    jsr can_stomp
    rtl

stomp_bounce:
    lda #$10000-$40
    jsl pl_bounce
    inc p_killtally
    phx
    jsr epy
    tay
    jsr epx
    tax
    lda p_killtally
    jsl w_chain_score
    plx
    SFX "STOMP"
    rts
w_stomp_bounce:
    jsr stomp_bounce
    rtl

; ================================================================== dispatch
; TakeHit(e = X, kind = o_d, dir = o_dir) -> carry = affected
take_hit:
    lda ent_type,x
    cmp #ET_GOOMBA
    bne :+
    jmp goomba_hit
:   cmp #ET_KOOPA
    bne :+
    jmp koopa_hit
:   cmp #ET_SHELL
    bne :+
    jmp shell_hit
:   cmp #ET_PIRANHA
    bne :+
    jmp piranha_hit
:   ; items, effects, goal: no
    clc
    rts

; OnBumpBelow(dir = o_dir)
bump_below:
    lda ent_type,x
    cmp #ET_MUSHROOM
    bne :+
    jmp mushroom_bump
:   cmp #ET_STAR
    bne :+
    jmp star_bump
:   cmp #ET_GOOMBA
    beq @hit
    cmp #ET_KOOPA
    beq @hit
    cmp #ET_SHELL
    beq @hit
    rts
@hit:
    lda #D_BUMP
    sta o_d
    jmp take_hit

; OnPlayerTouch
touch:
    lda ent_type,x
    cmp #ET_GOOMBA
    bne :+
    jmp goomba_touch
:   cmp #ET_KOOPA
    bne :+
    jmp koopa_touch
:   cmp #ET_SHELL
    bne :+
    jmp shell_touch
:   cmp #ET_MUSHROOM
    bne :+
    jmp mushroom_touch
:   cmp #ET_FLOWER
    bne :+
    jmp flower_touch
:   cmp #ET_STAR
    bne :+
    jmp star_touch
:   cmp #ET_LEAF
    bne :+
    jmp leaf_touch
:   cmp #ET_GOAL
    bne :+
    jmp goal_touch
:   cmp #ET_PSWITCH
    bne :+
    jmp pswitch_touch
:   ; default: Hurts -> p.Hurt()
    lda ent_fl,x
    and #F_HURTS
    beq :+
    jsl pl_hurt
:   rts

update_one:
    lda ent_type,x
    asl a
    phx
    tax
    lda f:upd_tbl,x
    plx
    sta o_tmp
    jmp (o_tmp)
upd_tbl:
    .addr upd_none, goomba_update, koopa_update, shell_update, piranha_update, efire_update, mushroom_update
    .addr flower_update, leaf_update, star_update, coinpop_update, bump_update, fireball_update, popup_update
    .addr puff_update, sparkle_update, debris_update, dust_update, goal_update, cardfly_update, vine_update
    .addr pswitch_update, splash_update, platform_update, platform_update
upd_none: rts

coinpop_update:
    inc ent_t,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #5
    sta ent_yvel,x
    lda ent_t,x
    cmp #27
    bcc :+
    jsr set_remove
    phx
    jsr epy
    tay
    jsr epx
    tax
    lda #$0100
    jsl ent_add_popup           ; ScorePopup 100 (score itself was added with the coin)
    plx
:   rts

bump_update:
    inc ent_t,x
    lda ent_t,x
    cmp #8
    bcc :+
    jsr set_remove
    lda ent_v0,x
    sta e_tx
    lda ent_v1,x
    sta e_ty
    phx
    jsl eng_show_tile
    plx
:   rts

popup_update:
    inc ent_t,x
    lda ent_t,x
    cmp #40
    bcs @n
    ldy #16
    cmp #20
    bcc :+
    ldy #8
:   sty o_tmp
    lda ent_y,x
    sec
    sbc o_tmp
    sta ent_y,x
@n: lda ent_t,x
    cmp #57
    bcc :+
    jsr set_remove
:   rts

puff_update:
    inc ent_t,x
    lda ent_t,x
    cmp #18
    bcc :+
    jsr set_remove
:   rts
sparkle_update:
    inc ent_t,x
    lda ent_t,x
    cmp #12
    bcc :+
    jsr set_remove
:   rts
splash_update:
    inc ent_t,x
    lda ent_t,x
    cmp #16
    bcc :+
    jsr set_remove
:   rts
dust_update:
    inc ent_t,x
    lda ent_y,x
    sec
    sbc #4
    sta ent_y,x
    lda ent_t,x
    cmp #12
    bcc :+
    jsr set_remove
:   rts
debris_update:
    inc ent_t,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #4
    sta ent_yvel,x
    lda cam_y
    clc
    adc #250
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    bmi :+
    beq :+
    jsr set_remove
:   rts
goal_update:
    inc ent_t,x
    lda ent_v1,x
    bne @r
    lda ent_t,x
    ldy #7
    jsr mod_a
    bne @r
    lda ent_v0,x
    inc a
    cmp #3
    bcc :+
    lda #0
:   sta ent_v0,x
@r: rts
goal_touch:
    lda ent_v1,x
    bne @r
    lda #1
    sta ent_v1,x
    phx
    lda ent_v0,x
    sta o_i
    jsr epx
    clc
    adc #8
    sta e_t3
    jsr epy
    clc
    adc #8
    sta e_t4
    lda #ET_CARDFLY
    jsr new_fx
    bcc :+
    lda o_i
    sta ent_v0,x
:   plx
    SFX "CARDSTOP"
    lda o_i
    jsl w_start_clear
@r: rts
cardfly_update:
    inc ent_t,x
    lda ent_y,x
    sec
    sbc #32
    sta ent_y,x
    lda ent_t,x
    cmp #61
    bcc :+
    jsr set_remove
:   rts

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
    jsr update_one
:   ply
    iny
    bra @u
@ud:
    jsr flush_adds
    jsr collisions
    jsr despawn
    jsr remove_all
    rtl

; only effects and the card (EndTick)
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
    jsr update_one
:   ply
    iny
    bra @u
@ud:
    jsr flush_adds
    jsr remove_all
    rtl

; Overlaps(e = X, rect tt_x/tt_y/tt_w/tt_h) -> carry
overlaps_rect:
    jsr epx
    clc
    adc ent_hbx,x
    sta e_t0                    ; ex
    jsr epy
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

; set tt_* rect from entity Y's hitbox
rect_of:
    phx
    tyx
    jsr epx
    clc
    adc ent_hbx,x
    sta tt_x
    jsr epy
    clc
    adc ent_hby,x
    sta tt_y
    lda ent_hbw,x
    sta tt_w
    lda ent_hbh,x
    sta tt_h
    plx
    rts

collisions:
    lda p_state
    beq @go
    cmp #PS_VINE
    beq @go
    cmp #PS_AUTOWALK
    beq @go
    rts
@go:
    stz o_i
@l: lda o_i
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
    txa
    inc a
    inc a
    cmp p_carrying
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
    lda p_star
    beq @nostar
    lda ent_class,x
    bne @nostar
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
    SFX "KICK"
    jsl w_mark_killed
    bra @next
@simm:
    lda ent_fl,x
    and #F_STARIMM
    beq @next
    lda ent_fl,x
    and #F_HURTS
    bne @next
    jsr touch
    bra @next
@nostar:
    lda p_sliding
    beq @touch
    lda ent_class,x
    bne @touch
    lda ent_fl,x
    and #F_SHELLIMM
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
    jsl w_mark_killed
    inc p_killtally
    SFX "KICK"
    bra @next
@touch:
    jsr touch
@next:
    inc o_i
    jmp @l
@shells:
    ; moving shells and the carried object vs other enemies
    stz o_i
@s: lda o_i
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
    txa
    inc a
    inc a
    cmp p_carrying
    beq :+
    lda ent_fl,x
    and #F_SHELL
    beq @snext
:   stz o_j
@o: lda o_j
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
    inc o_j
    bra @o
@snext:
    inc o_i
    jmp @s

; s = ent_cur, o = ent_other -> carry set = break
shell_vs:
    ldx ent_cur
    txa
    inc a
    inc a
    cmp p_carrying
    bne @notcarry
    ; carried object hitting an enemy: both die
    ldx ent_other
    lda #D_SHELL
    sta o_d
    lda p_facing
    sta o_dir
    jsr take_hit
    bcc :+
    jsl w_mark_killed
:   ldx ent_cur
    lda p_facing
    NEG16
    jsr knock_off
    jsl w_mark_killed
    stz p_carrying
    SFX "KICK"
    sec
    rts
@notcarry:
    ldy ent_other
    lda ent_type,y
    cmp #ET_SHELL
    bne @notshell
    lda ent_fl,y
    and #F_SHELL
    beq @notshell
    lda ent_xvel,y
    beq @notshell
    ; two moving shells: both die
    lda ent_xvel,x
    jsr sign16
    jsr knock_off
    jsl w_mark_killed
    ldx ent_other
    lda ent_xvel,x
    jsr sign16
    jsr knock_off
    jsl w_mark_killed
    SFX "KICK"
    sec
    rts
@notshell:
    lda ent_fl,y
    and #F_SHELLIMM
    beq :+
    clc
    rts
:   ; chain points
    lda ent_chain,x
    inc a
    sta e_t7                    ; n
    lda ent_points,y
    sta e_t6                    ; basePts
    lda e_t7
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
    lda e_t7
    sta ent_chain,x
    cmp #9
    bcc :+
    ldx ent_other
    phx
    jsr epy
    tay
    jsr epx
    tax
    jsl w_one_up_at
    plx
:   ldx ent_other
    jsl w_mark_killed
    SFX "KICK"
    clc
    rts
@nohit:
    ldx ent_other
    lda e_t6
    sta ent_points,x
    clc
    rts
chain_tbl: .word $0100, $0200, $0400, $0800, $1000, $2000, $4000, $8000

; projectile X vs enemies
proj_vs_enemies:
    ldy #0
@l: cpy ent_n
    beq @d
    phy
    tya
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
    ; e.Overlaps(o)
    jsr rect_of
    jsr overlaps_rect
    bcc @n
    ldx ent_cur
    jsr fireball_hit_enemy
    ply
    rts
@n: ply
    iny
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
    txa
    inc a
    inc a
    cmp p_carrying
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
    jsr epx
    sec
    sbc cam_x
    cmp #$10000-128
    bcs @n                      ; -128..-1 : on
    cmp #385
    bcc @n
    bra @off
@vert:
    jsr epy
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
    jsr epx
    sec
    sbc cam_x
    clc
    adc #32
    bmi @off
    cmp #321
    bcs @off
    jsr epy
    sec
    sbc cam_y
    clc
    adc #128
    bmi @off
    cmp #369
    bcs @off
    bra @n
@off:
    jsr set_remove
    lda ent_spawn,x
    bmi @n
    lda ent_fl,x
    and #F_KILLED
    beq @resp
    lda ent_spawn,x
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
    lda ent_spawn,x
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

; ------------------------------------------------------------------ exports used by the world
; BumpAbove(e_tx, e_ty): entities standing on the block get OnBumpBelow
ent_bump_above:
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    sta e_t6                    ; top
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta e_t7                    ; left
    ldy #0
@l: cpy ent_n
    beq @d
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    bne @n
    ; e.Bottom >= top - 2 && e.Bottom <= top + 4
    jsr epy
    clc
    adc ent_ht,x
    sec
    sbc e_t6
    clc
    adc #2
    bmi @n
    cmp #7
    bcs @n
    ; e.Px + e.Wd > tx*16 && e.Px < tx*16 + 16
    jsr epx
    clc
    adc ent_wd,x
    sec
    sbc e_t7
    bmi @n
    beq @n
    jsr epx
    sec
    sbc e_t7
    cmp #16
    bpl @n
    ; d = e.Cx < tx*16 + 8 ? -1 : 1
    jsr cx
    sec
    sbc e_t7
    sec
    sbc #8
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   sta o_dir
    lda e_tx
    pha
    lda e_ty
    pha
    lda e_t6
    pha
    lda e_t7
    pha
    jsr bump_below
    pla
    sta e_t7
    pla
    sta e_t6
    pla
    sta e_ty
    pla
    sta e_tx
@n: ply
    iny
    bra @l
@d: rtl

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
    jsr epy
    clc
    adc #8
    tay
    jsr cx
    jsr e_solid
    beq @ok
    lda p_facing
    jsr knock_off
    jsl w_mark_killed
    SFX "KICK"
    rtl
@ok:
    lda ent_type,x
    cmp #ET_SHELL
    bne :+
    lda p_facing
    ldy p_xvel
    jsr shell_kick
:   lda #$0C
    sta p_kickpose
    SFX "KICK"
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
    jsr set_remove
    stz p_carrying
:   rtl

; TailHit (timer in p_tailattack)
ent_tail_hit:
    ; bx = Facing > 0 ? Px - 10 : Px + 17 ; by = Py + 16 ; rect 10x15
    jsr ppx
    ldy p_facing
    bmi :+
    sec
    sbc #10
    bra :++
:   clc
    adc #17
:   sta tt_x
    jsr ppy
    clc
    adc #16
    sta tt_y
    lda #10
    sta tt_w
    lda #15
    sta tt_h
    ldy #0
@l: cpy ent_n
    beq @d
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #(F_REMOVE|F_DYING)
    bne @n
    lda ent_class,x
    bne @n
    jsr overlaps_rect
    bcc @n
    lda #D_TAIL
    sta o_d
    ; dir: -Facing * -1 * (Facing > 0 ? -1 : 1) == -1 always... (C# expression) = Facing>0 ? -1 : -1
    lda #$FFFF
    sta o_dir
    jsr take_hit
    bcc @n
    jsl w_mark_killed
    SFX "KICK"
    jsr cx
    sec
    sbc #4
    sta e_t3
    jsr epy
    sta e_t4
    phx
    lda #ET_SPARKLE
    jsr new_fx
    plx
@n: ply
    iny
    bra @l
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
    bne @nb2
    jsl w_break_brick
    rtl
@nb: sta o_tmp
@nb2:
    jsl eng_tile_at
    PROPS3
    and #TP_BUMP
    beq @r
    lda #1
    ldx #1
    jsl w_hit_block
@r: rtl

; PlatformSupport: no platforms in M1
ent_platform_support:
    ; any platform carrying the player (C# Platform.Carrying = playerOn)
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
    lda ent_v3,x                ; playerOn
    beq @n
    sec
    rtl
@n: iny
    bra @l
@no: clc
    rtl

; ------------------------------------------------------------------ platforms (MovingLift, DonutLift)
; v0 = t / stoodFor, v1 = ox / fall, v2 = oy, v3 = playerOn, state = vertical flag
new_lift:
    pha
    lda #ET_LIFT
    ldy #EC_PLATFORM
    jsl ent_add
    pla
    bcs :+
    rts
:   sta ent_state,x
    jsr set_pos
    lda #48
    jsr init_platform
    lda ent_x,x
    sta ent_v1,x
    lda ent_y,x
    sta ent_v2,x
    sec
    rts
new_donut:
    lda #ET_DONUT
    ldy #EC_PLATFORM
    jsl ent_add
    bcs :+
    rts
:   jsr set_pos
    lda #16
    jsr init_platform
    sec
    rts
init_platform:
    sta ent_wd,x
    sta ent_hbw,x
    lda #8
    sta ent_ht,x
    sta ent_hbh,x
    stz ent_hbx,x
    stz ent_hby,x
    lda #0
    sta ent_fl,x
    rts

platform_update:
    lda ent_x,x
    sta pl_prevx
    lda ent_y,x
    sta pl_prevy
    ; wasOn = playerOn && p.State == Normal && !p.InAir
    stz pl_wason
    lda ent_v3,x
    beq :+
    lda p_state
    ora p_inair
    bne :+
    inc pl_wason
:   ; Move()
    lda ent_type,x
    cmp #ET_LIFT
    beq @lift
    ; DonutLift: if playerOn stoodFor++ else if fall == 0 stoodFor = max(0, stoodFor - 1)
    lda ent_v3,x
    beq :+
    inc ent_v0,x
    bra @dn2
:   lda ent_v1,x
    bne @dn2
    lda ent_v0,x
    beq @dn2
    dec ent_v0,x
@dn2:
    lda ent_v1,x
    bne @falling
    lda ent_v0,x
    cmp #31
    bcc @moved
    lda #1
    sta ent_v1,x
@falling:
    inc ent_v1,x
    lda ent_yvel,x
    clc
    adc #2
    cmp #$31
    bcc :+
    lda #$30
:   sta ent_yvel,x
    clc
    adc ent_y,x
    sta ent_y,x
    lda cam_y
    clc
    adc #260
    sta o_tmp
    jsr epy
    sec
    sbc o_tmp
    bmi @moved
    beq @moved
    jsr set_remove
    bra @moved
@lift:
    inc ent_v0,x
    lda ent_v0,x
    ldy #240
    jsr mod_a
    asl a
    phx
    tax
    lda f:eng_tables+360,x      ; sin * 48*16
    sta o_tmp
    lda f:eng_tables+1320,x     ; sin * 56*16
    plx
    ldy ent_state,x
    beq @hz
    lda o_tmp
    clc
    adc ent_v2,x
    sta ent_y,x
    bra @moved
@hz: clc
    adc ent_v1,x
    sta ent_x,x
@moved:
    ; dx, dy
    lda ent_x,x
    sec
    sbc pl_prevx
    sta pl_dx
    lda ent_y,x
    sec
    sbc pl_prevy
    sta pl_dy
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
    jsr epx
    sec
    sbc o_tmp
    beq :+
    bpl @off
:   jsr epx
    clc
    adc ent_wd,x
    sta o_tmp
    jsr ppx
    clc
    adc #4
    sec
    sbc o_tmp
    beq @keep
    bpl @off
@keep:
    jsr epy
    sec
    sbc #32
    asl a
    asl a
    asl a
    asl a
    sta p_y
    stz p_inair
    stz p_yvel
    bra @land
@off: stz ent_v3,x
@land:
    ; landing on the platform
    stz ent_v3,x
    lda p_state
    jne @r
    lda p_yvel
    jmi @r
    jsr ppx
    clc
    adc #12
    sta o_tmp
    jsr epx
    sec
    sbc o_tmp
    beq :+
    bpl @r
:   jsr epx
    clc
    adc ent_wd,x
    sta o_tmp
    jsr ppx
    clc
    adc #4
    sec
    sbc o_tmp
    beq :+
    bpl @r
:   ; feet >= Py && feet <= Py + 6 + max(0, dy >> 4)
    jsr ppy
    clc
    adc #32
    sta pl_feet
    jsr epy
    sta o_tmp
    lda pl_feet
    sec
    sbc o_tmp
    bmi @r
    lda pl_dy
    ASR4
    bpl :+
    lda #0
:   clc
    adc #6
    adc o_tmp
    sec
    sbc pl_feet
    bmi @r
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
    lda #1
    sta ent_v3,x
@r: rts

lift_draw:
    DRAW SPR_SEMI_L
    lda spr_arg_x
    clc
    adc #16
    sta spr_arg_x
    DRAW SPR_SEMI_C
    lda spr_arg_x
    clc
    adc #16
    sta spr_arg_x
    DRAW SPR_SEMI_R
    rts
donut_draw:
    lda ent_v0,x
    cmp #11
    bcc :+
    lda ent_v1,x
    bne :+
    lda w_frame
    lsr a
    and #1
    clc
    adc spr_arg_x
    sta spr_arg_x
:   DRAW SPR_BUMP_USED
    rts

.global w_break_brick, w_hit_block

; ================================================================== drawing

; spr_arg_x/y from entity X (+ o_tmp offsets e_t0/e_t1), flags in e_t2
set_draw_xy:
    jsr epx
    sec
    sbc cam_x
    sta spr_arg_x
    jsr epy
    sec
    sbc cam_y
    sta spr_arg_y
    stz spr_arg_flags
    rts

draw_meta:
    sta spr_arg_id
    phx
    sep #$20
    jsl spr_meta
    rep #$30
    plx
    rts

ent_draw:
    ; behind-BG objects first (C# WorldRender order)
    lda #0
    jsr draw_pass_behind
    lda #1
    jsr draw_pass
    rtl
.export ent_draw_effects
ent_draw_effects:
    lda #2
    jsr draw_pass
    rtl

draw_pass_behind:
    ldy #0
@l: cpy ent_n
    beq @d
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #(F_REMOVE|F_BEHIND)
    cmp #F_BEHIND
    bne :+
    jsr draw_one
:   ply
    iny
    bra @l
@d: rts

; A = 1: non-effects (not behind, not carried), then the carried object; 2: effects
draw_pass:
    sta o_j
    ldy #0
@l: cpy ent_n
    beq @d
    phy
    tya
    asl a
    tay
    lda ent_order,y
    tax
    lda ent_fl,x
    and #(F_REMOVE|F_BEHIND)
    bne @n
    lda o_j
    cmp #2
    beq @fx
    lda ent_class,x
    cmp #EC_EFFECT
    beq @n
    txa
    inc a
    inc a
    cmp p_carrying
    beq @n
    jsr draw_one
    bra @n
@fx: lda ent_class,x
    cmp #EC_EFFECT
    bne @n
    jsr draw_one
@n: ply
    iny
    bra @l
@d: lda o_j
    cmp #1
    bne :+
    lda p_carrying
    beq :+
    dec a
    dec a
    tax
    jsr draw_one
:   rts

draw_one:
    jsr set_draw_xy
    lda ent_fl,x
    and #F_BEHIND
    beq :+
    lda #4
    sta spr_arg_flags
:   lda ent_type,x
    asl a
    phx
    tax
    lda f:drw_tbl,x
    plx
    sta o_tmp
    jmp (o_tmp)
drw_tbl:
    .addr drw_none, goomba_draw, koopa_draw, shell_draw, piranha_draw, efire_draw, mushroom_draw
    .addr flower_draw, leaf_draw, star_draw, coinpop_draw, bump_draw, fireball_draw, popup_draw
    .addr puff_draw, sparkle_draw, debris_draw, dust_draw, goal_draw, cardfly_draw, vine_draw
    .addr pswitch_draw, splash_draw, lift_draw, donut_draw
drw_none: rts

; flip helpers
face_flag:
    lda ent_facing,x
    bpl :+
    lda spr_arg_flags
    ora #1
    sta spr_arg_flags
:   rts
knocked_flag:
    lda spr_arg_flags
    ora #2
    sta spr_arg_flags
    jmp face_flag

goomba_draw:
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsr knocked_flag
    DRAW SPR_GOOMBA_1
    rts
:   lda ent_v1,x
    beq :+
    lda spr_arg_y
    clc
    adc #8
    sta spr_arg_y
    DRAW SPR_GOOMBA_FLAT
    rts
:   lda ent_v0,x
    beq @body
    ; wings behind the body
    lda spr_arg_x
    pha
    lda spr_arg_y
    pha
    sec
    sbc #2
    sta spr_arg_y
    lda spr_arg_x
    sec
    sbc #4
    sta spr_arg_x
    lda ent_anim,x
    ldy #8
    lda ent_fl,x
    and #F_GROUND
    bne :+
    ldy #3
:   sty o_d
    lda ent_anim,x
    jsr div_y
    and #1
    sta o_i
    bne :+
    DRAW SPR_WING_1
    bra :++
:   DRAW SPR_WING_2
:   lda spr_arg_x
    clc
    adc #16
    sta spr_arg_x
    lda #1
    sta spr_arg_flags
    lda o_i
    bne :+
    DRAW SPR_WING_1
    bra :++
:   DRAW SPR_WING_2
:   pla
    sta spr_arg_y
    pla
    sta spr_arg_x
    stz spr_arg_flags
@body:
    lda ent_anim,x
    lsr a
    lsr a
    lsr a
    and #1
    sta spr_arg_flags
    DRAW SPR_GOOMBA_1
    rts

; A / o_d (A >= 0) -> A
div_y:
    ldy #0
@l: cmp o_d
    bcc @r
    sec
    sbc o_d
    iny
    bra @l
@r: tya
    rts

koopa_draw:
    lda ent_fl,x
    and #F_DYING
    beq @alive
    jsr knocked_flag
    lda spr_arg_y
    clc
    adc #8
    sta spr_arg_y
    lda ent_v0,x
    bne :+
    DRAW SPR_SHELL_1
    rts
:   DRAW SPR_SHELL_1_RED
    rts
@alive:
    jsr face_flag
    lda ent_anim,x
    and #8
    bne @f2
    lda ent_v0,x
    bne :+
    DRAW SPR_KOOPA_1
    bra @wing
:   DRAW SPR_KOOPA_1_RED
    bra @wing
@f2: lda ent_v0,x
    bne :+
    DRAW SPR_KOOPA_2
    bra @wing
:   DRAW SPR_KOOPA_2_RED
@wing:
    lda ent_v1,x
    beq @r
    lda ent_facing,x
    bmi :+
    lda spr_arg_x
    sec
    sbc #2
    bra :++
:   lda spr_arg_x
    clc
    adc #10
:   sta spr_arg_x
    lda spr_arg_y
    clc
    adc #4
    sta spr_arg_y
    lda ent_anim,x
    and #4
    bne :+
    DRAW SPR_WING_1
    rts
:   DRAW SPR_WING_2
@r: rts

shell_draw:
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsr knocked_flag
    lda #0
    bra @img
:   lda ent_v1,x
    beq :+
    lda #2
    sta spr_arg_flags
:   ; frame
    jsr shell_moving
    beq @still
    lda ent_anim,x
    lsr a
    and #3
    bra @img
@still:
    ; shake before waking up
    lda ent_v2,x
    cmp #331
    bcc :+
    lda ent_anim,x
    lsr a
    and #1
    clc
    adc spr_arg_x
    sta spr_arg_x
:   lda #0
@img:
    ldy ent_v0,x
    bne @red
    cmp #0
    bne :+
    DRAW SPR_SHELL_1
    rts
:   cmp #1
    bne :+
    DRAW SPR_SHELL_2
    rts
:   cmp #2
    bne :+
    DRAW SPR_SHELL_3
    rts
:   DRAW SPR_SHELL_4
    rts
@red:
    cmp #0
    bne :+
    DRAW SPR_SHELL_1_RED
    rts
:   cmp #1
    bne :+
    DRAW SPR_SHELL_2_RED
    rts
:   cmp #2
    bne :+
    DRAW SPR_SHELL_3_RED
    rts
:   DRAW SPR_SHELL_4_RED
    rts

piranha_draw:
    lda #4
    sta spr_arg_flags
    lda ent_v0,x
    bne @venus
    lda ent_anim,x
    and #8
    bne :+
    DRAW SPR_PIRANHA_1
    rts
:   DRAW SPR_PIRANHA_2
    rts
@venus:
    ; faces the player; mouth open while shooting
    jsr face_toward
    bpl :+
    lda #5
    sta spr_arg_flags
:   lda ent_state,x
    cmp #2
    bne @c
    lda ent_t,x
    cmp #23
    bcc @c
    cmp #40
    bcs @c
    DRAW SPR_VENUS_2
    rts
@c: DRAW SPR_VENUS_1
    rts

efire_draw:
fireball_draw:
    lda ent_type,x
    cmp #ET_FIREBALL
    bne :+
    jsr face_flag
:   lda ent_t,x
    lsr a
    and #3
    beq @f1
    cmp #1
    beq @f2
    cmp #2
    beq @f3
    DRAW SPR_FIREBALL_4
    rts
@f1: DRAW SPR_FIREBALL_1
    rts
@f2: DRAW SPR_FIREBALL_2
    rts
@f3: DRAW SPR_FIREBALL_3
    rts

mushroom_draw:
    lda ent_v0,x
    bne :+
    DRAW SPR_MUSHROOM
    rts
:   DRAW SPR_MUSHROOM_ONEUP
    rts
flower_draw:
    lda w_frame
    and #4
    beq :+
    DRAW SPR_FLOWER_1_FLASH
    rts
:   DRAW SPR_FLOWER_1
    rts
leaf_draw:
    lda ent_v0,x
    bpl :+
    lda #1
    sta spr_arg_flags
:   DRAW SPR_LEAF
    rts
star_draw:
    lda w_frame
    lsr a
    and #3
    beq @s1
    cmp #1
    beq @s2
    cmp #2
    beq @s3
    DRAW SPR_STAR_4
    rts
@s1: DRAW SPR_STAR
    rts
@s2: DRAW SPR_STAR_2
    rts
@s3: DRAW SPR_STAR_3
    rts
coinpop_draw:
    lda ent_t,x
    ldy #3
    sty o_d
    jsr div_y
    and #3
    beq @1
    cmp #1
    beq @2
    cmp #2
    beq @3
    DRAW SPR_COIN_4
    rts
@1: DRAW SPR_COIN_1
    rts
@2: DRAW SPR_COIN_2
    rts
@3: DRAW SPR_COIN_3
    rts
bump_draw:
    ; tile image drawn as a sprite, offset by the bump curve
    lda ent_t,x
    cmp #4
    bcc :+
    eor #$FFFF
    clc
    adc #9                      ; 8 - t
:   asl a
    ldy ent_v2,x
    bpl :+
    NEG16
:   clc
    adc spr_arg_y
    sta spr_arg_y
    lda ent_v3,x
    cmp #T_BRICK
    bne :+
    DRAW SPR_BUMP_BRICK
    rts
:   cmp #T_NOTE
    bne :+
    DRAW SPR_BUMP_NOTE
    rts
:   cmp #T_WOOD
    bne :+
    DRAW SPR_BUMP_WOOD
    rts
:   DRAW SPR_BUMP_USED
    rts
popup_draw:
    lda ent_v0,x
    cmp #$FFFF
    bne @num
    DRAW SPR_TINY_1
    lda spr_arg_x
    clc
    adc #5
    sta spr_arg_x
    DRAW SPR_TINY_UP
    rts
@num:
    ; BCD digits without leading zeros
    sta o_i
    lda #4
    sta o_j
    stz o_d                     ; started
@dg: lda o_i
    xba
    lsr a
    lsr a
    lsr a
    lsr a
    and #15
    bne :+
    ldy o_d
    bne :+
    lda o_j
    cmp #1
    bne @skip
    lda #0
:   inc o_d
    asl a
    phx
    tax
    lda f:tiny_ids,x
    plx
    cmp #$FFFF
    beq :+
    jsr draw_meta
:   lda spr_arg_x
    clc
    adc #5
    sta spr_arg_x
@skip:
    lda o_i
    asl a
    asl a
    asl a
    asl a
    sta o_i
    dec o_j
    bne @dg
    rts
tiny_ids:
.ifdef SPR_TINY_0
    .word SPR_TINY_0, SPR_TINY_1, SPR_TINY_2, SPR_TINY_3, SPR_TINY_4, SPR_TINY_5, SPR_TINY_6, SPR_TINY_7, SPR_TINY_8, SPR_TINY_9
.else
    .word $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF
.endif
puff_draw:
    lda ent_v0,x
    bne @small
    lda ent_t,x
    cmp #12
    bcs @3
    cmp #6
    bcs @2
    DRAW SPR_PUFF_1
    rts
@2: DRAW SPR_PUFF_2
    rts
@3: DRAW SPR_PUFF_3
    rts
@small:
    lda ent_t,x
    and #4
    bne :+
    DRAW SPR_SPARKLE_1
    rts
:   DRAW SPR_SPARKLE_2
    rts
sparkle_draw:
    lda ent_t,x
    ldy #3
    sty o_d
    jsr div_y
    and #1
    bne :+
    DRAW SPR_SPARKLE_1
    rts
:   DRAW SPR_SPARKLE_2
    rts
debris_draw:
    lda ent_t,x
    lsr a
    lsr a
    and #1
    sta spr_arg_flags
    lda ent_t,x
    lsr a
    lsr a
    and #2
    ora spr_arg_flags
    sta spr_arg_flags
    DRAW SPR_DEBRIS
    rts
dust_draw:
    lda ent_t,x
    cmp #6
    bcc :+
    and #2
    bne @r
:   DRAW SPR_DUST
@r: rts
goal_draw:
    DRAW SPR_GOAL_BOX
    lda ent_v1,x
    bne @r
    lda spr_arg_x
    clc
    adc #8
    sta spr_arg_x
    lda spr_arg_y
    clc
    adc #8
    sta spr_arg_y
    lda ent_v0,x
    jmp draw_card
@r: rts
cardfly_draw:
    lda ent_v0,x
draw_card:
    cmp #1
    beq :+
    cmp #2
    beq :++
    DRAW SPR_CARD_MUSHROOM
    rts
:   DRAW SPR_CARD_FLOWER
    rts
:   DRAW SPR_CARD_STAR
    rts
vine_draw:
    ; tx*16 - camX, (ty+1)*16 - camY - (t % 8) * 2
    lda ent_v1,x
    inc a
    asl a
    asl a
    asl a
    asl a
    sec
    sbc cam_y
    sta spr_arg_y
    lda ent_t,x
    and #7
    asl a
    sta o_tmp
    lda spr_arg_y
    sec
    sbc o_tmp
    sta spr_arg_y
    DRAW SPR_VINE_SPROUT
    rts
pswitch_draw:
    DRAW SPR_PSWITCH
    rts
splash_draw:
    lda ent_t,x
    cmp #8
    bcs :+
    DRAW SPR_SPLASH_1
    rts
:   DRAW SPR_SPLASH_2
    rts

