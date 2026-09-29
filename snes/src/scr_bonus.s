; Bonus rooms (screens agent): Toad house chests (C# ToadHouseScreen), the spade slot game (C# SpadeScreen) and the
; N-Spade card game (C# NSpadeScreen, every 80,000 points). All three show the compact status bar.
.p816
.smart
.include "scr.inc"

.export toad_enter, toad_tick, spade_enter, spade_tick, nspade_enter, nspade_tick
.import mp_node, map_toad_used, map_node_items, map_nspade_done, scr_hud_text, scr_hud_cards, sb_item

.segment "BSS"
bn_sel: .res 2
bn_open: .res 2                ; opened chest (-1)
bn_opent: .res 2
bn_items: .res 6               ; the 3 chests' items
bn_lp: .res 2
bn_pp: .res 2
bn_tmp: .res 8
; spade
sp_pos: .res 6
sp_row: .res 2
sp_done: .res 2
sp_prize: .res 2
sp_donet: .res 2
; n-spade
ns_cards: .res 18
ns_up: .res 18
ns_cur: .res 2
ns_first: .res 2
ns_second: .res 2
ns_showt: .res 2
ns_miss: .res 2
ns_found: .res 2
ns_done: .res 2
ns_perfect: .res 2
ns_donet: .res 2
ns_msgt: .res 2
ns_msgid: .res 2
ns_line: .res 2                ; bottom line shown (-1 = redraw)

.segment "CODE12"
.a16
.i16

; ------------------------------------------------------------------ shared
bar_setup:
    ; Luigi badge, status texts
    lda ss_player
    beq :+
    lda bn_tmp+6                ; badge map id
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_now
:   jsl scr_hud_text
    rts

; A = t -> 1 when (t/8) % 3 == 0 (lantern glow)
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

; A = 16-bit random -> A = random 0..X-1
rand_n:
    stx bn_tmp+4
    jsl scr_rand
    and #$00FF
@m: cmp bn_tmp+4
    bcc @d
    sbc bn_tmp+4
    bra @m
@d: rts

; ================================================================== Toad house
toad_enter:
    php
    rep #$30
    lda #SCN_TOAD
    jsl scr_scene_load
    lda #MAP_TOAD_BADGE_L
    sta bn_tmp+6
    jsr bar_setup
    lda #1
    sta bn_sel
    lda #$FFFF
    sta bn_open
    sta bn_lp
    sta bn_pp
    ; 3 chests drawn from the node's pool without repeats (while more than one is left) - C# ToadHouseScreen.Enter
    lda mp_node
    jsl map_node_items
    sta bn_pool
    stz bn_ci
@chest:
    ; n = items in the pool
    stz bn_cnt
    ldx #1
@cn: jsr bitx
    and bn_pool
    beq :+
    inc bn_cnt
:   inx
    cpx #10
    bcc @cn
    lda #IT_MUSHROOM
    ldx bn_cnt
    beq @store
    jsr rand_n                  ; k in 0..n-1
    sta bn_k2
    ldx #0
@find:
    inx
    cpx #10
    bcs @store0
    jsr bitx
    and bn_pool
    beq @find
    lda bn_k2
    beq @hit
    dec bn_k2
    bra @find
@store0:
    lda #IT_MUSHROOM
    bra @store
@hit:
    lda bn_cnt
    cmp #2
    bcc :+
    jsr bitx                    ; remove it while more than one is left
    eor #$FFFF
    and bn_pool
    sta bn_pool
:   txa
@store:
    ldx bn_ci
    sta bn_items,x
    inx
    inx
    stx bn_ci
    cpx #6
    bcc @chest
    PRINT 40, 26, TXP_TOAD_WHITE, "PICK A CHEST!"
    PRINT 40, 38, TXP_TOAD_GOLD, "ONE ITEM TO KEEP."
    jsl txt_flush_now
    lda #SP_IDLE_SMALL
    ldx ss_cur
    lda pp_form,x
    and #$00FF
    clc
    adc #SP_IDLE_SMALL
    ldx ss_player
    jsl scr_obj_pal
    lda #SONG_TOADHOUSE
    ldx #1
    jsl scr_music
    jsr toad_draw
    plp
    rtl

toad_tick:
    php
    rep #$30
    lda bn_open
    bmi @pick
    inc bn_opent
    lda bn_opent
    cmp #151
    bcs @leave
    cmp #41
    jcc @draw
    lda scr_pressed
    and #PAD_START|KEY_OK
    jeq @draw
@leave:
    lda #1000
    sta bn_opent
    jsl map_toad_used
    lda #SC_MAP
    jsl scr_go
    brl @draw
@pick:
    jsl scr_navh
    beq @nh
    clc
    adc bn_sel
    bpl :+
    lda #2
:   cmp #3
    bcc :+
    lda #0
:   sta bn_sel
    SFX SFX_MENUMOVE
@nh:
    lda scr_pressed
    and #PAD_START|KEY_OK
    jeq @nob
    lda bn_sel
    sta bn_open
    stz bn_opent
    asl a
    tax
    lda bn_items,x
    jsl sv_add_item
    SFX SFX_CHEST
    ; "YOU GOT" / "<ITEM>!"
    lda #40
    sta txt_x
    lda #26
    sta txt_y
    lda #176
    sta txt_w
    lda #22
    sta txt_h
    jsl txt_clear
    PRINT 40, 26, TXP_TOAD_WHITE, "YOU GOT"
    jsl sb_reset
    lda bn_open
    asl a
    tax
    lda bn_items,x
    jsr sb_item
    lda #'!'
    jsl sb_char
    lda #40
    sta txt_x
    lda #38
    sta txt_y
    lda #TXP_TOAD_GOLD
    sta txt_pal
    jsl txt_print_sb
    bra @draw
@nob:
    lda scr_pressed
    and #KEY_BACK
    beq @draw
    lda #SC_MAP
    jsl scr_go
@draw:
    jsr toad_draw
    plp
    rtl

toad_draw:
    lda scr_t
    jsr glow
    cmp bn_lp
    beq :+
    sta bn_lp
    tax
    lda #SP_LANTERN
    jsl scr_obj_pal
:   jsl scr_obj_begin
    OBJ SP_LANTERN, #224, #64, #$30
    ; Toad (waves while you pick)
    lda bn_open
    bpl @t1
    lda scr_t
    DIVC 30
    and #1
    beq @t1
    lda #SP_TOAD2
    bra @tp
@t1: lda #SP_TOAD1
@tp: pha
    lda #28
    sta spr_x
    lda #136
    sta spr_y
    lda #$30
    sta spr_fl
    pla
    jsl scr_obj_put
    ; chests, the item rising from the opened one, the pointer
    stz bn_tmp
@ch: lda bn_tmp
    cmp #3
    jcs @chd
    asl a
    asl a
    asl a
    asl a
    sta bn_tmp+2
    asl a
    clc
    adc bn_tmp+2                ; 48 i
    adc #96
    sta bn_tmp+2
    sta spr_x
    lda #144
    sta spr_y
    lda #$30
    sta spr_fl
    lda bn_tmp
    cmp bn_open
    bne @closed
    lda #SP_CHEST_O
    jsl scr_obj_put
    ; icon at y = 144 - 18 - min(24, openT/2)
    lda bn_opent
    lsr a
    cmp #24
    bcc :+
    lda #24
:   eor #$FFFF
    sec
    adc #144-18
    sta spr_y
    lda bn_tmp
    asl a
    tax
    lda bn_items,x
    dec a
    clc
    adc #SP_ITEM_MUSHROOM
    jsl scr_obj_put
    bra @chn
@closed:
    lda #SP_CHEST_C
    jsl scr_obj_put
    lda bn_open
    bpl @chn
    lda bn_tmp
    cmp bn_sel
    bne @chn
    ; pointer above the chosen chest (color flash (t/8)%2, bob (t/12)%2)
    lda bn_tmp+2
    clc
    adc #8
    sta spr_x
    lda scr_t
    DIVC 12
    and #1
    clc
    adc #134
    sta spr_y
    lda scr_t
    lsr a
    lsr a
    lsr a
    and #1
    cmp bn_pp
    beq :+
    sta bn_pp
    tax
    lda #SP_POINTER
    jsl scr_obj_pal
:   lda #SP_POINTER
    jsl scr_obj_put
@chn:
    inc bn_tmp
    brl @ch
@chd:
    ; the player beside the chosen chest
    lda bn_sel
    asl a
    asl a
    asl a
    asl a
    sta bn_tmp
    asl a
    clc
    adc bn_tmp
    adc #76
    sta spr_x
    lda #128
    sta spr_y
    lda #$30
    sta spr_fl
    ldx ss_cur
    lda pp_form,x
    and #$00FF
    clc
    adc #SP_IDLE_SMALL
    jsl scr_obj_put
    jsl scr_hud_cards
    rts

; ================================================================== spade panel slot machine
spade_enter:
    php
    rep #$30
    lda #SCN_SPADE
    jsl scr_scene_load
    lda #MAP_SPADE_BADGE_L
    sta bn_tmp+6
    jsr bar_setup
    stz sp_pos
    stz sp_pos+2
    stz sp_pos+4
    stz sp_row
    stz sp_done
    stz sp_prize
    stz sp_donet
    PRINTC 180, TXP_SPADE_WHITE, "PRESS A TO STOP"
    jsl txt_flush_now
    lda #SONG_BONUS
    ldx #1
    jsl scr_music
    jsr spade_draw
    plp
    rtl

spade_tick:
    php
    rep #$30
    lda sp_done
    beq @run
    inc sp_donet
    lda sp_donet
    cmp #181
    bcs @leave
    cmp #41
    jcc @draw
    lda scr_pressed
    and #KEY_OK
    jeq @draw
@leave:
    lda #1000
    sta sp_donet
    jsl map_toad_used
    lda #SC_MAP
    jsl scr_go
    bra @draw
@run:
    ; the rows still running scroll by 3, 5, 4 px
    lda sp_row
    asl a
    tax
@r: cpx #6
    bcs @rd
    lda sp_pos,x
    clc
    adc f:sp_speed,x
    cmp #144
    bcc :+
    sbc #144
:   sta sp_pos,x
    inx
    inx
    bra @r
@rd:
    lda scr_pressed
    and #PAD_START|KEY_OK
    beq @draw
    ; stop the row on the nearest card
    lda sp_row
    asl a
    tax
    lda sp_pos,x
    clc
    adc #24
    DIVC 48
    asl a
    asl a
    asl a
    asl a
    sta bn_tmp
    asl a
    clc
    adc bn_tmp                  ; *48
    cmp #144
    bcc :+
    sbc #144
:   pha
    lda sp_row
    asl a
    tax
    pla
    sta sp_pos,x
    SFX SFX_CARDSTOP
    inc sp_row
    lda sp_row
    cmp #3
    bcc @draw
    jsr spade_finish
@draw:
    jsr spade_draw
    plp
    rtl
sp_speed: .word 3, 5, 4

spade_finish:
    lda #1
    sta sp_done
    lda sp_pos
    DIVC 48
    sta bn_tmp
    lda sp_pos+2
    DIVC 48
    cmp bn_tmp
    bne @miss
    lda sp_pos+4
    DIVC 48
    cmp bn_tmp
    bne @miss
    lda bn_tmp
    asl a
    tax
    lda f:prizes,x
    sta sp_prize
    sta bn_tmp+2
:   jsl sv_add_life
    dec bn_tmp+2
    bne :-
    lda #SONG_BONUS1UP
    ldx #1
    jsl scr_music
    jsr spade_msg
    rts
@miss:
    SFX SFX_ERROR
    jsr spade_msg
    rts
prizes: .word 2, 3, 5

spade_msg:
    lda #0
    sta txt_x
    lda #180
    sta txt_y
    lda #256
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
    lda sp_prize
    beq :+
    jsl sb_reset
    lda sp_prize
    jsl sb_dec
    SB "UP!"
    lda #180
    sta txt_y
    lda #TXP_SPADE_GOLD
    sta txt_pal
    jsl txt_printc_sb
    jsl scr_hud_text
    rts
:   PRINTC 180, TXP_SPADE_GREY, "NO MATCH..."
    rts

spade_draw:
    ; lattice drift (crop 0)
    lda scr_t
    lsr a
    and #31
    sta bn_tmp
    eor #$FFFF
    inc a
    and #$00FF
    sta scr_bg2x
    lda #0
    sec
    sbc bn_tmp
    and #$00FF
    sta scr_bg2y
    jsl scr_obj_begin
    stz bn_tmp+4                ; row
@row: lda bn_tmp+4
    cmp #3
    bcc :+
    jmp @rows_done
:   asl a
    tax
    lda sp_pos,x
    DIVC 48
    stx bn_tmp                  ; off = pos % 48
    DIVC 3
    stx bn_tmp+2                ; icon = (pos/48) % 3
    ; y = 40 + 44 r
    lda bn_tmp+4
    asl a
    asl a
    sta bn_tmp+6
    asl a
    sta bn_y
    asl a
    asl a
    clc
    adc bn_tmp+6
    adc bn_y                    ; 44 r
    adc #40
    sta bn_y
    ; k = -1, 0, 1
    lda #$FFFF
    sta bn_k
@k: ; x = 120 - off + 48 k
    lda bn_k
    asl a
    asl a
    asl a
    asl a
    sta bn_tmp+6
    asl a
    clc
    adc bn_tmp+6                ; 48 k
    clc
    adc #120
    sec
    sbc bn_tmp
    cmp #72
    bmi @kn
    cmp #169
    bpl @kn
    sta spr_x
    lda bn_y
    clc
    adc #12
    sta spr_y
    lda #$30
    sta spr_fl
    ; ic = (icon + k + 3) % 3
    lda bn_tmp+2
    clc
    adc bn_k
    clc
    adc #3
    DIVC 3
    txa
    clc
    adc #SP_CARD0
    jsl scr_obj_put
@kn: inc bn_k
    lda bn_k
    cmp #2
    bne @k
    ; cursor on the running row
    lda sp_done
    bne @nc
    lda bn_tmp+4
    cmp sp_row
    bne @nc
    lda scr_t
    lsr a
    lsr a
    lsr a
    and #3
    beq :+
    lda #68
    bra :++
:   lda #69
:   sta spr_x
    lda bn_y
    clc
    adc #16
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_CURSOR
    jsl scr_obj_put
@nc: inc bn_tmp+4
    jmp @row
@rows_done:
    jsl scr_hud_cards
    rts
.segment "BSS"
bn_y: .res 2
bn_k: .res 2
.segment "CODE12"

; ================================================================== N-Spade
NS_COLS = 6

nspade_enter:
    php
    rep #$30
    lda #SCN_NSPADE
    jsl scr_scene_load
    lda #MAP_NSPADE_BADGE_L
    sta bn_tmp+6
    jsr bar_setup
    ; deal: 4 mushrooms, 4 flowers, 4 stars, 2 1-UPs, 2 x 10 coins, 2 x 20 coins, shuffled
    ldx #0
@d: lda f:deck,x
    and #$00FF
    sep #$20
    .a8
    sta ns_cards,x
    stz ns_up,x
    rep #$20
    .a16
    inx
    cpx #18
    bcc @d
    ldx #17
@sh: phx
    inx
    jsr rand_n                  ; j in 0..i
    tay
    plx
    sep #$20
    .a8
    lda ns_cards,x
    pha
    lda ns_cards,y
    sta ns_cards,x
    pla
    sta ns_cards,y
    rep #$20
    .a16
    dex
    bne @sh
    stz ns_cur
    lda #$FFFF
    sta ns_first
    sta ns_second
    sta ns_line
    stz ns_miss
    stz ns_found
    stz ns_done
    stz ns_donet
    stz ns_msgt
    PRINTC 12, TXP_NSPADE_WHITE, "N-SPADE: FIND THE PAIRS!"
    jsr ns_bottom
    jsl txt_flush_now
    lda #SONG_BONUS
    ldx #1
    jsl scr_music
    jsr ns_draw
    plp
    rtl
deck: .byte 0,0,0,0, 1,1,1,1, 2,2,2,2, 3,3, 4,4, 5,5

nspade_tick:
    php
    rep #$30
    lda ns_msgt
    beq :+
    dec ns_msgt
    bne :+
    jsr ns_bottom
:   lda ns_done
    beq @play
    inc ns_donet
    lda ns_donet
    cmp #201
    bcs @leave
    cmp #41
    bcs :+
    jmp @draw
:   lda scr_pressed
    and #PAD_START|KEY_OK
    bne @leave
    jmp @draw
@leave:
    lda #1000
    sta ns_donet
    jsl map_nspade_done
    lda #SC_MAP
    jsl scr_go
    jmp @draw
@play:
    lda ns_second
    jmi @pick
    ; both cards are showing: resolve after a short look
    dec ns_showt
    beq :+
    jmp @draw
:   ldx ns_first
    ldy ns_second
    sep #$20
    .a8
    lda ns_cards,x
    cmp ns_cards,y
    rep #$20
    .a16
    bne @nomatch
    lda ns_cards,x
    and #$00FF
    jsr ns_award
    inc ns_found
    lda ns_found
    cmp #9
    bcc @res
    lda #1
    jsr ns_finish
    bra @res
@nomatch:
    sep #$20
    .a8
    lda #0
    sta ns_up,x
    sta ns_up,y
    rep #$20
    .a16
    txa
    jsr ns_block
    lda ns_second
    jsr ns_block
    inc ns_miss
    lda ns_miss
    cmp #2
    bcc :+
    lda #0
    jsr ns_finish
    bra @res
:   SFX SFX_ERROR
    lda #6
    sta ns_msgid
    lda #90
    sta ns_msgt
    jsr ns_bottom
@res:
    lda #$FFFF
    sta ns_first
    sta ns_second
    jmp @draw
@pick:
    jsl scr_navh
    sta bn_tmp
    jsl scr_navv
    sta bn_tmp+2
    ora bn_tmp
    beq @nomove
    ; col = (cur%6 + h + 6) % 6, row = (cur/6 + v + 3) % 3
    lda ns_cur
    DIVC NS_COLS
    stx bn_tmp+4
    clc
    adc bn_tmp+2
    clc
    adc #3
    DIVC 3
    stx bn_tmp+6
    lda bn_tmp+4
    clc
    adc bn_tmp
    clc
    adc #6
    DIVC 6
    stx bn_tmp+4
    lda bn_tmp+6
    asl a
    sta bn_tmp
    asl a
    clc
    adc bn_tmp
    adc bn_tmp+4
    sta ns_cur
    SFX SFX_MENUMOVE
@nomove:
    lda scr_pressed
    and #KEY_OK
    beq @draw
    ldx ns_cur
    lda ns_up,x
    and #$00FF
    beq :+
    SFX SFX_ERROR
    bra @draw
:   sep #$20
    .a8
    lda #1
    sta ns_up,x
    rep #$20
    .a16
    lda ns_cur
    jsr ns_block
    SFX SFX_CARDSTOP
    lda ns_first
    bpl :+
    lda ns_cur
    sta ns_first
    bra @draw
:   lda ns_cur
    sta ns_second
    lda #36
    sta ns_showt
@draw:
    jsr ns_draw
    plp
    rtl

; A = card -> put its block (face or back) at its place
ns_block:
    sta bn_tmp
    DIVC NS_COLS
    sta bn_tmp+2                ; row
    ; tile x = (16 + 40 c) / 8 = 2 + 5c, tile y = (32 + 48 r) / 8 = 4 + 6r
    txa
    asl a
    asl a
    stx bn_tmp+4
    clc
    adc bn_tmp+4
    adc #2
    sta scr_tmp0
    lda bn_tmp+2
    asl a
    sta bn_tmp+4
    asl a
    clc
    adc bn_tmp+4
    adc #4
    sta scr_tmp1
    ldx bn_tmp
    lda ns_up,x
    and #$00FF
    beq @back
    lda ns_cards,x
    and #$00FF
    sta bn_tmp+4
    asl a
    clc
    adc bn_tmp+4                ; face*3 + row
    adc bn_tmp+2
    clc
    adc #MAP_NSPADE_F0R0
    bra @put
@back:
    lda bn_tmp+2
    clc
    adc #MAP_NSPADE_B0
@put:
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put_at
    rts

; A = face -> the prize (C# Award)
ns_award:
    sta ns_msgid
    cmp #3
    bcs @other
    ; mushroom / flower / star -> an item
    inc a
    cmp #3
    bne :+
    lda #IT_STAR
:   jsl sv_add_item
    SFX SFX_POWERUP
    bra @msg
@other:
    cmp #3
    bne @coins
    jsl sv_add_life
    SFX SFX_ONEUP
    bra @msg
@coins:
    ldy #10
    cmp #4
    beq :+
    ldy #20
:   stz bn_tmp+6
@c: phy
    jsl sv_add_coin
    bcc :+
    lda #1
    sta bn_tmp+6
:   ply
    dey
    bne @c
    lda bn_tmp+6
    beq :+
    SFX SFX_ONEUP
    bra @msg
:   SFX SFX_COIN
@msg:
    lda #90
    sta ns_msgt
    jsl scr_hud_text
    jsr ns_bottom
    rts

; A = 1 perfect -> C# Finish: reveal everything, jingle or error, save
ns_finish:
    sta ns_perfect
    lda #1
    sta ns_done
    stz ns_donet
    stz ns_msgt
    ldx #0
@r: sep #$20
    .a8
    lda ns_up,x
    bne :+
    lda #1
    sta ns_up,x
    rep #$20
    .a16
    phx
    txa
    jsr ns_block
    plx
:   rep #$20
    .a16
    inx
    cpx #18
    bcc @r
    lda ns_perfect
    beq :+
    lda #SONG_BONUS1UP
    ldx #1
    jsl scr_music
    bra :++
:   SFX SFX_ERROR
:   jsl sv_save
    jsr ns_bottom
    rts

; the bottom line (y = 180)
ns_bottom:
    lda #0
    sta txt_x
    lda #180
    sta txt_y
    lda #256
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
    lda ns_done
    beq @notdone
    lda ns_perfect
    beq :+
    PRINTC 180, TXP_NSPADE_GOLD, "PERFECT! ALL PAIRS FOUND!"
    rts
:   PRINTC 180, TXP_NSPADE_RED, "TOO BAD! 2 MISSES."
    rts
@notdone:
    lda ns_msgt
    bne @msg
    jsl sb_reset
    SB "MISSES LEFT: "
    lda #2
    sec
    sbc ns_miss
    jsl sb_dec
    lda #180
    sta txt_y
    lda #TXP_NSPADE_WHITE
    sta txt_pal
    jsl txt_printc_sb
    rts
@msg:
    jsl sb_reset
    lda ns_msgid
    cmp #3
    bcs :+
    inc a
    cmp #3
    bne @it
    lda #IT_STAR
@it: jsr sb_item
    lda #'!'
    jsl sb_char
    bra @pr
:   cmp #3
    bne :+
    SB "1UP!"
    bra @pr
:   cmp #6
    bne :+
    SB "ONE MORE MISS AND IT'S OVER!"
    bra @pr
:   ldx #10
    cmp #4
    beq :+
    ldx #20
:   txa
    jsl sb_dec
    SB " COINS!"
@pr:
    lda #180
    sta txt_y
    lda #TXP_NSPADE_GREEN
    sta txt_pal
    jsl txt_printc_sb
    rts

ns_draw:
    jsl scr_obj_begin
    ; the selection frame (BG2, scrolled onto the card), blinking (t/8)%3 != 0
    lda ns_done
    bne @hide
    lda scr_t
    lsr a
    lsr a
    lsr a
    DIVC 3
    txa
    beq @hide
    lda ns_cur
    DIVC NS_COLS
    sta bn_tmp
    txa
    asl a
    asl a
    asl a
    sta bn_tmp+2
    asl a
    asl a
    clc
    adc bn_tmp+2                ; 40 c
    eor #$FFFF
    inc a
    and #$00FF
    sta scr_bg2x
    lda bn_tmp
    asl a
    asl a
    asl a
    asl a
    sta bn_tmp+2
    asl a
    clc
    adc bn_tmp+2                ; 48 r
    eor #$FFFF
    inc a
    and #$00FF
    sta scr_bg2y
    bra @cards
@hide:
    lda #$15                    ; BG2 (the frame) off
    sta scr_tm
    bra @cards2
@cards:
    lda #$17
    sta scr_tm
@cards2:
    jsl scr_hud_cards
    rts

; X = n -> A = 1 << n (X kept)
bitx:
    phx
    lda #1
@l: dex
    bmi @d
    asl a
    bra @l
@d: plx
    rts
.segment "BSS"
bn_pool: .res 2
bn_ci: .res 2
bn_cnt: .res 2
bn_k2: .res 2
