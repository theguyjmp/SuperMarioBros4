; Backgrounds module (backgrounds agent): BG2 parallax bands + sky gradient via HDMA. Docs: snes/BACKGROUNDS.md.
; Data: gen/bg_themes.s (smb4tools snes-export, src/Tools/Snes/SnesBackgrounds.cs) - one blob per theme variant
; (dry / wet / mix), each inside one LoROM bank. Blob layout = BGV_* below (keep in sync with the converter).
;
; Per frame bg_update paints, for the 192 playfield lines, which "segment" (a run of rows of one parallax layer)
; owns each scanline (margin blocks first, then the floor-camera bands far->near: nearest wins), then turns the runs
; into two HDMA tables (double-buffered in HIBSS, swapped in bg_nmi):
;   ch6: BG2HOFS/BG2VOFS per band (direct, mode 3)
;   ch7: CGRAM color 0 = backdrop (indirect, mode 3): the gradient band color, or per line the band's "hole fill"
;        (mean color of the farther layers the band hides) - 4-byte records 0,0,lo,hi prepared in RAM at load.
; Lines 192-223 (HUD): BG2 points at transparent map rows and the backdrop is black.
.p816
.smart
.include "snes.inc"

.export bg_nmi, bg_load, bg_set_water, bg_update, bg_hdma_off
.export bg_cam_x, bg_cam_y, bg_area_h, bg_flat_color, bg_hdma_other
.import bg_dir_dry, bg_dir_wet, bg_dir_mix

; ---- blob layout (SnesBackgrounds.cs BGV_*) ----
BGV_NLAYERS  = 0
BGV_NSEG     = 1
BGV_CHRBYTES = 2
BGV_TROW     = 4        ; map pixel row of the transparent area
BGV_TLEN     = 6        ; its height in px (max lines per transparent HDMA entry)
BGV_NDRY     = 8
BGV_NWET     = 9
BGV_DRYSTART = 10       ; 16 bytes: first scanline of each gradient band
BGV_DRYCOL   = 26       ; 16 words BGR555
BGV_WETSTART = 58
BGV_WETCOL   = 74
BGV_LNUM     = 106      ; 8 bytes: layer number 1..4 (scroll factor num/8 horizontal, num/16 vertical)
BGV_LBASE    = 114      ; 8 words: 192 - height + extraY (screen y of the layer top at the floor camera)
BGV_SLAYER   = 130      ; 32 bytes: segment -> layer index
BGV_SFLAGS   = 162      ; 32 bytes: bit0 = underwater segment
BGV_SR0      = 194      ; 32 words: first layer row
BGV_SR1      = 258      ; 32 words: end layer row (exclusive)
BGV_SVBASE   = 322      ; 32 words: map pixel row of layer row 0
BGV_SFILL    = 386      ; 32 words: first fill row of the segment, $FFFF = gradient
BGV_NFILL    = 450      ; word: fill rows
BGV_HDR      = 452      ; header bytes copied to RAM
BGV_FILL     = 452      ; 192 words BGR555 fill colors
BGV_PAL      = 836      ; 32 words: CGRAM 96-127 (BG palettes 6-7)
BGV_MAP      = 900      ; 2048 words: 32x64 BG2 map
BGV_CHR      = 4996     ; CHR (BGV_CHRBYTES bytes)

VRAM_BG2CHR = $3000
VRAM_BG2MAP = $4800
BG2SC_VAL   = $4A       ; map $4800, 32x64 (2048 words = the reserved 64x32 footprint; 256-px wrap for free)
BG12NBA_VAL = $30       ; BG1 CHR $0000, BG2 CHR $3000
VADJ        = 1         ; the first visible line shows BG row VOFS+1

STAB_SIZE = 512         ; bytes per scroll HDMA buffer (5 bytes/entry)
GTAB_SIZE = 256         ; bytes per gradient HDMA buffer (3 bytes/entry, indirect)
REC_BLACK = 32          ; gradient record index of the HUD black
MAXRUNS   = 48          ; band runs per frame (max)
MAXSEG    = 32

