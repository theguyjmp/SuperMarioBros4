; Screens text canvas (screens agent): BG3 is a 256x224 2bpp pixel canvas (896 tiles at VRAM $4000, identity
; tilemap at $5C00) so text lands on any pixel like the C# Hud.Txt: 1-px drop shadow (value 1), top rows 0-2
; (value 2) and body rows 3-7 (value 3) of the canvas palette (4 palettes = CGRAM 0-15, one per 8x8 cell, last
; writer wins). Glyphs: gen scr_glyphs (ASCII 32-95). Dirty tile rows are uploaded by txt_nmi.
.p816
.smart
.include "scr.inc"

.export txt_nmi, txt_init_scene, txt_print_inline, txt_printc_inline, txt_w, txt_h, sb_stri
.import scr_glyphs

TXT_ROWS = 28

.segment "ZEROPAGE"
txt_sp: .res 3                 ; far pointer to the string being printed

.segment "HIBSS"
txt_canvas: .res 896*16
txt_mapbuf: .res 32*TXT_ROWS*2

.segment "BSS"
txt_x: .res 2
txt_y: .res 2
txt_w: .res 2
txt_h: .res 2
txt_pal: .res 2
txt_dirty: .res TXT_ROWS*2
txt_used: .res TXT_ROWS*2        ; row holds text (clears of empty rows are skipped)
txt_mapd: .res 2               ; bit0 rows 0-13, bit1 rows 14-27
txt_len: .res 2
txt_cx: .res 2
txt_mode: .res 2               ; 0 shadow pass, 2 body pass
txt_gofs: .res 2
txt_sh: .res 2
txt_col: .res 2
txt_r: .res 2
txt_off: .res 2
txt_v: .res 2
txt_i: .res 2
sb_len: .res 2
sb_buf: .res 64

.segment "CODE11"
.a16
.i16

; ------------------------------------------------------------------ scene setup (forced blank)
; clears the canvas (WRAM), identity tilemap with palette 0 + priority, uploads the map now (VRAM CHR was cleared)
txt_init_scene:
    php
    rep #$30
    ; zero the canvas in WRAM by DMA (fixed source)
    sep #$20
    .a8
    lda #$08
    sta DMAP0
    lda #<WMDATA
    sta BBAD0
    ldx #.loword(zero1)
    stx A1T0L
    lda #^zero1
    sta A1B0
    ldx #.loword(txt_canvas)
    stx WMADDL
    lda #^txt_canvas
    sta WMADDL+2
    ldx #896*16
    stx DAS0L
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    ldx #0
    lda #$2000
@m: sta f:txt_mapbuf,x
    inc a
    inx
    inx
    cpx #32*TXT_ROWS*2
    bcc @m
    ldx #0
@d: stz txt_dirty,x
    stz txt_used,x
    inx
    inx
    cpx #TXT_ROWS*2
    bcc @d
    stz txt_mapd
    stz txt_pal
    ; map -> VRAM now
    sep #$20
    .a8
    lda #$80
    sta VMAIN
    ldx #SCR_VRAM_TXTMAP
    stx VMADDL
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    ldx #.loword(txt_mapbuf)
    stx A1T0L
    lda #^txt_mapbuf
    sta A1B0
    ldx #32*TXT_ROWS*2
    stx DAS0L
    lda #1
    sta MDMAEN
    plp
    rtl
zero1: .byte 0

; txt_clear_all: empty canvas (queued upload of every row)
txt_clear_all:
    php
    rep #$30
    lda #0
    ldy #896*16
    jsr fill0
    ldx #0
    lda #1
@d: sta txt_dirty,x
    stz txt_used,x
    inx
    inx
    cpx #TXT_ROWS*2
    bcc @d
    plp
    rtl

; txt_flush_now: forced blank: upload the dirty rows + map at once
txt_flush_now:
    php
    rep #$30
    lda #$FFFF
    sta txt_budget
    jsr upload
    plp
    rtl

; txt_nmi: uploads within a budget (called from scr_nmi, A8 XY16 in, any out)
txt_nmi:
    php
    rep #$30
    sta txt_budget              ; A = byte budget left by the caller
    jsr upload
    plp
    rtl

upload:
    .a16
    sep #$20
    .a8
    lda #$80
    sta VMAIN
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    lda #^txt_canvas
    sta A1B0
    rep #$20
    .a16
    ; map halves (896 bytes each)
    lda txt_mapd
    and #1
    beq @m2
    ldx #SCR_VRAM_TXTMAP
    ldy #.loword(txt_mapbuf)
    jsr up896
    bcs @done
    lda txt_mapd
    and #$FFFE
    sta txt_mapd
