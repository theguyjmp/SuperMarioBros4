; =====================================================================================================
; SMB4 SPC700 sound driver. Owner: sound agent (see snes/SOUND.md).
; Assembled by src/Tools/Snes/SnesAudioAsm.cs; SnesAudio.cs prepends constants (MVOL, FIRn, DRIVER_ORG,
; NSFX) and appends the generated tables (pitch, pan, volume, sample info, kit, SFX programs, DIR, resident
; samples). Ports: CPU->SPC  0 = command (toggle bit 7), 1 = argument, 2/3 = SFX requests (id | toggle bit 7)
;                  SPC->CPU  0 = command ack, 1 = status ($80 alive | 1 music playing), 2/3 = SFX acks
; =====================================================================================================

; ---- I/O
CONTROL = $F1
DSPADDR = $F2
DSPDATA = $F3
CPU0 = $F4
CPU1 = $F5
CPU2 = $F6
CPU3 = $F7
T0DIV = $FA
T0OUT = $FD

; ---- DSP registers
VOLL_R = 0
VOLR_R = 1
PITCHL_R = 2
PITCHH_R = 3
SRCN_R = 4
ADSR1_R = 5
ADSR2_R = 6
GAIN_R = 7
ENVX_R = 8
MVOLL_R = $0C
MVOLR_R = $1C
EVOLL_R = $2C
EVOLR_R = $3C
KON_R = $4C
KOFF_R = $5C
FLG_R = $6C
EFB_R = $0D
PMON_R = $2D
NON_R = $3D
EON_R = $4D
DIR_R = $5D
ESA_R = $6D
EDL_R = $7D

; ---- direct page
t0 = $00
t1 = $01
t2 = $02
t3 = $03
t4 = $04
t5 = $05
t6 = $06
t7 = $07
u_lo = $08
u_hi = $09
p_lo = $0A
p_hi = $0B
ptr = $0C
cb_lo = $0E
cb_hi = $0F
cs_lo = $10
cs_hi = $11
ti_lo = $12
ti_hi = $13
te_lo = $14
te_hi = $15
song_on = $16
paused = $17
hurry = $18
fade_len = $19
fade_left = $1A
fade8 = $1B
last0 = $1C
last2 = $1D
last3 = $1E
kon = $1F
eon = $20
non = $21
flg = $22
echo_wait = $23
e_vol = $24
e_fb = $25
e_dl = $26
e_sa = $27
sloop = $28
tcnt = $29
song = $2A
itab = $2C
wtab = $2E
cx = $30
cv = $31
lin = $33
pan = $34
n_lo = $35
n_hi = $36
guard = $37
hk = $38
alldone = $39
vl = $3A
vr = $3B
gate = $3C
nch = $3D
kk = $3E
e_flag = $3F
smp = $40
sfxv0 = $41
sfxv1 = $42
sx = $43
rp = $44
tn_lo = $46
tn_hi = $47
nzf = $48
sid = $49
tmo = $4A
ticks = $4C           ; 16-bit tick counter (tests)

; ---- channel arrays (8 entries each)
ch_pl = $0200
ch_ph = $0208
ch_done = $0210
ch_nbl = $0218
ch_nbh = $0220
ch_nph = $0228
ch_gbl = $0230
ch_gbh = $0238
ch_gph = $0240
ch_keyon = $0248
ch_note = $0250
ch_c2 = $0258
ch_c3 = $0260
ch_pc2 = $0268
ch_pc3 = $0270
ch_gll = $0278
ch_glh = $0280
ch_nframe = $0288
ch_vpl = $0290
ch_vph = $0298
ch_arpp = $02A0
ch_arps = $02A8
ch_vol = $02B0
ch_pan = $02B8
ch_inst = $02C0
ch_iskit = $02C8
ch_gate = $02D0
ch_vib = $02D8
ch_arp = $02E0
ch_echo = $02E8
ch_porta = $02F0
ch_det = $02F8
ch_tr = $0300
ch_piece = $0308
ch_trig = $0310
ch_full = $0318
ch_lpl = $0320
ch_lph = $0328
ch_depth = $0330
ch_sub1 = $0338
ch_sub2 = $0340
ch_nch = $0348
ch_irl = $0350
ch_irh = $0358
ch_gain = $0360
ch_sl = $0368
ch_sh = $0370
ch_hgrp = $0378
ls_lo = $0380
ls_hi = $03A0
ls_cnt = $03C0
; ---- voice arrays
v_lock = $0400        ; 0 music, 2 SFX, 3 handed back by SFX (music re-keys a held note)
v_borrow = $0408      ; channel borrowing this voice for a chord note / kit group, $FF none
v_samp = $0410
v_keyed = $0418
v_cpl = $0420
v_cph = $0428
v_cvl = $0430
v_cvr = $0438
; ---- SFX slot arrays (2 entries each)
sf_act = $0480
sf_id = $0482
sf_prio = $0484
sf_cs = $0486
sf_pl = $0488
sf_ph = $048A
sf_sl = $048C
sf_sh = $048E
sf_wait = $0490
sf_rmp = $0492
sf_ul = $0494
sf_uh = $0496
sf_dul = $0498
sf_duh = $049A
sf_vf = $049C
sf_vi = $049E
sf_dvl = $04A0
sf_dvh = $04A2
sf_pf = $04A4
sf_pi = $04A6
sf_dpl = $04A8
sf_dph = $04AA
sf_v = $04AC
sf_s = $04AE
sf_ech = $04B0

.org DRIVER_ORG
; =====================================================================================================
start:
    clrp
    mov x,#$EF
    mov sp,x
    mov a,#0
    mov x,a
st_z:
    mov (x)+,a
    cmp x,#$F0
    bne st_z
    mov ptr,#0
    mov ptr+1,#2
    mov y,#0
st_v:
    mov a,#0
    mov [ptr]+y,a
    inc y
    bne st_v
    inc ptr+1
    mov a,ptr+1
    cmp a,#6
    bne st_v
    mov CONTROL,#$30
    mov x,#0
st_d:
    mov a,!dspinit+x
    cmp a,#$FF
    beq st_dd
    mov DSPADDR,a
    inc x
    mov a,!dspinit+x
    mov DSPDATA,a
    inc x
    bra st_d
st_dd:
    mov flg,#$20
    mov fade8,#255
    mov sfxv0,#7
    mov sfxv1,#6
    mov a,#$FF
    mov x,#7
st_b:
    mov !v_borrow+x,a
    dec x
    bpl st_b
    mov T0DIV,#133
    mov CONTROL,#$01
    mov CPU0,#0
    mov CPU2,#0
    mov CPU3,#0
    mov CPU1,#$80

; =====================================================================================================
main:
    mov a,CPU0
    cmp a,last0
    beq mn_0
    call command
mn_0:
    mov a,CPU2
    cmp a,last2
    beq mn_2
    mov last2,a
    mov CPU2,a
    call sfx_request
mn_2:
    mov a,CPU3
    cmp a,last3
    beq mn_3
    mov last3,a
    mov CPU3,a
    call sfx_request
mn_3:
    mov a,T0OUT
    beq main
    cmp a,#3
    bcc mn_t
    mov a,#2
mn_t:
    mov tcnt,a
mn_tl:
    call tick
    dbnz tcnt,mn_tl
    bra main

; =====================================================================================================
command:
    mov last0,a
    mov t0,a
    mov t1,CPU1
    mov CPU0,t0
    mov a,t0
    and a,#$7F
    cmp a,#2
    bne cm_3
    jmp stop_song
cm_3:
    cmp a,#3
    bne cm_4
    mov hurry,t1
    jmp calc_te
