# Art contract (data/art/*.art)

**16-bit upgrade (2026-09-28): read `docs/06-16bit-upgrade.md` first. It supersedes the NES rules below: palettes take up to 15 colors incl. `#RRGGBB`, pixels `1`-`9`,`a`-`f`, dark outlines on characters, gradients and parallax layers.**

All graphics are **original pixel art**. Never copy or
trace Nintendo sprites pixel-for-pixel; characters should be recognisable, not duplicates.

## File format
```
# comment
pal NAME c1 c2 c3          # NES palette indices (hex) for slots 1,2,3. Slot 0 is always transparent.
img NAME WxH [pal=NAME]    # followed by exactly H rows of exactly W chars
................
..111111........
```
Pixel chars: `.` (or `0`) = transparent, `1` `2` `3` = palette slots. `pal=` sets the preview palette
(the game chooses real palettes at draw time, e.g. Mario's art is reused for Fire Mario and Luigi).

Preview your work: `tools\smb4tools.exe sheet out.png [namePrefix]` renders a 3x-scaled PNG sheet
(checkerboard = transparent) and lists format errors. Open the PNG to review it.

## NES rules (enforced by style, keep them!)
* 3 colors + transparent per image (sprites); background tiles: 3 colors + backdrop (slot 0 shows the sky color).
* 16x16 metatiles on a 16 px grid. Sprites: 16 px wide typically; sizes listed below are exact.
* SMB3 look: **no blanket outline around characters.** SMB3 uses the darkest colour only where the character
  really is dark (Mario's hair, eye, mustache, overalls, shoes; an enemy's eyes, feet, shell rim) and for a few
  interior separation pixels; large red, skin or coloured areas sit directly on the background. Bold, readable
  silhouettes and clearly different animation poses. Background blocks have 2-tone shading, light top-left,
  dark bottom-right; big bolted blocks with a drop shadow.
* Check characters as the game draws them: `smb4tools poses FORM out.png` (player, all poses with overlays)
  and `smb4tools level _gallery out.png` (every enemy and boss).
* **All character sprites face RIGHT.** The engine mirrors them.
* NES master palette index reference: $0F black, $00 dark gray, $10 gray, $20/$30 white, $16 red, $06 dark red,
  $27 orange/skin, $36/$37 light peach, $17 brown, $07 dark brown, $28 yellow, $38 light yellow, $1A green,
  $0A dark green, $2A light green, $19 green, $11/$12 blue, $21 light blue, $31 pale blue, $02 dark blue, $14 purple.

## Palette naming (use these names; engine looks them up)
Characters: `mario` (1 black, 2 red, 3 skin), `luigi` (2 green), `fire` (1 red, 2 white, 3 skin), `tanooki`
(1 dark brown, 2 brown, 3 skin), `tail` (raccoon tail/ears: 1 black, 2 brown/orange, 3 light), `goomba`, `koopa.green`,
`koopa.red`, `buzzy`, `spiny`, `plant` (piranha: 1 green 2 red 3 white), `bill`, `cheep`, `blooper`, `boo`,
`thwomp`, `bones`, `podoboo`, `bro` (hammer bro), `boomboom`, `koopaling`, `bowser`, `bowser.hair`,
`mushroom` (1 black 2 red 3 white), `oneup` (2 green), `flower`, `leaf`, `star`, `coin`, `pswitch`, `fireball`,
`hammer`, `fx` (effects), `hud`, `card`.
Tiles: `THEME.GROUP` where THEME ∈ {plains, underground, desert, sea, jungle, sky, ice, machine, volcano,
fortress, castle, airship, bonus} and GROUP ∈ {ground, brick, qblock, used, hard, wood, note, pipe, semi, cloud,
block1, block2, block3, block4, decor, water, lava, spike, muncher, door, vine, cannon}. Missing ones fall back to
`default.GROUP`, so define every `default.GROUP` first and then only the theme overrides that differ.
Map: `map.w1` … `map.w9` (land/decor per world), `map.path`, `map.panel`, `map.icon`.

## Image list (exact names and sizes)

### Player (Mario art; palettes mario/fire/luigi/tanooki are applied by the engine)
Small, 16x16 each (feet on the bottom row):
`ms.stand ms.walk ms.run1 ms.run2 ms.jump ms.pjump ms.skid ms.death ms.front ms.kick ms.hold1 ms.hold2 ms.swim1 ms.swim2 ms.swim3 ms.climb1 ms.climb2`
(`run1/run2` = P-speed arms-out run; `pjump` = arms-out jump; `death` and `front` face the camera; small Mario can't duck.)

Big, 16x32 each (feet on the bottom row; head near the top, cap included):
`mb.stand mb.walk1 mb.walk2 mb.run1 mb.run2 mb.run3 mb.jump mb.pjump mb.fall mb.skid mb.duck mb.front mb.kick mb.hold1 mb.hold2 mb.throw mb.swim1 mb.swim2 mb.swim3 mb.climb1 mb.climb2 mb.slide mb.spinfront mb.spinback`
(`duck` = crouched, occupies only the bottom ~18 px; `slide` = sitting slope-slide; `spinfront/spinback` = tail-spin
body frames seen from front/back; walk cycle is walk1, walk2, stand, walk2.)

Raccoon overlays (palette `tail`): `tail.down tail.mid tail.up` (16x16, drawn behind the body with its right edge at
the body's left side, lower half), `tail.side` (16x8, sticking straight out for the tail spin), `ears` (16x8, drawn
over the top of the cap). Tanooki: `statue` (16x32, a stone/brown Tanooki statue).
Map icon: `mp.mario1 mp.mario2` (16x16, small front-facing figure used on the world map).

### Items & effects
`coin.1 coin.2 coin.3 coin.4` (16x16 spin: full, 3/4, edge, 3/4) · `mushroom` · `flower.1` (16x16; palette `flower`:
1 green stem, 2 red/orange petals, 3 yellow center) · `leaf` (16x16) · `star` (16x16 with eyes) · `pswitch` (16x16) ·
`pswitch.flat` (16x16, pressed) · `fireball.1..4` (8x8 rotation frames) · `hammer.1..4` (16x16 spinning hammer) ·
`debris` (8x8 brick chunk) · `puff.1 puff.2 puff.3` (16x16 smoke puff growing/fading) · `dust` (8x8 skid dust) ·
`sparkle.1 sparkle.2` (8x8) · `splash.1 splash.2` (16x16) · `vine.sprout` (16x16) · `key` (16x16) ·
`card.mushroom card.flower card.star` (16x16 icons) · `orb` (16x16, the fortress "?" ball) ·
tiny digits for score pop-ups: `tiny.0 … tiny.9` (4x6) and `tiny.up` (8x6, "UP").

### Enemies (face right)
`goomba.1 goomba.2` (16x16) · `goomba.flat` (16x8) · `wing.1 wing.2` (8x8, wing up/down) ·
`koopa.1 koopa.2` (16x24 walking) · `shell.1 shell.2 shell.3 shell.4` (16x16; 1 = still/front, 2-4 = spin frames) ·
`buzzy.1 buzzy.2` (16x16) · `bshell.1 … bshell.4` (16x16) · `spiny.1 spiny.2` (16x16) · `spinyegg.1 spinyegg.2` (16x16) ·
`piranha.1 piranha.2` (16x24: head mouth-closed/open on top of a stem) · `venus.1 venus.2` (16x32: mouth closed/open,
head tilted down) · `bill` (16x16) · `cheep.1 cheep.2` (16x16) · `blooper.1` (16x24 stretched) `blooper.2` (16x16
contracted) · `boo.1` (16x16 chasing, tongue out) `boo.2` (16x16 shy, covering face) · `thwomp.1 thwomp.2` (24x32 calm/angry) ·
`bones.1 bones.2` (16x24 walking Dry Bones) `bones.pile` (16x16 collapsed) · `podoboo` (16x16) ·
`bro.1 bro.2` (16x24 Hammer Bro walking) `bro.throw` (16x24) · `bobomb.1 bobomb.2` (16x16) · `wrench.1` (8x8) ·
`rocky.1 rocky.2` (16x16 Rocky Wrench popping up) · `cannonball` (16x16) · `rotodisc` (16x16) · `lakitu` (16x24) ·
`cloud.ride` (16x16) · `micro.1 micro.2` (8x8 micro-goomba).

### Bosses
`boomboom.stand boomboom.run1 boomboom.run2 boomboom.jump boomboom.hurt` (32x32) ·
`koopaling.stand koopaling.walk koopaling.jump koopaling.shell koopaling.cast` (16x32; palette `koopaling`, the engine
recolours per world) · `bowser.stand bowser.walk bowser.jump bowser.breath` (32x40) · `bowserfire.1 bowserfire.2`
(24x8) · `ring.1 ring.2` (8x8 magic ring) · `wand` (8x16).

### Level tiles (16x16, background layer; slot 0 = transparent to the sky color)
Ground sets (surface style, 9-slice + inner corners):
`ground.grass.tl .t .tr .l .c .r .bl .b .br .itl .itr` and the same eleven suffixes for `ground.sand`, `ground.snow`,
`ground.cloud`. (`t`=top edge, `c`=fill, `itl/itr` = inner corners where ground continues up-left/up-right.)
Block-style sets (repeat): `ground.rock.t ground.rock.c`, `ground.stone.t ground.stone.c`, `ground.metal.t ground.metal.c`,
`ground.wood.t ground.wood.c`.
Blocks: `brick` · `qblock.1 qblock.2 qblock.3 qblock.4` (animated "?" block) · `used` · `hard` · `wood` · `note.1 note.2` ·
`ice` · `muncher.1 muncher.2` · `spike` · `cannon.top cannon.mid` (Bill Blaster) · `door.top door.bot` ·
`vine.top vine` · `coinblock` (optional variant brick).
Pipes (green, palette `pipe`): vertical `pipe.tl pipe.tr pipe.l pipe.r` (tl/tr = the wide lip) and ceiling pipe lips
`pipe.bl pipe.br`; horizontal (mouth facing left) `hpipe.ml hpipe.mlb` (mouth top/bottom) `hpipe.t hpipe.b` (body top/bottom rows).
Platforms: `semi.l semi.c semi.r` (thin bridge/log: top ~6 px) · `cloud.l cloud.c cloud.r` (cloud platform) ·
big bolted blocks `block.tl block.t block.tr block.l block.c block.r block.bl block.b block.br` (SMB3-style colored
platforms with bolts at the corners and a drop shadow on the right/bottom; the engine tints with block1..block4).
Liquids: `water.top1 water.top2 water` · `lava.top1 lava.top2 lava`.
Decor (drawn behind everything, no collision; palette `THEME.decor`): `bush.l bush.c bush.r` (16x16 bush pieces),
`hill.l hill.c hill.r hill.top` (background hill pieces), `skycloud.l skycloud.c skycloud.r` (sky cloud),
`flowerdecor`, `fence`, `palm.top palm.trunk`, `pillar` (castle column), `torch.1 torch.2`, `window` (castle window),
`chain`, `bolt`, `rope`, `star.bg`, `pyramid` (16x16 brick pattern), `coral`.
Goal area: `goal.box` (32x32 roulette frame, card drawn inside by the engine), `goal.floor` (16x16 dark "backstage" floor).

### World map (16x16)
`m.land m.land2 m.water1 m.water2 m.tree m.hill m.rock m.flower m.path.h m.path.v m.dot m.bridge.h m.bridge.v`
`m.panel` (level panel, the number is drawn on top by the engine) `m.panel.clear` (cleared, shows an "M")
`m.fortress m.fortress.ruin m.castle m.toad m.toad.used m.spade m.start m.pipe m.lock.h m.lock.v m.airship m.hbro m.palm m.sand m.snow m.cloudland m.lava m.skull`
