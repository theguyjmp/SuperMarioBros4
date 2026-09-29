# Level format (data/levels/*.lvl)

File name = level id: `1-1.lvl`, `1-2.lvl`, `1-f.lvl` (fortress), `1-a.lvl` (airship), `8-c.lvl` (Bowser's castle),
`2-f2.lvl` (second fortress). Plain text, one character per 16x16 tile.

```
title = GRASSLAND GALLOP        # optional display name
kind  = level                   # level | fortress | airship | castle
time  = 300                     # 200 / 300 / 400, or 0 = no timer
start = 3 12                    # tile x y where Mario's FEET are (the row just above the ground); optional 3rd number = area
area 0 theme=plains music=overworld          # options: theme= music= scroll= speed= water= bg= decor=
<grid rows ... every row the same width is best (shorter rows are padded with '.')>
end
area 1 theme=underground music=underground scroll=lock
<grid>
end
link 0:1 -> 1:1                 # entering marker 1 in area 0 exits from marker 1 in area 1
link 1:2 -> 0:2
```

## Coordinates, screen, camera
* x = column from the left (0-based), y = row from the top (0-based). Screen shows 16 x 12 tiles.
* `scroll=normal` (default): the camera shows the BOTTOM 12 rows and only scrolls up while the player is flying
  (raccoon P-flight / P-jump) or climbing a vine — exactly like SMB3. Use 14-15 rows for most levels, 20-27 rows
  when there is a sky route for flying players. Nothing mandatory above the bottom 12 rows.
* `scroll=free` follows vertically too · `scroll=lock` never scrolls vertically (bonus rooms, boss rooms) ·
  `scroll=vertical` for tall 16-wide climbing levels · `scroll=auto speed=6` autoscrolls right (speed in 1/16 px per
  tick; 6-10 is typical; the screen pushes the player and crushes against walls — leave room!).
* `water=ROW`: rows >= ROW are water (swimming); `water=0` = whole area underwater.
* Areas can be smaller than the screen (bonus rooms, e.g. 16 x 12).

## Legend
Terrain: `.` empty · `#` ground (auto-tiled per theme) · `%` hard block · `=` thin semisolid bridge (stand on top, jump
through from below) · `&` cloud platform (semisolid) · `A` `C` `E` `J` big SMB3 bolted blocks in 4 colors (semisolid:
land on top, pass through from below/sides; draw them as filled rectangles, overlapping/stacking looks great) ·
`X` invisible wall · `~` lava (instant death) · `^` spikes (hurt) · `M` muncher (hurts, P-switch turns it into a coin) ·
`|` climbable vine · `/` 45-degree slope rising to the right · `\` 45-degree slope falling to the right (put `#` fill
under slopes; a hill looks like `../##\..` on its top row, widening by one on each row below; slopes are SMB3's: walk up
them, and press DOWN on a slope to SLIDE and knock enemies away) · `(` conveyor belt moving left · `)` conveyor moving
right (solid; carries the player and enemies; P-switches stop them). Areas containing slopes use SMB3's sloped collision
set (the player needs his center over ground to stand on a ledge).

Blocks: `B` brick · `?` coin block · `F` flower block (mushroom when small) · `L` leaf block (mushroom when small) ·
`*` 1-UP block · `S` star brick · `$` multi-coin brick (up to 10 coins) · `!` P-switch brick · `V` vine brick (vine grows
to the top) · `Q` brick with flower · `O` brick with leaf · `h` hidden 1-UP block · `H` hidden coin block · `N` note block
(bouncy; press A while bouncing for a super bounce) · `W` wood block · `I` ice block (slippery) · `U` used block ·
`o` coin.