cm_4:
    cmp a,#4
    bne cm_5
    mov a,t1
    bne cm_4b
    mov a,#60
cm_4b:
    mov fade_len,a
    mov fade_left,a
    ret
cm_5:
    cmp a,#5
    bne cm_6
    jmp set_pause
cm_6:
    cmp a,#6
    bne cm_7
    jmp receive
cm_7:
    cmp a,#7
    bne cm_8
    jmp stop_sfx_id
cm_8:
    ret

calc_te:
    mov te_lo,ti_lo
    mov te_hi,ti_hi
    mov a,hurry
    beq cte_r
    mov a,ti_lo
    mov y,#77
    mul ya
    mov t0,y
    mov a,ti_hi
    mov y,#77
    mul ya
    clrc
    adc a,t0
    mov t2,a
    mov a,y
    adc a,#0
    mov t3,a
    clrc
    mov a,te_lo
    adc a,t2
    mov te_lo,a
    mov a,te_hi
    adc a,t3
    mov te_hi,a
cte_r:
    ret

set_pause:
    mov paused,t1
    mov x,#0
sp_l:
    mov a,!v_lock+x
    cmp a,#2
    beq sp_n
    mov a,#$FF
    mov !v_cvl+x,a
    mov !v_cvr+x,a
    mov a,paused
    beq sp_n
    mov a,x
    xcn a
    mov DSPADDR,a
    mov DSPDATA,#0
    or a,#1
    mov DSPADDR,a
    mov DSPDATA,#0
sp_n:
    inc x
    cmp x,#8
    bne sp_l
    ret

stop_sfx_id:
    mov x,#0
ss_l:
    mov a,!sf_act+x
    beq ss_n
    mov a,!sf_id+x
    cmp a,t1
    bne ss_n
    mov t6,x
    call sfx_end
    mov x,t6
ss_n:
    inc x
    cmp x,#2
    bne ss_l
    ret

; ---- receive mode: blocks [type=1, addr] + data (2 bytes per handshake, port0 = even index) + $FF end;
;      [type=0, song address] ends (song address 0 = none)
receive:
    call stop_song
    call sfx_stop_all
    mov a,flg
    or a,#$20
    mov flg,a
    mov DSPADDR,#FLG_R
    mov DSPDATA,a
    mov DSPADDR,#EVOLL_R
    mov DSPDATA,#0
    mov DSPADDR,#EVOLR_R
    mov DSPDATA,#0
    mov echo_wait,#0
    mov hk,#$41
rv_hdr:
    mov a,hk
rv_hw:
    cmp a,CPU0
    bne rv_hw
    mov t1,CPU1
    mov t2,CPU2
    mov t3,CPU3
    mov CPU0,a
    mov last0,a
    eor hk,#$02
    mov a,t1
    beq rv_end
    mov a,t2
    mov !rv_s1+1,a
    mov a,t3
    mov !rv_s1+2,a
    mov a,t2
    clrc
    adc a,#1
    mov !rv_s2+1,a
    mov a,t3
    adc a,#0
    mov !rv_s2+2,a
    mov y,#0
rv_dl:
    cmp y,CPU0
    beq rv_got
    mov a,CPU0
    cmp a,#$FF
    bne rv_dl
    mov CPU0,a
    bra rv_hdr
rv_got:
    mov a,CPU1
rv_s1:
    mov !$0000+y,a
    mov a,CPU2
    mov CPU0,y
rv_s2:
    mov !$0001+y,a
    inc y
    inc y
    bne rv_dl
    inc !rv_s1+2
    inc !rv_s2+2
    bra rv_dl
rv_end:
    mov tmo,#0
    mov tmo+1,#0
rv_rs:
    mov a,CPU2
    or a,CPU3
    beq rv_rsok
    decw tmo
    bne rv_rs
rv_rsok:
    mov last2,#0
    mov last3,#0
    mov CPU2,#0
    mov CPU3,#0
    mov a,T0OUT
    mov a,t2
    or a,t3
    beq rv_ret
    mov song,t2
    mov song+1,t3
    jmp start_song
rv_ret:
    ret

; =====================================================================================================
start_song:
    mov y,#0
    mov a,[song]+y
    mov ti_lo,a
    inc y
    mov a,[song]+y
    mov ti_hi,a
    inc y
    mov a,[song]+y
    mov sloop,a
    inc y
    mov a,[song]+y
    mov e_dl,a
    inc y
    mov a,[song]+y
    mov e_fb,a
    inc y
    mov a,[song]+y
    mov e_vol,a
    inc y
    mov a,[song]+y
    mov e_sa,a
    inc y
    mov a,[song]+y
    mov sfxv0,a
    inc y
    mov a,[song]+y
    mov sfxv1,a
    inc y
    mov a,[song]+y
    mov itab,a
    inc y
    mov a,[song]+y
    mov itab+1,a
    inc y
    mov a,[song]+y
    mov wtab,a
    inc y
    mov a,[song]+y
    mov wtab+1,a
    inc y
    mov x,#0
ssg_ch:
    mov a,[song]+y
    mov !ch_pl+x,a
    mov !ch_sl+x,a
    mov t0,a
    inc y
    mov a,[song]+y
    mov !ch_ph+x,a
    mov !ch_sh+x,a
    inc y
    or a,t0
    beq ssg_e
    mov a,#0
    bra ssg_d
ssg_e:
    mov a,#1
ssg_d:
    mov !ch_done+x,a
    mov a,#0
    mov !ch_nbl+x,a
    mov !ch_nbh+x,a
    mov !ch_nph+x,a
    mov !ch_gbl+x,a
    mov !ch_gbh+x,a
    mov !ch_gph+x,a
    mov !ch_keyon+x,a
    mov !ch_gll+x,a
    mov !ch_glh+x,a
    mov !ch_nframe+x,a
    mov !ch_vpl+x,a
    mov !ch_vph+x,a
    mov !ch_arpp+x,a
    mov !ch_arps+x,a
    mov !ch_inst+x,a
    mov !ch_iskit+x,a
    mov !ch_vib+x,a
    mov !ch_arp+x,a
    mov !ch_echo+x,a
    mov !ch_porta+x,a
    mov !ch_det+x,a
    mov !ch_tr+x,a
    mov !ch_piece+x,a
    mov !ch_trig+x,a
    mov !ch_full+x,a
    mov !ch_lpl+x,a
    mov !ch_lph+x,a
    mov !ch_depth+x,a
    mov !ch_gain+x,a
    mov a,#$FF
    mov !ch_note+x,a
    mov !ch_sub1+x,a
    mov !ch_sub2+x,a
    mov a,#$80
    mov !ch_c2+x,a
    mov !ch_c3+x,a
    mov !ch_pc2+x,a
    mov !ch_pc3+x,a
    mov a,#12
    mov !ch_vol+x,a
    mov a,#20
    mov !ch_pan+x,a
    mov a,#7
    mov !ch_gate+x,a
    mov a,#1
    mov !ch_nch+x,a
    inc x
    cmp x,#8
    beq ssg_cx
    jmp ssg_ch
ssg_cx:
    mov x,#0
ssg_v:
    mov a,!v_lock+x
    cmp a,#2
    beq ssg_vn
    call vrelease
    mov a,#0
    mov !v_lock+x,a
ssg_vn:
    mov a,#$FF
    mov !v_borrow+x,a
    inc x
    cmp x,#8
    bne ssg_v
    mov cb_lo,#0
    mov cb_hi,#0
    mov cs_lo,#0
    mov cs_hi,#0
    mov hurry,#0
    call calc_te
    mov fade_len,#0
    mov fade8,#255
    mov paused,#0
    call echo_setup
    mov song_on,#1
    mov CPU1,#$81
    ret

