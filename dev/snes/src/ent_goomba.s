; Goomba / Paragoomba (port of C# Goomba, src/Game/Entities/Enemies.cs). Owner: engine agent.
; Reference example for snes/ENTITIES.md: a walker with stomp/hit/touch/draw.
;@entity GOOMBA codes=gp
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE GOOMBA, goomba_init, goomba_update, goomba_draw, goomba_hit, 0, goomba_touch
; bump = 0: the engine default (TakeHit(Bump) -> goomba_hit)

; per-type fields: v0 = winged, v1 = flat, v2 = flatT, v3 = hopT, ent_state = hops
.a16
.i16

; new Goomba(px, py, winged): arg = spawn char ('p' = winged)
goomba_init:
    ENT_ENTER
    lda ent_arg,x
    cmp #'p'
    bne :+
    lda #1
    sta ent_v0,x
:   jsl ent_face_player_set     ; Facing = P.CenterX < px + 8 ? -1 : 1 (A = Facing)
    asl a
    asl a
    asl a
    sta ent_xvel,x              ; Facing * 8
    lda #4
    sta ent_hby,x
    lda #12
    sta ent_hbh,x               ; hitbox 2,4,12,12
    sec                         ; keep it
    rtl

goomba_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   lda ent_v1,x                ; flat: disappears after 30 ticks
    beq @live
    lda ent_v2,x
    inc a
    sta ent_v2,x
    cmp #31
    bcc :+
    jsl ent_remove
:   rtl
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
    sta es0
    lda #8
    ldy ent_state,x
    cpy #3
    bcc :+
    lda #40
:   cmp es0
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
    jsl ent_face_player
    sta ent_facing,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
@steer:
    ; target = FaceToward(P) * $14 ; XVel moves 1 toward it
    jsl ent_face_player
    bmi :+
    lda #$14
    bra :++
:   lda #$10000-$14
:   sta es0
    lda ent_xvel,x
    sec
    sbc es0
    beq @nowing
    bpl :+
    inc ent_xvel,x
    bra @nowing
:   dec ent_xvel,x
@nowing:
    lda #0                      ; MoveWalker(turnAtLedges = false, turnAtWalls = true)
    ldy #1
    jsl ent_move_walker
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

goomba_touch:
    ENT_ENTER
    lda ent_v1,x
    bne @r
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda ent_v0,x
    beq @flat
    stz ent_v0,x                ; loses its wings
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
@r: rtl
@flat:
    lda #1
    sta ent_v1,x
    lda ent_fl,x
    and #$FFFF^F_HURTS
    sta ent_fl,x
    jsl ent_kill                ; MarkKilled
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

; TakeHit(kind = ent_dmg, dir = ent_dir) -> carry = affected
goomba_hit:
    ENT_ENTER
    lda ent_v1,x
    beq :+
    clc
    rtl
:   lda ent_dir
    jsl ent_knock_off
    sec
    rtl

; draw: the engine already set spr_arg_x/y = Px-CamX, Py-CamY and spr_arg_flags
goomba_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_GOOMBA_1
    rtl
:   lda ent_v1,x
    beq :+
    lda spr_arg_y
    clc
    adc #8
    sta spr_arg_y
    ENT_DRAW SPR_GOOMBA_FLAT
    rtl
:   lda ent_v0,x
    beq @body
    ; wings behind the body: left wing at (-4,-2), mirrored right wing at (+12,-2)
    lda #$10000-4
    ldy #$10000-2
    jsl ent_draw_offset
    ldy #8
    lda ent_fl,x
    and #F_GROUND
    bne :+
    ldy #3
:   lda ent_anim,x
    jsl ent_div                 ; A = Anim / (OnGround ? 8 : 3)
    and #1
    sta es0
    bne :+
    ENT_DRAW SPR_WING_1
    bra :++
:   ENT_DRAW SPR_WING_2
:   lda #16
    ldy #0
    jsl ent_draw_offset
    lda #1
    sta spr_arg_flags
    lda es0
    bne :+
    ENT_DRAW SPR_WING_1
    bra :++
:   ENT_DRAW SPR_WING_2
:   lda #$10000-12
    ldy #2
    jsl ent_draw_offset
@body:
    ; SMB3 walk: one frame, mirrored every 8 ticks
    lda ent_anim,x
    lsr a
    lsr a
    lsr a
    and #1
    sta spr_arg_flags
    ENT_DRAW SPR_GOOMBA_1
    rtl
