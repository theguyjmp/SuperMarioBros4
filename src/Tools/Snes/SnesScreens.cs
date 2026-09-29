using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using SMB4.Engine;
using SMB4.Game;

namespace SMB4.Tools
{
    public static partial class SnesExport
    {
        /// <summary>Export step: screens (title, file select, maps, bonus games, ending...) — screens agent.</summary>
        static int ExportScreens(string outDir) { return SnesScreens.Export(outDir); }
    }

    /// <summary>
    /// Converts every non-level screen of the C# game into SNES data by drawing it with the game's own code
    /// (TitleScreen/Hud/MapScreen/... through reflection where the helpers are private) and converting the pictures:
    ///   BG1/BG2 = pre-rendered 4bpp layers sharing one tile pool per scene (VRAM $0000, maps at $3400/$3800),
    ///   BG3 = a 2bpp pixel canvas for text (VRAM $4000, drawn at runtime by scr_text.s with the font below),
    ///   OBJ = the scene's sprite set (VRAM $6000), color-0 / lattice gradients = HDMA tables.
    /// Output: gen/scr_data.s + gen/scr_*.bin (BANK64-79 + tables in CODE7), gen/scr_ids.inc.
    /// </summary>
    static class SnesScreens
    {
        const int ORG = 64;                 // sprite images are drawn with their anchor at (ORG, ORG)
        /// <summary>Bump when snes/src/scr_*.s start needing new generated symbols (scr.inc checks it).</summary>
        const int ScrVersion = 2;
        static ScrAsm A;
        static string OutDir;
        static readonly List<string> spriteIds = new List<string>();
        static readonly List<string> mapIds = new List<string>();
        static readonly StringBuilder mapTab = new StringBuilder();
        static readonly StringBuilder sceneTab = new StringBuilder();
        static readonly StringBuilder recs = new StringBuilder();
        static int sceneCount;
        static bool preview;

        // ================================================================== reflection helpers
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        static object CallS(Type t, string name, params object[] a) { return t.GetMethod(name, Any).Invoke(null, a); }
        static object CallI(object o, string name, params object[] a) { return o.GetType().GetMethod(name, Any).Invoke(o, a); }
        static void SetF(object o, string f, object v) { o.GetType().GetField(f, Any).SetValue(o, v); }

        static int C(int rgb) { return Hud.C(rgb); }

        // ================================================================== scene description
        sealed class Scene
        {
            public string Name;
            public ScrGfx.BgPool Pool = new ScrGfx.BgPool();
            public ScrGfx.ObjSet Obj = new ScrGfx.ObjSet();
            public string Bg1 = null, Bg2 = null;   // initial maps
            public bool Bg2Tall;
            public int[] Grad0, Grad1;              // per-line BGR555 for CGRAM 0 / the lattice color (224 lines)
            public int LatticeKey = -1;             // BGR555 marker color of the lattice layer
            public readonly List<int[]> Styles = new List<int[]>();   // text palettes: {shadow, top, body}
            public readonly List<string> StyleNames = new List<string>();
            public readonly List<string> Consts = new List<string>();
        }

        // ================================================================== export
        public static int Export(string outDir)
        {
            OutDir = outDir;
            if (!Art.Has("m.land")) Art.Load();
            preview = Environment.GetEnvironmentVariable("SMB4_SCR_PREVIEW") != null;
            spriteIds.Clear(); mapIds.Clear(); mapTab.Length = 0; sceneTab.Length = 0; recs.Length = 0; sceneCount = 0;
            A = new ScrAsm(outDir, 64, 79);
            A.Inc.AppendLine("; VRAM layout while a screen is up (word addresses)");
            A.Inc.AppendLine("SCR_VRAM_BGCHR = $0000\nSCR_VRAM_BG1MAP = $3400\nSCR_VRAM_BG2MAP = $3800\nSCR_VRAM_TXTCHR = $4000\nSCR_VRAM_TXTMAP = $5C00\nSCR_VRAM_OBJ = $6000");
            A.Inc.AppendLine("SCN_REC_SIZE = 50");
            // the engine (game.s) hands every non-level frame to scr_frame/scr_nmi when this is defined
            A.Inc.AppendLine("SCR_HOOKS = 1");
            A.Inc.AppendLine("SCR_VER = " + ScrVersion + "   ; scr.inc refuses an older smb4tools.exe");

            Font();
            LevelInfo();
            Emit(Title());
            Emit(Files());
            Emit(Intro());
            Emit(GameOver());
            Emit(WorldClear());
            Emit(Story());
            Emit(Ending());
            Emit(Toad());
            Emit(Spade());
            Emit(NSpade());
            Emit(Panel("options"));
            Emit(Panel("help"));
            Emit(Panel("lsel"));
            for (int w = 1; w <= 8; w++) Emit(Map(w));
            A.Code.AppendLine("scr_mapdefs: .addr 0, scr_mapdef1, scr_mapdef2, scr_mapdef3, scr_mapdef4, scr_mapdef5, scr_mapdef6, scr_mapdef7, scr_mapdef8");
            A.Code.AppendLine(".export scr_mapdefs");

            // global tables
            A.Code.AppendLine("scr_scene_tab:");
            A.Code.Append(sceneTab);
            A.Code.Append(recs);
            A.Code.AppendLine("scr_map_tab:          ; far ptr, tw, th, tx, ty");
            A.Code.Append(mapTab);
            A.Code.AppendLine(".export scr_scene_tab, scr_map_tab");
            for (int i = 0; i < spriteIds.Count; i++) A.Inc.AppendLine("SP_" + spriteIds[i] + " = " + i);
            A.Inc.AppendLine("SP_COUNT = " + spriteIds.Count);
            for (int i = 0; i < mapIds.Count; i++) A.Inc.AppendLine("MAP_" + mapIds[i] + " = " + i);
            A.Write("scr_data.s", "scr_ids.inc");
            return 0;
        }

        static int SpriteId(string name)
        {
            int i = spriteIds.IndexOf(name);
            if (i < 0) { spriteIds.Add(name); i = spriteIds.Count - 1; }
            return i;
        }

        static string Sym(string s) { return s.ToUpperInvariant().Replace('.', '_').Replace('-', '_'); }