.segment "ZEROPAGE"
bgz_ptr: .res 3         ; long pointer to the loaded variant blob
bgz_a:   .res 2
bgz_b:   .res 2
bgz_c:   .res 2
bgz_d:   .res 2
bgz_s:   .res 2         ; segment index / band owner
bgz_y:   .res 2         ; current scanline
bgz_e:   .res 2         ; band end
bgz_n:   .res 2         ; HDMA entry: line count
bgz_h:   .res 2         ;             first word
bgz_v:   .res 2         ;             second word
bgz_tab: .res 2         ; scroll table write pointer (bank $7E)
bgz_lim: .res 2
bgz_gt:  .res 2         ; gradient table write pointer (bank $7E)
bgz_glim: .res 2
bgz_kd:  .res 2         ; current dry gradient band
bgz_kw:  .res 2         ; current wet gradient band
bgz_gs:  .res 2         ; gradient: band index (bit15 = wet)
bgz_go:  .res 2         ;           starts offset
bgz_gn:  .res 2         ;           band count
bgz_ga:  .res 2         ;           cursor line
bgz_gb:  .res 2         ;           part end
bgz_gc:  .res 2         ;           range end
bgz_gd:  .res 2         ;           record pointer
bgz_gm:  .res 2         ;           lines left to emit
bgz_r:   .res 2         ; run offset
bgz_t:   .res 2
bgz_k:   .res 2         ; event index
bgz_nev: .res 2         ; events * 2
bgz_m0:  .res 2         ; active segments 0-15
bgz_m1:  .res 2         ; active segments 16-31
bgz_rn:  .res 2         ; runs * 2 while building

.segment "BSS"
bg_cam_x:      .res 2   ; camera (level pixels), set by the engine before bg_update
bg_cam_y:      .res 2
bg_area_h:     .res 2   ; area height in px (tiles*16): vertical parallax anchor = area floor
bg_flat_color: .res 2   ; BGR555 backdrop for bg_load with X=1 (bg= areas)
bg_hdma_other: .res 1   ; HDMAEN bits of other modules' channels (bg_nmi writes HDMAEN = $E0 | this; ch5 = HUD window)
bg_wnot:       .res 2   ; NOT water surface px (so cleared RAM = no water)
bg_theme:      .res 1
bg_flat:       .res 1
bg_loaded:     .res 1   ; 0 = nothing, 1 dry, 2 wet, 3 mix, 4 flat
bg_want:       .res 1
bg_ready:      .res 1   ; back buffer complete -> swap in NMI
bg_front:      .res 1   ; buffer HDMA reads (0/1)
bg_nlay:       .res 2
bg_nseg:       .res 2
bg_d:          .res 2
bg_yw:         .res 2   ; screen line where underwater starts (192 = none)
bg_hx:         .res 16  ; per layer BG2HOFS
bg_ly:         .res 16  ; per layer screen y of row 0
bg_mx:         .res 8   ; (cam_x & $7FF) * 1..4
bg_since:      .res 2   ; frames since the last run rebuild (3 = rebuild at once)
bg_md:         .res 8   ; d * 1..4
bg_last_ly:    .res 16  ; layer rows + water line the cached runs were built for (last_yw $FFFF = rebuild)
bg_last_yw:    .res 2
bg_gfront:     .res 1   ; gradient buffer HDMA reads (0/1)
bg_nruns:      .res 2   ; runs * 2

.segment "HIBSS"
bg_hdr:     .res BGV_HDR

bg_stab:    .res 2*STAB_SIZE
bg_gtab:    .res 2*GTAB_SIZE
bg_gline_dry: .res 4*192 ; per line backdrop records 0,0,lo,hi (HDMA ch7 indirect data)
bg_gline_wet: .res 4*192
bg_black:   .res 4
bg_evy:     .res 2*2*MAXSEG ; event lines: slot 4s = segment start, 4s+2 = end
bg_order:   .res 2*2*MAXSEG ; event slots sorted by line
bg_fillrec: .res 4*192
bg_run_y0:  .res 2*MAXRUNS  ; cached band runs (first line, end line, owner, VOFS, layer*2)
bg_run_y1:  .res 2*MAXRUNS
bg_run_s:   .res 2*MAXRUNS
bg_run_v:   .res 2*MAXRUNS
bg_run_l:   .res 2*MAXRUNS

.segment "CODE5"

; ---------------------------------------------------------------- bg_hdma_off
; Stops HDMA channels 6-7. Call before any forced-blank CGRAM/VRAM upload done outside NMI (HDMA writes CGADD every
; scanline and would corrupt a general DMA to CGRAM). The next bg_update + NMI turns them back on.
bg_hdma_off:
    php
    sep #$20
    lda bg_hdma_other
    sta HDMAEN
    stz bg_ready
    rep #$20
    lda #$FFFF
    sta bg_last_yw          ; force a full rebuild on the next bg_update
    lda #3
    sta bg_since
    plp
    rtl

