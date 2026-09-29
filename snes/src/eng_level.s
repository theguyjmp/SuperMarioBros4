; Engine: level/area loading, runtime grids, BG1 metatile streaming, tile changes, animated tiles, VRAM queues.
; Owner: engine agent. Port of World.LoadArea / WorldRender (data made by src/Tools/Snes/SnesLevels.cs).
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"
.include "spr_ids.inc"

.import eng_lvl_dir, eng_ts_dir
.import bg_load, bg_set_water, spr_level_load, spr_area, bg_hdma_off, bg_flat_color, bg_area_h
.export spr_loaded
.export eng_nmi_level, cur_ts

BG1_MAP = $4000

.segment "ZEROPAGE"
e_t0: .res 2
e_t1: .res 2
e_t2: .res 2
e_t3: .res 2
e_t4: .res 2
e_t5: .res 2
e_t6: .res 2
e_t7: .res 2
e_p0: .res 3
e_p1: .res 3
e_p2: .res 3
e_tx: .res 2
e_ty: .res 2

.segment "BSS"
lvl_ptr: .res 3
lvl_world: .res 2
lvl_kind: .res 2
lvl_time_def: .res 2
lvl_start_area: .res 2
lvl_start_x: .res 2
lvl_start_y: .res 2
lvl_nareas: .res 2
lvl_nlinks: .res 2
area_idx: .res 2
area_ptr: .res 3
area_w: .res 2
area_h: .res 2
area_ts: .res 2
area_bgtheme: .res 2
area_backdrop: .res 2
area_music: .res 2
area_scroll: .res 2
area_autospeed: .res 2
area_water: .res 2          ; water row or $FFFF
area_goalx: .res 2          ; goal tile x or $FFFF
area_goaly: .res 2
area_slopes: .res 2
area_ice: .res 2
area_flat: .res 2           ; bg= backdrop (BGR555)
area_link_ptr: .res 3
area_nlinkm: .res 2
area_spawn_ptr: .res 3
area_nspawn: .res 2
spr_loaded: .res 2         ; level whose sprite set is in VRAM ($FFFF none)
cur_ts: .res 2              ; tileset currently in VRAM ($FFFF none)
ts_ptr: .res 3
rowoff: .res 256            ; ty*W for ty 0..127
cam_x: .res 2
cam_y: .res 2
prev_cam_x: .res 2
prev_cam_y: .res 2
str_c0: .res 2              ; streaming window: metatile columns [c0, c0+31], rows [r0, r0+15]
str_r0: .res 2
col_pend: .res 2            ; NMI: column buffer valid (VRAM address of left tile column)
row_pend: .res 2            ; NMI: row buffer valid (VRAM address of the left screen's upper tile row)
tq_n: .res 2                ; tile queue entries
tq_addr: .res 2*24
tq_w0: .res 2*24
tq_w1: .res 2*24
tq_w2: .res 2*24
tq_w3: .res 2*24
; animated tiles
an_n: .res 2
an_vram: .res 2*12
an_count: .res 2*12
an_shift: .res 2*12
an_src: .res 3*12
an_cur: .res 2*12
aq_n: .res 2                ; anim upload queue (slot numbers)
aq_slot: .res 2*12
aq_frame: .res 2*12
dyn_tbl: .res 32
hidden_t: .res 2*8          ; bumped (hidden) cells: timer, x, y
hidden_x: .res 2*8
hidden_y: .res 2*8
redraw_n: .res 2
ps_end: .res 2

.segment "HIBSS"
mt_ram: .res 2048
colbuf: .res 128            ; left tile column (32 words) then right tile column (32 words)
rowbuf: .res 256            ; upper tile row (64 words) then lower tile row (64 words)

.segment "EXBSS"
lvl_tiles: .res 8192
lvl_gfx: .res 8192
lvl_cont: .res 8192
lvl_var: .res 8192
lvl_psw: .res 8192           ; P-switch swap marks (1 brick->coin, 2 coin->brick, 3 muncher->coin)

.segment "CODE1"
.a16
.i16

; ================================================================== level / area loading
; eng_load_level: A16 = level index. Reads the level header (does not load an area).
.a16
.i16
eng_load_level:
    sta e_t0
    asl a
    clc
    adc e_t0                    ; *3
    tax
    lda f:eng_lvl_dir,x
    sta lvl_ptr
    sep #$20
    lda f:eng_lvl_dir+2,x
    sta lvl_ptr+2
    rep #$20
    lda lvl_ptr
    sta e_p0
    lda lvl_ptr+1
    sta e_p0+1
    ldy #0
    lda [e_p0],y
    and #$00FF
    sta lvl_world
    iny
    lda [e_p0],y
    and #$00FF
    sta lvl_kind
    iny
    lda [e_p0],y
    sta lvl_time_def
    iny
    iny
    lda [e_p0],y
    and #$00FF
    sta lvl_start_area
    iny
    lda [e_p0],y
    and #$00FF
    sta lvl_start_x
    iny
    lda [e_p0],y
    and #$00FF
    sta lvl_start_y
    iny
    lda [e_p0],y
    and #$00FF
    sta lvl_nareas
    iny
    lda [e_p0],y
    and #$00FF
    sta lvl_nlinks
    rtl

; Finds link (fromArea=area_idx, fromId=A) -> A = toArea, X = toId; carry set if found.
eng_find_link:
    sta e_t0
    lda lvl_ptr
    sta e_p0
    lda lvl_ptr+1
    sta e_p0+1
    lda lvl_nareas
    asl a
    adc lvl_nareas
    adc #33
    tay                         ; links start after the area pointers
    ldx lvl_nlinks
    beq @no
@l: lda [e_p0],y
    and #$00FF
    cmp area_idx
    bne @next
    iny
    lda [e_p0],y
    dey
    and #$00FF
    cmp e_t0
    bne @next
    iny
    iny
    lda [e_p0],y
    and #$00FF
    pha
    iny
    lda [e_p0],y
    and #$00FF
    tax
    pla
    sec
    rtl
@next:
    iny
    iny
    iny
    iny
    dex
    bne @l
@no: clc
    rtl

; eng_load_area: A16 = area index. Forced blank must be on. Loads grids, tileset, backgrounds, sprites.
eng_load_area:
    sta area_idx
    sep #$20
    jsl bg_hdma_off
    rep #$30
    lda area_idx
    ; area header pointer = [lvl_ptr + 33 + 3*idx]
    asl a
    adc area_idx
    adc #33
    tay
    lda lvl_ptr
    sta e_p0
    lda lvl_ptr+1
    sta e_p0+1
    lda [e_p0],y
    sta area_ptr
    iny
    lda [e_p0],y
    sta area_ptr+1
    lda area_ptr
    sta e_p1
    lda area_ptr+1
    sta e_p1+1
    ldy #0
    jsr rd8
    sta area_w
    jsr rd8
    sta area_h
    jsr rd8
    sta area_ts
    jsr rd8
    sta area_bgtheme
    jsr rd8
    sta area_backdrop
    jsr rd8
    sta area_music
    jsr rd8
    sta area_scroll
    jsr rd8
    sta area_autospeed
    jsr rd8
    cmp #$FF
    bne :+
    lda #$FFFF
:   sta area_water
    jsr rd8
    cmp #$FF
    bne :+
    lda #$FFFF
:   sta area_goalx
    jsr rd8
    sta area_goaly
    jsr rd8
    sta area_slopes
    jsr rd8
    sta area_ice
    lda [e_p1],y
    sta area_flat
    iny
    iny
    ; tiles LZ -> lvl_tiles
    lda [e_p1],y
    sta e_p0
    iny
    lda [e_p1],y
    sta e_p0+1
    iny
    iny
    phy
    jsr area_size
    ldx #.loword(lvl_tiles)
    jsr lz_decomp
    ply
    lda [e_p1],y
    sta e_p0
    iny
    lda [e_p1],y
    sta e_p0+1
    iny
    iny
    phy
    ldx #.loword(lvl_gfx)
    jsr lz_decomp
    ; clear contents / var grids
    ldx #0
    lda #0
@clr: sta f:lvl_cont,x
    sta f:lvl_var,x
    inx
    inx
    cpx #8192
    bcc @clr
    ply
    ; contents: count, (index16, byte)*
    lda [e_p1],y
    iny
    iny
    tax
    beq @cdone
@cl: lda [e_p1],y
    sta e_t0
    iny
    iny
    lda [e_p1],y
    iny
    phx
    ldx e_t0
    sep #$20
    sta f:lvl_cont,x
    rep #$20
    plx
    dex
    bne @cl
@cdone:
    lda [e_p1],y
    iny
    iny
    tax
    beq @vdone
@vl: lda [e_p1],y
    sta e_t0
    iny
    iny
    lda [e_p1],y
    iny
    phx
    ldx e_t0
    sep #$20
    sta f:lvl_var,x
    rep #$20
    plx
    dex
    bne @vl
@vdone:
    ; link markers: count8, (x,y,id)*
    lda [e_p1],y
    and #$00FF
    sta area_nlinkm
    iny
    tya
    clc
    adc e_p1
    sta area_link_ptr
    sep #$20
    lda e_p1+2
    sta area_link_ptr+2
    rep #$20
    lda area_nlinkm
    asl a
    adc area_nlinkm
    sta e_t0
    tya
    clc
    adc e_t0
    tay
    ; spawns: count16, (code,x,y)*
    lda [e_p1],y
    sta area_nspawn
    iny
    iny
    tya
    clc
    adc e_p1
    sta area_spawn_ptr
    sep #$20
    lda e_p1+2
    sta area_spawn_ptr+2
    rep #$20
    ; row offsets
    ldx #0
    lda #0
@ro: sta rowoff,x
    clc
    adc area_w
    inx
    inx
    cpx #256
    bcc @ro
    jsr load_tileset
    ; backgrounds: water line = WaterRow*16 + (WaterRow > 0 ? 16 : 0), flat color, area height, then bg_load
    lda area_water
    cmp #$FFFF
    beq @nw
    asl a
    asl a
    asl a
    asl a
    beq @nw
    clc
    adc #16
@nw: tax
    sep #$20
    jsl bg_set_water
    rep #$30
    lda area_flat
    sta bg_flat_color
    lda area_h
    asl a
    asl a
    asl a
    asl a
    sta bg_area_h
    sep #$20
    lda area_bgtheme
    ldx area_backdrop
    jsl bg_load
    ; BG1 is masked by the backgrounds module's window 1 too (it covers lines 192+: the HUD)
    lda #$02
    sta W12SEL
    lda #$11
    sta TMW
    rep #$30
    lda g_level
    cmp spr_loaded
    beq :+
    sta spr_loaded
    sep #$20
    jsl spr_level_load
    rep #$30
:   sep #$20
    lda area_idx
    jsl spr_area
    rep #$30
    stz tq_n
    stz aq_n
    stz col_pend
    stz row_pend
    ldx #14
:   stz hidden_t,x
    dex
    dex
    bpl :-
    rtl

; reads one byte [e_p1],y -> A16 (zero-extended), y++
rd8:
    lda [e_p1],y
    and #$00FF
    iny
    rts

; e_t7 = area_w * area_h (output size)
area_size:
    lda #0
    ldx area_h
    beq @d
@m: clc
    adc area_w
    dex
    bne @m
@d: sta e_t7
    rts

; ------------------------------------------------------------------ LZ decompression: [e_p0] -> $7F:X, e_t7 bytes
; format (SnesLevels.Lz): c<$80: c+1 literals; c>=$80: copy (c&$7F)+3 bytes from distance (16-bit) back.
lz_decomp:
    phb
    sep #$20
    lda #$7F
    pha
    plb
    rep #$20
    txa
    clc
    adc e_t7
    sta e_t6                    ; end address
@loop:
    cpx e_t6
    bcs @done
    lda [e_p0]
    inc e_p0
    and #$00FF
    cmp #$80
    bcs @match
    inc a
    sta e_t5
    sep #$20
@lit: lda [e_p0]
    sta a:$0000,x
    rep #$20
    inc e_p0
    sep #$20
    inx
    dec e_t5
    bne @lit
    rep #$20
    bra @loop
@match:
    and #$7F
    clc
    adc #3
    sta e_t5
    lda [e_p0]
    inc e_p0
    inc e_p0
    sta e_t4
    txa
    sec
    sbc e_t4
    tay
    sep #$20
@cp: lda a:$0000,y
    sta a:$0000,x
    inx
    iny
    dec e_t5
    bne @cp
    rep #$20
    bra @loop
@done:
    plb
    rts

; ------------------------------------------------------------------ tileset: CHR, metatiles, palettes, anims
load_tileset:
    lda area_ts
    asl a
    adc area_ts
    tax
    lda f:eng_ts_dir,x
    sta ts_ptr
    lda f:eng_ts_dir+1,x
    sta ts_ptr+1
    lda ts_ptr
    sta e_p1
    lda ts_ptr+1
    sta e_p1+1
    ; metatile table -> mt_ram (always: cheap and keeps state simple)
    ldy #5
    lda [e_p1],y
    sta e_p0
    iny
    lda [e_p1],y
    sta e_p0+1
    ldy #8
    lda [e_p1],y
    asl a
    asl a
    asl a
    tax                         ; bytes
    ldy #0
    sep #$20
@mt: lda [e_p0],y
    phx
    tyx
    sta f:mt_ram,x
    plx
    iny
    dex
    bne @mt
    rep #$20
    ; dyn table
    ldy #138
    ldx #0
@dy: lda [e_p1],y
    sep #$20
    sta dyn_tbl,x
    rep #$20
    iny
    inx
    cpx #32
    bcc @dy
    ; anim slots
    ldy #170
    lda [e_p1],y
    and #$00FF
    sta an_n
    iny
    stz e_t0                    ; slot
@an: lda e_t0
    cmp an_n
    bcs @andone
    asl a
    tax
    lda [e_p1],y
    sta an_vram,x
    iny
    iny
    lda [e_p1],y
    and #$00FF
    sta an_count,x
    iny
    lda [e_p1],y
    and #$00FF
    sta an_shift,x
    iny
    lda #$FFFF
    sta an_cur,x
    lda e_t0
    asl a
    adc e_t0
    tax                         ; 3*slot
    lda [e_p1],y
    sta an_src,x
    iny
    lda [e_p1],y
    sta an_src+1,x
    iny
    iny
    inc e_t0
    bra @an
@andone:
    lda area_ts
    cmp cur_ts
    beq @same
    sta cur_ts
    ; CHR -> VRAM $0000 (DMA channel 0, forced blank)
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    stz VMADDL
    ldy #0
    lda [e_p1],y
    sta A1T0L
    ldy #2
    lda [e_p1],y
    sep #$20
    sta A1B0
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    ldy #3
    lda [e_p1],y
    sta DAS0L
    sep #$20
    lda #$01
    sta MDMAEN
    rep #$20
@same:
    ; palettes -> CGRAM colors 32..95 (BG palettes 2-5), slot 0 of each skipped
    ldx #0
@pal: txa
    and #15
    beq @pskip
    sep #$20
    txa
    clc
    adc #32
    sta CGADD
    rep #$20
    txa
    asl a
    adc #10
    tay
    lda [e_p1],y
    sep #$20
    sta CGDATA
    xba
    sta CGDATA
    rep #$20
@pskip:
    inx
    cpx #64
    bcc @pal
    rts

; ================================================================== tile access
; eng_tile_at_px: A = px, Y = py (signed pixels) -> A = tile type T. (World.TileAtPx)
eng_tile_at_px:
    ASR4
    sta e_tx
    tya
    ASR4
    sta e_ty
; eng_tile_at: e_tx, e_ty (signed tile coords) -> A = T, X = grid index (if inside). (World.TileAt)
eng_tile_at:
    lda e_tx
    bmi @outx
    cmp area_w
    bcs @outx
    lda e_ty
    bmi @empty
    cmp area_h
    bcs @empty
    asl a
    tax
    lda rowoff,x
    clc
    adc e_tx
    tax
    lda f:lvl_tiles,x
    and #$00FF
    rtl
@outx:
    lda e_ty
    bmi @empty
    lda #T_BARRIER
    rtl
@empty:
    lda #T_EMPTY
    rtl

; eng_link_at: e_tx,e_ty -> A = link id or $FFFF
eng_link_at:
    lda area_link_ptr
    sta e_p2
    lda area_link_ptr+1
    sta e_p2+1
    ldx area_nlinkm
    beq @none
    ldy #0
@l: lda [e_p2],y
    and #$00FF
    cmp e_tx
    bne @n
    iny
    lda [e_p2],y
    dey
    and #$00FF
    cmp e_ty
    bne @n
    iny
    iny
    lda [e_p2],y
    and #$00FF
    rtl
@n: iny
    iny
    iny
    dex
    bne @l
@none:
    lda #$FFFF
    rtl

; bg class of cell (e_tx, e_ty) -> A (0 sky, 1 water top, 2 water, 3 backstage)
bg_class:
    lda area_water
    bmi @nw
    lda e_ty
    cmp area_water
    bcc @nw
    bne @w2
    lda area_water
    beq @w2
    lda #1
    rts
@w2: lda #2
    rts
@nw:
    lda area_goalx
    bmi @sky
    dec a
    dec a
    sta e_t0
    lda e_tx
    sec
    sbc e_t0
    bmi @sky
    lda area_goaly
    sec
    sbc #12
    sta e_t0
    lda e_ty
    sec
    sbc e_t0
    bmi @sky
    cmp #16
    bcs @sky
    lda #3
    rts
@sky: lda #0
    rts

; eng_set_tile: e_tx, e_ty (inside the area), A = new T. Updates the collision + metatile grids and the screen.
; (World.SetTile + the renderer's view of it)
eng_set_tile:
    sta e_t2
    lda e_tx
    bmi @out
    cmp area_w
    bcs @out
    lda e_ty
    bmi @out
    cmp area_h
    bcs @out
    asl a
    tax
    lda rowoff,x
    clc
    adc e_tx
    tax
    sep #$20
    lda e_t2
    sta f:lvl_tiles,x
    rep #$20
    jsr dyn_metatile            ; A = metatile or $FFFF (unchanged)
    bmi @nochg
    sep #$20
    sta f:lvl_gfx,x
    rep #$20
@nochg:
    jsr refresh_cell
    ; a vine below this cell may change from vine.top to vine
    lda e_t2
    cmp #T_VINE
    bne @out
    inc e_ty
    jsl eng_tile_at
    cmp #T_VINE
    bne @nv
    jsr dyn_metatile
    sep #$20
    sta f:lvl_gfx,x
    rep #$20
    jsr refresh_cell
@nv:
    dec e_ty
@out:
    rtl

; X = grid index of (e_tx,e_ty), e_t2 = T -> A = runtime metatile for dynamic tile types, else $FFFF
dyn_metatile:
    phx
    lda e_t2
    ldy #DYN_EMPTY
    cmp #T_EMPTY
    beq @dyn
    ldy #DYN_COIN
    cmp #T_COIN
    beq @dyn
    ldy #DYN_BRICK
    cmp #T_BRICK
    beq @dyn
    ldy #DYN_USED
    cmp #T_USED
    beq @dyn
    ldy #DYN_MUNCHER
    cmp #T_MUNCHER
    beq @dyn
    ldy #DYN_QBLOCK
    cmp #T_QBLOCK
    beq @dyn
    cmp #T_VINE
    bne @none
    ; vine: "vine" if the tile above is a vine, else "vine.top"
    lda e_ty
    dec a
    bmi @top
    asl a
    tax
    lda rowoff,x
    clc
    adc e_tx
    tax
    lda f:lvl_tiles,x
    and #$00FF
    cmp #T_VINE
    bne @top
    ldy #DYN_VINE
    bra @dyn
@top: ldy #DYN_VINETOP
@dyn:
    sty e_t3
    jsr bg_class
    asl a
    asl a
    asl a
    clc
    adc e_t3
    tax
    lda dyn_tbl,x
    and #$00FF
    sta e_t3
    plx
    lda e_t3
    rts
@none:
    plx
    lda #$FFFF
    rts

; queue a VRAM refresh of cell (e_tx,e_ty) from lvl_gfx if it is inside the streaming window
refresh_cell:
    lda e_tx
    bmi @no
    cmp area_w
    bcs @no
    lda e_ty
    bmi @no
    cmp area_h
    bcs @no
    asl a
    tax
    lda rowoff,x
    clc
    adc e_tx
    tax
    lda f:lvl_gfx,x
    and #$00FF
    jmp queue_cell
@no: rts

; queue metatile A for cell (e_tx,e_ty) if inside the window
queue_cell:
    sta e_t3
    lda e_tx
    sec
    sbc str_c0
    bmi @no
    cmp #32
    bcs @no
    lda e_ty
    sec
    sbc str_r0
    bmi @no
    cmp #16
    bcs @no
    lda tq_n
    cmp #24
    bcs @no
    asl a
    tax
    ; VRAM address of the top-left tile
    lda e_tx
    and #31
    asl a
    sta e_t4                    ; tile column 0..62
    lda e_ty
    and #15
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a                       ; row*2*32
    clc
    adc e_t4
    sta e_t5
    lda e_t4
    and #32
    beq :+
    lda e_t5
    clc
    adc #$400-32
    sta e_t5
:   lda e_t5
    clc
    adc #BG1_MAP
    sta tq_addr,x
    stx e_t6                    ; queue slot*2
    lda e_t3
    asl a
    asl a
    asl a
    tax
    ldy e_t6
    lda f:mt_ram,x
    sta tq_w0,y
    lda f:mt_ram+2,x
    sta tq_w1,y
    lda f:mt_ram+4,x
    sta tq_w2,y
    lda f:mt_ram+6,x
    sta tq_w3,y
    inc tq_n
@no: rts

; eng_hide_tile: e_tx,e_ty: the cell shows its empty background while a bumped block is drawn as a sprite
; (C# World.Hidden[] set by BumpBlock). Up to 8 cells.
eng_hide_tile:
    ldx #0
@f: lda hidden_t,x
    beq @got
    inx
    inx
    cpx #16
    bcc @f
    rtl
@got:
    lda #1
    sta hidden_t,x
    lda e_tx
    sta hidden_x,x
    lda e_ty
    sta hidden_y,x
.ifdef SPR_BUMP_USED
    lda #T_EMPTY
    sta e_t2
    jsr dyn_metatile
    jsr queue_cell
.endif
    rtl

; eng_show_tile: e_tx,e_ty: end of the bump; re-show the cell from the grid
eng_show_tile:
    ldx #0
@f: lda hidden_t,x
    beq @n
    lda hidden_x,x
    cmp e_tx
    bne @n
    lda hidden_y,x
    cmp e_ty
    bne @n
    stz hidden_t,x
@n: inx
    inx
    cpx #16
    bcc @f
    jsr refresh_cell
    rtl

; eng_is_hidden: e_tx,e_ty -> carry set if the cell is hidden (being bumped)
eng_is_hidden:
    ldx #0
@f: lda hidden_t,x
    beq @n
    lda hidden_x,x
    cmp e_tx
    bne @n
    lda hidden_y,x
    cmp e_ty
    bne @n
    sec
    rtl
@n: inx
    inx
    cpx #16
    bcc @f
    clc
    rtl

; ================================================================== streaming
; ================================================================== P-switch (World.StartPSwitch / EndPSwitch)
; eng_pswitch_swap: A = 0 start (swap and mark), 1 end (swap back marked cells); then redraws the window over 32 ticks

eng_pswitch_swap:
    sta ps_end
    stz e_ty
@row: lda e_ty
    cmp area_h
    jcs @done
    stz e_tx
@col: lda e_tx
    cmp area_w
    jcs @nrow
    jsl eng_tile_at             ; X = index
    sta e_t2
    lda ps_end
    bne @end
    ; start: clear the mark, swap
    sep #$20
    lda #0
    sta f:lvl_psw,x
    rep #$20
    lda e_t2
    cmp #T_BRICK
    bne @nb
    lda f:lvl_cont,x
    and #$00FF
    bne @next
    lda #1
    ldy #T_COIN
    bra @set
@nb: cmp #T_COIN
    bne @nc
    lda #2
    ldy #T_BRICK
    bra @set
@nc: cmp #T_MUNCHER
    bne @next
    lda #3
    ldy #T_COIN
@set:
    sep #$20
    sta f:lvl_psw,x
    rep #$20
    tya
    jsr set_tile_quiet
    bra @next
@end:
    lda f:lvl_psw,x
    and #$00FF
    beq @next
    cmp #1
    bne @e2
    lda e_t2
    cmp #T_COIN
    bne @next
    lda #T_BRICK
    jsr set_tile_quiet
    bra @next
@e2: cmp #2
    bne @e3
    lda e_t2
    cmp #T_BRICK
    bne @next
    lda #T_COIN
    jsr set_tile_quiet
    bra @next
@e3: lda e_t2
    cmp #T_COIN
    bne @next
    lda #T_MUNCHER
    jsr set_tile_quiet
@next:
    inc e_tx
    brl @col
@nrow:
    inc e_ty
    brl @row
@done:
    lda #32
    sta redraw_n
    rtl

; A = T for cell (e_tx, e_ty) with X = grid index: tile + metatile grids only (no VRAM queue)
set_tile_quiet:
    sta e_t2
    phx
    sep #$20
    sta f:lvl_tiles,x
    rep #$20
    jsr dyn_metatile
    bmi :+
    sep #$20
    sta f:lvl_gfx,x
    rep #$20
:   plx
    rts

; eng_draw_all: forced blank; draws the whole 32x16 metatile window around the camera directly to VRAM.
eng_draw_all:
    jsr window_target
    lda e_t0
    sta str_c0
    lda e_t1
    sta str_r0
    lda #0
@c: pha
    clc
    adc str_c0
    jsr build_column
    jsr dma_column
    pla
    inc a
    cmp #32
    bcc @c
    stz col_pend
    stz row_pend
    rtl

; e_t0 = target c0, e_t1 = target r0
window_target:
    lda cam_x
    ASR4
    dec a
    sta e_t0
    lda cam_y
    ASR4
    dec a
    sta e_t1
    rts

; eng_stream: once per tick after the camera moved: queue at most one column and one row
eng_stream:
    jsr window_target
    lda col_pend
    bne @rows
    ; a pending full redraw (P-switch) rebuilds one window column per tick
    lda redraw_n
    beq @noredraw
    lda str_c0
    cmp e_t0
    bne @noredraw
    dec redraw_n
    lda str_c0
    clc
    adc redraw_n
    jsr build_column
    bra @rows
@noredraw:
    lda col_pend
    bne @rows
    lda str_c0
    cmp e_t0
    beq @rows
    bpl @left
    ; camera moved right: new column c0+32
    clc
    adc #32
    jsr build_column
    inc str_c0
    bra @rows
@left:
    dec a
    sta str_c0
    jsr build_column
@rows:
    lda row_pend
    bne @done
    lda str_r0
    cmp e_t1
    beq @done
    bpl @up
    clc
    adc #16
    jsr build_row
    inc str_r0
    bra @done
@up:
    dec a
    sta str_r0
    jsr build_row
@done:
    rtl

; A = metatile column mx: fill colbuf for rows str_r0..str_r0+15, set col_pend = VRAM address
build_column:
    sta e_t2                    ; mx
    and #31
    asl a
    sta e_t3                    ; tile column in 64-wide map
    and #32
    beq :+
    lda #$400-32
:   clc
    adc e_t3
    adc #BG1_MAP
    sta e_t4                    ; VRAM address of the column (row 0)
    lda str_r0
    sta e_t5                    ; my
    ldy #16
@r: ; metatile id
    lda e_t2
    bmi @blank
    cmp area_w
    bcs @blank
    lda e_t5
    bmi @blank
    cmp area_h
    bcs @blank
    asl a
    tax
    lda rowoff,x
    clc
    adc e_t2
    tax
    lda f:lvl_gfx,x
    and #$00FF
    bra @have
@blank: lda #0
@have:
    asl a
    asl a
    asl a
    tax
    lda e_t5
    and #15
    asl a
    asl a
    sta e_t6                    ; byte offset of row pair in a 32-word column
    ; (x = mt offset) copy words: TL->col[2r], BL->col[2r+1], TR->col2[2r], BR->col2[2r+1]
    lda f:mt_ram,x
    pha
    lda f:mt_ram+2,x
    pha
    lda f:mt_ram+4,x
    pha
    lda f:mt_ram+6,x
    ldx e_t6
    sta f:colbuf+64+2,x         ; BR
    pla
    sta f:colbuf+2,x            ; BL
    pla
    sta f:colbuf+64,x           ; TR
    pla
    sta f:colbuf,x              ; TL
    inc e_t5
    dey
    bne @r
    lda e_t4
    sta col_pend
    rts

; DMA the column buffer now (forced blank or NMI). A16 in/out.
dma_column:
    sep #$20
    lda #$81                    ; increment by 32 after the high byte
    sta VMAIN
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    lda #$7E
    sta A1B0
    rep #$20
    lda col_pend
    sta VMADDL
    lda #.loword(colbuf)
    sta A1T0L
    lda #64
    sta DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    lda col_pend
    inc a
    sta VMADDL
    lda #.loword(colbuf+64)
    sta A1T0L
    lda #64
    sta DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    rts

; A = metatile row my: fill rowbuf for columns str_c0..str_c0+31
build_row:
    sta e_t2                    ; my
    and #15
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    clc
    adc #BG1_MAP
    sta e_t4                    ; VRAM address of the upper tile row (left screen)
    lda str_c0
    sta e_t5                    ; mx
    ldy #32
    ; row base index
    lda e_t2
    bmi @allblank
    cmp area_h
    bcs @allblank
    asl a
    tax
    lda rowoff,x
    sta e_t7
    bra @c
@allblank:
    lda #$FFFF
    sta e_t7
@c: lda e_t7
    bmi @blank
    lda e_t5
    bmi @blank
    cmp area_w
    bcs @blank
    clc
    adc e_t7
    tax
    lda f:lvl_gfx,x
    and #$00FF
    bra @have
@blank: lda #0
@have:
    asl a
    asl a
    asl a
    tax
    lda e_t5
    and #31
    asl a
    asl a
    sta e_t6                    ; byte offset of the column pair
    lda f:mt_ram,x
    pha
    lda f:mt_ram+2,x
    pha
    lda f:mt_ram+4,x
    pha
    lda f:mt_ram+6,x
    ldx e_t6
    sta f:rowbuf+128+2,x        ; BR
    pla
    sta f:rowbuf+128,x          ; BL
    pla
    sta f:rowbuf+2,x            ; TR
    pla
    sta f:rowbuf,x              ; TL
    inc e_t5
    dey
    bne @c
    lda e_t4
    sta row_pend
    rts

; ================================================================== animated tiles
; eng_anim_tiles: queue CHR frame uploads for animated slots whose frame changed (max 4 per tick)
eng_anim_tiles:
    ldx #0
    stz aq_n
@s: txa
    lsr a
    cmp an_n
    bcs @done
    lda aq_n
    cmp #4
    bcs @done
    lda w_frame
    ldy an_shift,x
@sh: dey
    bmi @shd
    lsr a
    bra @sh
@shd:
    sta e_t0
    lda an_count,x
    dec a
    and e_t0
    ; conveyors stand still during a P-switch (not ported: fine)
    cmp an_cur,x
    beq @n
    sta an_cur,x
    pha
    lda aq_n
    asl a
    tay
    pla
    sta aq_frame,y
    txa
    lsr a
    sta aq_slot,y
    inc aq_n
@n: inx
    inx
    bra @s
@done:
    rtl

; force re-upload of every slot (after a load)
.export eng_anim_reset
eng_anim_reset:
    ldx #22
    lda #$FFFF
:   sta an_cur,x
    dex
    dex
    bpl :-
    rtl

; ================================================================== NMI part (called by eng_nmi in game.s; A8 XY16 on entry)
.a8
eng_nmi_level:
    rep #$20
    .a16
    lda col_pend
    beq @nocol
    jsr dma_column
    stz col_pend
@nocol:
    lda row_pend
    beq @norow
    sep #$20
    lda #$80
    sta VMAIN
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    lda #$7E
    sta A1B0
    rep #$20
    ; upper row: left screen, right screen; lower row: same
    lda row_pend
    sta VMADDL
    lda #.loword(rowbuf)
    ldx #64
    jsr dma_now
    lda row_pend
    clc
    adc #$400
    sta VMADDL
    lda #.loword(rowbuf+64)
    ldx #64
    jsr dma_now
    lda row_pend
    clc
    adc #32
    sta VMADDL
    lda #.loword(rowbuf+128)
    ldx #64
    jsr dma_now
    lda row_pend
    clc
    adc #$400+32
    sta VMADDL
    lda #.loword(rowbuf+192)
    ldx #64
    jsr dma_now
    stz row_pend
@norow:
    ; tile queue
    sep #$20
    lda #$80
    sta VMAIN
    rep #$20
    ldx #0
@tq: txa
    lsr a
    cmp tq_n
    bcs @tqd
    lda tq_addr,x
    sta VMADDL
    lda tq_w0,x
    sta VMDATAL
    lda tq_w1,x
    sta VMDATAL
    lda tq_addr,x
    clc
    adc #32
    sta VMADDL
    lda tq_w2,x
    sta VMDATAL
    lda tq_w3,x
    sta VMDATAL
    inx
    inx
    bra @tq
@tqd:
    stz tq_n
    ; animated CHR
    ldx #0
@aq: txa
    lsr a
    cmp aq_n
    bcs @aqd
    phx
    lda aq_slot,x
    asl a
    tay                         ; y = slot*2
    lda an_vram,y
    sta VMADDL
    lda aq_frame,x
    xba                         ; *256
    lsr a                       ; *128
    sta e_t0
    tya
    lsr a
    sta e_t1
    asl a
    adc e_t1
    tax                         ; slot*3
    lda an_src,x
    clc
    adc e_t0
    sta A1T0L
    sep #$20
    lda an_src+2,x
    sta A1B0
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    lda #128
    sta DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    plx
    inx
    inx
    bra @aq
@aqd:
    stz aq_n
    sep #$20
    rtl

.a16
; DMA A = source (bank already in A1B0), X = size, to VMDATA (channel 0 set up)
dma_now:
    sta A1T0L
    stx DAS0L
    sep #$20
    lda #1
    sta MDMAEN
    rep #$20
    rts
