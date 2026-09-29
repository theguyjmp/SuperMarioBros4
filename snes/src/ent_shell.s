; Shell (port of C# Shell): kickable, carryable, chain-scoring. Owner: engine agent.
; Spawned only: ent_spawn(ET_SHELL, arg = kind | flipped << 8) with kind 0 green, 1 red, 2 buzzy (fire-immune; wakes up
; as ET_BUZZY if that type exists). The creator hands its spawn index over (see ent_koopa.s to_shell).
;@entity SHELL
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE SHELL, shell_init, shell_update, shell_draw, shell_hit, 0, shell_touch

; v0 = kind, v1 = flipped, v2 = wakeT, v3 = kickGrace
.a16
.i16

shell_init:
    ENT_ENTER
    lda ent_arg,x
    and #$00FF
    sta ent_v0,x
    lda ent_arg,x
    xba
    and #$00FF
    sta ent_v1,x
    lda #(F_STOMP|F_SLOT|F_CARRY)   ; Hurts = false
    ldy ent_v0,x
    cpy #2
    bne :+
    ora #F_FIREIMM
:   sta ent_fl,x
    sec
    rtl

; Moving = XVel != 0 && P.Carrying != this -> Z clear if moving
moving:
    lda ent_xvel,x
    beq @no
    jsl ent_carried
    bcs @no
    lda #1
    rts
@no: lda #0
    rts

; Kick(dir = A, playerXVel = Y): XVel = dir*$30 + (sign(pxv) == dir ? pxv/2 : 0)
kick:
    sta es0
    sty es1
    asl a
    clc
    adc es0                     ; *3
    asl a
    asl a
    asl a
    asl a                       ; *$30
    sta es2
    lda es1
    ENT_SIGN
    cmp es0
    bne :+
    lda es1
    bpl @pos                    ; C# int division truncates toward zero
    NEG16
    lsr a
    NEG16
    bra :++
@pos: lsr a
    bra :++
:   lda #0
:   clc
    adc es2
    sta ent_xvel,x
    lda ent_fl,x
    ora #(F_SHELL|F_HURTS)
    sta ent_fl,x
    stz ent_chain,x
    lda #16
    sta ent_v3,x
    stz ent_v2,x
    rts

shell_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    lda ent_v3,x
    beq :+
    dec ent_v3,x
:   jsl ent_carried
    bcc @free
    ; carried: X = (p.Px + (facing > 0 ? 11 : -11)) << 4 ; Y = (p.Py + (Big && !Ducking ? 13 : 17)) << 4
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    ldy p_facing
    bmi :+
    clc
    adc #11
    bra :++
:   sec
    sbc #11
:   ENT_ASL4
    sta ent_x,x
    lda #17
    ldy p_form
    beq :+
    ldy p_ducking
    bne :+
    lda #13
:   sta es0
    lda p_y
    ASR4
    clc
    adc es0
    ENT_ASL4
    sta ent_y,x
    stz ent_xvel,x
    stz ent_yvel,x
    lda ent_v2,x
    inc a
    sta ent_v2,x
    cmp #421
    bcc :+
    lda #1
    jmp wake
:   rtl
@free:
    lda ent_xvel,x
    bne @mv
    lda ent_fl,x
    and #$FFFF^(F_SHELL|F_HURTS)
    sta ent_fl,x
    lda ent_v2,x
    inc a
    sta ent_v2,x
    cmp #421
    bcc @mv
    lda #0
    jmp wake
@mv:
    lda #0
    ldy #1
    jsl ent_move_walker         ; carry = hit wall
    bcs :+
    rtl
:   lda ent_xvel,x
    bne :+
    rtl
:   ENT_SFX "RICOCHET"
    ; the wall we hit is on the other side (XVel already reversed): bump the block there
    lda ent_xvel,x
    bmi :+
    jsl ent_px
    sec
    sbc #3
    bra :++
:   jsl ent_px
    clc
    adc ent_wd,x
    inc a
    inc a
:   ASR4
    sta e_tx
    jsl ent_py
    clc
    adc #8
    ASR4
    sta e_ty
    ; only near the screen: Px + 8 - CamX > -16 && Px - CamX < 272
    jsl ent_px
    clc
    adc #8
    sec
    sbc cam_x
    sec
    sbc #$10000-16
    bmi @r
    beq @r
    jsl ent_px
    sec
    sbc cam_x
    sec
    sbc #272
    bpl @r
    phx
    jsl eng_tile_at
    tax
    lda f:shell_props,x
    and #TP_BUMP
    beq :+
    lda #1
    ldx #0
    jsl w_hit_block
:   plx
@r: rtl
shell_props: .byte TILE_PROPS

; WakeUp(A = carried): back to a Koopa (or Buzzy) at the same spot
wake:
    sta es3
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda ent_v0,x
    cmp #2
    bne @koopa
.ifdef ET_BUZZY
    lda #ET_BUZZY
    ldy #0
    bra @sp
.else
    bra @gone
.endif
@koopa:
    tay                         ; arg = red flag (numeric)
    lda #ET_KOOPA
@sp: jsl ent_spawn
    bcc @gone
    lda ent_spawnidx,x
    sta ent_spawnidx,y
@gone:
    lda #$FFFF
    sta ent_spawnidx,x
    jsl ent_remove
    lda es3
    beq :+
    stz p_carrying
    jsl ent_hurt_player
:   rtl

shell_touch:
    ENT_ENTER
    jsl ent_carried
    bcc :+
    rtl
:   jsr moving
    bne @moving
    ; pick up with B held
    lda pad_held
    and #BTN_B
    beq @kick
    lda p_carrying
    ora p_statue
    ora p_swimming
    bne @kick
    txa
    inc a
    inc a
    sta p_carrying
    lda ent_v2,x
    cmp #300
    bcc :+
    lda #300
    sta ent_v2,x
:   rtl
@kick:
    jsl ent_can_stomp
    lda #0
    rol a
    sta es3                     ; fromAbove
    ; dir = P.CenterX < Cx ? 1 : -1
    jsl ent_face_player
    NEG16
    ldy #0
    jsr kick
    lda #$0C
    sta p_kickpose
    lda es3
    beq :+
    lda #$10000-$40
    jsl pl_bounce
:   ENT_SFX "KICK"
    lda #$0100
    jsl ent_score
    rtl
@moving:
    lda ent_v3,x
    beq :+
    rtl
:   jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    stz ent_xvel,x
    lda ent_fl,x
    and #$FFFF^(F_SHELL|F_HURTS)
    sta ent_fl,x
    stz ent_v2,x
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

shell_hit:
    ENT_ENTER
    lda ent_dmg
    cmp #D_KICK
    bne :+
    ; thrown by the player (World.KickCarried): Kick(p.Facing, p.XVel)
    lda p_facing
    ldy p_xvel
    jsr kick
    sec
    rtl
:   cmp #D_FIRE
    bne :+
    lda ent_fl,x
    and #F_FIREIMM
    beq :+
    clc
    rtl
:   lda ent_dmg
    cmp #D_TAIL
    beq @flip
    cmp #D_BUMP
    beq @flip
    lda ent_dir
    jsl ent_knock_off
    sec
    rtl
@flip:
    lda ent_v1,x
    eor #1
    sta ent_v1,x
    lda #$10000-$30
    sta ent_yvel,x
    stz ent_xvel,x
    lda ent_fl,x
    and #$FFFF^F_SHELL
    sta ent_fl,x
    stz ent_v2,x
    sec
    rtl

shell_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    lda #0
    bra @img
:   lda ent_v1,x
    beq :+
    lda #2
    sta spr_arg_flags
:   jsr moving
    beq @still
    lda ent_anim,x
    lsr a
    and #3
    bra @img
@still:
    ; shake before waking up
    lda ent_v2,x
    cmp #331
    bcc :+
    lda ent_anim,x
    lsr a
    and #1
    ldy #0
    jsl ent_draw_offset
:   lda #0
@img:
    ldy ent_v0,x
    beq @green
    cpy #1
    beq @red
    ; buzzy
    cmp #0
    bne :+
    ENT_DRAW SPR_BSHELL_1
    rtl
:   cmp #1
    bne :+
    ENT_DRAW SPR_BSHELL_2
    rtl
:   cmp #2
    bne :+
    ENT_DRAW SPR_BSHELL_3
    rtl
:   ENT_DRAW SPR_BSHELL_4
    rtl
@green:
    cmp #0
    bne :+
    ENT_DRAW SPR_SHELL_1
    rtl
:   cmp #1
    bne :+
    ENT_DRAW SPR_SHELL_2
    rtl
:   cmp #2
    bne :+
    ENT_DRAW SPR_SHELL_3
    rtl
:   ENT_DRAW SPR_SHELL_4
    rtl
@red:
    cmp #0
    bne :+
    ENT_DRAW SPR_SHELL_1_RED
    rtl
:   cmp #1
    bne :+
    ENT_DRAW SPR_SHELL_2_RED
    rtl
:   cmp #2
    bne :+
    ENT_DRAW SPR_SHELL_3_RED
    rtl
:   ENT_DRAW SPR_SHELL_4_RED
    rtl
