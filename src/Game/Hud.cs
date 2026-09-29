using System;
using System.Collections.Generic;
using SMB4.Engine;

namespace SMB4.Game
{
    /// <summary>
    /// 16-bit status bar in scanlines 192-239 (docs/04 §3.1, docs/06) plus the shared UI kit used by every menu:
    /// RGB gradients, bevelled panels, drop-shadowed two-tone text.
    /// </summary>
    public static class Hud
    {
        public const int Top = 192;

        // ------------------------------------------------------------------ colour helpers
        static readonly Dictionary<long, int> mixCache = new Dictionary<long, int>();

        /// <summary>Color index of a 24-bit RGB value.</summary>
        public static int C(int rgb) { return Art.Rgb(rgb); }

        /// <summary>Mix of two RGB colors (t = 0..256) as a color index.</summary>
        public static int Mix(int a, int b, int t)
        {
            long key = ((long)(a & 0xFFFFFF) << 33) | ((long)(b & 0xFFFFFF) << 9) | (long)t;
            int idx;
            if (mixCache.TryGetValue(key, out idx)) return idx;
            int r = ((a >> 16 & 255) * (256 - t) + (b >> 16 & 255) * t) >> 8;
            int g = ((a >> 8 & 255) * (256 - t) + (b >> 8 & 255) * t) >> 8;
            int bl = ((a & 255) * (256 - t) + (b & 255) * t) >> 8;
            idx = C(r << 16 | g << 8 | bl);
            mixCache[key] = idx;
            return idx;
        }

        /// <summary>RGB of a color index scaled toward white (amt > 0) or black (amt < 0), amt in 1/256.</summary>
        static int Shade(int colorIdx, int amt)
        {
            int rgb = NesPalette.RgbOf(colorIdx);
            return amt >= 0 ? Mix(rgb, 0xFFFFFF, amt) : Mix(rgb, 0x000000, -amt);
        }

        /// <summary>Vertical RGB gradient, one band per `band` rows.</summary>
        public static void VGrad(Ppu ppu, int x, int y, int w, int h, int top, int bottom, int band = 1)
        {
            for (int yy = 0; yy < h; yy += band)
            {
                int t = h <= 1 ? 0 : yy * 256 / (h - 1);
                ppu.FillRect(x, y + yy, w, Math.Min(band, h - yy), Mix(top, bottom, Math.Min(256, t)));
            }
        }

        /// <summary>Bevelled 16-bit panel: dark outline, rounded corners, light rim top-left, dark rim bottom-right, gradient face.</summary>
        public static void Panel(Ppu ppu, int x, int y, int w, int h, int faceTop, int faceBottom, int rimLight, int rimDark, int outline)
        {
            int o = C(outline);
            ppu.FillRect(x + 1, y, w - 2, 1, o); ppu.FillRect(x + 1, y + h - 1, w - 2, 1, o);
            ppu.FillRect(x, y + 1, 1, h - 2, o); ppu.FillRect(x + w - 1, y + 1, 1, h - 2, o);
            VGrad(ppu, x + 1, y + 1, w - 2, h - 2, faceTop, faceBottom);
            int l = C(rimLight), d = C(rimDark);
            ppu.FillRect(x + 2, y + 1, w - 4, 1, l); ppu.FillRect(x + 1, y + 2, 1, h - 4, l);
            ppu.FillRect(x + 2, y + h - 2, w - 4, 1, d); ppu.FillRect(x + w - 2, y + 2, 1, h - 4, d);
            ppu.FillRect(x + 1, y + 1, 1, 1, o); ppu.FillRect(x + w - 2, y + 1, 1, 1, o);
            ppu.FillRect(x + 1, y + h - 2, 1, 1, o); ppu.FillRect(x + w - 2, y + h - 2, 1, 1, o);
        }

        /// <summary>A recessed slot (inset bevel: dark top-left, light bottom-right).</summary>
        public static void Inset(Ppu ppu, int x, int y, int w, int h, int faceTop, int faceBottom)
        {
            VGrad(ppu, x, y, w, h, faceTop, faceBottom);
            ppu.FillRect(x, y, w, 1, C(0x080810)); ppu.FillRect(x, y, 1, h, C(0x080810));
            ppu.FillRect(x + 1, y + 1, w - 1, 1, C(0x181830)); ppu.FillRect(x + 1, y + 1, 1, h - 1, C(0x181830));
            ppu.FillRect(x, y + h - 1, w, 1, C(0x8890C0)); ppu.FillRect(x + w - 1, y, 1, h, C(0x8890C0));
        }

        // ------------------------------------------------------------------ text
        /// <summary>Two-tone 16-bit text with a dark drop shadow: light upper half, the color itself lower down.</summary>
        public static void Txt(Ppu ppu, string s, int x, int y, int color)
        {
            int rgb = NesPalette.RgbOf(color);
            int lum = ((rgb >> 16 & 255) * 3 + (rgb >> 8 & 255) * 6 + (rgb & 255)) / 10;
            int top = Shade(color, 90), bot = Shade(color, -40);
            int shadow = lum < 60 ? C(0x000000) : C(0x100818);
            GradText(ppu, s, x, y, top, C(rgb), bot, shadow);
        }

