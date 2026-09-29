; World map (screens agent) — port of C# MapScreen: walking node to node along the paths (smooth 2 px/tick walker
; with the walk cycle + idle bob + shadow), panels/fortresses/locks/Toad houses/spade panels with their cleared/used
; states (BG block swaps), animated water (two BG1 maps), wandering Hammer Bros (-> "hb" battle), the airship
; (retreats when you lose on it, SMB3), N-Spade panels every 80,000 points, the Start menu (items, world select,
; save & quit), the item inventory (mushroom ... P-wing, cloud) and the status bar. Also the level hand-over
; (C# LevelDone) and SMB3 "continue".
.p816
.smart
.include "scr.inc"

.export map_enter, map_tick, map_new_world, map_level_done, map_continue, map_toad_used, map_nspade_done
.export mp_node, mp_force, scr_lvl, scr_lvl_kind, mp_wclear
.import scr_mapdefs, scr_mapmaps, scr_go_in, sv_set_player
.import files_two

NODE_SZ = 11
MAXN = 32

; popups
POP_NONE = 0
POP_INTRO = 1
POP_MSG = 2
POP_INV = 3
POP_MENU = 4

.segment "BSS"
mp_world: .res 2
mp_force: .res 2               ; 1 = (re)start this world's map (C# new MapScreen), 2 = + intro card
mp_t: .res 2
mp_px: .res 2
mp_py: .res 2
mp_mdx: .res 2
mp_mdy: .res 2
mp_mpix: .res 2
mp_walkt: .res 2
mp_face: .res 2
mp_adx: .res 2
mp_ady: .res 2
mp_intro: .res 2
mp_msgt: .res 2
mp_msg: .res 40
mp_cloud: .res 2
mp_inv: .res 2
mp_invsel: .res 2
mp_menu: .res 2
mp_menusel: .res 2
mp_pick: .res 2
mp_pbro: .res 2                ; pending Hammer Bro battle (-1 none)
mp_bx: .res 4
mp_by: .res 4
mp_balive: .res 4
mp_nbros: .res 2
mp_nnodes: .res 2
mp_grid: .res 192
mp_nd: .res MAXN*NODE_SZ
mp_maps: .res 16               ; this world's MAP_* ids: water1 water2 badge_l pop_intro pop_msg pop_menu4 pop_menu5 pop_inv
mp_pop: .res 2
mp_popdirty: .res 2
mp_water: .res 2
mp_nsp: .res 2                 ; last N-Spade palette variant
mp_node: .res 2                ; node being played / visited (Toad house, spade)
mp_tmp: .res 8
nd_x: .res 2                   ; node_at scratch (must not share mp_tmp)
nd_y: .res 2
nd_i: .res 2
mp_nitems: .res 2
mp_mitems: .res 2              ; menu item count
scr_lvl: .res 2                ; level to play (LVL_*)
scr_lvl_kind: .res 2           ; intro title: 0 WORLD n-n, 1 FORTRESS, 2 AIRSHIP, 3 CASTLE, 4 HAMMER BRO BATTLE
mp_hudluigi: .res 2

.segment "ZEROPAGE"
mp_p: .res 3
mp_q: .res 3

.segment "CODE12"
.a16
.i16

; ================================================================== setup
; map_new_world: A = world, X = 1 to show the "WORLD n" card. (C# new MapScreen(world, showIntro))
map_new_world:
    php
    rep #$30
    sta mp_world
    txa
    beq :+
    lda #2
    bra :++
:   lda #1
:   sta mp_force
    plp
    rtl

map_enter:
    php
    rep #$30
    lda sv_world
    and #$00FF
    cmp mp_world
    beq :+
    sta mp_world
    lda mp_force
    bne :+
    lda #1
    sta mp_force
:   jsr load_def
    lda mp_force
    beq @scene
    jsr fresh
@scene:
    stz mp_force
    ; C# Enter: Save.World = world; HighestWorld = max
    sep #$20
    .a8
    lda mp_world
    sta sv_world
    cmp sv_highest
    bcc :+
    sta sv_highest
:   rep #$20
    .a16
    lda mp_world
    dec a
    clc
    adc #SCN_MAP1
    jsl scr_scene_load
    ; node states, badge
    jsr put_nodes
    lda ss_player
    beq :+
    lda mp_maps+4
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_now
:   lda #$FFFF
    sta mp_pop
    sta mp_nsp
    stz mp_water
    ; Luigi's palette for the walker
    lda #SP_WALK1
    ldx ss_player
    jsl scr_obj_pal
    jsr hud_text
    jsr popup
    jsl txt_flush_now
    ; music (keeps playing if it already is)
    ldy #4
    lda [mp_p],y
    and #$00FF
    ldx #0
    jsl scr_music
    jsr draw
    plp
    rtl

; copy this world's definition into RAM
load_def:
    lda mp_world
    asl a
    tax
    lda f:scr_mapdefs,x
    sta mp_p
    sep #$20
    .a8
    lda #^scr_mapdefs
    sta mp_p+2
    rep #$20
    .a16
    ldy #2
    lda [mp_p],y
    and #$00FF
    sta mp_nnodes
    iny
    lda [mp_p],y
    and #$00FF
    sta mp_nbros
    ; copy the grid (192 bytes) and the node records through [mp_q]
    ldy #12
    lda [mp_p],y
    sta mp_q
    sep #$20
    .a8
    lda #^scr_mapdefs
    sta mp_q+2
    rep #$20
    .a16
    ldy #0
@gl: lda [mp_q],y
    sta mp_grid,y
    iny
    iny
    cpy #192
    bcc @gl
    ldy #8
    lda [mp_p],y
    sta mp_q
    lda mp_nnodes
    asl a
    sta mp_tmp
    asl a
    asl a
    clc
    adc mp_tmp
    adc mp_nnodes               ; *11
    sta mp_tmp
    ldy #0
@nl: cpy mp_tmp
    bcs @nd
    lda [mp_q],y
    sta mp_nd,y
    iny
    iny
    bra @nl
@nd:
    ; this world's map ids
    lda mp_world
    asl a
    tax
    lda f:scr_mapmaps,x
    sta mp_q
    ldy #0
@ml: lda [mp_q],y
    sta mp_maps,y
    iny
    iny
    cpy #16
    bcc @ml
    rts


; C# MapScreen ctor + Enter (first time): start node or the saved position, intro card, Hammer Bros
fresh:
    ldy #0
    lda [mp_p],y
    and #$00FF
    sta mp_px
    iny
    lda [mp_p],y
    and #$00FF
    sta mp_py
    ; saved position (same world, a node there)
    lda sv_mapx
    and #$00FF
    cmp #$FF
    beq @nosv
    lda sv_world
    and #$00FF
    cmp mp_world
    bne @nosv
    lda sv_mapx
    and #$00FF
    sta mp_tmp
    lda sv_mapy
    and #$00FF
    sta mp_tmp+2
    ldx mp_tmp
    ldy mp_tmp+2
    jsr node_at
    bmi @nosv
    lda mp_tmp
    sta mp_px
    lda mp_tmp+2
    sta mp_py
@nosv:
    stz mp_mpix
    stz mp_mdx
    stz mp_mdy
    stz mp_adx
    stz mp_ady
    stz mp_walkt
    stz mp_face
    stz mp_cloud
    stz mp_inv
    stz mp_menu
    stz mp_msgt
    stz mp_msg
    lda #$FFFF
    sta mp_pbro
    stz mp_intro
    lda mp_force
    cmp #2
    bne @nointro
    lda #150
    sta mp_intro
    lda sv_two
    and #$00FF
    beq @nointro
    jsr msg_reset
    lda ss_player
    bne :+
    jsr msg_mario
    bra :++
:   jsr msg_luigi
:   jsr msg_start
@nointro:
    jsr load_bros
    rts

; ------------------------------------------------------------------ Hammer Bros (positions in the save)
load_bros:
    lda mp_world
    asl a
    tax
    lda f:scr_mapdefs,x
    sta mp_q
    sep #$20
    .a8
    lda #^scr_mapdefs
    sta mp_q+2
    rep #$20
    .a16
    ldy #10
    lda [mp_q],y                ; bros table (x, y, item per bro)
    sta mp_q
    stz mp_bi
@b: lda mp_bi
    cmp mp_nbros
    bcs @d
    asl a
    clc
    adc mp_bi
    tay
    lda [mp_q],y
    and #$00FF
    sta mp_tmp
    iny
    lda [mp_q],y
    and #$00FF
    sta mp_tmp+2
    ; moved since? (sv_hbpos)
    lda mp_world
    dec a
    asl a
    asl a
    sta mp_tmp+4
    lda mp_bi
    asl a
    clc
    adc mp_tmp+4
    tax
    lda sv_hbpos,x
    and #$00FF
    cmp #$FF
    beq :+
    sta mp_tmp
    lda sv_hbpos+1,x
    and #$00FF
    sta mp_tmp+2
:   lda mp_bi
    asl a
    tax
    lda mp_tmp
    sta mp_bx,x
    lda mp_tmp+2
    sta mp_by,x
    ; alive unless beaten
    ldx mp_bi
    jsr bro_bit
    and sv_hbdead
    beq :+
    lda #0
    bra :++
:   lda #1
:   pha
    lda mp_bi
    asl a
    tax
    pla
    sta mp_balive,x
    inc mp_bi
    bra @b
@d: rts
.segment "BSS"
mp_bi: .res 2
.segment "CODE12"
.a16
.i16

; X = bro -> A = its bit in sv_hbdead ((world-1)*2 + bro)
bro_bit:
    lda mp_world
    dec a
    asl a
    stx mp_tmp+6
    clc
    adc mp_tmp+6
    tay
    lda #1
@s: dey
    bmi @r
    asl a
    bra @s
@r: rts

; save bro X's position
save_bro:
    lda mp_world
    dec a
    asl a
    asl a
    sta mp_tmp+4
    txa
    asl a
    clc
    adc mp_tmp+4
    tay
    phx
    txa
    asl a
    tax
    sep #$20
    .a8
    lda mp_bx,x
    sta sv_hbpos,y
    lda mp_by,x
    sta sv_hbpos+1,y
    rep #$20
    .a16
    plx
    rts

; C# MoveBros: each living bro takes 1-2 random steps along path cells
move_bros:
    ldx #0
@b: cpx mp_nbros
    jcs @d
    phx
    txa
    asl a
    tax
    lda mp_balive,x
    beq @next
    jsl scr_rand
    and #1
    inc a
    sta mp_tmp+6                ; steps
@step:
    jsl scr_rand
    and #3
    sta mp_tmp+4                ; start dir
    ldy #0
@dir: phy
    tya
    clc
    adc mp_tmp+4
    and #3
    asl a
    phx
    tax
    lda f:dxs,x
    sta mp_tmp
    lda f:dys,x
    sta mp_tmp+2
    plx
    lda mp_bx,x
    clc
    adc mp_tmp
    sta mp_tmp
    lda mp_by,x
    clc
    adc mp_tmp+2
    sta mp_tmp+2
    phx
    ldx mp_tmp
    ldy mp_tmp+2
    jsr grid_at
    jsr is_path
    plx
    ply
    bcc @ndir
    lda mp_tmp
    sta mp_bx,x
    lda mp_tmp+2
    sta mp_by,x
    bra @sd
@ndir:
    iny
    cpy #4
    bcc @dir
@sd: dec mp_tmp+6
    bne @step
@next:
    plx
    jsr save_bro
    inx
    brl @b
@d: rts
dxs: .word 1, $FFFF, 0, 0
dys: .word 0, 0, 1, $FFFF

; X, Y = tile -> A = bro index or $FFFF
bro_at:
    stx mp_tmp
    sty mp_tmp+2
    ldx #0
@b: cpx mp_nbros
    bcs @no
    phx
    txa
    asl a
    tax
    lda mp_balive,x
    beq @n
    lda mp_bx,x
    cmp mp_tmp
    bne @n
    lda mp_by,x
    cmp mp_tmp+2
    bne @n
    pla
    rts
@n: plx
    inx
    bra @b
@no: lda #$FFFF
    rts

; ------------------------------------------------------------------ grid / nodes
; X, Y = tile (signed) -> A = grid char (' ' outside)
grid_at:
    cpx #16
    bcs @out
    cpy #12
    bcs @out
    tya
    asl a
    asl a
    asl a
    asl a
    stx mp_gx
    clc
    adc mp_gx
    tax
    lda mp_grid,x
    and #$00FF
    rts
@out: lda #' '
    rts

; A = char -> carry set if a path (- | = ! +)
is_path:
    cmp #'-'
    beq @y
    cmp #'|'
    beq @y
    cmp #'='
    beq @y
    cmp #'!'
    beq @y
    cmp #'+'
    beq @y
    clc
    rts
@y: sec
    rts

; X, Y = tile -> A = node index ($FFFF none, N flag set). The airship follows sv_ship when it retreated.
node_at:
    ; own scratch (nd_*): callers keep their move direction etc. in mp_tmp, which this must not clobber
    stx nd_x
    sty nd_y
    jsr ship_node               ; A = airship node index or $FFFF, carry = moved
    bcc @static
    sta nd_i
    jsr ship_pos                ; X, Y = where it is now
    cpx nd_x
    bne @static
    cpy nd_y
    bne @static
    lda nd_i
    rts
@static:
    ldx #0
    ldy #0
@l: cpy mp_nnodes
    bcs @no
    lda mp_nd+1,x
    and #$00FF
    cmp nd_x
    bne @n
    lda mp_nd+2,x
    and #$00FF
    cmp nd_y
    bne @n
    ; the airship's home cell is empty once it moved
    lda mp_nd+3,x
    and #$00FF
    cmp #NK_AIRSHIP
    bne @hit
    phx
    phy
    jsr ship_node
    ply
    plx
    bcs @n
@hit: tya
    rts
@n: txa
    clc
    adc #NODE_SZ
    tax
    iny
    bra @l
@no: lda #$FFFF
    rts

; -> A = airship node index ($FFFF none); carry set if it moved away from home (sv_ship)
ship_node:
    ldx #0
    ldy #0
@l: cpy mp_nnodes
    bcs @none
    lda mp_nd+3,x
    and #$00FF
    cmp #NK_AIRSHIP
    beq @f
    txa
    clc
    adc #NODE_SZ
    tax
    iny
    bra @l
@f: phy
    lda mp_world
    dec a
    asl a
    tax
    lda sv_ship,x
    and #$00FF
    cmp #$FF
    beq :+
    pla
    sec
    rts
:   pla
    clc
    rts
@none: lda #$FFFF
    clc
    rts
; -> X, Y = current airship tile (home or sv_ship)
ship_pos:
    lda mp_world
    dec a
    asl a
    tax
    lda sv_ship,x
    and #$00FF
    cmp #$FF
    beq @home
    pha
    lda sv_ship+1,x
    and #$00FF
    tay
    plx
    rts
@home:
    jsr ship_node
    bmi @no
    jsr nrec
    lda mp_nd+2,x
    and #$00FF
    tay
    lda mp_nd+1,x
    and #$00FF
    tax
    rts
@no: ldx #$FF
    ldy #$FF
    rts

; A = node -> X = record offset
nrec:
    sta mp_tmp+6
    asl a
    sta mp_tmp+4
    asl a
    asl a
    clc
    adc mp_tmp+4
    adc mp_tmp+6
    tax
    rts

; A = node -> A = kind
nkind:
    jsr nrec
    lda mp_nd+3,x
    and #$00FF
    rts

; A = node -> carry = its "other state" bit (cleared / open / used) in sv_nodes
nalt:
    jsr altaddr
    lda sv_nodes,x
    and mp_tmp+6
    beq :+
    sec
    rts
:   clc
    rts
; A = node: set the bit
set_alt:
    jsr altaddr
    lda sv_nodes,x
    ora mp_tmp+6
    sta sv_nodes,x
    rts
; A = node -> X = byte index in sv_nodes (16-bit access), mp_tmp+6 = mask
altaddr:
    pha
    and #15
    tay
    lda #1
@s: dey
    bmi @d
    asl a
    bra @s
@d: sta mp_tmp+6
    pla
    lsr a
    lsr a
    lsr a
    and #$FFFE                  ; word of 16 nodes
    sta mp_tmp+4
    lda mp_world
    dec a
    asl a
    asl a
    clc
    adc mp_tmp+4
    tax
    rts

; A = node -> carry set if it blocks (an uncleared level/fortress/airship/castle panel)
blocking:
    pha
    jsr nkind
    cmp #NK_LEVEL
    beq @k
    cmp #NK_FORTRESS
    beq @k
    cmp #NK_AIRSHIP
    beq @k
    cmp #NK_CASTLE
    beq @k
    pla
    clc
    rts
@k: pla
    jsr nalt
    bcs :+
    sec
    rts
:   clc
    rts

; A = node: carry set if it is a lock that is still closed
closed_lock:
    pha
    jsr nkind
    cmp #NK_LOCK
    beq :+
    pla
    clc
    rts
:   pla
    jsr nalt
    bcc :+
    clc
    rts
:   sec
    rts

; put every node's current state block (forced blank)
put_nodes:
    stz mp_tmp+2
@l: lda mp_tmp+2
    cmp mp_nnodes
    bcs @d
    jsr nalt
    bcc @n
    lda mp_tmp+2
    jsr nrec
    lda mp_nd+7,x
    beq @n
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_now
@n: inc mp_tmp+2
    bra @l
@d: rts

; ================================================================== tick
map_tick:
    php
    rep #$30
    inc mp_t
    ; play time
    inc sv_frames
    bne :+
    inc sv_frames+2
:   lda mp_msgt
    beq :+
    dec mp_msgt
:   lda mp_intro
    beq @nointro
    dec mp_intro
    lda scr_pressed
    and #PAD_START|KEY_OK
    beq :+
    stz mp_intro
:   jmp @draw
@nointro:
    lda mp_mpix
    beq @still
    inc mp_walkt
    lda mp_mdx
    beq :+
    and #$8000
    sta mp_face
:   jsr step_move
    jmp @draw
@still:
    stz mp_walkt
    ; a Hammer Bro walked onto us
    ldx mp_px
    ldy mp_py
    jsr bro_at
    bmi @nobro
    ldx mp_pbro
    bpl @nobro
    ldx mp_msgt
    bne @nobro
    jsr start_battle
    jmp @draw
@nobro:
    lda mp_inv
    beq :+
    jsr inv_tick
    jmp @draw
:   lda mp_menu
    beq :+
    jsr menu_tick
    jmp @draw
:   lda scr_pressed
    and #PAD_START
    beq :+
    lda #1
    sta mp_menu
    stz mp_menusel
    lda mp_world
    sta mp_pick
    lda #1
    sta mp_popdirty
    SFX SFX_PAUSE
    jmp @draw
:   ; walking (held direction: left, right, up, down)
    lda scr_held
    ldx #$FFFF
    ldy #0
    bit #PAD_LEFT
    bne @go
    ldx #1
    bit #PAD_RIGHT
    bne @go
    ldx #0
    ldy #$FFFF
    bit #PAD_UP
    bne @go
    ldy #1
    bit #PAD_DOWN
    bne @go
    ; A: enter; B(run)/Select: items
    lda scr_pressed
    and #KEY_OK
    beq :+
    jsr enter_node
    bra @draw
:   lda scr_pressed
    and #KEY_BACK|PAD_SELECT
    beq @draw
    lda #1
    sta mp_inv
    stz mp_invsel
    sta mp_popdirty
    SFX SFX_PAUSE
    bra @draw
@go:
    stx mp_tmp
    sty mp_tmp+2
    jsr try_move
    lda mp_mpix
    beq @draw
    jsr step_move               ; start moving this frame (no 1-frame stall)
@draw:
    jsr draw
    plp
    rtl

; C# TryMove (mp_tmp = dx, mp_tmp+2 = dy)
try_move:
    lda mp_tmp
    sta mp_tmp+8-8+0            ; keep
    ldx mp_px
    ldy mp_py
    jsr node_at
    bmi @free
    jsr blocking
    bcc @free
    lda mp_cloud
    bne @free
    ; may only go back the way we came
    lda mp_adx
    ora mp_ady
    beq @no
    lda mp_adx
    eor #$FFFF
    inc a
    cmp mp_tmp
    bne @no
    lda mp_ady
    eor #$FFFF
    inc a
    cmp mp_tmp+2
    bne @no
@free:
    lda mp_px
    clc
    adc mp_tmp
    tax
    lda mp_py
    clc
    adc mp_tmp+2
    tay
    phx
    phy
    jsr node_at
    ply
    plx
    bmi @nonode
    jsr closed_lock
    bcs @no
    bra @ok
@nonode:
    jsr grid_at
    jsr is_path
    bcc @no
@ok:
    lda mp_tmp
    sta mp_mdx
    lda mp_tmp+2
    sta mp_mdy
    lda #16
    sta mp_mpix
    SFX SFX_MAPSTEP
@no: rts

; C# StepMove
step_move:
    lda mp_mpix
    sec
    sbc #2
    sta mp_mpix
    beq :+
    rts
:   lda mp_px
    clc
    adc mp_mdx
    sta mp_px
    lda mp_py
    clc
    adc mp_mdy
    sta mp_py
    ; Hammer Bro on this tile: battle
    ldx mp_px
    ldy mp_py
    jsr bro_at
    bmi :+
    pha
    jsr arrive
    pla
    jmp start_battle
:   ldx mp_px
    ldy mp_py
    jsr node_at
    bmi @cont
    jsr nkind
    cmp #NK_LOCK
    beq @cont
    ; stop on the node
    jsr arrive
    sep #$20
    .a8
    lda mp_px
    sta sv_mapx
    lda mp_py
    sta sv_mapy
    rep #$20
    .a16
    rts
@cont:
    ; keep going straight?
    lda mp_mdx
    sta mp_tmp
    lda mp_mdy
    sta mp_tmp+2
    jsr passable
    bcc :+
    lda #16
    sta mp_mpix
    rts
:   ; the path turns: the single continuation that isn't where we came from
    ldy #0
@k: phy
    tyx
    lda f:dxs,x
    sta mp_tmp
    lda f:dys,x
    sta mp_tmp+2
    ; skip the reverse direction
    lda mp_mdx
    clc
    adc mp_tmp
    bne @try
    lda mp_mdy
    clc
    adc mp_tmp+2
    beq @skip
@try:
    jsr passable
    bcc @skip
    ply
    lda mp_tmp
    sta mp_mdx
    lda mp_tmp+2
    sta mp_mdy
    lda #16
    sta mp_mpix
    rts
@skip:
    ply
    iny
    iny
    cpy #8
    bcc @k
    jsr arrive
    rts

; the tile at (px + mp_tmp, py + mp_tmp+2) can be walked onto (path, or a node that isn't a closed lock)
passable:
    lda mp_px
    clc
    adc mp_tmp
    tax
    lda mp_py
    clc
    adc mp_tmp+2
    tay
    phx
    phy
    jsr node_at
    ply
    plx
    bmi @nn
    jsr closed_lock
    bcs @no
    sec
    rts
@nn: jsr grid_at
    jmp is_path
@no: clc
    rts

arrive:
    lda mp_mdx
    sta mp_adx
    lda mp_mdy
    sta mp_ady
    rts

; ------------------------------------------------------------------ entering nodes (C# EnterNode)
enter_node:
    ; N-Spade panel
    jsr nsp_pos
    bcc @nonsp
    cpx mp_px
    bne @nonsp
    cpy mp_py
    bne @nonsp
    SFX SFX_MAPENTER
    jsr save_pos
    lda #SC_NSPADE
    jsl scr_go
    rts
@nonsp:
    ldx mp_px
    ldy mp_py
    jsr node_at
    bpl :+
    rts
:   sta mp_node
    jsr nkind
    cmp #NK_TOAD
    beq @toad
    cmp #NK_SPADE
    beq @spade
    cmp #NK_LEVEL
    beq @lvl
    cmp #NK_FORTRESS
    beq @lvl
    cmp #NK_AIRSHIP
    beq @lvl
    cmp #NK_CASTLE
    beq @lvl
    rts
@lvl:
    lda mp_node
    jsr nalt
    bcs @err
    stz mp_cloud
    SFX SFX_MAPENTER
    jsr save_pos
    jsl sv_save
    lda mp_node
    jsr nrec
    lda mp_nd+4,x
    and #$00FF
    sta scr_lvl
    lda mp_nd+3,x
    and #$00FF
    sec
    sbc #NK_LEVEL
    sta scr_lvl_kind            ; 0 level, 1 fortress, 2 airship, 3 castle
    lda #SC_INTRO
    jsl scr_go
    rts
@toad:
    lda mp_node
    jsr nalt
    bcs @err
    SFX SFX_MAPENTER
    lda #SC_TOAD
    jsl scr_go
    rts
@spade:
    lda mp_node
    jsr nalt
    bcs @err
    SFX SFX_MAPENTER
    lda #SC_SPADE
    jsl scr_go
    rts
@err:
    SFX SFX_ERROR
    rts

save_pos:
    sep #$20
    .a8
    lda mp_px
    sta sv_mapx
    lda mp_py
    sta sv_mapy
    rep #$20
    .a16
    rts

; A = bro -> Hammer Bro battle (C# StartBattle)
start_battle:
    sta mp_pbro
    SFX SFX_MAPENTER
    jsr save_pos
    jsl sv_save
    lda #LVL_HB
    sta scr_lvl
    lda #4
    sta scr_lvl_kind
    lda #SC_INTRO
    jsl scr_go
    rts

; map_toad_used: C# MarkToadUsed(node = mp_node) + save
map_toad_used:
    php
    rep #$30
    lda mp_node
    jsr set_alt
    jsl sv_save
    plp
    rtl

; ================================================================== level results (C# LevelDone)
; map_level_done: A = g_result (RES_*), scr_lvl = the level that was played. Picks the next screen.
map_level_done:
    php
    rep #$30
    sta mp_tmp+8-8+2            ; (kept in mp_res)
    sta mp_res
    lda scr_lvl
    cmp #LVL_HB
    bne @normal
    ; Hammer Bro battle
    ldx mp_pbro
    lda #$FFFF
    sta mp_pbro
    lda mp_res
    cmp #RES_CLEARED
    bne @hblost
    cpx #$FFFF
    beq @hblost
    ; beaten: the bro disappears and leaves his item
    phx
    jsr bro_bit
    ora sv_hbdead
    sta sv_hbdead
    plx
    txa
    asl a
    tax
    stz mp_balive,x
    lsr a
    jsr bro_item
    sta mp_fcode
    jsl sv_add_item
    jsr msg_reset
    jsr msg_got
    lda mp_fcode
    jsr msg_item
    lda #'!'
    jsr msg_char
    lda #150
    sta mp_msgt
    jsl sv_save
    jmp @tomap
@hblost:
    lda mp_res
    cmp #RES_EXITED
    bne :+
    jmp @tomap
:   lda #$FFFF                  ; lost: normal death handling, no node
    sta mp_node
    bra @dispatch
@normal:
    ; the node of this level
    jsr find_level
    sta mp_node
@dispatch:
    ; Hammer Bros move after every result except exit / world / game clear
    lda mp_res
    cmp #RES_EXITED
    beq :+
    cmp #RES_WORLD
    beq :+
    cmp #RES_GAME
    beq :+
    jsr move_bros
:   lda mp_res
    cmp #RES_CLEARED
    beq @clr
    cmp #RES_FORTRESS
    beq @clr
    cmp #RES_WORLD
    bne :+
    jmp @world
:   cmp #RES_GAME
    bne :+
    jmp @game
:   cmp #RES_EXITED
    bne @died
    jmp @tomap
@clr:
    lda mp_node
    bmi @clr2
    jsr set_alt
    lda mp_node
    jsr nkind
    cmp #NK_FORTRESS
    bne @clr2
    ; open the locks of this fortress
    lda mp_node
    jsr nrec
    lda mp_nd,x
    and #$00FF
    sta mp_tmp+8-8+4
    sta mp_fcode
    stz mp_tmp+2
@lk: lda mp_tmp+2
    cmp mp_nnodes
    bcs @lkd
    jsr nrec
    lda mp_nd+3,x
    and #$00FF
    cmp #NK_LOCK
    bne :+
    lda mp_nd+5,x
    and #$00FF
    cmp mp_fcode
    bne :+
    lda mp_tmp+2
    jsr set_alt
:   inc mp_tmp+2
    bra @lk
@lkd:
    jsr msg_reset
    jsr msg_lock
    lda #150
    sta mp_msgt
@clr2:
    jsr check_nspade
    jsr next_turn
    jsr save_pos
    jsl sv_save
    bra @tomap
@died:
    ; C#: form = small, lose a life; game over at 0 lives
    ldx ss_cur
    sep #$20
    .a8
    lda #FM_SMALL
    sta pp_form,x
    rep #$20
    .a16
    jsl sv_lose_life
    ; SMB3: the airship flies off when you lose on it
    lda mp_node
    bmi :+
    jsr nkind
    cmp #NK_AIRSHIP
    bne :+
    jsr ship_retreat
:   ldx ss_cur
    lda pp_lives,x
    and #$00FF
    bne @alive
    lda sv_opt_inf
    bne @alive
    sep #$20
    .a8
    lda #1
    sta pp_over,x
    rep #$20
    .a16
    lda #SC_GAMEOVER
    jsl scr_go_in
    plp
    rtl
@alive:
    jsr check_nspade
    jsr next_turn
    jsr save_pos
    jsl sv_save
@tomap:
    lda #SC_MAP
    jsl scr_go_in
    plp
    rtl
@world:
    lda mp_world
    sta mp_wclear
    lda mp_node
    bmi :+
    jsr set_alt
:   lda mp_world
    inc a
    cmp #9
    bcc :+
    lda #8
:   sep #$20
    .a8
    sta sv_world
    lda #$FF
    sta sv_mapx
    sta sv_mapy
    rep #$20
    .a16
    jsl sv_save
    lda #SC_WORLDCLR
    jsl scr_go_in
    plp
    rtl
@game:
    lda mp_node
    bmi :+
    jsr set_alt
:   sep #$20
    .a8
    lda #1
    sta sv_beaten
    rep #$20
    .a16
    jsl sv_save
    lda #SC_ENDING
    jsl scr_go_in
    plp
    rtl
.segment "BSS"
mp_res: .res 2
mp_fcode: .res 2
mp_wclear: .res 2
.segment "CODE12"
.a16
.i16

; -> A = node index playing level scr_lvl ($FFFF none)
find_level:
    stz mp_tmp+2
@l: lda mp_tmp+2
    cmp mp_nnodes
    bcs @no
    jsr nrec
    lda mp_nd+4,x
    and #$00FF
    cmp scr_lvl
    bne :+
    lda mp_nd+3,x
    and #$00FF
    cmp #NK_LEVEL
    bcc :+
    cmp #NK_TOAD
    bcs :+
    lda mp_tmp+2
    rts
:   inc mp_tmp+2
    bra @l
@no: lda #$FFFF
    rts

; A = bro -> A = its item (IT_*)
bro_item:
    sta mp_tmp
    asl a
    clc
    adc mp_tmp
    clc
    adc #2
    sta mp_tmp
    lda mp_world
    asl a
    tax
    lda f:scr_mapdefs,x
    sta mp_q
    ldy #10
    lda [mp_q],y
    sta mp_q
    ldy mp_tmp
    lda [mp_q],y
    and #$00FF
    rts

; SMB3 airship retreat: it moves to a random resting dot (not under the player or a Hammer Bro)
ship_retreat:
    jsr ship_pos
    stx mp_tmp+8-8+0
    ; count candidate dots
    stz mp_cnt
    stz mp_li
@c: lda mp_li
    cmp mp_nnodes
    bcs @cd
    jsr cand
    bcc :+
    inc mp_cnt
:   inc mp_li
    bra @c
@cd: lda mp_cnt
    beq @r
    jsl scr_rand
    and #$00FF
@mod: cmp mp_cnt
    bcc :+
    sbc mp_cnt
    bra @mod
:   sta mp_pickn
    stz mp_li
@p: lda mp_li
    jsr cand
    bcc @pn
    lda mp_pickn
    beq @take
    dec mp_pickn
@pn: inc mp_li
    bra @p
@take:
    lda mp_li
    jsr nrec
    lda mp_world
    dec a
    asl a
    tay
    sep #$20
    .a8
    lda mp_nd+1,x
    sta sv_ship,y
    lda mp_nd+2,x
    sta sv_ship+1,y
    rep #$20
    .a16
@r: rts
; A = node -> carry if it's a free resting dot
cand:
    pha
    jsr nkind
    cmp #NK_DOT
    beq :+
    pla
    clc
    rts
:   pla
    jsr nrec
    lda mp_nd+1,x
    and #$00FF
    cmp mp_px
    bne :+
    lda mp_nd+2,x
    and #$00FF
    cmp mp_py
    beq @no
:   lda mp_nd+1,x
    and #$00FF
    pha
    lda mp_nd+2,x
    and #$00FF
    tay
    plx
    jsr bro_at
    bpl @no
    sec
    rts
@no: clc
    rts
.segment "BSS"
mp_cnt: .res 2
mp_li: .res 2
mp_gx: .res 2
mp_pickn: .res 2
.segment "CODE12"
.a16
.i16

; ------------------------------------------------------------------ N-Spade (C# CheckNSpade / NSpadeDone)
; -> carry + X, Y = this world's N-Spade panel
nsp_pos:
    lda mp_world
    dec a
    asl a
    tax
    lda sv_nsp,x
    and #$00FF
    cmp #$FF
    beq @no
    pha
    lda sv_nsp+1,x
    and #$00FF
    tay
    plx
    sec
    rts
@no: clc
    rts

check_nspade:
    ; score >= NSpadeAt ?  (BCD compare, high word first)
    ldx ss_cur
    lda pp_score+2,x
    cmp pp_nspade+2,x
    jcc @r
    bne @yes
    lda pp_score,x
    cmp pp_nspade,x
    jcc @r
@yes:
    ; while NSpadeAt <= score: += 80,000 (BCD)
@add: sed
    clc
    lda pp_nspade+2,x
    adc #$0008
    sta pp_nspade+2,x
    cld
    lda pp_nspade+2,x
    cmp pp_score+2,x
    bcc @add
    bne :+
    lda pp_nspade,x
    cmp pp_score,x
    bcc @add
    beq @add
:   jsr nsp_pos
    bcs @r                      ; one at a time
    ; a random free dot
    stz mp_cnt
    stz mp_li
@c: lda mp_li
    cmp mp_nnodes
    bcs @cd
    jsr cand
    bcc :+
    inc mp_cnt
:   inc mp_li
    bra @c
@cd: lda mp_cnt
    beq @r
    jsl scr_rand
    and #$00FF
@mod: cmp mp_cnt
    bcc :+
    sbc mp_cnt
    bra @mod
:   sta mp_pickn
    stz mp_li
@p: lda mp_li
    jsr cand
    bcc @pn
    lda mp_pickn
    beq @take
    dec mp_pickn
@pn: inc mp_li
    bra @p
@take:
    lda mp_li
    jsr nrec
    lda mp_world
    dec a
    asl a
    tay
    sep #$20
    .a8
    lda mp_nd+1,x
    sta sv_nsp,y
    lda mp_nd+2,x
    sta sv_nsp+1,y
    rep #$20
    .a16
    jsr msg_reset
    jsr msg_nsp
    lda #150
    sta mp_msgt
    SFX SFX_ONEUP
@r: rts

; map_nspade_done: the panel is used up
map_nspade_done:
    php
    rep #$30
    lda mp_world
    dec a
    asl a
    tax
    lda #$FFFF
    sta sv_nsp,x
    jsr save_pos
    jsl sv_save
    plp
    rtl

; ------------------------------------------------------------------ 2 players (C# NextTurn) / continue
next_turn:
    lda sv_two
    and #$00FF
    beq @r
    lda ss_player
    eor #1
    sta mp_tmp
    beq :+
    lda #PP_SIZE_
:   tax
    lda pp_over,x
    and #$00FF
    bne @r
    lda mp_tmp
    jsl sv_set_player
    jsr msg_reset
    lda ss_player
    bne :+
    jsr msg_mario
    bra :++
:   jsr msg_luigi
:   jsr msg_turn
    lda #120
    sta mp_msgt
@r: rts

; map_continue: C# Continue — 4 lives, score 0, cards lost, this world's panels (not fortresses/locks) back,
; Toad houses refilled, back to START
map_continue:
    php
    rep #$30
    ldx ss_cur
    sep #$20
    .a8
    lda #4
    sta pp_lives,x
    stz pp_score,x
    stz pp_score+1,x
    stz pp_score+2,x
    stz pp_score+3,x
    stz pp_nspade,x
    stz pp_nspade+1,x
    lda #8
    sta pp_nspade+2,x
    stz pp_nspade+3,x
    stz pp_ncards,x
    stz pp_over,x
    stz pp_form,x
    rep #$20
    .a16
    ; keep only fortress + lock bits of this world
    stz mp_keep
    stz mp_keep+2
    stz mp_tmp+2
@l: lda mp_tmp+2
    cmp mp_nnodes
    bcs @ld
    jsr nkind
    cmp #NK_FORTRESS
    beq @k
    cmp #NK_LOCK
    bne @n
@k: lda mp_tmp+2
    jsr altaddr
    txa
    sec
    sbc #0
    lda mp_tmp+2
    cmp #16
    bcs :+
    lda mp_keep
    ora mp_tmp+6
    sta mp_keep
    bra @n
:   lda mp_keep+2
    ora mp_tmp+6
    sta mp_keep+2
@n: inc mp_tmp+2
    bra @l
@ld:
    lda mp_world
    dec a
    asl a
    asl a
    tax
    lda sv_nodes,x
    and mp_keep
    sta sv_nodes,x
    lda sv_nodes+2,x
    and mp_keep+2
    sta sv_nodes+2,x
    ldy #0
    lda [mp_p],y
    and #$00FF
    sta mp_px
    iny
    lda [mp_p],y
    and #$00FF
    sta mp_py
    stz mp_adx
    stz mp_ady
    jsr save_pos
    jsl sv_save
    plp
    rtl
.segment "BSS"
mp_keep: .res 4
.segment "CODE12"
.a16
.i16

; ================================================================== inventory (C# InventoryTick / UseItem)
inv_tick:
    ldx ss_cur
    lda pp_nitems,x
    and #$00FF
    sta mp_nitems
    ; h + v * 7
    jsl scr_navh
    sta mp_tmp
    jsl scr_navv
    sta mp_tmp+2
    asl a
    asl a
    asl a
    sec
    sbc mp_tmp+2
    clc
    adc mp_tmp
    beq @nomove
    ldx mp_nitems
    beq @nomove
    clc
    adc mp_invsel
    bpl :+
    lda #0
:   cmp mp_nitems
    bcc :+
    lda mp_nitems
    dec a
:   sta mp_invsel
    lda #1
    sta mp_popdirty
    SFX SFX_MENUMOVE
@nomove:
    lda scr_pressed
    and #KEY_BACK|PAD_SELECT
    beq :+
    stz mp_inv
    SFX SFX_MENUBACK
    rts
:   lda scr_pressed
    and #KEY_OK|PAD_START
    beq @r
    lda mp_nitems
    beq @r
    ; the item
    lda ss_cur
    clc
    adc mp_invsel
    tax
    lda pp_items,x
    and #$00FF
    jsr use_item
    bcc @err
    ; remove it
    lda ss_cur
    clc
    adc mp_invsel
    tax
@rm: lda pp_items+1,x
    sep #$20
    .a8
    sta pp_items,x
    rep #$20
    .a16
    inx
    txa
    sec
    sbc ss_cur
    cmp #27
    bcc @rm
    ldx ss_cur
    sep #$20
    .a8
    dec pp_nitems,x
    rep #$20
    .a16
    ; invSel = max(0, min(invSel, count - 1))
    lda pp_nitems,x
    and #$00FF
    beq @zero
    dec a
    cmp mp_invsel
    bcs :+
    sta mp_invsel
    bra :+
@zero:
    stz mp_invsel
:   SFX SFX_ITEMUSE
    jsl sv_save
    stz mp_inv
    rts
@err:
    SFX SFX_ERROR
@r: rts

; A = item -> carry set if it was used
use_item:
    asl a
    tax
    ldy ss_cur
    lda pp_form,y
    and #$00FF
    sta mp_tmp
    jmp (use_tab,x)
use_tab: .addr use_no, use_mush, use_flower, use_leaf, use_star, use_pwing, use_tanooki, use_frog, use_hammer, use_cloud
use_no: clc
    rts
use_mush:
    lda mp_tmp
    cmp #FM_SMALL
    bne use_no
    lda #FM_BIG
    bra set_form
use_flower:
    lda #FM_FIRE
    bra form_if
use_leaf:
    lda #FM_RACCOON
    bra form_if
use_tanooki:
    lda #FM_TANOOKI
    bra form_if
use_frog:
    lda #FM_FROG
    bra form_if
use_hammer:
    lda #FM_HAMMER
form_if:
    cmp mp_tmp
    beq use_no
set_form:
    ldy ss_cur
    sep #$20
    .a8
    sta pp_form,y
    rep #$20
    .a16
    sec
    rts
use_star:
    lda ss_star
    bne use_no
    lda #1
    sta ss_star
    jsr msg_reset
    jsr msg_starready
    lda #90
    sta mp_msgt
    sec
    rts
use_pwing:
    lda ss_pwing
    bne use_no
    lda #1
    sta ss_pwing
    jsr msg_reset
    jsr msg_pwready
    lda #90
    sta mp_msgt
    sec
    rts
use_cloud:
    ldx mp_px
    ldy mp_py
    jsr node_at
    bmi use_no
    jsr blocking
    bcc use_no
    lda #1
    sta mp_cloud
    jsr msg_reset
    jsr msg_cloud
    lda #120
    sta mp_msgt
    sec
    rts

; ================================================================== Start menu (C# MenuTick)
menu_count:
    lda sv_highest
    and #$00FF
    cmp #2
    lda #4
    bcc :+
    lda #5
:   sta mp_mitems
    rts
; A = menu line -> action id (0 continue, 1 items, 2 world select, 3 options, 4 save & quit)
menu_id:
    ldx mp_mitems
    cpx #5
    beq :+
    cmp #2
    bcc :+
    inc a
:   rts

menu_tick:
    jsr menu_count
    jsl scr_navv
    beq @nv
    clc
    adc mp_menusel
    bpl :+
    lda mp_mitems
    dec a
:   cmp mp_mitems
    bcc :+
    lda #0
:   sta mp_menusel
    lda #1
    sta mp_popdirty
    SFX SFX_MENUMOVE
@nv:
    lda mp_menusel
    jsr menu_id
    sta mp_tmp
    cmp #2
    bne @nows
    jsl scr_navh
    beq @nows
    clc
    adc mp_pick
    beq @nows
    pha
    lda sv_highest
    and #$00FF
    sta mp_tmp+2
    pla
    cmp mp_tmp+2
    beq :+
    bcs @nows
:   sta mp_pick
    lda #1
    sta mp_popdirty
    SFX SFX_MENUMOVE
@nows:
    lda scr_pressed
    and #KEY_BACK
    bne @close
    lda scr_pressed
    and #PAD_START
    beq :+
    lda mp_tmp
    beq @close
:   lda scr_pressed
    and #KEY_OK|PAD_START
    bne :+
    rts
:   lda mp_tmp
    asl a
    tax
    jmp (menu_acts,x)
@close:
    stz mp_menu
    SFX SFX_MENUBACK
    rts
menu_acts: .addr m_cont, m_items, m_world, m_opt, m_quit
m_cont:
    stz mp_menu
    SFX SFX_MENUBACK
    rts
m_items:
    stz mp_menu
    lda #1
    sta mp_inv
    stz mp_invsel
    SFX SFX_MENUSELECT
    rts
m_world:
    stz mp_menu
    lda mp_pick
    cmp mp_world
    beq :+
    sep #$20
    .a8
    sta sv_world
    lda #$FF
    sta sv_mapx
    sta sv_mapy
    rep #$20
    .a16
    jsl sv_save
    SFX SFX_MAPENTER
    lda mp_pick
    ldx #1
    jsl map_new_world
    lda #SC_MAP
    jsl scr_go
:   rts
m_opt:
    stz mp_menu
    lda #SC_MAP
    sta opt_back
    lda #SC_OPTIONS
    jsl scr_go_now
    rts
m_quit:
    stz mp_menu
    jsr save_pos
    jsl sv_save
    SFX SFX_MENUBACK
    lda #SC_TITLE
    jsl scr_go
    rts
.import opt_back

; ================================================================== drawing
draw:
    ; water animation: (t/32) % 2
    lda mp_t
    and #32
    beq :+
    lda #1
:   cmp mp_water
    beq @nw
    sta mp_water
    asl a
    tax
    lda mp_maps,x
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put
    ; the water maps carry the initial node states and badge: re-apply the changed blocks
    jsr requeue_nodes
@nw:
    jsr popup
    jsl scr_obj_begin
    ; airship (bobbing)
    jsr ship_node
    bmi @nos
    jsr ship_pos
    txa
    asl a
    asl a
    asl a
    asl a
    sta spr_x
    tya
    asl a
    asl a
    asl a
    asl a
    sta spr_y
    lda mp_t
    and #16
    beq :+
    dec spr_y
:   lda #$20
    sta spr_fl
    lda #SP_AIRSHIP
    jsl scr_obj_put
@nos:
    ; N-Spade panel (blue, pulsing palette)
    jsr nsp_pos
    bcc @nonsp
    txa
    asl a
    asl a
    asl a
    asl a
    sta spr_x
    tya
    asl a
    asl a
    asl a
    asl a
    sta spr_y
    lda #$20
    sta spr_fl
    lda #SP_NSPADE
    jsl scr_obj_put
    lda mp_t
    and #16
    beq :+
    lda #1
:   cmp mp_nsp
    beq @nonsp
    sta mp_nsp
    tax
    lda #SP_NSPADE
    jsl scr_obj_pal
@nonsp:
    ; Hammer Bros
    ldx #0
@hb: cpx mp_nbros
    bcs @hbd
    phx
    txa
    asl a
    tax
    lda mp_balive,x
    beq @hbn
    lda mp_bx,x
    asl a
    asl a
    asl a
    asl a
    sta spr_x
    lda mp_by,x
    asl a
    asl a
    asl a
    asl a
    dec a
    dec a
    sta spr_y
    ; bob: ((t/10 + i) % 2)
    lda mp_t
    DIVC 10
    clc
    adc 1,s
    and #1
    beq :+
    dec spr_y
:   lda mp_t
    DIVC 40
    and #1
    eor #1
    ora #$20
    sta spr_fl
    lda #SP_HBRO
    jsl scr_obj_put
@hbn: plx
    inx
    bra @hb
@hbd:
    ; the walker (C# DrawPlayer)
    lda mp_mpix
    beq :+
    lda #16
    sec
    sbc mp_mpix
:   sta mp_tmp                  ; off
    lda mp_mdx
    jsr mul_off
    sta mp_tmp+2
    lda mp_px
    asl a
    asl a
    asl a
    asl a
    clc
    adc mp_tmp+2
    sta mp_tmp+4                ; vx
    lda mp_mdy
    jsr mul_off
    sta mp_tmp+2
    lda mp_py
    asl a
    asl a
    asl a
    asl a
    clc
    adc mp_tmp+2
    sta mp_tmp+6                ; vy
    ; frame / bob
    lda mp_mpix
    beq @idle
    lda mp_walkt
    lsr a
    lsr a
    and #1
    bra @fb
@idle:
    lda mp_t
    DIVC 24
    and #1
@fb: sta mp_tmp                 ; frame (bob = -frame)
    ; shadow at the feet (vy + 13 - 1)
    lda mp_tmp+4
    sta spr_x
    lda mp_tmp+6
    clc
    adc #12
    sta spr_y
    lda #$20
    sta spr_fl
    lda #SP_SHADOW
    jsl scr_obj_put
    lda mp_tmp+4
    sta spr_x
    lda mp_tmp+6
    sec
    sbc #3
    sec
    sbc mp_tmp
    sta spr_y
    lda mp_face
    beq :+
    lda #1
:   ora #$20
    sta spr_fl
    lda mp_tmp
    beq :+
    lda #SP_WALK2
    bra :++
:   lda #SP_WALK1
:   jsl scr_obj_put
    jsr hud_cards
    ; popup sprites
    lda mp_pop
    cmp #POP_INV
    bne @noinv
    jsr inv_sprites
@noinv:
    lda mp_pop
    cmp #POP_MENU
    bne @nomenu
    lda mp_t
    lsr a
    lsr a
    lsr a
    and #3
    beq :+
    lda #72
    bra :++
:   lda #73
:   sta spr_x
    lda mp_menusel
    asl a
    asl a
    sta mp_tmp
    asl a
    clc
    adc mp_tmp
    adc #52
    sta spr_y
    lda #$30
    sta spr_fl
    lda #SP_CURSOR
    jsl scr_obj_put
@nomenu:
    rts

; A = direction (-1/0/1) -> A = direction * mp_tmp (the walk offset)
mul_off:
    beq @z
    bmi @n
    lda mp_tmp
    rts
@n: lda mp_tmp
    eor #$FFFF
    inc a
    rts
@z: rts

; item icons + the flashing selection box
inv_sprites:
    ldx ss_cur
    lda pp_nitems,x
    and #$00FF
    sta mp_nitems
    stz mp_tmp+2
@i: lda mp_tmp+2
    cmp mp_nitems
    jcs @d
    ; x = 20 + (i%7)*32, y = 124 + (i/7)*18
    ldx #7
    jsl scr_div
    sta mp_tmp+4
    txa
    asl a
    asl a
    asl a
    asl a
    asl a
    clc
    adc #20
    sta spr_x
    lda mp_tmp+4
    asl a
    sta mp_tmp+6
    asl a
    asl a
    asl a
    clc
    adc mp_tmp+6
    adc #124
    sta spr_y
    lda #$30
    sta spr_fl
    lda mp_tmp+2
    cmp mp_invsel
    bne :+
    lda spr_x
    pha
    lda spr_y
    pha
    sec
    sbc #2
    sta spr_y
    lda spr_x
    sec
    sbc #2
    sta spr_x
    lda #SP_INVBOX
    jsl scr_obj_put
    lda mp_t
    lsr a
    lsr a
    lsr a
    and #1
    tax
    lda #SP_INVBOX
    jsl scr_obj_pal
    pla
    sta spr_y
    pla
    sta spr_x
:   lda ss_cur
    clc
    adc mp_tmp+2
    tax
    lda pp_items,x
    and #$00FF
    dec a
    clc
    adc #SP_ITEM_MUSHROOM
    jsl scr_obj_put
    inc mp_tmp+2
    brl @i
@d: rts

; ------------------------------------------------------------------ popups + texts
; which popup shows (C#: intro card or message, then inventory, then menu on top)
popup:
    lda #POP_NONE
    ldx mp_intro
    beq :+
    lda #POP_INTRO
    bra :++
:   ldx mp_msgt
    beq :+
    ldx mp_msg
    beq :+
    lda #POP_MSG
:   ldx mp_inv
    beq :+
    lda #POP_INV
:   ldx mp_menu
    beq :+
    lda #POP_MENU
:   cmp mp_pop
    beq @same
    sta mp_pop
    ; BG2 window
    cmp #POP_NONE
    bne :+
    jsl scr_map_clear2
    bra @txt
:   cmp #POP_MENU
    bne :+
    jsr menu_count
    lda mp_maps+10
    ldx mp_mitems
    cpx #5
    bne @pm
    lda mp_maps+12
    bra @pm
:   asl a
    tax
    lda f:pop_map_ofs,x
    tax
    lda mp_maps,x
@pm: ldx #SCR_VRAM_BG2MAP
    jsl scr_map_put
    bra @txt
@same:
    lda mp_popdirty
    bne @txt
    rts
@txt:
    stz mp_popdirty
    ; clear the playfield text, draw the popup's
    lda #0
    sta txt_x
    sta txt_y
    lda #256
    sta txt_w
    lda #192
    sta txt_h
    jsl txt_clear
    lda mp_pop
    asl a
    tax
    jmp (pop_txt,x)
pop_map_ofs: .word 0, 6, 8, 14, 0
pop_txt: .addr pt_none, pt_intro, pt_msg, pt_inv, pt_menu
pt_none: rts
pt_intro:
    jsl sb_reset
    SB "WORLD "
    lda mp_world
    jsl sb_dec
    lda #72
    sta txt_y
    lda #TXP_MAP1_WHITE
    sta txt_pal
    jsl txt_printc_sb
    jsl sb_reset
    ldy #6
    lda [mp_p],y
    sta txt_spn
    jsr sb_far_name
    lda #88
    sta txt_y
    lda #TXP_MAP1_GOLD
    sta txt_pal
    jsl txt_printc_sb
    lda mp_msg
    and #$00FF
    beq :+
    jsr msg_to_sb
    lda #100
    sta txt_y
    lda #TXP_MAP1_GREEN
    sta txt_pal
    jsl txt_printc_sb
:   rts
pt_msg:
    jsr msg_to_sb
    lda #16
    sta txt_y
    lda #TXP_MAP1_WHITE
    sta txt_pal
    jsl txt_printc_sb
    rts
pt_inv:
    PRINT 16, 112, TXP_MAP1_GOLD, "ITEMS"
    ldx ss_cur
    lda pp_nitems,x
    and #$00FF
    bne :+
    PRINT 96, 140, TXP_MAP1_GREY, "NO ITEMS"
    rts
:   lda ss_cur
    clc
    adc mp_invsel
    tax
    lda pp_items,x
    and #$00FF
    jsl sb_reset
    jsr sb_item
    lda #64
    sta txt_x
    lda #112
    sta txt_y
    lda #TXP_MAP1_WHITE
    sta txt_pal
    jsl txt_print_sb
    rts
pt_menu:
    jsr menu_count
    stz mp_tmp+2
@l: lda mp_tmp+2
    cmp mp_mitems
    bcs @d
    jsl sb_reset
    lda mp_tmp+2
    jsr menu_id
    cmp #2
    bne @plain
    SB "WORLD < "
    lda mp_pick
    jsl sb_dec
    SB " >"
    bra @pr
@plain:
    asl a
    tax
    lda f:menu_strs,x
    tax
@c: lda f:menu_str0,x
    and #$00FF
    beq @pr
    phx
    jsl sb_char
    plx
    inx
    bra @c
@pr: lda #84
    sta txt_x
    lda mp_tmp+2
    asl a
    asl a
    sta mp_tmp
    asl a
    clc
    adc mp_tmp
    adc #52
    sta txt_y
    lda #TXP_MAP1_GREY
    ldx mp_tmp+2
    cpx mp_menusel
    bne :+
    lda #TXP_MAP1_WHITE
:   sta txt_pal
    jsl txt_print_sb
    inc mp_tmp+2
    bra @l
@d: rts
menu_strs: .word m0-menu_str0, m1-menu_str0, 0, m3-menu_str0, m4-menu_str0
menu_str0:
m0: .byte "CONTINUE", 0
m1: .byte "ITEMS", 0
m3: .byte "OPTIONS", 0
m4: .byte "SAVE & QUIT", 0

; the status bar texts (C# Hud.Draw: world, coins, lives, score)
; scr_hud_text: the status bar texts of any screen with the compact bar (world, coins, lives, score)
; (all those scenes use text palette 0 = white, 1 = gold)
.export scr_hud_text, scr_hud_cards
scr_hud_text:
    php
    rep #$30
    jsr hud_text
    plp
    rtl
scr_hud_cards:
    php
    rep #$30
    jsr hud_cards
    plp
    rtl
hud_text:
    lda #0
    sta txt_x
    lda #192
    sta txt_y
    lda #256
    sta txt_w
    lda #32
    sta txt_h
    jsl txt_clear
    jsl sb_reset
    lda sv_world
    and #$00FF
    jsl sb_dec
    lda #55
    sta txt_x
    lda #HUD_Y1
    sta txt_y
    lda #TXP_MAP1_GOLD
    sta txt_pal
    jsl txt_print_sb
    ldx ss_cur
    jsl sb_reset
    lda pp_coins,x
    and #$00FF
    ldx #2
    ldy #' '
    jsl sb_dec_pad
    lda #160
    sta txt_x
    lda #TXP_MAP1_WHITE
    sta txt_pal
    jsl txt_print_sb
    jsl sb_reset
    lda #'*'
    jsl sb_char
    ldx ss_cur
    lda pp_lives,x
    and #$00FF
    ldx #2
    ldy #' '
    jsl sb_dec_pad
    lda #22
    sta txt_x
    lda #HUD_Y2
    sta txt_y
    jsl txt_print_sb
    jsl sb_reset
    lda ss_cur
    clc
    adc #.loword(pp_score)
    tax
    jsl sb_bcd7
    lda #56
    sta txt_x
    jsl txt_print_sb
    rts

; re-queue the node blocks in their other state (after a water map swap)
requeue_nodes:
    stz mp_tmp+2
@l: lda mp_tmp+2
    cmp mp_nnodes
    bcs @d
    jsr nalt
    bcc @n
    lda mp_tmp+2
    jsr nrec
    lda mp_nd+7,x
    beq @n
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put
@n: inc mp_tmp+2
    bra @l
@d: lda ss_player
    beq :+
    lda mp_maps+4
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_put
:   rts

; ------------------------------------------------------------------ message strings (mp_msg, 0-terminated)
msg_reset:
    stz mp_msg
    stz mp_msglen
    rts
msg_char:
    ldx mp_msglen
    cpx #38
    bcs :+
    sep #$20
    .a8
    sta mp_msg,x
    stz mp_msg+1,x
    rep #$20
    .a16
    inc mp_msglen
:   rts
; append the 0-terminated string at X (in this bank)
msg_str:
    phb
    phk
    plb
@c: lda a:0,x
    and #$00FF
    beq @d
    phx
    jsr msg_char
    plx
    inx
    bra @c
@d: plb
    rts
.macro MSGS label, text
label:
    ldx #.loword(:+)
    jmp msg_str
:   .byte text, 0
.endmacro
MSGS msg_mario, "MARIO"
MSGS msg_luigi, "LUIGI"
MSGS msg_start, " START!"
MSGS msg_turn, "'S TURN"
MSGS msg_got, "GOT "
MSGS msg_lock, "A LOCK HAS OPENED!"
MSGS msg_nsp, "AN N-SPADE PANEL APPEARED!"
MSGS msg_starready, "STARMAN READY!"
MSGS msg_pwready, "P-WING READY!"
MSGS msg_cloud, "CLOUD: PASS THIS PANEL"
; A = item -> its name into the message
msg_item:
    asl a
    tax
    lda f:item_names,x
    tax
    jmp msg_str
; A = item -> its name into sb
sb_item:
    asl a
    tax
    lda f:item_names,x
    tax
    phb
    phk
    plb
@c: lda a:0,x
    and #$00FF
    beq @d
    phx
    jsl sb_char
    plx
    inx
    bra @c
@d: plb
    rts
item_names: .addr in0, in1, in2, in3, in4, in5, in6, in7, in8, in9
in0: .byte 0
in1: .byte "SUPER MUSHROOM", 0
in2: .byte "FIRE FLOWER", 0
in3: .byte "SUPER LEAF", 0
in4: .byte "STARMAN", 0
in5: .byte "P-WING", 0
in6: .byte "TANOOKI SUIT", 0
in7: .byte "FROG SUIT", 0
in8: .byte "HAMMER SUIT", 0
in9: .byte "CLOUD", 0
.export sb_item, item_names

msg_to_sb:
    jsl sb_reset
    ldx #0
@c: lda mp_msg,x
    and #$00FF
    beq @d
    phx
    jsl sb_char
    plx
    inx
    bra @c
@d: rts

; the map name (CODE7 string at txt_spn) into sb
sb_far_name:
    lda txt_spn
    sta mp_q
    sep #$20
    .a8
    lda #^scr_mapdefs
    sta mp_q+2
    rep #$20
    .a16
    ldy #0
@c: lda [mp_q],y
    and #$00FF
    beq @d
    phy
    jsl sb_char
    ply
    iny
    bra @c
@d: rts
.segment "BSS"
mp_msglen: .res 2
txt_spn: .res 2

.segment "CODE12"
.a16
.i16
; the goal cards in the status bar (C# Hud.Draw: x = 188 + 22 i + 2)
hud_cards:
    ldx ss_cur
    lda pp_ncards,x
    and #$00FF
    sta hc_n
    stz hc_i
@cd: lda hc_i
    cmp hc_n
    bcs @d
    asl a
    sta hc_t
    asl a
    asl a
    clc
    adc hc_t
    asl a
    adc hc_t                    ; 22 i
    adc #190
    sta spr_x
    lda #HUD_CARD_Y
    sta spr_y
    lda #$30
    sta spr_fl
    lda ss_cur
    clc
    adc hc_i
    tax
    lda pp_cards,x
    and #$00FF
    clc
    adc #SP_CARD0
    jsl scr_obj_put
    inc hc_i
    bra @cd
@d: rts
.segment "BSS"
hc_n: .res 2
hc_i: .res 2
hc_t: .res 2
.segment "CODE12"
.a16
.i16
; map_node_items: A = node -> A = its Toad-house item mask (bit IT_*)
.export map_node_items
map_node_items:
    php
    rep #$30
    jsr nrec
    lda mp_nd+9,x
    plp
    rtl
