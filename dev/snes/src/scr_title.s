; Title screen (screens agent) — port of C# TitleScreen: theater curtain rising on the logo, footlights, flickering
; lanterns, the "4" sparkle, the Mario/Goomba attract vignette and the menu. SNES y = C# y - 8.
; BG1 = stage (pre-rendered), BG2 = curtain (32x64, scrolled up) then the menu highlight bar (scrolled per item),
; BG3 = text canvas, OBJ = actors (priority 2: the curtain covers them) + cursor (priority 3).
.p816
.smart
.include "scr.inc"

.export title_enter, title_tick, files_two

.segment "BSS"
ti_sel: .res 2
ti_cur: .res 2                 ; C# curtainY (0 .. -201)
ti_mx: .res 2
ti_my: .res 2                  ; 1/16 px
ti_mvy: .res 2
ti_gx: .res 2
ti_menu: .res 2                ; menu drawn
ti_lights: .res 2
ti_lp0: .res 2
ti_lp1: .res 2
files_two: .res 2              ; argument of the file select: 2-player game

.segment "CODE11"
.a16
.i16

ITEMS = 4
ITEM_Y = 100                   ; C# 108 - 8
ITEM_X = 76

title_enter:
    php
    rep #$30
    lda #SCN_TITLE
    jsl scr_scene_load
    stz ti_sel
    stz ti_cur
    stz ti_my
    stz ti_mvy
    stz ti_menu
    stz ti_lp0
    stz ti_lp1
    lda #$FFFF
    sta ti_lights
    lda #.loword(-24)
    sta ti_mx
    lda #290
    sta ti_gx
    PRINT 84, 92, TXP_TITLE_WHITE, "PRESS START"
    jsl txt_flush_now
    lda #SONG_TITLE
    ldx #1
    jsl scr_music
    jsr draw
    plp
    rtl

title_tick:
    php
    rep #$30
    ; curtain
    lda scr_t
    cmp #51
    bcc @nocur
    lda ti_cur
    cmp #.loword(-200)
    beq @nocur
    bmi @nocur
    sec
    sbc #3
    sta ti_cur
    lda scr_t
    cmp #51
    bne @nocur
    jsr clear_press
@nocur:
    jsr actors
    ; skip the curtain
    lda ti_cur
    cmp #.loword(-200)
    beq @menu
    bmi @menu
    lda scr_pressed
    and #PAD_START|KEY_OK
    jeq @draw
    lda #.loword(-200)
    sta ti_cur
    lda #60
    sta scr_t
    jsr clear_press
    bra @draw
@menu:
    lda ti_menu
    bne :+
    jsr show_menu
:   ; Select: the debug level select (hidden code)
    lda scr_pressed
    and #PAD_SELECT
    beq :+
    SFX SFX_MENUSELECT
    lda #SC_LVLSEL
    jsl scr_go
    bra @draw
:   jsl scr_navv
    beq @nov
    clc
    adc ti_sel
    bpl :+
    lda #ITEMS-1
:   cmp #ITEMS
    bcc :+
    lda #0
:   pha
    lda ti_sel
    sta scr_tmp0
    pla
    sta ti_sel
    SFX SFX_MENUMOVE
    jsr put_menu
    lda scr_tmp0
    jsr draw_item
    lda ti_sel
    jsr draw_item
@nov:
    lda scr_pressed
    and #PAD_START|KEY_OK
    beq @draw
    SFX SFX_MENUSELECT
    lda ti_sel
    asl a
    tax
    jsr (menu_act,x)
@draw:
    jsr draw
    plp
    rtl

menu_act: .addr act_1p, act_2p, act_help, act_opt
act_1p:
    stz files_two
    lda #SC_FILES
    jsl scr_go
    rts
act_2p:
    lda #1
    sta files_two
    lda #SC_FILES
    jsl scr_go
    rts
act_help:
    lda #SC_HELP
    jsl scr_go_now
    rts
act_opt:
    lda #SC_OPTIONS
    jsl scr_go_now
    rts

clear_press:
    lda #84
    sta txt_x
    lda #92
    sta txt_y
    lda #90
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
    rts

show_menu:
    lda #1
    sta ti_menu
    jsr put_menu
    lda #0
@l: pha
    jsr draw_item
    pla
    inc a
    cmp #ITEMS
    bcc @l
    lda ti_sel                  ; selected one last (its palette wins shared cells)
    jsr draw_item
    rts

; A = item: clear its line and print it (white when selected, the menu gradient otherwise)
draw_item:
    sta scr_tmp1
    lda #ITEM_X
    sta txt_x
    lda scr_tmp1
    asl a
    sta scr_tmp2
    asl a
    asl a
    clc
    adc scr_tmp2
    adc scr_tmp1                ; *11
    adc #ITEM_Y
    sta txt_y
    lda #120
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
    lda #TXP_TITLE_MENU
    ldx scr_tmp1
    cpx ti_sel
    bne :+
    lda #TXP_TITLE_WHITE
:   sta txt_pal
    jsl sb_reset
    lda scr_tmp1
    asl a
    tax
    lda f:item_txt,x
    sta txt_sp_tmp
    jsl sb_reset
    lda txt_sp_tmp
    tax
@c: lda f:item_str,x
    and #$00FF
    beq @d
    phx
    jsl sb_char
    plx
    inx
    bra @c
@d: jsl txt_print_sb
    rts
.segment "BSS"
txt_sp_tmp: .res 2
.segment "CODE11"
.a16
.i16
item_txt: .word item_1p-item_str, item_2p-item_str, item_help-item_str, item_opt-item_str
item_str:
item_1p: .byte "1 PLAYER GAME", 0
item_2p: .byte "2 PLAYER GAME", 0
item_help: .byte "HOW TO PLAY", 0
item_opt: .byte "OPTIONS", 0

