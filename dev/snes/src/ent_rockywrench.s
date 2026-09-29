; Rocky Wrench (port of C# RockyWrench, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Hides 12 px down in its airship hatch (drawn behind high-priority BG1 tiles). When the player is within 120 px it
; pops up (1 px/tick), throws a wrench at t = 30, ducks back at t > 60. Harmless and invisible while hidden;
; stompable; every hit kind knocks it off.
;@entity ROCKY_WRENCH codes=w
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE ROCKY_WRENCH, rocky_init, rocky_update, rocky_draw, 0, 0, rocky_touch

; per-type fields: ent_state = phase, ent_t = t, v0 = homeY (px)
.a16
.i16

rocky_init:
    ENT_ENTER
    ; homeY = py + 12; Y = homeY << 4
    lda ent_new_y
    clc
    adc #12
    sta ent_v0,x
    ENT_ASL4
    sta ent_y,x
    lda ent_fl,x
    ora #F_BEHIND
    sta ent_fl,x
    lda #2
    sta ent_hby,x
    lda #12
    sta ent_hbh,x               ; hitbox 2,2,12,12
    ; t = (px * 7) % 60
    lda ent_new_x
    sta es0
    asl a
    asl a
    asl a
    sec
    sbc es0
    ldy #60
    jsl ent_mod
    sta ent_t,x
    sec
    rtl

rocky_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    lda ent_state,x
    beq @hidden
    cmp #1
    beq @up
    cmp #2
    beq @throw
    ; phase 3: Y += 16; Py >= homeY -> phase 0
    lda ent_y,x
    clc
    adc #16
    sta ent_y,x
    jsl ent_py
    sec
    sbc ent_v0,x
    bmi :+
    stz ent_state,x
    stz ent_t,x
:   jmp @hurts
@hidden:
    ; t > 90 && |p.CenterX - Cx| < 120
    lda ent_t,x
    cmp #91
    bcc @hurts
    jsl ent_player_dx
    bpl :+
    eor #$FFFF
    inc a
:   cmp #120
    bcs @hurts
    lda #1
    sta ent_state,x
    stz ent_t,x
    bra @hurts
@up:
    lda ent_y,x
    sec
    sbc #16
    sta ent_y,x
    ; Py <= homeY - 12
    lda ent_v0,x
    sec
    sbc #12
    sta es0
    jsl ent_py
    sec
    sbc es0
    beq :+
    bpl @hurts
:   lda #2
    sta ent_state,x
    stz ent_t,x
    bra @hurts
@throw:
    jsl ent_face_player
    sta ent_facing,x
    lda ent_t,x
    cmp #30
    bne :+
    ; W.Add(new Wrench(Px + 4, Py, Facing))
    jsl ent_px
    clc
    adc #4
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #ET_WRENCH
    ldy ent_facing,x
    jsl ent_spawn
:   lda ent_t,x
    cmp #61
    bcc @hurts
    lda #3
    sta ent_state,x
    stz ent_t,x
@hurts:
    ; Hurts = phase != 0
    lda ent_state,x
    beq :+
    lda ent_fl,x
    ora #F_HURTS
    sta ent_fl,x
    rtl
:   lda ent_fl,x
    and #$FFFF^F_HURTS
    sta ent_fl,x
    rtl

rocky_touch:
    ENT_ENTER
    lda ent_fl,x
    and #F_HURTS
    beq @r
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda ent_fl,x
    ora #(F_DYING|F_KILLED)
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda #1
    sta ent_knock,x
    lda #$10000-$20
    sta ent_yvel,x
@r: rtl
@hurt:
    jsl ent_hurt_player
    rtl

; hidden: not drawn; up: rocky.1 / throwing: rocky.2; behind BG unless dying (then flipped, in front)
rocky_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    bne @dying
    lda ent_state,x
    beq @r
    jsl ent_draw_face
    lda ent_state,x
    cmp #2
    beq @f2
    ENT_DRAW SPR_ROCKY_1
@r: rtl
@f2: ENT_DRAW SPR_ROCKY_2
    rtl
@dying:
    lda #0
    sta spr_arg_flags
    jsl ent_draw_knocked
    lda ent_state,x
    cmp #2
    beq @f2
    ENT_DRAW SPR_ROCKY_1
    rtl
