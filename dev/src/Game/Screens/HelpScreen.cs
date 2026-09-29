using System;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    /// <summary>How to play: controls, moves, power-ups (reads current bindings).</summary>
    public sealed class HelpScreen : Screen
    {
        readonly Screen back;
        int page, t;
        const int Pages = 4;
        public HelpScreen(Screen back) { this.back = back; }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (any.P(Btn.Right) || any.P(Btn.A)) { if (page < Pages - 1) { page++; Sound.Sfx(SfxId.MenuMove); } else { G.Go(back, false); Sound.Sfx(SfxId.MenuSelect); } }
            if (any.P(Btn.Left) && page > 0) { page--; Sound.Sfx(SfxId.MenuMove); }
            if (any.P(Btn.B) || any.P(Btn.Start) || G.BackPressed) { G.Go(back, false); Sound.Sfx(SfxId.MenuBack); }
        }

        static string Keys(Act a)
        {
            var ks = Host.Settings.Bind.Keys[(int)a];
            return ks.Length == 0 ? "-" : InputSystem.KeyName(ks[0]);
        }
        static string Pad(Act a)
        {
            var ks = Host.Settings.Bind.XBtn[(int)a];
            return ks.Length == 0 ? "-" : InputSystem.XButtonName(ks[0]);
        }

        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.Backdrop(ppu, t);
            Hud.Window(ppu, 8, 8, 240, 224);
            string[] titles = { "CONTROLS", "MOVES", "POWER-UPS", "THE WORLD MAP" };
            Hud.TxtC(ppu, titles[page], 18, 0x28);
            int y0 = 36;
            switch (page)
            {
                case 0:
                    // three columns: action (x=20), keyboard (x=116), controller (x=196, 5 characters)
                    Row(ppu, y0, "", "KEYS", "PAD");
                    Row(ppu, y0 + 14, "MOVE", "ARROWS", "D-PAD");
                    Row(ppu, y0 + 26, "JUMP", Keys(Act.Jump), Pad(Act.Jump));
                    Row(ppu, y0 + 38, "RUN/FIRE", Keys(Act.Run), Pad(Act.Run));
                    Row(ppu, y0 + 50, "PAUSE", Keys(Act.Start), Pad(Act.Start));
                    Row(ppu, y0 + 62, "ITEMS", Keys(Act.Select), Pad(Act.Select));
                    // lines starting at x=20 fit 27 characters inside the window
                    Hud.Txt(ppu, "WASD AND STICK MOVE TOO.", 20, y0 + 80, 0x10);
                    Hud.Txt(ppu, "F11, ALT+ENTER: FULLSCREEN", 20, y0 + 94, 0x10);
                    Hud.Txt(ppu, "F12: SCREENSHOT  ESC: BACK", 20, y0 + 106, 0x10);
                    Hud.Txt(ppu, "REBIND EVERYTHING IN", 20, y0 + 124, 0x21);
                    Hud.Txt(ppu, "OPTIONS > CONTROLS.", 20, y0 + 136, 0x21);
                    Hud.Txt(ppu, "XBOX, PLAYSTATION, SWITCH", 20, y0 + 154, 0x2A);
                    Hud.Txt(ppu, "PRO AND USB PADS ALL WORK.", 20, y0 + 166, 0x2A);
                    break;
                case 1:
                    Lines(ppu, y0, new[]
                    {
                        "HOLD RUN TO GO FASTER. RUN",
                        "FOR A WHILE TO FILL THE",
                        "P-METER FOR P-SPEED.",
                        "HOLD JUMP TO JUMP HIGHER;",
                        "TAP IT FOR A SHORT HOP.",
                        "PRESS THE OTHER WAY TO SKID",
                        "AND TURN AROUND QUICKLY.",
                        "STOMP ENEMIES - HOLD JUMP",
                        "TO BOUNCE HIGHER.",
                        "HOLD RUN TO PICK UP SHELLS,",
                        "LET GO TO KICK THEM.",
                        "DOWN: DUCK, SLIDE DOWN",
                        "SLOPES AND ENTER PIPES.",
                        "UP: DOORS AND VINES.",
                        "IN WATER, TAP JUMP TO SWIM;",
                        "UP+JUMP LEAPS OUT OF IT.",
                    }, 11);
                    break;
                case 2:
                    // text starting at x=40 fits 25 characters inside the window
                    ppu.Spr(Art.Get("mushroom"), 20, y0, Art.Pal("mushroom")); Hud.Txt(ppu, "SUPER MUSHROOM: GROW BIG.", 40, y0 + 4, 0x30);
                    ppu.Spr(Art.Get("flower.1"), 20, y0 + 20, Art.Pal("flower")); Hud.Txt(ppu, "FIRE FLOWER: THE RUN", 40, y0 + 20, 0x30); Hud.Txt(ppu, "BUTTON THROWS FIREBALLS.", 40, y0 + 30, 0x10);
                    ppu.Spr(Art.Get("leaf"), 20, y0 + 48, Art.Pal("leaf")); Hud.Txt(ppu, "SUPER LEAF: RUN BUTTON", 40, y0 + 46, 0x30);
                    Hud.Txt(ppu, "SPINS YOUR TAIL. TAP JUMP", 40, y0 + 56, 0x10); Hud.Txt(ppu, "IN THE AIR TO FLOAT. WITH", 40, y0 + 66, 0x10); Hud.Txt(ppu, "A FULL P-METER, FLY!", 40, y0 + 76, 0x10);
                    ppu.Spr(Art.Get("star"), 20, y0 + 92, Art.Pal("star")); Hud.Txt(ppu, "STARMAN: INVINCIBLE!", 40, y0 + 96, 0x30);
                    Hud.Txt(ppu, "GET HIT AS A SUIT AND YOU", 20, y0 + 116, 0x21); Hud.Txt(ppu, "DROP BACK TO SUPER MARIO.", 20, y0 + 126, 0x21);
                    Hud.Txt(ppu, "GRAB THE GOAL CARD AT THE", 20, y0 + 142, 0x2A); Hud.Txt(ppu, "END. 3 CARDS = BONUS LIVES!", 20, y0 + 152, 0x2A);
                    Hud.Txt(ppu, "100 COINS = 1UP.", 20, y0 + 166, 0x2A);
                    break;
                case 3:
                    Lines(ppu, y0, new[]
                    {
                        "WALK THE PATHS WITH THE",
                        "D-PAD; PRESS JUMP TO ENTER.",
                        "CLEAR A LEVEL TO PASS IT.",
                        "",
                        "TOAD HOUSES GIVE ITEMS. USE",
                        "THEM WITH RUN OR SELECT.",
                        "FORTRESSES OPEN LOCKS, AND",
                        "EACH AIRSHIP HIDES A",
                        "KOOPALING.",
                        "",
                        "BEAT A WANDERING HAMMER BRO",
                        "FOR A SPECIAL PRIZE. EVERY",
                        "80,000 POINTS, AN N-SPADE",
                        "CARD GAME APPEARS.",
                        "",
                        "START: SAVE, OPTIONS AND",
                        "WORLD SELECT.",
                    }, 10);
                    break;
            }
            Hud.Txt(ppu, (page + 1) + "/" + Pages, 208, 214, 0x10);
            Hud.Txt(ppu, page < Pages - 1 ? "A/RIGHT: NEXT" : "A: DONE", 20, 214, (t / 20) % 2 == 0 ? 0x30 : 0x10);
        }

        static void Row(Ppu ppu, int y, string a, string b, string c)
        {
            Hud.Txt(ppu, a, 20, y, 0x30);
            Hud.Txt(ppu, b, 116, y, 0x28);
            Hud.Txt(ppu, c, 196, y, 0x2A);
        }

        static void Lines(Ppu ppu, int y, string[] lines, int spacing = 12)
        {
            for (int i = 0; i < lines.Length; i++) Hud.Txt(ppu, lines[i], 20, y + i * spacing, 0x30);
        }
    }
}
