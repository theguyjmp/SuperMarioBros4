; Dry Bones (port of C# DryBones, src/Game/Entities/Enemies.cs). Owner: enemies-B agent.
; Red-Koopa style walker (turns at ledges). A stomp collapses it into a bone pile for 240 ticks (harmless, shakes in
; the last 50), then it reassembles and walks toward the player. Fire/tail/shell/bump do nothing; star, hammer and
; statue knock it off.
;@entity DRY_BONES codes=d
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE9"
ENT_VTABLE DRY_BONES, bones_init, bones_update, bones_draw, bones_hit, ent_cb_none, bones_touch

; per-type fields: v0 = collapsed (ticks left)
.a16
.i16

; new DryBones(px, py): base(px, py - 8)
bones_init:
    ENT_ENTER
    lda ent_y,x
    sec
    sbc #8*16
    sta ent_y,x
    lda #24
    sta ent_ht,x
    lda #8
    sta ent_hby,x
    lda #16
    sta ent_hbh,x               ; hitbox 2,8,12,16
    lda ent_fl,x
    ora #(F_FIREIMM|F_TAILIMM)
    sta ent_fl,x
    ; Facing = P.CenterX < px ? -1 : 1 (note: px, not px + 8); XVel = Facing * 8
    jsl ent_px
    sta es0
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc #8
    sec
    sbc es0
    bmi :+
    lda #1
    bra :++
:   lda #$FFFF
:   sta ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
    sec
    rtl

bones_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    lda ent_v0,x
    beq @walk
    dec a
    sta ent_v0,x
    bne @r
    ; reassembled: Hurts again, walks toward the player
    lda ent_fl,x
    ora #F_HURTS
    sta ent_fl,x
    jsl ent_face_player
    asl a
    asl a
    asl a
    sta ent_xvel,x
@r: rtl
@walk:
    lda #1                      ; MoveWalker(turnAtLedges = true)
    ldy #1
    jsl ent_move_walker
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

bones_touch:
    ENT_ENTER
    lda ent_v0,x
    bne @r
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda #240
    sta ent_v0,x
    lda ent_fl,x
    and #$FFFF^F_HURTS
    sta ent_fl,x
    stz ent_xvel,x
    ENT_SFX "BREAK"
@r: rtl
@hurt:
    jsl ent_hurt_player
    rtl

; TakeHit: only Star / Hammer / Statue
bones_hit:
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

bones_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_BONES_1
    rtl
:   jsl ent_draw_face
    lda ent_v0,x
    beq @walk
    ; bone pile at +8, shaking (1 px every 2 ticks) in its last 50 ticks
    lda #0
    ldy ent_v0,x
    cpy #50
    bcs :+
    lda ent_anim,x
    lsr a
    and #1
:   ldy #8
    jsl ent_draw_offset
    ENT_DRAW SPR_BONES_PILE
    rtl
@walk:
    lda ent_anim,x
    lsr a
    lsr a
    lsr a
    and #1
    bne :+
    ENT_DRAW SPR_BONES_1
    rtl
:   ENT_DRAW SPR_BONES_2
    rtl