; ---------------------------------------------------------------- bg_set_water
; X = level pixel row where the underwater backdrop/layers start (WaterRow*16 + (WaterRow>0 ? 16 : 0)), $FFFF = none.
; Call before bg_load (same forced blank). If a theme is loaded and it needs another variant, it is re-uploaded
; (so this must then also happen during forced blank).
bg_set_water:
    php
    rep #$30
    txa
    eor #$FFFF
    sta bg_wnot
    sep #$20
    lda bg_loaded
    beq @done
    cmp #4
    beq @done
    jsr pick_variant
    lda bg_want
    cmp bg_loaded
    beq @done
    jsl bg_hdma_off
    jsr upload
    jsl bg_update
@done:
    plp
    rtl

; ---------------------------------------------------------------- bg_load
; A = theme id (gen/themes.inc), X = 1 for a flat bg= backdrop (color in bg_flat_color), 0 = gradient + parallax.
; Forced blank only. Sets BG2SC/BG12NBA, uploads BG2 CHR/map + palettes 6-7, builds the first HDMA tables.
bg_load:
    php
    sep #$20
    rep #$10
    sta bg_theme
    txa
    sta bg_flat
    jsl bg_hdma_off
    jsr pick_variant
    jsr upload
    jsl bg_update
    plp
    rtl

; pick_variant: sets bg_want (1 dry / 2 wet / 3 mix / 4 flat) and bgz_ptr. A8 X16.
pick_variant:
    lda bg_flat
    beq :+
    lda #4
    sta bg_want
    rts
:   rep #$20
    lda bg_theme
    and #$00FF
    sta bgz_a
    asl
    adc bgz_a
    tax                     ; X = theme*3
    lda bg_wnot
    beq @dry                ; water = $FFFF
    cmp #$FFFF
    beq @wet0               ; water = 0 -> fully underwater
@mix:                       ; partial water: mix, else dry
    lda f:bg_dir_mix+2,x
    and #$00FF
    beq @dry
    lda f:bg_dir_mix,x
    sta bgz_ptr
    lda f:bg_dir_mix+1,x
    sta bgz_ptr+1
    lda #3
    bra @set
@wet0:
    lda f:bg_dir_wet+2,x
    and #$00FF
    beq @mix
    lda f:bg_dir_wet,x
    sta bgz_ptr
    lda f:bg_dir_wet+1,x
    sta bgz_ptr+1
    lda #2
    bra @set
@dry:
    lda f:bg_dir_dry,x
    sta bgz_ptr
    lda f:bg_dir_dry+1,x
    sta bgz_ptr+1
    lda #1
@set:
    sep #$20
    sta bg_want
    rts

; upload: loads bg_want (forced blank, HDMA 6-7 off). A8 X16, DB=$80.
upload:
    lda #BG2SC_VAL
    sta BG2SC
    lda #$02                ; window 1 on OBJ (HUD split, see hud_win)
    sta WOBJSEL
    lda #$10
    sta TMW
    lda #BG12NBA_VAL
    sta BG12NBA
    lda bg_want
    sta bg_loaded
    cmp #4
    bne @theme
    ; ---- flat: empty map + blank tile 0, one-band gradient in bg_flat_color ----
    lda #$80
    sta VMAIN
    ldx #VRAM_BG2MAP
    stx VMADDL
    ldx #4096
    jsr vram_zero
    ldx #VRAM_BG2CHR
    stx VMADDL
    ldx #32
    jsr vram_zero
    rep #$20
    lda #0
    sta bg_nlay
    sta bg_nseg
    sta f:bg_hdr+BGV_TROW
    sta f:bg_hdr+BGV_NFILL
    lda #120
    sta f:bg_hdr+BGV_TLEN
    lda #$0101
    sta f:bg_hdr+BGV_NDRY   ; NDRY = NWET = 1
    lda #0
    sta f:bg_hdr+BGV_DRYSTART
    sta f:bg_hdr+BGV_WETSTART
    lda bg_flat_color
    sta f:bg_hdr+BGV_DRYCOL
    sta f:bg_hdr+BGV_WETCOL
    sep #$20
    jmp build_recs
