; Engine: the player (port of src/Game/Player/Player.cs + PlayerDraw.Animate) and the per-tick World logic that
; surrounds it (World.Tick order, camera, TouchTiles, blocks, pipes). Owner: engine agent.
; All routines: A16/XY16 in and out (engine convention), JSL/RTL.
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "eng.inc"
.include "spr_ids.inc"
.include "music.inc"



.global spr_player, spr_meta, spr_arg_x, spr_arg_y, spr_arg_flags, spr_arg_id

.segment "BSS"
; ---- C# Player fields (16-bit each)
p_x: .res 2
p_y: .res 2
p_xvel: .res 2
p_yvel: .res 2
p_inair: .res 2
p_power: .res 2
p_pmetercnt: .res 2
p_flytime: .res 2
p_wagcount: .res 2
p_allowairjump: .res 2
p_sliderate: .res 2
p_ducking: .res 2
p_sliding: .res 2
p_slide: .res 2
p_killtally: .res 2
p_facing: .res 2
p_form: .res 2
p_state: .res 2
p_hurtinv: .res 2
p_star: .res 2
p_tailattack: .res 2
p_statue: .res 2
p_throwpose: .res 2
p_kickpose: .res 2
p_jumpbuffer: .res 2
p_transform: .res 2
p_transformkind: .res 2
p_transfrom: .res 2
p_deathtimer: .res 2
p_pipetimer: .res 2
p_pipedir: .res 2
p_pipetarea: .res 2
p_pipetid: .res 2
p_pipeexiting: .res 2
p_swimming: .res 2
p_climbing: .res 2
p_swimanim: .res 2
p_climbanim: .res 2
p_carrying: .res 2          ; entity slot*2+2 (0 = none)
p_pwing: .res 2
p_noteride: .res 2
p_notesuper: .res 2
p_nolowgrav: .res 2
p_onslope: .res 2
p_conveyor: .res 2
p_pipeexitticks: .res 2
p_stun: .res 2
p_invisible: .res 2
p_animtick: .res 2
p_animframe: .res 2
p_skidding: .res 2
p_pjumppose: .res 2
p_somersault: .res 2
p_inwater: .res 2
p_swimfc: .res 2
p_leapt: .res 2
; ---- locals
c_apressed: .res 2
c_lowclear: .res 2
c_movedir: .res 2
c_dir: .res 2
c_row: .res 2               ; accel row pointer (index into phys_rows)
d_px: .res 2
d_py: .res 2
d_small: .res 2
d_right: .res 2
d_side: .res 2
d_f1: .res 2
d_f2: .res 2
d_surf: .res 2
d_ft: .res 2
d_cx: .res 2
d_fy: .res 2
d_k: .res 2
w_frame: .res 2
w_wiggly: .res 2
w_halt: .res 2
w_pswitch: .res 2
w_time: .res 2
w_timetick: .res 2
w_endtimer: .res 2
w_clearing: .res 2
w_result: .res 2            ; 0 none, 1 cleared, 2 died
w_autoscroll: .res 2
w_hurry: .res 2
w_cardgot: .res 2
w_shake: .res 2
w_bossarena: .res 2
w_psound: .res 2
w_hurrywait: .res 2
w_tallydone: .res 2
w_tallyend: .res 2

.segment "CODE2"
.a16
.i16

tile_props: .byte TILE_PROPS

; Phys rows: fricFrac, accelWhole, accelFrac, skidWhole, skidFrac, skidBWhole, skidBFrac (words)
ROW_SMALL = 0
ROW_BIG = 14
ROW_FROG = 28
ROW_ICE1 = 42
ROW_ICE2 = 56
ROW_WFLOOR = 70
ROW_SWIM = 84
phys_rows:
    .word $60, 0, $E0, 2, 0, 2, 0          ; Small / Tanooki / Hammer
    .word $20, 0, $E0, 2, 0, 2, 0          ; Big / Fire / Raccoon
    .word $00, 2, 0, 2, 0, 2, 0            ; Frog
    .word $A0, 0, $E0, 0, $C0, 1, $20      ; Ice1
    .word $D0, 0, $E0, 0, $60, 0, $C0      ; Ice2
    .word $30, 1, 0, 1, 0, 1, 0            ; WaterFloor
    .word $E0, 0, $30, 0, $80, 0, $80      ; Swim
jump_bonus: .word 0, 2, 4, 8, 0

; ------------------------------------------------------------------ helpers
; A = T -> A = props byte (flags set by the final lda/and)
.macro PROPS
    tax
    lda f:tile_props,x
    and #$00FF
.endmacro

; carry(frac, wiggly): A = frac -> A = 1 if frac + wiggly > 255 else 0
carry_frac:
    clc
    adc w_wiggly
    cmp #256
    bcs @one
    lda #0
    rts
@one: lda #1
    rts

; solid at pixel (A = px, Y = py) -> Z clear if solid (A = props & SOLID)
solid_px:
    jsl eng_tile_at_px
    PROPS
    and #TP_SOLID
    rts

; A = signed value -> A = sign (-1, 0, 1)
sign16:
    cmp #0
    beq @z
    bmi @m
    lda #1
    rts
@m: lda #$FFFF
@z: rts

; A = signed -> A = |A|
abs16:
    cmp #0
    bpl @p
    NEG16
@p: rts

; Px (unsigned X >> 4)
get_px:
    lda p_x
    lsr a
    lsr a
    lsr a
    lsr a
    rts
; Py (signed Y >> 4)
get_py:
    lda p_y
    ASR4
    rts

; SmallBox = !Big || Ducking || Sliding -> A = 1/0
small_box:
    lda p_form
    beq @one
    lda p_ducking
    ora p_sliding
    beq @z
@one: lda #1
@z: rts

; is tail suit -> Z clear if Raccoon/Tanooki
tail_suit:
    lda p_form
    cmp #PF_RACCOON
    beq @y
    cmp #PF_TANOOKI
    beq @y
    lda #0
    rts
@y: lda #1
    rts

; ================================================================== pl_init: fresh player for a level (World ctor)
; A = form
pl_init:
    pha
    ldx #0
    lda #0
@c: sta p_x,x
    inx
    inx
    cpx #(p_leapt - p_x + 2)
    bcc @c
    pla
    sta p_form
    lda #1
    sta p_inair
    sta p_facing
    lda #64
    sta p_pipeexitticks
    rtl