@m2:
    lda txt_mapd
    and #2
    beq @rows
    ldx #SCR_VRAM_TXTMAP+14*32
    ldy #.loword(txt_mapbuf)+14*64
    jsr up896
    bcs @done
    lda txt_mapd
    and #$FFFD
    sta txt_mapd
@rows:
    ldx #0
@r: lda txt_dirty,x
    beq @nx
    lda txt_budget
    cmp #512
    bcc @done
    sbc #512
    sta txt_budget
    stz txt_dirty,x
    txa                         ; row*2 -> VRAM $4000 + row*256, canvas + row*512
    xba
    lsr a
    clc
    adc #SCR_VRAM_TXTCHR
    sta VMADDL
    txa
    xba
    clc
    adc #.loword(txt_canvas)
    sta A1T0L
    lda #512
    sta DAS0L
    sep #$20
    .a8
    lda #1
    sta MDMAEN
    rep #$20
    .a16
@nx: inx
    inx
    cpx #TXT_ROWS*2
    bcc @r
@done:
    rts
; X = VRAM word address, Y = source (in txt bank); carry set = over budget
up896:
    lda txt_budget
    cmp #896
    bcc @no
    sbc #896
    sta txt_budget
    stx VMADDL
    sty A1T0L
    lda #896
    sta DAS0L
    sep #$20
    .a8
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    clc
    rts
@no: sec
    rts
.segment "BSS"
txt_budget: .res 2
.segment "CODE11"
.a16
.i16

; ------------------------------------------------------------------ printing
; txt_print: string at far pointer txt_sp (0-terminated), at txt_x/txt_y (pixels), canvas palette txt_pal (0-3)
txt_print:
    php
    rep #$30
    ldy #0
@n: lda [txt_sp],y
    and #$00FF
    beq @nd
    iny
    bra @n
@nd: sty txt_len
    jsr draw_string
    plp
    rtl