@theme:
    ; header -> bg_hdr (DMA ROM -> WRAM)
    ldx #.loword(bg_hdr)
    stx WMADDL
    lda #^bg_hdr
    sta WMADDL+2
    stz DMAP0
    lda #<WMDATA
    sta BBAD0
    ldx #0
    stx bgz_a
    ldx #BGV_HDR
    jsr dma_blob
    ; palettes 6-7
    lda #96
    sta CGADD
    stz DMAP0
    lda #<CGDATA
    sta BBAD0
    ldx #BGV_PAL
    stx bgz_a
    ldx #64
    jsr dma_blob
    ; map
    lda #$80
    sta VMAIN
    ldx #VRAM_BG2MAP
    stx VMADDL
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    ldx #BGV_MAP
    stx bgz_a
    ldx #4096
    jsr dma_blob
    ; CHR
    ldx #VRAM_BG2CHR
    stx VMADDL
    ldx #BGV_CHR
    stx bgz_a
    rep #$20
    lda f:bg_hdr+BGV_CHRBYTES
    tax
    sep #$20
    jsr dma_blob
    rep #$20
    lda f:bg_hdr+BGV_NLAYERS
    and #$00FF
    sta bg_nlay
    lda f:bg_hdr+BGV_NSEG
    and #$00FF
    sta bg_nseg
    asl
    asl
    tay                     ; event slots * 2
    ldx #0
:   txa                     ; event order = identity (re-sorted incrementally by bg_update)
    sta f:bg_order,x
    inx
    inx
    dey
    dey
    bne :-
    ; fill colors -> 4-byte HDMA records
    lda f:bg_hdr+BGV_NFILL
    beq @nofill
    sta bgz_c
    ldy #BGV_FILL
    ldx #0
:   lda #0
    sta f:bg_fillrec,x
    lda [bgz_ptr],y
    sta f:bg_fillrec+2,x
    iny
    iny
    inx
    inx
    inx
    inx
    dec bgz_c
    bne :-
@nofill:
    sep #$20
    ; fall through
; build_recs: gradient bands in bg_hdr -> one backdrop record per playfield line (dry + wet) + black. A8 X16.
build_recs:
    phb
    lda #$7E
    pha
    plb
    rep #$20
    lda #BGV_DRYSTART
    sta bgz_go
    lda #BGV_DRYCOL
    sta bgz_gb
    lda .loword(bg_hdr)+BGV_NDRY
    and #$00FF
    sta bgz_gn
    ldx #.loword(bg_gline_dry)
    jsr line_recs
    lda #BGV_WETSTART
    sta bgz_go
    lda #BGV_WETCOL
    sta bgz_gb
    lda .loword(bg_hdr)+BGV_NWET
    and #$00FF
    sta bgz_gn
    ldx #.loword(bg_gline_wet)
    jsr line_recs
    stz .loword(bg_black)
    stz .loword(bg_black)+2
    sep #$20
    plb
    rts

; line_recs: X = record array; bands: starts at bg_hdr+bgz_go, colors at bg_hdr+bgz_gb, bgz_gn bands. A16, DB=$7E.
line_recs:
    .a16
    .i16
    stz bgz_gs              ; band
    stz bgz_ga              ; line
@line:
    lda bgz_gs              ; advance while band+1 < n and start[band+1] <= line
    inc
    cmp bgz_gn
    bcs @put
    clc
    adc bgz_go
    tay
    lda .loword(bg_hdr),y
    and #$00FF
    cmp bgz_ga
    beq :+
    bcs @put
:   inc bgz_gs
    bra @line
@put:
    lda bgz_gs
    asl
    clc
    adc bgz_gb
    tay
    lda .loword(bg_hdr),y
    stz a:0,x
    sta a:2,x
    inx
    inx
    inx
    inx
    inc bgz_ga
    lda bgz_ga
    cmp #192
    bcc @line
    rts

; dma_blob: channel 0 from blob offset bgz_a, X bytes (DMAP0/BBAD0 already set). A8 X16.
dma_blob:
    stx DAS0L
    rep #$20
    lda bgz_ptr
    clc
    adc bgz_a
    sta A1T0L
    sep #$20
    lda bgz_ptr+2
    sta A1B0
    lda #$01
    sta MDMAEN
    rts

; vram_zero: X bytes of zero to VRAM at the current VMADD (VMAIN = $80). A8 X16.
vram_zero:
    stx DAS0L
    lda #$09                ; fixed source, 2 registers
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    ldx #.loword(zero_word)
    stx A1T0L
    lda #^zero_word
    sta A1B0
    lda #$01
    sta MDMAEN
    rts
zero_word: .word 0

; HDMA ch5 table (direct, mode 1 -> WH0/WH1): window 1 empty on the playfield, full width from line 192, so
; OBJ (masked by window 1 on the main screen, WOBJSEL/TMW set in upload) never shows over the HUD.
hud_win:
    .byte 127, $FF, $00
    .byte 65, $FF, $00
    .byte 1, $00, $FF
    .byte 0

