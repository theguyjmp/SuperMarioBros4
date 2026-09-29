; Player projectiles (port of C# Fireball / PlayerHammer). Owner: engine agent.
; Class EC_PROJ: the engine calls the touch callback with ent_other = an enemy the projectile overlaps
; (C# HitEnemy); carry set = stop checking other enemies this tick. arg = direction (-1/+1).
;@entity PL_FIREBALL
;@entity PL_HAMMER
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE PL_FIREBALL, fb_init, fb_update, fb_draw, ent_cb_none, ent_cb_none, fb_hit_enemy
ENT_VTABLE PL_HAMMER, hm_init, hm_update, hm_draw, ent_cb_none, ent_cb_none, hm_hit_enemy

.a16
.i16

proj_setup:
    lda #EC_PROJ
    sta ent_class,x
    lda #0
    sta ent_fl,x
    lda ent_arg,x
    sta ent_facing,x
    rts

; ------------------------------------------------------------------ Fireball
fb_init:
    ENT_ENTER
    jsr proj_setup
    lda #8
    sta ent_wd,x
    sta ent_ht,x
    sta ent_hbw,x
    sta ent_hbh,x
    stz ent_hbx,x
    stz ent_hby,x
    lda ent_facing,x
    bmi :+
    lda #$30
    bra :++
:   lda #$10000-$30
:   sta ent_xvel,x
    lda #$30
    sta ent_yvel,x
    sec
    rtl

fb_update:
    ENT_ENTER
    inc ent_t,x
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    ; SolidAt(Px + (XVel > 0 ? 7 : 0), Py + 4) -> poof
    jsl ent_py
    clc
    adc #4
    tay
    jsl ent_px
    ldy ent_xvel,x
    bmi :+
    beq :+
    clc
    adc #7
:   pha
    jsl ent_py
    clc
    adc #4
    tay
    pla
    jsl ent_solid_at
    bcc :+
    jmp poof
:   lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    lda ent_t,x
    and #3
    bne :+
    lda ent_yvel,x
    cmp #$40
    bpl :+
    clc
    adc #16
    sta ent_yvel,x
:   lda ent_yvel,x
    bmi @nob
    beq @nob
    ; bounce: FloorAt(Px + 4, Py + 8) && ((Py + 8) & 15) < 6
    jsl ent_py
    clc
    adc #8
    sta es0
    tay
    jsl ent_px
    clc
    adc #4
    jsl ent_floor_at
    bcc @nob
    lda es0
    and #15
    cmp #6
    bcs @nob
    lda es0
    and #$FFF0
    sec
    sbc #8
    ENT_ASL4
    sta ent_y,x
    lda #$10000-$30
    sta ent_yvel,x
@nob:
    jsl ent_py
    clc
    adc #4
    tay
    jsl ent_px
    clc
    adc #4
    jsl ent_props_at
    and #TP_LAVA
    beq :+
    jmp poof
:   rtl

poof:
    jsl ent_remove
    jsl ent_px
    sec
    sbc #4
    sta ent_new_x
    jsl ent_py
    sec
    sbc #4
    sta ent_new_y
    phx
    lda #ET_FX_PUFF
    ldy #1                      ; small puff (sparkle)
    jsl ent_spawn
    plx
    ENT_SFX "FIREBALLHIT"
    rtl

; HitEnemy(e = ent_other): always stops (carry set)
fb_hit_enemy:
    ENT_ENTER
    ldy ent_other
    lda ent_fl,y
    and #F_FIREIMM
    bne @poof
    lda #D_FIRE
    sta ent_dmg
    lda ent_facing,x
    sta ent_dir
    phx
    ldx ent_other
    jsl ent_hit                 ; TakeHit through the other entity's vtable
    bcc :+
    jsl ent_kill
    ENT_SFX "KICK"
:   plx
@poof:
    jsl poof
    sec
    rtl

fb_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_t,x
    lsr a
    and #3
    beq @f1
    cmp #1
    beq @f2
    cmp #2
    beq @f3
    ENT_DRAW SPR_FIREBALL_4
    rtl
@f1: ENT_DRAW SPR_FIREBALL_1
    rtl
@f2: ENT_DRAW SPR_FIREBALL_2
    rtl
@f3: ENT_DRAW SPR_FIREBALL_3
    rtl

; ------------------------------------------------------------------ Hammer (Hammer suit): spawned at (Px + (Facing > 0 ? 6 : 2), Py + 6)
; XVel = dir*$10 + (sign(p.XVel) == dir ? p.XVel : 0) ; YVel = -$30
hm_init:
    ENT_ENTER
    jsr proj_setup
    lda #2
    sta ent_hbx,x
    sta ent_hby,x
    lda #12
    sta ent_hbw,x
    sta ent_hbh,x
    lda ent_facing,x
    ENT_ASL4
    sta es0
    lda p_xvel
    ENT_SIGN
    cmp ent_facing,x
    bne :+
    lda p_xvel
    clc
    adc es0
    sta es0
:   lda es0
    sta ent_xvel,x
    lda #$10000-$30
    sta ent_yvel,x
    sec
    rtl

hm_update:
    ENT_ENTER
    inc ent_t,x
    jsl ent_apply_vel
    lda ent_t,x
    and #7
    bne :+
    lda ent_yvel,x
    clc
    adc #16
    sta ent_yvel,x
:   lda cam_y
    clc
    adc #240
    sta es0
    jsl ent_py
    sec
    sbc es0
    bmi :+
    beq :+
    jsl ent_remove
:   rtl

; HitEnemy: never stops (flies through everything)
hm_hit_enemy:
    ENT_ENTER
    ldy ent_other
    lda ent_fl,y
    and #F_HAMMERIMM
    bne @r
    lda #D_HAMMER
    sta ent_dmg
    lda ent_facing,x
    sta ent_dir
    phx
    ldx ent_other
    jsl ent_hit
    bcc :+
    jsl ent_kill
    ENT_SFX "KICK"
:   plx
@r: clc
    rtl

hm_draw:
    ENT_ENTER
    jsl ent_draw_face
    lda ent_t,x
    ldy #3
    jsl ent_div
    and #3
    beq @1
    cmp #1
    beq @2
    cmp #2
    beq @3
    ENT_DRAW SPR_HAMMER_4
    rtl
@1: ENT_DRAW SPR_HAMMER_1
    rtl
@2: ENT_DRAW SPR_HAMMER_2
    rtl
@3: ENT_DRAW SPR_HAMMER_3
    rtl