echo_setup:
    mov a,flg
    or a,#$20
    mov flg,a
    mov DSPADDR,#FLG_R
    mov DSPDATA,a
    mov DSPADDR,#EVOLL_R
    mov DSPDATA,#0
    mov DSPADDR,#EVOLR_R
    mov DSPDATA,#0
    mov DSPADDR,#EFB_R
    mov DSPDATA,#0
    mov DSPADDR,#ESA_R
    mov DSPDATA,e_sa
    mov DSPADDR,#EDL_R
    mov DSPDATA,e_dl
    mov a,e_dl
    clrc
    adc a,#17
    mov echo_wait,a
    ret

stop_song:
    mov song_on,#0
    mov fade_len,#0
    mov fade8,#255
    mov x,#0
sts_l:
    mov a,!v_lock+x
    cmp a,#2
    beq sts_n
    call vrelease
    mov a,#0
    mov !v_lock+x,a
sts_n:
    mov a,#$FF
    mov !v_borrow+x,a
    inc x
    cmp x,#8
    bne sts_l
    mov CPU1,#$80
    ret

; =====================================================================================================
tick:
    incw ticks
    mov a,echo_wait
    beq tk_e
    dec echo_wait
    mov a,echo_wait
    cmp a,e_dl
    bne tk_e1
    mov a,flg
    and a,#$DF
    mov flg,a
tk_e1:
    mov a,echo_wait
    bne tk_e
    mov DSPADDR,#EFB_R
    mov DSPDATA,e_fb
    mov DSPADDR,#EVOLL_R
    mov DSPDATA,e_vol
    mov DSPADDR,#EVOLR_R
    mov DSPDATA,e_vol
tk_e:
    mov a,song_on
    beq tk_sfx
    mov a,paused
    bne tk_sfx
    call music_tick
tk_sfx:
    call sfx_tick
    mov a,kon
    beq tk_nk
    mov DSPADDR,#KON_R
    mov DSPDATA,a
    mov kon,#0
tk_nk:
    mov DSPADDR,#EON_R
    mov DSPDATA,eon
    mov DSPADDR,#NON_R
    mov DSPDATA,non
    mov DSPADDR,#FLG_R
    mov DSPDATA,flg
    ret

