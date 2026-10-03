; Sprites module (owner: sprites agent). OAM buffer, metasprites, per-level OBJ sets, dynamic player frames.
; API + ids: snes/SPRITES.md and gen/spr_ids.inc. Data: gen/spr_data.s (SnesSprites.cs).
;
; OAM order: later spr_meta/spr_player calls get LOWER OAM indices (= drawn in front), matching the C# draw order.
; Allocation walks down from spr_start; when the PPU reported a >32 sprites / >34 slivers line last frame
; (STAT77), spr_start rotates each frame so the dropped sprites change (flicker instead of vanishing).
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "spr_ids.inc"

.export spr_nmi, spr_begin, spr_meta, spr_player, spr_level_load, spr_end, spr_area, spr_meta_size
.export spr_arg_id, spr_arg_x, spr_arg_y, spr_arg_flags, spr_pbase, spr_pslot
.import spr_level_tab, spr_chr_tab, spr_ppool_tab, spr_pform_tab, spr_ppal_tab
.import g_coop

STAT77 = $213E
OAMADDH = $2103
OBJ_VRAM = $6000            ; OBJ CHR word address (OBSEL = $03)

.segment "ZEROPAGE"
spr_ids:   .res 3           ; long ptr: current level's id -> record table
spr_thm:   .res 3           ; long ptr: current area's themed id table
spr_src:   .res 3           ; long ptr scratch
spr_t0:    .res 2
spr_t1:    .res 2

