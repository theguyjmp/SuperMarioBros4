; Thwomp (port of C# Thwomp, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; 24x32 block: rests at its spawn height; when the player is within 36 px horizontally and below its top it slams
; down (+6/tick, max $60), lands on the floor (sound + screen shake), waits 60 ticks, rises 12/16 px per tick.
; Only star, hammer and statue hurt it.
;@entity THWOMP codes=t
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE THWOMP, thwomp_init, thwomp_update, thwomp_draw, thwomp_hit, ent_cb_none, 0

; per-type fields: ent_state = phase, ent_t = t, v0 = homeY (px)
.a16
.i16

; new Thwomp(px, py): base(px - 4, py - 16)
thwomp_init:
    ENT_ENTER
    lda ent_x,x
    sec
    sbc #4*16
    sta ent_x,x
    lda ent_y,x
    sec
    sbc #16*16
    sta ent_y,x
    lda #24
    sta ent_wd,x
    lda #32
    sta ent_ht,x
    lda #2
    sta ent_hbx,x
    sta ent_hby,x
    lda #20
    sta ent_hbw,x
    lda #30
    sta ent_hbh,x               ; hitbox 2,2,20,30
    jsl ent_py
    sta ent_v0,x                ; homeY = Py
    lda ent_fl,x
    and #$FFFF^F_STOMP
    ora #(F_FIREIMM|F_TAILIMM|F_SHELLIMM)
    sta ent_fl,x
    sec
    rtl

thwomp_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    lda ent_state,x
    beq @rest
    cmp #1
    beq @fall
    cmp #2
    bne :+
    jmp @wait
:   jmp @rise
@rest:
    ; |p.CenterX - Cx| < 36 && p.Py + 16 > Py
    jsl ent_player_dx
    bpl :+
    eor #$FFFF
    inc a
:   cmp #36
    bcs @r
    jsl ent_py
    sta es0
    lda p_y
    ASR4
    clc
    adc #16
    sec
    sbc es0
    beq @r
    bmi @r
    lda #1
    sta ent_state,x
    stz ent_t,x
    stz ent_yvel,x
@r: rtl
@fall:
    lda ent_yvel,x
    clc
    adc #6
    bmi :+
    cmp #$61
    bcc :+
    lda #$60
:   sta ent_yvel,x
    clc
    adc ent_y,x
    sta ent_y,x
    ; FloorAt(Px + 4, Bottom) || FloorAt(Px + Wd - 4, Bottom) || Py > LevelPxH
    jsl ent_bottom
    sta es0
    jsl ent_px
    clc
    adc #4
    ldy es0
    jsl ent_floor_at
    bcs @land
    jsl ent_px
    clc
    adc #24-4
    ldy es0
    jsl ent_floor_at
    bcs @land
    lda area_h
    ENT_ASL4
    sta es1
    jsl ent_py
    sec
    sbc es1
    beq @r
    bmi @r
@land:
    ; Y = ((Bottom & ~15) - Ht) << 4
    lda es0
    and #$FFF0
    sec
    sbc #32
    ENT_ASL4
    sta ent_y,x
    lda #2
    sta ent_state,x
    stz ent_t,x
    ENT_SFX "THWOMP"
    lda #16
    sta w_shake                 ; W.Shake(16)
    rtl
@wait:
    lda ent_t,x
    cmp #61
    bcc :+
    lda #3
    sta ent_state,x             ; (t keeps counting, as in C#)
:   rtl
@rise:
    lda ent_y,x
    sec
    sbc #12
    sta ent_y,x
    jsl ent_py
    sec
    sbc ent_v0,x
    beq :+
    bpl @r2
:   lda ent_v0,x
    ENT_ASL4
    sta ent_y,x
    stz ent_state,x
    stz ent_t,x
@r2: rtl

; TakeHit: only Star / Hammer / Statue knock it off
thwomp_hit:
    ENT_ENTER
    lda ent_dmg
    cmp #D_STAR
    beq @k
    cmp #D_HAMMER
    beq @k
    cmp #D_STATUE
    beq @k
    clc
    rtl
@k: lda ent_dir
    jsl ent_knock_off
    sec
    rtl

thwomp_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    lda spr_arg_flags
    ora #SPR_VFLIP
    sta spr_arg_flags
:   lda ent_state,x
    cmp #1
    beq @f2
    cmp #2
    beq @f2
    ENT_DRAW SPR_THWOMP_1
    rtl
@f2: ENT_DRAW SPR_THWOMP_2
    rtl