        /// <summary>Converts a scene and writes its blobs + record.</summary>
        static void Emit(Scene s)
        {
            string N = Sym(s.Name);
            if (s.LatticeKey >= 0)
            {
                // the lattice marker must stay a color of its own
            }
            if (s.Styles.Count > 4) { s.Pool.FirstPal = 2; s.Pool.NPal = 6; }
            s.Pool.Build();
            s.Obj.Build();
            Console.WriteLine("  screens: " + s.Name + ": " + s.Pool.Report + "; obj " + s.Obj.Report);
            string chr = A.Blob("bgchr_" + s.Name, s.Pool.ChrBytes());
            string pal = A.Blob("bgpal_" + s.Name, s.Pool.PalBytes());
            string ochr = A.Blob("objchr_" + s.Name, s.Obj.ChrBytes());
            string ochr2 = A.Blob("objchr2_" + s.Name, s.Obj.Chr2Bytes());
            string opal = A.Blob("objpal_" + s.Name, s.Obj.PalBytes());
            // metasprites
            var meta = new List<byte>();
            int nIds = 0; foreach (var im in s.Obj.Imgs) nIds = Math.Max(nIds, SpriteId(im.Id) + 1);
            nIds = Math.Max(nIds, 1);
            var offs = new int[nIds];
            var body = new List<byte>();
            int hdr = 2 + nIds * 2;
            foreach (var im in s.Obj.Imgs)
            {
                offs[SpriteId(im.Id)] = hdr + body.Count;
                body.Add((byte)im.Pieces.Count);
                body.Add((byte)Math.Max(0, im.FlipW));
                foreach (var p in im.Pieces)
                {
                    int dx = im.X0 - ORG + p[0], dy = im.Y0 - ORG + p[1];
                    body.Add((byte)(sbyte)dx); body.Add((byte)(sbyte)dy);
                    body.Add((byte)(p[2] & 255));
                    body.Add((byte)((im.PalSlot << 1) | (p[2] >> 8)));
                    body.Add((byte)p[3]);
                }
                // variant palettes (for runtime switching): stored after the pieces
                if (im.VarPals.Count > 0)
                {
                    body.Add((byte)im.VarPals.Count);
                    foreach (var vp in im.VarPals) for (int i = 0; i < 16; i++) { body.Add((byte)vp[i]); body.Add((byte)(vp[i] >> 8)); }
                }
                else body.Add(0);
            }
            meta.Add((byte)nIds); meta.Add((byte)(nIds >> 8));
            foreach (int o in offs) { meta.Add((byte)o); meta.Add((byte)(o >> 8)); }
            meta.AddRange(body);
            string mlab = A.Blob("meta_" + s.Name, meta.ToArray());
            // maps
            int firstMap = mapIds.Count;
            foreach (var m in s.Pool.Maps)
            {
                string id = N + "_" + Sym(m.Name);
                var b = new byte[m.Words.Length * 2];
                for (int i = 0; i < m.Words.Length; i++) { b[i * 2] = (byte)m.Words[i]; b[i * 2 + 1] = (byte)(m.Words[i] >> 8); }
                string lab = A.Blob("map_" + s.Name + "_" + m.Name.Replace('.', '_'), b);
                mapIds.Add(id);
                mapTab.AppendLine(string.Format("    .faraddr {0}\n    .byte {1}, {2}, {3}, {4}, 0", lab, m.Tw, m.Th, m.Tx, m.Ty));
            }
            // gradients (HDMA mode 3 -> $2121: cgadd, cgadd, lo, hi)
            string g0 = s.Grad0 != null ? A.Blob("grad0_" + s.Name, Hdma(s.Grad0, 0)) : null;
            int latticeCg = 0;
            if (s.LatticeKey >= 0)
                for (int p = 0; p < s.Pool.Pals.Length; p++)
                {
                    int k = Array.IndexOf(s.Pool.Pals[p], s.LatticeKey, 1);
                    if (k > 0) { latticeCg = (s.Pool.FirstPal + p) * 16 + k; break; }
                }
            string g1 = s.Grad1 != null && latticeCg > 0 ? A.Blob("grad1_" + s.Name, Hdma(s.Grad1, latticeCg)) : null;
            // text palettes (CGRAM 0-15: 4 x {0, shadow, top, body})
            var tp = new byte[s.Styles.Count > 4 ? 64 : 32];
            for (int i = 0; i < s.Styles.Count && i < 8; i++)
                for (int k = 0; k < 3; k++) { tp[i * 8 + 2 + k * 2] = (byte)s.Styles[i][k]; tp[i * 8 + 3 + k * 2] = (byte)(s.Styles[i][k] >> 8); }
            string tpl = A.Blob("txtpal_" + s.Name, tp);

            sceneTab.AppendLine("    .addr scn_" + s.Name);
            recs.AppendLine("scn_" + s.Name + ":");
            recs.AppendLine(string.Format("    .faraddr {0}\n    .word {1}\n    .faraddr {2}\n    .word {3}\n    .word {4}", chr, s.Pool.Tiles.Count * 32, pal, s.Pool.Pals.Length * 32, s.Pool.FirstPal * 16));
            recs.AppendLine(string.Format("    .faraddr {0}\n    .word {1}\n    .faraddr {2}\n    .faraddr {3}", ochr, s.Obj.ChrBytes().Length, opal, mlab));
            recs.AppendLine(string.Format("    .faraddr {0}\n    .faraddr {1}\n    .faraddr {2}", g0 ?? "0", g1 ?? "0", tpl));
            int b1 = s.Bg1 == null ? 0xFFFF : firstMap + s.Pool.Maps.IndexOf(s.Pool.Map(s.Bg1));
            int b2 = s.Bg2 == null ? 0xFFFF : firstMap + s.Pool.Maps.IndexOf(s.Pool.Map(s.Bg2));
            recs.AppendLine(string.Format("    .word ${0:X4}, ${1:X4}, {2}, {3}, {4}", b1, b2, s.Bg2Tall ? 1 : 0, latticeCg, tp.Length));
            recs.AppendLine(string.Format("    .faraddr {0}\n    .word {1}, ${2:X4}", ochr2, s.Obj.Chr2Bytes().Length, 0x6000 + s.Obj.FirstSingle * 16));
            A.Inc.AppendLine("SCN_" + N + " = " + sceneCount);
            for (int i = 0; i < s.StyleNames.Count; i++) A.Inc.AppendLine("TXP_" + N + "_" + s.StyleNames[i] + " = " + i);
            foreach (var c in s.Consts) A.Inc.AppendLine(c);
            sceneCount++;

            if (preview)
            {
                string dir = Path.Combine(OutDir, "scr_preview");
                Directory.CreateDirectory(dir);
                foreach (var m in s.Pool.Maps)
                {
                    if (m.Tw < 32) continue;
                    var mm = m;
                    ScrGfx.Preview(Path.Combine(dir, s.Name + "_" + m.Name + ".png"), mm.Tw * 8, mm.Th * 8, (x, y) => ScrGfx.MapPixel(s.Pool, mm.Words, mm.Tw, x, y));
                }
            }
        }

        static byte[] Hdma(int[] colors, int cg)
        {
            var o = new List<byte>();
            int i = 0;
            while (i < colors.Length)
            {
                int n = 1;
                while (i + n < colors.Length && colors[i + n] == colors[i] && n < 127) n++;
                o.Add((byte)n); o.Add((byte)cg); o.Add((byte)cg); o.Add((byte)colors[i]); o.Add((byte)(colors[i] >> 8));
                i += n;
            }
            o.Add(0);
            return o.ToArray();
        }

        // ================================================================== text: font + styles
        /// <summary>Canvas font: per glyph (ASCII 32-95) 9 rows x {shadow word, body word}, bit 15 = leftmost pixel.</summary>
        static void Font()
        {
            var sb = new StringBuilder();
            sb.AppendLine(".segment \"CODE11\"");
            sb.AppendLine(".export scr_glyphs");
            sb.AppendLine("scr_glyphs:          ; 64 glyphs (ASCII 32-95) x 9 rows x {shadow, body} words");
            for (int c = 32; c < 96; c++)
            {
                var g = SMB4.Engine.Font.Glyph((char)c);
                var m = new int[10];
                if (g != null) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (g.P[y * 8 + x] != 0) m[y] |= 0x8000 >> x;
                var words = new List<string>();
                for (int r = 0; r < 9; r++)
                {
                    int sh = r > 0 ? (m[r - 1] | (m[r - 1] >> 1)) : 0;
                    words.Add("$" + sh.ToString("X4")); words.Add("$" + m[r].ToString("X4"));
                }
                sb.AppendLine("    .word " + string.Join(",", words) + "   ; '" + (c == 34 ? "dq" : ((char)c).ToString()) + "'");
            }
            sb.AppendLine(".segment \"CODE7\"");
            A.Code.Append(sb);
        }

