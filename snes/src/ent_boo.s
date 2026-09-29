; Boo (port of C# Boo, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Chases while the player's back is turned (accelerates 1/tick in X, 1 every other tick in Y toward the player),
; hides its face and brakes while the player looks at it. Not stompable, fire/tail immune; shell/star/hammer/bump kill.
;@entity BOO codes=u
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE BOO, boo_init, boo_update, boo_draw, 0, 0, 0
; hit = engine default (C# Entity.TakeHit with the immunity flags), bump = TakeHit(Bump), touch = p.Hurt()

; per-type fields: v0 = shy
.a16
.i16

boo_init:
    ENT_ENTER
    lda ent_fl,x
    and #$FFFF^F_STOMP
    ora #(F_FIREIMM|F_TAILIMM)
    sta ent_fl,x
    lda #2
    sta ent_hby,x
    lda #12
    sta ent_hbh,x               ; hitbox 2,2,12,12
    sec
    rtl

boo_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   jsl ent_player_dx           ; es0 = P.CenterX - Cx
    sta es0
    stz ent_v0,x
    ; facingBoo = (p.Facing > 0 && Cx > p.CenterX) || (p.Facing < 0 && Cx < p.CenterX)
    lda p_facing
    beq boo_chase
    bmi boo_pl
    lda es0
    bmi boo_shy
    bra boo_chase
boo_pl: lda es0
    beq boo_chase
    bmi boo_chase
boo_shy:
    lda #1
    sta ent_v0,x
    ; XVel -= Sign(XVel); YVel -= Sign(YVel)
    lda ent_xvel,x
    ENT_SIGN
    sta es1
    lda ent_xvel,x
    sec
    sbc es1
    sta ent_xvel,x
    lda ent_yvel,x
    ENT_SIGN
    sta es1
    lda ent_yvel,x
    sec
    sbc es1
    sta ent_yvel,x
    bra boo_move
boo_chase:
    ; tx = p.CenterX > Cx ? $0C : -$0C
    lda es0
    beq :+
    bmi :+
    lda #$0C
    bra :++
:   lda #$10000-$0C
:   sta es1
    lda ent_xvel,x
    sec
    sbc es1
    beq boo_xok
    bpl :+
    inc ent_xvel,x
    bra boo_xok
:   dec ent_xvel,x
boo_xok:
    ; ty = p.Py + 12 > Py ? 8 : -8 (only on even frames)
    lda w_frame
    and #1
    bne boo_face
    jsl ent_py
    sta es1
    lda p_y
    ASR4
    clc
    adc #12
    sec
    sbc es1
    beq :+
    bmi :+
    lda #8
    bra :++
:   lda #$10000-8
:   sta es1
    lda ent_yvel,x
    sec
    sbc es1
    beq boo_face
    bpl :+
    inc ent_yvel,x
    bra boo_face
:   dec ent_yvel,x
boo_face:
    ; Facing = XVel > 0 ? 1 : -1
    lda ent_xvel,x
    beq :+
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   sta ent_facing,x
boo_move:
    jsl ent_apply_vel
    rtl

boo_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_BOO_2
    rtl
:   jsl ent_draw_face
    lda ent_v0,x
    beq :+
    ENT_DRAW SPR_BOO_2
    rtl
:   ENT_DRAW SPR_BOO_1
    rtl
