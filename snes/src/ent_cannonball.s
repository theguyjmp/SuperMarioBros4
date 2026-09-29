; Cannonball (port of C# Cannonball, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Enemy projectile from AIRSHIP_CANNON (Y = dir): flies straight at dir * $14; can be stomped (falls off).
;@entity CANNONBALL
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE CANNONBALL, cball_init, cball_update, cball_draw, 0, 0, cball_touch

.a16
.i16

cball_init:
    ENT_ENTER
    lda #EC_EPROJ
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^F_SLOT
    ora #F_FIREIMM
    sta ent_fl,x
    lda #2
    sta ent_hby,x
    lda #12
    sta ent_hbh,x               ; hitbox 2,2,12,12
    lda ent_arg,x               ; XVel = dir * $14
    asl a
    asl a
    sta es0
    asl a
    asl a
    clc
    adc es0
    sta ent_xvel,x
    sec
    rtl

cball_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcs :+
    jsl ent_apply_vel
:   rtl

cball_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda ent_fl,x
    ora #F_DYING
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda #1
    sta ent_knock,x
    stz ent_yvel,x
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

cball_draw:
    ENT_ENTER
    ENT_DRAW SPR_CANNONBALL
    rtl
