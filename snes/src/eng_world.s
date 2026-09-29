; Engine: World.Tick and the World helpers around the player (port of src/Game/Level/World.cs + WorldPlay.cs).
; Owner: engine agent. A16/XY16 convention, JSL/RTL.
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"
.include "spr_ids.inc"
.include "music.inc"



.segment "BSS"
eng_tick_count: .res 2
mc_tile: .res 2             ; multi-coin brick state
mc_start: .res 2
mc_count: .res 2
tt_x: .res 2
tt_y: .res 2
tt_w: .res 2
tt_h: .res 2
tt_tx0: .res 2
tt_tx1: .res 2
tt_ty0: .res 2
tt_ty1: .res 2
hb_big: .res 2
hb_player: .res 2
hb_idx: .res 2
hb_t: .res 2
hb_c: .res 2
ar_mx: .res 2
ar_my: .res 2
w_clear_res: .res 2        ; result the running clear ends with (RES_*)

.segment "CODE2"
.a16
.i16

tile_props2: .byte TILE_PROPS
chain_pts: .word $0100, $0200, $0400, $0800, $1000, $2000, $4000, $8000

.macro PROPS2
    tax
    lda f:tile_props2,x
    and #$00FF
.endmacro

get_px:
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    rts
get_py:
    lda p_y
    ASR4
    rts

; ================================================================== World.Tick
w_tick:
    inc w_frame
    lda w_endtimer
    beq :+
    jmp end_tick
:   lda p_state
    cmp #PS_DYING
    bne @notdying
    jsl pl_dying_tick
    ; Result = Died after the death animation
    lda p_deathtimer
    cmp #$30+150+1
    bcs @died
    cmp #$30+100+1
    bcc :+
    jsr get_py
    sec
    sbc cam_y
    bmi :+
    cmp #261
    bcc :+
@died:
    lda #2
    sta w_result
:   rtl
@notdying:
    cmp #PS_PIPE
    beq @pipe
    cmp #PS_DOOR
    bne :+
@pipe: jmp pipe_tick
:   lda w_halt
    beq :+
    dec w_halt
    lda p_transform
    beq @r
    dec p_transform
@r: rtl
:   ; wiggly dither: CW = ((CW & $F0) - $90) & $FF
    lda w_wiggly
    and #$F0
    sec
    sbc #$90
    and #$FF
    sta w_wiggly
    inc eng_tick_count
    lda p_state
    cmp #PS_AUTOWALK
    bne @ctl
    jsr autowalk_tick
    bra @pw
@ctl:
    jsl pl_control
    jsr check_transitions
    lda p_state
    cmp #PS_PIPE
    beq @r
    cmp #PS_DOOR
    beq @r
@pw:
    jsl pl_power_update
    jsr p_sound
    jsl w_update_camera
    jsl pl_detect_solids
    jsr clamp_player
    jsl pl_timers
    jsl pl_animate
    lda p_state
    bne @alive
    ; pit death: Py > LevelPxH + 16
    lda area_h
    asl a
    asl a
    asl a
    asl a
    clc
    adc #16
    sta e_t0
    jsr get_py
    sec
    sbc e_t0
    bmi @alive
    beq @alive
    lda #1
    jsl pl_die
    rtl
@alive:
    jsl ent_tick                ; Spawner, updates, adds, collisions, despawn, removal
    jsr timers_tick
    jsl ent_battle_check        ; Hammer Bro battle won -> treasure chest
    lda w_shake
    beq :+
    dec w_shake
:   rtl

; P-meter whistle loops while running at full P on the ground
p_sound:
    lda #0
    ldx p_power
    cpx #$7F
    bne @s
    ldx p_inair
    bne @s
    ldx p_state
    bne @s
    ldx p_xvel
    bpl :+
    pha
    lda p_xvel
    NEG16
    tax
    pla
:   cpx #$28
    bcc @s
    lda #1
@s: cmp w_psound
    beq @r
    sta w_psound
    cmp #0
    beq @off
    SFX "PMETER"
@r: rts
@off:
.ifdef SFX_PMETER
    sep #$20
    lda #SFX_STOP|SFX_PMETER
    jsl snd_sfx
    rep #$30
.endif
    rts

; ================================================================== camera (docs/01 §12)
w_update_camera:
    lda cam_x
    sta prev_cam_x
    lda cam_y
    sta prev_cam_y
    ; maxX = max(0, W*16 - 256)
    lda area_w
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #256
    bpl :+
    lda #0
:   sta e_t0
    lda area_scroll
    cmp #SCROLL_AUTO
    bne @normx
    lda w_bossarena
    bne @camy
    lda w_autoscroll
    clc
    adc area_autospeed
    sta w_autoscroll
    lsr a
    lsr a
    lsr a
    lsr a
    cmp e_t0
    bcc :+
    lda e_t0
:   sta cam_x
    bra @camy
