using System;
using System.Collections.Generic;

namespace SMB4.Engine
{
    /// <summary>An indexed image (values 0..15 index a palette of up to 16 entries; 0 = transparent).</summary>
    public sealed class Img
    {
        public readonly int W, H;
        public readonly byte[] P;
        public Img(int w, int h) { W = w; H = h; P = new byte[w * h]; }
        public Img(int w, int h, byte[] p) { W = w; H = h; P = p; }
    }

    /// <summary>
    /// Global color table. Indices 0..63 are the NES master palette (so code-level NES constants keep working);
    /// art files can also name any 24-bit color (#RRGGBB), registered here on demand (SNES-class color).
    /// </summary>
    public static class NesPalette
    {
        /// <summary>The widely used 2C02 reference palette (RGB).</summary>
        public static readonly int[] Rgb =
        {
            0x7C7C7C,0x0000FC,0x0000BC,0x4428BC,0x940084,0xA80020,0xA81000,0x881400,0x503000,0x007800,0x006800,0x005800,0x004058,0x000000,0x000000,0x000000,
            0xBCBCBC,0x0078F8,0x0058F8,0x6844FC,0xD800CC,0xE40058,0xF83800,0xE45C10,0xAC7C00,0x00B800,0x00A800,0x00A844,0x008888,0x000000,0x000000,0x000000,
            0xF8F8F8,0x3CBCFC,0x6888FC,0x9878F8,0xF878F8,0xF85898,0xF87858,0xFCA044,0xF8B800,0xB8F818,0x58D854,0x58F898,0x00E8D8,0x787878,0x000000,0x000000,
            0xFCFCFC,0xA4E4FC,0xB8B8F8,0xD8B8F8,0xF8B8F8,0xF8A4C0,0xF0D0B0,0xFCE0A8,0xF8D878,0xD8F878,0xB8F8B8,0xB8F8D8,0x00FCFC,0xF8D8F8,0x000000,0x000000,
        };

        static readonly List<int> table = new List<int>(Rgb);
        static readonly Dictionary<int, int> byRgb = new Dictionary<int, int>();
        static int[] lutCache; static int lutFade = -1, lutCount = -1; static bool lutGray;

        /// <summary>Number of colors in the global table (64 NES + registered RGB colors).</summary>
        public static int Count { get { return table.Count; } }

        /// <summary>Registers a 24-bit color (0xRRGGBB) and returns its color index (>= 64).</summary>
        public static int Color(int rgb)
        {
            rgb &= 0xFFFFFF;
            int i;
            if (byRgb.TryGetValue(rgb, out i)) return i;
            if (table.Count >= 65535) return 0x0F;
            i = table.Count; table.Add(rgb); byRgb[rgb] = i;
            return i;
        }

        /// <summary>RGB of a color index.</summary>
        public static int RgbOf(int idx)
        {
            if (idx >= 0 && idx < 64) { int c = idx; if ((c & 0x0F) >= 0x0E) c = 0x0F; return Rgb[c]; }
            return idx >= 0 && idx < table.Count ? table[idx] : 0;
        }

        /// <summary>ARGB of one color index at a fade level (tools; the game uses Lut).</summary>
        public static int Argb(int idx, int fade) { var l = Lut(fade, false); return idx < l.Length ? l[idx] : unchecked((int)0xFF000000); }

        /// <summary>Cached ARGB lookup covering every registered color.</summary>
        public static int[] Lut(int fade, bool grayscale)
        {
            if (lutCache != null && fade == lutFade && grayscale == lutGray && lutCount == table.Count) return lutCache;
            var lut = new int[Math.Max(64, table.Count)];
            BuildLut(lut, fade, grayscale);
            lutCache = lut; lutFade = fade; lutGray = grayscale; lutCount = table.Count;
            return lut;
        }

        /// <summary>ARGB lookup; fade 0..4 darkens (NES colors NES-style, RGB colors by scaling).</summary>
        public static void BuildLut(int[] lut, int fade, bool grayscale)
        {
            int n = Math.Min(lut.Length, table.Count);
            for (int i = 0; i < n; i++)
            {
                int rgb;
                if (i < 64)
                {
                    int c = i;
                    if (grayscale) c &= 0x30;
                    if (fade > 0)
                    {
                        int hi = (c >> 4) - fade;
                        c = hi < 0 ? 0x0F : (hi << 4) | (c & 0x0F);
                    }
                    if ((c & 0x0F) >= 0x0E) c = 0x0F;
                    rgb = Rgb[c];
                }
                else
                {
                    rgb = table[i];
                    int r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
                    if (grayscale) { int y = (r * 77 + g * 150 + b * 29) >> 8; r = g = b = y; }
                    if (fade > 0) { int k = Math.Max(0, 4 - fade); r = r * k / 4; g = g * k / 4; b = b * k / 4; }
                    rgb = (r << 16) | (g << 8) | b;
                }
                lut[i] = unchecked((int)0xFF000000) | rgb;
            }
        }
    }

    /// <summary>
    /// Software "virtual PPU": a 256x240 frame of color indices plus a background-opacity mask so
    /// sprites flagged "behind" only show where the foreground tiles are transparent (pipe entry, white-block trick).
    /// </summary>
    public sealed class Ppu
    {
        public const int W = 256, H = 240;
        public readonly ushort[] Px = new ushort[W * H];
        public readonly byte[] Bg = new byte[W * H];
        public int ClipTop = 0, ClipBottom = H;     // sprites/tiles are clipped to [ClipTop, ClipBottom)
        public int ClipLeft = 0, ClipRight = W;
        public int Fade;                              // 0..4
        public bool Grayscale;

