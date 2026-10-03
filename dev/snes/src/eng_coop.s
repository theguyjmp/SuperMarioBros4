; Engine: simultaneous 2-player co-op (2 PLAYER GAME). Mario (pad 1) and Luigi (pad 2) are in the level at once.
; The player code works on one player at a time (the p_* "slot"); the other one waits in co_blk and is swapped in
; with co_swap (eng_player.s) to run its share of the tick, its entity collisions and its drawing.
;  * the slot holds the "primary": the leader (camera, entity AI) -- the one further right (higher in vertical areas),
;    the one living when the other dies, the one that entered a pipe/door or touched the goal.
;  * lives are shared (g_lives). A player whose death animation ends loses a life and respawns at the partner
;    (small, blinking) while lives remain, else is out; the level is lost when the last player dies (the map takes
;    that life, as in 1P).
;  * players stand on / bounce off each other's heads (SMB3 battle style, no damage).
;  * a partner that falls behind / off screen is warped to the leader; while the leader rides a pipe or door the
;    partner is hidden and comes along.
; Single player (g_coop = 0) never gets here: w_tick runs w_tick1 directly.
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"
.include "spr_ids.inc"
.include "music.inc"
.scope scrinc
.include "scr_ids.inc"
.endscope

.import w_tick1, w_step2, ent_coop_collide
.import CO_X, CO_Y, CO_XVEL, CO_YVEL, CO_INAIR, CO_STATE, CO_FORM, CO_CARRY, CO_HURTINV, CO_DUCK, CO_CLIMB, CO_PWING
.export co_tick, co_other_alive, co_level_start, co_level_end
.ifdef scrinc::SCR_HOOKS
.import sv_opt_inf
.endif

.segment "BSS"
g_coop: .res 2              ; 1 = this level is played by both players at once (set by the screens per level)
g_form2: .res 2             ; Luigi's form in / out (Mario's is g_form)
co_st: .res 4               ; per player (0 Mario, 1 Luigi): 0 in the game, 1 out of lives
co_hide: .res 2             ; the partner rides along hidden (leader in a pipe / door)
co_area: .res 2
co_camx: .res 2
co_t0: .res 2

.segment "CODE14"
.a16
.i16

; carry set if the player in co_blk is in the game. Keeps Y.
other_in:
    lda co_cur
    eor #1
    asl a
    tax
    lda co_st,x
    bne @no
    sec
    rts
@no: clc
    rts

; co_other_alive: carry set if the partner (co_blk) is in the game and not dying. JSL, keeps X Y.
co_other_alive:
    phx
    jsr other_in
    bcc @r
    lda f:co_blk+CO_STATE
    cmp #PS_DYING
    beq @no
    sec
@r: plx
    rtl
@no: clc
    plx
    rtl

; ------------------------------------------------------------------ the tick
co_tick:
    jsr other_in
    bcs :+
    jml w_tick1                 ; partner out of lives: plain 1P tick
:   lda area_idx                ; area changed (pipe/door arrival): the partner's carried object is gone
    cmp co_area
    beq :+
    sta co_area
    lda #0
    sta f:co_blk+CO_CARRY
:   lda w_halt
    beq @nohalt
    ; power-up freeze: the partner's transform animation runs too (w_tick1 does the primary's)
    jsl co_swap
    lda p_transform
    beq :+
    dec p_transform
:   jsl co_swap
    jml w_tick1
@nohalt:
    lda w_endtimer
    ora w_result
    bne @t1
    ; roles: a dying primary hands the slot to a living partner, else the leader takes it
    lda p_state
    cmp #PS_DYING
    bne @lead
    lda f:co_blk+CO_STATE
    cmp #PS_DYING
    beq @t1
    jsl co_swap
    bra @t1
@lead:
    jsr leader
