; Bosses: shared helpers (port of C# Boss base class, src/Game/Entities/Bosses.cs) + the "?" orb / wand that ends a
; fortress / airship (C# Orb, Items.cs). Owner: bosses agent. Bank CODE10.
;@entity BOSS_ORB
.p816
.smart
.macpack longbranch
.include "snes.inc"
.include "ent.inc"

.include "boss.inc"
.import snd_cur
.ifdef RES_FORTRESS
.global w_boss_clear            ; engine: A16 A = RES_FORTRESS / RES_WORLD (DESIGN.md API request)
.endif

.segment "BSS"
bm_a: .res 4                    ; 32-bit math scratch
bm_b: .res 4
bm_r: .res 4
bm_n2: .res 4                   ; (|d|*sp)^2
bm_d2: .res 4                   ; dx^2 + dy^2
bm_m: .res 2
bm_v: .res 2
bm_sp: .res 2
bm_dx: .res 2
bm_dy: .res 2
bm_s: .res 2
bm_ox: .res 2
bm_oy: .res 2

.segment "CODE10"
ENT_VTABLE BOSS_ORB, orb_init, orb_update, orb_draw, ent_cb_none, ent_cb_none, orb_touch

.a16
.i16

; ------------------------------------------------------------------ Boss.Activate(music)
; boss_activate: A = SONG_* -> W.BossArena = true (camera lock) and the boss song (only if it isn't already playing:
; the area music may already be the boss song, uploaded under the area-load blank). Keeps X.
boss_activate:
    pha
    lda #1
    sta w_bossarena
    pla
; boss_music: A = SONG_* -> snd_music unless that song is the current one (a level-song upload stalls ~0.2-0.65 s,
; see DESIGN.md; the jingles after a boss are requested as co-resident pairs). Keeps X.
boss_music:
    sta bm_m
    lda f:snd_cur
    and #$00FF
    cmp bm_m
    beq @r
    phx
    phy
    sep #$20
    lda bm_m
    jsl snd_music
    rep #$30
    ply
    plx
@r: rtl

; boss_result: A = level result for the end of the level (C# W.Result = r), used by Bowser (GameCleared). Keeps X.
boss_result:
.ifdef RES_GAME
    lda #RES_GAME
.else
    lda #1                      ; engine without RES_*: treat as a cleared level
.endif
    sta w_result
    rtl

; boss_player_near: A = dist -> carry set if |P.CenterX - Cx| < dist (C# PlayerNear). Keeps X.
boss_player_near:
    sta bm_m
    jsl ent_player_dx
    bpl :+
    NEG16
:   cmp bm_m
    bcc @y
    clc
    rtl
@y: sec
    rtl

; boss_approach: A = target, Y = step -> XVel moves by step toward target (C# if (XVel < t) XVel += s; else if > ...).
boss_approach:
    sta bm_m
    sty bm_s
    lda ent_xvel,x
    sec
    sbc bm_m
    beq @r
    bpl @dn
    lda ent_xvel,x
    clc
    adc bm_s
    sta ent_xvel,x
    rtl
@dn: lda ent_xvel,x
    sec
    sbc bm_s
    sta ent_xvel,x
@r: rtl

; ------------------------------------------------------------------ Boss.Defeated(r, orbX, orbY)
; boss_defeated: A = BOSS_R_FORTRESS / BOSS_R_WORLD, ent_new_x/ent_new_y = orb position (px).
; KnockOff(Facing), BossHit, "bosswin", +10000 at (Px, Py), Orb, MarkKilled. Keeps X.
boss_defeated:
    pha
    lda ent_new_x
    pha
    lda ent_new_y
    pha
    lda ent_facing,x
    jsl ent_knock_off           ; ent_points = 0: no score here
    ENT_SFX "BOSSHIT"
    lda #SONG_BOSSWIN
    jsl boss_music
    ; W.AddScore(10000, Px, Py): score + ScorePopup "10000" (the popup shows up to 4 BCD digits: "1000" + "0")
    lda #$5000
    jsl w_add_score
    lda #$5000
    jsl w_add_score
    jsl ent_px
    sta ent_new_x
    jsl ent_py
    sta ent_new_y
    lda #$1000
    jsl ent_popup
    lda ent_new_x
    clc
    adc #20
    sta ent_new_x
    lda #0
    jsl ent_popup
    pla
    sta ent_new_y
    pla
    sta ent_new_x
    pla
    tay                         ; arg = result
    lda #ET_BOSS_ORB
    jsl ent_spawn
    jsl ent_kill
    rtl

; ------------------------------------------------------------------ Koopaling magic aim (exact C# double math)
; boss_ring_vel: bm_dx, bm_dy (px, signed), A = speed sp -> bm_dx = (int)(dx/len*sp), bm_dy = (int)(dy/len*sp) with
; len = max(1, sqrt(dx^2+dy^2)). |v| = floor(|d|*sp/sqrt(d2)) = the largest v with v^2*d2 <= (|d|*sp)^2 (exact).
boss_ring_vel:
    sta bm_sp
    ; d2 = dx^2 + dy^2 (32-bit)
    lda bm_dx
    jsr abs16
    jsr sq16                    ; bm_r = dx^2
    lda bm_r
    sta bm_d2
    lda bm_r+2
    sta bm_d2+2
    lda bm_dy
    jsr abs16
    jsr sq16
    lda bm_r
    clc
    adc bm_d2
    sta bm_d2
    lda bm_r+2
    adc bm_d2+2
    sta bm_d2+2
    ora bm_d2
    bne :+
    stz bm_dx                   ; dx = dy = 0 -> 0/1*sp = 0
    stz bm_dy
    rtl
:   ; s = isqrt(d2) (>= 1)
    lda bm_d2
    sta e_t0
    lda bm_d2+2
    sta e_t1
    jsl ent_isqrt
    sta bm_s
    lda bm_dx
    jsr axis
    sta bm_dx
    lda bm_dy
    jsr axis
    sta bm_dy
    rtl

; A = d (signed) -> A = sign(d) * floor(|d|*sp/sqrt(d2))
axis:
    sta bm_ox                   ; keep the sign
    jsr abs16
    beq @z
    ; n = |d| * sp (16-bit: |d| < 1024, sp < 64)
    sta bm_a
    stz bm_a+2
    lda bm_sp
    jsr mul32x16                ; bm_r = |d|*sp
    lda bm_r
    sta bm_oy                   ; n
    jsr sq16                    ; bm_r = n^2
    lda bm_r
    sta bm_n2
    lda bm_r+2
    sta bm_n2+2
    ; v = min(sp, n / s) as a start (true value <= n/sqrt(d2) <= n/s), then step down until v^2*d2 <= n^2
    lda bm_oy
    ldy bm_s
    jsr div16
    cmp bm_sp
    bcc :+
    lda bm_sp
:   sta bm_v
@chk:
    lda bm_v
    beq @done
    jsr sq16v                   ; bm_b = v^2
    ; bm_r = v^2 * d2 (d2 < 2^18, v^2 < 2^12: fits 32 bits)
    lda bm_d2
    sta bm_a
    lda bm_d2+2
    sta bm_a+2
    lda bm_b
    jsr mul32x16
    ; v^2*d2 <= n^2 ?
    lda bm_r+2
    cmp bm_n2+2
    bcc @done
    bne @dec
    lda bm_r
    cmp bm_n2
    bcc @done
    beq @done
@dec: dec bm_v
    bra @chk
@done:
    lda bm_v
    ldy bm_ox
    bpl :+
    NEG16
:   rts
@z: lda #0
    rts

abs16:
    cmp #0
    bpl :+
    NEG16
:   rts

; bm_b = v^2 (16-bit) from bm_v
sq16v:
    lda bm_v
    sta bm_a
    stz bm_a+2
    lda bm_v
    jsr mul32x16
    lda bm_r
    sta bm_b
    rts

; A (16-bit unsigned) -> bm_r = A*A
sq16:
    sta bm_a
    stz bm_a+2
    jmp mul32x16

; bm_r = bm_a (32-bit) * A (16-bit), truncated to 32 bits (bm_a is preserved)
mul32x16:
    sta bm_m
    stz bm_r
    stz bm_r+2
    lda bm_a
    pha
    lda bm_a+2
    pha
@l: lda bm_m
    beq @d
    lsr bm_m
    bcc @s
    lda bm_r
    clc
    adc bm_a
    sta bm_r
    lda bm_r+2
    adc bm_a+2
    sta bm_r+2
@s: asl bm_a
    rol bm_a+2
    bra @l
@d: pla
    sta bm_a+2
    pla
    sta bm_a
    rts
; A / Y (16-bit unsigned, Y > 0) -> A = quotient
div16:
    sta bm_a
    sty bm_b
    stz bm_r                    ; remainder
    ldy #16
@l: asl bm_a
    rol bm_r
    lda bm_r
    cmp bm_b
    bcc :+
    sbc bm_b
    sta bm_r
    inc bm_a
:   dey
    bne @l
    lda bm_a
    rts

; ================================================================== Orb (C# Orb): arg = result
orb_init:
    ENT_ENTER
    lda #EC_SPECIAL
    sta ent_class,x
    lda ent_fl,x
    and #$FFFF^(F_HURTS|F_SLOT|F_STOMP)
    sta ent_fl,x
    lda #$10000-$30
    sta ent_yvel,x
    sec
    rtl

orb_update:
    ENT_ENTER
    lda ent_yvel,x
    clc
    adc #3
    bmi :+
    cmp #$41
    bcc :+
    lda #$40
:   sta ent_yvel,x
    lda ent_y,x
    clc
    adc ent_yvel,x
    sta ent_y,x
    ; if (YVel > 0 && (FloorAt(Px+4, Py+16) || FloorAt(Px+11, Py+16)))
    lda ent_yvel,x
    beq @r
    bmi @r
    jsl ent_py
    clc
    adc #16
    sta es0
    jsl ent_px
    clc
    adc #4
    ldy es0
    jsl ent_floor_at
    bcs @land
    jsl ent_px
    clc
    adc #11
    ldy es0
    jsl ent_floor_at
    bcc @r
@land:
    ; Y = ((Py + 16) & ~15) - 16 << 4 ; YVel = YVel > $20 ? -YVel / 2 : 0
    lda es0
    and #$FFF0
    sec
    sbc #16
    ENT_ASL4
    sta ent_y,x
    lda ent_yvel,x
    cmp #$21
    bcc @stop
    lsr a
    NEG16
    sta ent_yvel,x
@r: rtl
@stop:
    stz ent_yvel,x
    rtl

orb_draw:
    ENT_ENTER
    lda ent_arg,x
    cmp #BOSS_R_WORLD
    bne @orb
.ifdef SPR_WAND
    lda #4
    ldy #0
    jsl ent_draw_offset
    ENT_DRAW SPR_WAND
    rtl
.endif
@orb:
    ENT_DRAW SPR_ORB
    rtl

; OnPlayerTouch: Remove; W.StartClear(-1, result)
orb_touch:
    ENT_ENTER
    lda ent_fl,x
    and #F_REMOVE
    bne @r
    jsl ent_remove
.ifdef RES_FORTRESS
    lda ent_arg,x
    jsl w_boss_clear
.else
    lda #$FFFF                  ; engine without the boss-clear API yet: plain course clear, no card
    jsl w_start_clear
.endif
@r: rtl