@normx:
    lda w_bossarena
    bne @camy
    jsr get_px
    sta e_t1
    sec
    sbc cam_x                   ; sx
    bmi @left
    cmp #$81
    bcc @chkl
    lda e_t1
    sec
    sbc #$80
    sta cam_x
    bra @clampx
@chkl: cmp #$70
    bcs @clampx
@left:
    lda e_t1
    sec
    sbc #$70
    sta cam_x
@clampx:
    lda cam_x
    bpl :+
    stz cam_x
    bra @camy
:   cmp e_t0
    bcc @camy
    lda e_t0
    sta cam_x
@camy:
    ; maxY = H*16 - 192 ; minY = min(0, maxY)
    lda area_h
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #192
    sta e_t2                    ; maxY
    bpl :+
    lda e_t2
    bra :++
:   lda #0
:   sta e_t3                    ; minY
    lda area_scroll
    cmp #SCROLL_LOCK
    beq @lock
    lda e_t2
    bmi @lock
    beq @lock
    ; free = mode free/vertical || FlyTime != 0 || State == Vine
    stz e_t4
    lda area_scroll
    cmp #SCROLL_FREE
    beq @free
    cmp #SCROLL_VERTICAL
    beq @free
    lda p_flytime
    bne @free
    lda p_state
    cmp #PS_VINE
    bne @nf
@free: inc e_t4
@nf:
    lda area_scroll
    cmp #SCROLL_NORMAL
    beq :+
    cmp #SCROLL_AUTO
    bne @track
:   lda e_t4
    bne @track
    lda cam_y
    sec
    sbc e_t2
    bmi @track
@lock:
    lda e_t2
    sta cam_y
    rtl
@track:
    lda p_state
    cmp #PS_DYING
    beq @clampy
    jsr get_py
    sta e_t1
    sec
    sbc cam_y                   ; sy
    sec
    sbc #48
    bpl @below
    ; CamY = max(CamY - 3, Py - 48)
    lda e_t1
    sec
    sbc #48
    sta e_t5
    lda cam_y
    sec
    sbc #3
    sec
    sbc e_t5
    bmi :+
    lda cam_y
    sec
    sbc #3
    sta cam_y
    bra @clampy
:   lda e_t5
    sta cam_y
    bra @clampy
@below:
    lda e_t1
    sec
    sbc cam_y
    sec
    sbc #89
    bmi @clampy
    lda e_t1
    sec
    sbc #88
    sta e_t5
    lda area_scroll
    cmp #SCROLL_VERTICAL
    bne @snap
    lda cam_y
    clc
    adc #4
    sec
    sbc e_t5
    bpl @snap
    lda cam_y
    clc
    adc #4
    sta cam_y
    bra @clampy
@snap:
    lda e_t5
    sta cam_y
@clampy:
    ; clamp(CamY, max(minY, -128), maxY)
    lda e_t3
    sec
    sbc #$10000-128
    bpl :+
    lda #$10000-128
    sta e_t3
:   lda cam_y
    sec
    sbc e_t3
    bpl :+
    lda e_t3
    sta cam_y
:   lda cam_y
    sec
    sbc e_t2
    bmi :+
    lda e_t2
    sta cam_y
:   rtl

; eng_center_camera (World.CenterCamera): A = 1 snapV (unused difference)
eng_center_camera:
    lda area_w
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #256
    bpl :+
    lda #0
:   sta e_t0
    jsr get_px
    sec
    sbc #120
    bpl :+
    lda #0
:   cmp e_t0
    bcc :+
    lda e_t0
:   sta cam_x
    lda area_h
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #192
    sta e_t2
    lda area_scroll
    cmp #SCROLL_VERTICAL
    beq @v
    cmp #SCROLL_FREE
    beq @v
    lda e_t2
    sta cam_y
    bra @done
@v: ; clamp(Py - 72, min(0, maxY), maxY)
    lda e_t2
    bmi :+
    lda #0
:   sta e_t3
    jsr get_py
    sec
    sbc #72
    sta cam_y
    sec
    sbc e_t3
    bpl :+
    lda e_t3
    sta cam_y
:   lda cam_y
    sec
    sbc e_t2
    bmi @done
    lda e_t2
    sta cam_y
@done:
    lda area_scroll
    cmp #SCROLL_AUTO
    bne :+
    lda cam_x
    asl a
    asl a
    asl a
    asl a
    sta w_autoscroll
:   lda cam_x
    sta prev_cam_x
    lda cam_y
    sta prev_cam_y
    rtl

; ClampPlayerToScreen
clamp_player:
    lda p_state
    beq :+
    rts