        /// <summary>Per level (engine LVL_* order, then "hb"): its id text ("1-1") and C# LevelIntroScreen subtitle
        /// (def.Title upper-cased, max 18 chars).</summary>
        static void LevelInfo()
        {
            var ids = SnesLevels.LevelOrder();
            if (!ids.Contains("hb")) ids.Add("hb");
            var sb = new StringBuilder();
            sb.AppendLine("scr_lvl_info:        ; per level: .addr id text, title text");
            for (int i = 0; i < ids.Count; i++) sb.AppendLine("    .addr lvid_" + i + ", lvti_" + i);
            for (int i = 0; i < ids.Count; i++)
            {
                var def = LevelLoader.Load(ids[i]);
                string title = def != null && def.Title != null ? def.Title.ToUpperInvariant() : "";
                if (title.Length > 18) title = title.Substring(0, 18);
                title = title.Replace("\"", "'");
                sb.AppendLine("lvid_" + i + ": .byte \"" + ids[i].ToUpperInvariant() + "\", 0");
                sb.AppendLine("lvti_" + i + ": .byte \"" + title + "\", 0");
            }
            sb.AppendLine(".export scr_lvl_info");
            A.Code.Append(sb);
            A.Inc.AppendLine("SCR_NLEVELS = " + ids.Count);
        }

        static int Shade(int colorIdx, int amt)
        {
            int rgb = NesPalette.RgbOf(colorIdx);
            return amt >= 0 ? Hud.Mix(rgb, 0xFFFFFF, amt) : Hud.Mix(rgb, 0x000000, -amt);
        }
        static int Blend(int rgbA, int rgbB, int wa, int wb)
        {
            int r = ((rgbA >> 16 & 255) * wa + (rgbB >> 16 & 255) * wb) / (wa + wb);
            int g = ((rgbA >> 8 & 255) * wa + (rgbB >> 8 & 255) * wb) / (wa + wb);
            int b = ((rgbA & 255) * wa + (rgbB & 255) * wb) / (wa + wb);
            return r << 16 | g << 8 | b;
        }
        /// <summary>Hud.Txt(color) as a canvas palette {shadow, top (rows 0-2), body (rows 3-7 = mid/bot blend)}.</summary>
        static int[] TxtStyle(int color)
        {
            int rgb = NesPalette.RgbOf(color);
            int lum = ((rgb >> 16 & 255) * 3 + (rgb >> 8 & 255) * 6 + (rgb & 255)) / 10;
            int top = NesPalette.RgbOf(Shade(color, 90)), bot = NesPalette.RgbOf(Shade(color, -40));
            int shadow = lum < 60 ? 0x000000 : 0x100818;
            return new[] { ScrGfx.Bgr(shadow), ScrGfx.Bgr(top), ScrGfx.Bgr(Blend(rgb, bot, 2, 3)) };
        }
        static int[] GradStyle(int top, int mid, int bot, int shadow)
        {
            return new[] { ScrGfx.Bgr(shadow), ScrGfx.Bgr(top), ScrGfx.Bgr(Blend(mid, bot, 2, 3)) };
        }
        static void Style(Scene s, string name, int[] st) { s.StyleNames.Add(name); s.Styles.Add(st); }

        // ================================================================== shared pieces
        /// <summary>Hud.Backdrop split for the SNES: the gradient + lattice shade become HDMA color tables, the
        /// diamonds one marker color on BG2 (scrolled diagonally by the runtime).</summary>
        static void Backdrop(Scene s, int crop)
        {
            s.Grad0 = new int[224]; s.Grad1 = new int[224];
            for (int L = 0; L < 224; L++)
            {
                int yy = L + crop;
                int band = yy / 4 * 4;
                int t = band * 256 / 239;
                s.Grad0[L] = ScrGfx.Bgr(NesPalette.RgbOf(Hud.Mix(0x182868, 0x080818, Math.Min(256, t))));
                s.Grad1[L] = ScrGfx.Bgr(NesPalette.RgbOf(Hud.Mix(0x283C90, 0x101838, Math.Max(0, Math.Min(256, yy * 256 / 240)))));
            }
            s.LatticeKey = 0x7C1F;
            var l = new ScrGfx.Layer(256, 256);
            for (int y = 0; y < 256; y += 32)
                for (int x = 0; x < 256; x += 32)
                    for (int r = 0; r < 8; r++)
                    {
                        int w = r < 4 ? r * 2 + 2 : (7 - r) * 2 + 2;
                        for (int k = 0; k < w; k++) l.Px[(y + 12 + r) * 256 + x + 16 - w / 2 + k] = s.LatticeKey;
                    }
            s.Pool.AddFull("lattice", l, false);
            s.Bg2 = "lattice";
            s.Consts.Add("SCN_" + Sym(s.Name) + "_CROP = " + crop);
        }

        /// <summary>Layer from a drawing callback on a Ppu, rows [crop, crop+h).</summary>
        static ScrGfx.Layer Draw(Action<Ppu> f, int crop, int h)
        {
            var p = ScrGfx.NewPpu();
            f(p);
            return ScrGfx.FromPpu(p, crop, h);
        }

        /// <summary>A sprite image drawn with its anchor at (ORG, ORG).</summary>
        static ScrGfx.Layer Spr(Action<Ppu, int, int> f)
        {
            var p = ScrGfx.NewPpu();
            f(p, ORG, ORG);
            return ScrGfx.FromPpu(p, 0, 240);
        }

        static ScrGfx.SprImg AddSpr(Scene s, string id, int flipW, params ScrGfx.Layer[] v)
        {
            SpriteId(id);
            var im = s.Obj.Add(id, v);
            im.FlipW = flipW;
            return im;
        }

        static void CursorSprites(Scene s)
        {
            // Hud.Cursor at its two bob positions is the same image shifted by 1: one sprite, runtime adds dx
            AddSpr(s, "CURSOR", 0, Spr((p, x, y) => Hud.Cursor(p, x, y, 8)));
        }

        static ScrGfx.Layer SprImgPal(string img, ushort[] pal) { return Spr((p, x, y) => p.Spr(Art.Get(img), x, y, pal)); }

        /// <summary>n lit lanterns LANTERN, LANTERN2.. in OBJ slots 0..n-1 (variant 1 = the flicker's glow palette),
        /// so each can flicker on its own phase; plus LANTERN_DARK (packed).</summary>
        static void Lanterns(Scene s, int n)
        {
            for (int i = 0; i < n; i++)
                AddSpr(s, i == 0 ? "LANTERN" : "LANTERN" + (i + 1), 16, SprImgPal("lantern.lit", Art.PreviewPalFor("lantern.lit")), SprImgPal("lantern.lit", Art.Pal("lantern.glow"))).Slot = i;
        }