        public static void TxtC(Ppu ppu, string s, int y, int color) { Txt(ppu, s, (256 - s.Length * 8) / 2, y, color); }

        /// <summary>Text with three row-bands (rows 0-2, 3-4, 5-7) and a 1-px shadow down-right (shadow &lt; 0 = none).</summary>
        public static void GradText(Ppu ppu, string s, int x, int y, int top, int mid, int bot, int shadow)
        {
            if (shadow >= 0)
            {
                ppu.Text(s, x + 1, y + 1, shadow);
                ppu.Text(s, x, y + 1, shadow);
            }
            int cx = x;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\n') { cx = x; y += 8; continue; }
                var g = Font.Glyph(ch);
                if (g != null)
                    for (int gy = 0; gy < 8; gy++)
                    {
                        int c = gy < 3 ? top : gy < 5 ? mid : bot;
                        for (int gx = 0; gx < 8; gx++) if (g.P[gy * 8 + gx] != 0) ppu.FillRect(cx + gx, y + gy, 1, 1, c);
                    }
                cx += 8;
            }
        }

        /// <summary>Menu cursor: a small shaded arrow pointing right (8x8 cell).</summary>
        public static void Cursor(Ppu ppu, int x, int y, int t)
        {
            int dx = (t / 8) % 4 == 0 ? 1 : 0;
            int o = C(0x180800), hi = C(0xFFF8B0), mid = C(0xF8C830), lo = C(0xC06810);
            for (int r = 0; r < 7; r++)
            {
                int len = r < 4 ? r + 1 : 7 - r;
                ppu.FillRect(x + dx + 1, y + r + 1, len + 1, 1, o);
            }
            for (int r = 0; r < 7; r++)
            {
                int len = r < 4 ? r + 1 : 7 - r;
                ppu.FillRect(x + dx, y + r, len, 1, r < 2 ? hi : r < 4 ? mid : lo);
            }
        }

        // ------------------------------------------------------------------ backdrops
        /// <summary>SNES-style menu backdrop: deep blue gradient with a slowly scrolling diamond lattice.</summary>
        public static void Backdrop(Ppu ppu, int t)
        {
            VGrad(ppu, 0, 0, 256, 240, 0x182868, 0x080818, 4);
            int off = (t / 2) % 32;
            for (int y = -32; y < 240; y += 32)
                for (int x = -32; x < 256 + 32; x += 32)
                {
                    int cx = x + off, cy = y + off;
                    for (int r = 0; r < 8; r++)
                    {
                        int w = r < 4 ? r * 2 + 2 : (7 - r) * 2 + 2;
                        int yy = cy + 12 + r;
                        if (yy < 0 || yy >= 240) continue;
                        int shade = Mix(0x283C90, 0x101838, Math.Max(0, Math.Min(256, yy * 256 / 240)));
                        ppu.FillRect(cx + 16 - w / 2, yy, w, 1, shade);
                    }
                }
        }