:   jsr get_px
    sec
    sbc cam_x
    sta e_t0                    ; sx
    lda area_scroll
    cmp #SCROLL_AUTO
    bne @norm
    lda w_bossarena
    bne @norm
    lda e_t0
    cmp #16
    bpl @a1
    lda cam_x
    clc
    adc #16
    asl a
    asl a
    asl a
    asl a
    sta p_x
    ; crushed against a wall by the scroll
    jsr get_px
    clc
    adc #14
    pha
    jsr get_py
    clc
    adc #20
    tay
    pla
    jsl eng_tile_at_px
    PROPS2
    and #TP_SOLID
    beq @a1
    lda #0
    jsl pl_die
    rts
@a1: jsr get_px
    sec
    sbc cam_x
    cmp #225
    bmi :+
    lda cam_x
    clc
    adc #224
    asl a
    asl a
    asl a
    asl a
    sta p_x
:   rts
@norm:
    lda e_t0
    cmp #16
    bpl @hi
    lda cam_x
    clc
    adc #16
    asl a
    asl a
    asl a
    asl a
    sta p_x
    lda p_xvel
    bpl @hi
    NEG16
    and #15
    NEG16
    sta p_xvel
@hi:
    jsr get_px
    sec
    sbc cam_x
    cmp #233
    bmi @top
    lda cam_x
    clc
    adc #232
    asl a
    asl a
    asl a
    asl a
    sta p_x
    lda p_xvel
    bmi @top
    and #15
    sta p_xvel
@top:
    lda p_y
    bpl @r
    cmp #$10000-(128*16)
    bcs @r
    lda #$10000-(128*16)
    sta p_y
@r: rts

; ================================================================== tiles touching the player
; GetHitbox -> tt_x, tt_y, tt_w, tt_h
.export pl_hitbox
pl_hitbox:
    jsr get_px
    sta tt_x
    jsr get_py
    sta tt_y
    lda p_form
    beq @small
    lda p_ducking
    ora p_sliding
    bne @small
    lda tt_x
    clc
    adc #3
    sta tt_x
    lda tt_y
    clc
    adc #5
    sta tt_y
    lda #10
    sta tt_w
    lda #25
    sta tt_h
    rtl
@small:
    lda tt_x
    clc
    adc #4
    sta tt_x
    lda tt_y
    clc
    adc #17
    sta tt_y
    lda #8
    sta tt_w
    lda #13
    sta tt_h
    rtl
.export tt_x, tt_y, tt_w, tt_h

w_touch_tiles:
    jsl pl_hitbox
    lda tt_x
    ASR4
    sta tt_tx0
    lda tt_x
    clc
    adc tt_w
    dec a
    ASR4
    sta tt_tx1
    lda tt_y
    ASR4
    sta tt_ty0
    lda tt_y
    clc
    adc tt_h
    dec a
    ASR4
    sta tt_ty1
    lda tt_ty0
    sta e_ty
@ly: lda tt_tx0
    sta e_tx
@lx: jsl eng_tile_at
    cmp #T_COIN
    bne @nc
    lda #T_EMPTY
    jsl eng_set_tile
    jsl w_add_coin
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    clc
    adc #4
    tax
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    clc
    adc #4
    tay
    jsl ent_add_sparkle
    bra @nx
@nc: PROPS2
    and #TP_LAVA
    beq @nx
    ; y + h - 4 > ty*16 + 4
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    clc
    adc #8
    sta e_t0
    lda tt_y
    clc
    adc tt_h
    sec
    sbc e_t0
    bmi @nx
    beq @nx
    lda #0
    jsl pl_die
    rtl
@nx: inc e_tx
    lda e_tx
    cmp tt_tx1
    beq @lx
    bmi @lx
    inc e_ty
    lda e_ty
    cmp tt_ty1
    beq @ly
    bmi @ly
    ; hurting tiles (body or directly under the feet)
    lda p_star
    ora p_statue
    jne @r
    jsr get_py
    clc
    adc #32
    ASR4
    sta tt_ty1
    lda tt_x
    dec a
    ASR4
    sta tt_tx0
    lda tt_x
    clc
    adc tt_w
    ASR4
    sta tt_tx1
    lda tt_ty0
    sta e_ty
@hy: lda tt_tx0
    sta e_tx
@hx: jsl eng_tile_at
    PROPS2
    and #TP_HURTS
    beq @hn
    jsl pl_hurt
    rtl
@hn: inc e_tx
    lda e_tx
    cmp tt_tx1
    beq @hx
    bmi @hx
    inc e_ty
    lda e_ty
    cmp tt_ty1
    beq @hy
    bmi @hy
@r: rtl

; StandOn: A = T, e_tx, e_ty
w_stand_on:
    cmp #T_NOTE
    bne @r
    lda p_noteride
    bne @r
    lda p_state
    bne @r
    lda #8
    sta p_noteride
    stz p_notesuper
    lda p_jumpbuffer
    bne :+
    lda pad_pressed
    and #BTN_A
    beq :++
:   lda #1
    sta p_notesuper
:   lda #1
    ldx #T_NOTE
    jsl ent_add_bump            ; dir +1 (down), final T_NOTE
    SFX "NOTE"
    stz e_t0
    jsr bump_above
