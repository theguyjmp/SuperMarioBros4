; The 7 Koopalings, airship bosses (port of C# Koopaling + MagicRing, src/Game/Entities/Bosses.cs). Owner: bosses agent.
; variant = world number 1..7 (Lemmy, Roy, Iggy, Wendy, Morton, Larry, Ludwig): speed $0C+v, jump every 70-3v ticks
; (Roy/Morton/Ludwig = heavy landers: high jump, thud + shake, stun a grounded player 40 ticks), wand magic ring every
; 110-6v ticks aimed at the player at speed $14+v (Wendy's rings bounce off walls). 3 stomps (80-tick shell spin) or
; 10 fire/hammer/tail hits or a star -> knocked off, the wand drops at (Cx-8, Py): touching it clears the airship.
; Colors: the sprites module bakes this level's world Koopaling palette into SPR_KOOPALING_* and SPR_KL (accessory).
;@entity KOOPALING codes=K
;@entity BOSS_RING
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"
.include "boss.inc"

.segment "CODE10"
ENT_VTABLE KOOPALING, kl_init, kl_update, kl_draw, kl_hit, ent_cb_none, kl_touch
ENT_VTABLE BOSS_RING, ring_init, ring_update, ring_draw, ent_cb_none, ent_cb_none, ent_cb_hurt

; fields: v0 stomps left, v1 fire hits left, v2 castT, v3 hurt, v4 active, v5 hurtFlash, v6 variant (1-7)
.a16
.i16

; new Koopaling(px, py, world): base(px, py - 16), 16x32, hitbox 2,8,12,24, castT = 60
kl_init:
    ENT_ENTER
    lda ent_y,x
    sec
    sbc #16*16
    sta ent_y,x
    lda #32
    sta ent_ht,x
    lda #2
    sta ent_hbx,x
    lda #8
    sta ent_hby,x
    lda #12
    sta ent_hbw,x
    lda #24
    sta ent_hbh,x
    lda ent_fl,x
    and #$FFFF^F_SLOT
    sta ent_fl,x
    lda #F2_BOSS
    sta ent_fl2,x
    stz ent_points,x
    lda #3
    sta ent_v0,x
    lda #10
    sta ent_v1,x
    lda #60
    sta ent_v2,x
    ; variant = clamp(world, 1, 7) (C# EntityFactory.WorldNumber)
    lda lvl_world
    bne :+
    lda #1
:   cmp #8
    bcc :+
    lda #7
:   sta ent_v6,x
    lda #SONG_BOSS              ; boss room: uploaded under the area-load blank
    sta area_music
    sec
    rtl

; A = 1 if Roy (2), Morton (5) or Ludwig (7)
heavy:
    lda ent_v6,x
    cmp #2
    beq @y
    cmp #5
    beq @y
    cmp #7
    beq @y
    lda #0
    rts
@y: lda #1
    rts

kl_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    inc ent_anim,x
    lda ent_v5,x
    beq :+
    dec ent_v5,x
:   lda ent_v4,x
    bne @act
    lda #180
    jsl boss_player_near
    bcs :+
    rtl
:   lda #1
    sta ent_v4,x
    lda #SONG_BOSS
    jsl boss_activate
@act:
    lda ent_v3,x
    beq @live
    ; hurt: spin in the shell at Facing*$30; a wall flips Facing back (C# quirk: MoveWalker turns, then Facing = -Facing)
    dec ent_v3,x
    lda ent_facing,x
    bmi :+
    lda #$30
    bra :++
:   lda #$10000-$30
:   sta ent_xvel,x
    lda #0
    ldy #1
    jsl ent_move_walker
    bcc :+
    lda ent_facing,x
    NEG16
    sta ent_facing,x
:   rtl
@live:
    jsl ent_face_player
    sta ent_facing,x
    ; target = Facing * Speed (Speed = $0C + variant), 0 when |P.CenterX - Cx| < 20
    lda ent_v6,x
    clc
    adc #$0C
    ldy ent_facing,x
    bpl :+
    NEG16
:   sta es0
    lda #20
    jsl boss_player_near
    bcc :+
    stz es0
:   lda es0
    ldy #1
    jsl boss_approach
    ; if (OnGround && t % (70 - variant*3) == 0) YVel = heavy ? -$60 : -$48 - variant*2
    lda ent_fl,x
    and #F_GROUND
    beq @nojump
    lda ent_v6,x
    asl a
    adc ent_v6,x
    sta es0
    lda #70
    sec
    sbc es0
    tay
    lda ent_t,x
    jsl ent_mod
    cmp #0
    bne @nojump
    jsr heavy
    beq :+
    lda #$10000-$60
    bra :++
:   lda ent_v6,x
    asl a
    clc
    adc #$48
    NEG16
:   sta ent_yvel,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
@nojump:
    lda ent_fl,x
    and #F_GROUND
    sta es1                     ; 0 = was in the air
    lda #0
    ldy #1
    jsl ent_move_walker
    lda es1
    bne @cast
    lda ent_fl,x
    and #F_GROUND
    beq @cast
    jsr heavy
    beq @cast
    ; heavy landing: thud, shake, stun a grounded player
    ENT_SFX "THWOMP"
    lda #12
    sta w_shake
    lda p_inair
    bne @cast
    lda #40
    sta p_stun
@cast:
    dec ent_v2,x
    beq :+
    jpl @r
:   ; castT = 110 - variant*6
    lda ent_v6,x
    asl a
    adc ent_v6,x
    asl a
    sta es0
    lda #110
    sec
    sbc es0
    sta ent_v2,x
    ; dx = P.CenterX - Cx, dy = P.Py + 20 - (Py + 8), sp = $14 + variant
    jsl ent_player_dx
    sta bm_dx
    jsl ent_py
    clc
    adc #8
    sta es0
    lda p_y
    ASR4
    clc
    adc #20
    sec
    sbc es0
    sta bm_dy
    lda ent_v6,x
    clc
    adc #$14
    jsl boss_ring_vel
    ; new MagicRing(Cx - 4, Py + 6, vx, vy, bounce = variant == 4)
    jsl ent_cx
    sec
    sbc #4
    sta ent_new_x
    jsl ent_py
    clc
    adc #6
    sta ent_new_y
    ldy #0
    lda ent_v6,x
    cmp #4
    bne :+
    ldy #1
:   lda #ET_BOSS_RING
    jsl ent_spawn
    bcc :+
    lda bm_dx
    sta ent_xvel,y
    lda bm_dy
    sta ent_yvel,y
:   ENT_SFX "MAGIC"
@r: rtl

kl_touch:
    ENT_ENTER
    lda ent_v3,x
    bne @hurt
    jsl ent_can_stomp
    bcc @hurt
    lda #$10000-$40
    jsl pl_bounce
    ENT_SFX "BOSSHIT"
    dec ent_v0,x
    beq @def
    bmi @def
    lda #80
    sta ent_v3,x
    rtl
@def:
    jsr defeat
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

kl_hit:
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
    lda #10
    sta ent_v5,x
@no: clc
    rtl
@d: jsr defeat
    sec
    rtl

; Defeated(WorldCleared, Cx - 8, Py)
defeat:
    jsl ent_cx
    sec
    sbc #8
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #BOSS_R_WORLD
    jsl boss_defeated
    rts

kl_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_KOOPALING_SHELL
    rtl
:   ; flash = hurtFlash > 0 && (hurtFlash & 2)
    lda ent_v5,x
    and #BOSS_FLASH_MASK
    sta es0
    lda ent_v3,x
    beq :+
    lda es0
    bne @shf
    ENT_DRAW SPR_KOOPALING_SHELL
    rtl
@shf:
    ENT_DRAW SPR_KOOPALING_SHELL_FLASH
    rtl
:   lda ent_v2,x
    cmp #12
    bpl @nocast
    lda es0
    bne :+
    ENT_DRAW SPR_KOOPALING_CAST
    bra :++
:   ENT_DRAW SPR_KOOPALING_CAST_FLASH
:   ENT_DRAW SPR_KL
    ; wand at Px + (Facing > 0 ? 12 : -4), Py + 6
    lda ent_facing,x
    bmi :+
    lda #12
    bra :++
:   lda #$10000-4
:   ldy #6
    jsl ent_draw_offset
    ENT_DRAW SPR_WAND
    rtl
@nocast:
    lda ent_fl,x
    and #F_GROUND
    bne @gr
    lda es0
    bne :+
    ENT_DRAW SPR_KOOPALING_JUMP
    bra @acc
:   ENT_DRAW SPR_KOOPALING_JUMP_FLASH
    bra @acc
@gr:
    ; walk if |XVel| > 2 && (Anim / 8) % 2 == 0
    lda ent_xvel,x
    bpl :+
    NEG16
:   cmp #3
    bcc @stand
    lda ent_anim,x
    and #8
    bne @stand
    lda es0
    bne :+
    ENT_DRAW SPR_KOOPALING_WALK
    bra @acc
:   ENT_DRAW SPR_KOOPALING_WALK_FLASH
    bra @acc
@stand:
    lda es0
    bne :+
    ENT_DRAW SPR_KOOPALING_STAND
    bra @acc
:   ENT_DRAW SPR_KOOPALING_STAND_FLASH
@acc:
    ENT_DRAW SPR_KL
    rtl

; ================================================================== MagicRing: arg = bounce (Wendy)
; 8x8, hitbox 1,1,6,6, enemy projectile, star-immune, not stompable; bounces off walls for 400 ticks or flies 300 ticks
ring_init:
    ENT_ENTER
    lda #8
    sta ent_wd,x
    sta ent_ht,x
    lda #1
    sta ent_hbx,x
    sta ent_hby,x
    lda #6
    sta ent_hbw,x
    sta ent_hbh,x
    lda #EC_EPROJ
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^(F_SLOT|F_STOMP)
    ora #F_STARIMM
    sta ent_fl,x
    sec
    rtl

ring_update:
    ENT_ENTER
    inc ent_t,x
    jsl ent_apply_vel
    lda ent_arg,x
    beq @plain
    ; if (SolidAt(Px+4, Py) || SolidAt(Px+4, Py+8)) YVel = -YVel
    jsl ent_px
    clc
    adc #4
    sta es0
    jsl ent_py
    sta es1
    tay
    lda es0
    jsl ent_solid_at
    bcs @fy
    lda es1
    clc
    adc #8
    tay
    lda es0
    jsl ent_solid_at
    bcc @x
@fy: lda ent_yvel,x
    NEG16
    sta ent_yvel,x
@x: ; if (SolidAt(Px, Py+4) || SolidAt(Px+8, Py+4)) XVel = -XVel
    lda es1
    clc
    adc #4
    sta es1
    jsl ent_px
    sta es0
    ldy es1
    jsl ent_solid_at
    bcs @fx
    lda es0
    clc
    adc #8
    ldy es1
    jsl ent_solid_at
    bcc @t
@fx: lda ent_xvel,x
    NEG16
    sta ent_xvel,x
@t: lda #400
    bra @life
@plain:
    lda #300
@life:
    cmp ent_t,x
    bcs :+
    jsl ent_remove
:   rtl

ring_draw:
    ENT_ENTER
    ; pal = (t/3) % 2 == 0 ? ring.a : ring.b ; img = (t/4) % 2 == 0 ? ring.1 : ring.2
    lda ent_t,x
    ldy #3
    jsl ent_div
    and #1
    sta es0
    lda ent_t,x
    and #4
    bne @two
    lda es0
    bne :+
    ENT_DRAW SPR_RING_1
    rtl
:   ENT_DRAW SPR_RING_1_B
    rtl
@two:
    lda es0
    bne :+
    ENT_DRAW SPR_RING_2
    rtl
:   ENT_DRAW SPR_RING_2_B
    rtl
