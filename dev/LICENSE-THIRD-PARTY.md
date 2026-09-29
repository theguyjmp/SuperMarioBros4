# Third-party components

## bsnes libretro core — `bin/cores/bsnes_libretro.dll`
* What: the SNES emulator the game exe uses to run the ROM (loaded at runtime as a libretro core).
* License: **GNU General Public License v3.0** (https://www.gnu.org/licenses/gpl-3.0.html).
* Authors: byuu/Near and the bsnes contributors; libretro port by the libretro team.
* Source: https://github.com/libretro/bsnes — binary from the libretro buildbot
  (https://buildbot.libretro.com/nightly/windows/x86_64/latest/bsnes_libretro.dll.zip), fetched by `tools/fetch-core.ps1`.
* It is not modified. When distributing the exe together with this DLL, include this file and a link to the core's
  source above (GPLv3 §6). The exe loads the core in-process; the frontend source is in this repository (`src/Frontend`,
  `src/Platform`).

## libretro API
The frontend implements the libretro API (https://github.com/libretro/libretro-common, `libretro.h`, MIT license)
from its published definitions; no libretro source files are included.
