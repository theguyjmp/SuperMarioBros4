; Items (port of C# Items.cs): Mushroom / 1-UP, Fire Flower, Leaf, Star, P-switch, vine sprout, goal roulette box.
; Owner: engine agent. Block contents are released by the engine (ent_release in eng_obj.s) through ent_spawn.
;@entity MUSHROOM codes=m
;@entity FLOWER
;@entity LEAF
;@entity STAR
;@entity PSWITCH codes=P
;@entity VINE_SPROUT
;@entity GOAL_BOX
;@entity TREASURE_CHEST
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE MUSHROOM, mushroom_init, mushroom_update, mushroom_draw, ent_cb_none, mushroom_bump, mushroom_touch
ENT_VTABLE FLOWER, rising_init, flower_update, flower_draw, ent_cb_none, ent_cb_none, flower_touch
ENT_VTABLE LEAF, leaf_init, leaf_update, leaf_draw, ent_cb_none, ent_cb_none, leaf_touch
ENT_VTABLE STAR, star_init, star_update, star_draw, ent_cb_none, star_bump, star_touch
ENT_VTABLE PSWITCH, pswitch_init, pswitch_update, pswitch_draw, ent_cb_none, ent_cb_none, pswitch_touch
ENT_VTABLE VINE_SPROUT, vine_init, vine_update, vine_draw, ent_cb_none, ent_cb_none, ent_cb_none
ENT_VTABLE GOAL_BOX, goal_init, goal_update, goal_draw, ent_cb_none, ent_cb_none, goal_touch
ENT_VTABLE TREASURE_CHEST, chest_init, chest_update, chest_draw, ent_cb_none, ent_cb_none, chest_touch

.a16
.i16

; ------------------------------------------------------------------ Rising (C# base class): v1 = rise (px left)
rising_init:
    ENT_ENTER
    jsr rising_setup
    sec
    rtl
rising_setup:
    lda #EC_ITEM
    sta ent_class,x
    lda #16
    sta ent_v1,x
    lda #(F_BEHIND|F_STOMP)     ; Hurts = false, UsesSlot = false
    sta ent_fl,x
    rts

; Emerging(): carry set while still rising
emerging:
    lda ent_v1,x
    bne :+
    lda ent_fl,x
    and #$FFFF^F_BEHIND
    sta ent_fl,x
    clc
    rts
:   lda w_frame
    and #1
    bne :+
    lda ent_y,x
    sec
    sbc #16
    sta ent_y,x
    dec ent_v1,x
:   sec
    rts

; touch helper: rise > 8 -> carry set (not collectable yet)
too_deep:
    lda ent_v1,x
    cmp #9
    rts

; ------------------------------------------------------------------ Mushroom: v0 = oneUp. arg: 0 / 1 (1-UP) / 'm' (level)
mushroom_init:
    ENT_ENTER
    jsr rising_setup
    lda ent_arg,x
    cmp #'m'
    bne :+
    lda ent_y,x                 ; level object: new Mushroom(px, py + 16)
    clc
    adc #16*16
    sta ent_y,x
    lda #0
:   sta ent_v0,x
    lda #1
    sta ent_facing,x
    sec
    rtl

mushroom_update:
    ENT_ENTER
    jsr emerging
    bcc :+
    rtl
:   lda ent_xvel,x
    bne :+
    lda ent_facing,x
    ENT_ASL4
    sta ent_xvel,x
:   lda #0
    ldy #1
    jsl ent_move_walker
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

mushroom_touch:
    ENT_ENTER
    jsr too_deep
    bcc :+
    rtl
:   jsl ent_remove
    lda ent_v0,x
    beq @big
    phx
    jsl ent_py
    tay
    jsl ent_px
    tax
    jsl w_one_up_at
    plx
    rtl
@big: phx
    lda #PF_BIG
    jsl pl_powerup
    plx
    rtl

mushroom_bump:
    ENT_ENTER
    lda #$10000-$30
    sta ent_yvel,x
    lda ent_dir
    sta ent_facing,x
    ENT_ASL4
    sta ent_xvel,x
    rtl

mushroom_draw:
    ENT_ENTER
    lda ent_v0,x
    bne :+
    ENT_DRAW SPR_MUSHROOM
    rtl
:   ENT_DRAW SPR_MUSHROOM_ONEUP
    rtl

; ------------------------------------------------------------------ Fire flower
flower_update:
    ENT_ENTER
    jsr emerging
    rtl
flower_touch:
    ENT_ENTER
    jsr too_deep
    bcc :+
    rtl
:   jsl ent_remove
    phx
    lda #PF_FIRE
    jsl pl_powerup
    plx
    rtl
flower_draw:
    ENT_ENTER
    lda w_frame
    and #4
    beq :+
    ENT_DRAW SPR_FLOWER_1_FLASH
    rtl
:   ENT_DRAW SPR_FLOWER_1
    rtl

; ------------------------------------------------------------------ Star
star_init:
    ENT_ENTER
    jsr rising_setup
    lda #1
    sta ent_facing,x
    sec
    rtl
star_update:
    ENT_ENTER
    jsr emerging
    bcc :+
    rtl
:   lda ent_facing,x
    bmi :+
    lda #$18
    bra :++
:   lda #$10000-$18
:   sta ent_xvel,x
    clc
    adc ent_x,x
    sta ent_x,x
    ; turn at walls
    jsl ent_py
    clc
    adc #8
    tay
    lda ent_xvel,x
    bmi :+
    jsl ent_px
    clc
    adc #14
    bra :++
:   jsl ent_px
    inc a
:   jsl ent_solid_at
    bcc :+
    lda ent_facing,x
    NEG16
    sta ent_facing,x
:   lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    clc
    adc ent_y,x
    sta ent_y,x
    lda ent_yvel,x
    bmi @up
    beq @up
    jsl ent_py
    clc
    adc #16
    sta es0
    tay
    jsl ent_px
    clc
    adc #4
    jsl ent_floor_at
    bcs @bounce
    ldy es0
    jsl ent_px
    clc
    adc #11
    jsl ent_floor_at
    bcc @up
@bounce:
    lda es0
    and #$FFF0
    sec
    sbc #16
    ENT_ASL4
    sta ent_y,x
    lda #$10000-$48
    sta ent_yvel,x
@up:
    lda ent_yvel,x
    bpl @fall
    jsl ent_py
    tay
    jsl ent_px
    clc
    adc #8
    jsl ent_solid_at
    bcc @fall
    stz ent_yvel,x
@fall:
    lda area_h
    ENT_ASL4
    clc
    adc #16
    sta es0
    jsl ent_py
    sec
    sbc es0
    bmi :+
    beq :+
    jsl ent_remove
:   rtl
star_touch:
    ENT_ENTER
    jsr too_deep
    bcc :+
    rtl
:   jsl ent_remove
    phx
    jsl pl_get_star
    plx
    rtl
star_bump:
    ENT_ENTER
    lda #$10000-$40
    sta ent_yvel,x
    lda ent_dir
    sta ent_facing,x
    rtl
star_draw:
    ENT_ENTER
    lda w_frame
    lsr a
    and #3
    beq @s1
    cmp #1
    beq @s2
    cmp #2
    beq @s3
    ENT_DRAW SPR_STAR_4
    rtl
@s1: ENT_DRAW SPR_STAR
    rtl
@s2: ENT_DRAW SPR_STAR_2
    rtl
@s3: ENT_DRAW SPR_STAR_3
    rtl

; ------------------------------------------------------------------ Leaf: v0 = swayDir (base(px, py - 8))
leaf_init:
    ENT_ENTER
    lda #EC_ITEM
    sta ent_class,x
    lda ent_y,x
    sec
    sbc #8*16
    sta ent_y,x
    lda #0
    sta ent_fl,x
    lda #$10000-$40
    sta ent_yvel,x
    lda #1
    sta ent_v0,x
    sec
    rtl
leaf_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_yvel,x
    bpl @sway
    clc
    adc ent_y,x
    sta ent_y,x
    lda ent_yvel,x
    clc
    adc #3
    sta ent_yvel,x
    rtl
@sway:
    ; phase = t % 64; phase 0 flips; XVel = swayDir * (phase < 32 ? $14 : 8); Y += phase < 32 ? 6 : 10
    lda ent_t,x
    and #63
    sta es0
    bne :+
    lda ent_v0,x
    NEG16
    sta ent_v0,x
:   lda #$14
    ldy es0
    cpy #32
    bcc :+
    lda #$08
:   ldy ent_v0,x
    bpl :+
    NEG16
:   sta ent_xvel,x
    clc
    adc ent_x,x
    sta ent_x,x
    lda #6
    ldy es0
    cpy #32
    bcc :+
    lda #10
:   clc
    adc ent_y,x
    sta ent_y,x
    lda ent_v0,x
    sta ent_facing,x
    jsl ent_py
    sec
    sbc cam_y
    sec
    sbc #241
    bmi :+
    jsl ent_remove
:   rtl
leaf_touch:
    ENT_ENTER
    jsl ent_remove
    phx
    lda #PF_RACCOON
    jsl pl_powerup
    plx
    rtl
leaf_draw:
    ENT_ENTER
    lda ent_v0,x
    bpl :+
    lda #1
    sta spr_arg_flags
:   ENT_DRAW SPR_LEAF
    rtl

; ------------------------------------------------------------------ P-switch: v0 = pressed
pswitch_init:
    ENT_ENTER
    lda #EC_ITEM
    sta ent_class,x
    lda #4
    sta ent_hby,x
    lda #12
    sta ent_hbh,x
    lda #0
    sta ent_fl,x
    sec
    rtl
pswitch_update:
    ENT_ENTER
    lda ent_v0,x
    beq :+
    inc ent_t,x
    lda ent_t,x
    cmp #91
    bcc :+
    jsl ent_remove
:   rtl
pswitch_touch:
    ENT_ENTER
    lda ent_v0,x
    beq :+
    rtl
:   ; pressed from above while falling: p.YVel >= 0 && p.InAir && p.FeetY <= Py + 10
    lda p_yvel
    bmi @side
    lda p_inair
    beq @side
    jsl ent_py
    clc
    adc #10
    sta es0
    lda p_y
    ASR4
    clc
    adc #32
    sec
    sbc es0
    beq :+
    bpl @side
:   lda #1
    sta ent_v0,x
    lda #$10000-$20
    jsl pl_bounce
    phx
    jsl w_start_pswitch
    plx
    rtl
@side:
    ; push the player out horizontally (acts like a small solid)
    jsl ent_player_dx
    bpl @right
    lda p_xvel
    bmi :+
    beq :+
    stz p_xvel
:   rtl
@right:
    lda p_xvel
    bpl :+
    stz p_xvel
:   rtl
pswitch_draw:
    ENT_ENTER
    lda ent_v0,x
    bne :+
    ENT_DRAW SPR_PSWITCH
    rtl
:   ENT_DRAW SPR_PSWITCH_FLAT
    rtl

; ------------------------------------------------------------------ VineSprout(tx, ty): spawned at (tx*16, ty*16)
; v0 = tx, v1 = ty (row being grown)
vine_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda #0
    sta ent_fl,x
    jsl ent_px
    ASR4
    sta ent_v0,x
    jsl ent_py
    ASR4
    dec a
    sta ent_v1,x
    lda ent_y,x
    sec
    sbc #16*16
    sta ent_y,x
    sec
    rtl
vine_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_t,x
    and #7
    beq :+
    rtl
:   lda ent_v0,x
    sta e_tx
    lda ent_v1,x
    sta e_ty
    bmi @rem
    phx
    jsl eng_tile_at
    plx
    cmp #T_EMPTY
    beq :+
    cmp #T_VINE
    bne @rem
:   phx
    lda #T_VINE
    jsl eng_set_tile
    plx
    dec ent_v1,x
    lda ent_v1,x
    ENT_ASL4
    ENT_ASL4
    sta ent_y,x
    lda ent_v1,x
    cmp #$FFFF
    bpl :+
@rem: jsl ent_remove
:   rtl
vine_draw:
    ENT_ENTER
    ; tx*16 - camX, (ty+1)*16 - camY - (t % 8) * 2
    lda ent_v1,x
    inc a
    ENT_ASL4
    sec
    sbc cam_y
    sta spr_arg_y
    lda ent_t,x
    and #7
    asl a
    sta es0
    lda spr_arg_y
    sec
    sbc es0
    sta spr_arg_y
    ENT_DRAW SPR_VINE_SPROUT
    rtl

; ------------------------------------------------------------------ GoalBox: v0 = card, v1 = taken
goal_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda #32
    sta ent_wd,x
    sta ent_ht,x
    lda #4
    sta ent_hbx,x
    sta ent_hby,x
    lda #24
    sta ent_hbw,x
    sta ent_hbh,x
    lda #0
    sta ent_fl,x
    sec
    rtl
goal_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_v1,x
    bne @r
    lda ent_t,x
    ldy #7
    jsl ent_mod
    cmp #0
    bne @r
    lda ent_v0,x
    inc a
    cmp #3
    bcc :+
    lda #0
:   sta ent_v0,x
@r: rtl
goal_touch:
    ENT_ENTER
    lda ent_v1,x
    bne @r
    lda #1
    sta ent_v1,x
    jsl ent_px
    clc
    adc #8
    sta ent_new_x
    jsl ent_py
    clc
    adc #8
    sta ent_new_y
    lda #ET_FX_CARDFLY
    ldy ent_v0,x
    jsl ent_spawn
    ENT_SFX "CARDSTOP"
    lda ent_v0,x
    phx
    jsl w_start_clear
    plx
@r: rtl
goal_draw:
    ENT_ENTER
    ENT_DRAW SPR_GOAL_BOX
    lda ent_v1,x
    bne @r
    lda #8
    tay
    jsl ent_draw_offset
    lda ent_v0,x
    cmp #1
    beq :+
    cmp #2
    beq :++
    ENT_DRAW SPR_CARD_MUSHROOM
    rtl
:   ENT_DRAW SPR_CARD_FLOWER
    rtl
:   ENT_DRAW SPR_CARD_STAR
@r: rtl

; ------------------------------------------------------------------ TreasureChest (Hammer Bro battle reward)
chest_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda #0
    sta ent_fl,x
    sec
    rtl
chest_update:
    ENT_ENTER
    ; YVel += 3 (max $40); Y += YVel; lands on floor at (Px+4 | Px+11, Py+16)
    lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    clc
    adc ent_y,x
    sta ent_y,x
    lda ent_yvel,x
    bmi @r
    beq @r
    jsl ent_py
    clc
    adc #16
    sta es0
    tay
    jsl ent_px
    clc
    adc #4
    jsl ent_floor_at
    bcs @land
    ldy es0
    jsl ent_px
    clc
    adc #11
    jsl ent_floor_at
    bcc @r
@land:
    lda es0
    and #$FFF0
    sec
    sbc #16
    ENT_ASL4
    sta ent_y,x
    stz ent_yvel,x
@r: rtl
chest_touch:
    ENT_ENTER
    jsl ent_remove
    ENT_SFX "CHEST"
    lda #$FFFF                  ; no card; a battle clear has no auto-walk (lvl_kind = battle)
    phx
    jsl w_start_clear
    plx
    rtl
chest_draw:
    ENT_ENTER
    ENT_DRAW SPR_CHEST
    rtl
