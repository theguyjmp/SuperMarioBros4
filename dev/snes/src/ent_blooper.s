; Blooper (port of C# Blooper, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Sinks slowly; every 50 ticks, if the player is above it, it pushes up (and sideways toward him) for 18 ticks.
; Stays under the water surface and 40 px above the level bottom. Not stompable.
;@entity BLOOPER codes=q
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE BLOOPER, blooper_init, blooper_update, blooper_draw, 0, 0, ent_cb_hurt

; ent_t = t ; v0 = push ; v1 = t mod 50
.a16
.i16

blooper_init:
    ENT_ENTER
    lda ent_y,x
    sec
    sbc #8*16
    sta ent_y,x                 ; base(px, py - 8)
    lda #24
    sta ent_ht,x
    lda #2
    sta ent_hby,x
    lda #18
    sta ent_hbh,x               ; hitbox 2,2,12,18
    lda ent_fl,x
    and #$FFFF^F_STOMP
    sta ent_fl,x
    sec
    rtl

blooper_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    lda ent_v1,x
    inc a
    cmp #50
    bcc :+
    lda #0
:   sta ent_v1,x
    lda ent_v0,x
    beq @sink
    ; pushing: X += XVel ; Y += YVel ; YVel += 1 (up to 0)
    dec ent_v0,x
    jsl ent_apply_vel
    lda ent_yvel,x
    inc a
    bmi :+
    lda #0
:   sta ent_yvel,x
    bra @clamp
@sink:
    lda ent_y,x
    clc
    adc #6
    sta ent_y,x
    ; if (t % 50 == 0 && p.Py + 8 < Py + 8) push toward the player
    lda ent_v1,x
    bne @clamp
    jsl ent_py
    sta es0
    lda p_y
    ASR4
    sec
    sbc es0
    bpl @clamp
    lda #18
    sta ent_v0,x
    lda #$10000-$1C
    sta ent_yvel,x
    jsl ent_player_dx           ; P.CenterX - Cx
    bmi :+
    lda #$10
    bra :++
:   lda #$10000-$10
:   sta ent_xvel,x
@clamp:
    ; if (WaterRow > 0 && Py < WaterRow * 16) Y = WaterRow * 16
    lda area_water
    beq @bot
    bmi @bot
    ENT_ASL4
    sta es0
    jsl ent_py
    sec
    sbc es0
    bpl @bot
    lda es0
    ENT_ASL4
    sta ent_y,x
@bot:
    ; if (Py > LevelPxH - 40) Y = LevelPxH - 40
    lda area_h
    ENT_ASL4
    sec
    sbc #40
    sta es0
    jsl ent_py
    sec
    sbc es0
    beq @r
    bmi @r
    lda es0
    ENT_ASL4
    sta ent_y,x
@r: rtl

blooper_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_BLOOPER_2
    rtl
:   lda ent_v0,x
    beq :+
    lda spr_arg_y
    clc
    adc #8
    sta spr_arg_y
    ENT_DRAW SPR_BLOOPER_2
    rtl
:   ENT_DRAW SPR_BLOOPER_1
    rtl
