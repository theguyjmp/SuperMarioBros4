; File select (screens agent) — port of C# FileSelectScreen: 3 battery-backed save files, start/continue, erase.
; Backdrop = BG2 lattice + HDMA gradients; BG1 = the 3 file windows (map "confirm" adds the erase window).
.p816
.smart
.include "scr.inc"

.export files_enter, files_tick
.import map_new_world
.import files_two, sv_peek, sv_pk_exists, sv_pk_world, sv_pk_beaten, sv_pk_lives, sv_pk_score, sv_select

.segment "BSS"
fs_sel: .res 2
fs_erase: .res 2               ; erase mode
fs_confirm: .res 2
fs_exists: .res 6
fs_world: .res 6
fs_beaten: .res 6
fs_lives: .res 6
fs_score: .res 12

.segment "CODE11"
.a16
.i16

files_enter:
    php
    rep #$30
    lda #SCN_FILES
    jsl scr_scene_load
    stz fs_sel
    stz fs_erase
    stz fs_confirm
    jsr load_slots
    jsr redraw
    jsl txt_flush_now
    lda #SONG_SELECT
    ldx #0
    jsl scr_music
    jsr sprites
    plp
    rtl

load_slots:
    ldx #0
@l: phx
    txa
    jsl sv_peek
    plx
    txa
    asl a
    tay
    lda sv_pk_exists
    and #$00FF
    sta fs_exists,y
    lda sv_pk_world
    and #$00FF
    sta fs_world,y
    lda sv_pk_beaten
    and #$00FF
    sta fs_beaten,y
    lda sv_pk_lives
    and #$00FF
    sta fs_lives,y
    tya
    asl a
    tay
    lda sv_pk_score
    sta fs_score,y
    lda sv_pk_score+2
    sta fs_score+2,y
    inx
    cpx #3
    bcc @l
    rts

files_tick:
    php
    rep #$30
    ; back (Y): leave the confirm box / erase mode, else back to the title
    lda scr_pressed
    and #KEY_BACK
    beq @noback
    SFX SFX_MENUBACK
    lda fs_confirm
    beq :+
    stz fs_confirm
    stz fs_erase
    jsr close_confirm
    brl @out
:   lda fs_erase
    beq :+
    stz fs_erase
    jsr redraw
    brl @out
:   lda #SC_TITLE
    jsl scr_go
    brl @out
@noback:
    lda fs_confirm
    beq @nav
    lda scr_pressed
    and #PAD_START|KEY_OK
    jeq @out
    lda fs_sel
    jsl sv_erase
    jsr load_slots
    stz fs_confirm
    stz fs_erase
    SFX SFX_BREAK
    jsr close_confirm
    brl @out
@nav:
    jsl scr_navv
    beq @nov
    clc
    adc fs_sel
    and #3
    sta fs_sel
    SFX SFX_MENUMOVE
    jsr redraw
@nov:
    lda scr_pressed
    and #PAD_START|KEY_OK
    beq @out
    lda fs_sel
    cmp #3
    bne @slot
    ; toggle erase mode
    lda fs_erase
    eor #1
    sta fs_erase
    SFX SFX_MENUSELECT
    lda #3
    ldx fs_erase
    beq :+
    lda #0
:   sta fs_sel
    jsr redraw
    bra @out
@slot:
    lda fs_erase
    beq @start
    lda fs_sel
    asl a
    tax
    lda fs_exists,x
    bne :+
    stz fs_erase
    jsr redraw
    bra @out
:   lda #1
    sta fs_confirm
    jsr open_confirm
    bra @out
@start:
    ; pick the file: new -> story first, then the map
    lda fs_sel
    ldx files_two
    jsl sv_select
    php
    SFX SFX_MAPENTER
    ; C#: new MapScreen(save.World, true) (the story shows first for a new file)
    lda sv_world
    and #$00FF
    ldx #1
    jsl map_new_world
    plp
    lda #SC_MAP
    bcc :+
    lda #SC_STORY
:   jsl scr_go
@out:
    jsr sprites
    plp
    rtl

open_confirm:
    lda #MAP_FILES_CONFIRM
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put
    ; the window covers the slots' text (C# y 96..144)
    lda #40
    sta txt_x
    lda #88
    sta txt_y
    lda #176
    sta txt_w
    lda #48
    sta txt_h
    jsl txt_clear
    jsl sb_reset
    SB "ERASE FILE "
    lda fs_sel
    inc a
    jsl sb_dec
    lda #'?'
    jsl sb_char
    lda #72
    sta txt_x
    lda #100
    sta txt_y
    lda #TXP_FILES_WHITE
    sta txt_pal
    jsl txt_print_sb
    PRINT 72, 116, TXP_FILES_GREY, "A: YES   Y: NO"
    rts

close_confirm:
    lda #MAP_FILES_SLOTS
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put
    jsr redraw
    rts