        // ------------------------------------------------------------------ compact 32-px status bar (maps, bonus rooms)
        /// <summary>Hud.Draw re-laid out for the SNES's 32 HUD lines (192-223); digits/cards/badge are drawn at runtime.</summary>
        static void HudBar(Ppu ppu, int top, bool luigi)
        {
            Hud.VGrad(ppu, 0, top, 256, 32, 0x28284C, 0x08080F, 2);
            ppu.FillRect(0, top, 256, 1, C(0x000000));
            ppu.FillRect(0, top + 1, 256, 1, C(0xE8B830));
            ppu.FillRect(0, top + 2, 256, 1, C(0x805010));
            Hud.Panel(ppu, 6, top + 4, 178, 27, 0x3048B8, 0x101848, 0x90A8F8, 0x080C30, 0x000008);
            Hud.GradText(ppu, "WORLD", 11, top + 7, C(0xFFFFFF), C(0xD8E0F8), C(0x98A8D8), C(0x000010));
            Hud.Inset(ppu, 68, top + 6, 68, 10, 0x101020, 0x202040);
            for (int i = 0; i < 6; i++) Hud.GradText(ppu, "^", 70 + i * 8, top + 7, C(0x505878), C(0x404868), C(0x303450), -1);
            Hud.Panel(ppu, 119, top + 6, 17, 10, 0x505870, 0x282C40, 0x707890, 0x181820, 0x000008);
            Hud.GradText(ppu, "P", 124, top + 7, C(0x8890A8), C(0x707890), C(0x606880), -1);
            Hud.GradText(ppu, "$", 144, top + 7, C(0xFFF8C0), C(0xF8C830), C(0xB86810), C(0x000010));
            HudBadge(ppu, top, luigi);
            for (int i = 0; i < 3; i++)
            {
                int x = 188 + i * 22;
                Hud.Panel(ppu, x, top + 4, 20, 27, 0x3048B8, 0x101848, 0x90A8F8, 0x080C30, 0x000008);
                Hud.Inset(ppu, x + 2, top + 7, 16, 21, 0x0C1030, 0x20306C);
            }
        }
        static void HudBadge(Ppu ppu, int top, bool lu)
        {
            if (lu) Hud.Panel(ppu, 9, top + 17, 12, 11, 0x80F070, 0x188818, 0xC8FFC0, 0x084008, 0x001000);
            else Hud.Panel(ppu, 9, top + 17, 12, 11, 0xFF8070, 0xC01818, 0xFFC8B8, 0x500000, 0x180000);
            Hud.GradText(ppu, lu ? "L" : "M", 11, top + 19, C(0xFFFFFF), C(0xFFFFFF), C(0xE0E0E8), C(lu ? 0x084008 : 0x500000));
        }
        // runtime text positions of the bar (SNES y = 192 + ...)
        static void HudConsts(Scene s)
        {
            s.Consts.Add("HUD_Y1 = 199\nHUD_Y2 = 211\nHUD_CARD_Y = 200");
        }
        static void HudSprites(Scene s)
        {
            for (int c = 0; c < 3; c++)
            {
                int cc = c;
                AddSpr(s, "CARD" + c, 0, Spr((p, x, y) => p.Spr(Art.Get(GoalBox.CardName(cc)), x, y, GoalBox.CardPal(cc))));
            }
        }
        /// <summary>The bar + its M/L badge block (2x2 tiles at tile 1,26) in the scene's pool.</summary>
        static void HudLayers(Scene s, ScrGfx.Layer baseLayer, Action<Ppu> under, int crop)
        {
            var lu = Draw(p => { under(p); HudBar(p, 192 + crop, true); }, crop, 224);
            s.Pool.Add("badge_l", lu, 1, 26, 2, 2, false);
            HudConsts(s);
            HudSprites(s);
        }

        // ================================================================== TITLE
        static Scene Title()
        {
            var s = new Scene { Name = "title" };
            const int crop = 8;
            Type ts = typeof(TitleScreen);
            int[] red = { 0xFFD8B0, 0xFF9060, 0xF84828, 0xD01818, 0x901010 };
            int[] gold = { 0xFFFFE0, 0xFFF080, 0xF8C830, 0xE08818, 0xA05008 };
            Action<Ppu, int> stage = (p, t) =>
            {
                CallS(ts, "Stage", p, t);
                TitleScreen.BigText(p, "SUPER MARIO", 118, 24, 2, red, 0x180408, 0x5A0818, 2);
                TitleScreen.BigText(p, "BROS.", 96, 48, 2, red, 0x180408, 0x5A0818, 2);
                TitleScreen.BigText(p, "4", 178, 44, 5, gold, 0x200C00, 0x7A3000, 3);
                CallS(ts, "Ribbon", p, 60, 88, 136, 13);
                Hud.GradText(p, "THE LANTERN TOUR", 64, 91, Hud.C(0xFFFFFF), Hud.C(0xFFF0C0), Hud.C(0xF8D080), Hud.C(0x401000));
                Hud.Panel(p, 56, 103, 144, 51, 0x283070, 0x101438, 0x6070C0, 0x080820, 0x04040C);
                Hud.Panel(p, 48, 214, 160, 14, 0x282020, 0x100808, 0x584848, 0x080404, 0x000000);
                Hud.GradText(p, "A FAN-MADE TRIBUTE", 56, 217, Hud.C(0xFFF0C0), Hud.C(0xE8C890), Hud.C(0xB09060), Hud.C(0x000000));
            };
            var st = Draw(p => stage(p, 0), crop, 224);
            s.Pool.AddFull("stage", st, false);
            s.Bg1 = "stage";
            // footlights on: tile row of the lights (C# y 189-190 -> SNES 181-182 = tile row 22)
            var lit = Draw(p => stage(p, 10), crop, 224);
            s.Pool.Add("lights", lit, 0, 22, 32, 1, false);
            s.Pool.Add("lights_off", st, 0, 22, 32, 1, false);
            // curtain (BG2, 32x64): Curtain(y=-8) puts the C# curtainY=0 picture into SNES coordinates
            var cur = new ScrGfx.Layer(256, 512);
            var cl = Draw(p => TitleScreen.Curtain(p, -8, 0), 0, 224);
            Array.Copy(cl.Px, cur.Px, cl.Px.Length);
            s.Pool.AddFull("curtain", cur, true);
            s.Bg2 = "curtain"; s.Bg2Tall = true;
            // highlight bar of the menu (row 0 position; the runtime scrolls BG2 by 11 px per item)
            // the menu panel with the highlight bar on item i, on BG2 (priority: the attract actors pass behind it
            // like in the C#, where the menu is drawn after them); the runtime swaps the map when the selection moves
            for (int i = 0; i < 4; i++)
            {
                int ii = i;
                s.Pool.AddFull("menu" + i, Draw(p =>
                {
                    Hud.Panel(p, 56, 103, 144, 51, 0x283070, 0x101438, 0x6070C0, 0x080820, 0x04040C);
                    Hud.VGrad(p, 59, 108 + ii * 11 - 2, 138, 11, 0x5068D0, 0x283888);
                }, crop, 224), true);
            }
            Style(s, "MENU", GradStyle(0xC8D0F0, 0xA0A8D0, 0x8088B8, 0x04040C));
            Style(s, "WHITE", TxtStyle(0x30));
            // sprites
            CursorSprites(s);
            Lanterns(s, 2);
            AddSpr(s, "SPARKLE1", 0, Spr((p, x, y) => CallS(ts, "Sparkle", p, x, y, 0)));
            AddSpr(s, "SPARKLE2", 0, Spr((p, x, y) => CallS(ts, "Sparkle", p, x, y, 1)));
            AddSpr(s, "GOOMBA1", 16, SprImgPal("goomba.1", Art.Pal("goomba")));
            AddSpr(s, "GOOMBA2", 16, SprImgPal("goomba.2", Art.Pal("goomba")));
            foreach (var f in new[] { "walk1", "walk2", "stand", "jump" })
            {
                string ff = f;
                AddSpr(s, "TMARIO_" + Sym(f), 0, Spr((p, x, y) => Player.Body(p, Art.Get("mb." + ff), x, y, 32, Art.Pal("mario"), false, false, false)));
            }
            return s;
        }

