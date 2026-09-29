using System;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    /// <summary>Theater-curtain title screen (SMB3 homage): curtain rises on the logo and menu.</summary>
    public sealed class TitleScreen : Screen
    {
        int t, sel, curtainY;
        readonly MenuNav nav = new MenuNav();
        static readonly string[] Items = { "1 PLAYER GAME", "2 PLAYER GAME", "HOW TO PLAY", "OPTIONS", "QUIT" };
        // attract actors
        int marioX = -24, marioY, marioVy, goombaX = 290;   // marioY / marioVy in 1/16 px (SMB3 units)

        public override void Enter()
        {
            t = 0; curtainY = 0;
            Sound.Music("title", true);
        }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (t > 50 && curtainY > -200) curtainY -= 3;
            // attract animation
            marioX += 2;
            if (marioX > 300) { marioX = -40; goombaX = 290; }
            goombaX -= 1;
            if (marioY == 0 && Math.Abs((marioX + 8) - (goombaX + 8)) < 40 && marioX < goombaX) { marioVy = -0x3C; }
            if (marioY < 0 || marioVy < 0) { marioY += marioVy; marioVy += 4; if (marioVy > 0x40) marioVy = 0x40; if (marioY >= 0) { marioY = 0; marioVy = 0; } }
            if (Math.Abs(marioX - goombaX) < 12 && marioY < -64 && marioVy > 0) { goombaX = 400; marioVy = -0x30; Sound.Sfx(SfxId.Stomp); }

            if (curtainY > -200 && (any.P(Btn.Start) || any.P(Btn.A))) { curtainY = -200; t = 60; return; }
            if (curtainY > -200) return;
            int v = nav.Vertical(any);
            if (v != 0) { sel = (sel + v + Items.Length) % Items.Length; Sound.Sfx(SfxId.MenuMove); }
            if (any.P(Btn.Start) || any.P(Btn.A))
            {
                Sound.Sfx(SfxId.MenuSelect);
                switch (sel)
                {
                    case 0: G.Go(new FileSelectScreen(false)); break;
                    case 1: G.Go(new FileSelectScreen(true)); break;
                    case 2: G.Go(new HelpScreen(this), false); break;
                    case 3: G.Go(new OptionsScreen(this), false); break;
                    case 4: G.Quit(); break;
                }
            }
            if (G.BackPressed) sel = 4;
        }

        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Stage(ppu, t);
            // logo: extruded, outlined gradient lettering + a big gold "4"
            int[] red = { 0xFFD8B0, 0xFF9060, 0xF84828, 0xD01818, 0x901010 };
            int[] gold = { 0xFFFFE0, 0xFFF080, 0xF8C830, 0xE08818, 0xA05008 };
            BigText(ppu, "SUPER MARIO", 118, 24, 2, red, 0x180408, 0x5A0818, 2);
            BigText(ppu, "BROS.", 96, 48, 2, red, 0x180408, 0x5A0818, 2);
            BigText(ppu, "4", 178, 44, 5, gold, 0x200C00, 0x7A3000, 3);
            // twinkle on the 4
            int tw = (t / 6) % 24;
            if (tw < 4) Sparkle(ppu, 193, 50, tw);
            // subtitle ribbon with the lantern motif
            Ribbon(ppu, 60, 88, 136, 13);
            Hud.GradText(ppu, "THE LANTERN TOUR", 64, 91, Hud.C(0xFFFFFF), Hud.C(0xFFF0C0), Hud.C(0xF8D080), Hud.C(0x401000));
            Lantern.Draw(ppu, 38, 78, true, t);
            Lantern.Draw(ppu, 202, 78, true, t + 11);
            // actors
            var goom = Art.Get((t / 8) % 2 == 0 ? "goomba.1" : "goomba.2");
            if (goombaX < 300) ppu.Spr(goom, goombaX, 176, Art.Pal("goomba"));
            string mf = marioY < -16 ? "mb.jump" : new[] { "mb.walk1", "mb.walk2", "mb.stand", "mb.walk2" }[(t / 4) % 4];
            Player.Body(ppu, Art.Get(mf), marioX, 160 + (marioY >> 4), 32, Art.Pal("mario"), false, false, false);
            // menu
            if (curtainY <= -200)
            {
                Hud.Panel(ppu, 56, 103, 144, 62, 0x283070, 0x101438, 0x6070C0, 0x080820, 0x04040C);
                for (int i = 0; i < Items.Length; i++)
                {
                    int y = 108 + i * 11;
                    if (i == sel)
                    {
                        Hud.VGrad(ppu, 59, y - 2, 138, 11, 0x5068D0, 0x283888);
                        Hud.Txt(ppu, Items[i], 76, y, 0x30);
                        Hud.Cursor(ppu, 64, y, t);
                    }
                    else Hud.GradText(ppu, Items[i], 76, y, Hud.C(0xC8D0F0), Hud.C(0xA0A8D0), Hud.C(0x8088B8), Hud.C(0x04040C));
                }
                Hud.Panel(ppu, 48, 214, 160, 14, 0x282020, 0x100808, 0x584848, 0x080404, 0x000000);
                Hud.GradText(ppu, "A FAN-MADE TRIBUTE", 56, 217, Hud.C(0xFFF0C0), Hud.C(0xE8C890), Hud.C(0xB09060), Hud.C(0x000000));
            }
            // curtain
            if (curtainY > -200) Curtain(ppu, curtainY, t);
        }

        // ------------------------------------------------------------------ stage set
        static void Stage(Ppu ppu, int t)
        {
            // backdrop: indigo gradient with a soft spotlight pool behind the logo
            for (int y = 0; y < 192; y += 2)
            {
                int baseRgb = MixRgb(0x0C0830, 0x301858, y * 256 / 192);
                int x = 0;
                while (x < 256)
                {
                    int lv = SpotLevel(x, y), x2 = x + 1;
                    while (x2 < 256 && SpotLevel(x2, y) == lv) x2++;
                    ppu.FillRect(x, y, x2 - x, 2, Hud.Mix(baseRgb, 0x7858C0, lv * 22));
                    x = x2;
                }
            }
            // faint stars in the backdrop cloth
            for (int i = 0; i < 28; i++)
            {
                int x = (i * 89 + 17) % 220 + 18, y = (i * 47 + 5) % 150 + 16;
                if ((i + t / 20) % 5 == 0) continue;
                ppu.FillRect(x, y, 1, 1, Hud.C(i % 3 == 0 ? 0xFFF0C0 : 0x9888D8));
            }
            // side drapes (velvet folds) and the valance
            for (int x = 0; x < 20; x++) Drape(ppu, x, 192, x);
            for (int x = 236; x < 256; x++) Drape(ppu, x, 192, 255 - x);
            for (int x = 0; x < 256; x++)
            {
                int fold = x % 16;
                int h = 10 + (int)(Math.Sin(x * Math.PI / 16) * 2 + 2);
                ppu.FillRect(x, 0, 1, h, Velvet(fold < 8 ? fold : 15 - fold, 1));
                ppu.FillRect(x, h, 1, 1, Hud.C(0x300008));
            }
            for (int x = 0; x < 256; x += 3)
            {
                int h = 10 + (int)(Math.Sin(x * Math.PI / 16) * 2 + 2);
                ppu.FillRect(x, h - 1, 2, 2, Hud.C(0xF8C838));
                ppu.FillRect(x, h + 1, 2, 1, Hud.C(0xA06010));
            }
            // footlights and a lacquered checkerboard floor (brighter toward the audience)
            ppu.FillRect(0, 188, 256, 4, Hud.C(0x401808));
            ppu.FillRect(0, 188, 256, 1, Hud.C(0xE8A040));
            for (int x = 8; x < 256; x += 24)
            {
                ppu.FillRect(x, 189, 8, 2, Hud.C((t / 10 + x) % 3 == 0 ? 0xFFF8C0 : 0xF8D060));
                ppu.FillRect(x + 1, 191, 6, 1, Hud.C(0xC08020));
            }
            for (int y = 192; y < 240; y += 8)
            {
                int k = (y - 192) * 256 / 48;
                int lite = Hud.Mix(0xB8B0C8, 0xFFF8F0, k), dark = Hud.Mix(0x100818, 0x302838, k);
                for (int x = 0; x < 256; x += 8)
                {
                    bool w = ((x + y) / 8) % 2 == 0;
                    ppu.FillRect(x, y, 8, 8, w ? lite : dark);
                    ppu.FillRect(x, y, 8, 1, w ? Hud.C(0xFFFFFF) : Hud.Mix(0x403850, 0x605870, k));
                }
            }
        }

        static int MixRgb(int a, int b, int t)
        {
            int r = ((a >> 16 & 255) * (256 - t) + (b >> 16 & 255) * t) >> 8;
            int g = ((a >> 8 & 255) * (256 - t) + (b >> 8 & 255) * t) >> 8;
            int bl = ((a & 255) * (256 - t) + (b & 255) * t) >> 8;
            return r << 16 | g << 8 | bl;
        }

        static int SpotLevel(int x, int y)
        {
            int dx = x - 128, dy = (y - 60) * 2;
            int d = (dx * dx + dy * dy) / 400;
            return Math.Max(0, 5 - d / 6);
        }

        /// <summary>Velvet shade for a fold position 0..7 (0 = deep crease, 7 = lit crest); lift brightens.</summary>
        static int Velvet(int f, int lift)
        {
            int[] ramp = { 0x400010, 0x680818, 0x901020, 0xB81828, 0xD82838, 0xF04048, 0xF86060, 0xFF8878 };
            return Hud.C(ramp[Math.Max(0, Math.Min(7, f - 1 + lift))]);
        }

        static void Drape(Ppu ppu, int x, int h, int fromEdge)
        {
            int fold = (fromEdge * 3) % 12;
            int f = fold < 6 ? fold + 1 : 12 - fold;
            ppu.FillRect(x, 0, 1, h, Velvet(f, fromEdge > 12 ? -1 : 0));
        }

        static void Sparkle(Ppu ppu, int x, int y, int k)
        {
            int c = Hud.C(0xFFFFFF), r = k < 2 ? k + 1 : 4 - k;
            ppu.FillRect(x - r, y, r * 2 + 1, 1, c);
            ppu.FillRect(x, y - r, 1, r * 2 + 1, c);
        }

        static void Ribbon(Ppu ppu, int x, int y, int w, int h)
        {
            // folded ends
            ppu.FillRect(x - 8, y + 3, 10, h, Hud.C(0x701010));
            ppu.FillRect(x + w - 2, y + 3, 10, h, Hud.C(0x701010));
            ppu.FillRect(x - 8, y + 3 + h / 2, 3, 1, Hud.C(0x300008));
            ppu.FillRect(x + w + 5, y + 3 + h / 2, 3, 1, Hud.C(0x300008));
            Hud.Panel(ppu, x, y, w, h + 1, 0xF05050, 0xA01820, 0xFF9090, 0x500810, 0x280408);
        }

        public static void Curtain(Ppu ppu, int y, int t)
        {
            int bottom = 200 + y;
            if (bottom <= 0) return;
            for (int x = 0; x < 256; x++)
            {
                int fold = x % 32;
                int f = fold < 16 ? fold / 2 : (31 - fold) / 2;
                int wave = (int)(Math.Sin((x + t) * 0.2) * 2);
                int h = Math.Max(0, bottom + wave);
                // velvet ramp across the fold, lit a little more near the hem (footlights)
                ppu.FillRect(x, 0, 1, h, Velvet(f, 0));
                if (h > 40) ppu.FillRect(x, h - 40, 1, 40, Velvet(f, 1));
            }
            // gold tassel fringe
            for (int x = 0; x < 256; x += 4)
            {
                int by = Math.Max(0, bottom - 5);
                ppu.FillRect(x, by, 3, 5, Hud.C(0xE8A828));
                ppu.FillRect(x, by, 1, 5, Hud.C(0xFFE890));
                ppu.FillRect(x + 2, by + 1, 1, 4, Hud.C(0x905008));
            }
            if (y == 0) Hud.Txt(ppu, "PRESS START", 84, 100, 0x30);
        }

        /// <summary>Scaled logo lettering: a solid extruded shadow, a dark outline and a vertical gradient fill.</summary>
        public static void BigText(Ppu ppu, string s, int cx, int y, int scale, int[] ramp, int outline, int extrude, int depth)
        {
            int w = s.Length * 8 * scale;
            int x0 = cx - w / 2;
            int o = Hud.C(outline), e = Hud.C(extrude);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    var g = Font.Glyph(s[i]);
                    if (g == null) continue;
                    for (int gy = 0; gy < 8; gy++)
                        for (int gx = 0; gx < 8; gx++)
                        {
                            if (g.P[gy * 8 + gx] == 0) continue;
                            int px = x0 + (i * 8 + gx) * scale, py = y + gy * scale;
                            if (pass == 0) ppu.FillRect(px - 1 + depth, py - 1 + depth, scale + 2, scale + 2, o);
                            else if (pass == 1)
                            {
                                for (int d = depth; d >= 1; d--) ppu.FillRect(px + d, py + d, scale, scale, e);
                                ppu.FillRect(px - 1, py - 1, scale + 2, scale + 2, o);
                            }
                            else
                            {
                                for (int r = 0; r < scale; r++)
                                {
                                    int k = (gy * scale + r) * (ramp.Length - 1) * 256 / (8 * scale - 1);
                                    int a = Math.Min(ramp.Length - 2, k / 256);
                                    ppu.FillRect(px, py + r, scale, 1, Hud.Mix(ramp[a], ramp[a + 1], Math.Min(256, k - a * 256)));
                                }
                            }
                        }
                }
            }
            // specular glints: the top-left pixel of each glyph's first row
            for (int i = 0; i < s.Length; i++)
            {
                var g = Font.Glyph(s[i]);
                if (g == null) continue;
                for (int gy = 0; gy < 8; gy++)
                {
                    int gx = 0;
                    while (gx < 8 && g.P[gy * 8 + gx] == 0) gx++;
                    if (gx < 8) { ppu.FillRect(x0 + (i * 8 + gx) * scale, y + gy * scale, 1, scale, Hud.C(0xFFFFFF)); break; }
                }
            }
        }
    }
}
