# Music contract (data/music/*.mml)

All music is **original**: new 16-bit (SNES-class) arrangements. Do not transcribe or paraphrase any
Nintendo melody.

## The sound engine (src/Audio)
SPC700-style sample synth: 8 music channels (`C1:`..`C8:`), each playing one **instrument** (a sample
generated in code at start-up — no sample files) with ADSR, volume, stereo pan, vibrato, portamento and an
echo send into a SNES-style echo (delay line + 8-tap FIR low-pass + feedback). Each channel owns two voices so
a note's release tail rings on under the next note. Sound effects have their own 6 voices and never steal
music channels.

### Instruments (`@name`)
| Family | Names |
|---|---|
| Keys | `piano` `epiano` (FM) `organ` `musicbox` |
| Mallets / bells | `marimba` `xylo` `vibes` (motor tremolo) `steeldrum` `glock` `bell` (church) `timpani` |
| Strings / pads / voice | `strings` (ensemble) `pizz` `pad` (warm synth) `choir` ("aah") |
| Winds / brass | `brass` (section) `trumpet` (solo) `tuba` (oom-pah bass) `flute` `whistle` `ocarina` `oboe` `clarinet` |
| Synth | `lead` (chorused pulse lead) `snaplead` (snappy plucky pulse lead, great staccato) `sawlead` |
| Bass / plucks | `bass` (finger) `slapbass` `synbass` `guitar` (nylon pluck) `harp` `orchhit` |
| Raw waves | `sine` `square` `pulse` `saw` `tri` `noise` |
| Drums | `kit` — see below |

Sensible ranges: basses o1–o3, pads/strings o3–o5, leads o4–o6, glock/musicbox/xylo o5–o7, timpani o2–o3.

### Drum kit (`@kit`)
After `@kit` on a channel, lowercase letters are drum hits (with optional length, e.g. `k8 s8. h16`):

| Letter | Piece | Letter | Piece |
|---|---|---|---|
| `k` | kick | `t` `m` `u` | low / mid / high tom |
| `s` | snare | `g` `f` | low / high conga |
| `h` | closed hat | `z` `b` | high / low bongo |
| `n` | open hat | `y` | hand clap |
| `c` | crash | `j` | shaker |
| `e` | ride | `d` | rim / side-stick |
| `a` | cowbell | `w` `x` | boom / thud (big hits) |
| `i` | woodblock | | |

Reserved in kit mode: `r` rest, `l` length, `v` volume, `q` gate, `o` octave, `p` pan. A kit channel has three
voices: drums (`k s t m u g f z b y d i w x`), hats (`h n j a`) and cymbals (`c e`) — so one channel can play a full
kick/snare/hat groove with a ringing crash (hats within a group cut each other: a closed hat chokes the open hat).
Two kit channels (e.g. kick/snare on C6, hats/percussion on C7) give polyrhythms room. Each piece has its own
stereo placement; `p` offsets it.
Switch a channel back to pitched notes with any other instrument (e.g. `@timpani o2 g4 @kit k4`).

## Syntax
Header lines:
* `tempo 150` (BPM, quarter note) · `title Some Name` · `loop 0` for one-shot jingles (default loops)
* `echo DELAY FEEDBACK VOLUME` — delay in 16 ms steps (1–15), feedback % (-95..95), return volume % (0–100).
  Default `echo 5 40 30`. Big halls: `echo 8 55 40`; tight rooms: `echo 3 25 20`; underwater: `echo 6 60 45`.
* `swing 60` — shuffle: % of each beat given to the first 8th (50 straight … 67 triplet swing). Written as
  plain 8ths; 16ths get uneven under swing, so use 50 for 16th-note grooves.

Channel lines: `C1:` … `C8:`; repeat a prefix on more lines to continue that channel. `#` starts a comment.
* Notes `c d e f g a b`, sharp `+` or `#`, flat `-`, optional length (1 2 4 8 16 32, triplets 3 6 12 24) and
  dots: `c4.` `f+8` `b-16`. Rest `r8`. Tie `^8` extends the previous note (`c4^16`).
* **Chords:** `{c e g}4` plays up to 3 notes at once on one channel (it uses the channel's three voices, slightly
  spread in stereo). Octave marks inside braces are local: `{g > c e}2` = G4 C5 E5, and the octave after `}` is
  unchanged. Ties work (`{c e g}2^4`). Great for pads (`@strings`, `@pad`, `@choir`, `@organ`) and comping stabs.
* `o4` octave (o4 c = middle C), `>` / `<` up/down, `l8` default length.
* `@name` instrument (also resets nothing else) · `v0-15` volume (perceptual curve) · `p-10..10` pan
  (left..right) · `q1-8` gate (fraction of the note held before release; 8 = legato) · `E1`/`E0` echo send on/off ·
  `V0-3` vibrato (off, gentle delayed, medium, strong) · `P0-9` portamento (0 off, 1 slow … 9 fast; notes that
  follow a still-held note glide without re-attacking) · `D-50..50` detune in cents (layer two channels with
  `D-6` / `D6` for chorus) · `A0-6` arpeggio (1 major, 2 minor, 3 octave, 4 fifth, 5 sus6, 6 maj+oct) ·
  `K-24..24` transpose.
* `[ ... ]n` repeat n times (nestable) · `L` loop point (the song jumps back here at the end) · `T150` tempo change.
* Whole note = 192 ticks. **Every channel of a looping song must have the same length from its `L` to its end**
  (the validator checks this). Put `L` at the same musical position in every channel.