; ---------------------------------------------------------------- bg_nmi
; Points HDMA 6-7 at the tables bg_update just finished and enables them.
bg_nmi:
    php
    sep #$20
    rep #$10
    lda bg_ready
    beq @done
    lsr
    bcc :+
    lda bg_front            ; bit0: new scroll table
    eor #1
    sta bg_front
:   lda bg_ready
    and #2
    beq :+
    lda bg_gfront           ; bit1: new gradient table
    eor #1
    sta bg_gfront
:   stz bg_ready
    lda #$03                ; ch6: direct, mode 3 (p, p, p+1, p+1)
    sta DMAP0+$60
    lda #$43                ; ch7: indirect, mode 3
    sta DMAP0+$70
    lda #<BG2HOFS
    sta BBAD0+$60
    lda #<CGADD
    sta BBAD0+$70
    lda #$7E
    sta A1B0+$60
    sta A1B0+$70
    sta DASB0+$70           ; indirect data bank
    lda #$01                ; ch5: direct, mode 1 (WH0, WH1)
    sta DMAP0+$50
    lda #<WH0
    sta BBAD0+$50
    ldx #.loword(hud_win)
    stx A1T0L+$50
    lda #^hud_win
    sta A1B0+$50
    ldx #.loword(bg_stab)
    lda bg_front
    beq :+
    ldx #.loword(bg_stab)+STAB_SIZE
:   stx A1T0L+$60
    ldx #.loword(bg_gtab)
    lda bg_gfront
    beq :+
    ldx #.loword(bg_gtab)+GTAB_SIZE
:   stx A1T0L+$70
    lda bg_hdma_other
    ora #$E0
    sta HDMAEN
@done:
    plp
    rtl

; ---------------------------------------------------------------- bg_update
; Once per frame after the camera moved: bg_cam_x/bg_cam_y (and bg_area_h) -> HDMA tables for the next frame.
bg_update:
    php
    phb
    sep #$20
    rep #$10
    lda bg_loaded
    bne :+
    jmp @out
:   lda #$7E
    pha
    plb                     ; DB = $7E: low RAM + HIBSS reachable with absolute addressing
    rep #$20
    ; d = max(0, area_h - 192) - cam_y, clamped to 0..2047
    lda bg_area_h
    sec
    sbc #192
    bpl :+
    lda #0
:   sec
    sbc bg_cam_y
    bpl :+
    lda #0
:   cmp #2048
    bcc :+
    lda #2047
:   sta bg_d
    ; underwater line on screen
    lda bg_wnot
    beq @nowater
    eor #$FFFF
    sec
    sbc bg_cam_y
    bpl :+
    lda #0
:   cmp #192
    bcc :+
@nowater:
    lda #192
:   sta bg_yw
    ; ---- per layer: hx = ((cam_x & $7FF) * num >> 3) & $FF, ly = base + (d * num >> 4) ----
    lda bg_cam_x
    and #$07FF
    sta bg_mx
    asl
    sta bg_mx+2
    adc bg_mx
    sta bg_mx+4
    lda bg_mx+2
    asl
    sta bg_mx+6
    lda bg_d
    sta bg_md
    asl
    sta bg_md+2
    adc bg_md
    sta bg_md+4
    lda bg_md+2
    asl
    sta bg_md+6
    ldx #0
@lay:
    txa
    cmp bg_nlay
    bcs @laydone
    lda .loword(bg_hdr)+BGV_LNUM,x
    and #$00FF
    asl
    tay                     ; num*2
    lda bg_mx-2,y
    lsr
    lsr
    lsr
    and #$00FF
    sta bgz_a
    lda bg_md-2,y
    lsr
    lsr
    lsr
    lsr
    sta bgz_b
    txa
    asl
    tay
    lda bgz_a
    sta bg_hx,y
    lda .loword(bg_hdr)+BGV_LBASE,y
    clc
    adc bgz_b
    sta bg_ly,y
    inx
    bra @lay
@laydone:
    ; band layout depends only on the layer rows (ly) and the water line: rebuild the runs (and the gradient
    ; table) when one of them changed, but at most every 4th frame while the camera keeps moving vertically
    ; (in between the old bands are kept and only their scroll values follow the camera)
    lda bg_yw
    cmp bg_last_yw
    bne @changed
    ldx #0
