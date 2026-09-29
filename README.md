# Super Mario Bros. 4 — The Lantern Tour

> **A personal, just-for-fun fan project.** This is an unofficial, non-commercial fan game made by one hobbyist as
> a love letter to Super Mario Bros. 3. It is **not affiliated with, endorsed by, sponsored by, or connected to
> Nintendo** in any way. It is **not for sale** and never will be — please don't sell it, bundle it, or charge for it
> in any form. See the [Disclaimer](#disclaimer) below.

An imagined "next generation after Super Mario Bros. 3": eight new worlds with SMB3's exact movement and feel,
presented in a 16-bit style. The game is a **native Super Nintendo (SNES) ROM** written in 65816 assembly, so it
runs in any SNES emulator; this repo also includes a small Windows player that runs the ROM with modern
conveniences.

## Play it

**Option 1 — any SNES emulator:** open [`SuperMarioBros4.sfc`](SuperMarioBros4.sfc) in Mesen, bsnes,
snes9x or RetroArch. `.sfc` is the standard headerless SNES ROM format (4 MB LoROM with battery save), and it
should also run from SNES flash carts.

**Option 2 — the Windows player:** download [`SuperMarioBros4.exe`](SuperMarioBros4.exe) and double-click it. That single
file is the whole thing (ROM and emulator core built in); nothing to install on Windows 10/11. Saves live in `%APPDATA%\SuperMarioBros4\`,
screenshots (F12) in `Pictures\Super Mario Bros 4\`.

### Controls
| SNES pad | Keyboard | Xbox-style controller | Other controllers |
|---|---|---|---|
| D-pad | Arrow keys / WASD | D-pad or left stick | D-pad/hat or left stick |
| B — jump | X, K, Space | A | Cross / button 2 |
| Y — run / fire / tail | Z, J, Left Shift | X or RT | Square / button 1 |
| Start — pause | Enter | Menu | Options |
| Select — items (map) | Right Shift, Tab | View | Share |

Windows player extras: **Esc** options menu (rebinding, scaling, 8:7 pixels, scanlines, frame pacing for
60/120/240 Hz and VRR, run-ahead), **F11** fullscreen, **F5/F9** save/load state, **F12** screenshot.

### Moves
Hold **Run** to speed up and fill the **P-meter**; hold **Jump** longer to jump higher; press the opposite
direction to **skid**. **Down** ducks, **slides down slopes** and enters pipes; **Up** opens doors and climbs
vines. Hold Run to **carry shells**, release to kick. With the **Super Leaf**, tap Jump to float, Run to tail-spin,
and jump with a full P-meter to **fly**.

## What's in it
* 8 worlds plus Bowser's castle, 55 original levels, world maps, Toad houses, spade and N-Spade bonus games,
  wandering Hammer Bros, fortresses (Boom Boom), airships (7 Koopalings) and a final battle with Bowser.
* SMB3-style power-ups: Super Mushroom, Fire Flower, Super Leaf, Tanooki Suit, Frog Suit, Hammer Suit, Starman,
  P-Wing; an item inventory; goal cards; 3 battery-saved files; 2-player alternating.
* 16-bit presentation: outlined 15-color sprites, gradient skies and parallax backgrounds, and an SPC700 soundtrack
  of original compositions with echo.
* Physics reproduced rule-for-rule from SMB3 and verified frame-by-frame (see `dev/docs/01-game-feel-and-physics.md`).

## Repository layout
| Path | What it is |
|---|---|
| `SuperMarioBros4.exe` | **Play this** on Windows (single file) |
| `SuperMarioBros4.sfc` | **Or this** in any SNES emulator |
| `dev/` | Everything used to make it (below) |
| `dev/snes/` | **The game source.** 65816 source (`dev/snes/src`), linker config, build and test scripts, design docs |
| `dev/data/` | Game content as plain text: levels, world maps, pixel art, music |
| `dev/src/Tools/` | `smb4tools` — converts `data/` into SNES format, renders previews, runs tests |
| `dev/src/Frontend/`, `dev/src/Platform/` | The Windows ROM player (hosts the bsnes libretro core) |
| `dev/src/Game/` | The original C# version of the game, kept as the behaviour reference the ROM was ported from |
| `dev/docs/` | Design documents (game feel & physics, architecture, game design, art/audio, roadmap) |

## Build from source (Windows)
Everything lives in `dev/`. From the repository root:
```powershell
powershell -ExecutionPolicy Bypass -File dev\snes\tools\setup.ps1   # once: downloads the ca65 assembler + Mesen2 (tests)
powershell -ExecutionPolicy Bypass -File dev\tools\fetch-core.ps1   # once: downloads the bsnes libretro core
powershell -ExecutionPolicy Bypass -File dev\release.ps1            # builds + tests, then updates the top-level exe and ROM
```
`release.ps1` converts `dev\data` and assembles the ROM (`dev\snes\build.ps1`), runs the ROM tests, builds the
Windows player (`dev\build.ps1`, C# compiler built into Windows) with the ROM and core embedded, self-tests it, and
copies `SuperMarioBros4.exe` + `SuperMarioBros4.sfc` to the top level. Tests on their own:
`dev\snes\test\qa-rom.ps1` (every level boots and runs), `dev\snes\test\parity-suite.ps1` (frame-exact physics
against the reference), `SuperMarioBros4.exe --selftest` (player: video, audio, saves). Technical docs:
`dev/snes/DESIGN.md`, `dev/snes/ENTITIES.md`, `dev/snes/TESTING.md`, and `dev/snes/KIT-NOTES.md` — notes for reusing
this setup to make other SNES homebrew games.

## Disclaimer
* This is a **personal hobby project made for fun**. It is free, non-commercial, and **not intended to be sold**.
* **Not affiliated with Nintendo.** *Super Mario Bros.*, Mario, Luigi, Bowser, the Koopalings, Toad and all related
  names and characters are trademarks and copyrights of **Nintendo**. No endorsement by Nintendo is implied.
* **No Nintendo assets are included.** All art, music, sound, levels and code in this repository were made from
  scratch for this project; nothing was ripped, traced or copied from any Nintendo game or ROM. The original
  games were used only as a reference for how things should feel.
* If you are a rights holder and want something changed or taken down, please open an issue on this repository and
  it will be handled promptly.
* If you enjoy this, please support the official games — buy and play Nintendo's Mario titles.

## Licenses
The emulator core embedded in the Windows player (`SuperMarioBros4.exe`) is **bsnes** (GPLv3); see
[`dev/LICENSE-THIRD-PARTY.md`](dev/LICENSE-THIRD-PARTY.md). The toolchain downloaded by the setup scripts (cc65, Mesen2) is
not included in this repository.
