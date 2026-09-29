; Sound module (sound agent): uploads the SPC700 driver, songs and samples; sends music/SFX commands.
; Protocol + APU memory layout: snes/SOUND.md. Every wait has a timeout: if the APU does not answer, sound is disabled
; (snd_ok = 0) and all calls return immediately, so nothing here can hang the game.
.p816
.smart
.include "snes.inc"
.include "music.inc"
.export snd_init, snd_music, snd_sfx, snd_tempo, snd_fade, snd_pause, snd_status
.export snd_ok, snd_cur
.import snd_res: far, snd_song_tab: far, snd_smp_tab: far, snd_song_smp: far
.import SND_RES_ORG, SND_RES_LEN, SND_ENTRY, SND_NDYN

.segment "ZEROPAGE"
snd_src:  .res 3        ; long pointer for uploads / table walks
snd_lst:  .res 3        ; long pointer: sample list of the song being loaded

.segment "BSS"
snd_ok:   .res 1        ; 1 once the driver runs
snd_cur:  .res 1        ; song id last started (0 none)
snd_last0: .res 1       ; last value written to APUIO0 (commands toggle bit 7)
snd_hk:   .res 1        ; receive-mode header handshake ($41/$43)
snd_sl2:  .res 1        ; last values written to the SFX ports
snd_sl3:  .res 1
snd_len:  .res 2
snd_dst:  .res 2
snd_sdst: .res 2         ; APU address of the song being loaded
snd_end:  .res 2
snd_cnt:  .res 1
snd_i:    .res 2
snd_tmo:  .res 2
snd_res_flags: .res 256 ; 1 = dynamic sample currently in APU RAM

CMD_STOP  = 2
CMD_TEMPO = 3
CMD_FADE  = 4
CMD_PAUSE = 5
CMD_LOAD  = 6
CMD_STOPSFX = 7

.segment "CODE6"

; ---------------------------------------------------------------------------------------------------------------
; snd_init: IPL-upload the resident image (driver + tables + SFX + resident samples) and start it.
snd_init:
    php
    phb
    A8
    XY16
    lda #$80
    pha
    plb
    stz snd_ok
    stz snd_cur
    ; wait for the IPL ready signature $BBAA
    ldy #4                  ; ~1 s worst case
@w0:
    ldx #$FFFF
@w1:
    lda APUIO0
    cmp #$AA
    bne @w2
    lda APUIO1
    cmp #$BB
    beq @ready
@w2:
    dex
    bne @w1
    dey
    bne @w0
    jmp @fail
@ready:
    lda #^snd_res
    sta snd_src+2
    ldx #.loword(snd_res)
    stx snd_src
    ldx #SND_RES_ORG
    stx APUIO2
    lda #1
    sta APUIO1
    lda #$CC
    sta APUIO0
    jsr @waitcc
    bcs @fail
    ldy #0
@byte:
    lda [snd_src],y
    sta APUIO1
    tya
    sta APUIO0
    ldx #$4000
@wb:
    cmp APUIO0
    beq @ok
    dex
    bne @wb
    jmp @fail
@ok:
    iny
    cpy #SND_RES_LEN
    bne @byte
    ; jump to the driver: port1 = 0, port0 = counter + 2 (never 0)
    ldx #SND_ENTRY
    stx APUIO2
    stz APUIO1
    tya
    clc
    adc #2
    bne @k
    inc a
@k:
    sta APUIO0
    ldx #$FFFF
@wk:
    cmp APUIO0
    beq @kicked
    dex
    bne @wk
    jmp @fail
@kicked:
    ; driver clears the ports and reports status $80 on port 1
    ldx #0
    stx APUIO0              ; ports 0/1 = 0
    stx APUIO2              ; ports 2/3 = 0
    ldy #3
@wd0:
    ldx #$FFFF
@wd1:
    lda APUIO1
    cmp #$80
    bne @wd2
    lda APUIO0
    beq @alive
@wd2:
    dex
    bne @wd1
    dey
    bne @wd0
    jmp @fail
@alive:
    stz snd_last0
    stz snd_sl2
    stz snd_sl3
    ldx #0
@clr:
    stz snd_res_flags,x
    inx
    cpx #256
    bne @clr
    lda #1
    sta snd_ok
@fail:
    plb
    plp
    rtl
@waitcc:
    ldx #$FFFF
