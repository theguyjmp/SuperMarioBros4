; Menus (screens agent): options (the SNES-side settings: infinite-lives assist), how to play (C# HelpScreen, with
; the SNES controls) and the debug level select (Select on the title).
.p816
.smart
.include "scr.inc"

.export opt_enter, opt_tick, help_enter, help_tick, lsel_enter, lsel_tick, opt_back, lsel_active
.import scr_lvl, scr_lvl_kind, scr_lvl_info, sv_opts_save

.segment "BSS"
opt_back: .res 2               ; screen to return to (SC_TITLE / SC_MAP)
mn_sel: .res 2
mn_page: .res 2
mn_tmp: .res 6
lsel_active: .res 2
ls_x: .res 2*64
ls_y: .res 2*64

.segment "CODE11"
.a16
.i16

lattice:
    lda scr_t
    lsr a
    and #31
    sta mn_tmp
    eor #$FFFF
    inc a
    and #$00FF
    sta scr_bg2x
    lda #8
    sec
    sbc mn_tmp
    and #$00FF
    sta scr_bg2y
    rts

cursor:
    ; X = y -> Hud.Cursor at (16, y)
    stx spr_y
    lda scr_t
    lsr a
    lsr a
    lsr a
    and #3
    beq :+
    lda #16
    bra :++
:   lda #17
:   sta spr_x
    lda #$30
    sta spr_fl
    lda #SP_CURSOR
    jsl scr_obj_put
    rts

; ================================================================== options
OPT_N = 2

opt_enter:
    php
    rep #$30
    lda #SCN_OPTIONS
    jsl scr_scene_load
    stz mn_sel
    lda opt_back
    bne :+
    lda #SC_TITLE
    sta opt_back
:   jsr opt_text
    jsl txt_flush_now
    plp
    rtl

opt_tick:
    php
    rep #$30
    jsl scr_navv
    beq @nonav
    clc
    adc mn_sel
    bpl :+
    lda #OPT_N-1
:   cmp #OPT_N
    bcc :+
    lda #0
:   sta mn_sel
    SFX SFX_MENUMOVE
    jsr opt_text
@nonav: ; change (left/right or A on a value), back
    lda mn_sel
    bne @back
    jsl scr_navh
    bne @toggle
    lda scr_pressed
    and #KEY_OK
    beq @nob
@toggle:
    lda sv_opt_inf
    eor #1
    sta sv_opt_inf
    jsl sv_opts_save
    SFX SFX_MENUSELECT
    jsr opt_text
    bra @nob
@back:
    lda scr_pressed
    and #KEY_OK|PAD_START
    bne @leave
@nob:
    lda scr_pressed
    and #KEY_BACK
    beq @d
@leave:
    SFX SFX_MENUBACK
    lda opt_back
    stz opt_back
    jsl scr_go_now
    plp
    rtl
@d: jsr lattice
    jsl scr_obj_begin
    lda mn_sel
    asl a
    asl a
    sta mn_tmp
    asl a
    clc
    adc mn_tmp
    adc #28
    tax
    jsr cursor
    plp
    rtl

opt_text:
    jsl txt_clear_all
    PRINTC 10, TXP_OPTIONS_GOLD, "OPTIONS"
    ; line 0: INFINITE LIVES   ON/OFF
    lda #TXP_OPTIONS_GREY
    ldx mn_sel
    bne :+
    lda #TXP_OPTIONS_WHITE
:   sta txt_pal
    jsl sb_reset
    SB "INFINITE LIVES"
    lda #28
    sta txt_x
    sta txt_y
    jsl txt_print_sb
    jsl sb_reset
    lda sv_opt_inf
    bne :+
    SB "OFF"
    bra :++
:   SB "ON"
:   lda sb_len
    asl a
    asl a
    asl a
    eor #$FFFF
    sec
    adc #236
    sta txt_x
    lda #28
    sta txt_y
    lda #TXP_OPTIONS_GREY
    ldx mn_sel
    bne :+
    lda #TXP_OPTIONS_GOLD
:   sta txt_pal
    jsl txt_print_sb
    ; line 1: BACK
    lda #TXP_OPTIONS_GREY
    ldx mn_sel
    beq :+
    lda #TXP_OPTIONS_WHITE
:   sta txt_pal
    jsl sb_reset
    SB "BACK"
    lda #28
    sta txt_x
    lda #40
    sta txt_y
    jsl txt_print_sb
    ; help line
    lda mn_sel
    bne :+
    PRINT 20, 190, TXP_OPTIONS_BLUE, "ASSIST: NEVER RUN OUT OF LIVES."
:   rts

