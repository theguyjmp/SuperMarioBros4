; Roto-disc (port of C# RotoDisc, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Orbits its spawn cell at radius 40 px, one turn per 150 ticks; clockwise on even columns, counter-clockwise on odd.
; Immune to everything, not a slot enemy. Palette alternates every 3 frames.
; The orbit offsets are the C# values (int)(cos/sin(a) * 40) for a = n * 2pi/150 accumulated in doubles (first turn);
; the C# angle keeps accumulating rounding error, so on later turns 4 of the 150 positions (cos = +-0.5, x = +-20 vs 19)
; can differ by 1 px in X from this table.
;@entity ROTO_DISC codes=R
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE ROTO_DISC, roto_init, roto_update, roto_draw, ent_cb_none, ent_cb_none, 0

; per-type fields: v0 = cx, v1 = cy (px), v2 = direction (+1 / -1), ent_t = angle step 0-149
.a16
.i16

roto_init:
    ENT_ENTER
    jsl ent_px
    sta ent_v0,x
    and #16                     ; spawn column parity: (s.X & 1) == 0 ? 1 : -1
    beq :+
    lda #$FFFF
    bra :++
:   lda #1
:   sta ent_v2,x
    jsl ent_py
    sta ent_v1,x
    lda ent_fl,x
    and #$FFFF^(F_STOMP|F_SLOT)
    ora #(F_FIREIMM|F_TAILIMM|F_SHELLIMM|F_STARIMM|F_HAMMERIMM)
    sta ent_fl,x
    lda #3
    sta ent_hbx,x
    sta ent_hby,x
    lda #10
    sta ent_hbw,x
    sta ent_hbh,x               ; hitbox 3,3,10,10
    sec
    rtl

; A = signed byte from the table at index Y -> sign-extended
.macro TBL_S8 tbl
    tyx
    lda f:tbl,x
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:
.endmacro

roto_update:
    ENT_ENTER
    ldy ent_t,x
    phx
    TBL_S8 roto_cos
    plx
    clc
    adc ent_v0,x
    ENT_ASL4
    sta ent_x,x
    phx
    TBL_S8 roto_sin
    plx
    ldy ent_v2,x
    bpl :+
    eor #$FFFF
    inc a
:   clc
    adc ent_v1,x
    ENT_ASL4
    sta ent_y,x
    lda ent_t,x
    inc a
    cmp #150
    bcc :+
    lda #0
:   sta ent_t,x
    rtl

roto_draw:
    ENT_ENTER
    lda w_frame
    ldy #3
    jsl ent_div
    and #1
    bne :+
    ENT_DRAW SPR_ROTODISC
    rtl
:   ENT_DRAW SPR_ROTODISC_2
    rtl

roto_cos:
    .byte 39,39,39,39,39,38,38,37,37,36,35,35,34,33,32
    .byte 31,30,29,27,26,25,24,22,21,20,18,17,15,13,12
    .byte 10,9,7,5,4,2,0,0,$FE,$FC,$FB,$F9,$F7,$F6,$F4
    .byte $F3,$F1,$EF,$EE,$EC,$EB,$EA,$E8,$E7,$E6,$E5,$E3,$E2,$E1,$E0
    .byte $DF,$DE,$DD,$DD,$DC,$DB,$DB,$DA,$DA,$D9,$D9,$D9,$D9,$D9,$D8
    .byte $D9,$D9,$D9,$D9,$D9,$DA,$DA,$DB,$DB,$DC,$DD,$DD,$DE,$DF,$E0
    .byte $E1,$E2,$E3,$E5,$E6,$E7,$E8,$EA,$EB,$ED,$EE,$EF,$F1,$F3,$F4
    .byte $F6,$F7,$F9,$FB,$FC,$FE,0,0,2,4,5,7,9,10,12
    .byte 13,15,17,18,20,21,22,24,25,26,27,29,30,31,32
    .byte 33,34,35,35,36,37,37,38,38,39,39,39,39,39,40
roto_sin:
    .byte 1,3,5,6,8,9,11,13,14,16,17,19,20,22,23
    .byte 24,26,27,28,29,30,31,32,33,34,35,36,36,37,38
    .byte 38,38,39,39,39,39,39,39,39,39,39,39,38,38,38
    .byte 37,36,36,35,34,33,32,31,30,29,28,27,26,24,23
    .byte 22,20,19,17,16,14,13,11,9,8,6,5,3,1,0
    .byte $FF,$FD,$FB,$FA,$F8,$F7,$F5,$F3,$F2,$F0,$EF,$ED,$EC,$EA,$E9
    .byte $E8,$E6,$E5,$E4,$E3,$E2,$E1,$E0,$DF,$DE,$DD,$DC,$DC,$DB,$DA
    .byte $DA,$DA,$D9,$D9,$D9,$D9,$D9,$D9,$D9,$D9,$D9,$D9,$DA,$DA,$DA
    .byte $DB,$DC,$DC,$DD,$DE,$DF,$E0,$E1,$E2,$E3,$E4,$E5,$E6,$E8,$E9
    .byte $EA,$EC,$ED,$EF,$F0,$F2,$F3,$F5,$F7,$F8,$FA,$FB,$FD,$FF,0