; all texts (C# Render, y - 8)
redraw:
    jsl txt_clear_all
    lda files_two
    beq :+
    PRINT 76, 12, TXP_FILES_WHITE, "2 PLAYER GAME"
    bra :++
:   PRINT 76, 12, TXP_FILES_WHITE, "1 PLAYER GAME"
:   lda fs_erase
    beq :+
    PRINT 60, 28, TXP_FILES_RED, "ERASE WHICH FILE?"
    bra :++
:   PRINT 76, 28, TXP_FILES_GOLD, "SELECT A FILE"
:   stz scr_tmp3
@slot:
    ; y = 52 + 44*i
    lda scr_tmp3
    asl a
    tax
    lda f:slot_y,x
    sta scr_tmp5
    ; FILE n
    jsl sb_reset
    SB "FILE "
    lda scr_tmp3
    inc a
    jsl sb_dec
    lda #36
    sta txt_x
    lda scr_tmp5
    clc
    adc #8
    sta txt_y
    lda #TXP_FILES_GREY
    ldx scr_tmp3
    cpx fs_sel
    bne :+
    lda #TXP_FILES_WHITE
:   sta txt_pal
    jsl txt_print_sb
    lda scr_tmp3
    asl a
    tax
    lda fs_exists,x
    jeq @new
    jsl sb_reset
    SB "WORLD "
    lda scr_tmp3
    asl a
    tax
    lda fs_world,x
    jsl sb_dec
    lda #112
    sta txt_x
    lda scr_tmp5
    clc
    adc #8
    sta txt_y
    lda #TXP_FILES_WHITE
    sta txt_pal
    jsl txt_print_sb
    jsl sb_reset
    lda #'*'
    jsl sb_char
    lda scr_tmp3
    asl a
    tax
    lda fs_lives,x
    jsl sb_dec
    lda #54
    sta txt_x
    lda scr_tmp5
    clc
    adc #22
    sta txt_y
    jsl txt_print_sb
    jsl sb_reset
    lda scr_tmp3
    asl a
    asl a
    clc
    adc #.loword(fs_score)
    tax
    jsl sb_bcd7
    lda #112
    sta txt_x
    lda #TXP_FILES_GREY
    sta txt_pal
    jsl txt_print_sb
    bra @next
@new:
    lda #112
    sta txt_x
    lda scr_tmp5
    clc
    adc #16
    sta txt_y
    lda #TXP_FILES_GREEN
    sta txt_pal
    jsl sb_reset
    SB "NEW GAME"
    lda #112
    sta txt_x
    jsl txt_print_sb
@next:
    inc scr_tmp3
    lda scr_tmp3
    cmp #3
    bcs :+
    jmp @slot
:   lda fs_erase
    beq :+
    lda #TXP_FILES_GREY
    ldx fs_sel
    cpx #3
    bne @c1
    lda #TXP_FILES_WHITE
@c1: sta txt_pal
    lda #36
    sta txt_x
    lda #188
    sta txt_y
    jsl sb_reset
    SB "CANCEL"
    jsl txt_print_sb
    rts
:   lda #TXP_FILES_GREY
    ldx fs_sel
    cpx #3
    bne @c2
    lda #TXP_FILES_WHITE
@c2: sta txt_pal
    lda #36
    sta txt_x
    lda #188
    sta txt_y
    jsl sb_reset
    SB "ERASE A FILE"
    jsl txt_print_sb
    rts

sprites:
    jsl scr_obj_begin
    stz scr_tmp3
    lda fs_confirm              ; the erase window covers the slots
    beq @s
    lda #3
    sta scr_tmp3
@s: lda scr_tmp3
    asl a
    tax
    lda fs_exists,x
    beq @n
    lda scr_tmp3
    asl a
    tax
    lda f:slot_y,x
    sta scr_tmp5
    lda #36
    sta spr_x
    lda scr_tmp5
    clc
    adc #18
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_MPMARIO
    jsl scr_obj_put
    lda scr_tmp3
    asl a
    tax
    lda fs_beaten,x
    beq @n
    lda #204
    sta spr_x
    lda scr_tmp5
    clc
    adc #11
    sta spr_y
    lda #SP_STAR
    jsl scr_obj_put
@n: inc scr_tmp3
    lda scr_tmp3
    cmp #3
    bcc @s
    ; cursor
    lda fs_sel
    cmp #3
    bne :+
    lda #188
    bra :++
:   asl a
    tax
    lda f:slot_y,x
    clc
    adc #16
:   sta spr_y
    lda scr_t
    lsr a
    lsr a
    lsr a
    and #3
    beq :+
    lda #14
    bra :++
:   lda #15
:   sta spr_x
    lda #$30
    sta spr_fl
    lda fs_confirm
    bne :+
    lda #SP_CURSOR
    jsl scr_obj_put
:   ; the lattice drifts diagonally (C# off = (t/2) % 32)
    lda scr_t
    lsr a
    and #31
    sta scr_tmp0
    eor #$FFFF
    inc a
    and #$00FF
    sta scr_bg2x
    lda #SCN_FILES_CROP
    sec
    sbc scr_tmp0
    and #$00FF
    sta scr_bg2y
    rts
slot_y: .word 52, 96, 140