:   txa
    lsr
    cmp bg_nlay
    bcs :+
    lda bg_ly,x
    cmp bg_last_ly,x
    bne @changed
    inx
    inx
    bra :-
:   lda bg_since
    cmp #3
    bcs :+
    inc bg_since
:   jmp @scroll
@changed:
    lda bg_since
    cmp #3
    bcs @full
    inc bg_since
    jmp @scroll
@full:
    stz bg_since
    lda bg_yw
    sta bg_last_yw
    ldx #14
:   lda bg_ly,x
    sta bg_last_ly,x
    dex
    dex
    bpl :-
    ; ---- every segment -> screen interval [first, end) clipped to its region = two events ----
    stz bgz_s
@cseg:
    lda bgz_s
    cmp bg_nseg
    bcc :+
    jmp @cdone
:   tax                     ; X = s
    asl
    asl
    sta bgz_r               ; event slot offset (4s: start, 4s+2: end)
    txa
    asl
    tay                     ; Y = 2s
    lda .loword(bg_hdr)+BGV_SFLAGS,x
    lsr
    bcc :+
    lda bg_yw               ; underwater segment: [yw, 192)
    sta bgz_a
    lda #192
    sta bgz_b
    bra :++
:   stz bgz_a               ; dry segment: [0, yw)
    lda bg_yw
    sta bgz_b
:   lda .loword(bg_hdr)+BGV_SLAYER,x
    and #$00FF
    asl
    tax
    lda .loword(bg_hdr)+BGV_SR0,y
    clc
    adc bg_ly,x
    cmp bgz_a
    bpl :+
    lda bgz_a
:   sta bgz_c
    lda .loword(bg_hdr)+BGV_SR1,y
    clc
    adc bg_ly,x
    cmp bgz_b
    bmi :+
    lda bgz_b
:   cmp bgz_c
    beq :+
    bpl :++
:   lda #999                ; not visible: both events sort to the end
    sta bgz_c
:   ldx bgz_r
    sta .loword(bg_evy)+2,x
    lda bgz_c
    sta .loword(bg_evy),x
    inc bgz_s
    jmp @cseg
@cdone:
    ; ---- insertion sort of the event order (nearly sorted from the last frame) ----
    lda bg_nseg
    asl
    asl
    sta bgz_nev
    ldx #2
@is:
    cpx bgz_nev
    bcs @isdone
    stx bgz_k
    lda .loword(bg_order),x
    sta bgz_b
    tay
    lda .loword(bg_evy),y
    sta bgz_a
    txy
@isl:
    lda .loword(bg_order)-2,y
    tax
    lda .loword(bg_evy),x
    cmp bgz_a
    bcc @isput
    beq @isput
    txa
    sta .loword(bg_order),y
    dey
    dey
    bne @isl
@isput:
    lda bgz_b
    sta .loword(bg_order),y
    ldx bgz_k
    inx
    inx
    bra @is
@isdone:
    ; ---- sweep: between events the owner is the highest active segment (= painter order) ----
    stz bgz_m0
    stz bgz_m1
    stz bgz_y
    stz bgz_rn
    ldx #0
@sw:
    cpx bgz_nev
    bcs @swend
    stx bgz_k
    lda .loword(bg_order),x
    sta bgz_b
    tax
    lda .loword(bg_evy),x
    cmp #192
    bcs @swend
    cmp bgz_y
    beq :+
    sta bgz_e
    jsr add_runs
:   lda bgz_b               ; toggle the segment bit
    lsr
    lsr
    sta bgz_s
    and #$000F
    asl
    tax
    lda f:mask_tab,x
    sta bgz_c
    lda bgz_b
    and #$0002
    bne @tend
    lda bgz_s
    cmp #16
    bcs :+
    lda bgz_m0
    ora bgz_c
    sta bgz_m0
    bra @tdone
:   lda bgz_m1
    ora bgz_c
    sta bgz_m1
    bra @tdone
@tend:
    lda bgz_c
    eor #$FFFF
    sta bgz_c
    lda bgz_s
    cmp #16
    bcs :+
    lda bgz_m0
    and bgz_c
    sta bgz_m0
    bra @tdone
:   lda bgz_m1
    and bgz_c
    sta bgz_m1
@tdone:
    ldx bgz_k
    inx
    inx
    bra @sw
@swend:
    lda #192
    sta bgz_e
    jsr add_runs
    stz bgz_m0              ; HUD lines 192-223: transparent
    stz bgz_m1
    lda #224
    sta bgz_e
    jsr add_runs
    lda bgz_rn
    sta bg_nruns
    ; ---- gradient table (back buffer) from the runs ----
    sep #$20
    lda bg_gfront
    rep #$20
    bne :+
    lda #.loword(bg_gtab)+GTAB_SIZE
    bra :++
