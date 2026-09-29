; Battery-backed saves (engine agent) — generic, game-agnostic SRAM record store (reusable kit code).
; SRAM (8 KB, SRAMBSS at $F00000) holds SAVE_RECS records (0-2 = the three file slots, 3 = settings), each stored
; twice (copy A/B) so a power loss in the middle of a write never loses the previous save: a write goes to the copy
; that is NOT the newest valid one, with a higher sequence number. Each copy = 8-byte header + SAVE_DATA bytes:
;   +0 'S','4' magic  +2 version  +3 sequence (wraps)  +4 checksum (16-bit rotate-add of the data words)
;   +6 complement of the checksum  +8 data
; The data layout belongs to the caller (the screens module: C# SaveData fields); the engine only moves bytes.
; API (JSL/RTL, any A width on entry, XY16, DB = $80; A = record number 0..3; flags preserved except carry):
;   eng_save_load  -> carry set = a valid copy was found and copied to eng_save_buf; carry clear = none
;                     (eng_save_buf zero-filled)
;   eng_save_store -> writes eng_save_buf (SAVE_DATA bytes) into the record
;   eng_save_erase -> invalidates both copies
;   eng_save_valid -> carry set if the record holds a valid save (eng_save_buf untouched)
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"

.export eng_save_load, eng_save_store, eng_save_erase, eng_save_valid, eng_save_buf, SAVE_DATA, SAVE_RECS

SAVE_DATA = 960             ; bytes of payload per record (keep even)
SAVE_RECS = 4
SAVE_VER = 1
COPY_SIZE = SAVE_DATA + 8

.segment "SRAMBSS"
eng_sram: .res SAVE_RECS * 2 * COPY_SIZE   ; 7744 of 8192 bytes

.segment "HIBSS"
eng_save_buf: .res SAVE_DATA

.segment "BSS"
sv_rec: .res 2
sv_base: .res 2             ; offset of the copy being checked/written inside eng_sram
sv_sum: .res 2
sv_seq: .res 2
sv_best: .res 2             ; offset of the newest valid copy, $FFFF = none
sv_bseq: .res 2

.segment "CODE13"
.a16
.i16

; A = record -> sv_rec, X = offset of copy A
rec_base:
    and #$0003
    sta sv_rec
    ; rec * 2 * COPY_SIZE
    asl a
    tax
    lda f:rec_offs,x
    tax
    rts
rec_offs: .word 0, 2*COPY_SIZE, 4*COPY_SIZE, 6*COPY_SIZE

; X = copy offset -> carry set if the copy is valid, sv_sum = its sequence byte. Keeps X.
check_copy:
    stx sv_base
    lda f:eng_sram,x
    cmp #('S'|('4'<<8))
    bne @no
    lda f:eng_sram+2,x
    and #$00FF
    cmp #SAVE_VER
    bne @no
    lda f:eng_sram+4,x
    eor f:eng_sram+6,x
    cmp #$FFFF
    bne @no
    jsr data_sum                ; sv_sum
    ldx sv_base
    lda sv_sum
    cmp f:eng_sram+4,x
    bne @no
    lda f:eng_sram+3,x
    and #$00FF
    sta sv_seq
    sec
    rts
@no: ldx sv_base
    clc
    rts

; checksum of the SRAM data at sv_base -> sv_sum
data_sum:
    stz sv_sum
    lda sv_base
    clc
    adc #8
    tax
    ldy #SAVE_DATA/2
@l: lda sv_sum
    asl a
    adc #0                      ; rotate left
    clc
    adc f:eng_sram,x
    sta sv_sum
    inx
    inx
    dey
    bne @l
    rts

; newest valid copy of record A -> sv_best (offset) / carry
find_best:
    jsr rec_base
    lda #$FFFF
    sta sv_best
    jsr check_copy
    bcc :+
    stx sv_best
    lda sv_seq
    sta sv_bseq
:   txa
    clc
    adc #COPY_SIZE
    tax
    jsr check_copy
    bcc @done
    lda sv_best
    bmi @take
    ; both valid: the newer sequence wins (8-bit wrap: (b - a) & $FF in 1..127 = b newer)
    lda sv_seq
    sec
    sbc sv_bseq
    and #$00FF
    beq @done
    cmp #$80
    bcs @done
@take:
    stx sv_best
    lda sv_seq
    sta sv_bseq
@done:
    lda sv_best
    bmi @none
    sec
    rts
@none: clc
    rts

eng_save_valid:
    php
    rep #$30
    jsr find_best
    bcs @y
    plp
    clc
    rtl
@y: plp
    sec
    rtl

eng_save_load:
    php
    rep #$30
    jsr find_best
    bcc @empty
    lda sv_best
    clc
    adc #8
    tax
    ldy #0
@c: lda f:eng_sram,x
    phx
    tyx
    sta f:eng_save_buf,x
    plx
    inx
    inx
    iny
    iny
    cpy #SAVE_DATA
    bcc @c
    plp
    sec
    rtl
@empty:
    ldx #0
    lda #0
@z: sta f:eng_save_buf,x
    inx
    inx
    cpx #SAVE_DATA
    bcc @z
    plp
    clc
    rtl

eng_save_store:
    php
    rep #$30
    pha
    jsr find_best               ; the copy to overwrite = the other one
    pla
    jsr rec_base                ; X = copy A
    lda #1
    sta sv_seq
    lda sv_best
    bmi @write                  ; none valid: copy A, seq 1
    lda sv_bseq
    inc a
    and #$00FF
    sta sv_seq
    cpx sv_best
    bne @write
    txa
    clc
    adc #COPY_SIZE
    tax
@write:
    stx sv_base
    ; invalidate first (magic), then data, then header
    lda #0
    sta f:eng_sram,x
    txa
    clc
    adc #8
    tax
    ldy #0
@c: phx
    tyx
    lda f:eng_save_buf,x
    plx
    sta f:eng_sram,x
    inx
    inx
    iny
    iny
    cpy #SAVE_DATA
    bcc @c
    jsr data_sum
    ldx sv_base
    lda sv_sum
    sta f:eng_sram+4,x
    eor #$FFFF
    sta f:eng_sram+6,x
    sep #$20
    .a8
    lda #SAVE_VER
    sta f:eng_sram+2,x
    lda sv_seq
    sta f:eng_sram+3,x
    rep #$20
    .a16
    lda #('S'|('4'<<8))
    sta f:eng_sram,x
    plp
    rtl

eng_save_erase:
    php
    rep #$30
    jsr rec_base
    lda #0
    sta f:eng_sram,x
    txa
    clc
    adc #COPY_SIZE
    tax
    lda #0
    sta f:eng_sram,x
    plp
    rtl