@r: rtl

; ================================================================== blocks
; HeadBump: e_tx, e_ty
w_head_bump:
    jsl eng_tile_at
    pha
    lda #1
    sta hb_player
    lda p_form
    sta hb_big
    jsr hit_block
    pla
    cmp #T_NOTE
    bne :+
    lda #$20
    sta p_yvel
:   rtl

; w_hit_block: e_tx,e_ty, A = big (0/1), X = byPlayer (0/1) -> carry set if something reacted
w_hit_block:
    sta hb_big
    stx hb_player
    jsr hit_block
    rtl

hit_block:
    lda e_tx
    bmi @no
    cmp area_w
    bcs @no
    lda e_ty
    bmi @no
    cmp area_h
    bcs @no
    jsl eng_tile_at
    sta hb_t
    stx hb_idx
    lda f:lvl_cont,x
    and #$00FF
    sta hb_c
    jsl eng_is_hidden
    bcs @no
    lda hb_t
    cmp #T_HIDDENBLOCK
    beq @q
    cmp #T_QBLOCK
    beq @q
    cmp #T_BRICK
    beq @brick
    cmp #T_NOTE
    jeq @note
    cmp #T_WOOD
    jeq @note
    ; default: bump sound on solid tiles
    PROPS2
    and #TP_SOLID
    beq @no
    lda hb_player
    beq @no
    SFX "BUMP"
@no: clc
    rts
@q: ; hidden block / ? block -> content, becomes Used
    jsr release
    jsr clear_content
    lda #$FFFF
    ldx #T_USED
    jsl ent_add_bump
    stz e_t0
    jsr bump_above
    sec
    rts
@brick:
    lda hb_c
    cmp #CT_MULTICOIN
    bne @nomc
    jsl w_add_coin
    jsr coinpop_above
    lda mc_start
    bmi @mcnew
    lda mc_tile
    cmp hb_idx
    beq @mcold
@mcnew:
    lda hb_idx
    sta mc_tile
    lda w_frame
    sta mc_start
    stz mc_count
@mcold:
    inc mc_count
    ldx #T_BRICK
    lda mc_count
    cmp #10
    bcs @mclast
    lda w_frame
    sec
    sbc mc_start
    cmp #241
    bcc @mcbump
@mclast:
    jsr clear_content
    lda #$FFFF
    sta mc_tile
    sta mc_start
    ldx #T_USED
@mcbump:
    lda #$FFFF
    jsl ent_add_bump
    stz e_t0
    jsr bump_above
    sec
    rts
@nomc:
    lda hb_c
    beq @plain
    jsr release
    jsr clear_content
    lda #$FFFF
    ldx #T_USED
    jsl ent_add_bump
    stz e_t0
    jsr bump_above
    sec
    rts
@plain:
    lda hb_big
    bne @break
    lda hb_player
    beq @break
    lda #$FFFF
    ldx #T_BRICK
    jsl ent_add_bump
    SFX "BUMP"
    stz e_t0
    jsr bump_above
    sec
    rts
@break:
    jsl w_break_brick
    sec
    rts
@note:
    lda #$FFFF
    ldx hb_t
    jsl ent_add_bump
    lda hb_c
    beq :+
    jsr release
    jsr clear_content
:   SFX "BUMP"
    stz e_t0
    jsr bump_above
    sec
    rts

clear_content:
    ldx hb_idx
    sep #$20
    lda #0
    sta f:lvl_cont,x
    rep #$20
    rts

; coin pop one tile above (e_tx, e_ty)
coinpop_above:
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    tax
    lda e_ty
    dec a
    asl a
    asl a
    asl a
    asl a
    tay
    jsl ent_add_coinpop
    rts

; Release(tx, ty, content hb_c, big hb_big)
release:
    lda hb_c
    cmp #CT_COIN
    bne :+
    jsl w_add_coin
    jmp coinpop_above
:   cmp #CT_NONE
    bne :+
    SFX "BUMP"
    rts
:   ; items: ent_release(A = content, e_tx/e_ty, hb_big)
    lda hb_big
    tax
    lda hb_c
    jsl ent_release
    rts

; BreakBrick: e_tx, e_ty
w_break_brick:
    lda #T_EMPTY
    jsl eng_set_tile
    jsl eng_tile_at             ; X = index
    sep #$20
    lda #0
    sta f:lvl_cont,x
    rep #$20
    jsl ent_add_debris4
    lda #$0010
    jsl w_add_score
    SFX "BREAK"
    stz e_t0
    jsr bump_above
    rtl

; BumpAbove(tx, ty): coin on top is collected; objects standing on the block get bumped
bump_above:
    dec e_ty
    jsl eng_tile_at
    cmp #T_COIN
    bne :+
    lda #T_EMPTY
    jsl eng_set_tile
    jsl w_add_coin
    inc e_ty
    jsr coinpop_above
    dec e_ty
