; Engine shared declarations (engine agent). Included by game.s and eng_*.s only.
; Internal engine convention: every engine routine is entered/left with A 16-bit, X/Y 16-bit (AXY16), D = 0, DB = $80
; unless its comment says otherwise. Public module APIs (spr_*, bg_*, snd_*) are called with A 8-bit (see DESIGN.md).
.include "levels.inc"

; ------------------------------------------------------------------ C# Btn bits (PadState semantics)
BTN_UP = 1
BTN_DOWN = 2
BTN_LEFT = 4
BTN_RIGHT = 8
BTN_A = 16
BTN_B = 32
BTN_START = 64
BTN_SELECT = 128

; forms (C# Form enum)
PF_SMALL = 0
PF_BIG = 1
PF_FIRE = 2
PF_RACCOON = 3
PF_TANOOKI = 4
PF_FROG = 5
PF_HAMMER = 6

; player states (C# PState)
PS_NORMAL = 0
PS_PIPE = 1
PS_DOOR = 2
PS_DYING = 3
PS_AUTOWALK = 4
PS_FROZEN = 5
PS_VINE = 6

; tile property bits (TILE_PROPS)
TP_SOLID = 1
TP_FLOOR = 2
TP_SLOPE = 4
TP_HURTS = 8
TP_BUMP = 16
TP_LAVA = 32
TP_PIPE = 64

; game modes
GM_TITLE = 0
GM_SELECT = 1
GM_PLAY = 2
GM_GAMEOVER = 3
GM_SCREEN = 4               ; screens module (scr_frame / scr_nmi) owns the frame

; level results (g_result / w_result; C# LevelResult order)
RES_NONE = 0
RES_CLEARED = 1
RES_DIED = 2
RES_TIMEUP = 3              ; (C# never produces it: time-out = death)
RES_EXITED = 4              ; pause menu "EXIT LEVEL"
RES_FORTRESS = 5            ; Boom Boom's orb
RES_WORLD = 6               ; Koopaling's wand (airship)
RES_GAME = 7                ; Bowser defeated

; level kinds (lvl_kind)
LK_LEVEL = 0
LK_BATTLE = 1
LK_FORTRESS = 2
LK_AIRSHIP = 3
LK_CASTLE = 4

MAX_ENTS = 24

; ------------------------------------------------------------------ zero page temps
.globalzp e_t0, e_t1, e_t2, e_t3, e_t4, e_t5, e_t6, e_t7, e_p0, e_p1, e_p2, e_tx, e_ty

; ------------------------------------------------------------------ shared state (defined in game.s / eng_level.s)
.global g_mode, g_level, g_lives, g_score, g_coins, g_cards, g_ncards, g_form, g_hud_dirty, g_paused, g_tick
.global pad_held, pad_pressed, pad_released, pad_prev
.global lvl_ptr, lvl_world, lvl_kind, lvl_time_def, lvl_start_area, lvl_start_x, lvl_start_y, lvl_nareas, lvl_nlinks
.global area_idx, area_ptr, area_w, area_h, area_ts, area_bgtheme, area_backdrop, area_music, area_scroll, area_autospeed
.global area_water, area_goalx, area_goaly, area_slopes, area_ice, area_link_ptr, area_nlinkm, area_spawn_ptr, area_nspawn
.global rowoff, cam_x, cam_y, prev_cam_x, prev_cam_y, w_frame, w_wiggly, w_halt, w_pswitch, w_time, w_timetick
.global w_endtimer, w_clearing, w_result, w_autoscroll, w_hurry, w_cardgot, w_shake, w_bossarena
.global lvl_tiles, lvl_gfx, lvl_cont, lvl_var

; player (C# Player fields)
.global p_x, p_y, p_xvel, p_yvel, p_inair, p_power, p_pmetercnt, p_flytime, p_wagcount, p_allowairjump, p_sliderate
.global p_ducking, p_sliding, p_slide, p_killtally, p_facing, p_form, p_state, p_hurtinv, p_star, p_tailattack, p_statue
.global p_throwpose, p_kickpose, p_jumpbuffer, p_transform, p_transformkind, p_deathtimer, p_pipetimer, p_pipedir
.global p_pipetarea, p_pipetid, p_pipeexiting, p_swimming, p_climbing, p_swimanim, p_climbanim, p_carrying, p_pwing
.global p_noteride, p_notesuper, p_nolowgrav, p_onslope, p_conveyor, p_pipeexitticks, p_stun, p_invisible
.global p_animtick, p_animframe, p_skidding, p_pjumppose, p_somersault, p_inwater, p_swimfc, p_leapt, p_transfrom, p_behind

; routines
.global eng_tile_at_px, eng_tile_at, eng_set_tile, eng_hide_tile, eng_show_tile, eng_link_at
.global eng_load_level, eng_load_area, eng_stream, eng_anim_tiles, eng_hud_draw, eng_hud_init, eng_add_score, eng_add_coin
.global eng_one_up, eng_center_camera, eng_screen_on, eng_screen_off, eng_draw_all
.global pl_control, pl_power_update, pl_detect_solids, pl_timers, pl_animate, pl_draw, pl_hurt, pl_die, pl_bounce
.global pl_powerup, pl_get_star, pl_dying_tick, pl_init
.global w_tick, w_hit_block, w_bump_above, w_update_camera
.global ent_init, ent_spawn_initial, ent_tick, ent_draw, ent_count_class
.global ent_bump_above, ent_kick_carried, ent_drop_carried, ent_tail_hit, ent_release, ent_statue_landed

; ------------------------------------------------------------------ macros
.macro ASR16
    cmp #$8000
    ror a
.endmacro

.macro ASR4
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a
    cmp #$8000
    ror a
.endmacro

.macro NEG16
    eor #$FFFF
    inc a
.endmacro

; play a sound effect / song by name if the sound module defines it (A16 in and out)
.macro SFX name
.ifdef .ident(.concat("SFX_", name))
    phx
    phy
    sep #$20
    lda #.ident(.concat("SFX_", name))
    jsl snd_sfx
    rep #$30
    ply
    plx
.endif
.endmacro

.macro MUSIC name
.ifdef .ident(.concat("SONG_", name))
    sep #$20
    lda #.ident(.concat("SONG_", name))
    phx
    phy
    jsl snd_music
    rep #$30
    ply
    plx
.endif
.endmacro

.global snd_sfx, snd_music, snd_tempo, snd_fade, snd_pause
.global w_touch_tiles, w_head_bump, w_stand_on, w_play_area_music, w_arrive_at_climb, w_add_score_at, w_add_score
.global w_star_started, w_on_player_dying, w_add_coin, w_one_up_at, w_chain_score, w_start_clear, w_break_brick
.global w_can_stomp, w_stomp_bounce, w_mark_killed, w_kill_tally
.global ent_add_fx_dust, ent_add_fx_splash, ent_add_fx_puff_player, ent_throw_fireball, ent_platform_support
.global game_arrive_load, eng_is_hidden, eng_find_link
.global w_tallyend, w_tallydone, w_hurrywait, w_psound, eng_tick_count
.global ent_add_bump, ent_add_coinpop, ent_add_debris4, ent_add_popup, ent_add_sparkle, ent_effects_tick, ent_remove_carried, game_add_card, game_arrive_show, ent_add_item, ent_add_cardfly, ent_goal_update
.global w_start_pswitch, eng_pswitch_swap, ent_battle_check
.global bonus_lives, snd_status
.global g_player, g_pwing, g_star, g_result, g_allowexit, eng_level_start, w_boss_clear, w_clear_res
.global eng_save_load, eng_save_store, eng_save_erase, eng_save_valid, eng_save_buf
.global g_coop, g_form2, co_blk, co_swap, co_cur, co_st, co_hide   ; 2-player co-op (eng_coop.s, eng_player.s)
