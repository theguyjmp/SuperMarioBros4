; Boot, NMI, main loop. Owner: engine agent (see snes/DESIGN.md).
.p816
.smart
.include "snes.inc"

.import game_init, game_frame, eng_nmi
.import spr_nmi, bg_nmi, snd_init
.export frame_count, pad1, pad1_new, pad1_old, pad2, pad2_new, nmi_ready, wait_nmi

.segment "ZEROPAGE"
frame_count: .res 2
pad1:        .res 2     ; held (JOY1 layout, see snes.inc PAD_*)
pad1_new:    .res 2     ; pressed this frame
pad1_old:    .res 2
pad2:        .res 2
pad2_new:    .res 2
pad2_old:    .res 2
nmi_ready:   .res 1     ; main loop sets 1 when a frame's buffers are ready for the NMI upload
in_nmi:      .res 1

.segment "CODE"
reset:
    sei
    clc
    xce                 ; native mode
    AXY16
    ldx #$1FFF
    txs
    lda #$0000
    tcd                 ; D = 0
    A8
    lda #$80
    pha
    plb                 ; DB = $80
    jml fast_start      ; jump into FastROM bank $80
fast_start:
    lda #$01
    sta MEMSEL          ; FastROM
    lda #$8F
    sta INIDISP         ; forced blank
    stz NMITIMEN
    ; clear PPU registers $2101-$2133
    ldx #$2101
@clrppu:
    stz $00,x
    inx
    cpx #$2134
    bne @clrppu
    lda #$80
    sta INIDISP
    lda #$30
    sta CGWSEL
    lda #$E0
    sta COLDATA
    ; clear WRAM $0000-$1FFF except stack page tail and all of $7E2000-$7FFFFF via DMA fill
    jsr clear_memory
    jsl snd_init
    jsl game_init
    lda #$81
    sta NMITIMEN        ; NMI + auto-joypad
@loop:
    jsl game_frame      ; one game tick; sets nmi_ready when OAM/VRAM queues are complete
    jsr wait_nmi
    bra @loop

; Wait for the next NMI (vblank uploads happen there).
wait_nmi:
    lda #1
    sta nmi_ready
@w: lda nmi_ready
    bne @w
    rts

clear_memory:
    ; WRAM zero fill by DMA from a fixed zero byte. Skips $1E00-$1FFF (the stack we are running on).
    lda #$08            ; fixed source, B-bus single register
    sta DMAP0
    lda #<WMDATA
    sta BBAD0
    ldx #.loword(zero_byte)
    stx A1T0L
    lda #^zero_byte
    sta A1B0
    stz WMADDL
    stz WMADDL+1
    stz WMADDL+2
    ldx #$1E00          ; $7E0000-$7E1DFF
    stx DAS0L
    lda #$01
    sta MDMAEN
    ldx #$2000
    stx WMADDL
    stz WMADDL+2
    ldx #$E000          ; $7E2000-$7EFFFF
    stx DAS0L
    sta MDMAEN
    ldx #$0000
    stx WMADDL
    lda #$01
    sta WMADDL+2
    ldx #$0000          ; 64 KB: $7F0000-$7FFFFF
    stx DAS0L
    sta MDMAEN
    ; VRAM clear (64 KB)
    lda #$80
    sta VMAIN
    ldx #$0000
    stx VMADDL
    lda #$09            ; fixed source, write $2118/$2119 alternately
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    ldx #$0000
    stx DAS0L
    lda #$01
    sta MDMAEN
    ; CGRAM clear (512 bytes)
    stz CGADD
    lda #$08
    sta DMAP0
    lda #<CGDATA
    sta BBAD0
    ldx #512
    stx DAS0L
    lda #$01
    sta MDMAEN
    rts
zero_byte: .byte 0

nmi:
    jml nmi_fast
nmi_fast:
    AXY16
    pha
    phx
    phy
    phb
    phd
    A8
    lda #$80
    pha
    plb
    lda RDNMI           ; acknowledge
    lda nmi_ready
    beq @lag            ; main loop still busy: skip uploads (lag frame), still read pads
    jsl spr_nmi         ; OAM DMA + dynamic sprite CHR
    jsl eng_nmi         ; tilemap/CHR queues, scroll registers
    jsl bg_nmi          ; parallax scroll + HDMA tables
    stz nmi_ready
@lag:
    ; auto-joypad read finishes ~3 lines into vblank
@joy:
    lda HVBJOY
    and #$01
    bne @joy
    A16
    lda pad1
    sta pad1_old
    lda JOY1L
    sta pad1
    eor pad1_old
    and pad1
    sta pad1_new
    lda pad2
    sta pad2_old
    lda JOY2L
    sta pad2
    eor pad2_old
    and pad2
    sta pad2_new
    inc frame_count
    AXY16
    pld
    plb
    ply
    plx
    pla
    rti

irq:
    rti
cop_handler:
brk_handler:
    rti

.segment "HEADER"
    ;      123456789012345678901
    .byte "SUPER MARIO BROS 4   "   ; 21-byte title
    .byte $30                        ; LoROM + FastROM
    .byte $02                        ; ROM + RAM + battery (SRAM for saves)
    .byte $0B                        ; ROM size 2 MB
    .byte $03                        ; SRAM 8 KB
    .byte $01                        ; region: North America
    .byte $33                        ; developer id
    .byte $00                        ; version
    .word $FFFF, $0000               ; checksum complement / checksum (fixed by build script)

.segment "VECTORS"
    ; native: $FFE0
    .word 0, 0, .loword(cop_handler), .loword(brk_handler), 0, .loword(nmi), 0, .loword(irq)
    ; emulation: $FFF0
    .word 0, 0, .loword(cop_handler), 0, 0, .loword(nmi), .loword(reset), .loword(irq)
