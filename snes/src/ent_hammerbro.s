; Hammer Bro (port of C# HammerBro, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Paces +-16 px around its spawn point facing the player, throws a hammer (ET_ENEMY_HAMMER) every 50-109 ticks
; (C# hash of t), jumps high/low alternately every 150-239 ticks. 1000 points. Stomp = knocked off.
;@entity HAMMER_BRO codes=y
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE HAMMER_BRO, bro_init, bro_update, bro_draw, 0, 0, bro_touch

; ent_t = t ; v0 = homeX (px) ; v1 = throwT ; v2 = jumpT ; v3 = throwing
.a16
.i16

bro_init:
    ENT_ENTER
    jsl ent_px
    sta ent_v0,x                ; homeX = px
    lda ent_y,x
    sec
    sbc #8*16
    sta ent_y,x                 ; base(px, py - 8)
    lda #24
    sta ent_ht,x
    lda #6
    sta ent_hby,x
    lda #18
    sta ent_hbh,x               ; hitbox 2,6,12,18
    lda #$1000
    sta ent_points,x
    lda #$08
    sta ent_xvel,x
    lda #60
    sta ent_v1,x
    lda #180
    sta ent_v2,x
    sec
    rtl

bro_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    inc ent_anim,x
    jsl ent_face_player
    sta ent_facing,x
    ; pace: Px > homeX + 16 -> XVel = -8 ; Px < homeX - 16 -> XVel = 8
    jsl ent_px
    sta es0
    lda ent_v0,x
    clc
    adc #16
    sec
    sbc es0
    bpl :+
    lda #$10000-8
    sta ent_xvel,x
    bra @throw
:   lda ent_v0,x
    sec
    sbc #16
    sec
    sbc es0
    beq @throw
    bmi @throw
    lda #8
    sta ent_xvel,x
@throw:
    dec ent_v1,x
    beq :+
    bpl @thr2
:   lda #12
    sta ent_v3,x                ; throwing
    ; new EnemyHammer(Px + (Facing > 0 ? 8 : 0), Py, Facing)
    jsl ent_px
    ldy ent_facing,x
    bmi :+
    clc
    adc #8
:   sta ent_new_x
    jsl ent_py
    sta ent_new_y
.ifdef ET_ENEMY_HAMMER
    ldy ent_facing,x
    lda #ET_ENEMY_HAMMER
    jsl ent_spawn
.endif
    ; throwT = 50 + (uint)(t * 2246822519u) % 60
    jsr hash_mod60
    clc
    adc #50
    sta ent_v1,x
@thr2:
    lda ent_v3,x
    beq :+
    dec ent_v3,x
:   ; if (--jumpT <= 0 && OnGround) jump: (t / 180) % 2 == 0 ? -$50 : -$28 ; jumpT = 150 + t % 90
    dec ent_v2,x
    beq :+
    bpl @walk
:   lda ent_fl,x
    and #F_GROUND
    beq @walk
    lda ent_t,x
    ldy #180
    jsl ent_div
    and #1
    beq :+
    lda #$10000-$28
    bra :++
:   lda #$10000-$50
:   sta ent_yvel,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
    lda ent_t,x
    ldy #90
    jsl ent_mod
    clc
    adc #150
    sta ent_v2,x
@walk:
    lda #0                      ; MoveWalker(false, true)
    ldy #1
    jsl ent_move_walker
    rtl

; A = low32(t * $85EBCA77) mod 60 (t = ent_t, 16-bit)
HASH_LO = $CA77
HASH_HI = $85EB
hash_mod60:
    ; es1:es0 = t * HASH_LO (32-bit) ; es1 += low16(t * HASH_HI)
    lda ent_t,x
    sta es2
    stz es0
    stz es1
    lda #HASH_LO
    sta es3
    ldy #16
@m: asl es0                     ; es1:es0 <<= 1
    rol es1
    asl es3                     ; next multiplier bit (msb first)
    bcc :+
    lda es0
    clc
    adc es2
    sta es0
    bcc :+
    inc es1
:   dey
    bne @m
    ; low 16 bits of t * HASH_HI
    lda #HASH_HI
    sta es3
    lda #0
    ldy #16
@m2: asl a
    asl es3
    bcc :+
    clc
    adc es2
:   dey
    bne @m2
    clc
    adc es1
    sta es1
    ; 32-bit mod 60, a byte at a time: r = (r * 256 + byte) % 60
    lda es1
    xba
    and #$00FF
    ldy #60
    jsl ent_mod
    jsr @step
    lda es1
    and #$00FF
    jsr @add
    lda es0
    xba
    and #$00FF
    jsr @add
    lda es0
    and #$00FF
@add:
    sta es3
    tya                         ; r (previous remainder, in Y)
    xba
    and #$FF00
    clc
    adc es3
    ldy #60
    jsl ent_mod
@step:
    tay                         ; keep r in Y (ent_mod returns it in A)
    rts

bro_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda #1
    sta ent_knock,x
    lda ent_fl,x
    ora #(F_DYING|F_KILLED)
    and #$FFFF^F_HURTS
    sta ent_fl,x
    stz ent_yvel,x
    jsl ent_kill
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

bro_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_BRO_1
    rtl
:   jsl ent_draw_face
    lda ent_v3,x
    beq :+
    ENT_DRAW SPR_BRO_THROW
    bra @hammer
:   lda ent_anim,x
    ldy #10
    jsl ent_div
    and #1
    bne :+
    ENT_DRAW SPR_BRO_1
    bra @hammer
:   ENT_DRAW SPR_BRO_2
@hammer:
    ; the next hammer, held up: throwing == 0 && throwT < 20 at (+6 / -2, -10)
    lda ent_v3,x
    bne @r
    lda ent_v1,x
    cmp #20
    bpl @r
    lda #6
    ldy ent_facing,x
    bpl :+
    lda #$10000-2
:   ldy #$10000-10
    jsl ent_draw_offset
    ENT_DRAW SPR_HAMMER_1
@r: rtl