        public void Clear(int color)
        {
            ushort c = (ushort)color;
            for (int i = 0; i < Px.Length; i++) { Px[i] = c; Bg[i] = 0; }
        }

        public void ResetClip() { ClipTop = 0; ClipBottom = H; ClipLeft = 0; ClipRight = W; }

        /// <summary>Resets the foreground-opacity mask (must run every frame before tiles are drawn, or "behind"
        /// sprites stay hidden wherever a tile was drawn on an earlier frame).</summary>
        public void ClearMask() { Array.Clear(Bg, 0, Bg.Length); }

        public void FillRect(int x, int y, int w, int h, int color)
        {
            int x0 = Math.Max(x, ClipLeft), y0 = Math.Max(y, ClipTop), x1 = Math.Min(x + w, ClipRight), y1 = Math.Min(y + h, ClipBottom);
            ushort c = (ushort)color;
            for (int yy = y0; yy < y1; yy++) { int o = yy * W; for (int xx = x0; xx < x1; xx++) Px[o + xx] = c; }
        }

        /// <summary>Foreground tile draw: writes pixels with value != 0 and marks them opaque in the BG mask.</summary>
        public void Tile(Img img, int x, int y, ushort[] pal, bool flipH = false, bool flipV = false)
        {
            Blit(img, x, y, pal, flipH, flipV, false, true);
        }

        /// <summary>
        /// Scenery draw (parallax layers, decor, water body): writes pixels but does NOT mark them opaque, so
        /// "behind" sprites (piranhas in pipes, emerging items) stay visible in front of scenery.
        /// </summary>
        public void Back(Img img, int x, int y, ushort[] pal, bool flipH = false, bool flipV = false)
        {
            Blit(img, x, y, pal, flipH, flipV, false, false);
        }

        /// <summary>Sprite draw. behind = only visible where the foreground tiles are transparent.</summary>
        public void Spr(Img img, int x, int y, ushort[] pal, bool flipH = false, bool flipV = false, bool behind = false)
        {
            Blit(img, x, y, pal, flipH, flipV, behind, false);
        }

        /// <summary>Sprite draw using palTop for image rows [0, split) and palBottom for the rest (two-palette sprites).</summary>
        public void SprSplit(Img img, int x, int y, ushort[] palTop, ushort[] palBottom, int split, bool flipH = false, bool flipV = false)
        {
            int ct = ClipTop, cb = ClipBottom;
            int edge = flipV ? y + img.H - split : y + split;
            ClipBottom = Math.Min(cb, edge);
            Blit(img, x, y, flipV ? palBottom : palTop, flipH, flipV, false, false);
            ClipBottom = cb; ClipTop = Math.Max(ct, edge);
            Blit(img, x, y, flipV ? palTop : palBottom, flipH, flipV, false, false);
            ClipTop = ct;
        }

        void Blit(Img img, int x, int y, ushort[] pal, bool fh, bool fv, bool behind, bool bg)
        {
            int w = img.W, h = img.H;
            int sx0 = 0, sy0 = 0;
            int dx0 = x, dy0 = y;
            if (dx0 < ClipLeft) { sx0 = ClipLeft - dx0; dx0 = ClipLeft; }
            if (dy0 < ClipTop) { sy0 = ClipTop - dy0; dy0 = ClipTop; }
            int dx1 = Math.Min(x + w, ClipRight), dy1 = Math.Min(y + h, ClipBottom);
            if (dx0 >= dx1 || dy0 >= dy1) return;
            byte[] p = img.P;
            int last = pal.Length - 1;
            for (int dy = dy0, syi = sy0; dy < dy1; dy++, syi++)
            {
                int sy = fv ? h - 1 - syi : syi;
                int srow = sy * w;
                int drow = dy * W;
                for (int dx = dx0, sxi = sx0; dx < dx1; dx++, sxi++)
                {
                    int v = p[srow + (fh ? w - 1 - sxi : sxi)];
                    if (v == 0) continue;
                    int o = drow + dx;
                    if (behind && Bg[o] != 0) continue;
                    Px[o] = pal[v <= last ? v : last];
                    if (bg) Bg[o] = 1;
                }
            }
        }

        /// <summary>Draws an image with every opaque pixel in one color (silhouettes, flashes).</summary>
        public void SprSolid(Img img, int x, int y, int color, bool flipH = false)
        {
            int w = img.W, h = img.H;
            for (int yy = 0; yy < h; yy++)
            {
                int dy = y + yy; if (dy < ClipTop || dy >= ClipBottom) continue;
                for (int xx = 0; xx < w; xx++)
                {
                    int dx = x + xx; if (dx < ClipLeft || dx >= ClipRight) continue;
                    if (img.P[yy * w + (flipH ? w - 1 - xx : xx)] != 0) Px[dy * W + dx] = (ushort)color;
                }
            }
        }

        // ------------------------------------------------------------------ text (8x8 font, see Font.cs)
        public void Text(string s, int x, int y, int color)
        {
            int cx = x;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\n') { cx = x; y += 8; continue; }
                Img g = Font.Glyph(ch);
                if (g != null) SprSolid(g, cx, y, color);
                cx += 8;
            }
        }

        public void TextShadow(string s, int x, int y, int color, int shadow)
        {
            Text(s, x + 1, y + 1, shadow);
            Text(s, x, y, color);
        }

        public void TextCentered(string s, int y, int color) { Text(s, (W - s.Length * 8) / 2, y, color); }
    }
}