; ================================================================== how to play (C# HelpScreen)
PAGES = 4
Y0 = 28                         ; C# 36 - 8

help_enter:
    php
    rep #$30
    lda #SCN_HELP
    jsl scr_scene_load
    stz mn_page
    jsr help_text
    jsl txt_flush_now
    plp
    rtl

help_tick:
    php
    rep #$30
    lda scr_pressed
    and #PAD_RIGHT|KEY_OK
    beq @nr
    lda mn_page
    cmp #PAGES-1
    bcs @done
    inc mn_page
    SFX SFX_MENUMOVE
    jsr help_text
    bra @nr
@done:
    SFX SFX_MENUSELECT
    bra @leave
@nr:
    lda scr_pressed
    and #PAD_LEFT
    beq :+
    lda mn_page
    beq :+
    dec mn_page
    SFX SFX_MENUMOVE
    jsr help_text
:   lda scr_pressed
    and #KEY_BACK|PAD_START
    beq @draw
    SFX SFX_MENUBACK
@leave:
    lda #SC_TITLE
    jsl scr_go_now
    plp
    rtl
@draw:
    jsr lattice
    ; footer blink
    lda scr_t
    DIVC 20
    and #1
    cmp mn_tmp+4
    beq :+
    sta mn_tmp+4
    jsr footer
:   jsl scr_obj_begin
    lda mn_page
    cmp #2
    bne @r
    OBJ SP_H_MUSHROOM, #20, #Y0, #$30
    OBJ SP_H_FLOWER, #20, #Y0+20, #$30
    OBJ SP_H_LEAF, #20, #Y0+48, #$30
    OBJ SP_H_STAR, #20, #Y0+92, #$30
@r: plp
    rtl

footer:
    lda #16
    sta txt_x
    lda #206
    sta txt_y
    lda #232
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
    jsl sb_reset
    lda mn_page
    inc a
    jsl sb_dec
    SB "/4"
    lda #208
    sta txt_x
    lda #TXP_HELP_GREY
    sta txt_pal
    jsl txt_print_sb
    lda #TXP_HELP_WHITE
    ldx mn_tmp+4
    beq :+
    lda #TXP_HELP_GREY
:   sta txt_pal
    jsl sb_reset
    lda mn_page
    cmp #PAGES-1
    bcs :+
    SB "A/RIGHT: NEXT"
    bra :++
:   SB "A: DONE"
:   lda #20
    sta txt_x
    jsl txt_print_sb
    rts

help_text:
    jsl txt_clear_all
    lda mn_page
    asl a
    tax
    jsr (page_tab,x)
    jmp footer
page_tab: .addr page0, page1, page2, page3

.macro L yy, pal, text
    PRINT 20, Y0+yy, pal, text
.endmacro
.macro ROW yy, t1, t2
    PRINT 20, Y0+yy, TXP_HELP_WHITE, t1
    PRINT 132, Y0+yy, TXP_HELP_GOLD, t2
.endmacro

page0:
    PRINTC 10, TXP_HELP_GOLD, "CONTROLS"
    PRINT 132, Y0, TXP_HELP_GREEN, "SNES PAD"
    ROW 14, "MOVE", "D-PAD"
    ROW 26, "JUMP", "B OR A"
    ROW 38, "RUN/FIRE", "Y OR X"
    ROW 50, "PAUSE", "START"
    ROW 62, "ITEMS", "SELECT"
    L 80, TXP_HELP_GREY, "ON THE MAP: B OR A ENTERS"
    L 94, TXP_HELP_GREY, "A LEVEL, Y OR SELECT OPENS"
    L 106, TXP_HELP_GREY, "THE ITEMS, START THE MENU."
    L 124, TXP_HELP_BLUE, "TWO PLAYERS TAKE TURNS;"
    L 136, TXP_HELP_BLUE, "PAD 2 CAN PLAY LUIGI TOO."
    L 154, TXP_HELP_GREEN, "YOUR PROGRESS IS SAVED"
    L 166, TXP_HELP_GREEN, "ON THE CARTRIDGE."
    rts
page1:
    PRINTC 10, TXP_HELP_GOLD, "MOVES"
    L 0, TXP_HELP_WHITE, "HOLD RUN TO GO FASTER. RUN"
    L 11, TXP_HELP_WHITE, "FOR A WHILE TO FILL THE"
    L 22, TXP_HELP_WHITE, "P-METER FOR P-SPEED."
    L 33, TXP_HELP_WHITE, "HOLD JUMP TO JUMP HIGHER;"
    L 44, TXP_HELP_WHITE, "TAP IT FOR A SHORT HOP."
    L 55, TXP_HELP_WHITE, "PRESS THE OTHER WAY TO SKID"
    L 66, TXP_HELP_WHITE, "AND TURN AROUND QUICKLY."
    L 77, TXP_HELP_WHITE, "STOMP ENEMIES - HOLD JUMP"
    L 88, TXP_HELP_WHITE, "TO BOUNCE HIGHER."
    L 99, TXP_HELP_WHITE, "HOLD RUN TO PICK UP SHELLS,"
    L 110, TXP_HELP_WHITE, "LET GO TO KICK THEM."
    L 121, TXP_HELP_WHITE, "DOWN: DUCK, SLIDE DOWN"
    L 132, TXP_HELP_WHITE, "SLOPES AND ENTER PIPES."
    L 143, TXP_HELP_WHITE, "UP: DOORS AND VINES."
    L 154, TXP_HELP_WHITE, "IN WATER, TAP JUMP TO SWIM;"
    L 165, TXP_HELP_WHITE, "UP+JUMP LEAPS OUT OF IT."
    rts
page2:
    PRINTC 10, TXP_HELP_GOLD, "POWER-UPS"
    PRINT 40, Y0+4, TXP_HELP_WHITE, "SUPER MUSHROOM: GROW BIG."
    PRINT 40, Y0+20, TXP_HELP_WHITE, "FIRE FLOWER: THE RUN"
    PRINT 40, Y0+30, TXP_HELP_GREY, "BUTTON THROWS FIREBALLS."
    PRINT 40, Y0+46, TXP_HELP_WHITE, "SUPER LEAF: RUN BUTTON"
    PRINT 40, Y0+56, TXP_HELP_GREY, "SPINS YOUR TAIL. TAP JUMP"
    PRINT 40, Y0+66, TXP_HELP_GREY, "IN THE AIR TO FLOAT. WITH"
    PRINT 40, Y0+76, TXP_HELP_GREY, "A FULL P-METER, FLY!"
    PRINT 40, Y0+96, TXP_HELP_WHITE, "STARMAN: INVINCIBLE!"
    L 116, TXP_HELP_BLUE, "GET HIT AS A SUIT AND YOU"
    L 126, TXP_HELP_BLUE, "DROP BACK TO SUPER MARIO."
    L 142, TXP_HELP_GREEN, "GRAB THE GOAL CARD AT THE"
    L 152, TXP_HELP_GREEN, "END. 3 CARDS = BONUS LIVES!"
    L 166, TXP_HELP_GREEN, "100 COINS = 1UP."
    rts
page3:
    PRINTC 10, TXP_HELP_GOLD, "THE WORLD MAP"
    L 0, TXP_HELP_WHITE, "WALK THE PATHS WITH THE"
    L 10, TXP_HELP_WHITE, "D-PAD; PRESS JUMP TO ENTER."
    L 20, TXP_HELP_WHITE, "CLEAR A LEVEL TO PASS IT."
    L 40, TXP_HELP_WHITE, "TOAD HOUSES GIVE ITEMS. USE"
    L 50, TXP_HELP_WHITE, "THEM WITH RUN OR SELECT."
    L 60, TXP_HELP_WHITE, "FORTRESSES OPEN LOCKS, AND"
    L 70, TXP_HELP_WHITE, "EACH AIRSHIP HIDES A"
    L 80, TXP_HELP_WHITE, "KOOPALING."
    L 100, TXP_HELP_WHITE, "BEAT A WANDERING HAMMER BRO"
    L 110, TXP_HELP_WHITE, "FOR A SPECIAL PRIZE. EVERY"
    L 120, TXP_HELP_WHITE, "80,000 POINTS, AN N-SPADE"
    L 130, TXP_HELP_WHITE, "CARD GAME APPEARS."
    L 150, TXP_HELP_WHITE, "START: SAVE, OPTIONS AND"
    L 160, TXP_HELP_WHITE, "WORLD SELECT."
    rts

; ================================================================== debug level select (Select on the title)
lsel_enter:
    php
    rep #$30
    lda #SCN_LSEL
    jsl scr_scene_load
    stz lsel_active
    ; grid: row = world (hb last), column = index inside the world
    stz mn_tmp                  ; level
    stz mn_tmp+2                ; column
    lda #$FFFF
    sta mn_tmp+4                ; previous world char
@l: lda mn_tmp
    cmp #SCR_NLEVELS
    bcs @ld
    jsr lvl_world               ; A = row 0..8
    cmp mn_tmp+4
    beq :+
    sta mn_tmp+4
    stz mn_tmp+2
:   lda mn_tmp
    asl a
    tax
    lda mn_tmp+4
    asl a
    sta ls_y,x
    asl a
    asl a
    asl a
    clc
    adc ls_y,x                  ; row * 18
    adc #30
    sta ls_y,x
    lda mn_tmp+2
    asl a
    asl a
    sta ls_x,x
    asl a
    asl a
    asl a
    sec
    sbc ls_x,x                  ; col * 28
    clc
    adc mn_tmp+2
    adc mn_tmp+2                ; col * 30
    clc
    adc #40
    sta ls_x,x
    inc mn_tmp+2
    inc mn_tmp
    bra @l
@ld:
    PRINTC 10, TXP_LSEL_GOLD, "LEVEL SELECT"
    PRINTC 196, TXP_LSEL_GREY, "A: PLAY   Y: BACK"
    stz mn_tmp
@t: lda mn_tmp
    cmp #SCR_NLEVELS
    bcs @td
    jsr lsel_entry
    inc mn_tmp
    bra @t
@td:
    jsl txt_flush_now
    lda #SONG_SELECT
    ldx #0
    jsl scr_music
    plp
    rtl

; A = level -> A = row (world 1..8 -> 0..7, hb -> 8)
lvl_world:
    asl a
    asl a
    tax
    lda f:scr_lvl_info,x
    tax
    phb
    sep #$20
    .a8
    lda #^scr_lvl_info
    pha
    plb
    rep #$20
    .a16
    lda a:0,x
    plb
    and #$00FF
    sec
    sbc #'1'
    cmp #8
    bcc :+
    lda #8
:   rts

; mn_tmp = level: print its id (white when selected)
lsel_entry:
    lda mn_tmp
    asl a
    tax
    lda ls_x,x
    sta txt_x
    lda ls_y,x
    sta txt_y
    lda #TXP_LSEL_GREY
    ldx mn_tmp
    cpx scr_lvl
    bne :+
    lda #TXP_LSEL_WHITE
:   sta txt_pal
    jsl sb_reset
    lda mn_tmp
    asl a
    asl a
    tax
    lda f:scr_lvl_info,x
    tax
    phb
    lda #0
    sep #$20
    .a8
    lda #^scr_lvl_info
    pha
    plb
    rep #$20
    .a16
@c: lda a:0,x
    and #$00FF
    beq @d
    phx
    jsl sb_char
    plx
    inx
    bra @c
@d: plb
    jsl txt_print_sb
    rts

lsel_tick:
    php
    rep #$30
    lda scr_lvl
    sta mn_tmp+4
    jsl scr_navh
    beq :+
    clc
    adc scr_lvl
    bmi :+
    cmp #SCR_NLEVELS
    bcs :+
    sta scr_lvl
:   jsl scr_navv
    beq @nv
    ; the next/previous row: the entry with the same column (or the row's last)
    sta mn_tmp+2
    lda scr_lvl
    asl a
    tax
    lda ls_x,x
    sta mn_tmp
    lda ls_y,x
    ldx mn_tmp+2
    bmi :+
    clc
    adc #18
    bra :++
:   sec
    sbc #18
:   sta mn_tmp+2                ; target y
    ldx #0
    ldy #$FFFF
@f: cpx #SCR_NLEVELS*2
    bcs @fd
    lda ls_y,x
    cmp mn_tmp+2
    bne :+
    txa
    lsr a
    tay
    lda ls_x,x
    cmp mn_tmp
    beq @fd
:   inx
    inx
    bra @f
@fd: cpy #$FFFF
    beq @nv
    sty scr_lvl
@nv:
    lda scr_lvl
    cmp mn_tmp+4
    beq :+
    SFX SFX_MENUMOVE
    lda mn_tmp+4
    sta mn_tmp
    jsr lsel_entry
    lda scr_lvl
    sta mn_tmp
    jsr lsel_entry
:   lda scr_pressed
    and #KEY_OK|PAD_START
    beq @nb
    ; a throwaway session (not saved) unless a file is loaded
    lda sv_exists
    and #$00FF
    bne :+
    lda #0
    jsl sv_new
    stz ss_player
    stz ss_cur
:   lda #1
    sta lsel_active
    stz scr_lvl_kind
    SFX SFX_MAPENTER
    lda #SC_LEVEL
    jsl scr_go
    bra @draw
@nb:
    lda scr_pressed
    and #KEY_BACK
    beq @draw
    SFX SFX_MENUBACK
    lda #SC_TITLE
    jsl scr_go
@draw:
    jsr lattice
    jsl scr_obj_begin
    lda scr_lvl
    asl a
    tax
    lda ls_y,x
    pha
    lda ls_x,x
    sec
    sbc #12
    sta spr_x
    pla
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_CURSOR
    jsl scr_obj_put
    plp
    rtl
