; Buzzy Beetle (port of C# Buzzy, src/Game/Entities/Enemies.cs). Owner: enemies-A agent.
; Stomp -> Buzzy shell (ET_SHELL kind 2); tail/bump -> flipped Buzzy shell; fireproof.
;@entity BUZZY codes=z
.p816
.smart
.include "snes.inc"
.include "ent.inc"

.segment "CODE8"
ENT_VTABLE BUZZY, buzzy_init, buzzy_update, buzzy_draw, buzzy_hit, 0, buzzy_touch

SHELL_BUZZY = 2                 ; ET_SHELL spawn arg: kind (0 green, 1 red, 2 buzzy) | flipped << 8

.a16
.i16

buzzy_init:
    ENT_ENTER
    lda ent_fl,x
    ora #F_FIREIMM
    sta ent_fl,x
    lda #4
    sta ent_hby,x
    lda #12
    sta ent_hbh,x               ; hitbox 2,4,12,12
    jsl ent_face_player_set     ; Facing = P.CenterX < px + 8 ? -1 : 1
    asl a
    asl a
    asl a
    sta ent_xvel,x              ; Facing * 8
    sec
    rtl

buzzy_update:
    ENT_ENTER
    jsl ent_update_knocked
    bcc :+
    rtl
:   inc ent_anim,x
    lda #0                      ; MoveWalker(false)
    ldy #1
    jsl ent_move_walker
    lda ent_xvel,x
    beq :+
    ENT_SIGN
    sta ent_facing,x
:   rtl

; A = arg (kind | flipped << 8) -> spawns the shell at (Px, Py), hands the spawn point over, removes the Buzzy.
; Y = shell slot*2 or $FFFF.
to_shell:
    pha
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    ply
.ifdef ET_SHELL
    lda #ET_SHELL
    jsl ent_spawn
.else
    clc                         ; (engine shell not built yet)
.endif
    bcc @fail
    lda ent_spawnidx,x
    sta ent_spawnidx,y
    lda #$FFFF
    sta ent_spawnidx,x
    jsl ent_remove
    rts
@fail: jsl ent_remove
    ldy #$FFFF
    rts

buzzy_touch:
    ENT_ENTER
    jsl ent_can_stomp
    bcc @hurt
    jsl ent_stomp_bounce
    lda #SHELL_BUZZY
    jsr to_shell
    rtl
@hurt:
    jsl ent_hurt_player
    rtl

; TakeHit: fire -> no; tail/bump -> flipped shell popping up; anything else -> KnockOff
buzzy_hit:
    ENT_ENTER
    lda ent_dmg
    cmp #D_FIRE
    beq @no
    cmp #D_KICK
    beq @no
    cmp #D_TAIL
    beq @shell
    cmp #D_BUMP
    beq @shell
    lda ent_dir
    jsl ent_knock_off
    sec
    rtl
@shell:
    lda #SHELL_BUZZY|$100
    jsr to_shell
    cpy #$FFFF
    beq :+
    lda #$10000-$30
    sta ent_yvel,y
:   sec
    rtl
@no: clc
    rtl

buzzy_draw:
    ENT_ENTER
    lda ent_fl,x
    and #F_DYING
    beq :+
    jsl ent_draw_knocked
    ENT_DRAW SPR_BSHELL_1
    rtl
:   jsl ent_draw_face
    lda ent_anim,x
    and #8
    bne :+
    ENT_DRAW SPR_BUZZY_1
    rtl
:   ENT_DRAW SPR_BUZZY_2
    rtl
