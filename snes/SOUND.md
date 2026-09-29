# SNES sound (SPC700 driver, BRR samples, music + SFX) — owner: sound agent

The ROM plays the same music and sound effects as the Windows game (`src/Audio`, `data/music/*.mml`). Everything is
converted at build time by `smb4tools snes-export` (step "audio", `src/Tools/Snes/SnesAudio.cs`): the instruments are
rendered with the game's own generator code (`Instruments.cs`), the songs are parsed with the game's own MML parser
(`Music.cs`), the effects are read from `SfxBank` (`Sfx.cs`). Re-running the export picks up any MML/instrument change.

## Files
| File | What |
|---|---|
| `snes/spc/driver.asm` | SPC700 driver source (sequencer, voices, SFX, upload receiver) |
| `src/Tools/Snes/SnesAudioAsm.cs` | small two-pass SPC700 assembler (ca65 has no SPC700) |
| `src/Tools/Snes/SnesAudio.cs` | converter: samples → BRR, ADSR fit, songs → sequences, SFX → programs, APU RAM layout, gen files |
| `src/Tools/Snes/SnesAudioEmu.cs` | `snes-audio` / `snes-song` tools: SPC700 + S-DSP emulator running the real driver image |
| `snes/src/snd.s` | 65816 side: IPL boot upload, song/sample upload, commands (bank `CODE6`) |
| `<Out>/gen/music.inc` | `SONG_*` (alphabetical from 1, `SONG_NONE` = 0), `SFX_*` (= `SfxId` + 1), `SFX_STOP`, `SND_NSONGS/NSFX` |
| `<Out>/gen/snd_data.s` + `snd_*.bin` | driver image, song blobs, sample blobs, upload tables — **ROM banks `BANK16`…** (5 banks now, max 12) |
| `<Out>/gen/spc_driver.sym`, `spc_driver_full.asm` | driver symbols (SPC RAM addresses for tests) and the assembled source |

## 65816 API (`snd.s`; JSL/RTL, A8/XY16, DB=$80)
* `snd_init` — IPL-uploads the resident image (driver + tables + SFX + SFX samples, ~19 KB, ≈0.5 s) and starts it.
  Every wait has a timeout; if the APU never answers, `snd_ok` stays 0 and all other calls return at once.
* `snd_music` A = `SONG_*` — uploads the song and any samples not already in APU RAM, then starts it (always restarts;
  skip the call if the same song should just continue). A = 0 stops. The CPU is busy during the upload:
  full level/map song ≈ 0.2–0.65 s (do it during screen transitions); level ↔ star / P-switch / hurry / death / clear
  only re-sends the sequence (1–2 KB, 1–3 frames) because those pairs are laid out co-resident and the level song
  preloads its jingles' samples.
