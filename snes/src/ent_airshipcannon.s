; Airship cannon `<` / `>` (port of C# AirshipCannon, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Invisible controller on a cannon tile: while on screen fires a Cannonball (vx = dir * $14) every
; 140 + (tx * 37) % 60 ticks (first shot after 100).
;@entity AIRSHIP_CANNON codes=<>
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE AIRSHIP_CANNON, acan_init, acan_update, 0, ent_cb_none, ent_cb_none, ent_cb_none

; per-type fields: ent_t = t, v0 = cooldown, v1 = dir (-1 '<' / +1 '>')
.a16
.i16

acan_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^(F_HURTS|F_SLOT)
    sta ent_fl,x
    lda #100
    sta ent_v0,x
    lda ent_arg,x
    cmp #'<'
    bne :+
    lda #$FFFF
    bra :++
:   lda #1
:   sta ent_v1,x
    sec
    rtl

acan_update:
    ENT_ENTER
    inc ent_t,x
    dec ent_v0,x
    beq :+
    bmi :+
    rtl
:   ; sx < 0 || sx > 256 || sy < -8 || sy > 192 -> retry in 30
    jsl ent_px
    sec
    sbc cam_x
    bmi @wait
    cmp #257
    bcs @wait
    jsl ent_py
    sec
    sbc cam_y
    clc
    adc #8
    bmi @wait
    cmp #192+8+1
    bcc @fire
@wait:
    lda #30
    sta ent_v0,x
    rtl
@fire:
    ; W.Add(new Cannonball(tx*16 + dir*12, ty*16, dir*$14, 0))
    lda ent_v1,x
    asl a
    asl a
    sta es0
    asl a
    clc
    adc es0                     ; dir * 12
    sta es1
    jsl ent_px
    clc
    adc es1
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #ET_CANNONBALL
    ldy ent_v1,x
    jsl ent_spawn
    ; W.Puff(tx*16 + dir*14, ty*16)   (es* may be clobbered by the spawned object's init)
    lda ent_v1,x
    asl a
    sta es0                     ; dir * 2
    asl a
    asl a
    asl a                       ; dir * 16
    sec
    sbc es0                     ; dir * 14
    sta es1
    jsl ent_px
    clc
    adc es1
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    jsl ent_puff_at
    ENT_SFX "CANNON"
    ; cooldown = 140 + (tx * 37) % 60
    jsl ent_px
    lsr a
    lsr a
    lsr a
    lsr a
    sta es0
    asl a
    asl a
    asl a
    clc
    adc es0                     ; *9
    asl a
    asl a                       ; *36
    clc
    adc es0                     ; *37
    ldy #60
    jsl ent_mod
    clc
    adc #140
    sta ent_v0,x
    rtl
