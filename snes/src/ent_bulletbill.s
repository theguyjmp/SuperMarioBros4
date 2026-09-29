; Bullet Bill (port of C# BulletBill, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Spawned by BILL_BLASTER (Y = direction -1/+1). Accelerates 1/tick to $18. For its first 16 ticks it is drawn
; "behind" (OBJ priority 2) so the high-priority cannon tiles hide it while it leaves the barrel.
; Stomp: falls off (half speed); fire immune; tail/shell/star/hammer/bump knock it off.
;@entity BULLET_BILL
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE BULLET_BILL, bill_init, bill_update, bill_draw, 0, 0, bill_touch

.a16
.i16

bill_init:
    ENT_ENTER
    lda ent_arg,x
    sta ent_facing,x
    lda ent_fl,x
    ora #(F_FIREIMM|F_BEHIND)
    sta ent_fl,x
    lda #1
    sta ent_hbx,x
    lda #2
    sta ent_hby,x
    lda #14
    sta ent_hbw,x
    lda #12
    sta ent_hbh,x               ; hitbox 1,2,14,12
    sec
    rtl

bill_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    lda ent_anim,x
    cmp #17
    bcc :+
    lda ent_fl,x
    and #$FFFF^F_BEHIND
    sta ent_fl,x
:   ; target = Facing * $18; XVel moves 1 toward it
    lda ent_facing,x
    bmi :+
    lda #$18
    bra :++
:   lda #$10000-$18
:   sta es0
    lda ent_xvel,x
    sec
    sbc es0
    beq @mv
    bpl :+
    inc ent_xvel,x
    bra @mv
:   dec ent_xvel,x
@mv: lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    rtl

bill_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda ent_fl,x
    ora #(F_DYING|F_KILLED)
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda #1
    sta ent_knock,x
    stz ent_yvel,x
    ; XVel = XVel / 2 (C# division: truncates toward zero)
    lda ent_xvel,x
    bpl :+
    eor #$FFFF
    inc a
    lsr a
    eor #$FFFF
    inc a
    bra :++
:   lsr a
:   sta ent_xvel,x
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

bill_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_fl,x
    and #F_DYING
    beq :+
    lda spr_arg_flags
    ora #SPR_VFLIP
    sta spr_arg_flags
:   ENT_DRAW SPR_BILL
    rtl
