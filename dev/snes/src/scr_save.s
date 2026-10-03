; Screens: session + battery saves (screens agent). Port of C# SaveData/Session/PlayerProgress.
; Storage = the engine's record store (eng_save.s: records 0-2 = file slots, 3 = settings; two copies each).
; The running session is the RAM struct sv_base..sv_end (same layout as the stored record).
.p816
.smart
.include "scr.inc"

.export sv_peek, sv_pk_exists, sv_pk_world, sv_pk_beaten, sv_pk_lives, sv_pk_score, sv_pk_two
.export sv_boot
.import eng_save_load, eng_save_store, eng_save_erase, eng_save_buf



SV_SIZE = 8 + 4 + 32 + 2 + 32 + 16 + 16 + 2*PP_SIZE   ; 206
REC_SETTINGS = 3

.segment "BSS"
; ---- the running save (same layout as a slot)
sv_base:
sv_exists:  .res 1
sv_world:   .res 1
sv_highest: .res 1
sv_two:     .res 1
sv_turn:    .res 1
sv_beaten:  .res 1
sv_mapx:    .res 1             ; $FF = start
sv_mapy:    .res 1
sv_frames:  .res 4
sv_nodes:   .res 32            ; per world: 32-bit mask of node indices in their other state (cleared/open/used)
sv_hbdead:  .res 2             ; bit (world-1)*2 + bro
sv_hbpos:   .res 32            ; per world, per bro: x, y ($FF = map default)
sv_nsp:     .res 16            ; per world: N-Spade panel x, y ($FF = none)
sv_ship:    .res 16            ; per world: airship x, y ($FF = its node)
sv_players:
pp_lives:   .res 1
pp_score:   .res 4             ; BCD, like the engine's g_score
pp_coins:   .res 1
pp_nspade:  .res 4             ; BCD score of the next N-Spade panel
pp_form:    .res 1
pp_ncards:  .res 1
pp_cards:   .res 3
pp_nitems:  .res 1
pp_items:   .res 28
pp_over:    .res 1
            .res PP_SIZE - 45
            .res PP_SIZE       ; player 2