        // ------------------------------------------------------------------ status bar
        public static void Draw(Ppu ppu, Session s, int world, int power, bool pFlash, int time, int frame, bool showTime = true)
        {
            ppu.ResetClip();
            // bar backdrop: dark slate gradient with a gold trim line on top
            VGrad(ppu, 0, Top, 256, 48, 0x28284C, 0x08080F, 2);
            ppu.FillRect(0, Top, 256, 1, C(0x000000));
            ppu.FillRect(0, Top + 1, 256, 1, C(0xE8B830));
            ppu.FillRect(0, Top + 2, 256, 1, C(0x805010));
            // main panel
            Panel(ppu, 6, Top + 5, 178, 34, 0x3048B8, 0x101848, 0x90A8F8, 0x080C30, 0x000008);
            var cur = s.Cur;
            GradText(ppu, "WORLD", 11, Top + 10, C(0xFFFFFF), C(0xD8E0F8), C(0x98A8D8), C(0x000010));
            GradText(ppu, world.ToString(), 55, Top + 10, C(0xFFF8C0), C(0xF8D040), C(0xD08818), C(0x000010));
            // P-meter: 6 arrows (warm ramp when lit) + P badge
            Inset(ppu, 68, Top + 9, 68, 10, 0x101020, 0x202040);
            for (int i = 0; i < 6; i++)
            {
                bool on = (power & (1 << i)) != 0;
                int ax = 70 + i * 8, ay = Top + 10;
                if (on)
                {
                    int hue = Mix(0xF8E040, 0xF84818, i * 256 / 5);
                    GradText(ppu, "^", ax, ay, C(0xFFFFD0), hue, Mix(NesPalette.RgbOf(hue), 0x000000, 80), -1);
                }
                else GradText(ppu, "^", ax, ay, C(0x505878), C(0x404868), C(0x303450), -1);
            }
            bool full = power >= 0x7F;
            bool pOn = full && (!pFlash || ((frame >> 3) & 1) == 0);
            if (pOn) Panel(ppu, 119, Top + 9, 17, 10, 0xFF9070, 0xC01808, 0xFFD0C0, 0x600000, 0x200000);
            else Panel(ppu, 119, Top + 9, 17, 10, 0x505870, 0x282C40, 0x707890, 0x181820, 0x000008);
            GradText(ppu, "P", 124, Top + 10, pOn ? C(0xFFFFFF) : C(0x8890A8), pOn ? C(0xFFF0E0) : C(0x707890), pOn ? C(0xF8D0C0) : C(0x606880), pOn ? C(0x500000) : -1);
            // coin counter
            GradText(ppu, "$", 144, Top + 10, C(0xFFF8C0), C(0xF8C830), C(0xB86810), C(0x000010));
            Txt(ppu, Math.Min(99, cur.Coins).ToString().PadLeft(2), 160, Top + 10, 0x30);
            // row 2: player badge, lives, score, timer
            bool lu = s.PlayerIndex == 1;
            if (lu) Panel(ppu, 9, Top + 20, 12, 11, 0x80F070, 0x188818, 0xC8FFC0, 0x084008, 0x001000);
            else Panel(ppu, 9, Top + 20, 12, 11, 0xFF8070, 0xC01818, 0xFFC8B8, 0x500000, 0x180000);
            GradText(ppu, lu ? "L" : "M", 11, Top + 22, C(0xFFFFFF), C(0xFFFFFF), C(0xE0E0E8), C(lu ? 0x084008 : 0x500000));
            Txt(ppu, "*" + Math.Min(99, cur.Lives).ToString().PadLeft(2), 22, Top + 22, 0x30);
            Txt(ppu, cur.Score.ToString().PadLeft(7, '0'), 56, Top + 22, 0x30);
            if (showTime)
            {
                GradText(ppu, "@", 128, Top + 22, C(0xFFFFFF), C(0xC8D8F8), C(0x8098D0), C(0x000010));
                bool warn = time <= 100 && time > 0 && ((frame >> 4) & 1) == 0;
                Txt(ppu, Math.Max(0, time).ToString().PadLeft(3, '0'), 144, Top + 22, warn ? C(0xF84030) : 0x30);
            }
            // cards
            for (int i = 0; i < 3; i++)
            {
                int x = 188 + i * 22;
                Panel(ppu, x, Top + 5, 20, 34, 0x3048B8, 0x101848, 0x90A8F8, 0x080C30, 0x000008);
                Inset(ppu, x + 2, Top + 12, 16, 20, 0x0C1030, 0x20306C);
                if (i < cur.Cards.Count)
                {
                    int c = cur.Cards[i];
                    ppu.Spr(Art.Get(GoalBox.CardName(c)), x + 2, Top + 14, GoalBox.CardPal(c));
                }
            }
        }

        /// <summary>A selection frame: the color with a dark 1-px halo so it reads on any background.</summary>
        public static void Box(Ppu ppu, int x, int y, int w, int h, int color)
        {
            int d = C(0x080810);
            ppu.FillRect(x - 1, y - 1, w + 2, 1, d);
            ppu.FillRect(x - 1, y + h, w + 2, 1, d);
            ppu.FillRect(x - 1, y - 1, 1, h + 2, d);
            ppu.FillRect(x + w, y - 1, 1, h + 2, d);
            ppu.FillRect(x, y, w, 1, color);
            ppu.FillRect(x, y + h - 1, w, 1, color);
            ppu.FillRect(x, y, 1, h, color);
            ppu.FillRect(x + w - 1, y, 1, h, color);
        }

        /// <summary>A bevelled window with a gold rim and a deep-blue gradient face (menus, pause, messages).</summary>
        public static void Window(Ppu ppu, int x, int y, int w, int h)
        {
            // soft drop shadow
            ppu.FillRect(x + 3, y + h, w - 2, 2, C(0x04040A));
            ppu.FillRect(x + w, y + 3, 2, h - 1, C(0x04040A));
            // gold frame (outline, bright bevel, gold body, dark bevel)
            Panel(ppu, x, y, w, h, 0xF0C040, 0xB07018, 0xFFF0A0, 0x603008, 0x180800);
            // inner blue face (recessed)
            int ix = x + 3, iy = y + 3, iw = w - 6, ih = h - 6;
            VGrad(ppu, ix, iy, iw, ih, 0x2840A8, 0x0C1438, 2);
            ppu.FillRect(ix, iy, iw, 1, C(0x301800));
            ppu.FillRect(ix, iy, 1, ih, C(0x301800));
            ppu.FillRect(ix + 1, iy + 1, iw - 1, 1, C(0x5070D8));
            ppu.FillRect(ix, iy + ih - 1, iw, 1, C(0xFFE080));
            ppu.FillRect(ix + iw - 1, iy, 1, ih, C(0xFFE080));
        }
    }
}
