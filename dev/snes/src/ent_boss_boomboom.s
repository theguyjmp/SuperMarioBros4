; Boom Boom, the fortress boss (port of C# BoomBoom, src/Game/Entities/Bosses.cs). Owner: bosses agent.
; Waits until the player is within 170 px, then locks the camera (boss arena), runs at the player (speed $14, +-2/tick),
; jumps every 80 ticks (every 3rd jump high). 3 stomps (90-tick spin after each) or 5 fire/hammer/tail hits
; (30-tick hurt) or a star -> knocked off, "?" orb falls at (CamX+120, CamY+40): touching it ends the fortress.
;@entity BOOMBOOM codes=Z
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"
.include "boss.inc"

.segment "CODE10"
ENT_VTABLE BOOMBOOM, bb_init, bb_update, bb_draw, bb_hit, ent_cb_none, bb_touch

; fields: v0 = stomps left, v1 = fire hits left, v2 = jumps, v3 = hurt timer, v4 = active, ent_t = t, ent_anim = Anim
.a16
.i16

; new BoomBoom(px, py): base(px - 8, py - 16), 32x32, hitbox 5,6,22,26
bb_init:
    ENT_ENTER
    lda ent_x,x
    sec
    sbc #8*16
    sta ent_x,x
    lda ent_y,x
    sec
    sbc #16*16
    sta ent_y,x
    lda #32
    sta ent_wd,x
    sta ent_ht,x
    lda #5
    sta ent_hbx,x
    lda #6
    sta ent_hby,x
    lda #22
    sta ent_hbw,x
    lda #26
    sta ent_hbh,x
    lda ent_fl,x
    and #$FFFF^F_SLOT
    sta ent_fl,x
    lda #F2_BOSS
    sta ent_fl2,x
    stz ent_points,x
    lda #3
    sta ent_v0,x
    lda #5
    sta ent_v1,x
    ; boss room: the area song becomes the boss song, so the engine uploads it under the area-load blank
    lda #SONG_BOSS
    sta area_music
    sec
    rtl

bb_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    inc ent_anim,x
    lda ent_v4,x
    bne @act
    lda #170
    jsl boss_player_near
    bcs :+
    rtl
:   lda #1
    sta ent_v4,x
    lda #SONG_BOSS
    jsl boss_activate
@act:
    lda ent_v3,x
    beq @run
    ; hurt: XVel = (hurt / 16) % 2 == 0 ? $28 : -$28 (after the decrement)
    dec a
    sta ent_v3,x
    and #16
    beq :+
    lda #$10000-$28
    bra :++
:   lda #$28
:   sta ent_xvel,x
    lda #0
    ldy #1
    jsl ent_move_walker
    rtl
@run:
    jsl ent_face_player
    sta ent_facing,x
    bmi :+
    lda #$14
    bra :++
:   lda #$10000-$14
:   ldy #2
    jsl boss_approach
    ; if (OnGround && t % 80 == 0) { jumps++; YVel = jumps % 3 == 0 ? -$58 : -$38; }
    lda ent_fl,x
    and #F_GROUND
    beq @mv
    lda ent_t,x
    ldy #80
    jsl ent_mod
    cmp #0
    bne @mv
    inc ent_v2,x
    lda ent_v2,x
    ldy #3
    jsl ent_mod
    cmp #0
    bne :+
    lda #$10000-$58
    bra :++
:   lda #$10000-$38
:   sta ent_yvel,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
@mv:
    lda #0
    ldy #1
    jsl ent_move_walker
    rtl

; OnPlayerTouch: stomp (not while hurt) -> bounce, BossHit, 3rd stomp defeats; else hurt the player
bb_touch:
    ENT_ENTER
    lda ent_v3,x
    bne @hurt
    jsl ent_can_stomp
    bcc @hurt
    lda #$10000-$40             ; p.Bounce(Phys.StompBounce)
    jsl pl_bounce
    ENT_SFX "BOSSHIT"
    dec ent_v0,x
    beq @def
    bmi @def
    lda #90
    sta ent_v3,x
    rtl
@def:
    jsr defeat
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

; TakeHit: fire/hammer/tail = 1 of 5 hits (30-tick hurt, not affected); star = defeated
bb_hit:
    ENT_ENTER
    lda ent_v3,x
    bne @no
    lda ent_dmg
    cmp #D_FIRE
    beq @hit
    cmp #D_HAMMER
    beq @hit
    cmp #D_TAIL
    beq @hit
    cmp #D_STAR
    bne @no
    jsr defeat
    sec
    rtl
@hit:
    ENT_SFX "BOSSHIT"
    dec ent_v1,x
    beq @d
    bmi @d
    lda #30
    sta ent_v3,x
@no: clc
    rtl
@d: jsr defeat
    sec
    rtl

; Defeated(FortressCleared, CamX + 120, CamY + 40)
defeat:
    lda cam_x
    clc
    adc #120
    sta ent_new_x
    lda cam_y
    clc
    adc #40
    sta ent_new_y
    lda #BOSS_R_FORTRESS
    jsl boss_defeated
    rts

bb_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_BOOMBOOM_HURT
    rtl
:   lda ent_v4,x
    bne :+
    ENT_DRAW SPR_BOOMBOOM_STAND
    rtl
:   lda ent_v3,x
    beq @nh
    and #BOSS_FLASH_MASK
    beq :+
    ENT_DRAW SPR_BOOMBOOM_HURT_FLASH
    rtl
:   ENT_DRAW SPR_BOOMBOOM_HURT
    rtl
@nh:
    lda ent_fl,x
    and #F_GROUND
    bne :+
    ENT_DRAW SPR_BOOMBOOM_JUMP
    rtl
:   lda ent_anim,x
    ldy #6
    jsl ent_div
    and #1
    bne :+
    ENT_DRAW SPR_BOOMBOOM_RUN1
    rtl
:   ENT_DRAW SPR_BOOMBOOM_RUN2
    rtl
