; Effects (port of C# Effects.cs + BumpBlock/CoinPop/CardFly from Items.cs). Owner: engine agent.
; All are EC_EFFECT (drawn last, keep animating during the course clear) except FX_BUMP (EC_SPECIAL).
; Engine helpers spawn them: ent_puff/ent_puff_at, ent_sparkle_at, ent_popup, ent_add_* (eng_obj.s).
;@entity FX_PUFF
;@entity FX_SPARKLE
;@entity FX_DUST
;@entity FX_SPLASH
;@entity FX_DEBRIS
;@entity FX_POPUP
;@entity FX_COINPOP
;@entity FX_BUMP
;@entity FX_CARDFLY
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
; arg = 0 big puff / 1 small (sparkle frames)
ENT_VTABLE FX_PUFF, fx_init, puff_update, puff_draw, ent_cb_none, ent_cb_none, ent_cb_none
ENT_VTABLE FX_SPARKLE, fx_init, sparkle_update, sparkle_draw, ent_cb_none, ent_cb_none, ent_cb_none
ENT_VTABLE FX_DUST, fx_init, dust_update, dust_draw, ent_cb_none, ent_cb_none, ent_cb_none
ENT_VTABLE FX_SPLASH, fx_init, splash_update, splash_draw, ent_cb_none, ent_cb_none, ent_cb_none
; set ent_xvel/ent_yvel after spawning (Y = slot)
ENT_VTABLE FX_DEBRIS, fx_init, debris_update, debris_draw, ent_cb_none, ent_cb_none, ent_cb_none
; arg = BCD points or $FFFF = "1UP"
ENT_VTABLE FX_POPUP, fx_init, popup_update, popup_draw, ent_cb_none, ent_cb_none, ent_cb_none
ENT_VTABLE FX_COINPOP, coinpop_init, coinpop_update, coinpop_draw, ent_cb_none, ent_cb_none, ent_cb_none
; engine-driven (ent_add_bump): v0 tx, v1 ty, v2 dir, v3 final tile
ENT_VTABLE FX_BUMP, bump_init, bump_update, bump_draw, ent_cb_none, ent_cb_none, ent_cb_none
; arg = card 0-2
ENT_VTABLE FX_CARDFLY, fx_init, cardfly_update, cardfly_draw, ent_cb_none, ent_cb_none, ent_cb_none

.a16
.i16

; common: Class = Effect, Hurts = false, UsesSlot = false; v0 = arg
fx_init:
    ENT_ENTER
    lda #EC_EFFECT
    sta ent_class,x
    lda #0
    sta ent_fl,x
    lda ent_arg,x
    sta ent_v0,x
    sec
    rtl

; t++ ; t >= A -> Remove
.macro LIFE n
    inc ent_t,x
    lda ent_t,x
    cmp #n
    bcc :+
    jsl ent_remove
:
.endmacro

puff_update:
    ENT_ENTER
    LIFE 18
    rtl
puff_draw:
    ENT_ENTER
    lda ent_v0,x
    bne @small
    lda ent_t,x
    cmp #12
    bcs @3
    cmp #6
    bcs @2
    ENT_DRAW SPR_PUFF_1
    rtl
@2: ENT_DRAW SPR_PUFF_2
    rtl
@3: ENT_DRAW SPR_PUFF_3
    rtl
@small:
    lda ent_t,x
    and #4
    bne :+
    ENT_DRAW SPR_SPARKLE_1
    rtl
:   ENT_DRAW SPR_SPARKLE_2
    rtl

sparkle_update:
    ENT_ENTER
    LIFE 12
    rtl
sparkle_draw:
    ENT_ENTER
    lda ent_t,x
    ldy #3
    jsl ent_div
    and #1
    bne :+
    ENT_DRAW SPR_SPARKLE_1
    rtl
:   ENT_DRAW SPR_SPARKLE_2
    rtl

splash_update:
    ENT_ENTER
    LIFE 16
    rtl
splash_draw:
    ENT_ENTER
    lda ent_t,x
    cmp #8
    bcs :+
    ENT_DRAW SPR_SPLASH_1
    rtl
:   ENT_DRAW SPR_SPLASH_2
    rtl

dust_update:
    ENT_ENTER
    lda ent_y,x
    sec
    sbc #4
    sta ent_y,x
    LIFE 12
    rtl
dust_draw:
    ENT_ENTER
    lda ent_t,x
    cmp #6
    bcc :+
    and #2
    bne @r
:   ENT_DRAW SPR_DUST
@r: rtl

debris_update:
    ENT_ENTER
    inc ent_t,x
    jsl ent_apply_vel
    lda ent_yvel,x
    clc
    adc #4
    sta ent_yvel,x
    lda cam_y
    clc
    adc #250
    sta es0
    jsl ent_py
    sec
    sbc es0
    bmi :+
    beq :+
    jsl ent_remove
:   rtl
debris_draw:
    ENT_ENTER
    lda ent_t,x
    lsr a
    lsr a
    and #1
    sta spr_arg_flags
    lda ent_t,x
    lsr a
    lsr a
    and #2
    ora spr_arg_flags
    sta spr_arg_flags
    ENT_DRAW SPR_DEBRIS
    rtl

popup_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_t,x
    cmp #40
    bcs @n
    ldy #16
    cmp #20
    bcc :+
    ldy #8
:   sty es0
    lda ent_y,x
    sec
    sbc es0
    sta ent_y,x
@n: lda ent_t,x
    cmp #57
    bcc :+
    jsl ent_remove
:   rtl
popup_draw:
    ENT_ENTER
    lda ent_v0,x
    cmp #$FFFF
    bne @num
    ENT_DRAW SPR_TINY_1
    lda #5
    ldy #0
    jsl ent_draw_offset
    ENT_DRAW SPR_TINY_UP
    rtl
@num:
    ; BCD digits without leading zeros, 5 px apart
    sta es0
    lda #4
    sta es1
    stz es2                     ; started
@dg: lda es0
    xba
    lsr a
    lsr a
    lsr a
    lsr a
    and #15
    bne :+
    ldy es2
    bne :+
    lda es1
    cmp #1
    bne @skip
    lda #0
:   inc es2
    asl a
    phx
    tax
    lda f:tiny_ids,x
    plx
    cmp #$FFFF
    beq :+
    jsl ent_draw_meta
:   lda #5
    ldy #0
    jsl ent_draw_offset
@skip:
    lda es0
    ENT_ASL4
    sta es0
    dec es1
    bne @dg
    rtl
tiny_ids:
.ifdef SPR_TINY_0
    .word SPR_TINY_0, SPR_TINY_1, SPR_TINY_2, SPR_TINY_3, SPR_TINY_4, SPR_TINY_5, SPR_TINY_6, SPR_TINY_7, SPR_TINY_8, SPR_TINY_9
.else
    .word $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF, $FFFF
.endif

; CoinPop: YVel = -$50
coinpop_init:
    ENT_ENTER
    lda #EC_EFFECT
    sta ent_class,x
    lda #0
    sta ent_fl,x
    lda #$10000-$50
    sta ent_yvel,x
    sec
    rtl
coinpop_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #5
    sta ent_yvel,x
    lda ent_t,x
    cmp #27
    bcc :+
    jsl ent_remove
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #$0100
    jsl ent_popup               ; ScorePopup 100 (the score itself was added with the coin)
:   rtl
coinpop_draw:
    ENT_ENTER
    lda ent_t,x
    ldy #3
    jsl ent_div
    and #3
    beq @1
    cmp #1
    beq @2
    cmp #2
    beq @3
    ENT_DRAW SPR_COIN_4
    rtl
@1: ENT_DRAW SPR_COIN_1
    rtl
@2: ENT_DRAW SPR_COIN_2
    rtl
@3: ENT_DRAW SPR_COIN_3
    rtl

; BumpBlock
bump_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda #0
    sta ent_fl,x
    sec
    rtl
bump_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_t,x
    cmp #8
    bcc :+
    jsl ent_remove
    lda ent_v0,x
    sta e_tx
    lda ent_v1,x
    sta e_ty
    phx
    jsl eng_show_tile
    plx
:   rtl
bump_draw:
    ENT_ENTER
    ; tile image drawn as a sprite, offset by the bump curve: (t < 4 ? t : 8 - t) * 2 * dir
    lda ent_t,x
    cmp #4
    bcc :+
    eor #$FFFF
    clc
    adc #9                      ; 8 - t
:   asl a
    ldy ent_v2,x
    bpl :+
    NEG16
:   tay
    lda #0
    jsl ent_draw_offset
    lda ent_v3,x
    cmp #T_BRICK
    bne :+
    ENT_DRAW SPR_BUMP_BRICK
    rtl
:   cmp #T_NOTE
    bne :+
    ENT_DRAW SPR_BUMP_NOTE
    rtl
:   cmp #T_WOOD
    bne :+
    ENT_DRAW SPR_BUMP_WOOD
    rtl
:   ENT_DRAW SPR_BUMP_USED
    rtl

; CardFly
cardfly_update:
    ENT_ENTER
    lda ent_y,x
    sec
    sbc #32
    sta ent_y,x
    LIFE 61
    rtl
cardfly_draw:
    ENT_ENTER
    lda ent_v0,x
    cmp #1
    beq :+
    cmp #2
    beq :++
    ENT_DRAW SPR_CARD_MUSHROOM
    rtl
:   ENT_DRAW SPR_CARD_FLOWER
    rtl
:   ENT_DRAW SPR_CARD_STAR
    rtl