* `snd_sfx` A = `SFX_*`; A = `SFX_STOP | id` stops that effect (the looping `SFX_PMETER`).
* `snd_tempo` A = 1 hurry (×1.3, like `SetMusicSpeed(1.3)`), 0 normal. A new song resets to normal.
* `snd_fade` A = frames (0 = 60): C# `FadeOutMusic` curve (g²), stops at the end.
* `snd_pause` A = 1 mutes/freezes music (SFX keep playing), 0 resumes.
* `snd_status` → A = 1 while a song plays, 0 when stopped or a `loop 0` jingle has finished (C# `Sound.MusicFinished`).
  Same bit is readable any time at `APUIO1` bit 0. `snd_cur` = last song id started, `snd_ok` = driver alive.

## APU RAM layout
`$0000-$00EF` driver DP · `$0100-$01FF` stack · `$0200-$05FF` driver arrays · `$0600-…` code (4.5 KB) + tables
(pitch/pan/volume/sine/arp) + sample info (ADSR, release GAIN, tuning) + kit table + SFX table/programs + DIR
(page aligned) + resident SFX samples → `res_end` (≈`$5200`). Above that every dynamic sample has **one fixed address**
chosen by the converter so that samples used by the same song (or by a level song and its jingles) never overlap each
other, the song's sequence blob or its echo buffer. Each song blob sits right under its echo buffer, which ends at
`$FFFF` (EDL × 2 KB). The CPU keeps a resident flag per dynamic sample and evicts what an upload overwrites.

## Ports and protocol
CPU→SPC: port0 command (`cmd | toggled bit 7`), port1 argument, ports 2/3 SFX requests (`id | toggled bit 7`).
SPC→CPU: port0 command ack, port1 status (`$80` alive `| 1` playing), ports 2/3 SFX acks. Commands: 2 stop, 3 tempo,
4 fade, 5 pause, 6 load, 7 stop SFX. Load = receive mode: header `[type=1, addr]` on ports 1-3 with port0 = `$41/$43`
(alternating), then 2 bytes per handshake (ports 1/2, port0 = even byte index, echoed), `$FF` ends a block;
header `[0, song address]` ends and starts the song. ≈49 KB/s.

## Driver features (vs. the C# engine)
* 60 Hz sequencer (timer 0), same tick/tempo/swing (`Warp`) math, gate `q`, ties/next-is-tie, `[ ]n` repeats (4 deep),
  `L` loop points, `T` tempo, transpose, detune, arpeggio (every 2 frames), vibrato (5.5 Hz, delay + ramp),
  portamento (same exponential glide), echo send per channel, kit pieces with per-piece pan/volume.
* Pitch: 1/256-semitone note → table (1/16 semitone, top octave) + interpolation + octave shift → DSP pitch.
* Volume: C# perceptual curve × instrument gain × equal-power pan (41 steps) × fade.
* Envelopes: C# attack/decay/sustain/sustain-decay fitted to SNES ADSR per instrument; C# release = GAIN exponential
  decay with the fitted rate (not the fixed 8 ms SNES key-off).
* Echo: song's EDL/feedback/return; FIR = the C# 8-tap low-pass (5,12,19,28,28,19,12,5). Echo is switched safely on each
  song start (writes off, wait for the EDL latch, refill, then feedback/volume on — echo fades in after ~0.4 s).
* 8 DSP voices = 8 MML channels. The C# engine has 3 voices per channel; here a channel borrows released/idle voices of
  other channels for chord notes 2–3 and for hats/cymbals on a kit channel; when none is free the extra chord notes
  are dropped and kit groups share the channel voice. Release tails are cut by the next note on the same voice.
* SFX: 2 slots on the song's two least busy voices; each effect uses its 1–2 highest-priority C# parts; slot
  replacement follows the C# per-slot priority rule. When an effect ends the voice goes back to music (a held note is
  re-keyed). Noise effects use the DSP noise generator (clock swept like the C# noise pitch).
* Samples: instrument zones rendered by `Instruments.cs`, attack truncated with a click-free join into its steady loop,
  loop shortened with a cross-fade, resampled (windowed sinc) to 5–32 kHz, BRR-encoded (best filter/shift per block,
  filter 0 on first/loop blocks). Drum tails of crash/ride/open hat are flattened + looped with an SR decay.
  Quality is lowered automatically only where a song does not fit APU RAM, then raised back wherever it still fits.

## Tools and verification
* `smb4tools snes-audio OUTDIR` — audio export step only (fast).
* `smb4tools snes-song NAME out.wav [sec] [stats|hurry|pause|fade|then:OTHER]` — boots the driver image on the emulator,
  uploads through the driver's real receive protocol, renders 32 kHz stereo, prints peak/RMS/clipping, driver load,
  upload size/time; `stats` adds the C# reference level, note-ons per channel and loudness-envelope correlation.
  `snes-song sfx out.wav 37` renders every SFX. Env: `SND_PROF=1` (driver profile), `SND_ENV=1` (envelope table).
* Mesen (`snesrun.ps1`): `emu.read(a, emu.memType.spcRam)` / `spcDspRegisters`; driver tick counter at SPC `$004C`
  (16-bit, +60/s), `song_on` `$0016`, channel pointers `$0200`/`$0208`, voice ENVX = DSP reg `v*16+8`.
* Last check (all 37 songs, 20 s each): RMS within 1.5 dB of the C# render, no clipped samples, driver load ≈35% of a
  frame on average; Mesen test ROM: boot upload, song switches, hurry, jingles and SFX all run at 60 ticks/s.
