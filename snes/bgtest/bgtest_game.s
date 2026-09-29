; bg test driver: cycles through every theme variant, 120 frames each (A = floor camera x0, B = 40 px up x300)
.p816
.smart
.include "snes.inc"
.include "themes.inc"
.export game_init, game_frame, eng_nmi
.import bg_load, bg_set_water, bg_update, bg_hdma_off, bg_cam_x, bg_cam_y, bg_area_h, bg_flat_color

.segment "BSS"
t_idx:   .res 2
t_frame: .res 2
.export t_vmax
t_v0: .res 2
t_v1: .res 2
t_vmax: .res 2

.segment "CODE1"
; theme, flat, water px (floor 240 -> +120 = line 120 at A), flat color
tests:
    .word THEME_PLAINS, 0, $FFFF, 0
    .word THEME_UNDERGROUND, 0, $FFFF, 0
    .word THEME_DESERT, 0, $FFFF, 0
    .word THEME_SEA, 0, $FFFF, 0
    .word THEME_SEA, 0, 0, 0
    .word THEME_SEA, 0, 360, 0
    .word THEME_JUNGLE, 0, $FFFF, 0
    .word THEME_SKY, 0, $FFFF, 0
    .word THEME_ICE, 0, $FFFF, 0
    .word THEME_ICE, 0, 360, 0
    .word THEME_MACHINE, 0, $FFFF, 0
    .word THEME_VOLCANO, 0, $FFFF, 0
    .word THEME_FORTRESS, 0, $FFFF, 0
    .word THEME_FORTRESS, 0, 360, 0
    .word THEME_CASTLE, 0, $FFFF, 0
    .word THEME_AIRSHIP, 0, $FFFF, 0
    .word THEME_BONUS, 0, $FFFF, 0
    .word THEME_AIRSHIP, 1, $FFFF, $5294
NTESTS = (* - tests) / 8

game_init:
    .a8
    .i16
    ; test sprite: 16x16 solid white OBJ at (100,184) crossing the HUD line (must be cut at 192)
    lda #$03
    sta OBSEL
    lda #$80
    sta VMAIN
    ldx #$6000
    jsr t_tiles
    ldx #$6100
    jsr t_tiles
    lda #129
    sta CGADD
    lda #$FF
    sta CGDATA
    lda #$7F
    sta CGDATA
    stz OAMADDL
    stz OAMADDL+1
    lda #100
    sta OAMDATA
    lda #184
    sta OAMDATA
    stz OAMDATA
    lda #$30
    sta OAMDATA
    ldx #127
:   stz OAMDATA
    lda #$F0
    sta OAMDATA
    stz OAMDATA
    stz OAMDATA
    dex
    bne :-
    ldx #$0100
    stx OAMADDL
    lda #$02
    sta OAMDATA
    lda #$09
    sta BGMODE
    lda #$12
    sta TM
    jsr t_load
    lda #$0F
    sta INIDISP
    rtl

game_frame:
    rep #$20
    inc t_frame
    lda t_frame
    cmp #120
    bcc @cam
    stz t_frame
    stz t_vmax
    lda t_idx
    inc
    cmp #NTESTS
    bcc :+
    lda #0
:   sta t_idx
    sep #$20
    lda #$80
    sta INIDISP
    jsr t_load
    lda #$0F
    sta INIDISP
    rep #$20
@cam:
    lda t_frame
    cmp #60
    bcs @b
    stz bg_cam_x
    lda #240
    sta bg_cam_y
    bra @u
@b: lda #300
    sta bg_cam_x
    lda #200
    sta bg_cam_y
@u: sep #$20
    lda $213F
    lda $2137
    lda $213D
    sta t_v0
    lda $213D
    and #1
    sta t_v0+1
    jsl bg_update
    lda $213F
    lda $2137
    lda $213D
    sta t_v1
    lda $213D
    and #1
    sta t_v1+1
    rep #$20
    lda t_v1
    sec
    sbc t_v0
    bpl :+
    clc
    adc #262
:   sta t_v0
    cmp t_vmax
    bcc :+
    sta t_vmax
:   sep #$20
    rtl

eng_nmi:
    rtl

t_load:
    rep #$20
    lda t_idx
    asl
    asl
    asl
    tax
    lda #432
    sta bg_area_h
    lda #240
    sta bg_cam_y
    stz bg_cam_x
    lda f:tests+6,x
    sta bg_flat_color
    phx
    lda f:tests+4,x
    tax
    sep #$20
    jsl bg_hdma_off
    jsl bg_set_water
    rep #$20
    plx
    lda f:tests+2,x
    pha
    lda f:tests,x
    plx
    sep #$20
    jsl bg_load
    rts

t_tiles:
    .a8
    .i16
    ;                  ; 2 tiles of solid color 1 at VRAM X
    stx VMADDL
    ldx #0
:   txa
    and #$0F
    cmp #8
    lda #0
    bcs :+
    lda #$FF
:   sta VMDATAL
    stz VMDATAH
    inx
    cpx #32
    bne :--
    rts