        // ================================================================== FILE SELECT
        static Scene Files()
        {
            var s = new Scene { Name = "files" };
            const int crop = 8;
            Backdrop(s, crop);
            Action<Ppu> wins = p => { for (int i = 0; i < 3; i++) Hud.Window(p, 24, 60 + i * 44, 208, 38); };
            s.Pool.AddFull("slots", Draw(wins, crop, 224), false);
            s.Pool.AddFull("confirm", Draw(p => { wins(p); Hud.Window(p, 40, 96, 176, 48); }, crop, 224), false);
            s.Bg1 = "slots";
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GREY", TxtStyle(0x10));
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "RED", TxtStyle(0x26));
            CursorSprites(s);
            AddSpr(s, "MPMARIO", 16, SprImgPal("mp.mario1", Art.Pal("mario")));
            AddSpr(s, "STAR", 16, SprImgPal("star", Art.Pal("star")));
            Style(s, "GREEN", TxtStyle(0x2A));
            return s;
        }

        // ================================================================== LEVEL INTRO
        static Scene Intro()
        {
            var s = new Scene { Name = "intro" };
            const int crop = 8;
            Backdrop(s, crop);
            s.Pool.AddFull("card", Draw(p => Hud.Window(p, 40, 64, 176, 88), crop, 224), false);
            s.Bg1 = "card";
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GOLD", TxtStyle(0x28));
            PlayerIdle(s);
            return s;
        }

        // ================================================================== flow screens
        /// <summary>Hud.VGrad(0, 0, 256, h, top, bottom, band) as the per-line backdrop (HDMA on CGRAM 0).</summary>
        static void Grad(Scene s, int top, int bottom, int band, int crop, int h)
        {
            s.Grad0 = new int[224];
            for (int L = 0; L < 224; L++)
            {
                int y = L + crop;
                if (y >= h) { s.Grad0[L] = 0; continue; }
                int yy = y / band * band;
                int t = h <= 1 ? 0 : yy * 256 / (h - 1);
                s.Grad0[L] = ScrGfx.Bgr(NesPalette.RgbOf(Hud.Mix(top, bottom, Math.Min(256, t))));
            }
            s.Consts.Add("SCN_" + Sym(s.Name) + "_CROP = " + crop);
        }

        static void Dot(Scene s, string id, int color) { AddSpr(s, id, 0, Spr((p, x, y) => p.FillRect(x, y, 1, 1, color))); }

        static string[] StrField(Type t, string f) { return (string[])t.GetField(f, Any).GetValue(null); }

        /// <summary>A string table for the runtime: label: .addr s0, s1...; strings upper-cased, 0-terminated.</summary>
        static void Strings(string label, string[] lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine(label + ":");
            for (int i = 0; i < lines.Length; i++) sb.AppendLine("    .addr " + label + "_" + i);
            for (int i = 0; i < lines.Length; i++) sb.AppendLine(label + "_" + i + ": .byte \"" + lines[i].ToUpperInvariant().Replace("\"", "'") + "\", 0");
            sb.AppendLine(".export " + label);
            A.Code.Append(sb);
            A.Inc.AppendLine(label.ToUpperInvariant() + "_N = " + lines.Length);
        }

        static Scene GameOver()
        {
            var s = new Scene { Name = "gameover" };
            Grad(s, 0x000000, 0x380810, 4, 8, 240);
            s.Pool.AddFull("win", Draw(p =>
            {
                Hud.Window(p, 56, 64, 144, 88);
                Hud.GradText(p, "GAME OVER", 92, 80, Hud.C(0xFFB0A0), Hud.C(0xF84830), Hud.C(0xB01818), Hud.C(0x100008));
            }, 8, 224), false);
            s.Bg1 = "win";
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GREY", TxtStyle(0x10));
            CursorSprites(s);
            return s;
        }

        static Scene WorldClear()
        {
            var s = new Scene { Name = "worldclr" };
            Grad(s, 0x080820, 0x302060, 4, 8, 240);
            s.Pool.AddFull("win", Draw(p => Hud.Window(p, 16, 48, 224, 112), 8, 224), false);
            s.Bg1 = "win";
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GREEN", TxtStyle(0x2A));
            Style(s, "GREY", TxtStyle(0x10));
            Dot(s, "DOT_W", 0x30);
            Dot(s, "DOT_Y", 0x28);
            Lanterns(s, 1);
            Strings("scr_kingdoms", StrField(typeof(WorldClearScreen), "Kingdoms"));
            return s;
        }

