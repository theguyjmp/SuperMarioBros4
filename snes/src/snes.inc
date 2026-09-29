; Shared SNES register names and conventions (include from every module).
; Calling convention for all public routines: enter/exit with A 8-bit, X/Y 16-bit (sep #$20 / rep #$10),
; DB = $80 (the bank of CODE) unless noted, D = $0000. Routines may clobber A/X/Y unless documented.
; Long-callable module entry points use JSL/RTL (modules live in their own banks).

INIDISP = $2100
OBSEL   = $2101
OAMADDL = $2102
OAMDATA = $2104
BGMODE  = $2105
MOSAIC  = $2106
BG1SC   = $2107
BG2SC   = $2108
BG3SC   = $2109
BG4SC   = $210A
BG12NBA = $210B
BG34NBA = $210C
BG1HOFS = $210D
BG1VOFS = $210E
BG2HOFS = $210F
BG2VOFS = $2110
BG3HOFS = $2111
BG3VOFS = $2112
VMAIN   = $2115
VMADDL  = $2116
VMDATAL = $2118
VMDATAH = $2119
CGADD   = $2121
CGDATA  = $2122
W12SEL  = $2123
W34SEL  = $2124
WOBJSEL = $2125
WH0     = $2126
WH1     = $2127
WBGLOG  = $212A
WOBJLOG = $212B
TM      = $212C
TS      = $212D
TMW     = $212E
TSW     = $212F
CGWSEL  = $2130
CGADSUB = $2131
COLDATA = $2132
SETINI  = $2133
APUIO0  = $2140
APUIO1  = $2141
APUIO2  = $2142
APUIO3  = $2143
WMDATA  = $2180
WMADDL  = $2181
NMITIMEN = $4200
WRMPYA  = $4202
WRMPYB  = $4203
WRDIVL  = $4204
WRDIVB  = $4206
HTIMEL  = $4207
VTIMEL  = $4209
MDMAEN  = $420B
HDMAEN  = $420C
MEMSEL  = $420D
RDNMI   = $4210
TIMEUP  = $4211
HVBJOY  = $4212
RDDIVL  = $4214
RDMPYL  = $4216
JOY1L   = $4218
JOY2L   = $421A
DMAP0   = $4300   ; + $10 * channel
BBAD0   = $4301
A1T0L   = $4302
A1B0    = $4304
DAS0L   = $4305
DASB0   = $4307

; Joypad bits (16-bit JOY1L layout)
PAD_B      = $8000
PAD_Y      = $4000
PAD_SELECT = $2000
PAD_START  = $1000
PAD_UP     = $0800
PAD_DOWN   = $0400
PAD_LEFT   = $0200
PAD_RIGHT  = $0100
PAD_A      = $0080
PAD_X      = $0040
PAD_L      = $0020
PAD_R      = $0010

.macro A8
    sep #$20
    .a8
.endmacro
.macro A16
    rep #$20
    .a16
.endmacro
.macro XY8
    sep #$10
    .i8
.endmacro
.macro XY16
    rep #$10
    .i16
.endmacro
.macro AXY16
    rep #$30
    .a16
    .i16
.endmacro