@t1:
    lda cam_x
    sta co_camx
    jsl w_tick1
    jsr cam_smooth
    ; ---- the partner
    jsr other_in
    jcc @done
    lda w_result
    ora w_endtimer
    ora w_halt
    jne @done
    lda p_state                 ; both down: no respawn, the level is lost (co_level_end takes the partner's life)
    cmp #PS_DYING
    jeq @done
    lda f:co_blk+CO_STATE
    cmp #PS_DYING
    jeq @dying
    lda p_state
    cmp #PS_PIPE
    jeq @hide
    cmp #PS_DOOR
    jeq @hide
    cmp #PS_AUTOWALK
    jeq @done
    cmp #PS_DYING
    jeq @done
    lda co_hide
    beq :+
    stz co_hide                 ; leader out of the pipe/door: the partner reappears with him
    jsr warp_other
:   jsl co_swap
    ; time up: both die
    lda lvl_time_def
    beq @step
    lda w_time
    bne @step
    lda w_clearing
    bne @step
    lda #0
    jsl pl_die
    jsl co_swap
    rtl
@step:
    jsl w_step2
    lda p_state
    cmp #PS_DYING
    beq @back
    jsl ent_coop_collide
    lda w_endtimer
    ora w_result
    bne @esc
    lda p_state
    cmp #PS_PIPE
    beq @esc
    cmp #PS_DOOR
    beq @esc
    cmp #PS_AUTOWALK
    beq @esc
    cmp #PS_DYING
    beq @back
    jsr heads                   ; partner on the primary's head
    jsl co_swap
    jsr heads                   ; primary on the partner's head
    jsr warp_check
    rtl
@back:
    jsl co_swap
@done:
    rtl
@esc:
    ; the partner entered a pipe/door or reached the goal: it leads now (stays in the slot)
    rtl
@dying:
    jsl co_swap
    jsr dying2
    jsl co_swap
    rtl
@hide:
    lda #1
    sta co_hide
    jsr warp_other              ; rides along with the leader
    rtl

; the leader takes the slot (camera): further right by 24+ px (vertical areas: higher by 32+ px)
leader:
    lda co_hide
    bne @r
    lda p_state
    beq :+
    cmp #PS_VINE
    bne @r
:   lda f:co_blk+CO_STATE
    beq :+
    cmp #PS_VINE
    bne @r
:   lda area_scroll
    cmp #SCROLL_VERTICAL
    bne @h
    lda p_y
    sec
    sbc f:co_blk+CO_Y
    bmi @r
    cmp #32*16
    bcc @r
    jsl co_swap
@r: rts
@h: lda f:co_blk+CO_X
    sec
    sbc p_x
    bcc @r
    cmp #24*16
    bcc @r
    jsl co_swap
    rts

; after a change of leader the camera glides (max 6 px per tick) instead of jumping
cam_smooth:
    lda area_idx
    cmp co_area
    bne @r
    lda cam_x
    sec
    sbc co_camx
    bmi @neg
    cmp #7
    bcc @r
    lda co_camx
    clc
    adc #6
    sta cam_x
@r: rts
@neg:
    cmp #$10000-6
    bcs @r
    lda co_camx
    sec
    sbc #6
    sta cam_x
    rts

; partner's death animation (partner in the slot): then a life is lost; respawn at the leader or out
dying2:
    jsl pl_dying_tick
    lda p_deathtimer
    cmp #$30+100
    bcc @r
    jsr lose_life
    beq @out
    lda #0
    jsl pl_init                 ; small, fresh
    lda f:co_blk+CO_X
    sta p_x
    lda f:co_blk+CO_Y
    sta p_y
    lda #120
    sta p_hurtinv               ; blinking, can't be hurt
    SFX "PIPE"
@r: rts
@out:
    lda co_cur
    asl a
    tax
    lda #1
    sta co_st,x
    lda #PS_FROZEN
    sta p_state
    lda #1
    sta p_invisible
    rts

; shared lives: -> A = lives left, Z set when none (infinite-lives assist: never)
lose_life:
.ifdef scrinc::SCR_HOOKS
    lda sv_opt_inf
    bne @inf
.endif
    lda #1
    sta g_hud_dirty
    lda g_lives
    beq @z
    dec g_lives
    lda g_lives
@z: rts
@inf: lda #1
    rts

; the partner (co_blk) -> the leader's position (falling, blinking a moment)
warp_other:
    lda p_x
    sta f:co_blk+CO_X
    lda p_y
    sta f:co_blk+CO_Y
    lda #0
    sta f:co_blk+CO_XVEL
    sta f:co_blk+CO_YVEL
    sta f:co_blk+CO_STATE
    sta f:co_blk+CO_CLIMB
    lda #1
    sta f:co_blk+CO_INAIR
    lda f:co_blk+CO_HURTINV
    cmp #60
    bcs :+
    lda #60
    sta f:co_blk+CO_HURTINV
:   rts

; leader in the slot: a partner left behind / off screen is warped to him
warp_check:
    lda co_hide
    bne @r
    jsr other_in
    bcc @r
    lda f:co_blk+CO_STATE
    beq :+
    cmp #PS_VINE
    bne @r
:   lda f:co_blk+CO_X
    lsr a
    lsr a
    lsr a
    lsr a
    sec
    sbc cam_x
    clc
    adc #32                     ; warp if sx < -24 or sx >= 280
    bmi @warp
    cmp #8
    bcc @warp
    cmp #312
    bcs @warp
    lda f:co_blk+CO_Y
    ASR4
    sec
    sbc cam_y
    bmi @up
    cmp #240
    bcs @warp
@r: rts
@up: cmp #$10000-160
    bcs @r
@warp:
    jmp warp_other

; the player in the slot lands on the partner's head: fast -> bounce (higher with jump held; an airborne partner
; is pushed down), slow -> stands on him and rides along
heads:
    lda co_hide
    jne @r
    lda p_state
    jne @r
    lda p_invisible
    jne @r
    lda f:co_blk+CO_STATE
    jne @r
    lda p_yvel
    jmi @r
    lda p_x
    sec
    sbc f:co_blk+CO_X
    bpl :+
    NEG16