@wc:
    lda APUIO0
    cmp #$CC
    beq @wcok
    dex
    bne @wc
    sec
    rts
@wcok:
    clc
    rts

; ---------------------------------------------------------------------------------------------------------------
; snd_cmd: A = command, X = argument (low byte). Waits (bounded) for the driver's ack.
snd_cmd:
    pha
    txa                     ; low byte of X
    sta APUIO1
    lda snd_last0
    and #$80
    eor #$80
    sta snd_i
    pla
    ora snd_i
    sta APUIO0
    sta snd_last0
    ldx #$8000
@w:
    cmp APUIO0
    beq @ok
    dex
    bne @w
    stz snd_ok              ; the driver stopped answering: disable sound
@ok:
    rts

; snd_music: A = song id (gen/music.inc SONG_*), 0 = stop. Uploads the song (+ any samples not already resident).
snd_music:
    php
    phb
    A8
    XY16
    pha
    lda #$80
    pha
    plb
    pla
    sta snd_tmo
    lda snd_ok
    bne @go
    jmp @done
@go:
    lda snd_tmo
    bne @play
    stz snd_cur
    lda #CMD_STOP
    ldx #0
    jsr snd_cmd
    jmp @done
@play:
    cmp #SND_NSONGS+1
    bcc @inrange
    jmp @done
@inrange:
    sta snd_cur
    ; X = id * 10 (song table record)
    rep #$20
    .a16
    and #$00FF
    sta snd_i
    asl a
    asl a
    clc
    adc snd_i
    asl a
    tax
    sep #$20
    .a8
    phx
    lda #CMD_LOAD
    ldx #0
    jsr snd_cmd
    plx
    lda snd_ok
    bne @loadok
    jmp @done
@loadok:
    lda #$41
    sta snd_hk
    ; song record: blob(3) len(2) dest(2) list(2) count(1)
    rep #$20
    .a16
    lda f:snd_song_tab+5,x
    sta snd_dst
    sta snd_sdst
    lda f:snd_song_tab+7,x
    sta snd_lst
    sep #$20
    .a8
    lda #^snd_song_tab
    sta snd_lst+2
    lda f:snd_song_tab+9,x
    sta snd_cnt
    phx
    ; evict samples overlapping the song blob + echo buffer [dest, $FFFF]
    ldx #0
    ldy #0
@ev:
    cpy #SND_NDYN
    bcs @evd
    lda snd_res_flags,y
    beq @evn
    rep #$20
    .a16
    lda f:snd_smp_tab+7,x   ; last byte address
    cmp snd_dst
    sep #$20
    .a8
    bcc @evn
    lda #0
    sta snd_res_flags,y
@evn:
    rep #$20
    .a16
    txa
    clc
    adc #10
    tax
    sep #$20
    .a8
    iny
    bra @ev
@evd:
    ; upload the samples this song needs that are not resident
    ldy #0
@sl:
    tya
    cmp snd_cnt
    bcs @sld
    phy
    lda [snd_lst],y
    jsr snd_ensure_sample
    ply
    iny
    bra @sl
@sld:
    plx
    ; song blob
    lda f:snd_song_tab+2,x
    sta snd_src+2
    rep #$20
    .a16
    lda f:snd_song_tab+0,x
    sta snd_src
    lda f:snd_song_tab+3,x
    sta snd_len
    sep #$20
    .a8
    ldx snd_sdst
    jsr snd_block
    ; end: type 0 with the song address -> the driver starts it
    lda #0
    ldx snd_sdst
    jsr snd_header
    ldx #0
    stx APUIO2
    stz snd_sl2
    stz snd_sl3
@done:
    plb
    plp
    rtl

; A = dynamic sample index: upload it unless resident; evict samples it overwrites
snd_ensure_sample:
    rep #$30
    .a16
    and #$00FF
    sta snd_i
    tay
    sep #$20
    .a8
    lda snd_res_flags,y
    beq @need
    rts
@need:
    rep #$20
    .a16
    lda snd_i
    asl a
    asl a
    clc
    adc snd_i
    asl a
    tax                     ; X = record (10 bytes)
    lda f:snd_smp_tab+3,x
    sta snd_len
    lda f:snd_smp_tab+7,x
    sta snd_end
    lda f:snd_smp_tab+0,x
    sta snd_src
    sep #$20
    .a8
    lda f:snd_smp_tab+2,x
    sta snd_src+2
    rep #$20
    .a16
    lda f:snd_smp_tab+5,x
    sta snd_dst
    sep #$20
    .a8
    ; evict every other resident sample overlapping [dst, end]
    ldx #0
    ldy #0