:   lda #.loword(bg_gtab)
:   sta bgz_gt
    clc
    adc #GTAB_SIZE-4
    sta bgz_glim
    ldy #0
@grun:
    cpy bg_nruns
    bcc :+
    jmp @gdone
:   sty bgz_r
    lda .loword(bg_run_y0),y
    cmp #192
    bcc :+
    jmp @gdone
:   sta bgz_y
    lda .loword(bg_run_s),y
    cmp #$00FF
    beq @ggrad
    tax
    asl
    tay                     ; Y = 2s
    lda .loword(bg_hdr)+BGV_SFILL,y
    cmp #$FFFF
    beq @ggrad
    ; hole fill records: fill + (y0 - ly - r0)
    clc
    adc bgz_y
    sec
    sbc .loword(bg_hdr)+BGV_SR0,y
    ldy bgz_r
    ldx .loword(bg_run_l),y
    sec
    sbc bg_ly,x
    asl
    asl
    clc
    adc #.loword(bg_fillrec)
    sta bgz_gd
    lda .loword(bg_run_y1),y
    sec
    sbc bgz_y
    jsr emit_fill
    bra @gnext
@ggrad:
    ldy bgz_r
    lda .loword(bg_run_y1),y
    sta bgz_gc              ; run end
    lda bgz_y               ; dry part [y0, min(end, yw))
    cmp bg_yw
    bcs @gwet
    lda bgz_gc
    cmp bg_yw
    bcc :+
    lda bg_yw
:   sta bgz_gb
    lda bgz_y
    asl
    asl
    adc #.loword(bg_gline_dry)
    sta bgz_gd
    lda bgz_gb
    sec
    sbc bgz_y
    jsr emit_fill
    lda bgz_gb
    sta bgz_y
@gwet:                      ; wet part [max(y0, yw), end)
    lda bgz_gc
    sec
    sbc bgz_y
    beq @gnext
    bmi @gnext
    pha
    lda bgz_y
    asl
    asl
    adc #.loword(bg_gline_wet)
    sta bgz_gd
    pla
    jsr emit_fill
@gnext:
    ldy bgz_r
    iny
    iny
    jmp @grun
@gdone:
    lda #.loword(bg_black)
    sta bgz_gd
    lda #32
    jsr emit_gent
    ldx bgz_gt
    sep #$20
    stz a:0,x
    lda bg_ready
    ora #2
    sta bg_ready
    rep #$20
@scroll:
    ; ---- scroll table (back buffer), one entry per run: HOFS/VOFS follow the camera every frame ----
    sep #$20
    lda bg_front
    rep #$20
    bne :+
    ldx #.loword(bg_stab)+STAB_SIZE
    bra :++
:   ldx #.loword(bg_stab)
:   ldy #0
@sr:
    cpy bg_nruns
    bcs @sd
    sep #$20
    lda .loword(bg_run_y1),y
    sec
    sbc .loword(bg_run_y0),y
    sta a:0,x
    rep #$20
    lda .loword(bg_run_s),y
    cmp #$00FF
    bne @srs
    stz a:1,x               ; transparent: VOFS precomputed
    lda .loword(bg_run_v),y
    sta a:3,x
    bra @srn
@srs:
    phx
    ldx .loword(bg_run_l),y
    lda bg_hx,x
    sta bgz_h
    lda .loword(bg_run_v),y
    sec
    sbc bg_ly,x
    and #$03FF
    plx
    sta a:3,x
    lda bgz_h
    sta a:1,x
@srn:
    txa
    clc
    adc #5
    tax
    iny
    iny
    bra @sr
@sd:
    sep #$20
    stz a:0,x
    lda bg_ready
    ora #1
    sta bg_ready
@out:
    plb
    plp
    rtl

; add_runs: runs for [bgz_y, bgz_e) owned by the highest segment in bgz_m1:bgz_m0 ($FF = none); merges with the
; previous run when possible, splits at 127 lines (and at TLEN for transparent runs). Leaves bgz_y = bgz_e. A16 X16.
add_runs:
    .a16
    .i16
    lda bgz_m1
    beq :+
    jsr hibit
    clc
    adc #16
    bra @w
:   lda bgz_m0
    beq @none
    jsr hibit
    bra @w
@none:
    lda #$00FF