Channel defaults: `@piano v12 p0 q7 E0 V0 P0 D0 A0 K0 o4 l4`.

## Arranging conventions (house style)
`C1` lead · `C2` counter-melody / harmony · `C3` pad or chord voice · `C4` comping (piano, marimba, guitar, pizz,
arpeggios) · `C5` bass (echo off) · `C6` kick/snare/toms · `C7` hats/shaker/hand percussion · `C8` colour (second pad
voice, bells, timpani, orchestra hits). Leads v11–13, harmony v8–10, pads v6–8, bass v11–13, drums v10–13.
Spread the stereo field: counter p-3..-5, comp/pads p+3..+5. Echo on leads, pads, bells; off on bass & kick.

## The main motif ("Hop Along" hook) — the soundtrack's leitmotif
Our own 2-bar hook (from `overworld.mml`), written in C; transpose freely:
```
bar 1:  o4 g8 o5 c8 r8 e8 r8 g8 e8 c8     G C . E . G E C      (over C)
bar 2:  o5 a4 g8 e8 ^4 c8 d8              A . G E~~ . C D      (over Am, or F, or A7)
answer: o5 f8 a8 r8 o6 c8 r8 o5 a8 g8 f8 | o5 g4 d8 e8 ^4 r8 o4 g8   (F | G)
```
It appears, reharmonised and re-orchestrated, across the game: overworld (full statement), title & credits
(grand/lyrical), every map theme (a bar or two quoted in that world's flavour and key), course clear / world clear /
ending (fanfare form), select (gentle music-box), athletic (in 3/4 waltz rhythm), underwater (slow waltz),
and in MINOR for dark places (fortress, castle, map8, bowser: `G C . E- . G E- C | A- . G E-~ . C D`).
Quote it recognisably at least once in each of those tracks; everything else stays original.

## Mario-style guide (what makes it feel like a Super Mario Bros. game)
* Bright major keys (C, F, G, B-flat); minor only for fortress/castle/boss/death/game over.
* Bouncy shuffle (`swing 58-64`) or calypso/latin grooves (bongos `z b`, congas `g f`, woodblock `i`, rim `d`,
  shaker `j`, cowbell `a`); light, tight drums — rimshot backbeats, few crashes.
* Bass: syncopated "oom-pah" (root on 1, chord stab on the off-beat, fifth on 3) or walking quarters with
  chromatic approach notes (`f f+ g`, `b- b > c`); `slapbass`, `tuba`, `bass`, `pizz`.
* Harmony: I–vi–ii–V turnarounds, secondary dominants (A7, D7, E7), and the Mario cadence bVI–bVII–I
  (A-flat – B-flat – C) at section ends; chromatic passing chords.
* Melody: short catchy 2-bar motifs, repeated then developed (A A' B A); snappy staccato leads (`q4`-`q6`) with
  rests between notes; call-and-response between lead and counter (brass stabs / steel drum / marimba answering).
* Colours: `steeldrum`, `marimba`, `xylo`, `brass` stabs (`q3`), `whistle`/`flute`/`ocarina` leads, `snaplead`,
  `glock` sparkles, `pizz`. Pads stay low in the mix (v5-7) — the band is bouncy, not washy.
* Jingles are snappy and instantly readable: clear = rising triumphant run + bVI-bVII-I, death = quick
  descending tumble, game over = slow sad cadence, 1-up/bonus = bright arpeggio, enterlevel = "let's go" sting.

## Tools
`tools\smb4tools.exe validate` — parse errors + loop-length check for all songs.
`tools\smb4tools.exe song NAME out.wav 30` — renders 30 s of a song to a stereo WAV (reports peak level).
`tools\smb4tools.exe song sfx out.wav 38` — renders every sound effect in turn (0.8 s apart).
Files in `data\` next to/above the exe are read live, so MML edits need no rebuild.

## Track list (file name -> purpose)
Loops: `title` (title screen, jaunty showtime), `select` (file select, gentle), `map1` … `map8` (one world-map theme
per world: 1 meadow, 2 desert canyon, 3 coral coast/sea, 4 jungle, 5 sky kingdom, 6 frozen peaks, 7 machine works,
8 Bowser's volcano — dark), `overworld` (main level theme — THE signature tune: catchy, bouncy, memorable),
`athletic` (sky/athletic stages, lighter & swingy), `underground` (sparse, bouncy bass + percussive lead),
`underwater` (waltz-like 3/4, flowing), `desert` (exotic scale), `snow` (glittery, bell-like), `jungle`
(bongos, syncopated), `fortress` (tense, minor, driving), `airship` (march-like, ominous, timpani),
`castle` (Bowser's castle: dark, chromatic), `boss` (Boom Boom / Koopaling fight: fast, urgent),
`bowser` (final battle, epic), `toadhouse` (cheerful, calm), `bonus` (bonus rooms/coin heaven), `star`
(invincibility: very fast, energetic, ~6 s loop), `pswitch` (hurried, ticking), `credits` (warm finale).
One-shot jingles (`loop 0`; game flow waits on some, keep their lengths): `clear` (course clear ~4 s), `death`
(player down ~3 s), `gameover` (~5 s), `hurry` (timer warning ~2 s; the level theme resumes 2.5 s later at 1.3x
speed), `fortressclear` (~4 s), `worldclear` (king rescued / world complete ~6 s), `bonus1up` (3-card bonus
fanfare ~3 s), `ending` (~20 s finale fanfare), `enterlevel` (~1 s sting), `bosswin` (boss defeated ~3 s).
