using System;
using System.Collections.Generic;
using SMB4.Engine;

namespace SMB4.Frontend
{
    /// <summary>
    /// The 16-bit menu look of the original game (gold-rimmed blue windows, two-tone drop-shadowed text, arrow
    /// cursor) drawn into an Engine.Ppu. Port of the UI kit in src/Game/Hud.cs, which the player can't reference.
    /// </summary>
    public static class Ui
    {
        public const int Transparent = 0xFFFF;
        static readonly Dictionary<long, int> mixCache = new Dictionary<long, int>();

        public static int C(int rgb) { return NesPalette.Color(rgb); }

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

        static int Shade(int colorIdx, int amt)
        {
            int rgb = NesPalette.RgbOf(colorIdx);
            return amt >= 0 ? Mix(rgb, 0xFFFFFF, amt) : Mix(rgb, 0x000000, -amt);
        }

        public static void VGrad(Ppu ppu, int x, int y, int w, int h, int top, int bottom, int band = 1)
        {
            for (int yy = 0; yy < h; yy += band)
            {
                int t = h <= 1 ? 0 : yy * 256 / (h - 1);
                ppu.FillRect(x, y + yy, w, Math.Min(band, h - yy), Mix(top, bottom, Math.Min(256, t)));
            }
        }

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

        /// <summary>A bevelled window with a gold rim and a deep-blue gradient face.</summary>
        public static void Window(Ppu ppu, int x, int y, int w, int h)
        {
            ppu.FillRect(x + 3, y + h, w - 2, 2, C(0x04040A));
            ppu.FillRect(x + w, y + 3, 2, h - 1, C(0x04040A));
            Panel(ppu, x, y, w, h, 0xF0C040, 0xB07018, 0xFFF0A0, 0x603008, 0x180800);
            int ix = x + 3, iy = y + 3, iw = w - 6, ih = h - 6;
            VGrad(ppu, ix, iy, iw, ih, 0x2840A8, 0x0C1438, 2);
            ppu.FillRect(ix, iy, iw, 1, C(0x301800));
            ppu.FillRect(ix, iy, 1, ih, C(0x301800));
            ppu.FillRect(ix + 1, iy + 1, iw - 1, 1, C(0x5070D8));
            ppu.FillRect(ix, iy + ih - 1, iw, 1, C(0xFFE080));
            ppu.FillRect(ix + iw - 1, iy, 1, ih, C(0xFFE080));
        }

        /// <summary>Two-tone 16-bit text with a dark drop shadow. color is a palette index (NES 0..63 or C(rgb)).</summary>
        public static void Txt(Ppu ppu, string s, int x, int y, int color)
        {
            int rgb = NesPalette.RgbOf(color);
            int lum = ((rgb >> 16 & 255) * 3 + (rgb >> 8 & 255) * 6 + (rgb & 255)) / 10;
            int top = Shade(color, 90), bot = Shade(color, -40);
            int shadow = lum < 60 ? C(0x000000) : C(0x100818);
            ppu.Text(s, x + 1, y + 1, shadow);
            ppu.Text(s, x, y + 1, shadow);
            int cx = x;
            for (int i = 0; i < s.Length; i++)
            {
                var g = Font.Glyph(s[i]);
                if (g != null)
                    for (int gy = 0; gy < 8; gy++)
                    {
                        int c = gy < 3 ? top : gy < 5 ? color : bot;
                        for (int gx = 0; gx < 8; gx++) if (g.P[gy * 8 + gx] != 0) ppu.FillRect(cx + gx, y + gy, 1, 1, c);
                    }
                cx += 8;
            }
        }

        public static void TxtC(Ppu ppu, string s, int y, int color) { Txt(ppu, s, (256 - s.Length * 8) / 2, y, color); }

        public static void Cursor(Ppu ppu, int x, int y, int t)
        {
            int dx = (t / 8) % 4 == 0 ? 1 : 0;
            int o = C(0x180800), hi = C(0xFFF8B0), mid = C(0xF8C830), lo = C(0xC06810);
            for (int r = 0; r < 7; r++) { int len = r < 4 ? r + 1 : 7 - r; ppu.FillRect(x + dx + 1, y + r + 1, len + 1, 1, o); }
            for (int r = 0; r < 7; r++) { int len = r < 4 ? r + 1 : 7 - r; ppu.FillRect(x + dx, y + r, len, 1, r < 2 ? hi : r < 4 ? mid : lo); }
        }

        /// <summary>A dark translucent-looking strip for toasts/FPS (solid; the compositor keeps game pixels elsewhere).</summary>
        public static void Strip(Ppu ppu, int x, int y, int w, int h)
        {
            ppu.FillRect(x, y, w, h, C(0x080818));
            ppu.FillRect(x, y, w, 1, C(0x5070D8));
        }
    }
}