@w: sta bgz_s
    cmp #$00FF
    bne @seglim
    lda .loword(bg_hdr)+BGV_TLEN
    cmp #127
    bcc :+
@seglim:
    lda #127
:   sta bgz_lim
@loop:
    lda bgz_e
    sec
    sbc bgz_y
    beq :+
    bpl :++
:   jmp @done
:
    sta bgz_n               ; lines left
    ldy bgz_rn
    beq @new
    lda bgz_y
    cmp #192
    beq @new                ; never merge into the HUD
    lda .loword(bg_run_s)-2,y
    cmp bgz_s
    bne @new
    lda .loword(bg_run_y1)-2,y
    cmp bgz_y
    bne @new
    sec
    sbc .loword(bg_run_y0)-2,y
    eor #$FFFF
    sec
    adc bgz_lim             ; room = limit - length
    beq @new
    cmp bgz_n
    bcc :+
    lda bgz_n
:   clc
    adc bgz_y
    sta bgz_y
    sta .loword(bg_run_y1)-2,y
    bra @loop
@new:
    cpy #2*MAXRUNS
    bcs @done               ; full (never expected)
    lda bgz_n
    cmp bgz_lim
    bcc :+
    lda bgz_lim
:   clc
    adc bgz_y
    sta .loword(bg_run_y1),y
    lda bgz_y
    sta .loword(bg_run_y0),y
    lda bgz_s
    sta .loword(bg_run_s),y
    cmp #$00FF
    bne @rseg
    lda .loword(bg_hdr)+BGV_TROW    ; transparent: VOFS so this run shows the empty map rows
    sec
    sbc bgz_y
    sec
    sbc #VADJ
    and #$03FF
    sta .loword(bg_run_v),y
    bra @rec
@rseg:
    tax
    asl
    sta bgz_t
    lda .loword(bg_hdr)+BGV_SLAYER,x
    and #$00FF
    asl
    sta .loword(bg_run_l),y
    ldx bgz_t
    lda .loword(bg_hdr)+BGV_SVBASE,x
    sec
    sbc #VADJ
    sta .loword(bg_run_v),y     ; VOFS = this - ly[layer]
@rec:
    lda .loword(bg_run_y1),y
    sta bgz_y
    iny
    iny
    sty bgz_rn
    jmp @loop
@done:
    lda bgz_e
    sta bgz_y
    rts

; hibit: A = non-zero word -> index of its highest set bit. A16 X16.
hibit:
    .a16
    .i16
    cmp #$0100
    bcc @lo
    xba
    and #$00FF
    tax
    lda f:hib_tab,x
    and #$00FF
    clc
    adc #8
    rts
@lo:
    tax
    lda f:hib_tab,x
    and #$00FF
    rts

hib_tab:
    .repeat 256, I
    .byte (I >= 2) + (I >= 4) + (I >= 8) + (I >= 16) + (I >= 32) + (I >= 64) + (I >= 128)
    .endrepeat
mask_tab:
    .repeat 16, I
    .word 1 << I
    .endrepeat

; emit_gent: gradient (indirect) entries for A lines holding the record at bgz_gd (no repeat). A16 X16.
emit_gent:
    .a16
    .i16
    sta bgz_gm
@loop:
    lda bgz_gm
    beq @done
    cmp #128
    bcc :+
    lda #127
:   ldx bgz_gt
    cpx bgz_glim
    bcs @done
    sep #$20
    sta a:0,x
    rep #$20
    and #$00FF
    pha
    lda bgz_gd
    sta a:1,x
    inx
    inx
    inx
    stx bgz_gt
    pla
    eor #$FFFF
    sec
    adc bgz_gm
    sta bgz_gm
    bra @loop
@done:
    rts

; emit_fill: A lines of per-line records starting at bgz_gd (HDMA repeat mode: the pointer advances 4 per line).
emit_fill:
    .a16
    .i16
    sta bgz_gm
@loop:
    lda bgz_gm
    beq @done
    cmp #128
    bcc :+
    lda #127
:   ldx bgz_gt
    cpx bgz_glim
    bcs @done
    sep #$20
    ora #$80
    sta a:0,x
    rep #$20
    and #$007F
    pha
    lda bgz_gd
    sta a:1,x
    inx
    inx
    inx
    stx bgz_gt
    pla
    pha
    asl
    asl
    clc
    adc bgz_gd
    sta bgz_gd
    pla
    eor #$FFFF
    sec
    adc bgz_gm
    sta bgz_gm
    bra @loop
@done:
    rts

