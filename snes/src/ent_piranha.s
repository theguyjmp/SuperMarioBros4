; Piranha Plant / Venus Fire Trap (port of C# Piranha) and the Venus fireball (C# EnemyFire). Owner: engine agent.
; Place the spawn char in the cell directly above the pipe's left column: e Piranha, v Venus.
; VENUS_FIRE: ent_spawn(ET_VENUS_FIRE, arg = 0) with ent_xvel/ent_yvel set by the caller afterwards (Y = its slot);
; any enemy may reuse it as a generic 8x8 straight-flying enemy fireball (lives 400 ticks).
;@entity PIRANHA codes=ev
;@entity VENUS_FIRE
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE PIRANHA, piranha_init, piranha_update, piranha_draw, piranha_hit, ent_cb_none, piranha_touch
ENT_VTABLE VENUS_FIRE, efire_init, efire_update, efire_draw, ent_cb_none, ent_cb_none, ent_cb_hurt

; v0 = venus, v1 = homeY (px), v2 = height ; ent_state = phase ; ent_t = t
.a16
.i16

piranha_init:
    ENT_ENTER
    lda ent_arg,x
    cmp #'v'
    bne :+
    lda #1
    sta ent_v0,x
:   ; base(px + 8, pipeTopPy = py + 16)
    lda ent_x,x
    clc
    adc #8*16
    sta ent_x,x
    lda ent_y,x
    clc
    adc #16*16
    sta ent_y,x
    jsl ent_py
    sta ent_v1,x                ; homeY
    lda #24
    ldy ent_v0,x
    beq :+
    lda #32
:   sta ent_v2,x                ; height
    sta ent_ht,x
    sec
    sbc #4
    sta ent_hbh,x
    lda #3
    sta ent_hbx,x
    lda #2
    sta ent_hby,x
    lda #10
    sta ent_hbw,x
    lda #(F_HURTS|F_BEHIND|F_SLOT) ; Stompable = false
    sta ent_fl,x
    sec
    rtl

piranha_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    inc ent_anim,x
    lda ent_state,x
    bne @p1
    ; wait: t > 60 && |P.CenterX - Cx| > 24
    lda ent_t,x
    cmp #61
    jcc @hurts
    jsl ent_player_dx
    bpl :+
    NEG16
:   cmp #25
    jcc @hurts
    lda #1
    sta ent_state,x
    stz ent_t,x
    brl @hurts
@p1: cmp #1
    bne @p2
    ; rise 1 px per tick until Py <= homeY - height
    lda ent_y,x
    sec
    sbc #16
    sta ent_y,x
    lda ent_v1,x
    sec
    sbc ent_v2,x
    sta es0
    jsl ent_py
    sec
    sbc es0
    beq :+
    bpl @hurts
:   lda es0
    ENT_ASL4
    sta ent_y,x
    lda #2
    sta ent_state,x
    stz ent_t,x
    bra @hurts
@p2: cmp #2
    bne @p3
    lda ent_v0,x
    beq :+
    lda ent_t,x
    cmp #30
    bne :+
    jsr venus_shoot
:   lda ent_t,x
    cmp #61
    bcc @hurts
    lda #3
    sta ent_state,x
    stz ent_t,x
    bra @hurts
@p3: lda ent_y,x
    clc
    adc #16
    sta ent_y,x
    jsl ent_py
    sec
    sbc ent_v1,x
    bmi @hurts
    lda ent_v1,x
    ENT_ASL4
    sta ent_y,x
    stz ent_state,x
    stz ent_t,x
@hurts:
    ; Hurts = Py < homeY - 4
    lda ent_fl,x
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda ent_v1,x
    sec
    sbc #4
    sta es0
    jsl ent_py
    sec
    sbc es0
    bpl :+
    lda ent_fl,x
    ora #F_HURTS
    sta ent_fl,x
:   rtl

venus_shoot:
    ; dx = P.CenterX - Cx ; dy = P.Py + 16 - (Py + 8) ; vx = dx < 0 ? -$10 : $10 ; vy = |dy| < 24 ? 0 : +-$0C
    jsl ent_player_dx
    sta es0
    jsl ent_py
    clc
    adc #8
    sta es1
    lda p_y
    ASR4
    clc
    adc #16
    sec
    sbc es1
    sta es1                     ; dy
    ; EnemyFire(Cx - 4, Py + 6)
    jsl ent_cx
    sec
    sbc #4
    sta ent_new_x
    jsl ent_py
    clc
    adc #6
    sta ent_new_y
    lda #ET_VENUS_FIRE
    ldy #0
    jsl ent_spawn
    bcc @r
    lda #$10
    bit es0
    bpl :+
    lda #$10000-$10
:   sta ent_xvel,y
    lda es1
    bpl :+
    NEG16
:   cmp #24
    bcs :+
    lda #0
    bra @vy
:   lda #$0C
    bit es1
    bpl @vy
    lda #$10000-$0C
@vy: sta ent_yvel,y
@r: rts

piranha_touch:
    ENT_ENTER
    lda ent_fl,x
    and #F_HURTS
    beq :+
    jsl ent_hurt_player
:   rtl

; TakeHit: only while out of the pipe (Py < homeY - 2)
piranha_hit:
    ENT_ENTER
    lda ent_v1,x
    dec a
    dec a
    sta es0
    jsl ent_py
    sec
    sbc es0
    bmi :+
    clc
    rtl
:   lda ent_fl,x
    ora #(F_REMOVE|F_KILLED)
    sta ent_fl,x
    jsl ent_puff
    lda #$0100
    jsl ent_score
    sec
    rtl

piranha_draw:
    ENT_ENTER
    lda #4                      ; behind pipes
    sta spr_arg_flags
    lda ent_v0,x
    bne @venus
    lda ent_anim,x
    and #8
    bne :+
    ENT_DRAW SPR_PIRANHA_1
    rtl
:   ENT_DRAW SPR_PIRANHA_2
    rtl
@venus:
    ; faces the player (P.CenterX > Cx); mouth open while shooting
    jsl ent_player_dx
    beq :+
    bpl :++
:   lda #5
    sta spr_arg_flags
:   lda ent_state,x
    cmp #2
    bne @c
    lda ent_t,x
    cmp #23
    bcc @c
    cmp #40
    bcs @c
    ENT_DRAW SPR_VENUS_2
    rtl
@c: ENT_DRAW SPR_VENUS_1
    rtl

; ------------------------------------------------------------------ EnemyFire
efire_init:
    ENT_ENTER
    lda #EC_EPROJ
    sta ent_class,x
    lda #8
    sta ent_wd,x
    sta ent_ht,x
    lda #1
    sta ent_hbx,x
    sta ent_hby,x
    lda #6
    sta ent_hbw,x
    sta ent_hbh,x
    lda #(F_HURTS|F_STARIMM)
    sta ent_fl,x
    sec
    rtl

efire_update:
    ENT_ENTER
    inc ent_t,x
    jsl ent_apply_vel
    lda ent_t,x
    cmp #401
    bcc :+
    jsl ent_remove
:   rtl

efire_draw:
    ENT_ENTER
    lda ent_t,x
    lsr a
    and #3
    beq @f1
    cmp #1
    beq @f2
    cmp #2
    beq @f3
    ENT_DRAW SPR_FIREBALL_4
    rtl
@f1: ENT_DRAW SPR_FIREBALL_1
    rtl
@f2: ENT_DRAW SPR_FIREBALL_2
    rtl
@f3: ENT_DRAW SPR_FIREBALL_3
    rtl
