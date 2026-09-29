; Lakitu (port of C# Lakitu, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Hovers around the player (sine offset), keeps 24 px below the camera top, throws a Spiny egg every 150 ticks
; while fewer than 5 enemies are active. Stompable; 800 points.
;@entity LAKITU codes=i
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE LAKITU, lakitu_init, lakitu_update, lakitu_draw, 0, 0, lakitu_touch

; ent_t = t ; v0 = t mod 200 (sine phase) ; v1 = t mod 150 (throw timer)
.a16
.i16

lakitu_init:
    ENT_ENTER
    lda ent_y,x
    sec
    sbc #8*16
    sta ent_y,x                 ; base(px, py - 8)
    lda #24
    sta ent_ht,x
    lda #2
    sta ent_hby,x
    lda #20
    sta ent_hbh,x               ; hitbox 2,2,12,20
    lda #$0800
    sta ent_points,x
    sec
    rtl

lakitu_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_t,x
    ; target = P.CenterX + (int)(sin(t * 2pi / 200) * 60)
    lda ent_v0,x
    inc a
    cmp #200
    bcc :+
    lda #0
:   sta ent_v0,x
    phx
    tax
    lda f:lakitu_sin,x
    plx
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   sta es0
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    clc
    adc #8
    clc
    adc es0
    sta es0                     ; target
    ; tv = target > Cx ? $18 : -$18 ; XVel steps 1 toward tv
    jsl ent_cx
    sec
    sbc es0
    bmi :+                      ; Cx < target
    lda #$10000-$18
    bra :++
:   lda #$18
:   sta es0
    lda ent_xvel,x
    sec
    sbc es0
    beq @mv
    bpl :+
    inc ent_xvel,x
    bra @mv
:   dec ent_xvel,x
@mv:
    lda ent_x,x
    clc
    adc ent_xvel,x
    sta ent_x,x
    jsl ent_face_player         ; Facing = P.CenterX < Cx ? -1 : 1
    sta ent_facing,x
    ; Y += sign(CamY + 24 - Py) * 8
    jsl ent_py
    sta es0
    lda cam_y
    clc
    adc #24
    sec
    sbc es0
    beq @thr
    bmi :+
    lda ent_y,x
    clc
    adc #8
    sta ent_y,x
    bra @thr
:   lda ent_y,x
    sec
    sbc #8
    sta ent_y,x
@thr:
    ; if (t % 150 == 0 && CountClass(Enemy) < 5) throw a Spiny egg (YVel -$30)
    lda ent_v1,x
    inc a
    cmp #150
    bcc :+
    lda #0
:   sta ent_v1,x
    bne @r
    lda #EC_ENEMY
    jsl ent_count_class
    cmp #5
    bcs @r
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #ET_SPINY
    ldy #1                      ; egg
    jsl ent_spawn
    bcc @r
    lda #$10000-$30
    sta ent_yvel,y
    lda #0
    sta ent_xvel,y
@r: rtl

lakitu_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda ent_facing,x
    jsl ent_knock_off
    jsl ent_kill
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

lakitu_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_LAKITU
    rtl
:   jsl ent_draw_face
    ENT_DRAW SPR_LAKITU
    rtl

; (int)(Math.Sin(t * 2 * Math.PI / 200.0) * 60), t = 0..199 (signed bytes)
lakitu_sin:
    .byte 0,1,3,5,7,9,11,13,14,16,18,20,22,23,25,27,28,30,32,33,35,36,38,39,41,42,43,45,46,47,48,49,50,51,52,53
    .byte 54,55,55,56,57,57,58,58,58,59,59,59,59,59,60,59,59,59,59,59,58,58,58,57,57,56,55,55,54,53,52,51,50,49
    .byte 48,47,46,45,43,42,41,39,38,36,35,33,32,30,28,27,25,23,22,20,18,16,14,13,11,9,7,5,3,1,0
    .byte <-1,<-3,<-5,<-7,<-9,<-11,<-13,<-14,<-16,<-18,<-20,<-22,<-23,<-25,<-27,<-28,<-30,<-32,<-33,<-35,<-36
    .byte <-38,<-39,<-41,<-42,<-43,<-45,<-46,<-47,<-48,<-49,<-50,<-51,<-52,<-53,<-54,<-55,<-55,<-56,<-57,<-57
    .byte <-58,<-58,<-58,<-59,<-59,<-59,<-59,<-59,<-60,<-59,<-59,<-59,<-59,<-59,<-58,<-58,<-58,<-57,<-57,<-56
    .byte <-55,<-55,<-54,<-53,<-52,<-51,<-50,<-49,<-48,<-47,<-46,<-45,<-43,<-42,<-41,<-39,<-38,<-36,<-35,<-33
    .byte <-32,<-30,<-28,<-27,<-25,<-23,<-22,<-20,<-18,<-16,<-14,<-13,<-11,<-9,<-7,<-5,<-3,<-1
