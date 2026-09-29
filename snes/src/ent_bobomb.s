; Bob-omb (port of C# BobOmb, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Walks; a stomp lights the fuse (240 ticks): it stops, turns upside down, can be carried (hold Y) or kicked,
; flashes in the last 60 ticks and explodes (ET_EXPLOSION). Fireproof.
;@entity BOBOMB codes=a
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE BOBOMB, bobomb_init, bobomb_update, bobomb_draw, 0, 0, bobomb_touch

; v0 = fuse
.a16
.i16

bobomb_init:
    ENT_ENTER
    lda ent_fl,x
    ora #F_FIREIMM
    sta ent_fl,x
    ; Facing = P.CenterX < px ? -1 : 1 ; XVel = Facing * 8
    jsl ent_px
    sta es0
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc #8
    sec
    sbc es0
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   sta ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
    sec
    rtl

bobomb_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    lda ent_v0,x
    beq @walk
    jsl ent_carried
    bcc @free
    ; held: X = (P.Px + (P.Facing > 0 ? 11 : -11)) << 4 ; Y = (P.Py + (P.Big ? 13 : 17)) << 4
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    ldy p_facing
    bmi :+
    clc
    adc #11
    bra :++
:   sec
    sbc #11
:   ENT_ASL4
    sta ent_x,x
    lda #17
    ldy p_form
    beq :+
    lda #13
:   sta es0
    lda p_y
    ASR4
    clc
    adc es0
    ENT_ASL4
    sta ent_y,x
    bra @fuse
@free:
    lda #0                      ; MoveWalker(false, true)
    ldy #1
    jsl ent_move_walker
@fuse:
    dec ent_v0,x
    bne :+
    jsr explode
:   rtl
@walk:
    lda #0
    ldy #1
    jsl ent_move_walker
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

explode:
    jsl ent_carried
    bcc :+
    stz p_carrying
:   lda ent_fl,x
    ora #(F_REMOVE|F_KILLED)
    sta ent_fl,x
    ; new Explosion(Cx - 16, Py - 8)
    jsl ent_cx
    sec
    sbc #16
    sta ent_new_x
    jsl ent_py
    sec
    sbc #8
    sta ent_new_y
.ifdef ET_EXPLOSION
    lda #ET_EXPLOSION
    ldy #0
    jsl ent_spawn
.endif
    ENT_SFX "EXPLODE"
    lda #12
    sta w_shake
    rts

bobomb_touch:
    ENT_ENTER
    lda ent_v0,x
    beq @live
    jsl ent_carried
    bcc :+
    rtl
:   ; lit: pick up with Y (C# B) held, else kick it away
    lda pad_held
    and #BTN_B
    beq @kick
    lda p_carrying
    bne @kick
    txa
    inc a
    inc a
    sta p_carrying
    rtl
@kick:
    jsl ent_player_dx           ; P.CenterX - Cx
    bmi :+
    lda #$10000-$20
    bra :++
:   lda #$20
:   sta ent_xvel,x
    ENT_SFX "KICK"
    lda #$0C
    sta p_kickpose
    rtl
@live:
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda #240
    sta ent_v0,x
    stz ent_xvel,x
    lda ent_fl,x
    and #$FFFF^F_HURTS
    ora #F_CARRY
    sta ent_fl,x
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

bobomb_draw:
    ENT_ENTER
    ; flash palette while 0 < fuse < 60 and (fuse & 4)
    stz es0
    lda ent_v0,x
    beq :+
    cmp #60
    bcs :+
    and #4
    sta es0
:   lda ent_fl,x
    and #F_DYING
    beq @alive
    jsl ent_draw_knocked
    bra @img1
@alive:
    jsl ent_draw_face
    lda ent_v0,x
    beq :+
    lda spr_arg_flags
    ora #2                      ; lit: upside down
    sta spr_arg_flags
    bra @img1
:   lda ent_anim,x
    and #8
    beq @img1
    ENT_DRAW SPR_BOBOMB_2
    rtl
@img1:
    lda es0
    beq :+
    ENT_DRAW SPR_BOBOMB_1_FLASH
    rtl
:   ENT_DRAW SPR_BOBOMB_1
    rtl