:   inc e_ty
    jsl ent_bump_above
    rts
.export w_bump_above_l
w_bump_above_l:
    jsr bump_above
    rtl

; ================================================================== score / coins / lives
; w_add_score: A = BCD points (16-bit)
w_add_score:
    sed
    clc
    adc g_score
    sta g_score
    lda g_score+2
    adc #0
    sta g_score+2
    cld
    ; cap 9999990
    lda g_score+2
    cmp #$1000
    bcc :+
    lda #$0999
    sta g_score+2
    lda #$9990
    sta g_score
:   lda #1
    sta g_hud_dirty
    rtl

; w_add_score_at: A = BCD points, X = px, Y = py (adds a score popup)
w_add_score_at:
    cmp #0
    bne :+
    rtl
:   pha
    phx
    phy
    jsl w_add_score
    ply
    plx
    pla
    jsl ent_add_popup
    rtl

w_add_coin:
    lda #$0050
    jsl w_add_score
    inc g_coins
    lda g_coins
    cmp #100
    bcc :+
    sec
    sbc #100
    sta g_coins
    jsr get_px
    tax
    jsr get_py
    tay
    jsl w_one_up_at
:   SFX "COIN"
    lda #1
    sta g_hud_dirty
    rtl

; OneUp: X = px, Y = py
w_one_up_at:
    lda g_lives
    cmp #99
    bcs :+
    inc g_lives
:   SFX "ONEUP"
    lda #$FFFF                  ; popup "1UP"
    jsl ent_add_popup
    lda #1
    sta g_hud_dirty
    rtl

; ChainScore: A = n, X = px, Y = py
w_chain_score:
    cmp #1
    bcs :+
    lda #1
:   cmp #9
    bcs @up
    dec a
    asl a
    phx
    tax
    lda f:chain_pts,x
    plx
    jsl w_add_score_at
    rtl
@up: jsl w_one_up_at
    rtl

; ================================================================== music helpers
w_play_area_music:
    lda p_star
    bne @r
    lda w_pswitch
    beq :+
    MUSIC "PSWITCH"
    rtl
:   sep #$20
    lda area_music
    jsl snd_music
    lda #0
    ldx w_hurry
    beq :+
    lda #1
:   jsl snd_tempo
    rep #$20
@r: rtl

w_star_started:
    MUSIC "STAR"
    rtl

w_on_player_dying:
    MUSIC "DEATH"
    rtl

; ================================================================== timers (level time)
timers_tick:
    lda w_clearing
    bne @r
    lda w_time
    beq @r
    lda lvl_time_def
    beq @r
    dec w_timetick
    bpl @r
    lda #40
    sta w_timetick
    dec w_time
    lda #1
    sta g_hud_dirty
    lda w_time
    cmp #100
    bne :+
    lda w_hurry
    bne :+
    lda #1
    sta w_hurry
    MUSIC "HURRY"
    lda #150
    sta w_hurrywait
:   lda w_time
    bne @r
    lda p_state
    bne @r
    lda #0
    jsl pl_die
@r: lda w_hurrywait
    beq :+
    dec w_hurrywait
    bne :+
    jsl w_play_area_music
:   ; P-switch: one unit per 4 ticks
    lda w_pswitch
    beq @nps
    lda w_frame
    and #3
    bne @nps
    dec w_pswitch
    bne @nps
    lda #1
    jsl eng_pswitch_swap
    jsl w_play_area_music
@nps: rts

; World.StartPSwitch
w_start_pswitch:
    lda w_pswitch
    beq :+
    lda #128
    sta w_pswitch
    rtl
:   lda #128
    sta w_pswitch
    lda #0
    jsl eng_pswitch_swap
    SFX "PSWITCH"
    MUSIC "PSWITCH"
    lda #8
    sta w_shake
    rtl

; ================================================================== pipes & doors
check_transitions:
    lda p_state
    beq :+
    rts
:   jsr get_px
    clc
    adc #8
    sta e_t6                    ; cx
    ; ---- down into a pipe
    lda p_inair
    bne @up
    lda pad_held
    and #BTN_DOWN
    beq @up
    lda e_t6
    ASR4
    sta e_tx
    jsr get_py
    clc
    adc #32
    ASR4
    sta e_ty
    jsl eng_tile_at
    cmp #T_PIPETL
    beq :+
    cmp #T_PIPETR
    bne @up
    dec e_tx
:   jsr pipe_link               ; A = id or $FFFF, e_t5 = center
    bmi @up
    sta e_t4
    lda e_t6
    sec
    sbc e_t5
    jsr abs_a
    cmp #6
    bcs @up
    lda e_t5
    sec
    sbc #8
    ldx #2
    jmp start_pipe
@up:
    lda pad_held
    and #BTN_UP
    jeq @side
    ; ceiling pipe
    ldx #4
    lda p_form
    bne :+
    ldx #14