; txt_printc: like txt_print, centered: x = (256 - len*8) / 2 (C# Hud.TxtC)
txt_printc:
    php
    rep #$30
    ldy #0
@n: lda [txt_sp],y
    and #$00FF
    beq @nd
    iny
    bra @n
@nd: sty txt_len
    jsr center
    jsr draw_string
    plp
    rtl

center:
    lda txt_len
    asl a
    asl a
    asl a
    eor #$FFFF
    sec
    adc #256
    lsr a
    sta txt_x
    rts

; txt_print_inline / txt_printc_inline: the string follows the JSL (use the PRINT/PRINTC macros)
txt_print_inline:
    php
    rep #$30
    jsr inline_ptr
    jsr draw_string
    jsr inline_ret
    plp
    rtl
txt_printc_inline:
    php
    rep #$30
    jsr inline_ptr
    jsr center
    jsr draw_string
    jsr inline_ret
    plp
    rtl

; stack at entry of these helpers: 1-2 = rts addr, 3 = P (php), 4-6 = RTL address (last byte of the JSL)
inline_ptr:
    lda 4,s
    inc a
    sta txt_sp
    sep #$20
    .a8
    lda 6,s
    sta txt_sp+2
    rep #$20
    .a16
    ldy #0
@n: lda [txt_sp],y
    and #$00FF
    beq @nd
    iny
    bra @n
@nd: sty txt_len
    rts
inline_ret:
    ; the RTL address becomes the terminator's address (RTL adds 1)
    lda txt_sp
    clc
    adc txt_len
    sta 4,s
    rts

; sb_stri: append the inline string that follows the JSL to sb_buf
sb_stri:
    php
    rep #$30
    jsr inline_ptr
    ldy #0
@c: cpy txt_len
    beq @d
    lda [txt_sp],y
    and #$00FF
    jsr sb_put
    iny
    bra @c
@d: jsr inline_ret
    plp
    rtl

; draw txt_len chars of [txt_sp] at txt_x, txt_y with txt_pal
draw_string:
    lda txt_len
    bne :+
    rts
:   ; palette of the cells: rows y>>3 .. (y+8)>>3, cols x>>3 .. (x+len*8)>>3
    jsr set_pal_cells
    jsr pass
    ; dirty rows
    lda txt_y
    lsr a
    lsr a
    lsr a
    cmp #TXT_ROWS
    bcs @r
    asl a
    tax
    lda #1
    sta txt_dirty,x
    sta txt_used,x
    cpx #(TXT_ROWS-1)*2
    bcs @r
    sta txt_dirty+2,x
    sta txt_used+2,x
@r: rts

; One pass per character (shadow then body of each glyph gives the same pixels as the C# "all shadows, then all
; glyphs": a glyph's shadow only reaches right/down, where the next glyph's body is drawn afterwards).
pass:
    lda txt_x
    sta txt_cx
    ; offset of pixel row txt_y, column 0: (y>>3)*512 + (y&7)*2
    lda txt_y
    and #7
    asl a
    sta txt_off
    lda txt_y
    and #$FFF8
    xba
    lsr a
    lsr a
    clc
    adc txt_off
    sta txt_row0
    stz txt_i
@c: lda txt_i
    cmp txt_len
    bcs @d
    tay
    lda [txt_sp],y
    and #$00FF
    cmp #'a'
    bcc :+
    cmp #'z'+1
    bcs :+
    sbc #31                     ; lower -> upper (carry clear: -32)
:   sec
    sbc #32
    cmp #64
    bcs @sk
    jsr draw_char
@sk: lda txt_cx
    clc
    adc #8
    sta txt_cx
    inc txt_i
    bra @c
@d: rts

; A = glyph index; txt_cx, txt_y, txt_row0
draw_char:
    asl a
    asl a
    sta txt_v                   ; *4
    asl a
    asl a
    asl a
    clc
    adc txt_v                   ; *36
    sta txt_gofs
    lda txt_cx
    cmp #256
    jcs @out
    and #7
    sta txt_sh
    lda txt_cx
    and #$00F8
    asl a                       ; col * 16
    clc
    adc txt_row0
    sta txt_off
    lda txt_cx
    cmp #248
    lda #0
    rol a
    sta txt_last                ; 1 = no cell to the right
    lda txt_y
    sta txt_yy
    stz txt_r
@row:
    lda txt_yy
    cmp #224
    jcs @out
    ldx txt_gofs
    lda f:scr_glyphs,x          ; shadow
    sta txt_s16
    lda f:scr_glyphs+2,x        ; body
    sta txt_m16
    ora txt_s16
    beq @next
    ldy txt_sh
    beq @ns
@s: lsr txt_s16
    lsr txt_m16
    dey
    bne @s
@ns:
    sep #$20
    .a8
    ldx txt_off
    lda txt_s16+1
    sta txt_sb
    lda txt_m16+1
    sta txt_mb
    jsr apply2
    lda txt_last
    bne @nr
    rep #$20
    .a16
    lda txt_off
    clc
    adc #16
    tax
    sep #$20
    .a8
    lda txt_s16
    sta txt_sb
    lda txt_m16
    sta txt_mb
    jsr apply2
@nr: rep #$20
    .a16
@next:
    lda txt_gofs
    clc
    adc #4
    sta txt_gofs
    ; next pixel row: +2, or +512-14 when leaving the tile row
    inc txt_yy
    lda txt_yy
    and #7
    bne :+
    lda txt_off
    clc
    adc #512-14
    sta txt_off
    bra :++
:   inc txt_off
    inc txt_off
:   inc txt_r
    lda txt_r
    cmp #9
    jcc @row
@out:
    rts

; A8: X = canvas offset of plane 0 (plane 1 at +1), txt_sb = shadow mask, txt_mb = glyph mask, txt_r = glyph row
;   p1 = (p1 & ~s) | m ; p0 = rows 0-2: (p0 | s) & ~m, rows 3-7: p0 | s | m
apply2:
    .a8
    lda f:txt_canvas+1,x
    ora txt_sb
    eor txt_sb
    ora txt_mb
    sta f:txt_canvas+1,x
    lda f:txt_canvas,x
    ora txt_sb
    ora txt_mb
    ldy txt_r
    cpy #3
    bcs :+
    eor txt_mb
:   sta f:txt_canvas,x
    rts
.a16
.segment "BSS"
txt_row0: .res 2
txt_last: .res 2
txt_yy: .res 2
txt_s16: .res 2
txt_m16: .res 2
txt_sb: .res 1
txt_mb: .res 1
.segment "CODE11"
.a16
.i16

set_pal_cells:
    lda txt_pal
    and #7
    xba
    asl a
    asl a
    sta txt_v                   ; pal << 10
    lda txt_y
    lsr a
    lsr a
    lsr a
    sta txt_r                   ; first row
@row:
    lda txt_r
    cmp #TXT_ROWS
    bcs @d
    ; last row = (y+8)>>3
    lda txt_y
    clc
    adc #8
    lsr a
    lsr a
    lsr a
    cmp txt_r
    bcc @d
    lda txt_x
    lsr a
    lsr a
    lsr a
    sta txt_col
@col:
    lda txt_col
    cmp #32
    bcs @nr
    lda txt_len
    asl a
    asl a
    asl a
    clc
    adc txt_x
    lsr a
    lsr a
    lsr a
    cmp txt_col
    bcc @nr
    lda txt_r
    xba
    lsr a
    lsr a                       ; row*64
    sta txt_off
    lda txt_col
    asl a
    clc
    adc txt_off
    tax
    lda f:txt_mapbuf,x
    and #$E3FF
    ora txt_v
    sta f:txt_mapbuf,x
    inc txt_col
    bra @col
@nr: lda txt_r
    cmp #14
    lda #1
    bcc :+
    lda #2
:   ora txt_mapd
    sta txt_mapd
    inc txt_r
    bra @row
@d: rts

; ------------------------------------------------------------------ clearing
; txt_clear: pixel rectangle txt_x, txt_y, txt_w, txt_h -> transparent
txt_clear:
    php
    rep #$30
    lda txt_w
    jeq @out
    lda txt_h
    jeq @out
    lda txt_x
    bne @slow
    lda txt_w
    cmp #256
    bcc @slow
    jsr clear_rows
    plp
    rtl
@slow:
    stz txt_r
@row:
    lda txt_y
    clc
    adc txt_r
    cmp #224
    jcs @out
    pha
    and #7
    asl a
    sta txt_off
    pla
    and #$FFF8
    xba
    lsr a
    lsr a
    clc
    adc txt_off
    sta txt_off                 ; row base (col 0)
    ; dirty
    lda txt_y
    clc
    adc txt_r
    lsr a
    lsr a
    lsr a
    asl a
    tax
    lda #1
    sta txt_dirty,x
    ; columns
    lda txt_x
    sta txt_cx
@col:
    lda txt_x
    clc
    adc txt_w
    sta txt_i                   ; end x (exclusive)
    lda txt_cx
    cmp txt_i
    bcs @nr
    cmp #256
    bcs @nr
    ; mask for this cell: bits from (cx & 7) to min(7, end-1 - cellx)
    and #7
    tax
    lda f:mask_from,x
    and #$00FF
    sta txt_v
    lda txt_cx
    and #$FFF8
    clc
    adc #8
    sta txt_sh                  ; next cell x
    cmp txt_i
    bcc :+
    beq :+
    ; end inside this cell
    lda txt_i
    dec a
    and #7
    tax
    lda f:mask_to,x
    and txt_v
    sta txt_v
:   lda txt_cx
    lsr a
    lsr a
    lsr a
    asl a
    asl a
    asl a
    asl a
    clc
    adc txt_off
    tax
    sep #$20
    .a8
    lda txt_v
    eor #$FF
    sta txt_v+1
    lda f:txt_canvas,x
    and txt_v+1
    sta f:txt_canvas,x
    lda f:txt_canvas+1,x
    and txt_v+1
    sta f:txt_canvas+1,x
    rep #$20
    .a16
    lda txt_sh
    sta txt_cx
    brl @col
@nr: inc txt_r
    lda txt_r
    cmp txt_h
    jcc @row
@out:
    plp
    rtl
mask_from: .byte $FF, $7F, $3F, $1F, $0F, $07, $03, $01
mask_to:   .byte $80, $C0, $E0, $F0, $F8, $FC, $FE, $FF

; ------------------------------------------------------------------ string builder
sb_reset:
    php
    rep #$30
    stz sb_len
    plp
    rtl
sb_char:
    php
    rep #$30
    jsr sb_put
    plp
    rtl
sb_put:
    ldx sb_len
    cpx #63
    bcs :+
    sep #$20
    .a8
    sta sb_buf,x
    stz sb_buf+1,x
    rep #$20
    .a16
    inc sb_len
:   rts

; sb_str: append the 0-terminated string at far pointer txt_sp
sb_str:
    php
    rep #$30
    ldy #0
@c: lda [txt_sp],y
    and #$00FF
    beq @d
    phy
    jsr sb_put
    ply
    iny
    bra @c
@d: plp
    rtl

; sb_dec: A = 0..65535 without leading zeros; sb_dec_pad: A = value, X = width, Y = pad char
sb_dec:
    php
    rep #$30
    ldx #0
    ldy #' '
    bra dec_go
sb_dec_pad:
    php
    rep #$30
dec_go:
    stx txt_w
    sty txt_h
    ; digits into txt_tmpd (reverse)
    ldx #0
@l: sta WRDIVL                 ; divide by 10 (hardware divider)
    sep #$20
    .a8
    lda #10
    sta WRDIVB
    nop
    nop
    nop
    nop
    nop
    nop
    nop
    nop
    rep #$20
    .a16
    lda RDMPYL                  ; remainder
    and #$00FF
    clc
    adc #'0'
    sta txt_tmpd,x
    inx
    inx
    lda RDDIVL                  ; quotient
    bne @l
    ; pad
    txa
    lsr a
    sta txt_i                   ; digits
@p: lda txt_i
    cmp txt_w
    bcs @emit
    lda txt_h
    phx
    jsr sb_put
    plx
    dec txt_w
    bra @p
@emit:
    dex
    dex
    bmi @d
    lda txt_tmpd,x
    phx
    jsr sb_put
    plx
    bra @emit
@d: plp
    rtl
.segment "BSS"
txt_tmpd: .res 12
.segment "CODE11"
.a16
.i16

; sb_bcd7: X = address (bank $00 / WRAM low) of a 4-byte BCD score -> 7 digits with leading zeros (C# PadLeft(7,'0'))
sb_bcd7:
    php
    rep #$30
    stx txt_i
    ldy #6                      ; nibble index 6..0 (byte n>>1, high nibble when n is odd)
@l: tya
    lsr a
    clc
    adc txt_i
    tax
    lda a:0,x
    and #$00FF
    sta txt_v
    tya
    and #1
    beq :+
    lda txt_v
    lsr a
    lsr a
    lsr a
    lsr a
    sta txt_v
:   lda txt_v
    and #$000F
    clc
    adc #'0'
    phy
    jsr sb_put
    ply
    dey
    bpl @l
    plp
    rtl

; txt_print_sb / txt_printc_sb: print sb_buf at txt_x/txt_y (centered)
txt_print_sb:
    php
    rep #$30
    jsr sb_ptr
    jsr draw_string
    plp
    rtl
txt_printc_sb:
    php
    rep #$30
    jsr sb_ptr
    jsr center
    jsr draw_string
    plp
    rtl
sb_ptr:
    lda #.loword(sb_buf)
    sta txt_sp
    sep #$20
    .a8
    stz txt_sp+2
    rep #$20
    .a16
    lda sb_len
    sta txt_len
    rts

.segment "CODE11"
.a16
.i16
; A = canvas offset, Y = bytes: zero-fill by DMA (main thread: the NMI does no DMA while a game frame runs)
fill0:
    sta fl_dst
    sep #$20
    .a8
    lda #$08
    sta DMAP0
    lda #<WMDATA
    sta BBAD0
    ldx #.loword(zero1)
    stx A1T0L
    lda #^zero1
    sta A1B0
    rep #$20
    .a16
    lda fl_dst
    clc
    adc #.loword(txt_canvas)
    sta WMADDL
    sep #$20
    .a8
    lda #^txt_canvas
    sta WMADDL+2
    sty DAS0L
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    rts

; full-width clear of pixel rows txt_y .. txt_y+txt_h: whole tile rows by DMA, partial rows word by word
clear_rows:
    lda txt_y
    sta fl_y
    clc
    adc txt_h
    cmp #224
    bcc :+
    lda #224
:   sta fl_end
@l: lda fl_y
    cmp fl_end
    jcs @d
    ; rows without text need nothing
    lsr a
    lsr a
    lsr a
    asl a
    tax
    lda txt_used,x
    bne :+
    lda fl_y
    and #$FFF8
    clc
    adc #8
    sta fl_y
    bra @l
:   lda #1
    sta txt_dirty,x
    lda fl_y
    and #7
    bne @px
    lda fl_y
    clc
    adc #8
    cmp fl_end
    beq :+
    bcs @px
:   ; whole tile row
    stz txt_used,x
    lda fl_y
    xba
    lsr a
    lsr a                       ; (y>>3)*512 = y*64
    ldy #512
    jsr fill0
    lda fl_y
    clc
    adc #8
    sta fl_y
    bra @l
@px: ; one pixel row: 32 cells x 2 bytes
    lda fl_y
    and #7
    asl a
    sta fl_dst
    lda fl_y
    and #$FFF8
    xba
    lsr a
    lsr a
    clc
    adc fl_dst
    tax
    ldy #32
    lda #0
@c: sta f:txt_canvas,x
    txa
    clc
    adc #16
    tax
    lda #0
    dey
    bne @c
    inc fl_y
    brl @l
@d: rts
.segment "BSS"
fl_dst: .res 2
fl_y: .res 2
fl_end: .res 2
.segment "CODE11"
.a16
.i16
; txt_printn: A = count of characters at far pointer txt_sp (no terminator needed), at txt_x/txt_y/txt_pal
.export txt_printn
.exportzp txt_sp
txt_printn:
    php
    rep #$30
    sta txt_len
    jsr draw_string
    plp
    rtl
