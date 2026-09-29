; Podoboo (port of C# Podoboo, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Waits below the level floor (LevelPxH + 8), then jumps with the velocity that reaches its spawn cell
; (-(int)sqrt(96 * (homeY - py + 8))), gravity +3, flips when falling. Not a slot enemy; only star/hammer kill it.
;@entity PODOBOO codes=x
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE PODOBOO, podo_init, podo_update, podo_draw, podo_hit, ent_cb_none, 0

; per-type fields: v0 = homeY (px), v1 = wait, v2 = jumpVel
.a16
.i16

podo_init:
    ENT_ENTER
    ; homeY = LevelPxH + 8; Y = homeY << 4
    lda area_h
    ENT_ASL4
    clc
    adc #8
    sta ent_v0,x
    ENT_ASL4
    sta ent_y,x
    lda ent_fl,x
    and #$FFFF^(F_STOMP|F_SLOT)
    ora #(F_FIREIMM|F_TAILIMM|F_SHELLIMM)
    sta ent_fl,x
    ; wait = 30 + (px * 13) % 90
    jsl ent_px
    sta es0
    asl a
    clc
    adc es0                     ; *3
    asl a
    asl a                       ; *12
    clc
    adc es0                     ; *13
    ldy #90
    jsl ent_mod
    clc
    adc #30
    sta ent_v1,x
    lda #3
    sta ent_hbx,x
    lda #2
    sta ent_hby,x
    lda #10
    sta ent_hbw,x
    lda #12
    sta ent_hbh,x               ; hitbox 3,2,10,12
    ; jumpVel = -(int)Math.Sqrt(2 * 3 * ((homeY - py) + 8) * 16.0) = -isqrt(96 * n)
    lda ent_v0,x
    sec
    sbc ent_new_y
    clc
    adc #8
    sta es0                     ; n
    asl a
    clc
    adc es0                     ; 3n (n < 21845)
    sta e_t0
    stz e_t1
    ldy #5                      ; * 32
:   asl e_t0
    rol e_t1
    dey
    bne :-
    jsl ent_isqrt
    eor #$FFFF
    inc a
    sta ent_v2,x
    sec
    rtl

podo_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   lda ent_v1,x
    beq @fly
    dec a
    sta ent_v1,x
    bne @r
    lda ent_v2,x
    sta ent_yvel,x
    ENT_SFX "LAVA"
@r: rtl
@fly:
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #3
    sta ent_yvel,x
    ; if (Py >= homeY && YVel > 0) { Y = homeY << 4; wait = 110; }
    beq @r
    bmi @r
    jsl ent_py
    sec
    sbc ent_v0,x
    bmi @r
    lda ent_v0,x
    ENT_ASL4
    sta ent_y,x
    lda #110
    sta ent_v1,x
    rtl

podo_hit:
    ENT_ENTER
    lda ent_dmg
    cmp #D_STAR
    beq @k
    cmp #D_HAMMER
    beq @k
    clc
    rtl
@k: lda ent_dir
    jsl ent_knock_off
    sec
    rtl

podo_draw:
    ENT_ENTER
    lda ent_v1,x
    bne @r
    lda ent_yvel,x
    beq :+
    bmi :+
    lda spr_arg_flags
    ora #SPR_VFLIP
    sta spr_arg_flags
:   ENT_DRAW SPR_PODOBOO
@r: rtl
