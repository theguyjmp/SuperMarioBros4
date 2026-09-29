; Game core (engine agent): boot setup, title, level select, level flow (LevelScreen + Session), pause, HUD on BG3,
; player sprite selection (PlayerDraw.Frame), NMI glue. See snes/DESIGN.md and snes/TESTING.md.
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"
.include "spr_ids.inc"
.include "music.inc"

.export game_init, game_frame, eng_nmi
.export eng_px, eng_py, eng_pxvel, eng_pyvel, eng_cam_x, eng_cam_y
.importzp pad1, frame_count
.import eng_nmi_level, eng_anim_reset, eng_find_link, eng_font
.import ent_area_reset, ent_level_reset, ent_draw_effects
.import spr_begin, spr_end, spr_level_load, bg_update, bg_cam_x, bg_cam_y
.global spr_player, spr_meta, spr_arg_x, spr_arg_y, spr_arg_flags, spr_arg_id

; test-visible aliases (C# Player.Px/Py = eng_px/eng_py are computed each tick; X/Y/XVel/YVel in 1/16 px)
eng_pxvel = p_xvel
eng_pyvel = p_yvel
eng_cam_x = cam_x
eng_cam_y = cam_y

BG3_MAP = $5400

.segment "BSS"
g_mode: .res 2
g_level: .res 2
g_lives: .res 2
g_score: .res 4             ; BCD
g_coins: .res 2
g_cards: .res 6
g_ncards: .res 2
g_form: .res 2
g_hud_dirty: .res 2
g_paused: .res 2
g_tick: .res 2
g_timer: .res 2
g_sel: .res 2
g_banner: .res 2
pad_held: .res 2
pad_pressed: .res 2
pad_released: .res 2
pad_prev: .res 2
eng_px: .res 2
eng_py: .res 2
txq_n: .res 2               ; text queue: words used
scroll_x: .res 2
scroll_y: .res 2
bonus_lives: .res 2

.segment "HIBSS"
hud_buf: .res 256           ; BG3 rows 24-27 (4 x 32 words)
txq: .res 512               ; [vram addr, count, words...]*

.segment "CODE1"

; ================================================================== init
game_init:
    rep #$30
    .a16
    .i16
    sep #$20
    .a8
    lda #$80
    sta INIDISP
    lda #$09                    ; mode 1, BG3 priority
    sta BGMODE
    lda #$41                    ; BG1 map $4000, 64x32
    sta BG1SC
    lda #$49                    ; BG2 map $4800, 64x32
    sta BG2SC
    lda #$54                    ; BG3 map $5400, 32x32
    sta BG3SC
    lda #$30                    ; BG1 CHR $0000, BG2 CHR $3000
    sta BG12NBA
    lda #$05                    ; BG3 CHR $5000
    sta BG34NBA
    lda #$03
    sta OBSEL
    rep #$20
    .a16
    ; BG3 font -> VRAM $5000 (2bpp, 64 tiles)
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    lda #$5000
    sta VMADDL
    lda #.loword(eng_font)
    sta A1T0L
    sep #$20
    lda #^eng_font
    sta A1B0
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    lda #64*16
    sta DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    ; HUD palettes (CGRAM 1-31 = BG3 palettes 0-7 colors 1-3)
    ldx #0
@pal: txa
    and #3
    beq @ps
    sep #$20
    txa
    sta CGADD
    rep #$20
    phx
    txa
    asl a
    tax
    lda f:hud_pal,x
    plx
    sep #$20
    sta CGDATA
    xba
    sta CGDATA
    rep #$20
@ps: inx
    cpx #32
    bcc @pal
    lda #$FFFF
    sta f:cur_ts_ext
    sta f:spr_loaded
    jsr clear_bg3
    lda #GM_TITLE
    sta g_mode
    jsr title_enter
    sep #$20
    lda #$0F
    sta INIDISP
    rtl
.a16

; BG3 palettes: 0 white, 1 gold, 2 red, 3 dim, 4 green, 5 cyan, 6 orange, 7 black-ish
hud_pal:
    .word 0, $7FFF, $6F7B, $0842
    .word 0, $2BFF, $02FF, $0421
    .word 0, $6F7F, $109F, $0006
    .word 0, $3DEF, $2D6B, $0421
    .word 0, $6BFA, $03E0, $0100
    .word 0, $7FF0, $7EE0, $2100
    .word 0, $2F7F, $01DF, $0005
    .word 0, $4A52, $2108, $0000

cur_ts_ext = cur_ts
.global cur_ts, spr_loaded

; clear the whole BG3 map (forced blank)
clear_bg3:
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    lda #BG3_MAP
    sta VMADDL
    lda #.loword(zero_word)
    sta A1T0L
    sep #$20
    lda #^zero_word
    sta A1B0
    lda #$09
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    lda #2048
    sta DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    stz txq_n
    rts
zero_word: .word 0

; ================================================================== frame
game_frame:
    rep #$30
    ; C# PadState from the SNES pad: B/A = jump (Btn.A), Y/X = run (Btn.B)
    lda pad1
    sta e_t0
    lda #0
    bit e_t0
    bpl :+
    ora #BTN_A                  ; SNES B
:   bvc :+
    ora #BTN_B                  ; SNES Y
:   tax
    lda e_t0
    and #PAD_A
    beq :+
    txa
    ora #BTN_A
    tax
:   lda e_t0
    and #PAD_X
    beq :+
    txa
    ora #BTN_B
    tax
:   lda e_t0
    and #PAD_UP
    beq :+
    txa
    ora #BTN_UP
    tax
:   lda e_t0
    and #PAD_DOWN
    beq :+
    txa
    ora #BTN_DOWN
    tax
:   lda e_t0
    and #PAD_LEFT
    beq :+
    txa
    ora #BTN_LEFT
    tax
:   lda e_t0
    and #PAD_RIGHT
    beq :+
    txa
    ora #BTN_RIGHT
    tax
:   lda e_t0
    and #PAD_START
    beq :+
    txa
    ora #BTN_START
    tax
:   lda e_t0
    and #PAD_SELECT
    beq :+
    txa
    ora #BTN_SELECT
    tax
:   stx pad_held
    lda pad_prev
    eor #$FFFF
    and pad_held
    sta pad_pressed
    lda pad_held
    eor #$FFFF
    and pad_prev
    sta pad_released
    lda pad_held
    sta pad_prev
    lda g_mode
    asl a
    tax
    jsr (mode_tbl,x)
    sep #$20
    rtl
.a16
mode_tbl: .addr title_frame, select_frame, play_frame, gameover_frame

; ================================================================== title
title_enter:
    jsr blank_on
    jsr clear_bg3
    sep #$20
    lda #$04                    ; BG3 only
    sta TM
    rep #$20
    jsr print_now_begin
    lda #6
    ldx #6
    ldy #1
    jsr print_str
    .byte "SUPER MARIO BROS. 4", 0
    lda #10
    ldx #10
    ldy #0
    jsr print_str
    .byte "PRESS START", 0
    lda #14
    ldx #5
    ldy #3
    jsr print_str
    .byte "SELECT: LEVEL SELECT", 0
    lda #24
    ldx #6
    ldy #3
    jsr print_str
    .byte "SNES ENGINE PORT M1", 0
    jsr print_now_end
    MUSIC "TITLE"
    jsr blank_off
    stz g_timer
    rts

title_frame:
    jsl spr_begin_l
    jsl spr_end_l
    lda pad_pressed
    and #(BTN_START|BTN_A)
    beq :+
    stz g_level
    jmp new_game
:   lda pad_pressed
    and #BTN_SELECT
    beq :+
    lda #GM_SELECT
    sta g_mode
    stz g_sel
    jmp select_enter
:   rts

new_game:
    lda #4
    sta g_lives
    stz g_score
    stz g_score+2
    stz g_coins
    stz g_ncards
    stz g_form
    lda #GM_PLAY
    sta g_mode
    jmp start_level

; ================================================================== level select
select_enter:
    jsr blank_on
    jsr clear_bg3
    jsr print_now_begin
    lda #1
    ldx #10
    ldy #1
    jsr print_str
    .byte "LEVEL SELECT", 0
    lda #26
    ldx #3
    ldy #3
    jsr print_str
    .byte "DPAD + START / B TO PLAY", 0
    jsr draw_select
    jsr print_now_end
    jsr blank_off
    rts

; draws every level id: world rows 3,5,..,17 ; column = index inside the world
draw_select:
    stz e_t6
@l: lda e_t6
    cmp #LEVEL_COUNT
    bcs @d
    jsr draw_entry
    inc e_t6
    bra @l
@d: rts

; draw_entry: e_t6 = level index (palette 1 if selected)
draw_entry:
    ; column = number of earlier levels of the same world
    jsr level_hdr
    lda [e_p0]
    and #$00FF
    sta de_world
    stz de_col
    lda e_t6
    sta de_idx
@c: lda e_t6
    beq @cd
    dec e_t6
    jsr level_hdr
    lda [e_p0]
    and #$00FF
    cmp de_world
    bne @cd
    inc de_col
    bra @c
@cd: lda de_idx
    sta e_t6
    jsr level_hdr
    ; world number at col 2
    lda de_world
    clc
    adc #'0'-32
    ora #(3<<10)|$2000
    sta pw_word
    lda de_world
    asl a
    inc a
    sta de_row
    ldx #1
    jsr put_word
    ; 3 chars of the name
    lda #$2000
    ldx de_idx
    cpx g_sel
    bne :+
    lda #$2000|(1<<10)
:   sta de_pal
    lda de_col
    asl a
    asl a
    clc
    adc #4
    sta de_c
    ldy #9
@ch: lda [e_p0],y
    and #$00FF
    sec
    sbc #32
    ora de_pal
    sta pw_word
    phy
    lda de_row
    ldx de_c
    jsr put_word
    ply
    inc de_c
    iny
    cpy #12
    bcc @ch
    rts
.segment "BSS"
de_world: .res 2
de_col: .res 2
de_idx: .res 2
de_row: .res 2
de_pal: .res 2
de_c: .res 2
pw_word: .res 2
pw_addr: .res 2
.segment "CODE1"

; e_t6 = level index -> e_p0 = level header
level_hdr:
    lda e_t6
    asl a
    adc e_t6
    tax
    lda f:eng_lvl_dir_ext,x
    sta e_p0
    lda f:eng_lvl_dir_ext+1,x
    sta e_p0+1
    rts
.import eng_lvl_dir
eng_lvl_dir_ext = eng_lvl_dir

select_frame:
    jsl spr_begin_l
    jsl spr_end_l
    lda pad_pressed
    and #(BTN_START|BTN_A)
    beq :+
    lda g_sel
    sta g_level
    jmp new_game
:   lda pad_pressed
    and #BTN_SELECT
    beq :+
    lda #GM_TITLE
    sta g_mode
    jmp title_enter
:   ldx g_sel
    lda pad_pressed
    and #BTN_RIGHT
    beq :+
    inx
:   lda pad_pressed
    and #BTN_LEFT
    beq :+
    dex
:   lda pad_pressed
    and #BTN_DOWN
    beq :+
    txa
    clc
    adc #7
    tax
:   lda pad_pressed
    and #BTN_UP
    beq :+
    txa
    sec
    sbc #7
    tax
:   txa
    bpl :+
    lda #0
:   cmp #LEVEL_COUNT
    bcc :+
    lda #LEVEL_COUNT-1
:   cmp g_sel
    beq @r
    ldx g_sel
    sta g_sel
    stx e_t6
    jsr draw_entry              ; old entry back to white (queued for the NMI)
    lda g_sel
    sta e_t6
    jsr draw_entry
@r: rts

; ================================================================== game over
gameover_enter:
    jsr blank_on
    jsr clear_bg3
    sep #$20
    lda #$04
    sta TM
    rep #$20
    jsr print_now_begin
    lda #12
    ldx #11
    ldy #2
    jsr print_str
    .byte "GAME OVER", 0
    jsr print_now_end
    jsr blank_off
    lda #180
    sta g_timer
    MUSIC "GAMEOVER"
    rts

gameover_frame:
    jsl spr_begin_l
    jsl spr_end_l
    dec g_timer
    bne :+
    lda #GM_TITLE
    sta g_mode
    jmp title_enter
:   rts

; ================================================================== level flow
; start_level: g_level; lives/score/form kept in the session
start_level:
    jsr blank_on
    jsr clear_bg3
    lda g_level
    jsl eng_load_level
    ; World constructor
    lda lvl_time_def
    sta w_time
    lda #40
    sta w_timetick
    stz w_frame
    stz w_wiggly
    stz w_halt
    stz w_pswitch
    stz w_endtimer
    stz w_clearing
    stz w_result
    stz w_autoscroll
    stz w_hurry
    stz w_shake
    stz w_bossarena
    stz w_psound
    stz w_hurrywait
    stz eng_tick_count
    lda #$FFFF
    sta w_cardgot
    stz g_paused
    stz g_banner
    stz bonus_lives
    lda g_form
    jsl pl_init
    jsl ent_level_reset
    lda lvl_start_area
    jsl eng_load_area
    jsl ent_area_reset
    ; P.X = (StartX*16) << 4 ; P.Y = ((StartY + 1)*16 - 32) << 4
    lda lvl_start_x
    xba
    sta p_x
    lda lvl_start_y
    inc a
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #32
    asl a
    asl a
    asl a
    asl a
    sta p_y
    lda #1
    sta p_inair
    jsl eng_center_camera
    jsl ent_spawn_initial
    jsr show_area
    rts


; area is loaded and the camera is set: draw everything, music, screen on
show_area:
    jsl eng_draw_all
    jsl eng_anim_reset
    jsr hud_build
    jsr hud_upload_now
    sep #$20
    lda #$17                    ; BG1 BG2 BG3 OBJ
    sta TM
    rep #$20
    jsl w_play_area_music
    jsr update_eng_pos
    jsr set_scroll
    jsr blank_off
    rts

; called by the world (ArriveAt): A = area to load (forced blank on, entities reset)
game_arrive_load:
    stz w_pswitch               ; C# LoadArea: the P-switch ends without swapping back
    pha
    jsr blank_on
    pla
    jsl eng_load_area
    jsl ent_area_reset
    rtl
game_arrive_show:
    jsl ent_spawn_initial
    jsr show_area
    rtl

; A = card: Session.AddCard -> bonus lives
game_add_card:
    ldx g_ncards
    cpx #6
    bcs @r
    sta g_cards,x
    inx
    inx
    stx g_ncards
    cpx #6
    bcc @r
    ; three cards: 5 lives for 3 stars, 3 for flowers, 2 for mushrooms, else 1
    stz g_ncards
    lda g_cards
    cmp g_cards+2
    bne @one
    cmp g_cards+4
    bne @one
    tax
    lda #2
    cpx #0
    beq @b
    lda #3
    cpx #1
    beq @b
    lda #5
    bra @b
@one: lda #1
@b: sta bonus_lives
    clc
    adc g_lives
    cmp #100
    bcc :+
    lda #99
:   sta g_lives
    MUSIC "BONUS1UP"
@r: lda #1
    sta g_hud_dirty
    rtl

play_frame:
    ; ---- pause (LevelScreen: Start when the level is running normally)
    lda g_paused
    beq @notp
    lda pad_pressed
    and #BTN_START
    beq @draw
    stz g_paused
    sep #$20
    lda #0
    jsl snd_pause
    rep #$30
    SFX "PAUSE"
    jsr unpause_text
    bra @draw
@notp:
    lda pad_pressed
    and #BTN_START
    beq @tick
    lda w_result
    ora w_endtimer
    bne @tick
    lda p_state
    cmp #PS_DYING
    beq @tick
    lda #1
    sta g_paused
    sep #$20
    lda #1
    jsl snd_pause
    rep #$30
    SFX "PAUSE"
    jsr pause_text
    bra @draw
@tick:
    jsl w_tick
    jsr update_eng_pos
    lda w_result
    beq @draw
    jmp level_result
@draw:
    jsl eng_stream
    jsl eng_anim_tiles
    jsr draw_sprites
    jsr hud_update
    jsr banner_update
    jsr set_scroll
    ; backgrounds follow the camera
    lda cam_x
    sta bg_cam_x
    lda cam_y
    sta bg_cam_y
    sep #$20
    jsl bg_update
    rep #$30
    rts

update_eng_pos:
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    sta eng_px
    lda p_y
    ASR4
    sta eng_py
    rts

set_scroll:
    lda cam_x
    sta scroll_x
    lda #0
    ldx w_shake
    beq @ns
    lda w_shake
    and #2
    beq :+
    lda #2
    bra @ns
:   lda #$FFFE
@ns: clc
    adc cam_y
    dec a
    sta scroll_y
    rts

level_result:
    cmp #1
    beq @cleared
    ; died: lives--, restart or game over (form resets to small)
    stz g_form
    lda g_lives
    beq @over
    dec g_lives
    lda g_lives
    beq @over
    jmp start_level
@over:
    lda #GM_GAMEOVER
    sta g_mode
    jmp gameover_enter
@cleared:
    lda p_form
    sta g_form
    lda g_level
    inc a
    cmp #LEVEL_COUNT
    bcc :+
    lda #0
:   sta g_level
    jmp start_level

; ================================================================== sprites for a play frame
draw_sprites:
    jsl spr_begin_l
    jsl ent_draw
    lda p_state
    cmp #PS_DYING
    beq :+
    jsr pl_draw
:   jsl ent_draw_effects
    lda p_state
    cmp #PS_DYING
    bne :+
    jsr pl_draw
:   jsl spr_end_l
    rts

spr_begin_l:
    sep #$20
    jsl spr_begin
    rep #$30
    rtl
spr_end_l:
    sep #$20
    jsl spr_end
    rep #$30
    rtl

; ------------------------------------------------------------------ PlayerDraw
POSE_I_STAND = 0
POSE_I_WALK = 1
POSE_I_WALK1 = 2
POSE_I_WALK2 = 3
POSE_I_RUN1 = 4
POSE_I_RUN2 = 5
POSE_I_RUN3 = 6
POSE_I_JUMP = 7
POSE_I_PJUMP = 8
POSE_I_SKID = 9
POSE_I_DUCK = 10
POSE_I_FRONT = 11
POSE_I_KICK = 12
POSE_I_HOLD1 = 13
POSE_I_HOLD2 = 14
POSE_I_THROW = 15
POSE_I_SWIM1 = 16
POSE_I_SWIM2 = 17
POSE_I_SWIM3 = 18
POSE_I_CLIMB1 = 19
POSE_I_CLIMB2 = 20
POSE_I_SLIDE = 21
POSE_I_SPINFRONT = 22
POSE_I_SPINBACK = 23
POSE_I_DEATH = 24
POSE_I_STATUE = 25

.macro POSEW name
.ifdef .ident(.concat("POSE_", name))
    .word .ident(.concat("POSE_", name))
.else
    .word $FFFF
.endif
.endmacro
pose_ids:
    POSEW "STAND"
    POSEW "WALK"
    POSEW "WALK1"
    POSEW "WALK2"
    POSEW "RUN1"
    POSEW "RUN2"
    POSEW "RUN3"
    POSEW "JUMP"
    POSEW "PJUMP"
    POSEW "SKID"
    POSEW "DUCK"
    POSEW "FRONT"
    POSEW "KICK"
    POSEW "HOLD1"
    POSEW "HOLD2"
    POSEW "THROW"
    POSEW "SWIM1"
    POSEW "SWIM2"
    POSEW "SWIM3"
    POSEW "CLIMB1"
    POSEW "CLIMB2"
    POSEW "SLIDE"
    POSEW "SPINFRONT"
    POSEW "SPINBACK"
    POSEW "DEATH"
    POSEW "STATUE"
big_cycle: .word POSE_I_WALK1, POSE_I_WALK2, POSE_I_STAND, POSE_I_WALK2
small_cycle: .word POSE_I_WALK, POSE_I_WALK, POSE_I_STAND, POSE_I_STAND
big_run: .word POSE_I_RUN1, POSE_I_RUN2, POSE_I_RUN3, POSE_I_RUN2
small_run: .word POSE_I_RUN1, POSE_I_RUN2, POSE_I_RUN1, POSE_I_RUN2

; e_t0 = form to draw, e_t1 = pose index, spr_arg_flags built
pl_draw:
    lda p_invisible
    beq :+
    rts
:   lda p_hurtinv
    beq :+
    lda p_state
    bne :+
    lda p_transform
    bne :+
    lda p_hurtinv
    and #2
    beq :+
    rts
:   lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    sec
    sbc cam_x
    sta spr_arg_x
    lda p_y
    ASR4
    sec
    sbc cam_y
    sta spr_arg_y
    stz spr_arg_flags
    lda p_facing
    bpl :+
    inc spr_arg_flags
:   lda p_state
    cmp #PS_PIPE
    bne :+
    lda spr_arg_flags
    ora #4
    sta spr_arg_flags
:   ; palette flashes
    lda p_star
    beq @nostar
    ; k = (Frame / (Star < 96 ? 4 : 2)) & 3 -> flags bit 10 + bits 11-12
    lda w_frame
    lsr a
    ldx p_star
    cpx #96
    bcs :+
    lsr a
:   and #3
    xba
    asl a
    asl a
    asl a
    ora #$0400
    ora spr_arg_flags
    sta spr_arg_flags
@nostar:
    lda p_transform
    beq @notr
    lda p_transformkind
    cmp #4
    bne :+
    lda p_transform
    and #2
    bne @notr
    lda spr_arg_flags
    ora #$2000                  ; bit 13: power-up flash palette ("pflash")
    sta spr_arg_flags
    bra @notr
:   cmp #3
    bne :+
    lda p_transform
    and #4
    bne @notr
    rts                         ; poof: flicker
:   ; grow / shrink: flicker between the two sizes
    lda p_transform
    lsr a
    lsr a
    and #1
    sta e_t2
    ldx #1
    lda p_transformkind
    cmp #1
    beq :+
    ldx #0
:   stx e_t3
    lda e_t2
    cmp e_t3
    bne @showsmall
    lda #PF_BIG
    sta e_t0
    bra @trpose
@showsmall:
    stz e_t0
@trpose:
    lda #POSE_I_STAND
    ldx p_inair
    beq :+
    lda #POSE_I_JUMP
:   sta e_t1
    jmp @emit
@notr:
    jsr pl_frame
    ; tail pose (bits 8-9): 0 down, 1 mid, 2 up, 3 spin-side
    jsr tail_pose
    xba
    ora spr_arg_flags
    sta spr_arg_flags
    ; somersault
    lda p_somersault
    beq @emit
    lda p_inair
    beq @emit
    lda p_state
    bne @emit
    lda w_frame
    ldy #0
@d3: cmp #3
    bcc @d3d
    sbc #3
    iny
    bra @d3
@d3d: tya
    and #3
    cmp #1
    beq @fh
    cmp #2
    bne :+
    lda spr_arg_flags
    eor #3
    sta spr_arg_flags
    bra @emit
:   cmp #3
    bne @emit
    lda spr_arg_flags
    eor #2
    sta spr_arg_flags
    bra @emit
@fh: lda spr_arg_flags
    eor #1
    sta spr_arg_flags
@emit:
    lda e_t1
    asl a
    tax
    lda f:pose_ids,x
    bmi @r
    tax
    sep #$20
    lda e_t0
    jsl spr_player
    rep #$30
@r: rts

; PlayerDraw.Frame -> e_t0 = form to draw, e_t1 = pose index
pl_frame:
    lda p_form
    sta e_t0
    lda p_state
    cmp #PS_DYING
    bne :+
    stz e_t0
    lda #POSE_I_DEATH
    bra @set
:   lda p_statue
    beq :+
    lda #POSE_I_STATUE
    bra @set
:   lda p_state
    cmp #PS_PIPE
    bne :+
    lda p_pipedir
    cmp #2
    beq @front
    cmp #8
    beq @front
:   lda p_state
    cmp #PS_DOOR
    beq @front
    cmp #PS_VINE
    beq @climb
    lda p_climbing
    bne @climb
    lda p_swimming
    beq @nosw
    lda p_inair
    beq @nosw
    lda p_swimanim
    cmp #9
    bcc :+
    lda #POSE_I_SWIM1
    bra @set
:   cmp #0
    beq :+
    lda #POSE_I_SWIM2
    bra @set
:   lda #POSE_I_SWIM3
@set: sta e_t1
    rts
@front: lda #POSE_I_FRONT
    bra @set
@climb:
    lda p_climbanim
    lsr a
    lsr a
    lsr a
    and #1
    clc
    adc #POSE_I_CLIMB1
    bra @set
@nosw:
    lda p_tailattack
    beq @notail
    lda p_form
    cmp #PF_RACCOON
    beq :+
    cmp #PF_TANOOKI
    bne @notail
:   lda p_tailattack
    cmp #14
    bcs @stand
    cmp #10
    bcs :+
    cmp #6
    bcs @stand
    lda #POSE_I_SPINBACK
    bra @set
:   lda #POSE_I_SPINFRONT
    bra @set
@stand: lda #POSE_I_STAND
    bra @set
@notail:
    lda p_sliding
    beq :+
    lda #POSE_I_SLIDE
    ldx p_form
    bne @set
    lda #POSE_I_JUMP
    bra @set
:   lda p_ducking
    beq :+
    lda p_form
    beq :+
    lda #POSE_I_DUCK
    bra @set
:   lda p_kickpose
    beq :+
    lda #POSE_I_KICK
    bra @set
:   lda p_throwpose
    beq :+
    lda p_form
    beq :+
    lda #POSE_I_THROW
    brl @set
:   lda p_inair
    beq @ground
    lda p_carrying
    beq :+
    lda #POSE_I_HOLD1
    brl @set
:   lda p_pjumppose
    ora p_flytime
    beq :+
    lda #POSE_I_PJUMP
    brl @set
:   lda #POSE_I_JUMP
    brl @set
@ground:
    lda p_skidding
    beq :+
    lda p_carrying
    bne :+
    lda #POSE_I_SKID
    brl @set
:   lda p_carrying
    beq :+
    lda #POSE_I_HOLD1
    ldx p_animframe
    cpx #2
    bcc @set2
    lda #POSE_I_HOLD2
@set2: jmp @set
:   lda p_xvel
    bpl :+
    eor #$FFFF
    inc a
:   cmp #$37
    bcc @walk
    lda p_flytime
    bne @walk
    lda p_animframe
    and #3
    asl a
    tax
    lda p_form
    beq :+
    lda f:big_run,x
    jmp @set
:   lda f:small_run,x
    jmp @set
@walk:
    lda p_animframe
    and #3
    asl a
    tax
    lda p_form
    beq :+
    lda f:big_cycle,x
    jmp @set
:   lda f:small_cycle,x
    jmp @set

tail_pose:
    lda p_form
    cmp #PF_RACCOON
    beq :+
    cmp #PF_TANOOKI
    beq :+
    lda #0
    rts
:   lda e_t1
    cmp #POSE_I_SPINFRONT
    beq @spin
    cmp #POSE_I_SPINBACK
    beq @spin
    lda p_tailattack
    ora p_ducking
    ora p_sliding
    bne @mid
    lda p_inair
    beq @gnd
    lda p_wagcount
    beq :+
    lda w_frame
    and #4
    bne @mid
    lda #2
    rts
:   lda p_yvel
    bmi @mid
    lda #0
    rts
@gnd:
    lda p_xvel
    bpl :+
    eor #$FFFF
    inc a
:   cmp #$28
    bcc @down
    lda w_frame
    and #4
    bne @down
@mid: lda #1
    rts
@down: lda #0
    rts
@spin:
    lda p_tailattack
    cmp #14
    bcs @down
    cmp #10
    bcs @side
    cmp #6
    bcs @down
@side: lda #3
    rts

; ================================================================== HUD (BG3 rows 24-27)

hud_update:
    lda g_hud_dirty
    bne :+
    ; the P badge flashes and the P-meter changes: rebuild when power changes
    lda p_power
    cmp hud_lastpow
    bne :+
    lda w_frame
    and #8
    cmp hud_lastfl
    bne :+
    rts
:   stz g_hud_dirty
    jsr hud_build
    lda #1
    sta hud_pend
    rts

hud_build:
    lda p_power
    sta hud_lastpow
    lda w_frame
    and #8
    sta hud_lastfl
    ; clear
    ldx #0
    lda #0
:   sta f:hud_buf,x
    inx
    inx
    cpx #256
    bcc :-
    ; row 25 (buffer row 1): WORLD n-n
    ldx #1*64+2
    ldy #0
    lda #'W'
    jsr hud_ch
    lda #'O'
    jsr hud_ch
    lda #'R'
    jsr hud_ch
    lda #'L'
    jsr hud_ch
    lda #'D'
    jsr hud_ch
    inx
    inx
    ; level name from the header (4 chars)
    lda lvl_ptr
    sta e_p0
    lda lvl_ptr+1
    sta e_p0+1
    phy
    ldy #9
    lda [e_p0],y
    and #$00FF
    sta e_t0
    iny
    lda [e_p0],y
    and #$00FF
    sta e_t1
    iny
    lda [e_p0],y
    and #$00FF
    sta e_t2
    ply
    ldy #1
    lda e_t0
    jsr hud_ch
    lda e_t1
    jsr hud_ch
    lda e_t2
    jsr hud_ch
    ; P-meter: 6 arrows + P
    ldx #1*64+26
    lda #0
    sta e_t3
@ar: lda e_t3
    tay
    lda #1
@sh: dey
    bmi @shd
    asl a
    bra @sh
@shd: and p_power
    beq :+
    ldy #6
    bra :++
:   ldy #3
:   lda #'^'
    jsr hud_ch
    inc e_t3
    lda e_t3
    cmp #6
    bcc @ar
    ; P badge: lit when full (flashing while flying / at full power)
    ldy #3
    lda p_power
    cmp #$7F
    bcc :+
    ldy #2
    lda w_frame
    and #8
    beq :+
    ldy #0
:   lda #'P'
    jsr hud_ch
    ; coins
    ldx #1*64+44
    ldy #1
    lda #'$'
    jsr hud_ch
    inx
    inx
    ldy #0
    lda g_coins
    jsr hud_dec2
    ; row 26: M*lives  score  @time
    ldx #2*64+2
    ldy #2
    lda #'M'
    jsr hud_ch
    ldy #0
    lda #'*'
    jsr hud_ch
    lda g_lives
    jsr hud_dec2
    ldx #2*64+18
    ; score: 7 BCD digits
    ldy #0
    lda g_score+2
    xba
    jsr hud_dig
    lda g_score+2
    lsr a
    lsr a
    lsr a
    lsr a
    jsr hud_dig
    lda g_score+2
    jsr hud_dig
    lda g_score
    xba
    lsr a
    lsr a
    lsr a
    lsr a
    jsr hud_dig
    lda g_score
    xba
    jsr hud_dig
    lda g_score
    lsr a
    lsr a
    lsr a
    lsr a
    jsr hud_dig
    lda g_score
    jsr hud_dig
    ; time
    lda lvl_time_def
    beq @notime
    ldx #2*64+36
    ldy #0
    lda #'@'
    jsr hud_ch
    ldy #0
    lda w_time
    cmp #101
    bcs :+
    lda w_frame
    and #16
    bne :+
    ldy #2
:   lda w_time
    jsr hud_dec3
@notime:
    ; cards
    ldx #2*64+50
    ldy #0
@cd: cpy g_ncards
    bcs @cdd
    phy
    phx
    lda g_cards,y
    tax
    lda f:card_pal,x
    and #$00FF
    tay
    lda f:card_ch,x
    and #$00FF
    plx
    jsr hud_ch
    inx
    inx
    ply
    iny
    iny
    bra @cd
@cdd:
    rts
card_ch: .byte "M", 0, "F", 0, "S", 0
card_pal: .byte 2, 0, 6, 0, 1, 0
hud_lastpow = hud_tmp
hud_lastfl = hud_tmp+2
hud_pend = hud_tmp+4
.segment "BSS"
hud_tmp: .res 6
.segment "CODE1"

; A = char, Y = palette, X = buffer offset (advanced by 2)
hud_ch:
    sec
    sbc #32
    and #$3F
    sta e_t7
    tya
    xba
    asl a
    asl a
    ora e_t7
    ora #$2000
    sta f:hud_buf,x
    inx
    inx
    rts
; digit in A low nibble
hud_dig:
    and #15
    clc
    adc #'0'
    jmp hud_ch
hud_hexdig:
    and #15
    clc
    adc #'0'
    jmp hud_ch
; A = 0..99 -> 2 digits
hud_dec2:
    ldy #0
@t: cmp #10
    bcc @d
    sbc #10
    iny
    bra @t
@d: pha
    tya
    ldy #0
    clc
    adc #'0'
    jsr hud_ch
    pla
    clc
    adc #'0'
    jmp hud_ch
; A = 0..999 -> 3 digits, Y = palette
hud_dec3:
    sty e_t6
    ldy #0
@h: cmp #100
    bcc @hd
    sbc #100
    iny
    bra @h
@hd: sta e_t5
    tya
    clc
    adc #'0'
    ldy e_t6
    jsr hud_ch
    lda e_t5
    ldy #0
@t: cmp #10
    bcc @td
    sbc #10
    iny
    bra @t
@td: sta e_t5
    tya
    clc
    adc #'0'
    ldy e_t6
    jsr hud_ch
    lda e_t5
    clc
    adc #'0'
    ldy e_t6
    jmp hud_ch

; forced blank: upload the HUD now
hud_upload_now:
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    lda #BG3_MAP+24*32
    sta VMADDL
    jsr dma_hud
    stz hud_pend
    rts
dma_hud:
    lda #.loword(hud_buf)
    sta A1T0L
    sep #$20
    lda #^hud_buf
    sta A1B0
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    lda #256
    sta DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    rts

; ================================================================== banners / pause text (BG3 over the playfield)
pause_text:
    lda #10
    ldx #13
    ldy #0
    jsr print_str
    .byte "PAUSE", 0
    rts
unpause_text:
    lda #10
    ldx #13
    ldy #0
    jsr print_str
    .byte "     ", 0
    rts

banner_update:
    lda w_endtimer
    cmp #70
    bne @r
    lda g_banner
    bne @r
    inc g_banner
    lda #6
    ldx #9
    ldy #0
    jsr print_str
    .byte "COURSE CLEAR!", 0
    lda w_cardgot
    bmi @r
    lda #8
    ldx #9
    ldy #1
    jsr print_str
    .byte "YOU GOT A CARD", 0
@r: rts

; ------------------------------------------------------------------ text output
; print_str: A = row, X = col, Y = palette; the zero-terminated string follows the JSR.
; Queued for the NMI (or written directly between print_now_begin/end during forced blank).
print_str:
    sta e_t0
    stx e_t1
    sty e_t2
    ; string address = return address + 1 (in this bank)
    pla
    inc a
    sta e_p1
    sep #$20
    phk
    pla
    sta e_p1+2
    rep #$20
    ; count characters
    ldy #0
@n: lda [e_p1],y
    and #$00FF
    beq @nd
    iny
    bra @n
@nd: sty e_t3
    ; return address after the terminator
    tya
    clc
    adc e_p1
    pha                         ; (return address - 1 + 1: RTS adds 1)
    ; VRAM address
    lda e_t0
    asl a
    asl a
    asl a
    asl a
    asl a
    clc
    adc e_t1
    adc #BG3_MAP
    sta e_t4
    lda print_direct
    bne @direct
    ; queue: addr, count, words
    lda txq_n
    clc
    adc e_t3
    adc #2
    cmp #250
    bcs @full
    lda txq_n
    asl a
    tax
    lda e_t4
    sta f:txq,x
    lda e_t3
    sta f:txq+2,x
    inx
    inx
    inx
    inx
    ldy #0
@q: cpy e_t3
    beq @qd
    jsr str_word
    sta f:txq,x
    inx
    inx
    iny
    bra @q
@qd: txa
    lsr a
    sta txq_n
@full:
    rts
@direct:
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    lda e_t4
    sta VMADDL
    ldy #0
@w: cpy e_t3
    beq @wd
    jsr str_word
    sta VMDATAL
    iny
    bra @w
@wd: rts

str_word:
    lda [e_p1],y
    and #$00FF
    sec
    sbc #32
    and #$3F
    sta e_t5
    lda e_t2
    xba
    asl a
    asl a
    ora e_t5
    ora #$2000
    rts

print_now_begin:
    lda #1
    sta print_direct
    rts
print_now_end:
    stz print_direct
    rts
.segment "BSS"
print_direct: .res 2
.segment "CODE1"

; A = row, X = col, word in pw_word: write one BG3 word (direct or queued)
put_word:
    asl a
    asl a
    asl a
    asl a
    asl a
    stx pw_addr
    clc
    adc pw_addr
    adc #BG3_MAP
    sta pw_addr
    lda print_direct
    beq @q
    lda pw_addr
    sta VMADDL
    lda pw_word
    sta VMDATAL
    rts
@q: lda txq_n
    cmp #250
    bcs @r
    asl a
    tax
    lda pw_addr
    sta f:txq,x
    lda #1
    sta f:txq+2,x
    lda pw_word
    sta f:txq+4,x
    lda txq_n
    clc
    adc #3
    sta txq_n
@r: rts

; ------------------------------------------------------------------ screen on/off
blank_on:
    sep #$20
    lda #$80
    sta INIDISP
    rep #$20
    rts
blank_off:
    sep #$20
    lda #$0F
    sta INIDISP
    rep #$20
    rts

; ================================================================== NMI (A8 XY16, DB=$80)
.a8
eng_nmi:
    lda g_mode
    cmp #GM_PLAY
    bne @noplay
    jsl eng_nmi_level
@noplay:
    rep #$20
    .a16
    ; text queue
    lda txq_n
    beq @tqd
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    ldx #0
@tq: txa
    lsr a
    cmp txq_n
    bcs @tqe
    lda f:txq,x
    sta VMADDL
    lda f:txq+2,x
    tay
    inx
    inx
    inx
    inx
@tw: lda f:txq,x
    sta VMDATAL
    inx
    inx
    dey
    bne @tw
    bra @tq
@tqe: stz txq_n
@tqd:
    lda hud_pend
    beq @nh
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    lda #BG3_MAP+24*32
    sta VMADDL
    jsr dma_hud
    stz hud_pend
@nh:
    sep #$20
    .a8
    lda scroll_x
    sta BG1HOFS
    lda scroll_x+1
    sta BG1HOFS
    lda scroll_y
    sta BG1VOFS
    lda scroll_y+1
    sta BG1VOFS
    stz BG3HOFS
    stz BG3HOFS
    lda #$FF
    sta BG3VOFS
    sta BG3VOFS
    rtl
