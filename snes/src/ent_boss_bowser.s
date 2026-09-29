; Bowser, the final boss (port of C# Bowser + BowserFire, src/Game/Entities/Bosses.cs). Owner: bosses agent.
; Activates within 190 px (camera lock + "bowser"). State 0: walks at 8 toward the player (stops within 40 px), breathes
; fire every 140 ticks aimed at the player's head/body height; after jumpT (200, then 220) state 1: leaps (-$70) toward
; the player at $14, dives ($40) when above him, lands -> thud, shake 20, stun a grounded player 30, breaks the Brick
; tiles under his feet (state 2, 50 ticks). Falling through the broken floor below CamY+150 or 24 fire/hammer hits
; defeat him; once he has fallen below CamY+240: 90 ticks -> "bosswin", 260 ticks -> level result GameCleared.
;@entity BOWSER codes=Y
;@entity BOSS_FIRE
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"
.include "boss.inc"

.segment "CODE10"
ENT_VTABLE BOWSER, bw_init, bw_update, bw_draw, bw_hit, ent_cb_none, ent_cb_hurt
ENT_VTABLE BOSS_FIRE, bf_init, bf_update, bf_draw, ent_cb_none, ent_cb_none, ent_cb_hurt

; fields: v0 fire hits left, ent_state = state, v1 breathT, v2 jumpT, v3 fallTimer, v4 active, v5 hurtFlash,
;         v6 anim (breath pose), v7 fell, ent_t = t, ent_anim = Anim
.a16
.i16

; new Bowser(px, py): base(px - 8, py - 24), 32x40, hitbox 4,8,24,32; not stompable, tail/shell immune
bw_init:
    ENT_ENTER
    lda ent_x,x
    sec
    sbc #8*16
    sta ent_x,x
    lda ent_y,x
    sec
    sbc #24*16
    sta ent_y,x
    lda #32
    sta ent_wd,x
    lda #40
    sta ent_ht,x
    lda #4
    sta ent_hbx,x
    lda #8
    sta ent_hby,x
    lda #24
    sta ent_hbw,x
    lda #32
    sta ent_hbh,x
    lda ent_fl,x
    and #$FFFF^(F_SLOT|F_STOMP)
    ora #(F_TAILIMM|F_SHELLIMM)
    sta ent_fl,x
    lda #F2_BOSS
    sta ent_fl2,x
    stz ent_points,x
    lda #24
    sta ent_v0,x
    lda #120
    sta ent_v1,x
    lda #200
    sta ent_v2,x
    lda #SONG_BOWSER            ; boss room song (plays at once if he is spawned with the area, see DESIGN.md)
    sta area_music
    sec
    rtl

bw_update:
    ENT_ENTER
    lda ent_v7,x
    beq @notfell
    ; fell: Y += YVel; YVel = min($40, YVel + 3); ++fallTimer == 90 -> bosswin; == 260 -> GameCleared
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #3
    cmp #$41
    bmi :+
    lda #$40
:   sta ent_yvel,x
    inc ent_v3,x
    lda ent_v3,x
    cmp #90
    bne :+
    lda #SONG_BOSSWIN
    jsl boss_music
    rtl
:   cmp #260
    bne :+
    jsl boss_result
:   rtl
@notfell:
    lda ent_fl,x
    and #F_DYING
    beq @alive
    lda ent_knock,x
    beq @alive
    ; knocked fall: once below CamY + 240 (and no result yet) -> fell
    jsl ent_update_knocked
    jsl ent_py
    sec
    sbc cam_y
    sec
    sbc #241
    bmi :+
    lda w_result
    bne :+
    lda #1
    sta ent_v7,x
    stz ent_v3,x
    ; ent_update_knocked removes it below CamY+260: keep it (the fell branch takes over)
    lda ent_fl,x
    and #$FFFF^F_REMOVE
    sta ent_fl,x
:   rtl
@alive:
    inc ent_t,x
    inc ent_anim,x
    lda ent_v5,x
    beq :+
    dec ent_v5,x
:   lda ent_v4,x
    bne @act
    lda #190
    jsl boss_player_near
    bcs :+
    rtl
:   lda #1
    sta ent_v4,x
    lda #SONG_BOWSER
    jsl boss_activate
@act:
    jsl ent_face_player
    sta ent_facing,x
    lda ent_state,x
    beq @s0
    cmp #1
    jeq @s1
    jmp @s2

@s0: ; walk & breathe fire: XVel = |dx| > 40 ? Facing*8 : 0
    stz ent_xvel,x
    lda #41
    jsl boss_player_near
    bcs :+
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
:   dec ent_v1,x
    beq :+
    bpl @nob
:   lda #140
    sta ent_v1,x
    jsr breathe
@nob:
    ; if (--jumpT <= 0 && OnGround) { state = 1; YVel = -$70; jumpT = 220 }
    dec ent_v2,x
    beq :+
    bpl @mv0
:   lda ent_fl,x
    and #F_GROUND
    beq @mv0
    lda #1
    sta ent_state,x
    lda #$10000-$70
    sta ent_yvel,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
    lda #220
    sta ent_v2,x
@mv0:
    lda #0
    ldy #1
    jsl ent_move_walker
    jmp @end

@s1: ; leap toward the player, then pound
    ; XVel = sign(P.CenterX - Cx) * $14 (ENT_SIGN has macro labels that would end this @-label scope)
    jsl ent_player_dx
    beq :++
    bmi :+
    lda #$14
    bra :++
:   lda #$10000-$14
:   sta ent_xvel,x
    lda #8
    jsl boss_player_near
    bcc :+
    stz ent_xvel,x
:   lda ent_fl,x
    and #F_GROUND
    sta es1                     ; 0 = in the air before moving
    lda #0
    ldy #1
    jsl ent_move_walker
    ; if (YVel >= 0 && air && |dx| < 12) YVel = $40
    lda es1
    bne :+
    lda ent_yvel,x
    bmi :+
    lda #12
    jsl boss_player_near
    bcc :+
    lda #$40
    sta ent_yvel,x
:   ; landed: state 2, thud, shake 20, stun, break the floor
    lda es1
    jne @end
    lda ent_fl,x
    and #F_GROUND
    jeq @end
    lda #2
    sta ent_state,x
    stz ent_t,x
    ENT_SFX "THWOMP"
    lda #20
    sta w_shake
    lda p_inair
    bne :+
    lda #30
    sta p_stun
:   jsr break_floor
    bra @end

@s2: stz ent_xvel,x
    lda #0
    ldy #1
    jsl ent_move_walker
    ; fell through the broken floor: if (!OnGround && Py > CamY + 150) -> dying fall, killed
    lda ent_fl,x
    and #F_GROUND
    bne @t50
    jsl ent_py
    sec
    sbc cam_y
    sec
    sbc #151
    bmi @t50
    lda ent_fl,x
    ora #(F_DYING|F_KILLED)
    and #$FFFF^F_HURTS
    sta ent_fl,x
    lda #1
    sta ent_knock,x
    lda #$10
    sta ent_yvel,x
    stz ent_xvel,x
    jsl ent_kill
    ENT_SFX "BOSSHIT"
@t50:
    lda ent_t,x
    cmp #51
    bcc @end
    stz ent_state,x
@end:
    lda ent_v6,x
    beq :+
    dec ent_v6,x
:   rtl

; BowserFire(Facing > 0 ? Px + 28 : Px - 20, Py + 8, Facing, targetY = P.Py + (Big ? 8 : 20))
breathe:
    jsl ent_px
    ldy ent_facing,x
    bmi :+
    clc
    adc #28
    bra :++
:   sec
    sbc #20
:   sta ent_new_x
    jsl ent_py
    clc
    adc #8
    sta ent_new_y
    lda p_y
    ASR4
    ldy p_form
    bne :+
    clc
    adc #12                     ; small: +20 (= 8 + 12)
:   clc
    adc #8
    sta es0
    ldy #0
    lda #ET_BOSS_FIRE
    jsl ent_spawn
    bcc :+
    lda es0
    sta ent_v0,y                ; targetY
    lda ent_facing,x
    sta ent_facing,y
    asl a
    asl a
    asl a
    sta es1
    asl a
    clc
    adc es1
    sta ent_xvel,y              ; dir * $18
:   ENT_SFX "BOWSERFIRE"
    lda #20
    sta ent_v6,x
    rts

; BreakFloor: ty = (Py + Ht) / 16; for (x = Px + 2; x < Px + Wd - 2; x += 8) Brick -> BreakBrick
break_floor:
    jsl ent_bottom
    ASR4
    sta es1
    jsl ent_px
    clc
    adc #2
    sta es0
@l: jsl ent_px
    clc
    adc #30                     ; Px + Wd - 2
    cmp es0
    beq @d
    bmi @d
    lda es0
    ASR4
    sta e_tx
    lda es1
    sta e_ty
    phx
    jsl eng_tile_at             ; clobbers X
    plx
    cmp #T_BRICK
    bne :+
    lda es0
    ASR4
    sta e_tx
    lda es1
    sta e_ty
    lda es0                     ; bump callbacks / debris inits may use es*: keep ours
    pha
    lda es1
    pha
    phx
    jsl w_break_brick
    plx
    pla
    sta es1
    pla
    sta es0
:   lda es0
    clc
    adc #8
    sta es0
    bra @l
@d: rts

; TakeHit: fire / hammer: BossHit, 24th hit knocks him off; star and the rest: nothing
bw_hit:
    ENT_ENTER
    lda ent_dmg
    cmp #D_FIRE
    beq :+
    cmp #D_HAMMER
    bne @no
:   ENT_SFX "BOSSHIT"
    dec ent_v0,x
    beq @k
    bmi @k
    lda #10
    sta ent_v5,x
@no: clc
    rtl
@k: lda ent_dir
    jsl ent_knock_off
    stz ent_v7,x
    jsl ent_kill
    sec
    rtl

bw_draw:
    ENT_ENTER
    jsl ent_draw_face
    ; flash = hurtFlash > 0 && (hurtFlash & 2)
    lda ent_v5,x
    and #BOSS_FLASH_MASK
    sta es0
    lda ent_v7,x
    bne @fall
    lda ent_fl,x
    and #F_DYING
    beq :+
@fall:
    jsl ent_draw_knocked
    bra @jump
:   lda ent_v6,x
    beq @nb
    lda es0
    bne :+
    ENT_DRAW SPR_BOWSER_BREATH
    rtl
:   ENT_DRAW SPR_BOWSER_BREATH_FLASH
    rtl
@nb:
    lda ent_fl,x
    and #F_GROUND
    beq @jump
    ; walk if XVel != 0 && (Anim / 10) % 2 == 0
    lda ent_xvel,x
    beq @stand
    lda ent_anim,x
    ldy #10
    jsl ent_div
    and #1
    bne @stand
    lda es0
    bne :+
    ENT_DRAW SPR_BOWSER_WALK
    rtl
:   ENT_DRAW SPR_BOWSER_WALK_FLASH
    rtl
@stand:
    lda es0
    bne :+
    ENT_DRAW SPR_BOWSER_STAND
    rtl
:   ENT_DRAW SPR_BOWSER_STAND_FLASH
    rtl
@jump:
    lda es0
    bne :+
    ENT_DRAW SPR_BOWSER_JUMP
    rtl
:   ENT_DRAW SPR_BOWSER_JUMP_FLASH
    rtl

; ================================================================== BowserFire: 24x8, hitbox 2,1,20,6
; flies at dir*$18, drifts 8/16 px per tick toward targetY (v0), 400 ticks
bf_init:
    ENT_ENTER
    lda #24
    sta ent_wd,x
    lda #8
    sta ent_ht,x
    lda #2
    sta ent_hbx,x
    lda #1
    sta ent_hby,x
    lda #20
    sta ent_hbw,x
    lda #6
    sta ent_hbh,x
    lda #EC_EPROJ
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^(F_SLOT|F_STOMP)
    ora #F_STARIMM
    sta ent_fl,x
    sec
    rtl

bf_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    jsl ent_py
    sec
    sbc ent_v0,x
    beq @t
    bpl :+
    lda ent_y,x
    clc
    adc #8
    sta ent_y,x
    bra @t
:   lda ent_y,x
    sec
    sbc #8
    sta ent_y,x
@t: lda #400
    cmp ent_t,x
    bcs :+
    jsl ent_remove
:   rtl

bf_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_t,x
    and #4
    bne :+
    ENT_DRAW SPR_BOWSERFIRE_1
    rtl
:   ENT_DRAW SPR_BOWSERFIRE_2
    rtl