.segment "BSS"
spr_arg_id:    .res 2
spr_arg_x:     .res 2
spr_arg_y:     .res 2
spr_arg_flags: .res 2
spr_pbase:     .res 2       ; test only: OBJ tile of the player frame area (default 0 = tiles 0-31)
spr_oam:       .res 544     ; 512 low + 32 high table, DMA'd every NMI
spr_hi:        .res 128     ; per-sprite high bits (bit0 = X bit 8, bit1 = 16x16), packed by spr_end
spr_next:      .res 2       ; next free OAM slot (walks down)
spr_left:      .res 2       ; free slots left this frame
spr_start:     .res 2       ; first slot of the frame
spr_over:      .res 2       ; STAT77 overflow seen
spr_bank:      .res 2       ; bank of the loaded level set (0 = none)
spr_set:       .res 2       ; address of the level set header
spr_nareas:    .res 2
; per-call scratch
spr_n:         .res 2
spr_w:         .res 2
spr_h:         .res 2
spr_sz:        .res 2
spr_amask:     .res 2
spr_aor:       .res 2
spr_px:        .res 2
; player frame upload queue (filled by spr_player, consumed by spr_nmi)
spr_pf_rec:    .res 2       ; frame record queued/uploaded (address in the player data bank)
spr_pf_n:      .res 2       ; pieces to upload (0 = nothing pending)
spr_pf_base:   .res 2       ; spr_pbase of the queued frame
spr_pf_src:    .res 16      ; per piece: source address
spr_pf_bank:   .res 16      ; per piece: source bank (words)
spr_pp_cur:    .res 2       ; palette 8 source address (in spr_ppal_tab's bank) last queued
spr_pp_pend:   .res 2
spr_pp_src:    .res 2
spr_pf2_blk:   .res 44      ; co-op player 2: same layout as spr_pf_rec..spr_pp_src (swapped in by spr_player)
spr_pslot:     .res 2       ; player slot of the next spr_player call: 0 = tiles 0-31 + palette 8, 1 = tiles 480-511 + palette 15
; level load
spr_g01:       .res 32      ; per native slot: plane 0/1 bits (word), from the group LUT
spr_g23:       .res 32
spr_row01:     .res 16
spr_row23:     .res 16
spr_ld_ptr:    .res 2
spr_ld_cnt:    .res 2
spr_ld_srct:   .res 2
spr_ld_n16:    .res 2
spr_ld_n8:     .res 2
spr_ld_c:      .res 2
spr_ld_s:      .res 2
spr_ld_dst:    .res 2
spr_ld_grp:    .res 2

.segment "HIBSS"
spr_t01:       .res 512     ; level load: chunky byte (2 pixels) -> plane 0/1 bit pairs for the current palette group
spr_t23:       .res 512

.segment "CODE4"

; ------------------------------------------------------------------------------------------------ frame
; spr_begin: start a new OAM frame (all sprites hidden).
spr_begin:
    php
    AXY16
    lda spr_over
    beq @norot
    lda spr_start
    sec
    sbc #37
    and #127
    sta spr_start
@norot:
    lda spr_start
    bne @s
    lda #127                ; first call ever (BSS = 0)
    sta spr_start
@s: sta spr_next
    lda #128
    sta spr_left
    lda #$E000              ; x = 0, y = 224 (off screen)
    ldx #0
@hide:
    sta spr_oam,x
    inx
    inx
    inx
    inx
    cpx #512
    bne @hide
    ldx #0
@hi:
    stz spr_hi,x
    inx
    inx
    cpx #128
    bne @hi
    plp
    rtl

; spr_end: pack the high table.
spr_end:
    php
    A8
    XY16
    ldx #0
    ldy #0
@pack:
    lda spr_hi+3,x
    asl
    asl
    ora spr_hi+2,x
    asl
    asl
    ora spr_hi+1,x
    asl
    asl
    ora spr_hi,x
    sta spr_oam+512,y
    inx
    inx
    inx
    inx
    iny
    cpy #32
    bne @pack
    plp
    rtl

; NMI upload of one player frame queue (A8 XY16 in and out)
.macro PL_UPLOAD q_n, q_base, q_src, q_bank, q_pend, q_ppsrc, cg
.local pc, nochr, nopal
    ldy q_n
    beq nochr
    lda #$80
    sta VMAIN
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    AXY16
    ldx #0
    lda q_base
    asl
    asl
    asl
    asl
    clc
    adc #OBJ_VRAM
    sta spr_t0
pc:
    lda spr_t0
    sta VMADDL
    lda q_src,x
    sta A1T0L
    lda q_bank,x
    sta A1B0
    lda #64
    sta DAS0L
    A8
    lda #$01
    sta MDMAEN
    A16
    lda spr_t0
    clc
    adc #256
    sta VMADDL
    lda q_src,x
    clc
    adc #64
    sta A1T0L
    lda #64
    sta DAS0L
    A8
    lda #$01
    sta MDMAEN
    A16
    lda spr_t0
    clc
    adc #32
    sta spr_t0
    inx
    inx
    dey
    bne pc
    stz q_n
    A8
nochr:
    lda q_pend
    beq nopal
    stz q_pend
    lda #cg
    sta CGADD
    stz DMAP0
    lda #<CGDATA
    sta BBAD0
    ldx q_ppsrc
    stx A1T0L
    lda #^spr_ppal_tab
    sta A1B0
    ldx #32
    stx DAS0L
    lda #$01
    sta MDMAEN
nopal:
.endmacro

; ------------------------------------------------------------------------------------------------ NMI
spr_nmi:
    php
    A8
    XY16
    ; overflow flags of the frame just shown
    lda STAT77
    and #$C0
    sta spr_over
    stz spr_over+1
    ; OAM
    stz OAMADDL
    stz OAMADDH
    stz DMAP0
    lda #<OAMDATA
    sta BBAD0
    ldx #.loword(spr_oam)
    stx A1T0L
    lda #^spr_oam
    sta A1B0
    ldx #544
    stx DAS0L
    lda #$01
    sta MDMAEN
    ; player CHR pieces: tiles 2k,2k+1 (top) and 2k+16,2k+17 (bottom); palette 8. Co-op player 2: own queue, palette 15
    PL_UPLOAD spr_pf_n, spr_pf_base, spr_pf_src, spr_pf_bank, spr_pp_pend, spr_pp_src, 128
    PL_UPLOAD spr_pf2_blk+2, spr_pf2_blk+4, spr_pf2_blk+6, spr_pf2_blk+22, spr_pf2_blk+40, spr_pf2_blk+42, 240
    plp
    rtl

; ------------------------------------------------------------------------------------------------ metasprites
; spr_meta: draw metasprite spr_arg_id with its image top-left at (spr_arg_x, spr_arg_y) (16-bit signed screen
; coords), spr_arg_flags: bit0 hflip, bit1 vflip, bit2 behind (OBJ priority 2), bits 4-7 palette override (8-15).
spr_meta:
    phb
    php
    AXY16
    lda spr_bank
    beq @out0
    lda spr_arg_id
    cmp #SPR_NPLAIN
    bcs @themed
    asl
    tay
    lda [spr_ids],y
    bra @got
@themed:
    sbc #SPR_NPLAIN
    cmp #SPR_NTHEMED
    bcs @out0
    asl
    tay
    lda [spr_thm],y
@got:
    bne @have
@out0:
    plp
    plb
    rtl
@have:
    tax
    jsr attr_setup
    A8
    lda spr_bank
    pha
    plb                     ; DB = level set bank (WRAM $0000-$1FFF still mirrored there)
    A16
    lda a:0,x
    and #$00FF
    sta spr_n
    lda a:1,x
    and #$00FF
    sta spr_w
    lda a:2,x
    and #$00FF
    sta spr_h
    inx
    inx
    inx
    lda spr_n
    jeq @done
@piece:
    lda spr_left
    jeq @done
    ; size
    lda a:3,x
    and #$0010
    beq @s8
    lda #16
    bra @ss
@s8:
    lda #8
@ss:
    sta spr_sz
    ; x
    lda a:0,x
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   sta spr_t0              ; dx
    lda spr_arg_flags
    lsr
    bcc @nh
    lda spr_w               ; hflip: w - dx - size
    sec
    sbc spr_t0
    sec
    sbc spr_sz
    sta spr_t0
@nh:
    lda spr_t0
    clc
    adc spr_arg_x
    sta spr_px
    clc
    adc #16
    cmp #272
    jcs @skip
    ; y
    lda a:1,x
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   sta spr_t0
    lda spr_arg_flags
    and #SPR_VFLIP
    beq @nv
    lda spr_h
    sec
    sbc spr_t0
    sec
    sbc spr_sz
    sta spr_t0
@nv:
    lda spr_t0
    clc
    adc spr_arg_y
    sta spr_t1              ; sy
    clc
    adc #16
    cmp #208                ; visible rows -16..191 only (playfield)
    bcs @skip
    ; allocate slot
    ldy spr_next
    A8
    lda spr_px+1
    and #$01
    sta spr_hi,y
    lda spr_sz
    cmp #16
    bne :+
    lda spr_hi,y
    ora #$02
    sta spr_hi,y
:   A16
    tya
    asl
    asl
    tay
    A8
    lda spr_px
    sta spr_oam,y
    lda spr_t1
    sta spr_oam+1,y
    lda a:2,x
    sta spr_oam+2,y
    lda a:3,x
    and spr_amask
    ora spr_aor
    sta spr_oam+3,y
    A16
    lda spr_next
    dec
    and #127
    sta spr_next
    dec spr_left
@skip:
    inx
    inx
    inx
    inx
    dec spr_n
    jne @piece
@done:
    plp
    plb
    rtl

; attr_setup: spr_amask/spr_aor from spr_arg_flags (A16 XY16 on entry and exit). Keeps name bit + palette
; (or replaces the palette with the override), adds priority and flips.
attr_setup:
    .a16
    .i16
    lda spr_arg_flags
    and #SPR_BEHIND
    beq :+
    lda #$20
    bra :++
:   lda #$30
:   sta spr_aor
    lda spr_arg_flags
    lsr
    bcc :+
    lda spr_aor
    ora #$40
    sta spr_aor
:   lda spr_arg_flags
    and #SPR_VFLIP
    beq :+
    lda spr_aor
    ora #$80
    sta spr_aor
:   lda spr_arg_flags
    and #$00F0
    beq @def
    lsr
    lsr
    lsr                     ; (pal 8-15) << 1 -> bits 1-3 = pal & 7
    and #$000E
    ora spr_aor
    sta spr_aor
    lda #$0001
    sta spr_amask
    rts
@def:
    lda #$000F
    sta spr_amask
    rts

; spr_meta_size: A16 spr_arg_id -> X = width, Y = height of the image (0/0 if not loaded in this level).
spr_meta_size:
    phb
    php
    AXY16
    ldx #0
    ldy #0
    lda spr_bank
    beq @o
    lda spr_arg_id
    cmp #SPR_NPLAIN
    bcs @t
    asl
    tay
    lda [spr_ids],y
    bra @g
@t: sbc #SPR_NPLAIN
    ldy #0
    cmp #SPR_NTHEMED
    bcs @o
    asl
    tay
    lda [spr_thm],y
@g: ldy #0
    tax
    beq @o
    A8
    lda spr_bank
    pha
    plb
    A16
    lda a:2,x
    and #$00FF
    tay
    lda a:1,x
    and #$00FF
    tax
@o: plp
    plb
    rtl

; ------------------------------------------------------------------------------------------------ player
; spr_player: A = form (FORM_*), X = pose (POSE_*), spr_arg_x/y = top-left of the 16x32 player box,
; spr_arg_flags: SPR_HFLIP/VFLIP/BEHIND, tail pose bits 8-9, SPR_STARPAL + k bits 11-12, SPR_LUIGI, SPR_PFLASH.
spr_player:
    phb
    php
    AXY16
    pha
    lda spr_pslot
    beq :+
    phx
    jsr pslot_swap
    plx
:   pla
    and #$00FF
    stx spr_t1              ; pose
    ; index = ((form * NPOSES + pose) * 4 + tail) * 2
    sta spr_t0              ; form
    tay
    lda #0
    cpy #0
    beq @m1
@m0:
    clc
    adc #SPR_NPOSES
    dey
    bne @m0
@m1:
    clc
    adc spr_t1
    asl
    asl
    sta spr_n
    lda spr_arg_flags
    xba
    and #$0003
    ora spr_n
    asl
    tax
    lda f:spr_pform_tab,x
    sta spr_px              ; frame record
    ; ---- palette variant
    lda spr_t1
    cmp #POSE_DEATH
    bne @nd
    lda #PPAL_MARIO
    bra @pv
@nd:
    lda spr_arg_flags
    and #SPR_STARPAL
    beq @nstar
    lda spr_arg_flags
    xba
    lsr
    lsr
    lsr
    and #$0003
    beq @k0
    clc
    adc #PPAL_FIRE-1        ; 1 fire, 2 pstar1, 3 pstar2
    bra @pv
@k0:
    lda #PPAL_MARIO
    bra @pv
@nstar:
    lda spr_t1
    cmp #POSE_STATUE
    bne :+
    lda #PPAL_STATUE
    bra @pv
:   lda spr_arg_flags
    and #SPR_PFLASH
    beq :+
    lda #PPAL_PFLASH
    bra @pv
:   lda #PPAL_DEFAULT
@pv:
    ; Luigi: DEFAULT -> LUIGI, MARIO -> LUIGI_PLAIN
    sta spr_t1
    lda spr_arg_flags
    and #SPR_LUIGI
    beq @nl
    lda spr_t1
    cmp #PPAL_DEFAULT
    bne :+
    lda #PPAL_LUIGI
    sta spr_t1
:   cmp #PPAL_MARIO
    bne @nl
    lda #PPAL_LUIGI_PLAIN
    sta spr_t1
@nl:
    ; palette address = spr_ppal_tab + (form * NPPAL + variant) * 32
    lda spr_t0
    tay
    lda #0
    cpy #0
    beq @p1
@p0:
    clc
    adc #SPR_NPPAL
    dey
    bne @p0
@p1:
    clc
    adc spr_t1
    asl
    asl
    asl
    asl
    asl
    clc
    adc #.loword(spr_ppal_tab)
    cmp spr_pp_cur
    beq :+
    sta spr_pp_cur
    sta spr_pp_src
    lda #1
    sta spr_pp_pend
:
    ; ---- switch to the player data bank
    A8
    lda #^spr_pform_tab
    pha
    plb
    A16
    ldx spr_px
    lda a:0,x
    and #$00FF
    sta spr_n               ; pieces
    lda a:1,x
    and #$00FF
    sta spr_h               ; vflip height
    ; queue the CHR upload when the frame changed
    cpx spr_pf_rec
    bne @q0
    lda spr_pbase
    cmp spr_pf_base
    beq @queued
@q0:
    stx spr_pf_rec
    lda spr_pbase
    sta spr_pf_base
    phx
    ldy #0
    lda spr_n
    sta spr_w
    inx
    inx
@q:
    lda a:2,x               ; pool index
    phx
    pha
    xba
    and #$00FF              ; chunk = idx >> 8
    sta spr_t0
    asl
    adc spr_t0              ; *3
    tax
    lda f:spr_ppool_tab+2,x
    and #$00FF
    sta spr_pf_bank,y
    lda f:spr_ppool_tab,x
    sta spr_t0
    pla
    and #$00FF
    xba                     ; * 256
    lsr                     ; * 128
    clc
    adc spr_t0
    sta spr_pf_src,y
    plx
    inx
    inx
    inx
    inx
    iny
    iny
    dec spr_w
    bne @q
    lda spr_n
    sta spr_pf_n
    plx
@queued:
    ; ---- OAM entries
    lda spr_arg_flags
    pha
    and #$0007              ; hflip/vflip/behind only (no palette override for the player)
    sta spr_arg_flags
    jsr attr_setup
    pla
    sta spr_arg_flags
    lda spr_aor
    and #$00FE              ; palette 0 = OBJ 8
    ldy spr_pslot
    beq :+
    ora #$000E              ; co-op player 2: palette 7 = OBJ 15
:
    sta spr_aor
    lda spr_pbase
    xba
    and #$0001              ; name table bit of the frame area
    ora spr_aor
    sta spr_aor
    inx
    inx
    lda spr_pbase
    sta spr_t1              ; tile = base + 2k
    lda spr_n
    jeq @pdone
@pp:
    lda spr_left
    jeq @pdone
    lda a:0,x
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   sta spr_t0
    lda spr_arg_flags
    lsr
    bcc :+
    lda #0                  ; hflip: 16 - dx - 16 = -dx
    sec
    sbc spr_t0
    sta spr_t0
:   lda spr_t0
    clc
    adc spr_arg_x
    sta spr_px
    clc
    adc #16
    cmp #272
    bcs @pskip
    lda a:1,x
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   sta spr_t0
    lda spr_arg_flags
    and #SPR_VFLIP
    beq :+
    lda spr_h               ; vflip: hv - dy - 16
    sec
    sbc spr_t0
    sec
    sbc #16
    sta spr_t0
:   lda spr_t0
    clc
    adc spr_arg_y
    sta spr_w               ; sy
    clc
    adc #16
    cmp #208
    bcs @pskip
    ldy spr_next
    A8
    lda spr_px+1
    and #$01
    ora #$02
    sta spr_hi,y
    A16
    tya
    asl
    asl
    tay
    A8
    lda spr_px
    sta spr_oam,y
    lda spr_w
    sta spr_oam+1,y
    lda spr_t1
    sta spr_oam+2,y
    lda spr_aor
    sta spr_oam+3,y
    A16
    lda spr_next
    dec
    and #127
    sta spr_next
    dec spr_left
@pskip:
    lda spr_t1
    clc
    adc #2
    sta spr_t1
    inx
    inx
    inx
    inx
    dec spr_n
    jne @pp
@pdone:
    plp
    plb
    php
    AXY16
    lda spr_pslot
    beq :+
    jsr pslot_swap
:   plp
    rtl

; co-op player 2 (spr_pslot != 0): its own upload queue (spr_pf2_blk), frame area at OBJ tile 480, palette 15
pslot_swap:
    ldx #0
:   lda spr_pf_rec,x
    tay
    lda spr_pf2_blk,x
    sta spr_pf_rec,x
    tya
    sta spr_pf2_blk,x
    inx
    inx
    cpx #44
    bcc :-
    lda spr_pbase
    eor #480
    sta spr_pbase
    rts

; ------------------------------------------------------------------------------------------------ level load
; spr_area: A = area index of the current level -> theme-colored ids (SPR_BUMP_*, SPR_SPLASH_*, ...) use its theme.
spr_area:
    phb
    php
    AXY16
    and #$00FF
    cmp spr_nareas
    bcc :+
    lda #0
:   ; offset = area * NTHEMED * 2
    tay
    lda #0
    cpy #0
    beq @m1
@m0:
    clc
    adc #SPR_NTHEMED*2
    dey
    bne @m0
@m1:
    sta spr_t0
    ldx spr_set
    lda spr_bank
    beq @o
    A8
    pha
    plb
    A16
    lda a:8,x
    clc
    adc spr_t0
    sta spr_thm
    A8
    lda spr_bank
    sta spr_thm+2
@o: plp
    plb
    rtl

; spr_level_load: A = level index (LVL_* of gen/levels.inc). Forced blank only: uploads the level's OBJ CHR
; (tiles 32-511, remapped to its palettes) and OBJ palettes 9-15, selects the start area's theme.
spr_level_load:
    phb
    php
    AXY16
    and #$00FF
    ldx g_coop                  ; co-op: the level's set with 6 enemy palettes (15 = Luigi)
    beq :+
    clc
    adc #SPR_NLEVELS
:
    sta spr_t0
    asl
    adc spr_t0
    tax
    lda f:spr_level_tab,x
    sta spr_set
    lda f:spr_level_tab+2,x
    and #$00FF
    sta spr_bank
    A8
    pha
    plb                     ; DB = set bank
    A16
    ldx spr_set
    lda a:6,x
    sta spr_ids
    A8
    lda spr_bank
    sta spr_ids+2
    A16
    lda a:11,x
    and #$00FF
    sta spr_nareas
    ; palettes 9-15
    A8
    lda #144
    sta CGADD
    stz DMAP0
    lda #<CGDATA
    sta BBAD0
    A16
    lda a:0,x
    sta A1T0L
    A8
    lda spr_bank
    sta A1B0
    A16
    lda #224
    sta DAS0L
    A8
    lda #$01
    sta MDMAEN
    lda #$80
    sta VMAIN
    A16
    ; load list
    lda #$FFFF
    sta spr_ld_grp
    lda a:4,x
    sta spr_ld_ptr
    lda a:10,x
    and #$00FF
    sta spr_ld_cnt
    jeq @ldone
@entry:
    ldx spr_ld_ptr
    lda a:0,x
    sta spr_ld_srct
    lda a:2,x
    and #$00FF
    sta spr_ld_n16
    lda a:3,x
    and #$00FF
    sta spr_ld_n8
    lda a:4,x
    and #$00FF
    sta spr_ld_c
    lda a:5,x
    and #$00FF
    sta spr_ld_s
    lda a:6,x               ; group -> LUT at set.lut + g*16 (tables rebuilt only when the group changes)
    and #$00FF
    cmp spr_ld_grp
    beq @samegrp
    sta spr_ld_grp
    asl
    asl
    asl
    asl
    ldx spr_set
    clc
    adc a:2,x
    tax
    jsr build_masks
@samegrp:
    ; 16x16 cells
    lda spr_ld_n16
    beq @singles
@cell:
    lda spr_ld_c            ; tile = 32 + (c >> 3) * 32 + (c & 7) * 2
    and #$0007
    asl
    sta spr_t0
    lda spr_ld_c
    and #$FFF8
    asl
    asl
    clc
    adc spr_t0
    adc #32
    sta spr_ld_dst
    jsr conv_tile           ; TL
    inc spr_ld_dst
    jsr conv_tile           ; TR
    lda spr_ld_dst
    clc
    adc #15
    sta spr_ld_dst
    jsr conv_tile           ; BL
    inc spr_ld_dst
    jsr conv_tile           ; BR
    inc spr_ld_c
    dec spr_ld_n16
    bne @cell
@singles:
    lda spr_ld_n8
    beq @next
@single:
    ; cell = 119 - (s >> 2); quadrant q = s & 3 -> + (q & 1) + (q >> 1) * 16
    lda spr_ld_s
    lsr
    lsr
    sta spr_t0
    lda #119
    sec
    sbc spr_t0
    sta spr_t0
    and #$0007
    asl
    sta spr_t1
    lda spr_t0
    and #$FFF8
    asl
    asl
    clc
    adc spr_t1
    adc #32
    sta spr_ld_dst
    lda spr_ld_s
    and #$0001
    clc
    adc spr_ld_dst
    sta spr_ld_dst
    lda spr_ld_s
    and #$0002
    beq :+
    lda spr_ld_dst
    clc
    adc #16
    sta spr_ld_dst
:   jsr conv_tile
    inc spr_ld_s
    dec spr_ld_n8
    bne @single
@next:
    lda spr_ld_ptr
    clc
    adc #7
    sta spr_ld_ptr
    dec spr_ld_cnt
    jne @entry
@ldone:
    ; start area theme, force a player CHR/palette re-upload
    stz spr_pf_rec
    stz spr_pp_cur
    stz spr_pf2_blk
    stz spr_pf2_blk+38
    ldx spr_set
    lda a:12,x
    and #$00FF
    A8
    pha
    lda #$80
    pha
    plb
    pla
    jsl spr_area
    plp
    plb
    rtl

; build_masks: X = LUT address (set bank, DB = set bank), A16 XY16. spr_g01/g23[slot] = plane bits of lut[slot].
build_masks:
    .a16
    .i16
    ldy #0
@m:
    lda a:0,x
    and #$000F
    sta spr_t0
    ; g01 = (bit1 << 8) | bit0 ; g23 = (bit3 << 8) | bit2
    and #$0001
    sta spr_t1
    lda spr_t0
    and #$0002
    beq :+
    lda spr_t1
    ora #$0100
    sta spr_t1
:   lda spr_t1
    sta spr_g01,y
    lda spr_t0
    and #$0004
    lsr
    lsr
    sta spr_t1
    lda spr_t0
    and #$0008
    beq :+
    lda spr_t1
    ora #$0100
    sta spr_t1
:   lda spr_t1
    sta spr_g23,y
    inx
    iny
    iny
    cpy #32
    bne @m
    ; byte (2 pixels, hi nibble = left) -> 2-bit plane pairs: t01[b] = g01[b >> 4] << 1 | g01[b & 15]
    ldx #0                  ; x = b * 2
@hi:
    txa
    lsr
    lsr
    lsr
    lsr
    tay                     ; (b >> 4) * 2
    lda spr_g01,y
    asl
    sta spr_t0
    lda spr_g23,y
    asl
    sta spr_t1
    ldy #0
@lo:
    lda spr_g01,y
    ora spr_t0
    sta f:spr_t01,x
    lda spr_g23,y
    ora spr_t1
    sta f:spr_t23,x
    inx
    inx
    iny
    iny
    cpy #32
    bne @lo
    cpx #512
    bne @hi
    rts

; conv_tile: global tile spr_ld_srct (post-incremented) -> VRAM OBJ tile spr_ld_dst, remapped through spr_t01/t23.
; A16 XY16, DB = set bank (only WRAM-mirror absolute addresses are used here).
conv_tile:
    .a16
    .i16
    ; source = spr_chr_tab[idx >> 10] + (idx & 1023) * 32
    lda spr_ld_srct
    xba
    lsr
    lsr
    and #$003F
    sta spr_t0
    asl
    adc spr_t0
    tax
    lda f:spr_chr_tab,x
    sta spr_src
    lda f:spr_chr_tab+2,x
    and #$00FF
    sta spr_src+2           ; (+3 = spr_t0 low byte, scratch)
    lda spr_ld_srct
    and #$03FF
    asl
    asl
    asl
    asl
    asl
    clc
    adc spr_src
    sta spr_src
    inc spr_ld_srct
    ; VRAM address
    lda spr_ld_dst
    asl
    asl
    asl
    asl
    clc
    adc #OBJ_VRAM
    sta VMADDL
    ; 8 rows x 4 bytes (2 pixels each): w = (w << 2) | t[b]
    ldy #0
    ldx #0
@row:
    phx
    lda [spr_src],y
    and #$00FF
    asl
    tax
    lda f:spr_t01,x
    sta spr_t0
    lda f:spr_t23,x
    sta spr_t1
    iny
.repeat 3
    lda [spr_src],y
    and #$00FF
    asl
    tax
    lda spr_t0
    asl
    asl
    ora f:spr_t01,x
    sta spr_t0
    lda spr_t1
    asl
    asl
    ora f:spr_t23,x
    sta spr_t1
    iny
.endrepeat
    plx
    lda spr_t0
    sta spr_row01,x
    lda spr_t1
    sta spr_row23,x
    inx
    inx
    cpx #16
    bne @row
    ldx #0
@w01:
    lda spr_row01,x
    sta VMDATAL
    inx
    inx
    cpx #16
    bne @w01
    ldx #0
@w23:
    lda spr_row23,x
    sta VMDATAL
    inx
    inx
    cpx #16
    bne @w23
    rts