; ------------------------------------------------------------------ attract actors (C# Tick)
actors:
    lda ti_mx
    clc
    adc #2
    sta ti_mx
    cmp #301
    bmi :+
    lda #.loword(-40)
    sta ti_mx
    lda #290
    sta ti_gx
:   dec ti_gx
    ; jump when the Goomba is near
    lda ti_my
    bne @nojump
    lda ti_mx
    sec
    sbc ti_gx
    jsl scr_abs
    cmp #79                     ; 26 air ticks x 3 px closing = feet meet the head on the way down
    bcs @nojump
    lda ti_mx
    cmp ti_gx
    bpl @nojump
    lda #.loword(-$3C)
    sta ti_mvy
@nojump:
    lda ti_my
    bmi @air
    lda ti_mvy
    bpl @stomp
@air:
    lda ti_my
    clc
    adc ti_mvy
    sta ti_my
    lda ti_mvy
    clc
    adc #4
    cmp #$41
    bmi :+
    cmp #$8000
    bcs :+
    lda #$40
:   sta ti_mvy
    lda ti_my
    bmi @stomp
    stz ti_my
    stz ti_mvy
@stomp:
    ; |mx - gx| < 12 && falling && feet (184 + my/16) at the Goomba head (~170) -> stomp
    lda ti_mx
    sec
    sbc ti_gx
    jsl scr_abs
    cmp #12
    bcs @r
    lda ti_my
    cmp #.loword(-240)
    bmi @r
    lda ti_mvy
    beq @r
    bmi @r
    lda #400
    sta ti_gx
    lda #.loword(-$30)
    sta ti_mvy
    SFX SFX_STOMP
@r: rts

; ------------------------------------------------------------------ per-frame drawing
draw:
    ; BG2: curtain scroll, or the highlight bar at the selected item
    lda ti_menu
    bne @bar
    lda ti_cur
    eor #$FFFF
    inc a
    sta scr_bg2y
    bra @lights
@bar:
    stz scr_bg2y
@lights:
    ; footlights: all lit when (t/10 + 2) % 3 == 0
    lda scr_t
    DIVC 10
    inc a
    inc a
    DIVC 3
    txa
    beq :+
    lda #1
:   cmp ti_lights
    beq @lant
    sta ti_lights
    cmp #0
    bne :+
    lda #MAP_TITLE_LIGHTS
    bra :++
:   lda #MAP_TITLE_LIGHTS_OFF
:   ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put
@lant:
    ; lantern flicker: glow palette when (t/8) % 3 == 0 (second lantern: t + 11)
    lda scr_t
    jsr glow
    cmp ti_lp0
    beq :+
    sta ti_lp0
    tax
    lda #SP_LANTERN
    jsl scr_obj_pal
:   lda scr_t
    clc
    adc #11
    jsr glow
    cmp ti_lp1
    beq :+
    sta ti_lp1
    tax
    lda #SP_LANTERN2
    jsl scr_obj_pal
:   ; sprites (painter's order: later = in front)
    jsl scr_obj_begin
    OBJ SP_LANTERN, #38, #70, #$20
    OBJ SP_LANTERN2, #202, #70, #$20
    ; sparkle on the 4: tw = (t/6) % 24 < 4 -> radius 1,2,2,1
    lda scr_t
    DIVC 6
    DIVC 24
    txa
    cmp #4
    bcs @nosp
    ldy #SP_SPARKLE1
    cmp #1
    beq :+
    cmp #2
    bne :++
:   ldy #SP_SPARKLE2
:   lda #193
    sta spr_x
    lda #42
    sta spr_y
    lda #$20
    sta spr_fl
    tya
    jsl scr_obj_put
@nosp:
    ; goomba
    lda ti_gx
    cmp #300
    bpl @nog
    sta spr_x
    lda #168
    sta spr_y
    lda #$20
    sta spr_fl
    lda scr_t
    and #8
    beq :+
    lda #SP_GOOMBA2
    bra :++
:   lda #SP_GOOMBA1
:   jsl scr_obj_put
@nog:
    ; mario
    lda ti_mx
    sta spr_x
    lda ti_my
    ASR1
    ASR1
    ASR1
    ASR1
    clc
    adc #152
    sta spr_y
    lda #$20
    sta spr_fl
    lda ti_my
    cmp #.loword(-16)
    bpl :+
    lda #SP_TMARIO_JUMP
    bra @m
:   lda scr_t
    lsr a
    lsr a
    and #3
    tax
    lda f:walk_cycle,x
    and #$00FF
@m: jsl scr_obj_put
    ; cursor
    lda ti_menu
    beq @nc
    lda scr_t
    lsr a
    lsr a
    lsr a
    and #3
    beq :+
    lda #0
    bra :++
:   lda #1
:   clc
    adc #64
    sta spr_x
    lda ti_sel
    asl a
    sta scr_tmp2
    asl a
    asl a
    clc
    adc scr_tmp2
    adc ti_sel
    adc #ITEM_Y
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_CURSOR
    jsl scr_obj_put
@nc:
    rts
walk_cycle: .byte SP_TMARIO_WALK1, SP_TMARIO_WALK2, SP_TMARIO_STAND, SP_TMARIO_WALK2

; A = t -> A = 1 when (t/8) % 3 == 0
glow:
    lsr a
    lsr a
    lsr a
    DIVC 3
    txa
    beq :+
    lda #0
    rts
:   lda #1
    rts

.segment "CODE11"
.a16
.i16
; BG2 = the menu panel with the highlight bar on the selected item
put_menu:
    lda ti_sel
    clc
    adc #MAP_TITLE_MENU0
    ldx #SCR_VRAM_BG2MAP
    jsl scr_map_put
    rts