; ================================================================== Control (C# Player.Control), docs/01 §3 step 2
pl_control:
    lda p_stun
    beq @nostun
    dec p_stun
    stz pad_held
    stz pad_pressed
@nostun:
    lda p_state
    beq @go
    cmp #PS_VINE
    beq @go
    rtl
@go:
    ; ---- note block ride
    lda p_noteride
    beq @nonote
    lda pad_pressed
    and #BTN_A
    beq :+
    lda #1
    sta p_notesuper
:   dec p_noteride
    lda p_x
    clc
    adc p_xvel
    sta p_x
    stz p_yvel
    stz p_inair
    lda p_noteride
    bne :+
    lda #$10000-$38
    ldx p_notesuper
    beq @nl
    lda #$10000-$70
@nl: sta p_yvel
    lda #1
    sta p_inair
    sta p_nolowgrav
    stz p_notesuper
    SFX "SPRING"
:   rtl
@nonote:
    ; ---- Modern jump buffer
    lda pad_pressed
    and #BTN_A
    sta c_apressed
    ldx #0                      ; buffered
    lda c_apressed
    bne @nb
    lda p_jumpbuffer
    beq @nb
    lda p_inair
    bne @nb
    lda #BTN_A
    sta c_apressed
    ldx #1
@nb:
    lda p_jumpbuffer
    beq :+
    dec p_jumpbuffer
:   txa
    beq :+
    stz p_jumpbuffer
:
    lda p_state
    cmp #PS_VINE
    bne :+
    jsr climb_control
    rtl
:
    ; ---- 1. duck / low clearance / water / vine
    jsr update_water_state
    lda p_inair
    bne @noduck
    stz p_ducking
    lda p_form
    beq @noduck
    cmp #PF_FROG
    beq @noduck
    lda p_carrying
    ora p_sliding
    ora p_statue
    ora p_swimming
    bne @noduck
    lda pad_held
    and #BTN_DOWN
    beq @noduck
    lda #1
    sta p_ducking
@noduck:
    stz c_lowclear
    lda p_form
    beq @nolow
    lda p_ducking
    ora p_inair
    ora p_statue
    bne @nolow
    jsr get_px
    clc
    adc #8
    pha
    jsr get_py
    clc
    adc #10
    tay
    pla
    jsr solid_px
    beq @nolow
    lda #1
    sta c_lowclear
    stz p_xvel
    lda p_x
    clc
    adc #16
    sta p_x
@nolow:
    lda p_swimming
    bne :+
    jsr try_grab_vine
    bcc :+
    rtl
:
    ; ---- slope slide start
    lda p_inair
    bne @noslide
    lda p_onslope
    beq @noslide
    lda pad_held
    and #BTN_DOWN
    beq @noslide
    lda p_sliding
    bne @noslide
    lda p_form
    cmp #PF_FROG
    beq @noslide
    cmp #PF_HAMMER
    beq @noslide
    lda p_carrying
    ora p_statue
    ora p_swimming
    bne @noslide
    lda #1
    sta p_sliding
    stz p_slide
    stz p_ducking
@noslide:
    lda p_sliding
    beq @sl2
    lda p_inair
    ora p_swimming
    bne @endslide
    lda pad_held
    and #(BTN_LEFT|BTN_RIGHT|BTN_UP)
    beq @sl2
@endslide:
    stz p_sliding
@sl2:
    ; ---- 2. X move (SlideRate moves only the position)
    lda #0
    ldx p_inair
    bne @sr
    lda p_sliding
    bne :+
    lda p_onslope
    bra :++
:   lda #0
:   ldx w_pswitch
    bne @sr
    clc
    adc p_conveyor
@sr: sta p_sliderate
    clc
    adc p_xvel
    ; clamp +-$40
    bmi @neg
    cmp #$41
    bcc @cl
    lda #$40
    bra @cl
@neg: cmp #$10000-$40
    bcs @cl
    lda #$10000-$40
@cl: sta p_xvel
    clc
    adc p_x
    sta p_x
    lda p_xvel
    sec
    sbc p_sliderate
    sta p_xvel
    ; ---- 3. direction
    jsr sign16
    sta c_movedir
    ; ---- 4. Y move if in the air on the previous tick
    lda p_inair
    beq @noy
    lda p_yvel
    bmi @yadd
    cmp #$41
    bcc @yadd
    lda #$40
@yadd: clc
    adc p_y
    sta p_y
@noy:
    ; ---- 5. skid detection
    stz c_dir
    lda pad_held
    and #BTN_LEFT
    beq :+
    lda #$FFFF
    sta c_dir
    bra :++
:   lda pad_held
    and #BTN_RIGHT
    beq :+
    lda #1
    sta c_dir
:
    stz p_skidding
    lda p_inair
    ora p_swimming
    bne @noskid
    lda c_dir
    beq @noskid
    lda c_movedir
    beq @noskid
    cmp c_dir
    beq @noskid
    lda p_xvel
    jsr abs16
    cmp #2
    bcc @noskid
    lda #1
    sta p_skidding
    lda w_frame
    and #7
    bne @noskid
    SFX "SKID"
    jsl ent_add_fx_dust
@noskid:
    lda c_dir
    beq :+
    lda p_tailattack
    ora p_statue
    bne :+
    lda c_dir
    sta p_facing
:
    ; ---- 6. horizontal control
    lda p_statue
    beq :+
    stz p_xvel
    jmp @hdone
:   lda p_sliding
    beq @nosl
    lda p_slide
    clc
    adc p_onslope
    bmi @sn
    cmp #$41
    bcc @sc
    lda #$40
    bra @sc
@sn: cmp #$10000-$40
    bcs @sc
    lda #$10000-$40
@sc: sta p_slide
    jsr sign16
    eor #$FFFF
    inc a
    clc
    adc p_slide
    sta p_slide
    sta p_xvel
    beq :+
    jsr sign16
    sta p_facing
:   lda p_onslope
    ora p_slide
    bne :+
    stz p_sliding
:   jmp @hdone
@nosl:
    lda p_swimming
    beq :+
    jsr swim_horizontal
    jmp @hdone
:   lda p_form
    cmp #PF_FROG
    bne :+
    lda p_inair
    bne :+
    jsr frog_land
    bra @hdone
:   jsr ground_hcontrol
@hdone:
    ; ---- 7. jump / fly / flutter / gravity
    lda p_swimming
    beq :+
    jsr swim_vertical
    bra @vdone
:   lda c_lowclear
    beq :+
    stz c_apressed
:   jsr jump_fly_flutter
@vdone:
    ; ---- 8. tail wag air speed limit
    jsr tail_suit
    beq @notw
    lda p_inair
    beq @notw
    lda p_flytime
    ora p_wagcount
    beq @notw
    lda p_statue
    bne @notw
    lda p_xvel
    jsr abs16
    cmp #$18
    bcc @notw
    lda p_xvel
    jsr sign16
    eor #$FFFF
    inc a
    clc
    adc p_xvel
    sta p_xvel
@notw:
    lda p_inair
    beq :+
    lda p_wagcount
    beq :+
    dec p_wagcount
:
    jsr actions
    rtl

; ------------------------------------------------------------------ GroundHControl
ground_hcontrol:
    ; row
    lda p_form
    ldx #ROW_BIG
    cmp #PF_SMALL
    beq @sm
    cmp #PF_TANOOKI
    beq @sm
    cmp #PF_HAMMER
    beq @sm
    cmp #PF_FROG
    bne @rowok
    ldx #ROW_FROG
    bra @rowok
@sm: ldx #ROW_SMALL
@rowok:
    lda p_inair
    bne @noice
    phx
    jsr ice_under
    plx
    cmp #1
    bne :+
    ldx #ROW_ICE1
:   cmp #2
    bne @noice
    ldx #ROW_ICE2
@noice:
    stx c_row
    ; dir (ducking on the ground ignores L/R)
    lda c_dir
    sta e_t0
    lda p_ducking
    beq :+
    lda p_inair
    bne :+
    stz e_t0
:   lda e_t0
    bne @notzero
    ; ---- no L/R: ground friction
    lda p_inair
    bne @ret
    lda p_xvel
    beq @ret
    jsr fric_dec                ; A = d
    sta e_t1
    lda p_xvel
    bmi @fn
    sec
    sbc e_t1
    bpl :+
    lda #0
:   sta p_xvel
@ret: rts
@fn: clc
    adc e_t1
    bmi :+
    lda #0
:   sta p_xvel
    rts
@notzero:
    lda c_movedir
    beq @accel
    cmp e_t0
    beq @accel
    ; ---- skid: XVel += dir * Inc(runHeld ? SkidB : Skid)
    ldx c_row
    lda pad_held
    and #BTN_B
    beq :+
    inx
    inx
    inx
    inx
:   lda f:phys_rows+8,x         ; frac
    jsr carry_frac
    clc
    adc f:phys_rows+6,x         ; whole
    jsr apply_dir
    clc
    adc p_xvel
    sta p_xvel
    rts
@accel:
    ; cap
    lda pad_held
    and #BTN_B
    beq @walk
    lda #$28
    ldx p_power
    cpx #$7F
    bne :+
    lda #$38
:   bra @capok
@walk: lda #$18
@capok:
    sta e_t2
    lda p_carrying
    beq :+
    lda e_t2
    cmp #$29
    bcc :+
    lda #$28
    sta e_t2
:   ; uphill caps
    lda p_inair
    bne @nouphill
    lda p_onslope
    beq @nouphill
    jsr sign16
    NEG16
    cmp e_t0
    bne @nouphill
    lda #$0D
    ldx pad_held
    txa
    and #BTN_B
    beq :+
    lda #$16
    bra :++
:   lda #$0D
:   sta e_t2
@nouphill:
    lda p_xvel
    jsr abs16
    sta e_t3                    ; ax
    cmp e_t2
    bcs @atcap
    ldx c_row
    lda f:phys_rows+4,x
    jsr carry_frac
    clc
    adc f:phys_rows+2,x
    jsr apply_dir
    clc
    adc p_xvel
    sta p_xvel
    rts
@atcap:
    beq @r2
    lda p_inair
    bne @r2
    jsr fric_dec
    sta e_t1
    lda e_t3
    sec
    sbc e_t1
    cmp e_t2
    bcs :+
    lda e_t2
:   sta e_t1                    ; n
    lda p_xvel
    bpl :+
    lda e_t1
    NEG16
    sta p_xvel
    rts
:   lda e_t1
    sta p_xvel
@r2: rts

; A = value -> A = value * e_t0 (dir -1/+1)
apply_dir:
    ldy e_t0
    bpl :+
    NEG16
:   rts

; FricDec(row.FricFrac, cw) -> A = 1 - carry
fric_dec:
    ldx c_row
    lda f:phys_rows,x
    jsr carry_frac
    eor #1
    rts

; ------------------------------------------------------------------ IceUnder -> A = 0/1/2
ice_under:
    lda p_inair
    bne @no
    jsr get_py
    clc
    adc #32
    sta e_t4
    jsr get_px
    clc
    adc #4
    ldy e_t4
    jsl eng_tile_at_px
    sta e_t5
    jsr get_px
    clc
    adc #11
    ldy e_t4
    jsl eng_tile_at_px
    cmp #T_ICE
    beq @two
    ldx e_t5
    cpx #T_ICE
    beq @two
    ldx area_ice
    beq @no
    cmp #T_GROUND
    beq @one
    lda e_t5
    cmp #T_GROUND
    beq @one
@no: lda #0
    rts
@one: lda #1
    rts
@two: lda #2
    rts

; ------------------------------------------------------------------ JumpFlyFlutter
jump_fly_flutter:
    lda c_apressed
    jeq @nojump
    lda p_inair
    beq @cand
    lda p_allowairjump
    jeq @airpress
@cand:
    lda p_statue
    beq @jump
    lda p_inair
    jeq @nojump
    brl @airpress
@jump:
    lda p_xvel
    jsr abs16
    lsr a
    lsr a
    lsr a
    lsr a
    cmp #4
    bcc :+
    lda #4
:   asl a
    tax
    lda #$10000-$38
    sec
    sbc f:jump_bonus,x
    sta p_yvel
    lda #1
    sta p_inair
    stz p_allowairjump
    stz p_jumpbuffer
    stz p_pjumppose
    lda p_power
    cmp #$7F
    bne :+
    lda #1
    sta p_pjumppose
    lda p_flytime
    bne :+
    lda #$80
    sta p_flytime
:   lda p_pwing
    beq :+
    lda #$FF
    sta p_flytime
:   SFX "JUMP"
    lda p_star
    beq :+
    lda p_form
    beq :+
    cmp #PF_FROG
    beq :+
    lda p_carrying
    bne :+
    lda p_power
    cmp #$7F
    beq :+
    lda #1
    sta p_somersault
:   stz c_apressed
    bra @nojump
@airpress:
    ; a new A press in the air: tail wag (flutter) or Modern jump buffer
    jsr tail_suit
    beq @buf
    lda p_statue
    bne @nojump
    lda #$10
    sta p_wagcount
    SFX "FLUTTER"
    bra @nojump
@buf:
    lda #4
    sta p_jumpbuffer
@nojump:
    lda p_inair
    bne :+
    rts
:   ; ---- gravity
    lda p_statue
    beq :+
    lda p_yvel
    clc
    adc #7
    sta p_yvel
    rts
:   lda p_yvel
    bpl @high
    cmp #$10000-$20
    bcs @high
    lda pad_held
    and #BTN_A
    beq @high
    lda p_nolowgrav
    bne @high
    inc p_yvel
    bra @grav
@high:
    lda p_yvel
    clc
    adc #5
    sta p_yvel
@grav:
    ; ---- flight / flutter
    jsr tail_suit
    beq @ret
    lda p_wagcount
    beq @ret
    lda p_flytime
    beq @flutter
    lda p_yvel
    bpl @lift
    cmp #$10000-$18
    bcc @ret                    ; YVel < FlyLift
@lift:
    lda p_flytime
    cmp #$0F
    bcc :+
    lda #$10000-$18
    sta p_yvel
    rts
:   and #8
    beq :+
    lda #$10000-$10
:   sta p_yvel
    rts
@flutter:
    lda p_yvel
    bmi @ret
    cmp #$10
    bcc @ret
    lda #$10
    sta p_yvel
@ret: rts

; ------------------------------------------------------------------ Actions (fire / tail / kick)
actions:
    lda pad_pressed
    and #BTN_B
    jeq @nob
    jsr tail_suit
    beq @notail
    lda pad_held
    and #BTN_DOWN
    beq @spin
    ; Tanooki statue (Down + B)
    lda p_form
    cmp #PF_TANOOKI
    bne @nob
    lda p_statue
    ora p_carrying
    bne @nob
    lda #$C0
    sta p_statue
    stz p_xvel
    jsr puff16
    SFX "STATUE"
    bra @nob
@spin:
    lda p_tailattack
    ora p_carrying
    ora p_statue
    bne @nob
    lda #$12
    sta p_tailattack
    SFX "TAIL"
    bra @nob
@notail:
    lda p_form
    cmp #PF_FIRE
    beq :+
    cmp #PF_HAMMER
    bne @nob
:   lda p_carrying
    ora p_ducking
    bne @nob
    jsl ent_throw_fireball      ; fireball or hammer (max 2 player projectiles)
@nob:
    ; the statue ends when Down is released
    lda p_statue
    beq :+
    lda pad_held
    and #BTN_DOWN
    bne :+
    jsr end_statue
:   ; releasing B kicks whatever we carry
    lda p_carrying
    beq :+
    lda pad_held
    and #BTN_B
    bne :+
    jsl ent_kick_carried
:   rts

; EndStatue: statue off, puff, poof sound
end_statue:
    stz p_statue
    jsr puff16
    SFX "POOF"
    rts

; W.Puff(Px, Py + 16)
puff16:
    jsr get_px
    sta ent_new_x
    jsr get_py
    clc
    adc #16
    sta ent_new_y
    jsl ent_puff_at
    rts
.global ent_new_x, ent_new_y, ent_puff_at

; ------------------------------------------------------------------ swimming (C# UpdateWaterState, Swim*)
update_water_state:
    lda p_swimming
    sta e_t6                    ; was
    stz p_inwater
    stz p_swimming
    lda area_water
    bmi @dry
    jsr small_box
    tax
    lda #20
    cpx #0
    beq :+
    lda #24
:   sta e_t0
    jsr get_py
    clc
    adc e_t0
    ASR4
    cmp area_water
    bmi @dry
    lda #1
    sta p_inwater
    lda p_statue
    bne @dry
    lda #1
    sta p_swimming
    lda e_t6
    bne @ret
    ; entered the water
    stz p_flytime
    stz p_wagcount
    stz p_ducking
    lda p_yvel
    bmi :+
    cmp #$11
    bcc :+
    lda #$10
    sta p_yvel
:   lda area_water
    beq @ret
    SFX "SPLASH"
    jsl ent_add_fx_splash
@ret: rts
@dry:
    stz p_leapt
    rts

swim_horizontal:
    ldx #ROW_WFLOOR
    lda #8
    ldy p_inair
    beq :+
    ldx #ROW_SWIM
    lda #$18
:   stx c_row
    sta e_t2                    ; cap
    lda p_xvel
    jsr sign16
    sta c_movedir
    lda c_dir
    sta e_t0
    bne @dir
    jsr fric_dec
    sta e_t1
    lda p_xvel
    beq @r
    bmi @fn
    sec
    sbc e_t1
    bpl :+
    lda #0
:   sta p_xvel
@r: rts
@fn: clc
    adc e_t1
    bmi :+
    lda #0
:   sta p_xvel
    rts
@dir:
    lda c_movedir
    beq @acc
    cmp e_t0
    beq @acc
    ldx c_row
    lda f:phys_rows+8,x
    jsr carry_frac
    clc
    adc f:phys_rows+6,x
    jsr apply_dir
    clc
    adc p_xvel
    sta p_xvel
    rts
@acc:
    lda p_xvel
    jsr abs16
    sta e_t3
    cmp e_t2
    bcs @over
    ldx c_row
    lda f:phys_rows+4,x
    jsr carry_frac
    clc
    adc f:phys_rows+2,x
    jsr apply_dir
    clc
    adc p_xvel
    sta p_xvel
    rts
@over:
    beq @r2
    jsr fric_dec
    sta e_t1
    lda e_t3
    sec
    sbc e_t1
    cmp e_t2
    bcs :+
    lda e_t2
:   ldx p_xvel
    bpl :+
    NEG16
:   sta p_xvel
@r2: rts

swim_vertical:
    ; headOut = wr > 0 && ((Py + (small ? 18 : 6)) >> 4) < wr
    stz e_t5
    lda area_water
    beq @nh
    jsr small_box
    tax
    lda #6
    cpx #0
    beq :+
    lda #18
:   sta e_t0
    jsr get_py
    clc
    adc e_t0
    ASR4
    cmp area_water
    bpl @nh
    lda #1
    sta e_t5
@nh:
    lda p_form
    cmp #PF_FROG
    bne @nofrog
    jmp frog_swim
@nofrog:
    lda p_leapt
    beq @noleap
    dec p_leapt
    lda p_yvel
    bpl :+
    inc p_yvel
    rts
:   stz p_leapt
@noleap:
    lda e_t5
    beq @under
    lda p_yvel
    bpl @under
    ; surface bobbing
    lda pad_held
    and #BTN_UP
    beq @bob
    lda c_apressed
    beq @bob
    lda #$10000-$34
    sta p_yvel
    lda #1
    sta p_inair
    lda #16
    sta p_leapt
    SFX "JUMP"
    rts
@bob:
    lda p_yvel
    cmp #$10000-$0C
    bcs :+
    lda #$10000-$0C
    sta p_yvel
:   lda w_frame
    and #7
    bne :+
    inc p_yvel
:   rts
@under:
    lda c_apressed
    beq @noswim
    lda p_inair
    bne :+
    lda #$10000-$20
    bra :++
:   lda p_yvel
    sec
    sbc #$20
:   bpl :+
    cmp #$10000-$20
    bcs :+
    lda #$10000-$20
:   sta p_yvel
    lda #1
    sta p_inair
    lda #12
    sta p_swimanim
    SFX "SWIM"
@noswim:
    lda p_inair
    beq @anim
    lda p_yvel
    bpl @down
    inc p_yvel
    bra @cap
@down:
    lda p_swimfc
    inc p_swimfc
    and #3
    cmp #2
    bcs @cap
    inc p_yvel
@cap:
    lda p_yvel
    bmi :+
    cmp #$21
    bcc :+
    lda #$20
    sta p_yvel
:   ; if (Py + 8 < CamY - 8) YVel += 0x10
    jsr get_py
    clc
    adc #16
    sec
    sbc cam_y
    bpl @anim
    lda p_yvel
    clc
    adc #$10
    sta p_yvel
@anim:
    lda p_swimanim
    beq :+
    dec p_swimanim
:   rts

; Frog swimming (C# SwimVertical, Frog branch): the d-pad sets the velocity directly, no sinking. e_t5 = headOut
frog_swim:
    ldx #$10
    lda pad_held
    and #BTN_A
    beq :+
    ldx #$20
:   stx e_t0                    ; sp
    stz e_t1                    ; tx
    lda pad_held
    and #BTN_LEFT
    beq :+
    lda e_t0
    NEG16
    sta e_t1
    bra :++
:   lda pad_held
    and #BTN_RIGHT
    beq :+
    lda e_t0
    sta e_t1
:   stz e_t2                    ; ty
    lda pad_held
    and #BTN_UP
    beq :+
    lda e_t0
    NEG16
    sta e_t2
    bra :++
:   lda pad_held
    and #BTN_DOWN
    beq :+
    lda e_t0
    sta e_t2
:   ; XVel = tx != 0 ? tx : XVel - Sign(XVel) ; same for Y
    lda e_t1
    bne :+
    lda p_xvel
    jsr sign16
    NEG16
    clc
    adc p_xvel
:   sta p_xvel
    lda e_t2
    bne :+
    lda p_yvel
    jsr sign16
    NEG16
    clc
    adc p_yvel
:   sta p_yvel
    ; headOut && YVel < 0 && !(Up && A held) -> YVel = 0
    lda e_t5
    beq @nohead
    lda p_yvel
    bpl @leap
    lda pad_held
    and #(BTN_UP|BTN_A)
    cmp #(BTN_UP|BTN_A)
    beq @leap
    stz p_yvel
@leap:
    lda pad_held
    and #BTN_UP
    beq @nohead
    lda c_apressed
    beq @nohead
    lda #$10000-$34
    sta p_yvel
    stz p_swimming
@nohead:
    lda #1
    sta p_inair
    lda e_t1
    ora e_t2
    beq :+
    inc p_swimanim
:   rts

frog_land:
    lda c_dir
    bne :+
    lda p_xvel
    jsr sign16
    NEG16
    clc
    adc p_xvel
    sta p_xvel
    rts
:   lda w_frame
    and #31
    bne :+
    stz p_xvel
:   lda #$18
    ldx pad_held
    txa
    and #BTN_B
    beq :+
    lda #$28
    bra :++
:   lda #$18
:   sta e_t2
    lda p_xvel
    jsr abs16
    cmp e_t2
    bcs :+
    lda c_dir
    asl a
    clc
    adc p_xvel
    sta p_xvel
:   rts

; ------------------------------------------------------------------ vines
; carry set = grabbed
try_grab_vine:
    lda p_carrying
    ora p_statue
    bne @no
    lda pad_held
    and #BTN_UP
    bne @want
    lda p_inair
    beq @no
    lda pad_held
    and #BTN_DOWN
    beq @no
@want:
    jsr small_box
    tax
    lda #16
    cpx #0
    beq :+
    lda #24
:   sta e_t0
    jsr get_py
    clc
    adc e_t0
    tay
    jsr get_px
    clc
    adc #8
    jsl eng_tile_at_px
    cmp #T_VINE
    bne @no
    lda #PS_VINE
    sta p_state
    lda #1
    sta p_climbing
    stz p_xvel
    stz p_yvel
    stz p_flytime
    stz p_wagcount
    stz p_ducking
    jsr get_px
    clc
    adc #8
    and #$FFF0
    asl a
    asl a
    asl a
    asl a
    sta p_x
    sec
    rts
@no: clc
    rts

; VineAt(px = A, py = Y) -> Z clear if vine
vine_at:
    cpy #0
    bpl @n
    cpy #$10000-48
    bcc @n
    ldy #0
@n: jsl eng_tile_at_px
    cmp #T_VINE
    beq @y
    lda #0
    rts
@y: lda #1
    rts

climb_control:
    jsr small_box
    tax
    lda #16
    cpx #0
    beq :+
    lda #24
:   sta e_t6                    ; body offset
    lda c_apressed
    beq :+
    lda #PS_NORMAL
    sta p_state
    stz p_climbing
    lda #$10000-$38
    sta p_yvel
    lda #1
    sta p_inair
    SFX "JUMP"
    rts
:   ; vx
    stz e_t4
    lda pad_held
    and #BTN_LEFT
    beq :+
    lda #$10000-$10
    sta e_t4
    bra :++
:   lda pad_held
    and #BTN_RIGHT
    beq :+
    lda #$10
    sta e_t4
:   stz e_t5
    lda pad_held
    and #BTN_UP
    beq @down
    jsr get_py
    clc
    adc e_t6
    sec
    sbc #8
    tay
    jsr get_px
    clc
    adc #8
    jsr vine_at
    beq @down
    lda #$10000-$10
    sta e_t5
    bra @mv
@down:
    lda pad_held
    and #BTN_DOWN
    beq @mv
    lda #$10
    sta e_t5
@mv:
    lda p_x
    clc
    adc e_t4
    sta p_x
    lda p_y
    clc
    adc e_t5
    sta p_y
    lda e_t4
    beq :+
    jsr sign16
    sta p_facing
:   lda e_t4
    ora e_t5
    beq :+
    inc p_climbanim
:   ; leave the vine when it ends
    jsr get_py
    clc
    adc e_t6
    tay
    jsr get_px
    clc
    adc #8
    jsr vine_at
    bne :+
    lda #PS_NORMAL
    sta p_state
    stz p_climbing
    lda #1
    sta p_inair
    stz p_yvel
:   lda e_t5
    beq @top
    bmi @top
    jsr floor_below
    beq @top
    lda #PS_NORMAL
    sta p_state
    stz p_climbing
@top:
    ; climbing off the top of the area (link marker 9)
    jsr get_py
    clc
    adc #16
    bpl @r
    lda #9
    jsl eng_find_link
    bcs :+
    lda #$10000-(16*16)
    sta p_y
    rts
:   sta p_pipetarea
    stx p_pipetid
    lda #PS_NORMAL
    sta p_state
    stz p_climbing
    jsl w_arrive_at_climb
@r: rts

; FloorBelow -> Z clear if floor under either foot
floor_below:
    jsr get_py
    clc
    adc #32
    sta e_t0
    jsr get_px
    clc
    adc #4
    ldy e_t0
    jsl eng_tile_at_px
    PROPS
    and #TP_FLOOR
    bne @r
    jsr get_px
    clc
    adc #11
    ldy e_t0
    jsl eng_tile_at_px
    PROPS
    and #TP_FLOOR
@r: rts

; ================================================================== PowerUpdate (P-meter)
pl_power_update:
    ; running = !InAir && B held && !Sliding && |XVel| >= $28 && !Swimming
    stz e_t0
    lda p_inair
    ora p_sliding
    ora p_swimming
    bne @nr
    lda pad_held
    and #BTN_B
    beq @nr
    lda p_xvel
    jsr abs16
    cmp #$28
    bcc @nr
    inc e_t0
@nr:
    lda p_pwing
    beq :+
    lda #$7F
    sta p_power
    lda p_flytime
    bne @r
    lda #$FF
    sta p_flytime
@r: rtl
:   lda p_flytime
    bne @chk
    lda p_power
    cmp #$7F
    bne @chk
    lda e_t0
    beq @chk
    lda #$10
    sta p_pmetercnt
    rtl
@chk:
    lda p_pmetercnt
    bne @r
    lda e_t0
    beq @decay
    lda p_power
    asl a
    ora #1
    and #$7F
    sta p_power
    lda #8
    sta p_pmetercnt
    rtl
@decay:
    lda p_flytime
    bne @r
    lsr p_power
    lda #$18
    sta p_pmetercnt
    rtl

; ================================================================== DetectSolids (docs/01 §10)
pl_detect_solids:
    lda p_state
    cmp #PS_VINE
    bne :+
    rtl
:   lda area_slopes
    beq :+
    jmp detect_sloped
:   jsr get_px
    sta d_px
    jsr get_py
    sta d_py
    jsr small_box
    sta d_small
    ; rightSide = (px & 15) < 8
    stz d_right
    lda d_px
    and #15
    cmp #8
    bcs :+
    inc d_right
:   ; sx
    lda d_right
    beq @sl
    lda #14
    ldx d_small
    beq :+
    lda #13
:   bra @sx
@sl: lda #1
    ldx d_small
    beq :+
    lda #2
:
@sx: clc
    adc d_px
    sta e_t7                    ; probe x
    ; side = Solid(px+sx, py+27) || Solid(px+sx, py+sy2)
    lda d_py
    clc
    adc #27
    tay
    lda e_t7
    jsr solid_px
    sta d_side
    bne @sd
    lda #14
    ldx d_small
    beq :+
    lda #20
:   clc
    adc d_py
    tay
    lda e_t7
    jsr solid_px
    sta d_side
@sd:
    jsl w_touch_tiles
    ; ---- walls
    lda d_side
    beq @nowall
    lda p_statue
    bne @nowall
    lda d_right
    beq @wl
    lda #14
    ldx d_small
    beq :+
    lda #13
:   clc
    adc d_px
    and #15
    beq :+
    lda p_x
    sec
    sbc #16
    sta p_x
:   lda p_xvel
    beq @wd
    bmi @wd
    stz p_xvel
    bra @wd
@wl: lda #2
    ldx d_small
    beq :+
    lda #3
:   clc
    adc d_px
    and #15
    beq :+
    lda p_x
    clc
    adc #16
    sta p_x
:   lda p_xvel
    bpl @wd
    stz p_xvel
@wd: jsr get_px
    sta d_px
@nowall:
    ; ---- vertical
    lda p_yvel
    bpl @down
    lda p_inair
    beq @down
    ; head
    lda #6
    ldx d_small
    beq :+
    lda #16
:   clc
    adc d_py
    sta e_t6                    ; hy
    tay
    lda d_px
    clc
    adc #8
    jsl eng_tile_at_px
    cmp #T_HIDDENBLOCK
    beq @bonk
    PROPS
    and #TP_SOLID
    beq @r
@bonk:
    stz p_yvel
    lda d_px
    clc
    adc #8
    ASR4
    sta e_tx
    lda e_t6
    ASR4
    sta e_ty
    jsl w_head_bump
@r: rtl
@down:
    lda d_py
    clc
    adc #32
    sta d_fy
    tay
    lda d_px
    clc
    adc #4
    jsl eng_tile_at_px
    sta d_f1
    ldy d_fy
    lda d_px
    clc
    adc #11
    jsl eng_tile_at_px
    sta d_f2
    PROPS
    and #TP_FLOOR
    sta e_t5
    lda d_f1
    PROPS
    and #TP_FLOOR
    sta e_t4                    ; floor(f1)
    ora e_t5
    jeq @nofloor
    lda d_fy
    and #15
    cmp #6
    jcs @deep
    ; land / stand
    cmp #1
    bne :+
    lda p_y
    sec
    sbc #16
    sta p_y
    bra @lnd
:   cmp #2
    bcc @lnd
    lda p_y
    sec
    sbc #32
    sta p_y
@lnd:
    lda p_inair
    beq :+
    jsr on_land
:   stz p_inair
    stz p_yvel
    stz p_killtally
    ; conveyor
    lda #0
    ldx d_f1
    cpx #T_CONVEYORL
    beq @cl
    ldx d_f2
    cpx #T_CONVEYORL
    beq @cl
    ldx d_f1
    cpx #T_CONVEYORR
    beq @cr
    ldx d_f2
    cpx #T_CONVEYORR
    beq @cr
    bra @cs
@cl: lda #$10000-16
    bra @cs
@cr: lda #16
@cs: sta p_conveyor
    ; StandOn(floor(f1) ? f1 : f2, ...)
    lda e_t4
    beq :+
    lda d_f1
    ldx #4
    bra :++
:   lda d_f2
    ldx #11
:   sta e_t2
    txa
    clc
    adc d_px
    ASR4
    sta e_tx
    lda d_fy
    ASR4
    sta e_ty
    lda e_t2
    jsl w_stand_on
    rtl
@deep:
    lda p_inair
    bne :+
    lda #1
    sta p_inair
:   rtl
@nofloor:
    lda p_inair
    bne @r2
    jsl ent_platform_support
    bcs @r2
    ; walked off a ledge
    lda #1
    sta p_inair
    stz p_yvel
    stz p_conveyor
    stz p_onslope
    lda p_allowairjump
    bne @r2
    lda #6                      ; Modern: CoyoteTicks + 1
    sta p_allowairjump
@r2: rtl

; ------------------------------------------------------------------ sloped probe set
detect_sloped:
    jsr get_px
    sta d_px
    jsr get_py
    sta d_py
    jsr small_box
    sta d_small
    stz d_right
    lda d_px
    and #15
    cmp #8
    bcs :+
    inc d_right
:   lda #3
    ldx d_right
    beq :+
    lda #13
:   clc
    adc d_px
    sta e_t7
    lda #24
    ldx d_small
    beq :+
    lda #23
:   clc
    adc d_py
    tay
    lda e_t7
    jsr solid_px
    sta d_side
    bne @sd
    lda #12
    ldx d_small
    beq :+
    lda #23
:   clc
    adc d_py
    tay
    lda e_t7
    jsr solid_px
    sta d_side
@sd:
    jsl w_touch_tiles
    lda d_side
    beq @nowall
    lda p_statue
    bne @nowall
    lda d_right
    beq @wl
    lda d_px
    clc
    adc #13
    and #15
    beq :+
    lda p_x
    sec
    sbc #16
    sta p_x
:   lda p_xvel
    beq @wd
    bmi @wd
    stz p_xvel
    stz p_slide
    bra @wd
@wl: lda d_px
    clc
    adc #4
    and #15
    beq :+
    lda p_x
    clc
    adc #16
    sta p_x
:   lda p_xvel
    bpl @wd
    stz p_xvel
    stz p_slide
@wd: jsr get_px
    sta d_px
@nowall:
    lda p_yvel
    bpl @down
    lda p_inair
    beq @down
    lda #5
    ldx d_small
    beq :+
    lda #18
:   clc
    adc d_py
    sta e_t6
    tay
    lda d_px
    clc
    adc #8
    jsl eng_tile_at_px
    cmp #T_HIDDENBLOCK
    beq @bonk
    PROPS
    and #TP_SOLID
    beq @nob
@bonk:
    stz p_yvel
    lda d_px
    clc
    adc #8
    ASR4
    sta e_tx
    lda e_t6
    ASR4
    sta e_ty
    jsl w_head_bump
@nob:
    stz p_onslope
    rtl
@down:
    lda d_px
    clc
    adc #8
    sta d_cx
    lda d_py
    clc
    adc #32
    sta d_fy
    jsr find_surface
    jcc @nosurf
    ; depth = fy - surf
    lda d_fy
    sec
    sbc d_surf
    sta e_t5                    ; depth (signed)
    lda d_ft
    PROPS
    and #TP_SLOPE
    sta e_t4                    ; slope?
    ; above = !InAir ? 8 : 0 ; below = slope ? 12 : 6
    lda #0
    ldx p_inair
    bne :+
    lda #8
:   NEG16
    sta e_t3                    ; -above
    lda #6
    ldx e_t4
    beq :+
    lda #12
:   sta e_t2                    ; below
    lda e_t5
    sec
    sbc e_t3
    jmi @nosurf                 ; depth < -above
    lda e_t5
    sec
    sbc e_t2
    jpl @nosurf                 ; depth >= below
    ; snap
    lda e_t4
    ora p_onslope
    bne @snap
    lda e_t5
    beq @snap
    bmi @snap
    cmp #1
    bne :+
    lda p_y
    sec
    sbc #16
    sta p_y
    bra @landed
:   lda p_y
    sec
    sbc #32
    sta p_y
    bra @landed
@snap:
    lda d_surf
    sec
    sbc #32
    asl a
    asl a
    asl a
    asl a
    sta p_y
@landed:
    lda p_inair
    beq :+
    jsr on_land
:   stz p_inair
    stz p_yvel
    stz p_killtally
    ; OnSlope = slope ? push : 0
    lda #0
    ldx e_t4
    beq @os
    lda d_ft
    cmp #T_SLOPEUP
    bne :+
    lda #$10000-3
    bra @os
:   lda #3
@os: sta p_onslope
    lda #0
    ldx d_ft
    cpx #T_CONVEYORL
    bne :+
    lda #$10000-16
:   cpx #T_CONVEYORR
    bne :+
    lda #16
:   sta p_conveyor
    lda e_t4
    bne @r
    lda d_cx
    ASR4
    sta e_tx
    lda d_surf
    ASR4
    sta e_ty
    lda d_ft
    jsl w_stand_on
@r: rtl
@nosurf:
    lda p_inair
    bne @air
    jsl ent_platform_support
    bcs @r
    lda #1
    sta p_inair
    stz p_yvel
    stz p_onslope
    stz p_conveyor
    lda p_allowairjump
    bne @r
    lda #6
    sta p_allowairjump
    rtl
@air:
    stz p_onslope
    rtl

; FindSurface(cx = d_cx, fy = d_fy, grounded = !InAir) -> carry set if found, d_surf, d_ft
find_surface:
    lda #$7000
    sta d_surf
    stz d_ft
    lda d_cx
    ASR4
    sta e_tx
    stz d_k
@k: lda d_k
    ldx p_inair
    bne :+
    cmp #3
    bra :++
:   cmp #2
:   jcs @done
    ; yy = k==0 ? fy-16 : k==1 ? fy : fy+8
    lda d_k
    bne :+
    lda d_fy
    sec
    sbc #16
    bra @yy
:   cmp #1
    bne :+
    lda d_fy
    bra @yy
:   lda d_fy
    clc
    adc #8
@yy: ASR4
    sta e_ty
    jsl eng_tile_at
    sta e_t0                    ; t
    PROPS
    sta e_t1
    and #TP_SLOPE
    beq @notslope
    ; SlopeSurface: lx = cx - tx*16 clamped 0..15 ; ty*16 + (Up ? 15-lx : lx)
    lda e_tx
    asl a
    asl a
    asl a
    asl a
    sta e_t2
    lda d_cx
    sec
    sbc e_t2
    bpl :+
    lda #0
:   cmp #16
    bcc :+
    lda #15
:   sta e_t2
    lda e_t0
    cmp #T_SLOPEUP
    bne :+
    lda #15
    sec
    sbc e_t2
    sta e_t2
:   lda e_ty
    asl a
    asl a
    asl a
    asl a
    clc
    adc e_t2
    bra @have
@notslope:
    lda e_t1
    and #TP_FLOOR
    beq @next
    lda d_k
    beq @next
    lda e_ty
    asl a
    asl a
    asl a
    asl a
@have:
    ; if (s < fy - 16) continue ; if (s < surf) take
    sta e_t2
    lda d_fy
    sec
    sbc #16
    sta e_t3
    lda e_t2
    sec
    sbc e_t3
    bmi @next
    lda e_t2
    sec
    sbc d_surf
    bpl @next
    lda e_t2
    sta d_surf
    lda e_t0
    sta d_ft
@next:
    inc d_k
    jmp @k
@done:
    lda d_surf
    cmp #$7000
    beq @nf
    sec
    rts
@nf: clc
    rts

on_land:
    stz p_somersault
    stz p_pjumppose
    stz p_nolowgrav
    stz p_wagcount
    jsr tail_suit
    bne :+
    lda p_pwing
    bne :+
    stz p_flytime
:   lda pad_held
    and #BTN_DOWN
    bne :+
    stz p_ducking
:   lda p_statue
    beq :+
    jsl ent_statue_landed       ; a falling statue crushes enemies under it
:   rts

; ================================================================== Timers
pl_timers:
    lda p_pmetercnt
    beq :+
    dec p_pmetercnt
:   lda p_allowairjump
    beq :+
    dec p_allowairjump
:   lda p_hurtinv
    beq :+
    dec p_hurtinv
:   lda p_star
    beq @nostar
    dec p_star
    lda p_star
    cmp #64
    bne :+
    jsl w_play_area_music
:
@nostar:
    lda p_tailattack
    beq @notail
    dec p_tailattack
    lda p_tailattack
    cmp #$0B
    beq @flip
    cmp #$03
    bne :+
@flip: lda p_facing
    NEG16
    sta p_facing
    lda p_tailattack
:   cmp #$0C
    beq @th
    cmp #$09
    bne @notail
@th: jsl ent_tail_hit
@notail:
    lda p_statue
    beq :+
    dec p_statue
    bne :+
    jsr end_statue
:   lda p_throwpose
    beq :+
    dec p_throwpose
:   lda p_kickpose
    beq :+
    dec p_kickpose
:   lda p_invisible
    beq :+
    dec p_invisible
:   lda p_flytime
    beq @r
    cmp #$FF
    beq @r
    lda w_frame
    and #1
    beq @r
    dec p_flytime
    bne @r
    stz p_power
@r: rtl

; ================================================================== Animate (PlayerDraw.Animate)
walk_ticks: .word 7, 6, 5, 4, 3, 2, 1, 1, 1
pl_animate:
    lda p_inair
    bne @r
    lda p_state
    beq @anim
    cmp #PS_AUTOWALK          ; the goal walk-off animates like normal walking
    bne @r
@anim:
    lda p_xvel
    jsr abs16
    sta e_t0
    bne @go
    lda p_skidding
    bne @go
    lda #2
    sta p_animframe
    stz p_animtick
@r: rtl
@go:
    lda p_animtick
    beq :+
    dec p_animtick
    rtl
:   lda p_animframe
    inc a
    and #3
    sta p_animframe
    lda e_t0
    lsr a
    lsr a
    lsr a
    cmp #8
    bcc :+
    lda #8
:   asl a
    tax
    lda f:walk_ticks,x
    sta e_t1
    jsr ice_under
    beq :+
    inc e_t1
:   lda e_t1
    sta p_animtick
    rtl

; ================================================================== damage / power (Player.Hurt etc.)
; Invulnerable -> Z clear if invulnerable
invulnerable:
    lda p_hurtinv
    ora p_star
    ora p_statue
    ora p_transform
    bne @r
    lda p_state
    beq @r
    cmp #PS_VINE
    beq @z
    lda #1
@r: rts
@z: lda #0
    rts

pl_hurt:
    jsr invulnerable
    beq :+
    rtl
:   stz p_pwing
    lda p_form
    bne :+
    lda #0
    jmp pl_die
:   cmp #PF_BIG
    bne @suit
    lda #PF_SMALL
    ldx #2
    ldy #$2F
    jsr start_transform
    SFX "PIPE"
    bra @inv
@suit:
    lda #PF_BIG
    ldx #3
    ldy #$17
    jsr start_transform
    jsl ent_add_fx_puff_player
    SFX "PIPE"
@inv:
    lda #$71
    sta p_hurtinv
    lda p_carrying
    beq :+
    jsl ent_drop_carried
:   rtl

; A = to form, X = kind, Y = ticks
start_transform:
    sta e_t0
    lda p_form
    sta p_transfrom
    stx p_transformkind
    sty p_transform
    lda e_t0
    sta p_form
    bne :+
    stz p_ducking
:   jsr tail_suit
    bne :+
    stz p_wagcount
:   lda p_transform
    sta w_halt
    rts

; pl_powerup: A = target form (PF_BIG, PF_FIRE, PF_RACCOON). Adds 1000 points.
pl_powerup:
    sta e_t7
    jsr powerup_score
    lda e_t7
    cmp #PF_BIG
    bne @notbig
    lda p_form
    bne @snd
    lda #PF_BIG
    ldx #1
    ldy #$2F
    jsr start_transform
@snd: SFX "POWERUP"
    rtl
@notbig:
    cmp p_form
    beq @snd
    cmp #PF_FIRE
    bne @poof
    lda p_form
    cmp #PF_BIG+1
    bcs @poof
    lda #PF_FIRE
    ldx #4
    ldy #$1F
    jsr start_transform
    bra @snd
@poof:
    lda e_t7
    ldx #3
    ldy #$17
    jsr start_transform
    jsl ent_add_fx_puff_player
    lda e_t7
    cmp #PF_RACCOON
    bne @snd
    SFX "POOF"
    rtl

powerup_score:
    jsr get_px
    sta e_t0
    jsr get_py
    ldx p_form
    bne :+
    clc
    adc #16
:   tay
    ldx e_t0
    lda #$1000                  ; BCD 1000
    jsl w_add_score_at
    rts

pl_get_star:
    lda #448
    sta p_star
    jsr powerup_score
    jsl w_star_started
    rtl

; pl_die: A = 1 for a pit death
pl_die:
    tax
    lda p_state
    cmp #PS_DYING
    bne :+
    rtl
:   lda #PS_DYING
    sta p_state
    stz p_deathtimer
    stz p_xvel
    lda #$10000-$40
    cpx #0
    beq :+
    lda #0
:   sta p_yvel
    stz p_form
    stz p_ducking
    stz p_star
    stz p_flytime
    stz p_power
    lda p_carrying
    beq :+
    jsl ent_drop_carried
:   jsl w_on_player_dying
    rtl

pl_dying_tick:
    inc p_deathtimer
    lda p_deathtimer
    cmp #$30
    bcc @r
    lda p_y
    clc
    adc p_yvel
    sta p_y
    lda p_yvel
    clc
    adc #2
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta p_yvel
@r: rtl

; pl_bounce: A = yvel
pl_bounce:
    sta p_yvel
    lda #1
    sta p_inair
    stz p_jumpbuffer
    stz p_allowairjump
    rtl

