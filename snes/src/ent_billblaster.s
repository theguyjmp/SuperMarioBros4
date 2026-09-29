; Bill Blaster (port of C# BillBlaster, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Invisible controller on the top cell of a `b` stack (the level loader adds the 'b' spawn only for the top cell).
; Fires a Bullet Bill toward the player when the cannon is on screen, the player is >= 28 px away and fewer than
; 5 enemies are active. Cooldown 90 at start, then 150 + (uint)(t * 2654435761) % 120.
;@entity BILL_BLASTER codes=b
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE BILL_BLASTER, blaster_init, blaster_update, 0, ent_cb_none, ent_cb_none, ent_cb_none

; per-type fields: ent_t = t, v0 = cooldown, v1 = direction of the shot
.a16
.i16

blaster_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^(F_HURTS|F_SLOT)
    sta ent_fl,x
    lda #90
    sta ent_v0,x
    sec
    rtl

blaster_update:
    ENT_ENTER
    inc ent_t,x
    dec ent_v0,x
    beq :+
    bmi :+
    rtl
:   ; sx = tx*16 - CamX, sy = ty*16 - CamY: off screen (sx < -8 || sx > 256 || sy < -8 || sy > 192) -> retry in 20
    jsl ent_px
    sec
    sbc cam_x
    clc
    adc #8
    bmi @wait20
    cmp #256+8+1
    bcs @wait20
    jsl ent_py
    sec
    sbc cam_y
    clc
    adc #8
    bmi @wait20
    cmp #192+8+1
    bcs @wait20
    ; |p.CenterX - (tx*16 + 8)| < 28 -> retry in 20
    jsl ent_player_dx
    sta es0
    bpl :+
    eor #$FFFF
    inc a
:   cmp #28
    bcs :+
@wait20:
    lda #20
    sta ent_v0,x
    rtl
:   lda es0                     ; dir = p.CenterX < tx*16 + 8 ? -1 : 1
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   sta ent_v1,x
    lda #EC_ENEMY
    jsl ent_count_class
    cmp #5
    bcc :+
    lda #40
    sta ent_v0,x
    rtl
:   ; W.Add(new BulletBill(tx*16 + (dir < 0 ? -8 : 8), ty*16, dir))
    lda ent_v1,x
    asl a
    asl a
    asl a
    sta es0                     ; dir * 8
    jsl ent_px
    clc
    adc es0
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #ET_BULLET_BILL
    ldy ent_v1,x
    jsl ent_spawn
    ; W.Puff(tx*16 + (dir < 0 ? -12 : 12), ty*16)
    lda ent_v1,x
    asl a
    asl a
    sta es0                     ; dir * 4
    asl a
    clc
    adc es0                     ; dir * 12
    sta es0
    jsl ent_px
    clc
    adc es0
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    jsl ent_puff_at
    ENT_SFX "CANNON"
    ; cooldown = 150 + (int)((uint)(t * 2654435761u) % 120)
    lda ent_t,x
    sta es0
    lda #$9E37                  ; high half of 2654435761 = $9E3779B1: only the low 16 bits of t*$9E37 matter
    jsr mul16
    lda es1
    pha
    lda #$79B1
    jsr mul16                   ; es2:es1 = t * $79B1
    pla
    clc
    adc es2
    sta es2                     ; es2:es1 = low 32 bits of t * $9E3779B1
    ; (hi*65536 + lo) % 120 = ((hi % 120) * 16 + lo % 120) % 120   (65536 % 120 = 16)
    lda es2
    ldy #120
    jsl ent_mod
    asl a
    asl a
    asl a
    asl a
    sta es2
    lda es1
    ldy #120
    jsl ent_mod
    clc
    adc es2
    ldy #120
    jsl ent_mod
    clc
    adc #150
    sta ent_v0,x
    rtl

; es2:es1 = es0 * A (unsigned 16x16 -> 32). Clobbers es3, Y.
mul16:
    sta es3
    stz es1
    stz es2
    ldy #16
@l: asl es1
    rol es2
    asl es3
    bcc @n
    lda es1
    clc
    adc es0
    sta es1
    bcc @n
    inc es2
@n: dey
    bne @l
    rts
