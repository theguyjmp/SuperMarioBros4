; Koopa Troopa / Paratroopa (port of C# Koopa). Owner: engine agent.
; Spawn chars: k green, r red (turns at ledges), j green paratroopa (bounces), f red paratroopa (flies up/down),
; n red paratroopa (flies left/right). ent_spawn(ET_KOOPA, arg = 0 green / 1 red) = a plain walker (shell wake-up).
;@entity KOOPA codes=krjfn
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.segment "CODE13"
ENT_VTABLE KOOPA, koopa_init, koopa_update, koopa_draw, koopa_hit, 0, koopa_touch

; v0 = red, v1 = wingMode (0 walk, 1 bounce, 2 fly vertical, 3 fly horizontal), v2 = ox, v3 = oy, ent_t = t
.a16
.i16

; spawn char -> red | wingMode<<8
koopa_kinds:
    .byte 'k', 0, 0
    .byte 'r', 1, 0
    .byte 'j', 0, 1
    .byte 'f', 1, 2
    .byte 'n', 1, 3
    .byte 0

koopa_init:
    ENT_ENTER
    lda ent_arg,x
    cmp #'a'
    bcs @char
    sta ent_v0,x                ; numeric arg: red flag, walker
    bra @set
@char:
    sta es0
    txy                         ; Y = slot (tables in this bank need long addressing: lda f:table,x)
    ldx #0
@k: lda f:koopa_kinds,x
    and #$00FF
    beq @nf
    cmp es0
    beq @found
    inx
    inx
    inx
    bra @k
@found:
    lda f:koopa_kinds+1,x
    and #$00FF
    sta ent_v0,y
    lda f:koopa_kinds+2,x
    and #$00FF
    sta ent_v1,y
@nf: tyx
@set:
    ; base(px, py - 8)
    lda ent_y,x
    sec
    sbc #8*16
    sta ent_y,x
    lda #24
    sta ent_ht,x
    lda #10
    sta ent_hby,x
    lda #14
    sta ent_hbh,x               ; hitbox 2,10,12,14
    jsl ent_face_player_set
    stz ent_xvel,x
    lda ent_v1,x
    cmp #2
    beq :+
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
:   lda ent_x,x
    sta ent_v2,x                ; ox
    lda ent_y,x
    sta ent_v3,x                ; oy
    sec
    rtl

koopa_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    inc ent_t,x
    lda ent_v1,x
    cmp #2
    bne @n2
    ; red paratroopa: Y = oy + sin(t*2pi/180)*40*16
    lda ent_t,x
    ldy #180
    jsl ent_mod
    asl a
    phx
    tax
    lda f:eng_tables,x
    plx
    clc
    adc ent_v3,x
    sta ent_y,x
    jsl ent_face_player
    sta ent_facing,x
    rtl
@n2: cmp #3
    bne @n3
    ; X = ox + sin(t*2pi/240)*48*16 ; Facing = cos > 0 ? 1 : -1
    lda ent_t,x
    ldy #240
    jsl ent_mod
    asl a
    phx
    tax
    lda f:eng_tables+840,x
    sta es0
    lda f:eng_tables+360,x
    plx
    clc
    adc ent_v2,x
    sta ent_x,x
    lda es0
    sta ent_facing,x
    rtl
@n3: cmp #1
    bne @walk
    lda ent_fl,x
    and #F_GROUND
    beq @walk
    lda #$10000-$30
    sta ent_yvel,x
    lda ent_fl,x
    and #$FFFF^F_GROUND
    sta ent_fl,x
@walk:
    ; MoveWalker(Red && wingMode == 0)
    lda #0
    ldy ent_v0,x
    beq :+
    ldy ent_v1,x
    bne :+
    lda #1
:   ldy #1
    jsl ent_move_walker
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

koopa_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcs :+
    jsl ent_hurt_player
    rtl
:   jsl ent_stomp_bounce
    lda ent_v1,x
    beq @shell
    stz ent_v1,x                ; loses its wings
    lda ent_facing,x
    asl a
    asl a
    asl a
    sta ent_xvel,x
    stz ent_yvel,x
    rtl
@shell:
    ; new Shell(Px, Py + 8, kind, false) inherits the spawn index
    lda #0
    jsr to_shell
    jsl ent_remove
    rtl

; A = flipped -> spawns the shell for this koopa (Y = shell slot*2 or $FFFF). Keeps X.
to_shell:
    xba
    ora ent_v0,x                ; arg = kind (0 green / 1 red) | flipped << 8
    pha
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    clc
    adc #8
    sta ent_new_y
    ply
    lda #ET_SHELL
    jsl ent_spawn
    bcc @fail
    lda ent_spawnidx,x          ; hand the spawn point over to the shell
    sta ent_spawnidx,y
    lda #$FFFF
    sta ent_spawnidx,x
    rts
@fail: ldy #$FFFF
    rts

koopa_hit:
    ENT_ENTER
    lda ent_dmg
    cmp #D_TAIL
    beq @shell
    cmp #D_BUMP
    beq @shell
    lda ent_dir
    jsl ent_knock_off
    sec
    rtl
@shell:
    lda #1
    jsr to_shell
    cpy #$FFFF
    beq :+
    lda #$10000-$30
    sta ent_yvel,y
    lda ent_dir
    asl a
    asl a
    asl a
    sta ent_xvel,y
:   jsl ent_remove
    lda #$0100
    jsl ent_score
    sec
    rtl

koopa_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq @alive
    jsl ent_draw_knocked
    lda #0
    ldy #8
    jsl ent_draw_offset
    lda ent_v0,x
    bne :+
    ENT_DRAW SPR_SHELL_1
    rtl
:   ENT_DRAW SPR_SHELL_1_RED
    rtl
@alive:
    jsl ent_draw_face
    lda ent_anim,x
    and #8
    bne @f2
    lda ent_v0,x
    bne :+
    ENT_DRAW SPR_KOOPA_1
    bra @wing
:   ENT_DRAW SPR_KOOPA_1_RED
    bra @wing
@f2: lda ent_v0,x
    bne :+
    ENT_DRAW SPR_KOOPA_2
    bra @wing
:   ENT_DRAW SPR_KOOPA_2_RED
@wing:
    lda ent_v1,x
    beq @r
    lda #$10000-2               ; Facing > 0 ? sx - 2 : sx + 10
    ldy ent_facing,x
    bpl :+
    lda #10
:   ldy #4
    jsl ent_draw_offset
    lda ent_anim,x
    and #4
    bne :+
    ENT_DRAW SPR_WING_1
    rtl
:   ENT_DRAW SPR_WING_2
@r: rtl
