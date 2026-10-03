; Flow screens (screens agent): level intro card (C# LevelIntroScreen), the level hand-over, game over
; (continue/end), world clear (king's room), the story prologue and the ending + credits.
.p816
.smart
.include "scr.inc"

.export intro_enter, intro_tick, level_enter, level_tick, gover_enter, gover_tick
.export wclear_enter, wclear_tick, story_enter, story_tick, ending_enter, ending_tick
.import scr_lvl, scr_lvl_kind, scr_lvl_info, map_level_done, map_continue, map_new_world, sv_set_player
.import lsel_active
.import txt_printn, scr_level_ppu
.importzp txt_sp
.import scr_go_in, scr_kingdoms, scr_story, scr_endstory, scr_credits, sv_from_engine, sv_to_engine

.segment "BSS"
fl_t: .res 2
fl_sel: .res 2
fl_tmp: .res 4
fl_shown: .res 2
fl_lp: .res 2

.segment "CODE7"
.a16
.i16

; A = t -> A = 1 when (t/8) % 3 == 0 (lantern glow phase)
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

; lantern flicker of OBJ slot 0 (SP_LANTERN) for phase t
lantern_pal:
    jsr glow
    cmp fl_lp
    beq :+
    sta fl_lp
    tax
    lda #SP_LANTERN
    jsl scr_obj_pal
:   rts

; the Hud.Backdrop lattice drift (BG2), crop = 8
lattice:
    lda scr_t
    lsr a
    and #31
    sta fl_tmp
    eor #$FFFF
    inc a
    and #$00FF
    sta scr_bg2x
    lda #8
    sec
    sbc fl_tmp
    and #$00FF
    sta scr_bg2y
    rts

; A = char -> sb (string at far pointer scr_ptr in CODE7: use helper below)
; sb_code7: X = address of a 0-terminated string in this bank -> appended to sb
sb_code7:
    phb
    phk
    plb
@c: lda a:0,x
    and #$00FF
    beq @d
    phx
    jsl sb_char
    plx
    inx
    bra @c
@d: plb
    rts

; ================================================================== level intro (C# LevelIntroScreen)
intro_enter:
    php
    rep #$30
    lda #SCN_INTRO
    jsl scr_scene_load
    ; title (C# IntroTitle)
    jsl sb_reset
    lda scr_lvl_kind
    asl a
    tax
    jsr (ititle,x)
    lda #70
    sta txt_y
    lda #TXP_INTRO_WHITE
    sta txt_pal
    jsl txt_printc_sb
    ; subtitle (the level's name)
    jsl sb_reset
    lda scr_lvl
    asl a
    asl a
    tax
    lda f:scr_lvl_info+2,x
    tax
    jsr sb_code7
    lda sb_len
    beq :+
    lda #84
    sta txt_y
    lda #TXP_INTRO_GOLD
    sta txt_pal
    jsl txt_printc_sb
:   ; "*  n" lives
    jsl sb_reset
    SB "*  "
    ldx ss_cur
    lda pp_lives,x
    and #$00FF
    jsl sb_dec
    lda #120
    sta txt_x
    lda #120
    sta txt_y
    lda #TXP_INTRO_WHITE
    sta txt_pal
    jsl txt_print_sb
    jsl txt_flush_now
    ; the player's form + Mario/Luigi palette
    jsr idle_id
    ldx ss_player
    jsl scr_obj_pal
    lda #SONG_ENTERLEVEL
    ldx #1
    jsl scr_music
    jsr intro_draw
    plp
    rtl

ititle: .addr it_level, it_fort, it_ship, it_castle, it_hb
it_level:
    SB "WORLD "
    lda scr_lvl
    asl a
    asl a
    tax
    lda f:scr_lvl_info,x
    tax
    jmp sb_code7
it_fort:
    jsr world_n
    SB "  FORTRESS"
    rts
it_ship:
    jsr world_n
    SB "  AIRSHIP"
    rts
it_castle:
    SB "BOWSER'S CASTLE"
    rts
it_hb:
    SB "HAMMER BRO BATTLE"
    rts
world_n:
    SB "WORLD "
    lda sv_world
    and #$00FF
    jsl sb_dec
    rts

; -> A = SP_IDLE_<form> of the current player
idle_id:
    ldx ss_cur
    lda pp_form,x
    and #$00FF
    clc
    adc #SP_IDLE_SMALL
    rts

intro_tick:
    php
    rep #$30
    ; C#: t == 80, or A/Start between 10 and 80 -> the level
    lda scr_t
    cmp #80
    beq @go
    bcs @d
    cmp #11
    bcc @d
    lda scr_pressed
    and #PAD_START|KEY_OK
    beq @d
@go:
    lda #999
    sta scr_t
    lda #SC_LEVEL
    jsl scr_go
@d: jsr intro_draw
    plp
    rtl

intro_draw:
    jsr lattice
    jsl scr_obj_begin
    jsr idle_id
    pha
    lda #96
    sta spr_x
    lda #100
    sta spr_y
    lda #$30
    sta spr_fl
    pla
    jsl scr_obj_put
    rts

; ================================================================== the level (engine) and its result
level_enter:
    php
    rep #$30
    jsl scr_level_ppu
    jsl sv_to_engine
    lda #1                      ; QUIT LEVEL is always offered (map: back to the map, no life lost)
    sta g_allowexit
    lda #$FFFF                  ; the engine plays its own music now
    sta scr_music_cur
    lda scr_lvl
    tax
    jsl eng_level_start
    plp
    rtl

; runs when the engine hands the frame back (g_mode = GM_SCREEN, display blanked)
level_tick:
    php
    rep #$30
    jsl sv_from_engine
    lda lsel_active
    beq :+
    lda #SC_LVLSEL
    jsl scr_go_in
    plp
    rtl
:
    lda g_result
    jsl map_level_done
    plp
    rtl

; ================================================================== game over (C# GameOverScreen)
gover_enter:
    php
    rep #$30
    lda #SCN_GAMEOVER
    jsl scr_scene_load
    stz fl_sel
    stz fl_shown
    lda #SONG_GAMEOVER
    ldx #1
    jsl scr_music
    plp
    rtl

gover_tick:
    php
    rep #$30
    jsl scr_obj_begin
    lda scr_t
    cmp #90
    bcs :+
    plp
    rtl
:   lda fl_shown
    bne :+
    lda #1
    sta fl_shown
    jsr gover_text
:   jsl scr_navv
    beq :+
    lda fl_sel
    eor #1
    sta fl_sel
    SFX SFX_MENUMOVE
    jsr gover_text
:   lda scr_pressed
    and #PAD_START|KEY_OK
    beq @draw
    lda fl_sel
    bne @end
    ; CONTINUE: C# map.Continue(); back to the map
    jsl map_continue
    lda #SC_MAP
    jsl scr_go
    bra @draw
@end:
    ; END: save and back to the title (2-player games are co-op: both players are out together)
    jsl sv_save
    lda #SC_TITLE
    jsl scr_go
@draw:
    lda scr_t
    lsr a
    lsr a
    lsr a
    and #3
    beq :+
    lda #84
    bra :++
:   lda #85
:   sta spr_x
    lda fl_sel
    asl a
    asl a
    asl a
    asl a
    clc
    adc #100
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_CURSOR
    jsl scr_obj_put
    plp
    rtl

gover_text:
    lda #100
    sta txt_x
    sta txt_y
    lda #80
    sta txt_w
    lda #26
    sta txt_h
    jsl txt_clear
    lda #TXP_GAMEOVER_GREY
    ldx fl_sel
    bne :+
    lda #TXP_GAMEOVER_WHITE
:   sta txt_pal
    jsl sb_reset
    SB "CONTINUE"
    lda #100
    sta txt_x
    sta txt_y
    jsl txt_print_sb
    lda #TXP_GAMEOVER_GREY
    ldx fl_sel
    beq :+
    lda #TXP_GAMEOVER_WHITE
:   sta txt_pal
    jsl sb_reset
    SB "END"
    lda #100
    sta txt_x
    lda #116
    sta txt_y
    jsl txt_print_sb
    rts

; ================================================================== world clear (C# WorldClearScreen)
wclear_enter:
    php
    rep #$30
    lda #SCN_WORLDCLR
    jsl scr_scene_load
    stz fl_lp
    ; the world just cleared = the save's world - 1 (the save already points at the next one)
    lda sv_world
    and #$00FF
    dec a
    bne :+
    lda #1
:   sta fl_sel
    lda mp_wclear
    beq :+
    cmp #9
    bcs :+
    sta fl_sel
:   jsl sb_reset
    SB "WORLD "
    lda fl_sel
    jsl sb_dec
    SB " CLEAR!"
    lda #56
    sta txt_y
    lda #TXP_WORLDCLR_GOLD
    sta txt_pal
    jsl txt_printc_sb
    jsl sb_reset
    SB "THE "
    lda fl_sel
    dec a
    asl a
    tax
    lda f:scr_kingdoms,x
    tax
    jsr sb_code7
    SB " LANTERN"
    lda #80
    sta txt_y
    lda #TXP_WORLDCLR_WHITE
    sta txt_pal
    jsl txt_printc_sb
    PRINTC 92, TXP_WORLDCLR_WHITE, "SHINES AGAIN!"
    lda ss_player
    bne :+
    PRINTC 112, TXP_WORLDCLR_GREEN, "THANK YOU, MARIO!"
    bra :++
:   PRINTC 112, TXP_WORLDCLR_GREEN, "THANK YOU, LUIGI!"
:   jsl txt_flush_now
    lda #SONG_WORLDCLEAR
    ldx #0
    jsl scr_music
    plp
    rtl
.import mp_wclear

wclear_tick:
    php
    rep #$30
    ; PRESS A blinks after 2 s
    lda scr_t
    cmp #121
    bcc @nopress
    and #16
    bne :+
    PRINTC 134, TXP_WORLDCLR_GREY, "PRESS A"
    bra @nopress
:   lda #96
    sta txt_x
    lda #134
    sta txt_y
    lda #64
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
@nopress:
    lda scr_t
    cmp #121
    bcc @nogo
    lda scr_pressed
    and #PAD_START|KEY_OK
    bne @go
@nogo:
    lda scr_t
    cmp #901
    bcc @draw
@go:
    lda #1000
    sta scr_t
    ; C#: new MapScreen(min(8, world + 1), true)
    lda fl_sel
    inc a
    cmp #9
    bcc :+
    lda #8
:   ldx #1
    jsl map_new_world
    lda #SC_MAP
    jsl scr_go
@draw:
    jsl scr_obj_begin
    ; 40 stars drifting right: x = (i*97 + t/2) % 256, y = (i*53) % 180 - 8, white when (i + t/8) % 3 == 0
    stz fl_tmp
@s: lda fl_tmp
    cmp #40
    bcs @sd
    ; x
    lda fl_tmp
    ldx #97
    jsr mul8
    sta fl_tmp+2
    lda scr_t
    lsr a
    clc
    adc fl_tmp+2
    and #$00FF
    sta spr_x
    lda fl_tmp
    ldx #53
    jsr mul8
    DIVC 180
    txa
    sec
    sbc #8
    sta spr_y
    lda #$30
    sta spr_fl
    lda scr_t
    lsr a
    lsr a
    lsr a
    clc
    adc fl_tmp
    DIVC 3
    txa
    beq :+
    lda #SP_DOT_Y
    bra :++
:   lda #SP_DOT_W
:   jsl scr_obj_put
    inc fl_tmp
    bra @s
@sd:
    lda scr_t
    jsr lantern_pal
    lda #120
    sta spr_x
    lda #8
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_LANTERN
    jsl scr_obj_put
    plp
    rtl

; WRMPYA (low byte) * A (8-bit) -> A (16-bit product)
mul8:
    sep #$20
    .a8
    sta WRMPYA
    txa
    sta WRMPYB
    nop
    nop
    nop
    nop
    rep #$20
    .a16
    lda RDMPYL
    rts

; ================================================================== story (C# StoryScreen, new games)
story_enter:
    php
    rep #$30
    lda #SCN_STORY
    jsl scr_scene_load
    stz fl_shown
    stz fl_lp
    lda #SONG_SELECT
    ldx #1
    jsl scr_music
    plp
    rtl

story_tick:
    php
    rep #$30
    lda scr_t
    cmp #21
    bcc :+
    lda scr_pressed
    and #PAD_START|KEY_OK|KEY_BACK
    bne @go
:   lda scr_t
    cmp #901
    bcc @txt
@go:
    lda #1000
    sta scr_t
    lda sv_world
    and #$00FF
    ldx #1
    jsl map_new_world
    lda #SC_MAP
    jsl scr_go
@txt:
    ; lines type in: shown = min(n, t/24 + 1)
    lda scr_t
    DIVC 24
    inc a
    cmp #SCR_STORY_N
    bcc :+
    lda #SCR_STORY_N
:   cmp fl_shown
    beq @press
    ; print line fl_shown
    lda fl_shown
    asl a
    tax
    lda f:scr_story,x
    tax
    jsl sb_reset
    jsr sb_code7
    lda fl_shown
    asl a
    asl a
    sta fl_tmp
    asl a
    clc
    adc fl_tmp
    adc #32                     ; y = 40 + i*12 - 8
    sta txt_y
    lda #TXP_STORY_WHITE
    ldx fl_shown
    cpx #9
    bcc :+
    lda #TXP_STORY_GOLD
:   sta txt_pal
    lda sb_len
    beq :+
    jsl txt_printc_sb
:   inc fl_shown
@press:
    ; PRESS A after 1 s, blinking every 20 ticks
    lda scr_t
    cmp #61
    bcc @draw
    DIVC 20
    and #1
    bne :+
    PRINTC 212, TXP_STORY_GREY, "PRESS A"
    bra @draw
:   lda #96
    sta txt_x
    lda #212
    sta txt_y
    lda #64
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
@draw:
    jsl scr_obj_begin
    ; 48 stars: x = (i*97 + 13) % 256, y = (i*61 + 7) % 200 - 8, white when (i + t/12) % 5 == 0
    stz fl_tmp
@s: lda fl_tmp
    cmp #48
    bcs @sd
    lda fl_tmp
    ldx #97
    jsr mul8
    clc
    adc #13
    and #$00FF
    sta spr_x
    lda fl_tmp
    ldx #61
    jsr mul8
    clc
    adc #7
    DIVC 200
    txa
    sec
    sbc #8
    sta spr_y
    lda #$30
    sta spr_fl
    lda scr_t
    DIVC 12
    clc
    adc fl_tmp
    DIVC 5
    txa
    beq :+
    lda #SP_DOT_D
    bra :++
:   lda #SP_DOT_W
:   jsl scr_obj_put
    inc fl_tmp
    bra @s
@sd:
    ; the 8 lanterns: lit until t = 40 + 6i, then stolen (dark)
    lda scr_t
    jsr lantern_pal
    stz fl_tmp
@l: lda fl_tmp
    cmp #8
    bcs @ld
    asl a
    asl a
    asl a
    sta fl_tmp+2
    asl a
    clc
    adc fl_tmp+2                ; *24
    adc #36
    sta spr_x
    lda #172
    sta spr_y
    lda #$30
    sta spr_fl
    lda fl_tmp
    asl a
    sta fl_tmp+2
    asl a
    clc
    adc fl_tmp+2
    adc #40                     ; 40 + 6i
    cmp scr_t
    beq :+
    bcc :+
    lda #SP_LANTERN
    bra :++
:   lda #SP_LANTERN_DARK
:   jsl scr_obj_put
    inc fl_tmp
    bra @l
@ld:
    plp
    rtl

; ================================================================== ending + credits (C# EndingScreen)
WALK_END = 160
STORY_END = 760
CR_TOP = 32                     ; C# 40..164 (SNES -8)
CR_BOT = 156
CR_N = SCR_CREDITS_N

ending_enter:
    php
    rep #$30
    lda #SCN_ENDING
    jsl scr_scene_load
    stz fl_lp
    stz fl_shown
    stz en_chars
    lda #$FFFF
    sta en_endy
    lda #SONG_ENDING
    ldx #1
    jsl scr_music
    ; hero palette (Mario / Luigi) for the walker
    lda #SP_EMARIO_WALK1
    ldx ss_player
    jsl scr_obj_pal
    plp
    rtl
.segment "BSS"
en_chars: .res 2
en_endy: .res 2
en_phase: .res 2
.segment "CODE7"
.a16
.i16

ending_tick:
    php
    rep #$30
    lda scr_t
    cmp #STORY_END
    bne :+
    lda #SONG_CREDITS
    ldx #1
    jsl scr_music
:   ; leaving (C#): after the credits + 10 s, or A/Start after the credits, or Start during the credits
    jsr credits_done_t
    sta fl_tmp
    clc
    adc #600
    cmp scr_t
    bcs :+
    jmp @title
:   lda scr_t
    cmp fl_tmp
    bcc @nodone
    beq @nodone
    lda scr_pressed
    and #PAD_START|KEY_OK
    beq @nodone
    jmp @title
@nodone:
    lda scr_t
    cmp #STORY_END+1
    bcc :+
    cmp fl_tmp
    bcs :+
    lda scr_pressed
    and #PAD_START
    beq :+
    jmp @title
:
    ; phases: 0 walk, 1 story window, 2 credits
    lda scr_t
    cmp #WALK_END
    jcc @walk
    cmp #STORY_END
    bcs @credits
    ; story: show the window, type the text (2 ticks per char)
    lda en_phase
    bne :+
    lda #1
    sta en_phase
    lda #MAP_ENDING_STORY
    ldx #SCR_VRAM_BG2MAP
    jsl scr_map_put
    stz scr_bg2y
    lda #$FFFF
    sta en_line
    stz en_col
    stz en_len
    stz en_typed
:   lda scr_t
    sec
    sbc #WALK_END
    lsr a
    cmp en_chars
    jeq @draw
    sta en_chars
    jsr type_story
    brl @draw
@credits:
    lda en_phase
    cmp #2
    beq :+
    lda #2
    sta en_phase
    ; the credits page on BG2 (32x64), clipped by HDMA on TM to lines 32-155
    lda #MAP_ENDING_CREDITS
    ldx #SCR_VRAM_BG2MAP
    jsl scr_map_put
    lda #0
    sta txt_x
    lda #26
    sta txt_y
    lda #256
    sta txt_w
    lda #110
    sta txt_h
    jsl txt_clear
    jsr tm_clip
:   ; y0 = 156 - (t - 760)/3 ; BG2 row of line i = 8 + 14 i  -> VOFS = 8 - y0
    lda scr_t
    sec
    sbc #STORY_END
    DIVC 3
    sta fl_tmp+2
    lda #CR_BOT
    sec
    sbc fl_tmp+2
    sta fl_tmp                  ; y0
    lda #8
    sec
    sbc fl_tmp
    and #$01FF
    sta scr_bg2y
    ; THE END: y = max(y0 + (N-1)*14, middle) on the canvas
    lda fl_tmp
    clc
    adc #(CR_N-1)*14
    bmi :+
    cmp #(CR_TOP+CR_BOT)/2-4
    bcs :++
:   lda #(CR_TOP+CR_BOT)/2-4
:   cmp en_endy
    beq @press
    pha
    lda en_endy
    bmi :+
    sta txt_y
    lda #64
    sta txt_x
    lda #128
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
:   pla
    sta en_endy
    cmp #CR_BOT
    bcs @press
    sta txt_y
    lda #TXP_ENDING_GOLD
    sta txt_pal
    lda #CR_N-1
    asl a
    tax
    lda f:scr_credits,x
    tax
    jsl sb_reset
    jsr sb_code7
    jsl txt_printc_sb
@press:
    jsr credits_done_t
    cmp scr_t
    bcs @draw
    lda scr_t
    DIVC 24
    and #1
    bne :+
    PRINTC 200, TXP_ENDING_GREY, "PRESS START"
    bra @draw
:   lda #80
    sta txt_x
    lda #200
    sta txt_y
    lda #96
    sta txt_w
    lda #9
    sta txt_h
    jsl txt_clear
    bra @draw
@walk:
@draw:
    jsr ending_sprites
    plp
    rtl
@title:
    lda #9999
    sta scr_t
    lda #SC_TITLE
    jsl scr_go
    plp
    rtl

; C# CreditsDoneT = StoryEnd + (Bottom - Top + Credits.Length*14 - 60) * CreditSpeed
credits_done_t:
    lda #STORY_END + ((164 - 40) + CR_N*14 - 60) * 3
    rts

; the Princess's thanks typed out: en_chars characters over the story lines
type_story:
    ; type up to en_chars characters (one glyph each, the line centered on its full length like C# PadRight)
@l: lda en_typed
    cmp en_chars
    jcs @d
@again:
    lda en_col
    cmp en_len
    bcc @char
    ; next line
    inc en_line
    lda en_line
    cmp #SCR_ENDSTORY_N
    jcs @d
    asl a
    tax
    lda f:scr_endstory,x
    tax
    jsl sb_reset
    jsr sb_story
    ldx #0
@cp: lda sb_buf,x
    sta en_buf,x
    inx
    inx
    cpx #40
    bcc @cp
    lda sb_len
    sta en_len
    stz en_col
    asl a
    asl a
    asl a
    eor #$FFFF
    sec
    adc #256
    lsr a
    sta en_x
    lda en_line
    asl a
    sta en_y
    asl a
    asl a
    clc
    adc en_y
    adc en_line                 ; *11
    adc #38                     ; 46 + 11 i - 8
    sta en_y
    bra @again
@char:
    lda en_col
    asl a
    asl a
    asl a
    clc
    adc en_x
    sta txt_x
    lda en_y
    sta txt_y
    lda #TXP_ENDING_WHITE
    ldx en_line
    bne :+
    lda #TXP_ENDING_GOLD
:   sta txt_pal
    lda en_col
    clc
    adc #.loword(en_buf)
    sta txt_sp
    sep #$20
    .a8
    stz txt_sp+2
    rep #$20
    .a16
    lda #1
    jsl txt_printn
    inc en_col
    inc en_typed
    brl @l
@d: rts
.segment "BSS"
en_line: .res 2
en_col: .res 2
en_len: .res 2
en_x: .res 2
en_y: .res 2
en_typed: .res 2
en_buf: .res 40
.segment "CODE7"
.a16
.i16

; X = story line (CODE7) -> sb, "{0}" becomes the hero's name
sb_story:
    phb
    phk
    plb
@c: lda a:0,x
    and #$00FF
    beq @d
    cmp #'{'
    bne @ch
    inx
    inx
    inx
    phx
    lda ss_player
    bne :+
    SB "MARIO"
    bra :++
:   SB "LUIGI"
:   plx
    bra @c
@ch: phx
    jsl sb_char
    plx
    inx
    bra @c
@d: plb
    rts

; HDMA ch4 -> TM: BG2 (credits) only between lines 32 and 155; BG3 hidden 156-199 too (THE END stays inside)
tm_clip:
    sep #$20
    .a8
    lda #$00
    sta DMAP0+$40
    lda #<TM
    sta BBAD0+$40
    lda #^tm_tab
    sta A1B0+$40
    rep #$20
    .a16
    lda #.loword(tm_tab)
    sta A1T0L+$40
    lda scr_hdma
    ora #$10
    sta scr_hdma
    rts
tm_tab:
    .byte 32, $15               ; lines 0-31: BG1 BG3 OBJ
    .byte 124, $17              ; 32-155: + BG2
    .byte 44, $11               ; 156-199: BG1 OBJ
    .byte 1, $15                ; 200-: BG1 BG3 OBJ
    .byte 0

ending_sprites:
    jsl scr_obj_begin
    ; stars: 50, x = (i*83 + 11) % 256, y = (i*37) % 170 - 8, white when (i + t/16) % 4 == 0
    stz fl_tmp
@s: lda fl_tmp
    cmp #50
    bcs @sd
    lda fl_tmp
    ldx #83
    jsr mul8
    clc
    adc #11
    and #$00FF
    sta spr_x
    lda fl_tmp
    ldx #37
    jsr mul8
    DIVC 170
    txa
    sec
    sbc #8
    sta spr_y
    lda #$20
    sta spr_fl
    lda scr_t
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc fl_tmp
    and #3
    beq :+
    lda #SP_DOT_B
    bra :++
:   lda #SP_DOT_W
:   jsl scr_obj_put
    inc fl_tmp
    bra @s
@sd:
    ; 8 lanterns relit: x = 12 + 31i, y = 6 + (odd ? 4 : 0) - 8
    lda scr_t
    jsr lantern_pal
    stz fl_tmp
@l: lda fl_tmp
    cmp #8
    bcs @ld
    asl a
    asl a
    asl a
    asl a
    asl a
    sec
    sbc fl_tmp
    adc #11                     ; (carry set) 31i + 12
    sta spr_x
    lda fl_tmp
    and #1
    asl a
    asl a
    sec
    sbc #2
    sta spr_y
    lda #$20
    sta spr_fl
    lda #SP_LANTERN
    jsl scr_obj_put
    inc fl_tmp
    bra @l
@ld:
    ; the hero walks in: x = t < 160 ? -16 + t*136/160 : 120
    lda scr_t
    cmp #WALK_END
    bcs @stand
    ldx #136
    jsr mul8
    ldx #160
    jsl scr_div
    sec
    sbc #16
    sta spr_x
    lda scr_t
    DIVC 6
    and #1
    beq :+
    lda #SP_EMARIO_WALK2
    bra @hero
:   lda #SP_EMARIO_WALK1
    bra @hero
@stand:
    lda #120
    sta spr_x
    lda #SP_EMARIO_STAND
@hero:
    pha
    lda #136
    sta spr_y
    lda #$30
    sta spr_fl
    pla
    jsl scr_obj_put
    ; the Princess (waves every 40 ticks after the walk)
    lda #148
    sta spr_x
    lda #136
    sta spr_y
    lda #$30
    sta spr_fl
    lda scr_t
    cmp #WALK_END+1
    bcc @p1
    DIVC 40
    DIVC 3
    cpx #1
    bne @p1
    lda #SP_PRINCESS2
    bra @pp
@p1: lda #SP_PRINCESS1
@pp: jsl scr_obj_put
    rts