:   cmp #12*16
    jcs @r
    lda f:co_blk+CO_Y
    ASR4
    sta co_t0                   ; partner's head
    lda f:co_blk+CO_FORM
    beq @sm
    lda f:co_blk+CO_DUCK
    beq :+
@sm: lda co_t0
    clc
    adc #16
    sta co_t0
:   lda p_y
    ASR4
    clc
    adc #32
    sec
    sbc co_t0                   ; feet - head
    jmi @r
    cmp #11
    jcs @r
    lda p_yvel
    cmp #$38                    ; slower than a (small) bounce comes back -> stand
    jcc @stand
    lda pad_held
    and #BTN_A
    beq :+
    lda #$10000-$50
    bra :++
:   lda #$10000-$30
:   jsl pl_bounce
    SFX "STOMP"
    lda f:co_blk+CO_INAIR
    beq @r
    lda f:co_blk+CO_YVEL
    bmi :+
    cmp #$20
    jcs @r
:   lda #$20
    sta f:co_blk+CO_YVEL
@r: rts
@stand:
    lda co_t0
    sec
    sbc #32
    asl a
    asl a
    asl a
    asl a
    sta p_y
    stz p_yvel
    stz p_inair
    lda f:co_blk+CO_XVEL
    clc
    adc p_x
    sta p_x
    rts

; ------------------------------------------------------------------ level start / end (game.s)
; co_level_start: Mario is set up in the slot -> Luigi joins beside him (form g_form2). JSL.
co_level_start:
    stz co_st
    stz co_st+2
    stz co_hide
    lda area_idx
    sta co_area
    lda p_x
    sta co_t0
    jsl co_swap                 ; Luigi in the slot
    lda g_form2
    jsl pl_init
    lda co_t0                   ; 20 px left of Mario, or right near the level start
    sec
    sbc #20*16
    bcc @right
    cmp #16*16
    bcs @set
@right:
    lda co_t0
    clc
    adc #20*16
@set:
    sta p_x
    lda f:co_blk+CO_Y
    sta p_y
    stz pad_held
    stz pad_pressed
    stz pad_released
    stz pad_prev
    jsl co_swap
    rtl

; co_level_end: Mario back in the slot (game.s reads his form as in 1P); g_form2 = Luigi's form. A partner still
; dying when the level ends loses that life too. JSL.
co_level_end:
    jsr other_in
    bcc :+
    lda f:co_blk+CO_STATE
    cmp #PS_DYING
    bne :+
    jsr lose_life
:   lda co_cur
    beq :+
    jsl co_swap
:   lda co_st+2
    bne @small
    lda g_result
    cmp #RES_DIED
    beq @small
    lda f:co_blk+CO_STATE
    cmp #PS_DYING
    beq @small
    lda f:co_blk+CO_FORM
    tax
    lda f:co_blk+CO_PWING
    beq :+
    ldx #PF_RACCOON
:   stx g_form2
    rtl
@small:
    stz g_form2
    rtl