music_tick:
    ; fade (C#: g = (left/len)^2 applied to channel volumes; stops at 0)
    mov a,fade_len
    beq mt_nf
    dec fade_left
    mov a,fade_left
    bne mt_f1
    jmp stop_song
mt_f1:
    mov y,#255
    mul ya
    mov x,fade_len
    div ya,x
    mov y,a
    mul ya
    mov fade8,y
mt_nf:
    clrc
    mov a,cs_lo
    adc a,te_lo
    mov cs_lo,a
    mov a,cs_hi
    adc a,te_hi
    mov cs_hi,a
mt_norm:
    mov a,cs_hi
    cmp a,#$30
    bcc mt_ok
    sbc a,#$30
    mov cs_hi,a
    incw cb_lo
    bra mt_norm
mt_ok:
    mov alldone,#1
    mov x,#0
mt_ch:
    mov cx,x
    mov a,!ch_done+x
    bne mt_out
    call chan_events
    mov x,cx
    mov a,!ch_done+x
    bne mt_out
    mov alldone,#0
mt_out:
    call chan_output
    mov x,cx
    inc x
    cmp x,#8
    bne mt_ch
    mov a,alldone
    beq mt_r
    jmp stop_song
mt_r:
    ret

; ---- event timing: due when clock >= warp(next)
chan_events:
    mov guard,#0
ce_l:
    mov x,cx
    call due_next
    bcc ce_r
    call step_event
    mov x,cx
    mov a,!ch_done+x
    bne ce_r
    inc guard
    bne ce_l
ce_r:
    ret

due_next:
    mov a,cb_hi
    cmp a,!ch_nbh+x
    bne dn_r
    mov a,cb_lo
    cmp a,!ch_nbl+x
    bne dn_r
    mov a,!ch_nph+x
    asl a
    mov y,a
    mov a,[wtab]+y
    mov t0,a
    inc y
    mov a,[wtab]+y
    mov t1,a
    mov a,cs_hi
    cmp a,t1
    bne dn_r
    mov a,cs_lo
    cmp a,t0
dn_r:
    ret

due_gate:
    mov a,cb_hi
    cmp a,!ch_gbh+x
    bne dg_r
    mov a,cb_lo
    cmp a,!ch_gbl+x
    bne dg_r
    mov a,!ch_gph+x
    asl a
    mov y,a
    mov a,[wtab]+y
    mov t0,a
    inc y
    mov a,[wtab]+y
    mov t1,a
    mov a,cs_hi
    cmp a,t1
    bne dg_r
    mov a,cs_lo
    cmp a,t0
dg_r:
    ret

; C = 1 if next <= gate end (unwarped)
next_le_gate:
    mov a,!ch_gbh+x
    cmp a,!ch_nbh+x
    bne nlg_r
    mov a,!ch_gbl+x
    cmp a,!ch_nbl+x
    bne nlg_r
    mov a,!ch_gph+x
    cmp a,!ch_nph+x
nlg_r:
    ret

rd:
    mov y,#0
    mov a,[ptr]+y
    incw ptr
    ret

; duration -> t2:t3
rddur:
    call rd
    mov t2,a
    mov t3,#0
    cmp a,#0
    bne rdd_r
    call rd
    mov t2,a
    call rd
    mov t3,a
rdd_r:
    ret

; next += t2:t3
adv_next:
    mov x,cx
    mov a,!ch_nph+x
    clrc
    adc a,t2
    mov t0,a
    mov a,t3
    adc a,#0
    mov y,a
    mov a,t0
    mov x,#48
    div ya,x
    mov x,cx
    mov t0,a
    mov a,y
    mov !ch_nph+x,a
    mov a,!ch_nbl+x
    clrc
    adc a,t0
    mov !ch_nbl+x,a
    mov a,!ch_nbh+x
    adc a,#0
    mov !ch_nbh+x,a
    ret

; gate end = next + t4:t5
set_gate:
    mov x,cx
    mov a,!ch_nph+x
    clrc
    adc a,t4
    mov t0,a
    mov a,t5
    adc a,#0
    mov y,a
    mov a,t0
    mov x,#48
    div ya,x
    mov x,cx
    mov t0,a
    mov a,y
    mov !ch_gph+x,a
    mov a,!ch_nbl+x
    clrc
    adc a,t0
    mov !ch_gbl+x,a
    mov a,!ch_nbh+x
    adc a,#0
    mov !ch_gbh+x,a
    ret

; gate length t4:t5 from duration t2:t3 (C#: gate>=8 or next-is-tie -> dur, else max(1, dur*gate/8))
calc_g:
    mov x,cx
    mov a,!ch_full+x
    bne cg_full
    mov a,!ch_gate+x
    cmp a,#8
    bcs cg_full
    mov t6,a
    mov a,t2
    mov y,t6
    mul ya
    mov t4,a
    mov t5,y
    mov a,t3
    mov y,t6
    mul ya
    clrc
    adc a,t5
    mov t5,a
    mov a,y
    adc a,#0
    mov t7,a
    lsr t7
    ror t5
    ror t4
    lsr t7
    ror t5
    ror t4
    lsr t7
    ror t5
    ror t4
    mov a,t4
    or a,t5
    bne cg_r
    mov t4,#1
cg_r:
    ret
cg_full:
    mov t4,t2
    mov t5,t3
    ret

step_event:
    mov a,!ch_pl+x
    mov ptr,a
    mov a,!ch_ph+x
    mov ptr+1,a
    call rd
    cmp a,#$80
    bcs se_cmd
    call ev_note
    jmp ev_save
se_cmd:
    and a,#$7F
    cmp a,#$14
    bcs se_bad
    asl a
    mov x,a
    jmp [!evtab+x]
se_bad:
    jmp ev_end
ev_save:
    mov x,cx
    mov a,ptr
    mov !ch_pl+x,a
    mov a,ptr+1
    mov !ch_ph+x,a
    ret

evtab:
    .dw ev_rest, ev_tie, ev_full, ev_chord, ev_drum, ev_vol, ev_pan, ev_inst, ev_gate, ev_vib
    .dw ev_arp, ev_echo, ev_porta, ev_det, ev_tr, ev_tempo, ev_ls, ev_le, ev_lp, ev_end

ev_note:
    mov x,cx
    clrc
    adc a,!ch_tr+x
    mov t1,a
    push a
    call rddur
    pop a
    mov t1,a
    mov x,cx
    mov t0,#0
    mov a,!ch_porta+x
    beq en_nl
    mov a,!ch_keyon+x
    beq en_nl
    mov a,!ch_note+x
    cmp a,#$FF
    beq en_nl
    call next_le_gate
    bcc en_nl
    mov t0,#1
en_nl:
    mov a,t1
    mov !ch_note+x,a
    mov a,#1
    mov !ch_keyon+x,a
    mov a,!ch_pc2+x
    mov !ch_c2+x,a
    mov a,!ch_pc3+x
    mov !ch_c3+x,a
    mov a,#$80
    mov !ch_pc2+x,a
    mov !ch_pc3+x,a
    mov a,t0
    bne en_leg
    mov a,#1
    mov !ch_trig+x,a
    mov a,#0
    mov !ch_nframe+x,a
    mov !ch_arpp+x,a
    mov !ch_arps+x,a
    mov !ch_vpl+x,a
    mov !ch_vph+x,a
    mov !ch_gll+x,a
    mov a,t1
    mov !ch_glh+x,a
en_leg:
    call calc_g
    call set_gate
    mov x,cx
    mov a,#0
    mov !ch_full+x,a
    jmp adv_next

ev_rest:
    call rddur
    mov x,cx
    mov a,#0
    mov !ch_keyon+x,a
    call adv_next
    jmp ev_save
ev_tie:
    call rddur
    call calc_g
    call set_gate
    mov x,cx
    mov a,#0
    mov !ch_full+x,a
    call adv_next
    jmp ev_save
ev_full:
    mov x,cx
    mov a,#1
    mov !ch_full+x,a
    jmp ev_save
ev_chord:
    call rd
    mov x,cx
    mov !ch_pc2+x,a
    call rd
    mov x,cx
    mov !ch_pc3+x,a
    jmp ev_save
ev_drum:
    call rd
    mov x,cx
    mov !ch_piece+x,a
    call rddur
    mov x,cx
    mov a,#1
    mov !ch_trig+x,a
    mov !ch_keyon+x,a
    mov a,#0
    mov !ch_nframe+x,a
    mov t4,t2
    mov t5,t3
    call set_gate
    call adv_next
    jmp ev_save
ev_vol:
    call rd
    mov x,cx
    mov !ch_vol+x,a
    jmp ev_save
ev_pan:
    call rd
    mov x,cx
    mov !ch_pan+x,a
    jmp ev_save
ev_inst:
    call rd
    mov x,cx
    mov !ch_inst+x,a
    call set_inst
    jmp ev_save
ev_gate:
    call rd
    mov x,cx
    mov !ch_gate+x,a
    jmp ev_save
ev_vib:
    call rd
    mov x,cx
    mov !ch_vib+x,a
    jmp ev_save
ev_arp:
    call rd
    mov x,cx
    mov !ch_arp+x,a
    jmp ev_save
ev_echo:
    call rd
    mov x,cx
    mov !ch_echo+x,a
    jmp ev_save
ev_porta:
    call rd
    mov x,cx
    mov !ch_porta+x,a
    jmp ev_save
ev_det:
    call rd
    mov x,cx
    mov !ch_det+x,a
    jmp ev_save
ev_tr:
    call rd
    mov x,cx
    mov !ch_tr+x,a
    jmp ev_save
ev_tempo:
    call rd
    mov ti_lo,a
    call rd
    mov ti_hi,a
    call calc_te
    jmp ev_save
ev_ls:
    mov x,cx
    mov a,!ch_depth+x
    cmp a,#4
    bcs els_r
    mov t0,a
    mov a,x
    asl a
    asl a
    clrc
    adc a,t0
    mov y,a
    mov a,ptr
    mov !ls_lo+y,a
    mov a,ptr+1
    mov !ls_hi+y,a
    mov a,#0
    mov !ls_cnt+y,a
    mov a,!ch_depth+x
    inc a
    mov !ch_depth+x,a
els_r:
    jmp ev_save
ev_le:
    call rd
    mov t1,a
    mov x,cx
    mov a,!ch_depth+x
    beq ele_r
    dec a
    mov t0,a
    mov a,x
    asl a
    asl a
    clrc
    adc a,t0
    mov y,a
    mov a,!ls_cnt+y
    inc a
    mov !ls_cnt+y,a
    cmp a,t1
    bcs ele_done
    mov a,!ls_lo+y
    mov ptr,a
    mov a,!ls_hi+y
    mov ptr+1,a
ele_r:
    jmp ev_save
ele_done:
    mov a,t0
    mov !ch_depth+x,a
    jmp ev_save
ev_lp:
    mov x,cx
    mov a,ptr
    mov !ch_lpl+x,a
    mov a,ptr+1
    mov !ch_lph+x,a
    jmp ev_save
ev_end:
    mov x,cx
    mov a,sloop
    beq eend_stop
    mov a,#0
    mov !ch_depth+x,a
    mov a,!ch_lph+x
    beq eend_start
    mov ptr+1,a
    mov a,!ch_lpl+x
    mov ptr,a
    jmp ev_save
eend_start:
    mov a,!ch_sl+x
    mov ptr,a
    mov a,!ch_sh+x
    mov ptr+1,a
    jmp ev_save
eend_stop:
    mov a,#1
    mov !ch_done+x,a
    mov a,#0
    mov !ch_keyon+x,a
    jmp ev_save

; a = local instrument index, x = channel
set_inst:
    asl a
    mov y,a
    mov a,[itab]+y
    mov rp,a
    inc y
    mov a,[itab]+y
    mov rp+1,a
    mov a,rp
    mov !ch_irl+x,a
    mov a,rp+1
    mov !ch_irh+x,a
    mov y,#0
    mov a,[rp]+y
    and a,#$80
    mov !ch_iskit+x,a
    inc y
    mov a,[rp]+y
    mov !ch_gain+x,a
    ret

; =====================================================================================================
; per-frame output of channel cx (C# MusicPlayer.Output + Synth.ApplyMusic)
chan_output:
    mov x,cx
    mov a,!ch_iskit+x
    beq co_p
    mov a,!ch_trig+x
    beq co_r
    mov a,#0
    mov !ch_trig+x,a
    jmp kit_trigger
co_r:
    ret
co_p:
    mov a,!ch_note+x
    cmp a,#$FF
    bne co_hn
    mov a,#0
    mov !ch_trig+x,a
    ret
co_hn:
    mov gate,#0
    mov a,!ch_keyon+x
    beq co_g0
    call due_gate
    bcs co_g0
    mov gate,#1
co_g0:
    mov x,cx
    ; ---- portamento glide (1/256 semitone)
    mov a,!ch_porta+x
    bne co_gl
    mov a,#0
    mov !ch_gll+x,a
    mov a,!ch_note+x
    mov !ch_glh+x,a
    jmp co_gd
co_gl:
    mov y,a
    mov a,!portat+y
    mov t6,a
    mov a,#0
    setc
    sbc a,!ch_gll+x
    mov t0,a
    mov a,!ch_note+x
    sbc a,!ch_glh+x
    mov t1,a
    mov t7,#0
    bpl co_pos
    mov t7,#1
    mov a,#0
    setc
    sbc a,t0
    mov t0,a
    mov a,#0
    sbc a,t1
    mov t1,a
co_pos:
    mov a,t0
    or a,t1
    beq co_gd
    mov a,t0
    mov y,t6
    mul ya
    mov t2,y
    mov a,t1
    mov y,t6
    mul ya
    clrc
    adc a,t2
    mov t2,a
    mov a,y
    adc a,#0
    mov t3,a
    mov a,t2
    or a,t3
    bne co_s1
    mov t2,#1
co_s1:
    mov a,t7
    beq co_add
    mov a,!ch_gll+x
    setc
    sbc a,t2
    mov !ch_gll+x,a
    mov a,!ch_glh+x
    sbc a,t3
    mov !ch_glh+x,a
    bra co_gd
co_add:
    mov a,!ch_gll+x
    clrc
    adc a,t2
    mov !ch_gll+x,a
    mov a,!ch_glh+x
    adc a,t3
    mov !ch_glh+x,a
co_gd:
    ; ---- n = glide + detune
    mov a,!ch_det+x
    mov t0,a
    mov t1,#0
    bpl co_dp
    mov t1,#$FF
co_dp:
    mov a,!ch_gll+x
    clrc
    adc a,t0
    mov n_lo,a
    mov a,!ch_glh+x
    adc a,t1
    mov n_hi,a
    ; ---- arpeggio: + arp[(frame/2) % len] semitones
    mov a,!ch_arp+x
    beq co_na
    mov y,a
    mov a,!arpoff+y
    clrc
    adc a,!ch_arpp+x
    mov y,a
    mov a,!arpdat+y
    clrc
    adc a,n_hi
    mov n_hi,a
    mov a,!ch_arps+x
    eor a,#1
    mov !ch_arps+x,a
    bne co_na
    mov a,!ch_arp+x
    mov y,a
    mov a,!ch_arpp+x
    inc a
    cmp a,!arplen+y
    bcc co_ap
    mov a,#0
co_ap:
    mov !ch_arpp+x,a
co_na:
    ; ---- vibrato: 5.5 Hz after a delay, depth ramps in over 12 frames
    mov a,!ch_vib+x
    beq co_nv
    mov y,a
    mov a,!ch_nframe+x
    setc
    sbc a,!vibdel+y
    beq co_nv
    bcc co_nv
    mov t2,a
    mov a,!ch_vpl+x
    clrc
    adc a,#<6007
    mov !ch_vpl+x,a
    mov a,!ch_vph+x
    adc a,#>6007
    mov !ch_vph+x,a
    mov y,a
    mov a,!sintab+y
    mov t7,a
    bpl co_vp
    eor a,#$FF
    inc a
co_vp:
    mov t3,a
    mov a,!ch_vib+x
    mov y,a
    mov a,!vibdep+y
    mov y,t3
    mul ya
    asl a
    mov a,y
    rol a
    mov t4,a
    mov a,t2
    cmp a,#12
    bcs co_vf
    mov y,a
    mov a,!rampt+y
    mov y,t4
    mul ya
    mov t4,y
co_vf:
    mov a,t7
    bmi co_vneg
    mov a,n_lo
    clrc
    adc a,t4
    mov n_lo,a
    mov a,n_hi
    adc a,#0
    mov n_hi,a
    bra co_nv
co_vneg:
    mov a,n_lo
    setc
    sbc a,t4
    mov n_lo,a
    mov a,n_hi
    sbc a,#0
    mov n_hi,a
co_nv:
    mov a,!ch_nframe+x
    inc a
    beq co_nf
    mov !ch_nframe+x,a
co_nf:
    mov a,!ch_trig+x
    beq co_upd
    mov a,#0
    mov !ch_trig+x,a
    jmp p_trigger
co_upd:
    jmp p_update

; ---- pitched note-on (root + up to 2 chord notes on borrowed voices)
p_trigger:
    call release_subs
    mov x,cx
    mov nch,#1
    mov a,!ch_c2+x
    cmp a,#$80
    beq pt_n
    mov nch,#2
    mov a,!ch_c3+x
    cmp a,#$80
    beq pt_n
    mov nch,#3
pt_n:
    mov a,nch
    mov !ch_nch+x,a
    mov kk,#0
pt_l:
    mov a,kk
    bne pt_sub
    call home_voice
    bra pt_v
pt_sub:
    call acquire
pt_v:
    mov a,cv
    cmp a,#$FF
    beq pt_nx
    call pu_keyon
pt_nx:
    inc kk
    mov a,kk
    cmp a,nch
    bcc pt_l
    ret

; key on sub-note kk of channel cx on voice cv
pu_keyon:
    mov x,cv
    mov a,!v_lock+x
    cmp a,#2
    beq pk_r
    mov a,#0
    mov !v_lock+x,a
    call note_for_kk
    mov x,cx
    call pick_zone
    mov y,smp
    mov a,tn_lo
    clrc
    adc a,!s_tlo+y
    mov u_lo,a
    mov a,tn_hi
    adc a,!s_thi+y
    mov u_hi,a
    call lin_pan_kk
    call calc_vol
    mov x,cx
    mov a,!ch_echo+x
    mov e_flag,a
    mov nzf,#0
    jmp keyon_voice
pk_r:
    ret

; ---- per-frame update of a held pitched note (pitch, volume, echo; release when the gate closes)
p_update:
    mov a,!ch_nch+x
    mov nch,a
    mov kk,#0
pu_l:
    mov x,cx
    mov a,kk
    bne pu_sub
    mov a,x
    mov y,a
    mov a,!v_borrow+y
    cmp a,#$FF
    bne pu_nj
    mov a,!v_lock+y
    beq pu_go
    cmp a,#3
    bne pu_nj
    mov a,gate
    beq pu_nj
    mov a,y
    xcn a
    or a,#ENVX_R
    mov DSPADDR,a
    mov a,DSPDATA
    cmp a,#8
    bcs pu_nj
    mov cv,y
    call pu_keyon
pu_nj:
    jmp pu_n
pu_sub:
    cmp a,#1
    bne pu_s2
    mov a,!ch_sub1+x
    bra pu_sc
pu_s2:
    mov a,!ch_sub2+x
pu_sc:
    cmp a,#$FF
    beq pu_n
    mov y,a
    mov a,!v_borrow+y
    cmp a,cx
    bne pu_n
    mov a,!v_lock+y
    bne pu_n
pu_go:
    mov cv,y
    mov a,!v_keyed+y
    beq pu_n
    mov a,gate
    bne pu_on
    mov x,cv
    call vrelease
    bra pu_n
pu_on:
    call note_for_kk
    mov x,cv
    mov a,!v_samp+x
    mov y,a
    mov a,tn_lo
    clrc
    adc a,!s_tlo+y
    mov u_lo,a
    mov a,tn_hi
    adc a,!s_thi+y
    mov u_hi,a
    call set_pitch_u
    call lin_pan_kk
    call calc_vol
    mov x,cv
    call write_vol
    mov x,cx
    mov a,!ch_echo+x
    mov x,cv
    call set_eon
pu_n:
    inc kk
    mov a,kk
    cmp a,nch
    bcs pu_x
    jmp pu_l
pu_x:
    ret

; tn = n + chord offset of sub-note kk
note_for_kk:
    mov tn_lo,n_lo
    mov tn_hi,n_hi
    mov a,kk
    beq nk_r
    mov x,cx
    cmp a,#1
    bne nk_3
    mov a,!ch_c2+x
    bra nk_a
nk_3:
    mov a,!ch_c3+x
nk_a:
    clrc
    adc a,tn_hi
    mov tn_hi,a
nk_r:
    ret

; lin = volc[vol] * gain (x0.8 in chords); pan = channel pan (+ chord spread)
lin_pan_kk:
    mov x,cx
    mov a,!ch_vol+x
    mov y,a
    mov a,!volc+y
    mov y,a
    mov a,!ch_gain+x
    mul ya
    mov lin,y
    mov a,!ch_pan+x
    mov pan,a
    mov a,nch
    cmp a,#2
    bcc lpk_r
    mov a,lin
    mov y,#205
    mul ya
    mov lin,y
    mov a,kk
    asl a
    clrc
    adc a,kk
    setc
    sbc a,#3
    clrc
    adc a,pan
    call clamp_pan
lpk_r:
    ret

clamp_pan:
    mov pan,a
    bmi cpn_0
    cmp a,#41
    bcc cpn_r
    mov pan,#40
cpn_r:
    ret
cpn_0:
    mov pan,#0
    ret

; x = channel; tn = note -> smp (instrument key zone)
pick_zone:
    mov a,!ch_irl+x
    mov rp,a
    mov a,!ch_irh+x
    mov rp+1,a
    mov y,#2
    mov a,[rp]+y
    mov t6,a
    inc y
pz_l:
    dec t6
    beq pz_last
    mov a,[rp]+y
    mov smp,a
    inc y
    mov a,[rp]+y
    mov t4,a
    inc y
    mov a,[rp]+y
    mov t5,a
    inc y
    mov a,tn_hi
    cmp a,t5
    bne pz_c
    mov a,tn_lo
    cmp a,t4
pz_c:
    bcc pz_sel
    bra pz_l
pz_last:
    mov a,[rp]+y
    mov smp,a
pz_sel:
    ret

; ---- drum hit: kit group 0 on the channel's own voice, hats/cymbals on a borrowed idle voice if one is free
kit_trigger:
    mov a,!ch_piece+x
    mov t5,a
    mov y,a
    mov a,#1
    mov !ch_nch+x,a
    mov a,!kp_grp+y
    mov t4,a
    beq kt_home
    ; hats/cymbals: the channel voice if it already rings this group or is silent, else a borrowed voice
    mov a,!ch_hgrp+x
    cmp a,t4
    beq kt_home
    mov a,x
    xcn a
    or a,#ENVX_R
    mov DSPADDR,a
    mov a,DSPDATA
    beq kt_home
    mov kk,t4
    call acquire
    mov a,cv
    cmp a,#$FF
    bne kt_v
kt_home:
    call home_voice
    mov x,cx
    mov a,t4
    mov !ch_hgrp+x,a
kt_v:
    mov x,cv
    mov a,!v_lock+x
    cmp a,#2
    beq kt_r
    mov a,#0
    mov !v_lock+x,a
    mov y,t5
    mov a,!kp_samp+y
    mov smp,a
    mov a,!kp_ulo+y
    mov u_lo,a
    mov a,!kp_uhi+y
    mov u_hi,a
    mov x,cx
    mov a,!ch_vol+x
    mov y,a
    mov a,!volc+y
    mov t6,a
    mov y,t5
    mov a,!kp_vol+y
    mov y,t6
    mul ya
    mov lin,y
    mov y,t5
    mov a,!kp_pan+y
    clrc
    adc a,!ch_pan+x
    call clamp_pan
    call calc_vol
    mov x,cx
    mov a,!ch_echo+x
    mov e_flag,a
    mov nzf,#0
    jmp keyon_voice
kt_r:
    ret

; cv = cx, taking the voice back from any borrower
home_voice:
    mov a,cx
    mov cv,a
    mov x,a
    mov a,!v_borrow+x
    cmp a,#$FF
    beq hv_r
    mov y,a
    mov a,#$FF
    mov !v_borrow+x,a
    mov a,!ch_sub1+y
    cmp a,cv
    bne hv_2
    mov a,#$FF
    mov !ch_sub1+y,a
hv_2:
    mov a,!ch_sub2+y
    cmp a,cv
    bne hv_r
    mov a,#$FF
    mov !ch_sub2+y,a
hv_r:
    ret

; borrow a voice for sub-note kk of channel cx -> cv ($FF none)
acquire:
    mov x,cx
    mov a,kk
    cmp a,#1
    bne aq_2
    mov a,!ch_sub1+x
    bra aq_c
aq_2:
    mov a,!ch_sub2+x
aq_c:
    cmp a,#$FF
    beq aq_find
    mov y,a
    mov a,!v_borrow+y
    cmp a,cx
    bne aq_find
    mov a,!v_lock+y
    cmp a,#2
    beq aq_find
    mov cv,y
    ret
aq_find:
    ; candidates: not SFX, not borrowed, home channel finished / its note released / silent; quietest wins
    mov cv,#$FF
    mov t7,#$FF
    mov y,#7
aq_l:
    cmp y,cx
    beq aq_n
    mov a,!v_lock+y
    cmp a,#2
    beq aq_n
    mov a,!v_borrow+y
    cmp a,#$FF
    bne aq_n
    mov a,y
    xcn a
    or a,#ENVX_R
    mov DSPADDR,a
    mov a,DSPDATA
    mov t6,a
    mov a,!ch_done+y
    bne aq_c1
    mov a,!v_keyed+y
    beq aq_c1
    mov a,t6
    bne aq_n
aq_c1:
    mov a,t6
    cmp a,t7
    bcs aq_n
    mov t7,a
    mov cv,y
aq_n:
    dec y
    bpl aq_l
    mov a,cv
    cmp a,#$FF
    bne aq_ok2
    ret
aq_ok2:
    mov y,a
    mov a,cx
    mov !v_borrow+y,a
    mov x,cx
    mov a,kk
    cmp a,#1
    bne aq_s2
    mov a,cv
    mov !ch_sub1+x,a
    ret
aq_s2:
    mov a,cv
    mov !ch_sub2+x,a
    ret

release_subs:
    mov x,cx
    mov a,!ch_sub1+x
    call rs_one
    mov x,cx
    mov a,!ch_sub2+x
    call rs_one
    mov x,cx
    ret
rs_one:
    cmp a,#$FF
    beq rs_r
    mov x,a
    mov a,!v_borrow+x
    cmp a,cx
    bne rs_r
    mov a,!v_lock+x
    bne rs_r
    jmp vrelease
rs_r:
    ret

; =====================================================================================================
; voice primitives (x = voice)
vrelease:
    mov a,!v_keyed+x
    beq vr_r
    mov a,#0
    mov !v_keyed+x,a
    mov a,!v_samp+x
    mov y,a
    mov a,x
    xcn a
    or a,#GAIN_R
    mov DSPADDR,a
    mov a,!s_rel+y
    mov DSPDATA,a
    mov a,x
    xcn a
    or a,#ADSR1_R
    mov DSPADDR,a
    mov a,!s_adsr1+y
    and a,#$7F
    mov DSPDATA,a
vr_r:
    ret

; key on voice cv: sample smp, pitch u, volume vl/vr, echo e_flag, noise nzf
keyon_voice:
    mov x,cv
    mov a,x
    xcn a
    or a,#SRCN_R
    mov DSPADDR,a
    mov DSPDATA,smp
    mov y,smp
    mov a,x
    xcn a
    or a,#ADSR1_R
    mov DSPADDR,a
    mov a,!s_adsr1+y
    or a,#$80
    mov DSPDATA,a
    mov a,x
    xcn a
    or a,#ADSR2_R
    mov DSPADDR,a
    mov a,!s_adsr2+y
    mov DSPDATA,a
    mov a,smp
    mov !v_samp+x,a
    mov a,#1
    mov !v_keyed+x,a
    mov a,#$FF
    mov !v_cph+x,a
    mov !v_cvl+x,a
    mov !v_cvr+x,a
    call set_pitch_u
    mov x,cv
    call write_vol
    mov a,kon
    or a,!bits+x
    mov kon,a
    mov a,e_flag
    call set_eon
    mov a,nzf
    beq kv_nn
    mov a,non
    or a,!bits+x
    mov non,a
    mov a,u_hi
    and a,#$1F
    mov t0,a
    mov a,flg
    and a,#$E0
    or a,t0
    mov flg,a
    ret
kv_nn:
    mov a,!bits+x
    eor a,#$FF
    and a,non
    mov non,a
    ret

; x = voice, a = echo on (Z flag from a)
set_eon:
    cmp a,#0
    beq se_0
    mov a,eon
    or a,!bits+x
    mov eon,a
    ret
se_0:
    mov a,!bits+x
    eor a,#$FF
    and a,eon
    mov eon,a
    ret

; u -> pitch register (cached), x = voice (reloaded from cv)
set_pitch_u:
    call calc_pitch
    mov x,cv
    mov a,p_hi
    cmp a,!v_cph+x
    bne spu_w
    mov a,p_lo
    cmp a,!v_cpl+x
    beq spu_r
spu_w:
    mov a,p_lo
    mov !v_cpl+x,a
    mov a,p_hi
    mov !v_cph+x,a
    mov a,x
    xcn a
    or a,#PITCHL_R
    mov DSPADDR,a
    mov DSPDATA,p_lo
    inc a
    mov DSPADDR,a
    mov DSPDATA,p_hi
spu_r:
    ret

; x = voice: write vl/vr if changed
write_vol:
    mov a,vl
    cmp a,!v_cvl+x
    beq wv_1
    mov !v_cvl+x,a
    mov a,x
    xcn a
    mov DSPADDR,a
    mov DSPDATA,vl
wv_1:
    mov a,vr
    cmp a,!v_cvr+x
    beq wv_2
    mov !v_cvr+x,a
    mov a,x
    xcn a
    or a,#1
    mov DSPADDR,a
    mov DSPDATA,vr
wv_2:
    ret

; music volume: lin (0-255) x pan table (x fade)
calc_vol:
    mov y,pan
    mov a,!panl+y
    mov y,lin
    mul ya
    mov vl,y
    mov y,pan
    mov a,!panr+y
    mov y,lin
    mul ya
    mov vr,y
    mov a,fade8
    cmp a,#255
    beq cvo_r
    mov y,vl
    mul ya
    mov vl,y
    mov a,fade8
    mov y,vr
    mul ya
    mov vr,y
cvo_r:
    ret

; P = 2^(u/12), u in 1/256 semitones: 1/16-semitone table for the top octave + linear interpolation, then shift
calc_pitch:
    mov a,u_hi
    cmp a,#$A8
    bcs cp_max
    mov t0,u_lo
    mov t1,u_hi
    lsr t1
    ror t0
    lsr t1
    ror t0
    lsr t1
    ror t0
    lsr t1
    ror t0
    movw ya,t0
    mov x,#192
    div ya,x
    cmp a,#14
    bcs cp_max
    mov t2,a
    mov t3,y
    mov a,u_lo
    and a,#15
    mov y,a
    mov x,t3
    mov a,!ptab_d+x
    mul ya
    mov t4,a
    mov t5,y
    lsr t5
    ror t4
    lsr t5
    ror t4
    lsr t5
    ror t4
    lsr t5
    ror t4
    mov a,!ptab_lo+x
    clrc
    adc a,t4
    mov p_lo,a
    mov a,!ptab_hi+x
    adc a,#0
    mov p_hi,a
    mov a,#13
    setc
    sbc a,t2
    beq cp_r
    mov x,a
cp_sh:
    lsr p_hi
    ror p_lo
    dec x
    bne cp_sh
cp_r:
    ret
cp_max:
    mov p_lo,#$FF
    mov p_hi,#$3F
    ret

; =====================================================================================================
; sound effects: 2 slots, each on one voice (sfxv0/sfxv1 from the song header)
sfx_request:
    and a,#$7F
    beq sr_r
    cmp a,#NSFX+1
    bcs sr_r
    mov sid,a
    mov y,a
    mov a,!sfx_lo+y
    mov rp,a
    mov a,!sfx_hi+y
    mov rp+1,a
    mov y,#0
    mov a,[rp]+y
    beq sr_r
    mov t7,a
    mov kk,#0
sr_part:
    mov a,kk
    asl a
    asl a
    inc a
    mov y,a
    mov a,[rp]+y
    mov t4,a
    inc y
    mov a,[rp]+y
    mov t5,a
    inc y
    mov a,[rp]+y
    mov t2,a
    inc y
    mov a,[rp]+y
    mov t3,a
    call sfx_choose
    cmp x,#$FF
    beq sr_nx
    call sfx_take
sr_nx:
    inc kk
    mov a,kk
    cmp a,t7
    bcc sr_part
sr_r:
    ret

; -> x = slot for a part (prio t4, C# slot t5, part index kk) or $FF
sfx_choose:
    mov x,#0
sc_a:
    mov a,!sf_act+x
    beq sc_an
    mov a,!sf_cs+x
    cmp a,t5
    bne sc_an
    mov a,t4
    cmp a,!sf_prio+x
    bcs sc_ret
    mov x,#$FF
    ret
sc_an:
    inc x
    cmp x,#2
    bne sc_a
    mov x,#0
sc_b:
    mov a,!sf_act+x
    beq sc_ret
    inc x
    cmp x,#2
    bne sc_b
    mov a,kk
    bne sc_no
    mov x,#0
    mov a,!sf_prio
    cmp a,!sf_prio+1
    bcc sc_c
    beq sc_c
    mov x,#1
sc_c:
    mov a,t4
    cmp a,!sf_prio+x
    bcs sc_ret
sc_no:
    mov x,#$FF
sc_ret:
    ret

; start part (program t2:t3, prio t4, C# slot t5) of sfx sid in slot x
sfx_take:
    mov sx,x
    mov a,!sf_act+x
    beq stk_new
    mov a,!sf_v+x
    mov x,a
    call vrelease
    mov a,#3
    mov !v_lock+x,a
    mov x,sx
stk_new:
    mov a,#1
    mov !sf_act+x,a
    mov a,sid
    mov !sf_id+x,a
    mov a,t4
    mov !sf_prio+x,a
    mov a,t5
    mov !sf_cs+x,a
    mov a,t2
    mov !sf_pl+x,a
    mov !sf_sl+x,a
    mov a,t3
    mov !sf_ph+x,a
    mov !sf_sh+x,a
    mov a,#0
    mov !sf_wait+x,a
    mov !sf_rmp+x,a
    mov a,x
    bne stk_v1
    mov a,sfxv0
    bra stk_v
stk_v1:
    mov a,sfxv1
stk_v:
    mov !sf_v+x,a
    mov y,a
    mov a,#2
    mov !v_lock+y,a
    mov a,!v_borrow+y
    cmp a,#$FF
    beq stk_r
    mov x,a
    mov a,#$FF
    mov !v_borrow+y,a
    mov a,!ch_sub1+x
    mov t6,y
    cmp a,t6
    bne stk_2
    mov a,#$FF
    mov !ch_sub1+x,a
stk_2:
    mov a,!ch_sub2+x
    cmp a,t6
    bne stk_r
    mov a,#$FF
    mov !ch_sub2+x,a
stk_r:
    mov x,sx
    ret

sfx_end:
    mov a,#0
    mov !sf_act+x,a
    mov a,!sf_v+x
    mov x,a
    call vrelease
    mov a,#3
    mov !v_lock+x,a
    ret

sfx_stop_all:
    mov x,#0
ssa_l:
    mov a,!sf_act+x
    beq ssa_n
    mov t6,x
    call sfx_end
    mov x,t6
ssa_n:
    inc x
    cmp x,#2
    bne ssa_l
    ret

sfx_tick:
    mov x,#0
sx_l:
    mov sx,x
    mov a,!sf_act+x
    beq sx_n
    call sfx_step
sx_n:
    mov x,sx
    inc x
    cmp x,#2
    bne sx_l
    ret

sfx_step:
    mov a,!sf_wait+x
    beq sx_fetch
    dec a
    mov !sf_wait+x,a
    mov a,!sf_rmp+x
    beq sx_r
    jmp sfx_ramp_step
sx_r:
    ret
sx_fetch:
    mov a,!sf_pl+x
    mov ptr,a
    mov a,!sf_ph+x
    mov ptr+1,a
    mov guard,#0
sx_op:
    inc guard
    beq sx_endj
    call rd
    cmp a,#0
    beq sx_endj
    cmp a,#1
    beq sx_wait
    cmp a,#2
    beq sx_kon
    cmp a,#3
    beq sx_rampj
    cmp a,#4
    beq sx_rel
    mov x,sx
    mov a,!sf_sl+x
    mov ptr,a
    mov a,!sf_sh+x
    mov ptr+1,a
    bra sx_op
sx_endj:
    mov x,sx
    jmp sfx_end
sx_rampj:
    jmp sx_ramp
sx_rel:
    mov x,sx
    mov a,!sf_v+x
    mov x,a
    call vrelease
    bra sx_op
sx_wait:
    call rd
    mov x,sx
    dec a
    mov !sf_wait+x,a
    mov a,#0
    mov !sf_rmp+x,a
    jmp sx_save
sx_kon:
    call rd
    mov x,sx
    mov !sf_s+x,a
    call rd
    mov x,sx
    mov !sf_ul+x,a
    call rd
    mov x,sx
    mov !sf_uh+x,a
    call rd
    mov x,sx
    mov !sf_vi+x,a
    call rd
    mov x,sx
    mov !sf_pi+x,a
    call rd
    mov x,sx
    mov !sf_ech+x,a
    mov a,#0
    mov !sf_vf+x,a
    mov !sf_pf+x,a
    mov !sf_wait+x,a
    mov !sf_rmp+x,a
    call sx_save
    call sfx_calc
    mov x,sx
    mov a,!sf_v+x
    mov cv,a
    jmp keyon_voice
sx_ramp:
    call rd
    mov x,sx
    dec a
    mov !sf_wait+x,a
    call rd
    mov x,sx
    mov !sf_dul+x,a
    call rd
    mov x,sx
    mov !sf_duh+x,a
    call rd
    mov x,sx
    mov !sf_dvl+x,a
    call rd
    mov x,sx
    mov !sf_dvh+x,a
    call rd
    mov x,sx
    mov !sf_dpl+x,a
    call rd
    mov x,sx
    mov !sf_dph+x,a
    mov a,#1
    mov !sf_rmp+x,a
    call sx_save
    jmp sfx_ramp_step
sx_save:
    mov x,sx
    mov a,ptr
    mov !sf_pl+x,a
    mov a,ptr+1
    mov !sf_ph+x,a
    ret

sfx_ramp_step:
    mov x,sx
    mov a,!sf_ul+x
    clrc
    adc a,!sf_dul+x
    mov !sf_ul+x,a
    mov a,!sf_uh+x
    adc a,!sf_duh+x
    mov !sf_uh+x,a
    ; volume 8.8, clamped 0..255
    mov a,!sf_vf+x
    clrc
    adc a,!sf_dvl+x
    mov !sf_vf+x,a
    mov a,!sf_vi+x
    adc a,!sf_dvh+x
    mov !sf_vi+x,a
    mov t0,a
    mov a,!sf_dvh+x
    bmi srs_vneg
    bcc srs_v
    mov a,#255
    mov !sf_vi+x,a
    bra srs_v
srs_vneg:
    bcs srs_v
    mov a,#0
    mov !sf_vi+x,a
    mov !sf_vf+x,a
srs_v:
    ; pan 8.8
    mov a,!sf_pf+x
    clrc
    adc a,!sf_dpl+x
    mov !sf_pf+x,a
    mov a,!sf_pi+x
    adc a,!sf_dph+x
    mov !sf_pi+x,a
    call sfx_calc
    mov x,sx
    mov a,!sf_v+x
    mov cv,a
    mov x,a
    mov a,nzf
    beq srs_pitch
    mov a,u_hi
    and a,#$1F
    mov t0,a
    mov a,flg
    and a,#$E0
    or a,t0
    mov flg,a
    bra srs_vol
srs_pitch:
    call set_pitch_u
srs_vol:
    mov x,cv
    jmp write_vol

; slot sx -> u, smp, vl, vr (vol8 x pan >> 7, max 127), e_flag, nzf
sfx_calc:
    mov x,sx
    mov a,!sf_ul+x
    mov u_lo,a
    mov a,!sf_uh+x
    mov u_hi,a
    mov a,!sf_s+x
    mov smp,a
    mov a,!sf_ech+x
    mov t6,a
    and a,#1
    mov e_flag,a
    mov a,t6
    and a,#2
    mov nzf,a
    mov a,!sf_pi+x
    call clamp_pan
    mov x,sx
    mov a,!sf_vi+x
    mov lin,a
    mov y,pan
    mov a,!panl+y
    mov y,lin
    mul ya
    call sh7c
    mov vl,a
    mov y,pan
    mov a,!panr+y
    mov y,lin
    mul ya
    call sh7c
    mov vr,a
    ret

sh7c:
    cmp y,#$40
    bcs s7_max
    asl a
    mov a,y
    rol a
    ret
s7_max:
    mov a,#127
    ret

; =====================================================================================================
bits:
    .db 1,2,4,8,16,32,64,128
dspinit:
    .db FLG_R,$E0, KOFF_R,$FF, MVOLL_R,0, MVOLR_R,0, EVOLL_R,0, EVOLR_R,0, PMON_R,0, NON_R,0, EON_R,0
    .db DIR_R,>dir, ESA_R,$FF, EDL_R,0, EFB_R,0
    .db $0F,FIR0, $1F,FIR1, $2F,FIR2, $3F,FIR3, $4F,FIR4, $5F,FIR5, $6F,FIR6, $7F,FIR7
    .db $00,0, $01,0, $10,0, $11,0, $20,0, $21,0, $30,0, $31,0, $40,0, $41,0, $50,0, $51,0, $60,0, $61,0, $70,0, $71,0
    .db KOFF_R,0, FLG_R,$20, MVOLL_R,MVOL, MVOLR_R,MVOL
    .db $FF
code_end:
