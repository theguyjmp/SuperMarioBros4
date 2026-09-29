; Wrench (port of C# Wrench, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Rocky Wrench's projectile (Y = dir): 8x8, flies straight at dir * $18 for 300 ticks, spins by flipping.
; Hurts the player, cannot be hit, ignores star.
;@entity WRENCH
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE WRENCH, wrench_init, wrench_update, wrench_draw, ent_cb_none, ent_cb_none, 0

.a16
.i16

wrench_init:
    ENT_ENTER
    lda #EC_EPROJ
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^(F_SLOT|F_STOMP)
    ora #F_STARIMM
    sta ent_fl,x
    lda #8
    sta ent_wd,x
    sta ent_ht,x
    sta ent_hbw,x
    sta ent_hbh,x
    stz ent_hbx,x
    stz ent_hby,x               ; hitbox 0,0,8,8
    lda ent_arg,x               ; XVel = dir * $18
    asl a
    asl a
    asl a
    sta es0
    asl a
    clc
    adc es0
    sta ent_xvel,x
    sec
    rtl

wrench_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    lda ent_t,x
    cmp #301
    bcc :+
    jsl ent_remove
:   rtl

; hflip when (t/4)%2 == 0, vflip when (t/8)%2 == 0
wrench_draw:
    ENT_ENTER
    lda ent_t,x
    lsr a
    lsr a
    and #1
    eor #1
    sta es0
    lda ent_t,x
    lsr a
    lsr a
    and #2
    eor #2
    ora es0
    ora spr_arg_flags
    sta spr_arg_flags
    ENT_DRAW SPR_WRENCH_1
    rtl
