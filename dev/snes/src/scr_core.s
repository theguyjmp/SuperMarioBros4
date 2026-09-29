; Screens core (screens agent): screen state machine + fades (C# GameMain.Go), scene loader, NMI upload queue,
; OBJ drawing for screens, menu input with key repeat (C# MenuNav), random, music helper.
; See snes/DESIGN.md (screens) and snes/KIT-NOTES.md "Screens lessons / reusable parts".
.p816
.smart
.include "scr.inc"

.export scr_init, scr_frame, scr_nmi
.import scr_scene_tab, scr_map_tab
.import txt_nmi, txt_init_scene
.import title_enter, title_tick, files_enter, files_tick, map_enter, map_tick, intro_enter, intro_tick
.import level_enter, level_tick, toad_enter, toad_tick, spade_enter, spade_tick, nspade_enter, nspade_tick
.import gover_enter, gover_tick, wclear_enter, wclear_tick, story_enter, story_tick, ending_enter, ending_tick
.import opt_enter, opt_tick, help_enter, help_tick, lsel_enter, lsel_tick
.import sv_boot
.importzp pad1, pad1_new, pad2, pad2_new

.segment "ZEROPAGE"
scr_zp0: .res 3
scr_zp1: .res 3
scr_zp2: .res 3
scr_meta: .res 3               ; far ptr: current scene's metasprite blob

.segment "BSS"
scr_screen:   .res 2           ; current screen (SC_*)
scr_pending:  .res 2
scr_fadest:   .res 2           ; 0 idle, 1 fading out, 2 fading in
scr_fadet:    .res 2
scr_fade:     .res 2           ; 0..4 like the C# (4 = black)
scr_t:        .res 2           ; ticks since the screen was entered
scr_frames:   .res 2
scr_held:     .res 2           ; pad1|pad2
scr_pressed:  .res 2
scr_prevheld: .res 2
scr_nav:      .res 8           ; heldV, dirV, heldH, dirH
scr_seed:     .res 2
scr_bg1x:     .res 2
scr_bg1y:     .res 2
scr_bg2x:     .res 2
scr_bg2y:     .res 2
scr_bg3y:     .res 2
scr_lattice:  .res 2           ; 1: BG2 lattice scrolls diagonally (Hud.Backdrop)
scr_bg2_off:  .res 2
scr_hdma:     .res 2           ; HDMAEN bits of the screen
scr_tm:       .res 2           ; main screen layers (written every NMI)
scr_crop:     .res 2
scr_music_cur: .res 2
scr_scene:    .res 2
scr_tmp0:     .res 2
scr_tmp1:     .res 2
scr_tmp2:     .res 2
scr_tmp3:     .res 2
scr_tmp4:     .res 2
scr_tmp5:     .res 2
scr_ptr:      .res 3
spr_x:        .res 2           ; scr_obj_put arguments
spr_y:        .res 2
spr_fl:       .res 2           ; bit0 hflip, bits 4-5 OBJ priority (use $30 for normal), bit 15 + bits 9-11 palette override
obj_next:     .res 2
obj_n:        .res 2
obj_fw:       .res 2
obj_sz:       .res 2
obj_px:       .res 2
obj_py:       .res 2
obj_buf:      .res 544
obj_hi:       .res 128
obj_ready:    .res 2
vq_n:         .res 2           ; VRAM jobs queued (8 bytes each: src24, pad, dst16, len16)
vq:           .res 8*32
cq_n:         .res 2           ; CGRAM jobs (src24, cg8, len16, pad)
cq:           .res 8*16
nmi_budget:   .res 2
obj_attr:     .res 2
op_t2:        .res 2
op_t3:        .res 2
op_t4:        .res 2
nv_t2:        .res 2
nv_t3:        .res 2
nv_t4:        .res 2
nmi_v1:       .res 2
nmi_v2:       .res 2
nmi_v3:       .res 2

.segment "CODE7"
.a16
.i16

; ================================================================== entry points (DESIGN: A8 XY16, DB=$80)
; scr_init: called once by the engine's game_init (forced blank) -> title screen.
scr_init:
    php
    rep #$30
    .a16
    .i16
    lda #GM_SCREEN
    sta g_mode
    lda #$1234
    sta scr_seed
    stz scr_music_cur
    jsl sv_boot
    lda #SC_TITLE
    jsl scr_go_now
    plp
    rtl

; scr_frame: one tick while no level runs (g_mode = GM_SCREEN).
scr_frame:
    php
    phb
    rep #$30
    .a16
    .i16
    sep #$20
    .a8
    lda #$80
    pha
    plb
    rep #$20
    .a16
    inc scr_frames
    ; buttons (both pads, C# `any`)
    lda pad1
    ora pad2
    sta scr_held
    eor scr_prevheld            ; pressed = held now, not held at the last game frame (lag-proof)
    and scr_held
    sta scr_pressed
    lda scr_held
    sta scr_prevheld
    ; fades (C# GameMain.Tick)
    lda scr_fadest
    cmp #1
    bne @notout
    inc scr_fadet
    lda scr_fadet
    cmp #3
    bcc @done
    stz scr_fadet
    inc scr_fade
    lda scr_fade
    cmp #4
    bcc @done
    lda scr_pending
    jsr switch_screen
    lda #2
    sta scr_fadest
    stz scr_fadet
    lda #4
    sta scr_fade
    bra @done
@notout:
    cmp #2
    bne @tick
    inc scr_fadet
    lda scr_fadet
    cmp #3
    bcc @tick
    stz scr_fadet
    dec scr_fade
    bne @tick
    stz scr_fadest
@tick:
    ; the level screen returns here when the engine hands control back (g_mode = GM_SCREEN)
    inc scr_t
    lda scr_screen
    asl a
    tax
    jsr (tick_tab,x)
@done:
    rep #$30
    jsl scr_obj_end
    plb
    plp
    rtl

tick_tab:  .addr nop_tick, t_title, t_files, t_map, t_intro, t_level, t_toad, t_spade, t_nspade, t_gover, t_wclear, t_story, t_ending, t_opt, t_help, t_lsel
enter_tab: .addr nop_tick, e_title, e_files, e_map, e_intro, e_level, e_toad, e_spade, e_nspade, e_gover, e_wclear, e_story, e_ending, e_opt, e_help, e_lsel
nop_tick: rts
t_title: jsl title_tick
    rts
t_files: jsl files_tick
    rts
t_map: jsl map_tick
    rts
t_intro: jsl intro_tick
    rts
t_level: jsl level_tick
    rts
t_toad: jsl toad_tick
    rts
t_spade: jsl spade_tick
    rts
t_nspade: jsl nspade_tick
    rts
t_gover: jsl gover_tick
    rts
t_wclear: jsl wclear_tick
    rts
t_story: jsl story_tick
    rts
t_ending: jsl ending_tick
    rts
t_opt: jsl opt_tick
    rts
t_help: jsl help_tick
    rts
t_lsel: jsl lsel_tick
    rts
e_title: jsl title_enter
    rts
e_files: jsl files_enter
    rts
e_map: jsl map_enter
    rts
e_intro: jsl intro_enter
    rts
e_level: jsl level_enter
    rts
e_toad: jsl toad_enter
    rts
e_spade: jsl spade_enter
    rts
e_nspade: jsl nspade_enter
    rts
e_gover: jsl gover_enter
    rts
e_wclear: jsl wclear_enter
    rts
e_story: jsl story_enter
    rts
e_ending: jsl ending_enter
    rts
e_opt: jsl opt_enter
    rts
e_help: jsl help_enter
    rts
e_lsel: jsl lsel_enter
    rts

; A = screen: forced blank, reset per-screen state, call its enter routine (which loads its scene).
switch_screen:
    .a16
    sta scr_screen
    jsr blank
    stz scr_t
    stz vq_n
    stz cq_n
    stz scr_lattice
    stz scr_bg1x
    stz scr_bg1y
    stz scr_bg2x
    stz scr_bg2y
    stz scr_bg3y
    stz scr_bg2_off
    stz scr_nav
    stz scr_nav+2
    stz scr_nav+4
    stz scr_nav+6
    jsl scr_obj_begin
    lda scr_screen
    asl a
    tax
    jsr (enter_tab,x)
    rep #$30
    rts

blank:
    sep #$20
    .a8
    lda #$80
    sta INIDISP
    stz HDMAEN
    rep #$20
    .a16
    rts

; scr_go: A = screen; fade out (12 ticks), switch, fade in (C# G.Go(s)).
scr_go:
    php
    rep #$30
    sta scr_pending
    lda #1
    sta scr_fadest
    stz scr_fadet
    plp
    rtl

; scr_go_now: A = screen; switch at once (C# G.Go(s, false)): full brightness next frame.
scr_go_now:
    php
    rep #$30
    jsr switch_screen
    stz scr_fade
    stz scr_fadest
    plp
    rtl

; ================================================================== NMI (called from eng_nmi while g_mode = GM_SCREEN; A8 XY16)
.a8
scr_nmi:
    php
    phb
    sep #$20
    rep #$10
    lda #$80
    pha
    plb
    ; the eng_dbg_level test hook starts a level on the next game frame: give it the level PPU setup now
    lda eng_dbg_level
    ora eng_dbg_level+1
    beq @nodbg
    jsl scr_level_ppu
    plb
    plp
    rtl
@nodbg:
    ; OAM
    lda obj_ready
    beq @nooam
    stz OAMADDL
    stz OAMADDL+1
    stz DMAP0
    lda #<OAMDATA
    sta BBAD0
    ldx #.loword(obj_buf)
    stx A1T0L
    lda #^obj_buf
    sta A1B0
    ldx #544
    stx DAS0L
    lda #1
    sta MDMAEN
@nooam:
    ; palettes
    rep #$20
    .a16
    ldx #0
@cg: cpx cq_n
    bcs @cgd
    sep #$20
    .a8
    lda cq+3,x
    sta CGADD
    lda #$02                    ; A->B, 1 register write twice... (mode 2: p, p)
    stz DMAP0
    lda #<CGDATA
    sta BBAD0
    rep #$20
    .a16
    lda cq,x
    sta A1T0L
    sep #$20
    .a8
    lda cq+2,x
    sta A1B0
    rep #$20
    .a16
    lda cq+4,x
    sta DAS0L
    sep #$20
    .a8
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    txa
    clc
    adc #8
    tax
    bra @cg
@cgd:
    stz cq_n
    ; VRAM jobs (budget ~3.5 KB per NMI; the rest waits)
    lda #3072
    sta nmi_budget
    sep #$20
    .a8
    lda #$80
    sta VMAIN
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    .a16
    ldx #0
@vq: cpx vq_n
    bcs @vqd
    lda vq+6,x
    cmp nmi_budget
    bcs @vqd                    ; over budget: keep the rest for the next frame
    lda nmi_budget
    sec
    sbc vq+6,x
    sta nmi_budget
    lda vq+4,x
    sta VMADDL
    lda vq,x
    sta A1T0L
    lda vq+6,x
    sta DAS0L
    sep #$20
    .a8
    lda vq+2,x
    sta A1B0
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    txa
    clc
    adc #8
    tax
    bra @vq
@vqd:
    ; drop the jobs done (move the rest down)

    ldy #0
@mv: cpx vq_n
    bcs @mvd
    lda vq,x
    sta vq,y
    inx
    inx
    iny
    iny
    bra @mv
@mvd:
    sty vq_n
    ; text canvas rows with what is left of the budget
    lda nmi_budget
    jsl txt_nmi
    ; scroll (the PPU shows BG row VOFS+1 on line 0: write y - 1) + brightness + HDMA
    rep #$20
    .a16
    lda scr_bg1y
    dec a
    sta nmi_v1
    lda scr_bg2y
    dec a
    sta nmi_v2
    lda scr_bg3y
    dec a
    sta nmi_v3
    sep #$20
    .a8
    lda scr_bg1x
    sta BG1HOFS
    lda scr_bg1x+1
    sta BG1HOFS
    lda nmi_v1
    sta BG1VOFS
    lda nmi_v1+1
    sta BG1VOFS
    lda scr_bg2x
    sta BG2HOFS
    lda scr_bg2x+1
    sta BG2HOFS
    lda nmi_v2
    sta BG2VOFS
    lda nmi_v2+1
    sta BG2VOFS
    stz BG3HOFS
    stz BG3HOFS
    lda nmi_v3
    sta BG3VOFS
    lda nmi_v3+1
    sta BG3VOFS
    lda scr_tm
    sta TM
    lda scr_hdma
    sta HDMAEN
    ldx scr_fade
    lda f:bright_tab,x
    sta INIDISP
    plb
    plp
    rtl
bright_tab: .byte $0F, $0B, $07, $03, $00
.a16

; ================================================================== upload queues (usable any time)
; scr_vq_add: scr_zp0 = far source, X = VRAM word address, Y = byte length. Big jobs are split in 2 KB parts.
scr_vq_add:
    php
    rep #$30
    stx vq_dst
    sty vq_left
@again:
    lda vq_left
    beq @out
    cmp #2049
    bcc :+
    lda #2048
:   sta vq_len
    ldx vq_n
    cpx #8*32
    bcs @out
    lda scr_zp0
    sta vq,x
    lda scr_zp0+2
    and #$00FF
    sta vq+2,x
    lda vq_dst
    sta vq+4,x
    lda vq_len
    sta vq+6,x
    txa
    clc
    adc #8
    sta vq_n
    lda scr_zp0
    clc
    adc vq_len
    sta scr_zp0
    lda vq_len
    lsr a
    clc
    adc vq_dst
    sta vq_dst
    lda vq_left
    sec
    sbc vq_len
    sta vq_left
    bra @again
@out:
    plp
    rtl
.segment "BSS"
vq_dst: .res 2
vq_left: .res 2
vq_len: .res 2
.segment "CODE7"
.a16
.i16

; scr_cg_set: scr_zp0 = far source, X = CGRAM color index, Y = byte length (queued for the NMI).
scr_cg_set:
    php
    rep #$30
    phx
    ldx cq_n
    cpx #8*16
    bcs @full
    lda scr_zp0
    sta cq,x
    sep #$20
    .a8
    lda scr_zp0+2
    sta cq+2,x
    pla
    sta cq+3,x
    pla
    rep #$20
    .a16
    tya
    sta cq+4,x
    txa
    clc
    adc #8
    sta cq_n
    plp
    rtl
@full:
    plx
    plp
    rtl

; ================================================================== scene loader (forced blank)
; scr_scene_load: A = SCN_* . Sets mode-1 PPU registers for screens, uploads BG CHR/palettes, OBJ set, text
; palettes, clears the maps and the text canvas, puts the scene's initial BG1/BG2 maps, sets the HDMA gradients.
scr_scene_load:
    php
    rep #$30
    sta scr_scene
    sep #$20
    .a8
    jsl bg_hdma_off
    sep #$20
    lda #$80
    sta INIDISP
    stz HDMAEN
    stz scr_hdma
    lda #$09
    sta BGMODE
    lda #$34                    ; BG1 map $3400 32x32
    sta BG1SC
    lda #$38                    ; BG2 map $3800 (32x64 if the scene says so)
    sta BG2SC
    lda #$5C                    ; BG3 map $5C00
    sta BG3SC
    stz BG12NBA                 ; BG1/BG2 CHR $0000
    lda #$04                    ; BG3 CHR $4000
    sta BG34NBA
    lda #$03
    sta OBSEL
    lda #$17
    sta TM
    sta scr_tm
    stz scr_tm+1
    stz TS
    stz TMW
    stz TSW
    stz W12SEL
    stz W34SEL
    stz WOBJSEL
    stz CGWSEL
    stz CGADSUB
    stz MOSAIC
    rep #$20
    .a16
    ; record pointer
    lda scr_scene
    asl a
    tax
    lda f:scr_scene_tab,x
    sta scr_zp1
    sep #$20
    .a8
    lda #^scr_scene_tab
    sta scr_zp1+2
    rep #$20
    .a16
    ; clear VRAM $3400-$5FFF (maps + text canvas) and OBJ is overwritten below
    ldx #$3400
    ldy #$2C00*2
    jsr vclear
    ; BG CHR
    ldy #0
    jsr far_at                  ; scr_zp0 = far ptr at [zp1]+0
    ldy #3
    lda [scr_zp1],y
    tay
    ldx #SCR_VRAM_BGCHR
    jsr vdma
    ; BG palettes
    ldy #5
    jsr far_at
    ldy #8
    lda [scr_zp1],y
    sta scr_tmp0
    ldy #10
    lda [scr_zp1],y
    tax
    ldy scr_tmp0
    jsr cdma
    ; OBJ CHR + palettes
    ldy #12
    jsr far_at
    ldy #15
    lda [scr_zp1],y
    tay
    ldx #SCR_VRAM_OBJ
    jsr vdma
    ldy #42                     ; the 8x8 singles at the end of the OBJ tiles
    jsr far_at
    ldy #47
    lda [scr_zp1],y
    tax
    ldy #45
    lda [scr_zp1],y
    tay
    jsr vdma
    ldy #17
    jsr far_at
    ldx #128
    ldy #256
    jsr cdma
    ; metasprites
    ldy #20
    lda [scr_zp1],y
    sta scr_meta
    ldy #22
    sep #$20
    .a8
    lda [scr_zp1],y
    sta scr_meta+2
    rep #$20
    .a16
    ; text palettes -> CGRAM 0-15
    ldy #40
    lda [scr_zp1],y
    sta scr_tmp0
    ldy #29
    jsr far_at
    ldx #0
    ldy scr_tmp0
    jsr cdma
    ; HDMA gradients: ch2 = color 0, ch3 = lattice color (mode 3 -> $2121/$2122)
    ldy #23
    jsr far_at
    lda scr_zp0+2
    and #$00FF
    beq @nog0
    ldx #$20
    jsr hdma_setup
    lda scr_hdma
    ora #$04
    sta scr_hdma
@nog0:
    ldy #26
    jsr far_at
    lda scr_zp0+2
    and #$00FF
    beq @nog1
    ldx #$30
    jsr hdma_setup
    lda scr_hdma
    ora #$08
    sta scr_hdma
    lda #1
    sta scr_lattice
@nog1:
    ; BG2 size
    ldy #36
    lda [scr_zp1],y
    beq :+
    sep #$20
    lda #$3A
    sta BG2SC
    rep #$20
:   ; initial maps
    ldy #32
    lda [scr_zp1],y
    cmp #$FFFF
    beq :+
    ldx #SCR_VRAM_BG1MAP
    jsl scr_map_now
:   ldy #34
    lda [scr_zp1],y
    cmp #$FFFF
    beq :+
    ldx #SCR_VRAM_BG2MAP
    jsl scr_map_now
:   jsl txt_init_scene
    jsl scr_obj_begin
    plp
    rtl

; scr_zp0 = far pointer stored at [scr_zp1] + Y
far_at:
    lda [scr_zp1],y
    sta scr_zp0
    iny
    lda [scr_zp1],y
    sta scr_zp0+1
    rts

; X = channel offset ($20/$30): HDMA mode 3 to CGADD from table scr_zp0
hdma_setup:
    sep #$20
    .a8
    lda #$03
    sta DMAP0,x
    lda #<CGADD
    sta BBAD0,x
    lda scr_zp0+2
    sta A1B0,x
    rep #$20
    .a16
    lda scr_zp0
    sta A1T0L,x
    rts

; VRAM fill with 0: X = word address, Y = bytes
vclear:
    sep #$20
    .a8
    lda #$80
    sta VMAIN
    stx VMADDL
    lda #$09
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    rep #$20
    .a16
    lda #.loword(zero2)
    sta A1T0L
    sep #$20
    .a8
    lda #^zero2
    sta A1B0
    sty DAS0L
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    rts
zero2: .word 0

; immediate VRAM DMA: scr_zp0 src, X = VRAM word address, Y = bytes (0 = nothing)
vdma:
    cpy #0
    beq @r
    sep #$20
    .a8
    lda #$80
    sta VMAIN
    stx VMADDL
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    ldx scr_zp0
    stx A1T0L
    lda scr_zp0+2
    sta A1B0
    sty DAS0L
    lda #1
    sta MDMAEN
    rep #$20
    .a16
@r: rts

; immediate CGRAM DMA: scr_zp0 src, X = color index, Y = bytes
cdma:
    cpy #0
    beq @r
    sep #$20
    .a8
    txa
    sta CGADD
    stz DMAP0
    lda #<CGDATA
    sta BBAD0
    ldx scr_zp0
    stx A1T0L
    lda scr_zp0+2
    sta A1B0
    sty DAS0L
    lda #1
    sta MDMAEN
    rep #$20
    .a16
@r: rts

; ================================================================== tilemaps
; scr_map_now: A = MAP_* id, X = map base (SCR_VRAM_BG1MAP/BG2MAP) -> VRAM at the map's own tile position (forced blank)
; scr_map_put: same, queued for the NMI
; scr_map_put_at: A = id, X = base, scr_tmp0/scr_tmp1 = tile x/y (queued)
.export scr_map_now
scr_map_now:
    php
    rep #$30
    ldy #1
    bra map_common
scr_map_put:
    php
    rep #$30
    ldy #0
map_common:
    sty mp_now
    stx mp_base
    jsr map_rec
    ldy #3
    lda [scr_zp2],y             ; tw, th, tx, ty
    and #$00FF
    sta mp_tw
    iny
    lda [scr_zp2],y
    and #$00FF
    sta mp_th
    iny
    lda [scr_zp2],y
    and #$00FF
    sta scr_tmp0
    iny
    lda [scr_zp2],y
    and #$00FF
    sta scr_tmp1
    bra map_go
scr_map_put_at:
    php
    rep #$30
    stz mp_now
    stx mp_base
    jsr map_rec
    ldy #3
    lda [scr_zp2],y
    and #$00FF
    sta mp_tw
    iny
    lda [scr_zp2],y
    and #$00FF
    sta mp_th
map_go:
    ; source
    ldy #0
    lda [scr_zp2],y
    sta scr_zp0
    iny
    lda [scr_zp2],y
    sta scr_zp0+1
    ; dest = base + ty*32 + tx
    lda scr_tmp1
    asl a
    asl a
    asl a
    asl a
    asl a
    clc
    adc scr_tmp0
    adc mp_base
    sta mp_dst
    lda mp_tw
    cmp #32
    bne @rows
    ; full-width map: one job
    lda mp_th
    xba
    lsr a
    lsr a                       ; th * 64 bytes
    tay
    ldx mp_dst
    lda mp_now
    beq :+
    jsr vdma
    plp
    rtl
:   jsl scr_vq_add
    plp
    rtl
@rows:
    lda mp_tw
    asl a
    sta mp_rowb
@r: lda mp_th
    beq @d
    ldx mp_dst
    ldy mp_rowb
    lda mp_now
    beq :+
    jsr vdma
    bra :++
:   lda scr_zp0
    pha
    jsl scr_vq_add
    pla
    sta scr_zp0
:   lda scr_zp0
    clc
    adc mp_rowb
    sta scr_zp0
    lda mp_dst
    clc
    adc #32
    sta mp_dst
    dec mp_th
    bra @r
@d: plp
    rtl

map_rec:
    ; scr_zp2 = &scr_map_tab[A] (8 bytes each)
    asl a
    asl a
    asl a
    clc
    adc #.loword(scr_map_tab)
    sta scr_zp2
    sep #$20
    .a8
    lda #^scr_map_tab
    sta scr_zp2+2
    rep #$20
    .a16
    rts

; scr_map_clear2: clear the BG2 map (queued: 1792 bytes of zeros)
scr_map_clear2:
    php
    rep #$30
    lda #.loword(zero_map)
    sta scr_zp0
    sep #$20
    .a8
    lda #^zero_map
    sta scr_zp0+2
    rep #$20
    .a16
    ldx #SCR_VRAM_BG2MAP
    ldy #1792
    jsl scr_vq_add
    plp
    rtl

.segment "BSS"
mp_now: .res 2
mp_base: .res 2
mp_tw: .res 2
mp_th: .res 2
mp_dst: .res 2
mp_rowb: .res 2
.segment "CODE7"
.a16
.i16

; ================================================================== OBJ (screens' own OAM buffer; uploaded by scr_nmi)
scr_obj_begin:
    php
    rep #$30
    ldx #0
    lda #$E000                  ; y = 224 (hidden)
@h: sta obj_buf,x
    inx
    inx
    inx
    inx
    cpx #512
    bne @h
    ldx #0
@hi: stz obj_hi,x
    inx
    inx
    cpx #128
    bne @hi
    lda #127
    sta obj_next
    plp
    rtl

; scr_obj_put: A = SP_* id, spr_x/spr_y = anchor (16-bit signed), spr_fl (see BSS). Later calls are in front.
scr_obj_put:
    php
    rep #$30
    cmp [scr_meta]              ; id < count ?
    jcs @out
    asl a
    inc a
    inc a
    tay
    lda [scr_meta],y
    jeq @out
    tay                         ; Y = record offset within the blob
    lda [scr_meta],y
    and #$00FF
    jeq @out
    sta obj_n
    iny
    lda [scr_meta],y
    and #$00FF
    sta obj_fw
    iny
@piece:
    lda obj_next
    jmi @out
    ; size
    iny
    iny
    iny
    iny
    lda [scr_meta],y            ; big flag
    dey
    dey
    dey
    dey
    and #$00FF
    beq :+
    lda #16
    bra :++
:   lda #8
:   sta obj_sz
    ; x
    lda [scr_meta],y
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   sta obj_px
    lda spr_fl
    lsr a
    bcc :+
    lda obj_fw
    sec
    sbc obj_px
    sec
    sbc obj_sz
    sta obj_px
:   lda obj_px
    clc
    adc spr_x
    sta obj_px
    ; y
    iny
    lda [scr_meta],y
    and #$00FF
    cmp #$0080
    bcc :+
    ora #$FF00
:   clc
    adc spr_y
    sta obj_py
    iny
    ; cull
    lda obj_px
    clc
    adc obj_sz
    jmi @skip
    jeq @skip
    lda obj_px
    cmp #256
    jpl @skip
    lda obj_py
    clc
    adc obj_sz
    jmi @skip
    beq @skip
    lda obj_py
    cmp #224
    bpl @skip
    ; OAM entry
    lda obj_next
    asl a
    asl a
    tax
    sep #$20
    .a8
    lda obj_px
    sta obj_buf,x
    lda obj_py
    sta obj_buf+1,x
    lda [scr_meta],y            ; tile
    sta obj_buf+2,x
    iny
    lda [scr_meta],y            ; attr: palette << 1 | name bit
    and #$0F
    bit spr_fl+1
    bpl :+
    and #$01                    ; palette override (spr_fl bits 9-11)
    ora spr_fl+1
    and #$0F
:   sta obj_attr
    lda spr_fl
    and #$30                    ; priority
    ora obj_attr
    sta obj_attr
    lda spr_fl
    lsr a
    bcc :+
    lda obj_attr
    ora #$40
    sta obj_attr
:   lda obj_attr
    sta obj_buf+3,x
    iny
    ; high bits
    ldx obj_next
    lda obj_px+1
    and #$01
    sta obj_hi,x
    lda obj_sz
    cmp #16
    bne :+
    lda obj_hi,x
    ora #$02
    sta obj_hi,x
:   rep #$20
    .a16
    dec obj_next
    iny
    dec obj_n
    jne @piece
    bra @out
@skip:
    iny
    iny
    iny
    dec obj_n
    jne @piece
@out:
    plp
    rtl

; scr_obj_end: pack the high table; the NMI uploads the buffer.
scr_obj_end:
    php
    sep #$20
    .a8
    rep #$10
    ldx #0
    ldy #0
@p: lda obj_hi+3,x
    asl a
    asl a
    ora obj_hi+2,x
    asl a
    asl a
    ora obj_hi+1,x
    asl a
    asl a
    ora obj_hi,x
    sta obj_buf+512,y
    inx
    inx
    inx
    inx
    iny
    cpy #32
    bne @p
    lda #1
    sta obj_ready
    plp
    rtl
.a16

; scr_obj_pal: A = SP_* id with palette variants, X = variant -> queue that palette into the sprite's OBJ slot.
scr_obj_pal:
    php
    rep #$30
    stx op_t3
    cmp [scr_meta]
    bcs @out
    asl a
    inc a
    inc a
    tay
    lda [scr_meta],y
    beq @out
    tay
    lda [scr_meta],y
    and #$00FF
    sta op_t2                ; pieces
    ; slot from the first piece's attr (offset 2 + 3)
    iny
    iny
    iny
    iny
    iny
    lda [scr_meta],y
    and #$000E
    asl a
    asl a
    asl a
    ora #128
    sta op_t4                ; CGRAM index
    dey
    dey
    dey
    ; skip pieces: Y = rec+2 + n*5
    lda op_t2
    asl a
    asl a
    adc op_t2
    sta op_t2
    tya
    clc
    adc op_t2
    tay
    lda [scr_meta],y
    and #$00FF
    beq @out
    iny
    lda op_t3
    xba
    lsr a
    lsr a
    lsr a                       ; variant * 32
    sta op_t3
    tya
    clc
    adc op_t3
    clc
    adc scr_meta
    sta scr_zp0
    sep #$20
    .a8
    lda scr_meta+2
    sta scr_zp0+2
    rep #$20
    .a16
    ldx op_t4
    ldy #32
    jsl scr_cg_set
@out:
    plp
    rtl

; ================================================================== input
; scr_navv: -> A = -1/0/1 vertical with the C# MenuNav key repeat (18 ticks, then every 5); scr_navh: horizontal.
scr_navv:
    php
    rep #$30
    ldx #0
    lda #PAD_UP
    sta nv_t4
    lda #PAD_DOWN
    bra nav_common
scr_navh:
    php
    rep #$30
    ldx #4
    lda #PAD_LEFT
    sta nv_t4
    lda #PAD_RIGHT
nav_common:
    sta nv_t3
    ; d = held(neg) ? -1 : held(pos) ? 1 : 0
    lda scr_held
    and nv_t4
    beq :+
    lda #$FFFF
    bra @d
:   lda scr_held
    and nv_t3
    beq :+
    lda #1
    bra @d
:   lda #0
@d: sta nv_t2
    bne @nz
    stz scr_nav,x
    stz scr_nav+2,x
    lda #0
    plp
    cmp #0                      ; flags of the result (plp restored the caller's)
    rtl
@nz:
    lda nv_t3
    ora nv_t4
    and scr_pressed
    bne @fresh
    lda nv_t2
    cmp scr_nav+2,x
    bne @fresh
    inc scr_nav,x
    lda scr_nav,x
    cmp #19
    bcc @zero
    sec
    sbc #18
@mod: cmp #5
    bcc @m
    sbc #5
    bra @mod
@m: cmp #0
    bne @zero
    lda nv_t2
    plp
    cmp #0                      ; flags of the result (plp restored the caller's)
    rtl
@fresh:
    lda nv_t2
    sta scr_nav+2,x
    stz scr_nav,x
    plp
    cmp #0                      ; flags of the result (plp restored the caller's)
    rtl
@zero:
    lda #0
    plp
    cmp #0                      ; flags of the result (plp restored the caller's)
    rtl

; ================================================================== misc
; scr_rand: A = 16-bit pseudo random (xorshift)
scr_rand:
    php
    rep #$30
    lda scr_seed
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    eor scr_seed
    sta scr_seed
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    lsr a
    eor scr_seed
    sta scr_seed
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    asl a
    eor scr_seed
    sta scr_seed
    clc
    adc scr_frames
    plp
    rtl

; scr_sfx: A = SFX_*
scr_sfx:
    php
    sep #$20
    .a8
    jsl snd_sfx
    plp
    rtl
.a16

; scr_music: A = SONG_*; X = 1 restart even if it is already playing (C# Sound.Music(name, true))
scr_music:
    php
    rep #$30
    cpx #0
    bne @go
    cmp scr_music_cur
    beq @r
@go:
    sta scr_music_cur
    sep #$20
    .a8
    jsl snd_music
@r: plp
    rtl
.a16

.segment "HIBSS"
zero_map: .res 1792

.segment "CODE7"
.a16
.i16
; scr_div: A = A / X (X = 1..255, unsigned), X = remainder (hardware divider)
scr_div:
    php
    rep #$30
    sta WRDIVL
    sep #$20
    .a8
    txa
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
    ldx RDMPYL
    lda RDDIVL
    plp
    rtl
; scr_abs: A = |A| (signed 16-bit)
scr_abs:
    php
    rep #$30
    cmp #$8000
    bcc :+
    eor #$FFFF
    inc a
:   plp
    rtl

; scr_go_in: A = screen; switch at once (the display is already blank, e.g. after a level) and fade in.
.export scr_go_in
scr_go_in:
    php
    rep #$30
    jsr switch_screen
    lda #4
    sta scr_fade
    lda #2
    sta scr_fadest
    stz scr_fadet
    plp
    rtl

; ================================================================== back to the engine's level PPU setup
; scr_level_ppu: undo everything screens changed (game_init's level setup: BG modes/maps/CHR bases, BG3 font, HUD
; palettes CGRAM 0-31, color math/window/HDMA off) so a level can start. Forced blank / vblank only.
; (Asked of the engine as a `ppu_setup` in start_level; until then screens do it before eng_level_start and, for the
; eng_dbg_level test hook, from scr_nmi.)
.import eng_font
.global cur_ts, spr_loaded, eng_dbg_level
.export scr_level_ppu
scr_level_ppu:
    php
    sep #$20
    .a8
    rep #$10
    stz HDMAEN
    stz scr_hdma
    lda #$09
    sta BGMODE
    lda #$41
    sta BG1SC
    lda #$49
    sta BG2SC
    lda #$54
    sta BG3SC
    lda #$30
    sta BG12NBA
    lda #$05
    sta BG34NBA
    lda #$03
    sta OBSEL
    stz TS
    stz TMW
    stz TSW
    stz W12SEL
    stz W34SEL
    stz WOBJSEL
    lda #$30
    sta CGWSEL
    stz CGADSUB
    ; BG3 font -> VRAM $5000 (64 tiles 2bpp)
    lda #$80
    sta VMAIN
    ldx #$5000
    stx VMADDL
    lda #$01
    sta DMAP0
    lda #<VMDATAL
    sta BBAD0
    ldx #.loword(eng_font)
    stx A1T0L
    lda #^eng_font
    sta A1B0
    ldx #64*16
    stx DAS0L
    lda #1
    sta MDMAEN
    ; HUD palettes -> CGRAM 0-31
    stz CGADD
    stz DMAP0
    lda #<CGDATA
    sta BBAD0
    ldx #.loword(lvl_hud_pal)
    stx A1T0L
    lda #^lvl_hud_pal
    sta A1B0
    ldx #64
    stx DAS0L
    lda #1
    sta MDMAEN
    rep #$20
    .a16
    lda #$FFFF
    sta f:cur_ts
    sta f:spr_loaded
    plp
    rtl
; copy of the engine's BG3 HUD palettes (game.s hud_pal)
lvl_hud_pal:
    .word 0, $7FFF, $6F7B, $0842
    .word 0, $2BFF, $02FF, $0421
    .word 0, $6F7F, $109F, $0006
    .word 0, $3DEF, $2D6B, $0421
    .word 0, $6BFA, $03E0, $0100
    .word 0, $7FF0, $7EE0, $2100
    .word 0, $2F7F, $01DF, $0005
    .word 0, $4A52, $2108, $0000
