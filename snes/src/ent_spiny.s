; Spiny and Spiny egg (port of C# Spiny, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Level char 's' = walking Spiny. ent_spawn(ET_SPINY, arg = 1) = egg (Lakitu's throw; it hatches when it lands).
; Can't be stomped; fire/tail/shell/star/hammer/bump knock it off (C# base TakeHit).
;@entity SPINY codes=s
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE SPINY, spiny_init, spiny_update, spiny_draw, 0, 0, ent_cb_hurt

; v0 = egg
.a16
.i16

spiny_init:
    ENT_ENTER
    lda ent_fl,x
    and #$FFFF^F_STOMP
    sta ent_fl,x
    lda #4
    sta ent_hby,x
    lda #12
    sta ent_hbh,x               ; hitbox 2,4,12,12
    lda ent_arg,x
    cmp #1
    bne :+
    sta ent_v0,x                ; egg
:   jsl ent_face_player_set     ; Facing = P.CenterX < px + 8 ? -1 : 1
    ldy ent_v0,x
    bne :+
    asl a
    asl a
    asl a
    sta ent_xvel,x              ; egg ? 0 : Facing * 8
:   sec
    rtl

spiny_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    lda #0                      ; MoveWalker(false)
    ldy #1
    jsl ent_move_walker
    lda ent_v0,x
    beq @nf
    lda ent_fl,x
    and #F_GROUND
    beq @nf
    stz ent_v0,x                ; hatched: walks toward the player
    jsl ent_face_player
    sta ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
@nf:
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

spiny_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_SPINY_1
    rtl
:   jsl ent_draw_face
    lda ent_v0,x
    beq @walk
    lda ent_anim,x
    and #4
    bne :+
    ENT_DRAW SPR_SPINYEGG_1
    rtl
:   ENT_DRAW SPR_SPINYEGG_2
    rtl
@walk:
    lda ent_anim,x
    and #8
    bne :+
    ENT_DRAW SPR_SPINY_1
    rtl
:   ENT_DRAW SPR_SPINY_2
    rtl
