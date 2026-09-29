; Hammer Bro's hammer (port of C# EnemyHammer, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Spawned only: ent_spawn(ET_ENEMY_HAMMER, arg = direction -1/+1). Arcs (XVel dir*$12, YVel -$48, +3/tick),
; spins (4 frames every 3 ticks), hurts the player, can't be destroyed.
;@entity ENEMY_HAMMER
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE ENEMY_HAMMER, ham_init, ham_update, ham_draw, ent_cb_none, ent_cb_none, ent_cb_hurt

; ent_t = t
.a16
.i16

ham_init:
    ENT_ENTER
    lda #EC_EPROJ
    sta ent_class,x
    lda #(F_STARIMM|F_HURTS)    ; UsesSlot = false, Stompable = false
    sta ent_fl,x
    lda ent_arg,x
    sta ent_facing,x
    ; XVel = dir * $12
    bmi :+
    lda #$12
    bra :++
:   lda #$10000-$12
:   sta ent_xvel,x
    lda #$10000-$48
    sta ent_yvel,x
    lda #3
    sta ent_hbx,x
    sta ent_hby,x
    lda #10
    sta ent_hbw,x
    sta ent_hbh,x
    sec
    rtl

ham_update:
    ENT_ENTER
    inc ent_t,x
    jsl ent_apply_vel
    lda ent_yvel,x
    clc
    adc #3
    sta ent_yvel,x
    ; Py > CamY + 240 -> gone
    lda cam_y
    clc
    adc #240
    sta es0
    jsl ent_py
    sec
    sbc es0
    beq :+
    bmi :+
    jsl ent_remove
:   rtl

ham_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_t,x
    ldy #3
    jsl ent_div
    and #3
    asl a
    phx
    tax
    lda f:ham_ids,x
    plx
    jsl ent_draw_meta
    rtl

.ifndef SPR_HAMMER_1
SPR_HAMMER_1 = 0
SPR_HAMMER_2 = 0
SPR_HAMMER_3 = 0
SPR_HAMMER_4 = 0
.endif
ham_ids: .word SPR_HAMMER_1, SPR_HAMMER_2, SPR_HAMMER_3, SPR_HAMMER_4