@ev:
    cpy #SND_NDYN
    bcs @evd
    lda snd_res_flags,y
    beq @evn
    rep #$20
    .a16
    lda f:snd_smp_tab+5,x   ; other.addr <= end ?
    cmp snd_end
    beq @c2
    bcs @evn16
@c2:
    lda f:snd_smp_tab+7,x   ; other.last >= dst ?
    cmp snd_dst
    bcc @evn16
    sep #$20
    .a8
    lda #0
    sta snd_res_flags,y
@evn16:
    sep #$20
    .a8
@evn:
    rep #$20
    .a16
    txa
    clc
    adc #10
    tax
    sep #$20
    .a8
    iny
    bra @ev
@evd:
    ldx snd_dst
    jsr snd_block
    ldy snd_i
    lda #1
    sta snd_res_flags,y
    rts

; A = type, X = address
snd_header:
    sta APUIO1
    stx APUIO2
    lda snd_hk
    sta APUIO0
    sta snd_last0
    ldx #$8000
@w:
    cmp APUIO0
    beq @ok
    dex
    bne @w
    stz snd_ok
@ok:
    eor #$02
    sta snd_hk
    rts

; upload snd_len bytes from [snd_src] to APU address X (2 bytes per handshake, port 0 = even index)
snd_block:
    lda #1
    jsr snd_header
    lda snd_ok
    beq @r
    ldy #0
@lp:
    rep #$20
    .a16
    lda [snd_src],y
    sta APUIO1              ; $2141 = even byte, $2142 = odd byte
    sep #$20
    .a8
    tya
    sta APUIO0
    ldx #$4000
@w:
    cmp APUIO0
    beq @ok
    dex
    bne @w
    stz snd_ok
    rts
@ok:
    iny
    iny
    cpy snd_len
    bcc @lp
    lda #$FF
    sta APUIO0
    ldx #$4000
@we:
    cmp APUIO0
    beq @r
    dex
    bne @we
    stz snd_ok
@r:
    rts

; ---------------------------------------------------------------------------------------------------------------
; snd_sfx: A = sfx id (SFX_*); A = SFX_STOP | id stops that effect (e.g. the looping P-meter tick).
snd_sfx:
    php
    phb
    A8
    XY16
    pha
    lda #$80
    pha
    plb
    pla
    xba
    lda snd_ok
    beq @done
    xba
    bit #$80
    beq @start
    and #$7F
    tax
    lda #CMD_STOPSFX
    jsr snd_cmd
    bra @done
@start:
    and #$7F
    beq @done
    sta snd_i
    ldx #200                ; ports busy (two effects this very moment): wait a little, then drop
@try:
    lda APUIO2
    cmp snd_sl2
    bne @p3
    lda snd_sl2
    and #$80
    eor #$80
    ora snd_i
    sta APUIO2
    sta snd_sl2
    bra @done
@p3:
    lda APUIO3
    cmp snd_sl3
    bne @busy
    lda snd_sl3
    and #$80
    eor #$80
    ora snd_i
    sta APUIO3
    sta snd_sl3
    bra @done
@busy:
    dex
    bne @try
@done:
    plb
    plp
    rtl

; snd_tempo: A = 0 normal, 1 hurry (x1.3)
snd_tempo:
    ldx #CMD_TEMPO
    bra snd_simple
; snd_fade: A = fade length in frames (0 = 60); the song stops at the end
snd_fade:
    ldx #CMD_FADE
    bra snd_simple
; snd_pause: A = 1 pause music (SFX keep playing), 0 resume
snd_pause:
    ldx #CMD_PAUSE
snd_simple:
    php
    phb
    A8
    XY16
    pha
    lda #$80
    pha
    plb
    pla
    xba
    lda snd_ok
    beq @done
    xba                     ; A = argument, X = command
    sta snd_tmo
    txa
    ldx snd_tmo             ; X low byte = argument, A = command
    jsr snd_cmd
@done:
    plb
    plp
    rtl

; snd_status: returns A = 1 while a song is playing (0 when stopped / a jingle has finished)
snd_status:
    php
    A8
    lda APUIO1
    and #$01
    plp
    rtl