Pipes: vertical pipes are 2 columns: `[` left and `]` right, as tall as you like; the lip is drawn automatically on the
open end (top, or bottom for ceiling pipes). Horizontal pipes are 2 rows: `{` top row and `}` bottom row, as long as you
like; the mouth is drawn on the open end. Doors: `D` in two stacked cells.
Links: put a digit `0`-`9` ON the pipe mouth cell (the top-left `[` of an up-facing pipe, the bottom-left of a ceiling
pipe, the top `{` cell of a horizontal pipe mouth) or on the lower `D` of a door, then add `link` lines. A digit on an
empty cell is an arrival point (player drops in there). Marker `9` in an area is used when the player climbs a vine off
the top of that area (`link 0:9 -> 2:1` takes them to a coin heaven). Up-facing pipes are entered with DOWN, ceiling
pipes with UP, horizontal pipes by walking into the mouth, doors with UP.

Goal: `G` = top-left of the 2x2 roulette box. Put it 2-4 tiles above the ground near the end; everything from 2
columns left of G is drawn as the dark "backstage" area. Leave >= 14 columns of flat ground after G (the player walks
off to the right). Fortresses/airships/castles have no G: beating the boss ends them.

Objects (lowercase = enemies): `g` Goomba · `p` Paragoomba (hops) · `k` green Koopa (walks off ledges) · `r` red Koopa
(turns at ledges) · `j` green Paratroopa (bounces) · `f` red Paratroopa (flies up/down) · `n` red Paratroopa (flies
left/right) · `z` Buzzy Beetle (fireproof) · `s` Spiny (can't be stomped) · `e` Piranha Plant and `v` Venus Fire Trap
(place in the cell directly ABOVE the pipe's left column; they live in the pipe) · `c` Cheep Cheep (swims) · `l` leaping
Cheep Cheep (jumps out of the water from below the screen) · `q` Blooper · `u` Boo · `t` Thwomp (place at its resting
height; it slams down when Mario passes under) · `d` Dry Bones · `x` Podoboo (put in/above lava; jumps up to that
height) · `y` Hammer Bro · `w` Rocky Wrench (airship hatches) · `a` Bob-omb · `i` Lakitu · `b` Bill Blaster (stack `b`
cells vertically; the top one fires Bullet Bills) · `<` `>` airship cannon (fires left/right) · `R` Roto-disc (orbits
this cell) · `P` P-switch · `-` donut lift (falls when stood on) · `_` horizontal moving platform · `:` vertical moving
platform · `m` mushroom (free item) · `Z` Boom Boom (fortress boss) · `K` Koopaling (airship boss) · `Y` Bowser.

## Themes & music
Themes: plains, underground, desert, sea, jungle, sky, ice, machine, volcano, fortress, castle, airship, bonus.
Music: overworld, athletic, underground, underwater, desert, snow, jungle, fortress, airship, castle, boss, bowser,
toadhouse, bonus.

## Physics ruler (from docs/01 §15.1) — design within the COMFORTABLE numbers
| | max gap | comfortable gap | max height | comfortable height |
|---|---|---|---|---|
| standing | - | - | 4 tiles | 3 tiles |
| walking jump | 5 | 3 | 4 | 3 |
| running jump (B) | 10 | 6 | 5 | 4 |
| P-speed jump | 15 | 10 (optional routes only) | 6 | 5 |
Stomp bounce holding A: ~6 tiles. Note block super bounce: ~5 tiles. P-meter needs ~16 tiles of flat runway.
Raccoon flight climbs ~22 tiles in 4.3 s.

## Design rules (SMB3 style)
* SMB3 only allows 5 enemies active at once — spread enemies out (never more than ~4 inside a 20-column window).
* Teach, then test: introduce an idea safely, develop it, twist it, then a short victory run to the goal.
* No blind jumps: coins or scenery point to landings. Put power-up blocks early.
* Every level: at least one secret (hidden block, coin cache, pipe to a bonus room, flight-only area).
* Typical sizes: normal level 150-220 columns; fortress 100-160; airship 120-180 (autoscroll) + a boss cabin area.
