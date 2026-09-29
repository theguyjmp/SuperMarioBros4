# Super Mario Bros. 4 — The Lantern Tour

A fan-made, 16-bit-style sequel to **Super Mario Bros. 3** (the generation after it), built as a native Windows desktop game.
Movement is a rule-for-rule reproduction of SMB3's physics (see `docs/01-game-feel-and-physics.md`), with an
optional "Modern" layer of invisible leniency (coyote time + jump buffer).

## Play
Double-click **`bin\SuperMarioBros4.exe`** (keep the `bin\cores` folder next to it). Nothing to install (it runs on the
.NET Framework 4.8 built into Windows 10/11). The game itself is the SNES ROM embedded in the exe; the exe is a
player for it built on the bsnes emulator core (libretro). A `SuperMarioBros4.sfc` placed next to the exe is played
instead of the embedded one. Settings, the battery save (`save.srm`) and save states live in
`%APPDATA%\SuperMarioBros4\`; screenshots (F12) go to `Pictures\Super Mario Bros 4\`.

### Controls (all rebindable in the Esc menu → Controls)
| SNES pad | Keyboard | Xbox-style controller | Other controllers (PlayStation, Switch Pro, 8BitDo, USB) |
|---|---|---|---|
| D-pad | Arrow keys / WASD | D-pad or left stick | D-pad/hat or left stick |
| B (jump) | X, K, Space | A | Cross / button 2 |
| Y (run / fire / tail) | Z, J, Left Shift | X or RT | Square / button 1 |
| A | C, L | B | Circle / button 3 |
| X | V, I | Y | Triangle / button 4 |
| L / R | Q / E | LB (or LT) / RB | L1 / R1 |
| Start | Enter | Menu | Options / button 10 |
| Select | Right Shift, Tab | View | Share / button 9 |

| Player | Key / button |
|---|---|
| Options menu (pauses) | Esc, or hold Select+Start on a controller |
| Fullscreen | F11 or Alt+Enter |
| Screenshot | F12 |
| Save / load state | F5 / F9 |
| Pause | Pause/Break |

Options menu: window/fullscreen, window size, pixel-perfect or sharp scaling, SNES 8:7 pixel shape, scanlines, FPS
readout; frame pacing (auto-locks to 60/120/240 Hz displays; VRR mode for G-Sync/FreeSync), 1-frame run-ahead,
low-latency mode, pause when inactive; volume and audio buffer; controller layout (SNES positions / by label),
per-button rebinding for keyboard, XInput and DirectInput-class pads, stick deadzone and up/down angle, left-stick
on/off, SOCD handling, live controller test, hot-plugging (the game pauses if a controller disconnects); emulator
core options; save/load state, reset, quit.

### Moves
Hold **Run** to speed up; keep running to fill the **P-meter** for P-speed. Hold **Jump** longer to jump higher.
Press the opposite direction to **skid** and turn. **Down** ducks (big Mario), **slides down slopes** (knocking
enemies away) and enters pipes; **Up** opens doors and climbs vines. Hold Run to **carry shells**, release to kick
them. In water, tap Jump to swim; **Up + Jump** at the surface leaps out.
With the **Super Leaf**: tap Jump to float, Run to tail-spin, and jump with a full P-meter to **fly**.

## What's in the game
* 8 worlds of original levels (plains, desert, sea, jungle, sky, ice, machines, Bowser's volcano) with world maps,
  Toad houses, spade bonus games, the N-Spade card-matching game (appears every 80,000 points), wandering
  Hammer Bros, fortresses (Boom Boom), airships (7 Koopalings) and a final battle with Bowser.
* SMB3 power-ups: Super Mushroom, Fire Flower, Super Leaf (Raccoon), Tanooki Suit, Frog Suit, Hammer Suit,
  Starman, P-Wing; items usable from the map; goal cards with 1-UP bonuses; 3 save slots; 2-player alternating mode.
* A 16-bit-style renderer (256×240, 24-bit palettes, 15-color outlined sprites, gradient skies, parallax layers) and an SNES-style sampled-instrument sound engine with echo, original music and sound effects.

## Build from source
`powershell -ExecutionPolicy Bypass -File tools\fetch-core.ps1` once (downloads the bsnes libretro core into
`bin\cores`), then `powershell -ExecutionPolicy Bypass -File build.ps1` compiles `bin\SuperMarioBros4.exe` (the player,
`src\Frontend` + `src\Platform`, with `bin\SuperMarioBros4.sfc` — else `snes\build\smb4.sfc` — embedded; `-Rom x.sfc`
picks another) and the developer tool `bin\smb4tools.exe` with the C# compiler that ships with Windows. `-Out dir`
builds elsewhere, `-ToolsOnly` builds just the tool. `SuperMarioBros4.exe --selftest` runs the ROM headless for 300
frames with scripted input and checks video, audio, run-ahead, save states and SRAM saving (report + PNGs in
`selftest\`). The player is generic: see "Frontend player" in `snes/KIT-NOTES.md` to package another SNES ROM.

The original C# version of the game (`src\Game`, content in `data\`) is no longer the runtime; it is kept as the
reference the ROM is ported from and is compiled into `smb4tools.exe`, whose converter and tests use it.
Developer tools (`bin\smb4tools.exe`): `selftest` (physics checks against the spec), `validate`, `reach <level>`
(proves a level is completable), `progress` (proves every world map can be finished), `fuzz <level>`,
`level <id> <png>`, `map <n> <png>`, `shot <level> <png> <script>`, `sheet <png>`, `screen <name> <png>`,
`song <name> <wav>`, `flow <dir> <script>`. `qa.ps1` runs the whole suite.

## Legal
This is a non-commercial fan project. Mario and all related characters and names are trademarks of Nintendo.
All art, music, sound and code here are original works made for this project; no Nintendo assets are included.
Please don't distribute it publicly under this name (see `PLAN.md` §7). The emulator core `bin\cores\bsnes_libretro.dll`
is bsnes (GPLv3) — see `LICENSE-THIRD-PARTY.md`.

## SNES ROM
`bin/SuperMarioBros4.sfc` **is the game**: a native SNES (Super Nintendo) ROM, 4 MB LoROM, written in 65816 assembly with an
SPC700 sound driver. Open it in any SNES emulator (Mesen, bsnes, snes9x, RetroArch) or just run the exe (it embeds it).
Everything is in: 8 worlds + Bowser's castle, all enemies and bosses, all suits, world maps, Toad houses, spade and
N-Spade games, Hammer Bro battles, 3 battery-saved files, 2P alternating, ending. Controls: B = jump, Y = run/fire/
tail, Start = start/pause, Select on the title = debug level select.
Rebuild: `powershell -File snes/tools/setup.ps1` once (downloads the assembler + test emulator), then
`powershell -File snes/build.ps1` → `snes/build/smb4.sfc`. Tests: `snes/test/qa-rom.ps1` (every level boots and
runs), `snes/test/parity-suite.ps1` (frame-exact physics vs the C# reference). Docs: `snes/DESIGN.md`,
`snes/ENTITIES.md`, `snes/TESTING.md`, and `snes/KIT-NOTES.md` (how to build the next SNES game with this kit).