:   stx e_t3
    jsr get_py
    clc
    adc e_t3
    sta e_t3                    ; hy
    lda e_t6
    ASR4
    sta e_tx
    lda e_t3
    dec a
    dec a
    ASR4
    sta e_ty
    jsl eng_tile_at
    cmp #T_PIPEBL
    beq :+
    cmp #T_PIPEBR
    bne @door
    dec e_tx
:   jsr pipe_link
    bmi @door
    sta e_t4
    lda e_t6
    sec
    sbc e_t5
    jsr abs_a
    cmp #6
    bcs @door
    ; hy - 2 - (ty*16 + 15) <= 2
    lda e_ty
    asl a
    asl a
    asl a
    asl a
    clc
    adc #15+2
    sta e_t0
    lda e_t3
    sec
    sbc e_t0
    sec
    sbc #3
    bpl @door
    lda e_t5
    sec
    sbc #8
    ldx #8
    jmp start_pipe
@door:
    lda p_inair
    jne @side
    lda pad_pressed
    and #BTN_UP
    jeq @side
    lda e_t6
    ASR4
    sta e_tx
    jsr get_py
    clc
    adc #24
    ASR4
    sta e_ty
    jsl eng_tile_at
    cmp #T_DOORTOP
    beq @dtop
    cmp #T_DOORBOT
    bne @side
    bra @dfind
@dtop: inc e_ty
@dfind:
    jsl eng_link_at
    bmi @side
    sta e_t4
    jsl eng_find_link
    bcs :+
    jmp @side
:   sta p_pipetarea
    stx p_pipetid
    lda #PS_DOOR
    sta p_state
    stz p_pipetimer
    stz p_pipeexiting
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    sta p_x
    stz p_xvel
    stz p_yvel
    SFX "DOOR"
    rts
@side:
    ; ---- side pipes (horizontal mouths)
    lda p_inair
    bne @r
    lda pad_held
    and #BTN_RIGHT
    beq :+
    lda #1
    bra @hd
:   lda pad_held
    and #BTN_LEFT
    beq @r
    lda #$FFFF
@hd: sta e_t2                    ; dir
    jsr get_px
    ldx e_t2
    bmi :+
    clc
    adc #16
    bra :++
