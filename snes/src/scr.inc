; Screens module shared declarations (screens agent). Included by scr_*.s only.
.macpack longbranch
; Internal convention: routines are JSL/RTL, entered/left with A 16-bit, X/Y 16-bit (AXY16), D = 0, DB = $80,
; unless noted. Public module entry points (scr_init/scr_frame/scr_nmi) follow DESIGN.md (A8 XY16).
.include "snes.inc"
.include "scr_ids.inc"
.include "music.inc"
.include "levels.inc"

.ifndef LVL_HB
LVL_HB = LEVEL_COUNT            ; Hammer Bro battle (sprites: SPR_LEVEL_HB = 54 follows the levels)
.endif

; ---------------------------------------------------------------- screen ids (scr_core.s dispatch table order)
SC_NONE      = 0
SC_TITLE     = 1
SC_FILES     = 2
SC_MAP       = 3
SC_INTRO     = 4
SC_LEVEL     = 5      ; the engine is running a level
SC_TOAD      = 6
SC_SPADE     = 7
SC_NSPADE    = 8
SC_GAMEOVER  = 9
SC_WORLDCLR  = 10
SC_STORY     = 11
SC_ENDING    = 12
SC_OPTIONS   = 13
SC_HELP      = 14
SC_LVLSEL    = 15
SC_COUNT     = 16

; level results (engine g_result; RES_* as proposed by the bosses agent + EXITED)
RES_NONE     = 0
RES_CLEARED  = 1
RES_DIED     = 2
RES_FORTRESS = 5
RES_WORLD    = 6
RES_GAME     = 7
RES_TIMEUP   = 3
RES_EXITED   = 4

; C# PadState-like buttons for menus (SNES pad1|pad2): OK = B/A (jump), BACK = Y/X (run)
KEY_OK   = PAD_A | PAD_B
KEY_BACK = PAD_Y | PAD_X

; forms (C# Form)
FM_SMALL = 0
FM_BIG = 1
FM_FIRE = 2
FM_RACCOON = 3
FM_TANOOKI = 4
FM_FROG = 5
FM_HAMMER = 6

GM_PLAY_ = 2
PP_SIZE = 48
PP_SIZE_ = PP_SIZE
GM_SCREEN = 4

; ---------------------------------------------------------------- core (scr_core.s)
.global scr_t, scr_go, scr_go_now, scr_scene_load, scr_map_put, scr_map_put_at, scr_map_clear2
.global scr_obj_begin, scr_obj_put, scr_obj_end, scr_obj_pal, spr_x, spr_y, spr_fl
.global scr_navv, scr_navh, scr_pressed, scr_held, scr_rand, scr_sfx, scr_music
.global scr_bg1x, scr_bg1y, scr_bg2x, scr_bg2y, scr_bg3y, scr_lattice, scr_bg2_off
.global scr_vq_add, scr_cg_set, scr_music_cur, scr_screen, scr_frames, scr_hdma, scr_map_now, scr_tm
.global scr_tmp0, scr_tmp1, scr_tmp2, scr_tmp3, scr_tmp4, scr_tmp5, scr_ptr
.globalzp scr_zp0, scr_zp1, scr_zp2

; ---------------------------------------------------------------- text canvas (scr_text.s)
.global txt_clear_all, txt_print, txt_printc, txt_clear, txt_pal, txt_x, txt_y, txt_flush_now
.global sb_reset, sb_str, sb_dec, sb_dec_pad, sb_bcd7, sb_char, sb_buf, sb_len, txt_print_sb, txt_printc_sb

; ---------------------------------------------------------------- session / save (scr_save.s)
.global sv_load_slots, sv_slot_exists, sv_slot_info, sv_select, sv_save, sv_erase, sv_new
.global sv_world, sv_highest, sv_two, sv_turn, sv_beaten, sv_mapx, sv_mapy, sv_frames, sv_nodes, sv_hbdead, sv_hbpos
.global sv_nsp, sv_ship, sv_opt_inf, sv_opt_music, sv_exists
.global ss_player, ss_pwing, ss_star, ss_cur
.global pp_lives, pp_score, pp_coins, pp_nspade, pp_form, pp_cards, pp_ncards, pp_items, pp_nitems, pp_over
.global sv_to_engine, sv_from_engine, sv_add_life, sv_lose_life, sv_add_coin, sv_add_item, sv_cur_slot, sv_info
.global sv_opts_save

; ---------------------------------------------------------------- engine (game.s)
.global g_mode, g_level, g_lives, g_score, g_coins, g_cards, g_ncards, g_form, g_result, g_player, g_pwing, g_star
.global eng_level_start, g_allowexit
.global snd_music, snd_sfx, snd_fade, bg_hdma_off, snd_cur

.macro SCR_A8
    sep #$20
    .a8
.endmacro
.macro SCR_A16
    rep #$20
    .a16
.endmacro

; ---------------------------------------------------------------- text macros (A16)
.macro PRINT px, py, pal, str
    lda #px
    sta txt_x
    lda #py
    sta txt_y
    lda #pal
    sta txt_pal
    jsl txt_print_inline
    .byte str, 0
.endmacro
.macro PRINTC py, pal, str
    lda #py
    sta txt_y
    lda #pal
    sta txt_pal
    jsl txt_printc_inline
    .byte str, 0
.endmacro
.macro SB str
    jsl sb_stri
    .byte str, 0
.endmacro
.macro SFX id
    lda #id
    jsl scr_sfx
.endmacro
.macro OBJ id, px, py, fl
    lda px
    sta spr_x
    lda py
    sta spr_y
    lda fl
    sta spr_fl
    lda #id
    jsl scr_obj_put
.endmacro
.global txt_print_inline, txt_printc_inline, sb_stri, txt_w, txt_h, txt_clear_all
.global scr_div, scr_abs
.macro ASR1
    cmp #$8000
    ror a
.endmacro
; DIVC n: A = A / n (unsigned 16-bit by 8-bit constant), X = remainder
.macro DIVC n
    ldx #n
    jsl scr_div
.endmacro
.if SCR_VER < 2
.error "gen/scr_ids.inc comes from an older smb4tools.exe than the screens code: rebuild your tools (build.ps1 -ToolsOnly)"
.endif
