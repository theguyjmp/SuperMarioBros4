# SNES backgrounds (sky gradient + parallax) — backgrounds agent

Files: `snes/src/bg.s` (runtime), `src/Tools/Snes/SnesBackgrounds.cs` (converter step of `smb4tools snes-export`),
`snes/bgtest/` (standalone test ROM). API contract: `snes/DESIGN.md` § Backgrounds.

## What the C# game does (reference: `WorldRender.DrawSkyAndParallax / DrawParallax`)
* Gradient `THEME.grad` (n colors) = n even bands over the 192-px playfield, screen fixed.
* Layers `para.THEME.1..4` (far→near), 256 px wide, repeated; x = `-(camX*N/8 % 256)`,
  y = `192 - H + extraY + (floorCam - camY)*N/16` with `floorCam = max(0, areaH - 192)`.
* Water areas: `para.THEME.w1..4` over the water body, clipped to lines below `WaterRow*16 + (WaterRow>0 ? 16 : 0) - camY`.

## How the ROM does it (one BG2 layer)
* **Converter**, per theme and water variant — `dry` (normal layers), `wet` (w layers only, `water=0` areas), `mix`
  (both, split at the water line) — only the variants some area uses (13 dry + sea wet/mix, ice mix, fortress mix):
  * Chooses per screen row which layer owns it at the floor camera: the layer whose SNES rendering (own pixels, holes
    filled with one flat color) is closest to the true composite, smoothed by a Viterbi pass (penalty per band switch).
    The **hole fill** color (mean of what the holes should show) becomes the per-line backdrop of that band.
  * Simulates every camera height the areas of that theme can reach to find the extra layer rows vertical parallax
    reveals ("margins"); stores owned rows + as many margin rows as fit (weighted by how often they are needed).
  * Palettes: layers split into BG palettes 6/7 (best partition), merged/quantized to 15 colors each.
  * Tiles: 8x8 4bpp, deduped with H/V flips; if > 256, lossy merge of the cheapest tile pairs (same palette).
  * Map 32x**64** at $4800 (BG2SC=$4A: same 2048 words as the reserved 64x32, 256-px wrap for free, 512 px of rows).
  * Gradient colors → BGR555 with a chroma-preserving rounding (dark greys stay neutral). Underwater gradient: palette
    `THEME.wgrad` if the art defines it, else derived from the average of the `water` tile in `THEME.water`.
  * Output: `gen/themes.inc` (THEME_* ids 0-12), `gen/bg_themes.s` + `gen/bg_<theme>_<variant>.bin` (one blob per
    variant, ≤ 13.2 KB, packed 2 per bank in **BANK52-BANK63**, currently 52-59), directory `bg_dir_dry/wet/mix` in CODE5.
    Blob layout = `BGV_*` constants in both files.
* **Runtime** (`bg_update`, DB=$7E, all state in BSS/HIBSS):
  * per layer: `hx`, `ly` from the C# formulas (camera x/y, `bg_area_h`).
  * when some `ly` or the water line changed: clip every segment (owned band or margin run) to its region (dry above
    the water line, wet below), sort the 2·n events (insertion sort, nearly sorted from last frame) and sweep them:
    owner = highest active segment → runs (≤127 lines) + gradient/fill HDMA table. While the camera keeps moving
    vertically this rebuild runs at most every 4th frame (in between the old bands are kept, their scroll follows).
  * every frame: scroll table, one entry per run (HOFS = hx, VOFS = vbase − ly − 1).
  * HDMA (double-buffered, swapped in `bg_nmi`): ch6 direct mode 3 → BG2HOFS/BG2VOFS; ch7 **indirect** mode 3 →
    CGADD,CGADD,CGDATA×2 with per-line records (gradient line colors, hole fills, black HUD); ch5 direct mode 1 →
    WH0/WH1: window 1 covers the screen from line 192 so OBJ (WOBJSEL=$02, TMW=$10) never shows over the HUD.
  * Lines 192-223: BG2 points at empty map rows, backdrop black.
  * Flat `bg=` areas (X=1): empty BG2, backdrop `bg_flat_color` (BGR555) on the playfield, black HUD.
* Cost (Mesen, scanlines of 1364 master clocks): per frame 5-10 lines; a band rebuild 26-99 lines (only when a layer
  row/water line changed, ≤ every 4th frame). Upload in `bg_load`: ~13 KB DMA (forced blank).

## Engine checklist
`bg_hdma_off` before its own forced-blank uploads → `bg_set_water` (X = `WaterRow*16 + (WaterRow>0?16:0)` or $FFFF)
→ `bg_flat_color` if `bg=` → `bg_load` (A theme, X flat) → every frame `bg_cam_x/y`, `bg_area_h`, `bg_update`.
BG1 must leave the water body transparent (only the surface row), TM must include BG2, don't touch WOBJSEL/TMW/WH0/WH1.

## Test
```
powershell -ExecutionPolicy Bypass -File build.ps1 -ToolsOnly -Out .agentbin\snes-bg
$env:SMB4_BGPREVIEW = "snes\build-bg\bgtest\prev"     # optional: converter's simulation of the same frames
powershell -ExecutionPolicy Bypass -File snes\bgtest\build.ps1 -Tools .agentbin\snes-bg\smb4tools.exe -Out snes\build-bg\bgtest
powershell -ExecutionPolicy Bypass -File snes\tools\snesrun.ps1 -Rom snes\build-bg\bgtest\bgtest.sfc -Lua snes\bgtest\shots.lua -OutDir snes\build-bg\bgtest\shots
```
The test ROM cycles through all 17 variants + a flat backdrop (A = floor camera at x 0, B = 40 px up at x 300, water
line at 120/160 for mix) with a 16x16 test sprite crossing line 192. Verified 2026-09-28: all 34 ROM screenshots equal
the converter's simulation pixel for pixel except the sprite's 8 visible lines (sprite hidden below 192), and match
the Windows renderer (`smb4tools shot <level> out.png W30`) layout; losses vs Windows = one layer per scanline (hidden
layer parts replaced by the hole-fill color) and lossy tiles in the busiest themes (jungle, sky, ice, plains).
