# Super Mario Bros. 4 — The Lantern Tour

A fan-made, 16-bit-style sequel to **Super Mario Bros. 3** (the generation after it), built as a native Windows desktop game.
Movement is a rule-for-rule reproduction of SMB3's physics (see `docs/01-game-feel-and-physics.md`), with an
optional "Modern" layer of invisible leniency (coyote time + jump buffer).

## Play
Double-click **`bin\SuperMarioBros4.exe`**. Nothing to install (it runs on the .NET Framework 4.8 built into
Windows 10/11). Settings and saves live in `%APPDATA%\SuperMarioBros4\`; screenshots (F12) go to
`Pictures\Super Mario Bros 4\`.

### Controls (all rebindable in Options → Controls)
| Action | Keyboard | Xbox-style controller | Other controllers (PlayStation, Switch Pro, 8BitDo, USB) |
|---|---|---|---|
| Move | Arrow keys / WASD | D-pad or left stick | D-pad/hat or left stick |
| Jump (A) | X, K, Space | A or B | Cross / button 2 |
| Run / fire / tail (B) | Z, J, Left Shift | X, Y, RT or RB | Square / button 1 |
| Pause | Enter | Menu | Options / button 10 |
| Items (world map) | Right Shift, Tab | View | Share / button 9 |
| Fullscreen | F11 or Alt+Enter | | |
| Screenshot | F12 | | |
| Back / pause | Esc | | |

Controller settings: button layout presets (Modern / Nintendo), per-action rebinding for keyboard, XInput and
DirectInput-class pads, stick deadzone, the stick's "duck angle" (prevents accidental ducking while running),
left-stick on/off, vibration on/off and strength, opposite-direction (SOCD) handling, a live controller test page,
and hot-plugging (the game auto-pauses if a controller disconnects).

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
* Options: window/fullscreen, pixel-perfect or sharp scaling, NES 8:7 pixel aspect, scanlines, frame pacing
  (auto-locks to 60/120/240 Hz displays; VRR mode for G-Sync/FreeSync), low-latency mode, volume, feel profile
  (Modern / Classic), assists (infinite lives), input display, reduced flashing.

## Build from source
`powershell -ExecutionPolicy Bypass -File build.ps1` compiles `bin\SuperMarioBros4.exe` and the developer tool
`bin\smb4tools.exe` with the C# compiler that ships with Windows. Game content lives in `data\` (levels, maps, art,
music as plain text) and is embedded into the exe; while a `data` folder sits next to (or one level above) the exe,
files there are used instead, so content can be edited without rebuilding.

Developer tools (`bin\smb4tools.exe`): `selftest` (physics checks against the spec), `validate`, `reach <level>`
(proves a level is completable), `progress` (proves every world map can be finished), `fuzz <level>`,
`level <id> <png>`, `map <n> <png>`, `shot <level> <png> <script>`, `sheet <png>`, `screen <name> <png>`,
`song <name> <wav>`, `flow <dir> <script>`. `qa.ps1` runs the whole suite.

## Legal
This is a non-commercial fan project. Mario and all related characters and names are trademarks of Nintendo.
All art, music, sound and code here are original works made for this project; no Nintendo assets are included.
Please don't distribute it publicly under this name (see `PLAN.md` §7).

## SNES ROM
`bin\SuperMarioBros4.sfc` is a real SNES (Super Nintendo) ROM of the game — open it in any SNES emulator
(Mesen, bsnes, snes9x, RetroArch). Controls: B = jump, Y = run/fire/tail, Start = start/pause, Select on the title
= level select (all 54 levels). Milestone 1: World 1-style play with Mario's exact physics, blocks, power-ups,
Goombas, Koopas, Piranhas, pipes, HUD, music and SFX; enemies beyond that set don't spawn yet.
Rebuild: `powershell -File snes\tools\setup.ps1` once (downloads the assembler + test emulator), then
`powershell -File snes\build.ps1` → `snes\build\smb4.sfc`. Design/tests: `snes/DESIGN.md`, `snes/TESTING.md`.
