# 06 — The 16-bit upgrade ("the next generation after SMB3")

Owner feedback 2026-09-28: movement feels great, but the game *looks and sounds like "SMB 1.5"*. Super Mario Bros. 4
is the generation after SMB3: it must look and sound like a **16-bit (SNES-class) game** — richer color, shading,
outlined characters, gradient skies, parallax backgrounds, sampled-instrument music with echo. Physics stays
exactly as is. Everything stays **original** (no traced Nintendo sprites, no transcribed Nintendo melodies,
no level layouts copied from SMB1/SMB3/SMW).

## Engine capabilities (already implemented)
* **Colors:** palettes may use any 24-bit color written `#RRGGBB`; 2-hex-digit NES indices still work
  (`pal NAME #F8D8A0 #C07840 0F ...`). Global table is unlimited; fades/grayscale work on RGB too.
* **15 colors per image:** pixel chars `1`-`9` and `a`-`f` = palette slots 1..15; `.`/`0` = transparent.
  `pal NAME c1 .. cN` takes 1..15 colors. If an image uses a slot beyond its palette's length, the last color is
  used (so always give a palette at least as many entries as the highest slot you draw with).
* **Sky gradients:** palette `THEME.grad` = colors top→bottom, drawn as even horizontal bands across the 192-px
  playfield (screen-fixed). 8-16 bands looks SNES-like.
* **Parallax layers:** images `para.THEME.1` (farthest) … `para.THEME.4` (nearest), palettes of the same name.
  Tiled horizontally (make the width a multiple of 16 and the left/right edges seamless), bottom-anchored to the
  playfield bottom at the area floor. Scroll factors: 1/8, 2/8, 3/8, 4/8 horizontally, half that vertically.
  Optional `pal para.THEME.N.y NN` (one NES-index-style hex number used as pixels) pushes a layer down NN px.
  Typical sizes: 256 wide, 64-160 tall. Layers are drawn behind decor and tiles, and never hide "behind" sprites.
* **Per-image decor palettes:** `pal decor.IMG ...` or `pal THEME.decor.IMG ...` override the theme's decor palette
  for that one decor image (so a bush and a hill can have different 15-color palettes).
* An area with an explicit `bg=` backdrop in its level file skips the gradient and parallax (bonus rooms etc.).
* `ppu.Back(img,x,y,pal)` = scenery draw that does not mark the foreground mask.

## Art direction (all art files)
* **Keep every image name and size** listed in `data/art/README.md` — hitboxes and anchors depend on them.
  All character sprites still face RIGHT.
* **Characters/enemies/items:** a 1-px **dark outline** around the silhouette (a very dark, hue-tinted color such
  as `#181010`, `#101828` — not pure NES black everywhere), then a **3-4 step shading ramp per material**
  (highlight, base, shade, deep shade), light from the top-left, a few specular highlight pixels (eyes, shells,
  metal). Readable on ANY backdrop: sky blue, black caves, dark castles, green jungle, white snow.
* **Tiles:** rich 4-6 tone shading, texture (soil speckles, rock strata, wood grain), rounded grass lips that
  overhang the dirt, bevelled blocks with highlights and shadows. Ground fill should not be a flat color.
* **Backgrounds:** every outdoor theme gets a gradient sky and 2-4 parallax layers (distant mountains/mesas/
  reefs/cloud banks/city of gears…); indoor themes get dark gradient + a faint far layer (cave stalactites, castle
  brickwork with windows, machinery silhouettes). Far layers are desaturated and lighter/closer to the sky color;
  near layers are more saturated. Keep background value contrast LOW so foreground sprites pop.
* Don't imitate SMW's specific designs (no round-eyed hills, no Yoshi, no copied tiles). The goal is *16-bit
  fidelity with this game's own identity* ("The Lantern Tour" — warm lantern light is a nice recurring motif).

## Theme color bible (shared between tiles, decor and backgrounds so everything matches)
| theme | sky gradient (top→bottom) | ground / materials |
|---|---|---|
| plains | deep azure `#3058D8` → pale `#B8E0F8` | grass `#38B838`/`#80E048`/`#187818`, soil warm tan-brown `#C88848` `#985828` `#603818` |
| underground | `#080810` → `#202038` | blue-gray rock `#5868A0` `#384070` `#202848`, cyan crystals |
| desert | `#E88838` (top, warm) → `#F8D898` | sand `#F8D080` `#D8A050` `#A87030`, sandstone red `#C05830` |
| sea | `#2070D0` → `#90D8F8` | pale sand + coral pink `#F87898`, water `#1850B0` `#3890E8` `#A0E0F8` |
| jungle | `#105838` → `#60B878` (hazy) | dark leaf `#105020` `#289830` `#70D040`, bark `#704020` |
| sky | `#5898F8` → `#F8E8F8` (pastel) | clouds white/lavender `#FFFFFF` `#D8D8F8` `#9898D8`, gold trims |
| ice | `#182868` → `#98B8F0` | snow `#F8F8FF` `#C8D8F8` `#8098D0`, ice `#A0E8F8` |
| machine | `#181818` → `#403830` | steel `#B8B8C8` `#787890` `#404050`, brass `#D8A840` |
| volcano | `#300808` → `#A83010` | basalt `#503838` `#302020`, lava `#F8D030` `#F87010` `#C02000` |
| fortress | `#101018` → `#282838` | stone `#A8A0A0` `#686070` `#383040` |
| castle | `#180808` → `#401818` | dark brick `#784040` `#502828`, gold `#E8B830` |
| airship | `#F08850` (sunset) → `#F8D8A0` | wood `#D89858` `#A06030` `#603018`, iron bolts `#606878` |
| bonus | `#200828` → `#401848` | purple/gold festive |

## Player colors (fixes: pants invisible on same-color backgrounds)
Mario: cap/shirt red ramp (`#F85858` `#D82020` `#901010`), **overalls royal blue ramp** (`#6890F8` `#3050D0`
`#182880`), skin ramp, dark-brown hair/mustache, brown shoes, yellow buttons, dark outline. Luigi: green shirt/cap,
**navy overalls**. Fire: white cap/shirt, red overalls. Every form must stay readable on sky, black, green, white
and red backgrounds (check with `smb4tools poses`).

## Music & sound direction
SNES-style: 8 voices, sampled-sounding instruments (generated in code — no sample files from any game),
ADSR envelopes, per-voice volume/pan, **echo/reverb**, bass + drum kit + lead + pads + counter-melodies.
Original compositions — melodic, catchy, but not transcriptions of any Nintendo tune.

## Level direction
No layout may be a recognizable copy of an SMB1/SMB2/SMB3/SMW level (e.g. SMB1 1-1's `?B?B?` row, pipe-height
progression and end staircase; SMB3 1-1's layout). Each level gets its own central idea, SMB3 physics rules and
the physics ruler in `data/levels/README.md`.
