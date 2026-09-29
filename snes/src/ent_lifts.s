; Platforms (port of C# MovingLift / DonutLift). Owner: engine agent. Reference example for platforms:
; update = ent_plat_begin, move, ent_plat_end (carries/lands the player and keeps F_PON for PlatformSupport).
; Spawn chars: _ horizontal lift, : vertical lift (48 px wide, sine motion), - donut lift (falls when stood on).
;@entity LIFT codes=_:
;@entity DONUT_LIFT codes=-
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE LIFT, lift_init, lift_update, lift_draw, ent_cb_none, ent_cb_none, ent_cb_none
ENT_VTABLE DONUT_LIFT, donut_init, donut_update, donut_draw, ent_cb_none, ent_cb_none, ent_cb_none

.a16
.i16

; Platform(widthPx = A)
plat_setup:
    sta ent_wd,x
    sta ent_hbw,x
    lda #8
    sta ent_ht,x
    sta ent_hbh,x
    stz ent_hbx,x
    stz ent_hby,x
    lda #0
    sta ent_fl,x
    lda #EC_PLATFORM
    sta ent_class,x
    rts

; MovingLift: state = vertical, v0 = t, v1 = ox, v2 = oy
lift_init:
    ENT_ENTER
    lda ent_arg,x
    cmp #':'
    bne :+
    lda #1
    sta ent_state,x
:   lda #48
    jsr plat_setup
    lda ent_x,x
    sta ent_v1,x
    lda ent_y,x
    sta ent_v2,x
    sec
    rtl

lift_update:
    ENT_ENTER
    jsl ent_plat_begin
    ; t++ ; s = sin(t*2pi/240) ; vertical: Y = oy + s*48*16 ; else X = ox + s*56*16
    inc ent_v0,x
    lda ent_v0,x
    ldy #240
    jsl ent_mod
    asl a
    phx
    tax
    lda f:eng_tables+360,x      ; sin * 48*16
    sta es0
    lda f:eng_tables+1320,x     ; sin * 56*16
    plx
    ldy ent_state,x
    beq @hz
    lda es0
    clc
    adc ent_v2,x
    sta ent_y,x
    bra @moved
@hz: clc
    adc ent_v1,x
    sta ent_x,x
@moved:
    jsl ent_plat_end
    rtl

lift_draw:
    ENT_ENTER
    ENT_DRAW SPR_SEMI_L
    lda #16
    ldy #0
    jsl ent_draw_offset
    ENT_DRAW SPR_SEMI_C
    lda #16
    ldy #0
    jsl ent_draw_offset
    ENT_DRAW SPR_SEMI_R
    rtl

; DonutLift: v0 = stoodFor, v1 = fall
donut_init:
    ENT_ENTER
    lda #16
    jsr plat_setup
    sec
    rtl

donut_update:
    ENT_ENTER
    jsl ent_plat_begin
    ; if playerOn stoodFor++ else if fall == 0 stoodFor = max(0, stoodFor - 1)
    lda ent_fl,x
    and #F_PON
    beq :+
    inc ent_v0,x
    bra @dn2
:   lda ent_v1,x
    bne @dn2
    lda ent_v0,x
    beq @dn2
    dec ent_v0,x
@dn2:
    lda ent_v1,x
    bne @falling
    lda ent_v0,x
    cmp #31
    bcc @moved
    lda #1
    sta ent_v1,x
@falling:
    inc ent_v1,x
    lda ent_yvel,x
    clc
    adc #2
    cmp #$31
    bcc :+
    lda #$30
:   sta ent_yvel,x
    clc
    adc ent_y,x
    sta ent_y,x
    lda cam_y
    clc
    adc #260
    sta es0
    jsl ent_py
    sec
    sbc es0
    bmi @moved
    beq @moved
    jsl ent_remove
@moved:
    jsl ent_plat_end
    rtl

donut_draw:
    ENT_ENTER
    ; shake while about to fall
    lda ent_v0,x
    cmp #11
    bcc :+
    lda ent_v1,x
    bne :+
    lda w_frame
    lsr a
    and #1
    ldy #0
    jsl ent_draw_offset
:   ENT_DRAW SPR_BUMP_USED
    rtl
