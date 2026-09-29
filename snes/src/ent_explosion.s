; Bob-omb explosion (port of C# Explosion, src/Game/Entities/Enemies.cs). Owner: enemies-A agent. Spawned only.
; 32x32 enemy projectile: on its 2nd tick it knocks off every enemy it overlaps; hurts the player for 13 ticks;
; gone after 20. Drawn as 4 mirrored puff quarters alternating two fire palettes.
;@entity EXPLOSION
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE EXPLOSION, boom_init, boom_update, boom_draw, ent_cb_none, ent_cb_none, boom_touch

; ent_t = t
.a16
.i16

boom_init:
    ENT_ENTER
    lda #EC_EPROJ
    sta ent_class,x
    lda #F_STARIMM              ; UsesSlot = false, Stompable = false
    sta ent_fl,x
    lda #32
    sta ent_wd,x
    sta ent_ht,x
    lda #2
    sta ent_hbx,x
    sta ent_hby,x
    lda #28
    sta ent_hbw,x
    sta ent_hbh,x
    sec
    rtl

boom_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_t,x
    cmp #2
    bne @end
    ; foreach enemy e (list order): !Dying && e.Overlaps(this) -> e.KnockOff(e.Cx < Cx + 16 ? -1 : 1); MarkKilled(e)
    jsl ent_cx
    clc
    adc #16
    sta es1                     ; Cx + 16
    stx es2                     ; this
    stz es0
@l: lda es0
    cmp ent_n
    bcs @done
    asl a
    tay
    lda ent_order,y
    tax
    cpx es2
    beq @n
    lda ent_class,x
    cmp #EC_ENEMY
    bne @n
    lda ent_fl,x
    and #F_DYING
    bne @n
    ldy es2
    jsl ent_overlaps_ent
    bcc @n
    jsl ent_cx
    sec
    sbc es1
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   jsl ent_knock_off
    jsl ent_kill
@n: inc es0
    bra @l
@done:
    ldx es2
@end:
    lda ent_t,x
    cmp #21
    bcc :+
    jsl ent_remove
:   rtl

boom_touch:
    ENT_ENTER
    lda ent_t,x
    cmp #14
    bcs :+
    jsl ent_hurt_player
:   clc
    rtl

; puff.(1 + min(2, t / 7)) in the hot (t/2 even) or warm fire palette, 4 quarters (plain, h, v, hv)
boom_draw:
    ENT_ENTER
    lda ent_t,x
    ldy #7
    jsl ent_div
    cmp #2
    bcc :+
    lda #2
:   sta es0                     ; frame 0..2
    lda ent_t,x
    and #2
    beq :+
    lda #3
:   clc
    adc es0                     ; (t & 2) == 0: hot palette = (t/2) % 2 == 0
    asl a
    phx
    tax
    lda f:boom_ids,x
    plx
    sta es0                     ; metasprite id
    beq @r
    lda spr_arg_x
    sta es1
    lda spr_arg_y
    sta es2
    stz es3                     ; quarter 0..3 = flags hflip(1) / vflip(2)
@q: lda es3
    and #1
    beq :+
    lda #16
:   clc
    adc es1
    sta spr_arg_x
    lda es3
    and #2
    beq :+
    lda #16
:   clc
    adc es2
    sta spr_arg_y
    lda es3
    sta spr_arg_flags
    lda es0
    jsl ent_draw_meta
    inc es3
    lda es3
    cmp #4
    bcc @q
@r: rtl

.ifndef SPR_PUFF_1_HOT
SPR_PUFF_1_HOT = 0
SPR_PUFF_2_HOT = 0
SPR_PUFF_3_HOT = 0
.endif
.ifndef SPR_PUFF_1_WARM
SPR_PUFF_1_WARM = 0
SPR_PUFF_2_WARM = 0
SPR_PUFF_3_WARM = 0
.endif
boom_ids: .word SPR_PUFF_1_HOT, SPR_PUFF_2_HOT, SPR_PUFF_3_HOT, SPR_PUFF_1_WARM, SPR_PUFF_2_WARM, SPR_PUFF_3_WARM