sv_end:
; ---- session (not saved)
sv_cur_slot: .res 2
ss_player:  .res 2             ; 0 Mario, 1 Luigi (C# Session.PlayerIndex)
ss_cur:     .res 2             ; PP_SIZE * ss_player (index for pp_* fields)
ss_pwing:   .res 2
ss_star:    .res 2
sv_opt_inf: .res 2             ; option: infinite lives
sv_opt_music: .res 2           ; option: music off
; ---- peek buffer for the file select
sv_peekbuf: .res SV_SIZE
sv_pk_exists = sv_peekbuf
sv_pk_world  = sv_peekbuf + 1
sv_pk_two    = sv_peekbuf + 3
sv_pk_beaten = sv_peekbuf + 5
sv_pk_lives  = sv_peekbuf + (sv_players - sv_base)
sv_pk_score  = sv_peekbuf + (pp_score - sv_base)
sv_tmp: .res 2
sv_tmp2: .res 2

.segment "CODE11"
.a16
.i16

; sv_boot: options from the settings record
sv_boot:
    php
    rep #$30
    stz sv_opt_inf
    stz sv_opt_music
    lda #REC_SETTINGS
    jsl eng_save_load
    bcc :+
    lda f:eng_save_buf
    and #$00FF
    sta sv_opt_inf
    lda f:eng_save_buf+1
    and #$00FF
    sta sv_opt_music
:   plp
    rtl

; sv_opts_save: writes the options record
sv_opts_save:
    php
    rep #$30
    ldx #0
    lda #0
@z: sta f:eng_save_buf,x
    inx
    inx
    cpx #64
    bcc @z
    sep #$20
    .a8
    lda sv_opt_inf
    sta f:eng_save_buf
    lda sv_opt_music
    sta f:eng_save_buf+1
    rep #$20
    .a16
    lda #REC_SETTINGS
    jsl eng_save_store
    plp
    rtl

; sv_peek: A = slot -> sv_peekbuf (sv_pk_exists = 0 if empty/corrupt)
sv_peek:
    php
    rep #$30
    jsl eng_save_load
    bcs :+
    sep #$20
    .a8
    stz sv_pk_exists
    rep #$20
    .a16
    plp
    rtl
:   ldx #0
@cp: lda f:eng_save_buf,x
    sta sv_peekbuf,x
    inx
    inx
    cpx #SV_SIZE
    bcc @cp
    plp
    rtl

; sv_select: A = slot, X = 1 for a 2-player game. C# FileSelectScreen: a new file is created (world 1) and saved;
; an existing one is loaded (a 1P file becomes 2P when picked from "2 PLAYER GAME"). Sets up the session.
; -> carry set if the file was new (the story plays first).
sv_select:
    php
    rep #$30
    sta sv_cur_slot
    stx sv_tmp
    jsl sv_peek
    lda sv_pk_exists
    and #$00FF
    beq @new
    ldx #0
@cp: lda sv_peekbuf,x
    sta sv_base,x
    inx
    inx
    cpx #SV_SIZE
    bcc @cp
    lda sv_tmp
    beq :+
    sep #$20
    .a8
    lda #1
    sta sv_two
    rep #$20
    .a16
:   jsr session
    plp
    clc
    rtl
@new:
    lda sv_tmp
    jsl sv_new
    jsl sv_save
    jsr session
    plp
    sec
    rtl

; sv_new: A = two-player flag -> fresh SaveData in RAM (C# defaults: 4 lives, N-Spade at 80,000)
sv_new:
    php
    rep #$30
    pha
    ldx #0
@z: stz sv_base,x
    inx
    inx
    cpx #SV_SIZE
    bcc @z
    sep #$20
    .a8
    pla
    sta sv_two
    pla
    lda #1
    sta sv_exists
    sta sv_world
    sta sv_highest
    lda #$FF
    sta sv_mapx
    sta sv_mapy
    ldx #0
@f: sta sv_hbpos,x
    inx
    cpx #32+16+16               ; hbpos, nsp, ship
    bcc @f
    ldx #0
    jsr pp_default
    ldx #PP_SIZE
    jsr pp_default
    rep #$20
    .a16
    plp
    rtl
.a8
pp_default:
    lda #4
    sta pp_lives,x
    lda #$00                    ; 80,000 = BCD 00 00 08 00 (byte0 = digits 1,0)
    sta pp_nspade,x
    sta pp_nspade+1,x
    sta pp_nspade+3,x
    lda #$08
    sta pp_nspade+2,x
    rts
.a16

; C# Session ctor: PlayerIndex = TwoPlayer ? Turn : 0
; 2-player games are simultaneous co-op now: Mario's record holds the shared progress (lives, score, coins, cards,
; items), Luigi's record only his form.
session:
    stz ss_player
    jsr cur_index
    stz ss_pwing
    stz ss_star
    rts
cur_index:
    lda ss_player
    beq :+
    lda #PP_SIZE
:   sta ss_cur
    rts
.export sv_set_player
; sv_set_player: A = player index (0/1): the current player of the session (and the save's turn)
sv_set_player:
    php
    rep #$30
    sta ss_player
    sep #$20
    .a8
    sta sv_turn
    rep #$20
    .a16
    jsr cur_index
    plp
    rtl

; sv_save: the running save -> record sv_cur_slot
sv_save:
    php
    rep #$30
    sep #$20
    .a8
    lda #1
    sta sv_exists
    rep #$20
    .a16
    ldx #0
@cp: lda sv_base,x
    sta f:eng_save_buf,x
    inx
    inx
    cpx #SV_SIZE
    bcc @cp
    lda sv_cur_slot
    jsl eng_save_store
    plp
    rtl

; sv_erase: A = slot
sv_erase:
    php
    rep #$30
    jsl eng_save_erase
    plp
    rtl

; ------------------------------------------------------------------ engine hand-over (C# Session <-> World)
; sv_to_engine: the current player's progress -> the engine's g_* variables (before eng_level_start)
sv_to_engine:
    php
    rep #$30
    ldx ss_cur
    lda pp_lives,x
    and #$00FF
    sta g_lives
    lda pp_score,x
    sta g_score
    lda pp_score+2,x
    sta g_score+2
    lda pp_coins,x
    and #$00FF
    sta g_coins
    lda pp_form,x
    and #$00FF
    sta g_form
    lda pp_ncards,x
    and #$00FF
    asl a
    sta g_ncards
    lda pp_cards,x
    and #$00FF
    sta g_cards
    lda pp_cards+1,x
    and #$00FF
    sta g_cards+2
    lda pp_cards+2,x
    and #$00FF
    sta g_cards+4
    lda ss_player
    sta g_player
    stz g_coop
    lda sv_two
    and #$00FF
    beq :+
    sta g_coop                  ; both players in the level (co-op)
    lda pp_form+PP_SIZE
    and #$00FF
    sta g_form2
:   lda ss_pwing
    sta g_pwing
    lda ss_star
    sta g_star
    stz ss_pwing                ; C# World ctor: s.UsePWing = s.StartStar = false
    stz ss_star
    plp
    rtl

; sv_from_engine: g_* -> the current player's progress (after the level)
sv_from_engine:
    php
    rep #$30
    ldx ss_cur
    sep #$20
    .a8
    lda g_lives
    sta pp_lives,x
    lda g_coins
    sta pp_coins,x
    lda g_form
    sta pp_form,x
    lda g_ncards
    lsr a
    sta pp_ncards,x
    lda g_cards
    sta pp_cards,x
    lda g_cards+2
    sta pp_cards+1,x
    lda g_cards+4
    sta pp_cards+2,x
    rep #$20
    .a16
    lda g_score
    sta pp_score,x
    lda g_score+2
    sta pp_score+2,x
    lda sv_two
    and #$00FF
    beq :+
    sep #$20
    .a8
    lda g_form2
    sta pp_form+PP_SIZE
    rep #$20
    .a16
:   plp
    rtl

; C# Session.AddLife (not with the infinite-lives assist)
sv_add_life:
    php
    rep #$30
    lda sv_opt_inf
    bne @r
    ldx ss_cur
    lda pp_lives,x
    and #$00FF
    cmp #99
    bcs @r
    sep #$20
    .a8
    inc pp_lives,x
@r: plp
    rtl
.a16
sv_lose_life:
    php
    rep #$30
    lda sv_opt_inf
    bne @r
    ldx ss_cur
    sep #$20
    .a8
    lda pp_lives,x
    beq @r
    dec pp_lives,x
@r: plp
    rtl
.a16
; sv_add_coin: -> carry set when 100 coins gave a life
sv_add_coin:
    php
    rep #$30
    ldx ss_cur
    sep #$20
    .a8
    inc pp_coins,x
    lda pp_coins,x
    cmp #100
    bcc @no
    sbc #100
    sta pp_coins,x
    rep #$20
    .a16
    jsl sv_add_life
    plp
    sec
    rtl
@no: plp
    clc
    rtl
.a16
; sv_add_item: A = IT_* (inventory holds 28)
sv_add_item:
    php
    rep #$30
    ldx ss_cur
    sta sv_tmp
    lda pp_nitems,x
    and #$00FF
    cmp #28
    bcs @r
    sta sv_tmp2
    txa
    clc
    adc sv_tmp2
    tay
    sep #$20
    .a8
    lda sv_tmp
    sta pp_items,y
    inc pp_nitems,x
@r: plp
    rtl
.a16