        static Scene Story()
        {
            var s = new Scene { Name = "story" };
            Grad(s, 0x04040C, 0x201840, 4, 8, 240);
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "GREY", TxtStyle(0x10));
            Dot(s, "DOT_W", 0x30);
            Dot(s, "DOT_D", C(0x6060A0));
            Lanterns(s, 1);
            AddSpr(s, "LANTERN_DARK", 16, SprImgPal("lantern.dark", Art.PreviewPalFor("lantern.dark")));
            Strings("scr_story", StrField(typeof(StoryScreen), "Lines"));
            return s;
        }

        static Scene Ending()
        {
            var s = new Scene { Name = "ending" };
            const int crop = 8;
            Grad(s, 0x080828, 0x483878, 4, crop, 176);
            s.Pool.AddFull("floor", Draw(p =>
            {
                p.FillRect(0, 176, 256, 64, Hud.C(0x301008));
                for (int row = 0; row < 8; row++)
                    for (int x = (row & 1) * -8; x < 256; x += 16)
                    {
                        int y = 176 + row * 8;
                        Hud.VGrad(p, x, y, 15, 7, row == 0 ? 0xF0B060 : 0xC87038, row == 0 ? 0xC87838 : 0x8A4820);
                        p.FillRect(x, y, 15, 1, Hud.C(row == 0 ? 0xFFE0A0 : 0xE0985A));
                        p.FillRect(x + 14, y, 1, 7, Hud.C(0x602810));
                    }
            }, crop, 224), false);
            s.Bg1 = "floor";
            s.Pool.AddFull("story", Draw(p => Hud.Window(p, 8, 34, 240, 104), crop, 224), true);
            // credits (all but THE END) pre-rendered on a 512-px BG2 page: line i at y = 8 + 14 i
            var credits = StrField(typeof(EndingScreen), "Credits");
            var cl = new ScrGfx.Layer(256, 512);
            for (int i = 0; i < credits.Length - 1; i++)
            {
                if (credits[i].Length == 0) continue;
                int ii = i;
                var one = Draw(p => Hud.TxtC(p, credits[ii], 8, ii < 2 ? 0x2A : 0x30), 0, 24);
                for (int y = 0; y < 10; y++) for (int x = 0; x < 256; x++)
                    { int c = one.At(x, 8 + y); if (c != ScrGfx.Transparent) cl.Px[(8 + i * 14 + y) * 256 + x] = c; }
            }
            s.Pool.AddFull("credits", cl, true);
            s.Bg2Tall = true;
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GREY", TxtStyle(0x10));
            Dot(s, "DOT_W", 0x30);
            Dot(s, "DOT_B", 0x21);
            Lanterns(s, 1);
            foreach (var f in new[] { "walk1", "walk2", "stand" })
            {
                string ff = f;
                AddSpr(s, "EMARIO_" + Sym(f), 0,
                    Spr((p, x, y) => Player.Body(p, Art.Get("mb." + ff), x, y, 32, Art.Pal("mario"), false, false, false)),
                    Spr((p, x, y) => Player.Body(p, Art.Get("mb." + ff), x, y, 32, Art.Pal("luigi"), false, false, false))).Slot = 1;
            }
            AddSpr(s, "PRINCESS1", 0, Spr((p, x, y) => Art.DrawSplit(p, "princess.1", "princess", x, y, true)));
            AddSpr(s, "PRINCESS2", 0, Spr((p, x, y) => Art.DrawSplit(p, "princess.2", "princess", x, y, true)));
            var story = StrField(typeof(EndingScreen), "Story");
            Strings("scr_endstory", story);
            Strings("scr_credits", credits);
            return s;
        }

        static void ToadRoom(Ppu p)
        {
            for (int y = 0; y < 160; y += 16)
                for (int x = -((y / 16) % 2) * 16; x < 256; x += 32)
                {
                    Hud.VGrad(p, x, y, 32, 16, 0xA86030, 0x6A3818);
                    p.FillRect(x, y, 32, 1, Hud.C(0xD89050));
                    p.FillRect(x, y + 15, 32, 1, Hud.C(0x301008));
                    p.FillRect(x + 31, y, 1, 16, Hud.C(0x401808));
                    p.FillRect(x + 6 + (y * 7) % 13, y + 6, 9, 1, Hud.C(0x804020));
                    p.FillRect(x + 3 + (y * 5) % 17, y + 10, 7, 1, Hud.C(0x804020));
                    p.FillRect(x + 2, y + 3, 1, 1, Hud.C(0xE8C080));
                    p.FillRect(x + 29, y + 3, 1, 1, Hud.C(0xE8C080));
                }
            p.FillRect(231, 58, 1, 8, Hud.C(0x301008));
            Hud.VGrad(p, 0, 160, 256, 32, 0xD89048, 0x804018);
            p.FillRect(0, 160, 256, 1, Hud.C(0xFFD890));
            p.FillRect(0, 161, 256, 1, Hud.C(0xE8A860));
            for (int x = 0; x < 256; x += 48) p.FillRect(x, 162, 1, 30, Hud.C(0x703810));
            p.FillRect(0, 176, 256, 1, Hud.C(0x703810));
            Hud.Window(p, 24, 16, 208, 40);
        }

        /// <summary>The compact status bar with its Luigi badge block and card sprites.</summary>
        static void WithHud(Scene s, string map, Action<Ppu> under)
        {
            s.Pool.AddFull(map, Draw(p => { under(p); HudBar(p, 192, false); }, 0, 224), false);
            s.Pool.Add("badge_l", Draw(p => { under(p); HudBar(p, 192, true); }, 0, 224), 1, 26, 2, 2, false);
            s.Bg1 = map;
            HudSprites(s);
        }

        static void ItemIcons(Scene s)
        {
            for (int i = 1; i < ItemNames.Length; i++)
            {
                Item it = (Item)i;
                AddSpr(s, "ITEM_" + ItemNames[i], 16, Spr((p, x, y) => MapScreen.DrawItemIcon(p, it, x, y, 0)));
            }
        }

        static Scene Toad()
        {
            var s = new Scene { Name = "toad" };
            WithHud(s, "room", ToadRoom);
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GOLD", TxtStyle(0x28));
            Lanterns(s, 1);
            var ptr = AddSpr(s, "POINTER", 0, Spr((p, x, y) => CallS(typeof(ToadHouseScreen), "Pointer", p, x, y, 0x30)),
                Spr((p, x, y) => CallS(typeof(ToadHouseScreen), "Pointer", p, x, y, 0x28)));
            ptr.Slot = 1;
            AddSpr(s, "TOAD1", 16, Spr((p, x, y) => Art.DrawSplit(p, "toad.1", "toad", x, y, false)));
            AddSpr(s, "TOAD2", 16, Spr((p, x, y) => Art.DrawSplit(p, "toad.2", "toad", x, y, false)));
            AddSpr(s, "CHEST_C", 16, SprImgPal("chest.closed", Art.PreviewPalFor("chest.closed")));
            AddSpr(s, "CHEST_O", 16, SprImgPal("chest.open", Art.PreviewPalFor("chest.open")));
            ItemIcons(s);
            PlayerIdle(s);
            return s;
        }

        static Scene Spade()
        {
            var s = new Scene { Name = "spade" };
            Backdrop(s, 0);
            WithHud(s, "box", p =>
            {
                Hud.Window(p, 64, 24, 128, 152);
                for (int r = 0; r < 3; r++) Hud.Inset(p, 80, 40 + r * 44, 96, 40, 0xE0E8F8, 0xFFFFFF);
            });
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "GREY", TxtStyle(0x10));
            CursorSprites(s);
            return s;
        }

        // N-Spade cards on a tile grid: card (c, r) at x = 16 + 40c, y = 32 + 48r (C#: 17 + 38c, 34 + 48r)
        const int NsX0 = 16, NsDX = 40, NsY0 = 32, NsDY = 48;
        static void NsFelt(Ppu p)
        {
            Hud.VGrad(p, 0, 0, 256, 192, 0x106030, 0x04200C, 2);
            for (int y = 4; y < 192; y += 8) for (int x = (y / 8) % 2 * 8; x < 256; x += 16) p.FillRect(x, y, 1, 1, Hud.Mix(0x30A050, 0x104820, y * 256 / 192));
        }
        static void NsCard(Ppu p, int face, int x, int y)
        {
            const int W = 32, H = 40;
            p.FillRect(x + 2, y + H, W - 1, 2, Hud.C(0x021008));
            p.FillRect(x + W, y + 2, 2, H - 1, Hud.C(0x021008));
            if (face < 0)
            {
                Hud.Panel(p, x, y, W, H, 0xFFB050, 0xD06818, 0xFFE0A0, 0x803008, 0x200800);
                Hud.Panel(p, x + 3, y + 3, W - 6, H - 6, 0xE88830, 0xB85010, 0xFFC878, 0x803008, 0x582000);
                p.Spr(Art.Get("m.spade"), x + 8, y + 12, Art.PN(Hud.C(0x100818), Hud.C(0xF8F0E0), Hud.C(0xFFFFFF), Hud.C(0xC8C0B0),
                    Hud.C(0x807868), Hud.C(0xFFFFFF), Hud.C(0xE83020), Hud.C(0x901010), Hud.C(0x182048), Hud.C(0x5068B0)));
                return;
            }
            Hud.Panel(p, x, y, W, H, 0xFFFFFF, 0xC8D0E8, 0xFFFFFF, 0x8088A8, 0x101020);
            int ix = x + 8, iy = y + 8;
            switch (face)
            {
                case 0: p.Spr(Art.Get("mushroom"), ix, iy, Art.Pal("mushroom")); break;
                case 1: p.Spr(Art.Get("flower.1"), ix, iy, Art.Pal("flower")); break;
                case 2: p.Spr(Art.Get("star"), ix, iy, Art.Pal("star")); break;
                case 3: p.Spr(Art.Get("mushroom"), ix, iy, Art.Pal("oneup")); Hud.GradText(p, "1UP", x + 4, y + 28, Hud.C(0x50D040), Hud.C(0x209020), Hud.C(0x106010), -1); break;
                case 4: p.Spr(Art.Get("coin.1"), ix, iy, Art.Pal("coin")); Hud.GradText(p, "10", x + 8, y + 28, Hud.C(0x404058), Hud.C(0x202030), Hud.C(0x101018), -1); break;
                case 5: p.Spr(Art.Get("coin.1"), ix, iy, Art.Pal("coin")); Hud.GradText(p, "20", x + 8, y + 28, Hud.C(0x404058), Hud.C(0x202030), Hud.C(0x101018), -1); break;
            }
        }

        static Scene NSpade()
        {
            var s = new Scene { Name = "nspade" };
            WithHud(s, "table", p =>
            {
                NsFelt(p);
                for (int i = 0; i < 18; i++) NsCard(p, -1, NsX0 + (i % 6) * NsDX, NsY0 + (i / 6) * NsDY);
            });
            for (int f = 0; f < 6; f++)
            {
                int ff = f;
                var l = Draw(p => { NsFelt(p); for (int r = 0; r < 3; r++) NsCard(p, ff, NsX0, NsY0 + r * NsDY); }, 0, 224);
                for (int r = 0; r < 3; r++) s.Pool.Add("f" + f + "r" + r, l, NsX0 / 8, (NsY0 + r * NsDY) / 8, 4, 5, false);
            }
            var back = Draw(p => { NsFelt(p); for (int r = 0; r < 3; r++) NsCard(p, -1, NsX0, NsY0 + r * NsDY); }, 0, 224);
            for (int r = 0; r < 3; r++) s.Pool.Add("b" + r, back, NsX0 / 8, (NsY0 + r * NsDY) / 8, 4, 5, false);
            s.Pool.AddFull("box", Draw(p => Hud.Box(p, NsX0 - 2, NsY0 - 2, 36, 44, 0x28), 0, 224), true);
            s.Bg2 = "box";
            s.Consts.Add("NS_X0 = " + NsX0 + "\nNS_DX = " + NsDX + "\nNS_Y0 = " + NsY0 + "\nNS_DY = " + NsDY);
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "RED", TxtStyle(0x16));
            Style(s, "GREEN", TxtStyle(0x2A));
            return s;
        }

        /// <summary>Backdrop + the big 240x224 window (options, how to play, level select).</summary>
        static Scene Panel(string name)
        {
            var s = new Scene { Name = name };
            Backdrop(s, 8);
            s.Pool.AddFull("win", Draw(p => Hud.Window(p, 8, 8, 240, 224), 8, 224), false);
            s.Bg1 = "win";
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GREY", TxtStyle(0x10));
            Style(s, "BLUE", TxtStyle(0x21));
            Style(s, "GREEN", TxtStyle(0x2A));
            CursorSprites(s);
            if (name == "help")
            {
                AddSpr(s, "H_MUSHROOM", 16, SprImgPal("mushroom", Art.Pal("mushroom")));
                AddSpr(s, "H_FLOWER", 16, SprImgPal("flower.1", Art.Pal("flower")));
                AddSpr(s, "H_LEAF", 16, SprImgPal("leaf", Art.Pal("leaf")));
                AddSpr(s, "H_STAR", 16, SprImgPal("star", Art.Pal("star")));
            }
            return s;
        }

        /// <summary>Player.DrawIdle for every form, Mario/Luigi palettes as variants of OBJ palette 7.</summary>
        static void PlayerIdle(Scene s)
        {
            foreach (Form f in Enum.GetValues(typeof(Form)))
            {
                Form ff = f;
                var im = AddSpr(s, "IDLE_" + Sym(f.ToString()), 0,
                    Spr((p, x, y) => Player.DrawIdle(p, ff, false, x, y, false)),
                    Spr((p, x, y) => Player.DrawIdle(p, ff, true, x, y, false)));
                im.Slot = 7; im.Dynamic = true;
            }
        }

        // ================================================================== WORLD MAPS
        static readonly string[] ItemNames = { "", "MUSHROOM", "FLOWER", "LEAF", "STAR", "PWING", "TANOOKI", "FROG", "HAMMER", "CLOUD" };

        static Scene Map(int world)
        {
            var s = new Scene { Name = "map" + world };
            var def = MapDef.Load(world);
            var ms = new MapScreen(world, false);
            SetF(ms, "map", def);
            var land = Art.Pal(def.Pal);
            Func<string, ushort[]> mapPal = img => (ushort[])CallI(ms, "MapPal", img);

            // terrain (passes 1-3 of MapScreen.Render) + nodes in state `alt` (false = initial, true = the other state)
            Action<Ppu, int, bool, bool> terrain = (p, waterFrame, alt, withNodes) =>
            {
                p.FillRect(0, 0, 256, 192, land[0] != 0 ? land[0] : (Art.HasPal(def.Pal + ".bg") ? Art.Pal(def.Pal + ".bg")[1] : 0x0F));
                string water = waterFrame == 0 ? "m.water1" : "m.water2";
                for (int y = 0; y < def.H; y++)
                    for (int x = 0; x < def.W; x++)
                    {
                        string baseImg;
                        switch ((char)CallI(ms, "BaseTerrain", x, y))
                        {
                            case '~': case '=': case '!': baseImg = water; break;
                            case ',': baseImg = "m.land2"; break;
                            case 's': baseImg = "m.sand"; break;
                            case 'w': baseImg = "m.snow"; break;
                            case 'c': baseImg = "m.cloudland"; break;
                            case 'v': baseImg = "m.lava"; break;
                            default: baseImg = "m.land"; break;
                        }
                        p.Tile(Art.Get(baseImg), x * 16, y * 16, mapPal(baseImg));
                    }
                for (int y = 0; y < def.H; y++)
                    for (int x = 0; x < def.W; x++)
                    {
                        char c = def.Grid[x, y];
                        bool liquid = c == '~' || c == '=' || c == '!' || c == 'v';
                        if (liquid) CallI(ms, "DrawShore", p, land, x, y, x * 16, y * 16);
                        else CallI(ms, "DrawPatchRim", p, land, x, y, x * 16, y * 16);
                    }
                for (int y = 0; y < def.H; y++)
                    for (int x = 0; x < def.W; x++)
                    {
                        string ov = null;
                        switch (def.Grid[x, y])
                        {
                            case 'T': ov = "m.tree"; break; case '^': ov = "m.hill"; break; case 'r': ov = "m.rock"; break;
                            case '*': ov = "m.flower"; break; case 'p': ov = "m.palm"; break; case 'k': ov = "m.skull"; break;
                            case '-': ov = "m.path.h"; break; case '|': ov = "m.path.v"; break;
                            case '=': ov = "m.bridge.h"; break; case '!': ov = "m.bridge.v"; break;
                        }
                        if (ov != null) p.Tile(Art.Get(ov), x * 16, y * 16, mapPal(ov));
                    }
                if (!withNodes) return;
                foreach (var n in def.Nodes)
                {
                    string img = NodeImg(def, n, alt);
                    if (img == null) continue;
                    p.Tile(Art.Get(img), n.X * 16, n.Y * 16, mapPal(img));
                    if (img == "m.panel") Hud.GradText(p, n.Code.ToString(), n.X * 16 + 4, n.Y * 16 + 4, Hud.C(0x482008), Hud.C(0x301000), Hud.C(0x200800), Hud.C(0xFFE8A0));
                }
            };
            ScrGfx.Layer w1 = null, w2 = null, alt1 = null;
            w1 = Draw(p => { terrain(p, 0, false, true); HudBar(p, 192, false); }, 0, 224);
            w2 = Draw(p => { terrain(p, 1, false, true); HudBar(p, 192, false); }, 0, 224);
            alt1 = Draw(p => { terrain(p, 0, true, true); HudBar(p, 192, true); }, 0, 224);
            s.Pool.AddFull("water1", w1, false);
            s.Pool.AddFull("water2", w2, false);
            s.Bg1 = "water1";
            s.Pool.Add("badge_l", alt1, 1, 26, 2, 2, false);
            // node blocks (alternate state) + node table
            var nt = new StringBuilder();
            string N = "MAP" + world;
            int ni = 0;
            foreach (var n in def.Nodes)
            {
                string a = NodeImg(def, n, true), b = NodeImg(def, n, false);
                string blk = "0";
                if (a != b) { s.Pool.Add("n" + ni, alt1, n.X * 2, n.Y * 2, 2, 2, false); blk = "MAP_" + N + "_N" + ni; }
                int kind = Array.IndexOf(new[] { "start", "dot", "level", "fortress", "airship", "castle", "toad", "spade", "lock" }, n.Kind);
                string lvl = n.Level != null ? "LVL_" + Sym(n.Level) : "$FF";
                int items = 0;
                if (n.Items != null) foreach (var it in n.Items) { int k = Array.IndexOf(ItemNames, it.Trim().ToUpperInvariant()); if (k > 0) items |= 1 << k; }
                if (n.Kind == "toad" && n.Items == null) items = (1 << 1) | (1 << 2) | (1 << 3);
                bool horiz = def.At(n.X - 1, n.Y) == '-' || def.At(n.X + 1, n.Y) == '-';
                nt.AppendLine(string.Format("    .byte '{0}', {1}, {2}, {3}, {4}, '{5}', {6}\n    .word {7}, {8}",
                    n.Code, n.X, n.Y, kind, lvl, n.OpensWith == 0 ? ' ' : n.OpensWith, horiz ? 1 : 0, blk, items));
                ni++;
            }
            // grid, bros, header
            var gsb = new StringBuilder();
            for (int y = 0; y < 12; y++)
            {
                var row = new List<string>();
                for (int x = 0; x < 16; x++) row.Add(((int)def.At(x, y)).ToString());
                gsb.AppendLine("    .byte " + string.Join(",", row));
            }
            var st = def.Find('S');
            A.Code.AppendLine("scr_mapdef" + world + ":");
            A.Code.AppendLine(string.Format("    .byte {0}, {1}, {2}, {3}   ; start x/y, nodes, bros", st.X, st.Y, def.Nodes.Count, def.Bros.Count));
            A.Code.AppendLine("    .byte SONG_" + Sym(def.Music) + ", 0");
            A.Code.AppendLine("    .addr scr_mapname" + world + ", scr_mapnodes" + world + ", scr_mapbros" + world + ", scr_mapgrid" + world);
            A.Code.AppendLine("scr_mapname" + world + ": .byte \"" + def.Name.ToUpperInvariant() + "\", 0");
            A.Code.AppendLine("scr_mapnodes" + world + ":   ; code x y kind level opens horiz / block-map items");
            A.Code.Append(nt);
            A.Code.AppendLine("scr_mapbros" + world + ":   ; x y item");
            foreach (var bro in def.Bros) A.Code.AppendLine(string.Format("    .byte {0}, {1}, {2}", bro.X, bro.Y, Math.Max(1, Array.IndexOf(ItemNames, (bro.Item ?? "hammer").ToUpperInvariant()))));
            A.Code.AppendLine("scr_mapgrid" + world + ":");
            A.Code.Append(gsb);
            if (world == 1)
            {
                A.Inc.AppendLine("MAPNODE_SIZE = 11\nNK_START = 0\nNK_DOT = 1\nNK_LEVEL = 2\nNK_FORTRESS = 3\nNK_AIRSHIP = 4\nNK_CASTLE = 5\nNK_TOAD = 6\nNK_SPADE = 7\nNK_LOCK = 8");
                for (int i = 1; i < ItemNames.Length; i++) A.Inc.AppendLine("IT_" + ItemNames[i] + " = " + i);
            }
            A.Code.AppendLine(".export scr_mapdef" + world);
            A.Code.AppendLine("scr_mapmaps" + world + ": .word " + string.Join(", ", new[] { "WATER1", "WATER2", "BADGE_L", "POP_INTRO", "POP_MSG", "POP_MENU4", "POP_MENU5", "POP_INV" }.Select(x => "MAP_" + N + "_" + x)));
            if (world == 8)
            {
                A.Code.AppendLine("scr_mapmaps: .addr 0, scr_mapmaps1, scr_mapmaps2, scr_mapmaps3, scr_mapmaps4, scr_mapmaps5, scr_mapmaps6, scr_mapmaps7, scr_mapmaps8");
                A.Code.AppendLine(".export scr_mapmaps");
            }
            // popups on BG2 (windows over the map, above the map sprites)
            s.Pool.AddFull("pop_intro", Draw(p => Hud.Window(p, 40, 60, 176, 56), 0, 224), true);
            s.Pool.AddFull("pop_msg", Draw(p => Hud.Window(p, 16, 8, 224, 24), 0, 224), true);
            s.Pool.AddFull("pop_menu4", Draw(p => Hud.Window(p, 64, 40, 128, 24 + 4 * 12), 0, 224), true);
            s.Pool.AddFull("pop_menu5", Draw(p => Hud.Window(p, 64, 40, 128, 24 + 5 * 12), 0, 224), true);
            s.Pool.AddFull("pop_inv", Draw(p => Hud.Window(p, 8, 104, 240, 84), 0, 224), true);
            Style(s, "WHITE", TxtStyle(0x30));
            Style(s, "GOLD", TxtStyle(0x28));
            Style(s, "GREEN", TxtStyle(0x2A));
            Style(s, "GREY", TxtStyle(0x10));
            if (world == 1) HudConsts(s);
            // sprites
            var walker = new[] { "mp.mario1", "mp.mario2" };
            for (int i = 0; i < 2; i++)
            {
                string img = walker[i];
                var im = AddSpr(s, "WALK" + (i + 1), Art.Get(img).W, SprImgPal(img, Art.Pal("mario")), SprImgPal(img, Art.Pal("luigi")));
                im.Slot = 0;
            }
            AddSpr(s, "HBRO", 16, SprImgPal("m.hbro", mapPal("m.hbro")));
            AddSpr(s, "AIRSHIP", 16, SprImgPal("m.airship", mapPal("m.airship")));
            var nsp = AddSpr(s, "NSPADE", 16, SprImgPal("m.spade", MapScreen.NSpadePal(true)), SprImgPal("m.spade", MapScreen.NSpadePal(false)));
            nsp.Slot = 1;
            CursorSprites(s);
            var box = AddSpr(s, "INVBOX", 0, Spr((p, x, y) => Hud.Box(p, x, y, 20, 20, 0x28)), Spr((p, x, y) => Hud.Box(p, x, y, 20, 20, 0x30)));
            box.Slot = 2;
            for (int i = 1; i < ItemNames.Length; i++)
            {
                Item it = (Item)i;
                AddSpr(s, "ITEM_" + ItemNames[i], 16, Spr((p, x, y) => MapScreen.DrawItemIcon(p, it, x, y, 0)));
            }
            HudSprites(s);
            // soft shadow under the walker (the C# darkens the ground: a dark translucent-looking ellipse)
            AddSpr(s, "SHADOW", 0, Spr((p, x, y) => { int[] half = { 4, 6, 4 }; for (int r = 0; r < 3; r++) p.FillRect(x + 8 - half[r], y + r, half[r] * 2, 1, Hud.Mix(0xF0C078, 0x000010, 96)); }));
            return s;
        }

        static string NodeImg(MapDef def, MapNode n, bool alt)
        {
            switch (n.Kind)
            {
                case "start": return "m.start";
                case "dot": return "m.dot";
                case "lock":
                {
                    bool horiz = def.At(n.X - 1, n.Y) == '-' || def.At(n.X + 1, n.Y) == '-';
                    return alt ? (horiz ? "m.path.h" : "m.path.v") : (horiz ? "m.lock.v" : "m.lock.h");
                }
                case "toad": return alt ? "m.toad.used" : "m.toad";
                case "spade": return alt ? "m.dot" : "m.spade";
                case "fortress": return alt ? "m.fortress.ruin" : "m.fortress";
                case "airship": return null;          // a bobbing sprite
                case "castle": return "m.castle";
                default: return alt ? "m.panel.clear" : "m.panel";
            }
        }
    }
}