:   dec a
:   ASR4
    sta e_tx
    jsr get_py
    clc
    adc #31
    ASR4
    sta e_ty
    jsl eng_tile_at
    cmp #T_HPIPEMOUTHB
    beq @hm
    cmp #T_HPIPEMOUTHT
    bne @r
    ; only the bottom mouth tile starts the pipe (C# requires t == HPipeMouthB)
@r: rts
@hm:
    ; topRow = ty - 1; facesLeft = Var[topRow*W + tx] == 0
    dec e_ty
    jsl eng_tile_at
    lda f:lvl_var,x
    and #$00FF
    sta e_t3                    ; 0 = faces left
    lda e_t2
    bmi @dl
    lda e_t3
    bne @r2
    bra @hok
@dl: lda e_t3
    beq @r2
@hok:
    jsl eng_link_at
    bpl @hfound
    inc e_ty
    jsl eng_link_at
    pha
    dec e_ty
    pla
    bmi @r2
@hfound:
    sta e_t4
    jsr get_px
    ldx #6
    lda e_t2
    bpl :+
    ldx #4
:   jsr get_px
    jmp start_pipe
@r2: rts

; A = |A|
abs_a:
    cmp #0
    bpl :+
    NEG16
:   rts

; e_tx = left pipe column, e_ty: A = link id at (left, ty) or (left+1, ty) ($FFFF none), e_t5 = center px
pipe_link:
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    clc
    adc #16
    sta e_t5
    jsl eng_link_at
    bpl @r
    inc e_tx
    jsl eng_link_at
    pha
    dec e_tx
    pla
@r: rts

; StartPipe: e_t4 = link id, X = dir, A = new px
start_pipe:
    sta e_t7
    stx e_t6
    lda e_t4
    jsl eng_find_link
    bcs :+
    rts
:   sta p_pipetarea
    stx p_pipetid
    lda #PS_PIPE
    sta p_state
    lda e_t6
    sta p_pipedir
    stz p_pipetimer
    stz p_pipeexiting
    lda e_t7
    asl a
    asl a
    asl a
    asl a
    sta p_x
    stz p_xvel
    stz p_yvel
    stz p_ducking
    jsl ent_remove_carried
    SFX "PIPE"
    rts

pipe_tick:
    inc p_pipetimer
    lda p_state
    cmp #PS_DOOR
    bne @pipe
    lda p_pipeexiting
    bne @dexit
    lda p_pipetimer
    cmp #24
    bcc :+
    lda #2
    sta p_invisible
:   lda p_pipetimer
    cmp #40
    bcc @r
    lda #1
    jmp arrive_at
@dexit:
    lda p_pipetimer
    cmp #20
    bcc @r
    lda #PS_NORMAL
    sta p_state
    stz p_inair
@r: rtl
@pipe:
    stz e_t0                    ; dx
    stz e_t1                    ; dy
    ldx #8                      ; step = 0.5 px per tick
    ldy #$10000-8
    lda p_pipedir
    cmp #2
    bne :+
    stx e_t1                    ; down
:   cmp #8
    bne :+
    sty e_t1                    ; up
:   cmp #6
    bne :+
    stx e_t0                    ; right
:   cmp #4
    bne :+
    sty e_t0                    ; left
:   lda p_x
    clc
    adc e_t0
    sta p_x
    lda p_y
    clc
    adc e_t1
    sta p_y
    lda p_pipedir
    cmp #6
    beq @walk
    cmp #4
    bne @nw
@walk:
    dec p_animtick
    beq :+
    bpl @nw
:   lda #4
    sta p_animtick
    lda p_animframe
    inc a
    and #3
    sta p_animframe
@nw:
    lda p_pipeexiting
    bne @exit
    lda p_pipetimer
    cmp #60
    bcc @r
    lda #0
    jmp arrive_at
@exit:
    lda p_pipetimer
    cmp p_pipeexitticks
    jcc @r
    lda #PS_NORMAL
    sta p_state
    lda #1
    sta p_inair
    stz p_yvel
    rtl

w_arrive_at_climb:
    lda #0
; ArriveAt(p_pipetarea, p_pipetid): A = 1 for a door. Loads the area (forced blank), places the player.
arrive_at:
    sta e_t7
    lda p_pipetarea
    jsl game_arrive_load        ; LoadArea (forced blank; entities cleared; screen stays off)
    ; find the marker (first in row-major order)
    lda #$FFFF
    sta ar_mx
    lda area_link_ptr
    sta e_p2
    lda area_link_ptr+1
    sta e_p2+1
    ldx area_nlinkm
    beq @nf
    ldy #0
@l: iny
    iny
    lda [e_p2],y
    and #$00FF
    cmp p_pipetid
    beq @found
    iny
    dex
    bne @l
    bra @nf
@found:
    dey
    dey
    lda [e_p2],y
    and #$00FF
    sta ar_mx
    iny
    lda [e_p2],y
    and #$00FF
    sta ar_my
@nf:
    lda ar_mx
    bpl :+
    lda #2
    sta ar_mx
    lda area_h
    sec
    sbc #3
    sta ar_my
:   lda ar_mx
    sta e_tx
    lda ar_my
    sta e_ty
    jsl eng_tile_at
    sta e_t6
    lda #1
    sta p_pipeexiting
    stz p_pipetimer
    stz p_invisible
    lda e_t6
    cmp #T_PIPETL
    beq @ptop
    cmp #T_PIPETR
    bne @nptop
    dec e_tx
@ptop:
    ; X = (left*16 + 8) << 4 ; Y = (my*16) << 4 ; exit upward
    jsr set_x_left8
    lda ar_my
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    sta p_y
    lda #8
    sta p_pipedir
    lda #64
    sta p_pipeexitticks
    lda #PS_PIPE
    sta p_state
    SFX "PIPE"
    jmp @cam
@nptop:
    cmp #T_PIPEBL
    beq @pbot
    cmp #T_PIPEBR
    bne @nbot
    dec e_tx
@pbot:
    jsr set_x_left8
    lda ar_my
    inc a
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #40
    asl a
    asl a
    asl a
    asl a
    sta p_y
    lda #2
    sta p_pipedir
    lda #40
    sta p_pipeexitticks
    lda #PS_PIPE
    sta p_state
    SFX "PIPE"
    jmp @cam
@nbot:
    cmp #T_HPIPEMOUTHT
    beq @hp
    cmp #T_HPIPEMOUTHB
    bne @nh
    dec e_ty
@hp:
    jsl eng_tile_at
    lda f:lvl_var,x
    and #$00FF
    sta e_t5                    ; 1 = faces right
    lda ar_mx
    asl a
    asl a
    asl a
    asl a
    ldx e_t5
    bne :+
    clc
    adc #8
    bra :++
:   sec
    sbc #8
:   asl a
    asl a
    asl a
    asl a
    sta p_x
    lda e_ty
    inc a
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
    lda #4
    ldx #$FFFF
    ldy e_t5
    beq :+
    lda #6
    ldx #1
:   sta p_pipedir
    stx p_facing
    lda #40
    sta p_pipeexitticks
    lda #PS_PIPE
    sta p_state
    SFX "PIPE"
    bra @cam
@nh:
    cmp #T_DOORBOT
    beq @door
    cmp #T_DOORTOP
    bne @plain
    inc ar_my
@door:
    jsr set_xy_plain
    lda #PS_DOOR
    sta p_state
    SFX "DOOR"
    bra @cam
@plain:
    jsr set_xy_plain
    lda #PS_NORMAL
    sta p_state
    lda #1
    sta p_inair
@cam:
    jsl eng_center_camera
    ; UpdateCameraAfterArrive
    lda area_scroll
    cmp #SCROLL_FREE
    beq @done
    cmp #SCROLL_VERTICAL
    beq @done
    lda area_h
    asl a
    asl a
    asl a
    asl a
    sec
    sbc #192
    sta e_t2
    sta cam_y
    lda area_scroll
    cmp #SCROLL_LOCK
    beq @done
    jsr get_py
    sec
    sbc cam_y
    cmp #16
    bpl @done
    lda e_t2
    bmi :+
    lda #0
:   sta e_t3
    jsr get_py
    sec
    sbc #72
    sta e_t4
    sec
    sbc e_t3
    bpl :+
    lda e_t3
    sta e_t4
:   lda e_t4
    sta cam_y
@done:
    lda cam_x
    sta prev_cam_x
    lda cam_y
    sta prev_cam_y
    jsl game_arrive_show        ; spawn initial, draw, screen on, music
    rtl

set_x_left8:
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    clc
    adc #8
    asl a
    asl a
    asl a
    asl a
    sta p_x
    rts
set_xy_plain:
    lda ar_mx
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    sta p_x
    lda ar_my
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
    rts

; ================================================================== goal / course clear
; w_start_clear: A = card (0-2, or $FFFF) (C# StartClear(card, Cleared))
w_start_clear:
    ldx #RES_CLEARED
    stx w_clear_res
    bra start_clear
; w_boss_clear: A = result RES_FORTRESS / RES_WORLD (C# StartClear(-1, result): boss jingle, no auto-walk)
w_boss_clear:
    sta w_clear_res
    lda #$FFFF
start_clear:
    ldx w_clearing
    beq :+
    rtl
:   sta w_cardgot
    lda #1
    sta w_clearing
    lda w_clear_res
    cmp #RES_FORTRESS
    bne :+
    MUSIC "FORTRESSCLEAR"
    bra @m
:   cmp #RES_WORLD
    bne :+
    MUSIC "WORLDCLEAR"
    bra @m
:   MUSIC "CLEAR"
@m: jsr autowalk_on
    beq :+
    lda #PS_AUTOWALK
    sta p_state
:   lda #1
    sta w_endtimer
    stz w_tallydone
    stz p_star
    stz p_hurtinv
    rtl

; auto-walk after the goal: result == Cleared && kind != battle -> Z clear
autowalk_on:
    lda w_clear_res
    cmp #RES_CLEARED
    bne @no
    lda lvl_kind
    cmp #LK_BATTLE
    beq @no
    lda #1
    rts
@no: lda #0
    rts
autowalk_tick:
    lda pad_held
    pha
    lda pad_pressed
    pha
    lda #BTN_RIGHT
    sta pad_held
    stz pad_pressed
    stz p_state
    jsl pl_control
    lda #PS_AUTOWALK
    sta p_state
    pla
    pla
    rts

end_tick:
    inc w_endtimer
    lda w_wiggly
    and #$F0
    sec
    sbc #$90
    and #$FF
    sta w_wiggly
    ; keep walking off with normal physics while on screen (course clear only)
    jsr autowalk_on
    beq :+
    jsr get_px
    sec
    sbc cam_x
    cmp #300
    bpl :+
    jsr autowalk_tick
    jsl pl_power_update
    jsl pl_detect_solids
    jsl pl_animate
    jsl pl_timers
:   jsl ent_effects_tick
    lda w_endtimer
    cmp #201
    jcc @r
    lda w_tallydone
    bne @wait
    lda w_time
    beq @tdone
    cmp #3
    bcc :+
    lda #3
:   sta e_t0
    lda w_time
    sec
    sbc e_t0
    sta w_time
    lda #1
    sta g_hud_dirty
@add: lda #$0050
    jsl w_add_score
    dec e_t0
    bne @add
    lda w_endtimer
    and #3
    bne @r
    SFX "TALLY"
    rtl
@tdone:
    lda #1
    sta w_tallydone
    lda w_endtimer
    sta w_tallyend
    lda w_cardgot
    bmi @r
    jsl game_add_card
    rtl
@wait:
    ; EndTimer > tallyEnd + (bonus ? 200 : 60) && (MusicFinished || EndTimer > tallyEnd + 400)
    lda #60
    ldx bonus_lives
    beq :+
    lda #200
:   clc
    adc w_tallyend
    cmp w_endtimer
    bcs @r
    lda w_tallyend
    clc
    adc #400
    cmp w_endtimer
    bcc @done
    sep #$20
    jsl snd_status
    rep #$30
    and #$00FF
    bne @r
@done:
    lda w_clear_res
    sta w_result
@r: rtl
