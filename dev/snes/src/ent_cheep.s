; Cheep Cheep (port of C# Cheep, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; 'c' swims horizontally with a sine bob (turns at walls; not stompable). 'l' leaps out of the water from below the
; level bottom near the player, again and again (stompable when not swimming).
;@entity CHEEP codes=cl
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE CHEEP, cheep_init, cheep_update, cheep_draw, 0, 0, cheep_touch

; v0 = leaper, v1 = oy (px), v2 = wait, v3 = t mod 90 (swim phase)
.a16
.i16

; LevelPxH + 8
level_bottom8:
    lda area_h
    asl a
    asl a
    asl a
    asl a
    clc
    adc #8
    rtl

cheep_init:
    ENT_ENTER
    lda ent_arg,x
    cmp #'l'
    bne :+
    lda #1
    sta ent_v0,x
:   jsl ent_py
    sta ent_v1,x                ; oy
    ; Facing = P.CenterX < px ? -1 : 1
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
    ; XVel = Facing * (leaper ? $10 : $08)
    asl a
    asl a
    asl a
    ldy ent_v0,x
    beq :+
    asl a
:   sta ent_xvel,x
    lda #11
    sta ent_hbh,x               ; hitbox 2,3,12,11
    lda ent_v0,x
    bne @leaper
    lda ent_fl,x
    and #$FFFF^F_STOMP
    sta ent_fl,x                ; Stompable = leaper
    sec
    rtl
@leaper:
    jsl level_bottom8
    ENT_ASL4
    sta ent_y,x                 ; starts hidden below the level
    ; wait = 30 + (px * 7) % 90
    lda es0
    asl a
    asl a
    asl a
    sec
    sbc es0
    ldy #90
    jsl ent_mod
    clc
    adc #30
    sta ent_v2,x
    sec
    rtl

cheep_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    inc ent_anim,x
    lda ent_v0,x
    jeq @swim
    ; ---- leaper
    lda ent_v2,x
    beq :+
    dec ent_v2,x
    rtl
:   jsl level_bottom8
    sta es1                     ; LevelPxH + 8
    lda ent_yvel,x
    bmi @fly
    jsl ent_py
    sec
    sbc es1
    bmi @fly                    ; Py < bottom
    ; launch: Y = bottom, YVel = -$58, X = CamX + (P.CenterX - CamX < 128 ? 200 : 40), face the player
    lda es1
    ENT_ASL4
    sta ent_y,x
    lda #$10000-$58
    sta ent_yvel,x
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc #8
    sec
    sbc cam_x
    sec
    sbc #128
    bvc :+
    eor #$8000
:   bmi :+
    lda #40
    bra :++
:   lda #200
:   clc
    adc cam_x
    ENT_ASL4
    sta ent_x,x
    jsl ent_face_player
    sta ent_facing,x
    asl a
    asl a
    asl a
    asl a
    sta ent_xvel,x              ; Facing * $10
@fly:
    jsl ent_apply_vel
    lda ent_yvel,x
    clc
    adc #2
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    jsl ent_py
    sec
    sbc es1
    beq :+
    bmi :+
    lda #60
    sta ent_v2,x                ; below the bottom again: wait
:   rtl
@swim:
    ; X += XVel ; Y = (oy << 4) + (int)(sin(t * 2pi / 90) * 6 * 16)
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    lda ent_v3,x
    inc a
    cmp #90
    bcc :+
    lda #0
:   sta ent_v3,x
    asl a
    phx
    tax
    lda f:cheep_sin,x
    plx
    sta es0
    lda ent_v1,x
    ENT_ASL4
    clc
    adc es0
    sta ent_y,x
    ; turn at walls: XVel > 0 && SolidAt(Px + 15, Py + 8) || XVel < 0 && SolidAt(Px, Py + 8)
    lda ent_xvel,x
    beq @r
    jsl ent_py
    clc
    adc #8
    tay
    jsl ent_px
    pha
    lda ent_xvel,x
    bmi :+
    pla
    clc
    adc #15
    bra :++
:   pla
:   jsl ent_solid_at
    bcc @r
    lda ent_xvel,x
    NEG16
    sta ent_xvel,x
    lda ent_facing,x
    NEG16
    sta ent_facing,x
@r: rtl

cheep_touch:
    ENT_ENTER
    jsl ent_can_stomp           ; (checks Stompable and !p.Swimming)
    bcc @hurt
    jsl ent_stomp_bounce
    lda ent_facing,x
    jsl ent_knock_off
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

cheep_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_CHEEP_1
    rtl
:   jsl ent_draw_face
    lda ent_anim,x
    and #8
    bne :+
    ENT_DRAW SPR_CHEEP_1
    rtl
:   ENT_DRAW SPR_CHEEP_2
    rtl

; (int)(Math.Sin(t * 2 * Math.PI / 90.0) * 6 * 16), t = 0..89 (1/16 px)
cheep_sin:
    .word 0,6,13,19,26,32,39,45,50,56,61,66,71,75,79,83,86,89,91,93,94,95,95,95,95,94,93,91,89,86,83,79,75,71,66
    .word 61,56,50,45,39,32,26,19,13,6,0
    .word $10000-6,$10000-13,$10000-19,$10000-26,$10000-32,$10000-39,$10000-45,$10000-50,$10000-56,$10000-61
    .word $10000-66,$10000-71,$10000-75,$10000-79,$10000-83,$10000-86,$10000-89,$10000-91,$10000-93,$10000-94
    .word $10000-95,$10000-95,$10000-95,$10000-95,$10000-94,$10000-93,$10000-91,$10000-89,$10000-86,$10000-83
    .word $10000-79,$10000-75,$10000-71,$10000-66,$10000-61,$10000-56,$10000-50,$10000-45,$10000-39,$10000-32
    .word $10000-26,$10000-19,$10000-13,$10000-6
