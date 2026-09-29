using System;
using System.Collections.Generic;
using SMB4.Engine;

namespace SMB4.Game
{
    public sealed partial class World
    {
        static readonly Dictionary<string, string> GroundSet = new Dictionary<string, string>
        {
            { "plains", "grass" }, { "jungle", "grass" }, { "desert", "sand" }, { "sea", "sand" }, { "sky", "cloud" }, { "ice", "snow" },
            { "underground", "rock" }, { "volcano", "rock" }, { "fortress", "stone" }, { "castle", "stone" }, { "machine", "metal" },
            { "airship", "wood" }, { "bonus", "wood" },
        };
        static readonly Dictionary<string, int> SkyColor = new Dictionary<string, int>
        {
            { "plains", 0x21 }, { "jungle", 0x2C }, { "desert", 0x21 }, { "sea", 0x21 }, { "sky", 0x31 }, { "ice", 0x11 },
            { "underground", 0x0F }, { "volcano", 0x0F }, { "fortress", 0x0F }, { "castle", 0x0F }, { "machine", 0x0F },
            { "airship", 0x21 }, { "bonus", 0x0F },
        };
        readonly Dictionary<string, ushort[]> palCache = new Dictionary<string, ushort[]>();

        public ushort[] ThemePal(string group)
        {
            string key = Area.Theme + "." + group;
            ushort[] p;
            if (palCache.TryGetValue(key, out p)) return p;
            p = Art.HasPal(key) ? Art.Pal(key) : Art.HasPal("default." + group) ? Art.Pal("default." + group) : Art.P(0x0F, 0x17, 0x27);
            palCache[key] = p;
            return p;
        }

        public int Backdrop
        {
            get
            {
                if (Area.Backdrop >= 0) return Area.Backdrop;
                string k = Area.Theme + ".sky";
                if (Art.HasPal(k)) return Art.Pal(k)[1];
                int c; return SkyColor.TryGetValue(Area.Theme, out c) ? c : 0x21;
            }
        }

        bool IsGround(int tx, int ty)
        {
            if (tx < 0 || tx >= W) { T e = ty >= 0 && ty < H ? TileAt(Math.Max(0, Math.Min(W - 1, tx)), ty) : T.Empty; return e == T.Ground || TileInfo.Slope(e); }
            if (ty >= H) return true;
            if (ty < 0) return false;
            T t = Tiles[ty * W + tx];
            return t == T.Ground || TileInfo.Slope(t);
        }

        string GroundPiece(int tx, int ty)
        {
            string set;
            if (!GroundSet.TryGetValue(Area.Theme, out set)) set = "grass";
            bool up = IsGround(tx, ty - 1);
            // the tile right under a slope continues the slope's grass band
            T above = TileAt(tx, ty - 1);
            if (above == T.SlopeUp && Art.Has("ground." + set + ".sub")) return "ground." + set + ".sub";
            if (above == T.SlopeDown && Art.Has("ground." + set + ".sdb")) return "ground." + set + ".sdb";
            if (set == "rock" || set == "stone" || set == "metal" || set == "wood") return "ground." + set + (up ? ".c" : ".t");
            bool dn = IsGround(tx, ty + 1), lf = IsGround(tx - 1, ty), rt = IsGround(tx + 1, ty);
            string s;
            if (!up)
            {
                // top surface; "itl"/"itr" = the foot of a wall rising up-left / up-right (tile art convention)
                bool wallL = lf && IsGround(tx - 1, ty - 1), wallR = rt && IsGround(tx + 1, ty - 1);
                if (wallL && !wallR) s = "itl";
                else if (wallR && !wallL) s = "itr";
                else s = !lf && rt ? "tl" : !rt && lf ? "tr" : "t";
            }
            else if (!dn) s = !lf ? "bl" : !rt ? "br" : "b";
            else if (!lf) s = "l";
            else if (!rt) s = "r";
            else s = "c";
            return "ground." + set + "." + s;
        }

        string BlockPiece(int tx, int ty, byte color)
        {
            Func<int, int, bool> same = (x, y) => x >= 0 && y >= 0 && x < W && y < H && (Tiles[y * W + x] == T.BigBlock || Tiles[y * W + x] == T.BigBlockBody) && Var[y * W + x] == color;
            bool up = same(tx, ty - 1), dn = same(tx, ty + 1), lf = same(tx - 1, ty), rt = same(tx + 1, ty);
            string v = !up ? "t" : !dn ? "b" : "";
            string h = !lf ? "l" : !rt ? "r" : "";
            if (v == "" && h == "") return "block.c";
            return "block." + v + h;
        }

        /// <summary>Draws one tile image (also used by bump animations).</summary>
        public void DrawTileImage(Ppu ppu, T t, int tx, int ty, int sx, int sy, bool asSprite)
        {
            string img = null; ushort[] pal = null; bool fh = false, fv = false;
            int f8 = Frame / 8;
            switch (t)
            {
                case T.Ground: img = GroundPiece(tx, ty); pal = ThemePal("ground"); break;
                case T.Hard: img = "hard"; pal = ThemePal("hard"); break;
                case T.Brick: img = "brick"; pal = ThemePal("brick"); break;
                case T.QBlock: img = "qblock." + (1 + (f8 % 4)); pal = ThemePal("qblock"); break;
                case T.Used: img = "used"; pal = ThemePal("used"); break;
                case T.Wood: img = "wood"; pal = ThemePal("wood"); break;
                case T.Note: img = (f8 % 2 == 0) ? "note.1" : "note.2"; pal = ThemePal("note"); break;
                case T.Ice: img = "ice"; pal = ThemePal("ice"); break;
                case T.Muncher: img = (f8 % 2 == 0) ? "muncher.1" : "muncher.2"; pal = ThemePal("muncher"); break;
                case T.Spike: img = "spike"; pal = ThemePal("spike"); break;
                case T.Semi:
                {
                    bool l = TileAt(tx - 1, ty) == T.Semi, r = TileAt(tx + 1, ty) == T.Semi;
                    img = !l ? "semi.l" : !r ? "semi.r" : "semi.c"; pal = ThemePal("semi"); break;
                }
                case T.Cloud:
                {
                    bool l = TileAt(tx - 1, ty) == T.Cloud, r = TileAt(tx + 1, ty) == T.Cloud;
                    img = !l ? "cloud.l" : !r ? "cloud.r" : "cloud.c"; pal = ThemePal("cloud"); break;
                }
                case T.BigBlock:
                case T.BigBlockBody:
                {
                    byte c = Var[ty * W + tx];
                    img = BlockPiece(tx, ty, c); pal = ThemePal("block" + Math.Max(1, Math.Min(4, (int)c))); break;
                }
                case T.PipeTL: img = "pipe.tl"; pal = ThemePal("pipe"); break;
                case T.PipeTR: img = "pipe.tr"; pal = ThemePal("pipe"); break;
                case T.PipeL: img = "pipe.l"; pal = ThemePal("pipe"); break;
                case T.PipeR: img = "pipe.r"; pal = ThemePal("pipe"); break;
                case T.PipeBL: img = "pipe.bl"; pal = ThemePal("pipe"); break;
                case T.PipeBR: img = "pipe.br"; pal = ThemePal("pipe"); break;
                case T.HPipeMouthT: img = "hpipe.ml"; pal = ThemePal("pipe"); fh = Var[ty * W + tx] == 1; break;
                case T.HPipeMouthB: img = "hpipe.mlb"; pal = ThemePal("pipe"); fh = ty > 0 && Var[(ty - 1) * W + tx] == 1; break;
                case T.HPipeT: img = "hpipe.t"; pal = ThemePal("pipe"); break;
                case T.HPipeB: img = "hpipe.b"; pal = ThemePal("pipe"); break;
                case T.CannonTop: img = "cannon.top"; pal = ThemePal("cannon"); break;
                case T.CannonMid: img = "cannon.mid"; pal = ThemePal("cannon"); break;
                case T.CannonL: img = "cannon.top"; pal = ThemePal("cannon"); break;
                case T.CannonR: img = "cannon.top"; pal = ThemePal("cannon"); fh = true; break;
                case T.Coin: img = "coin." + (1 + ((Frame / 8) % 4)); pal = Art.Pal("coin"); break;
                case T.Vine: img = TileAt(tx, ty - 1) == T.Vine ? "vine" : "vine.top"; pal = ThemePal("vine"); break;
                case T.DoorTop: img = "door.top"; pal = ThemePal("door"); break;
                case T.DoorBot: img = "door.bot"; pal = ThemePal("door"); break;
                case T.LavaTop: img = ((Frame / 16) % 2 == 0) ? "lava.top1" : "lava.top2"; pal = ThemePal("lava"); break;
                case T.Lava: img = "lava"; pal = ThemePal("lava"); break;
                case T.GoalFloor: img = "goal.floor"; pal = ThemePal("goal"); break;
                case T.SlopeUp:
                case T.SlopeDown:
                {
                    string set;
                    if (!GroundSet.TryGetValue(Area.Theme, out set)) set = "grass";
                    if (set == "cloud" || set == "metal" || set == "wood") set = "grass";
                    img = "ground." + set + (t == T.SlopeUp ? ".su" : ".sd");
                    pal = ThemePal("ground");
                    break;
                }
                case T.ConveyorL: img = ((Frame / 4) % 2 == 0 || PSwitchTimer > 0) ? "conveyor.1" : "conveyor.2"; pal = ThemePal("conveyor"); fh = true; break;
                case T.ConveyorR: img = ((Frame / 4) % 2 == 0 || PSwitchTimer > 0) ? "conveyor.1" : "conveyor.2"; pal = ThemePal("conveyor"); break;
                default: return;
            }
            if (asSprite) ppu.Spr(Art.Get(img), sx, sy, pal, fh, fv);
            else ppu.Tile(Art.Get(img), sx, sy, pal, fh, fv);
        }

        /// <summary>Decor palette: a per-image palette "decor.IMG" (or "THEME.decor.IMG") wins over the theme's decor palette.</summary>
        ushort[] DecorPal(string img, ushort[] fallback)
        {
            string k1 = Area.Theme + ".decor." + img, k2 = "decor." + img;
            return Art.HasPal(k1) ? Art.Pal(k1) : Art.HasPal(k2) ? Art.Pal(k2) : fallback;
        }

        /// <summary>
        /// 16-bit era backgrounds (drawn behind everything, no collision, never hide "behind" sprites):
        ///  * sky gradient: palette "THEME.grad" = colors top to bottom, split into even horizontal bands over the
        ///    192-px playfield (screen-fixed, like SNES HDMA gradients);
        ///  * parallax layers: images "para.THEME.1" (farthest) .. "para.THEME.4" (nearest), each with palette
        ///    "para.THEME.N"; tiled horizontally, bottom-anchored to the playfield bottom when the camera is at the
        ///    area floor. Horizontal scroll factor 1/8, 1/4, 3/8, 1/2; vertical factor half of that.
        ///    Optional palette "para.THEME.N.y" with one entry = extra downward offset in px (use for mid-air layers).
        /// </summary>
        void DrawSkyAndParallax(Ppu ppu, int camX, int camY)
        {
            string th = Area.Theme;
            if (Art.HasPal(th + ".grad"))
            {
                var g = Art.Pal(th + ".grad");
                int n = g.Length - 1;
                for (int k = 0; k < n; k++)
                {
                    int y0 = 192 * k / n, y1 = 192 * (k + 1) / n;
                    ppu.FillRect(0, y0, 256, y1 - y0, g[k + 1]);
                }
            }
            DrawParallax(ppu, camX, camY, ".");
        }

        /// <summary>Parallax layers "para.THEME" + infix + N (N = 1 farthest .. 4 nearest).</summary>
        void DrawParallax(Ppu ppu, int camX, int camY, string infix)
        {
            string th = Area.Theme;
            int floorCam = Math.Max(0, LevelPxH - 192);
            for (int L = 1; L <= 4; L++)
            {
                string name = "para." + th + infix + L;
                if (!Art.Has(name)) continue;
                var img = Art.Get(name);
                var pal = Art.HasPal(name) ? Art.Pal(name) : Art.PreviewPalFor(name);
                int num = L == 1 ? 1 : L == 2 ? 2 : L == 3 ? 3 : 4;      // factor num/8
                int ox = (int)(((long)camX * num / 8) % img.W);
                int extraY = Art.HasPal(name + ".y") ? Art.Pal(name + ".y")[1] : 0;
                int y = 192 - img.H + extraY + (int)((long)(floorCam - camY) * num / 16);
                if (y >= 192 || y + img.H <= 0) continue;
                for (int x = -ox; x < 256; x += img.W) ppu.Back(img, x, y, pal);
            }
        }

        public void Render(Ppu ppu)
        {
            int shake = ShakeTimer > 0 ? ((ShakeTimer & 2) != 0 ? 2 : -2) : 0;
            int camX = CamX, camY = CamY + shake;
            ppu.ClipTop = 0; ppu.ClipBottom = 192; ppu.ClipLeft = 0; ppu.ClipRight = 256;
            ppu.ClearMask();
            ppu.FillRect(0, 0, 256, 192, Backdrop);
            if (Area.Backdrop < 0) DrawSkyAndParallax(ppu, camX, camY);

            // dark "backstage" behind the goal: one screen tall around the goal row (matters in tall areas)
            int goalPx = Area.GoalX >= 0 ? (Area.GoalX - 2) * 16 : int.MaxValue;
            if (goalPx < camX + 256)
            {
                int top = Math.Max(0, (Area.GoalY - 12) * 16 - camY), bot = Math.Min(192, (Area.GoalY + 4) * 16 - camY);
                if (bot > top) ppu.FillRect(Math.Max(0, goalPx - camX), top, 256, bot - top, 0x0F);
            }

            int tx0 = FloorDiv(camX, 16), ty0 = FloorDiv(camY, 16);
            // water body behind everything
            if (Area.WaterRow >= 0)
            {
                var wp = ThemePal("water");
                for (int ty = Math.Max(ty0, Area.WaterRow); ty <= ty0 + 12; ty++)
                    for (int tx = tx0; tx <= tx0 + 16; tx++)
                    {
                        if (TileAt(tx, ty) != T.Empty && TileAt(tx, ty) != T.Coin) continue;
                        string img = ty == Area.WaterRow && Area.WaterRow > 0 ? ((Frame / 16) % 2 == 0 ? "water.top1" : "water.top2") : "water";
                        if (ty == Area.WaterRow || Area.WaterRow == 0 && false) ppu.Back(Art.Get(img), tx * 16 - camX, ty * 16 - camY, wp);
                        else if (Art.Has("water") && ty > Area.WaterRow) ppu.Back(Art.Get("water"), tx * 16 - camX, ty * 16 - camY, wp);
                    }
                // underwater parallax: "para.THEME.w1".."w4" drawn over the water body, below the surface row
                int ct = ppu.ClipTop;
                ppu.ClipTop = Math.Max(ct, Math.Min(192, Area.WaterRow * 16 - camY + (Area.WaterRow > 0 ? 16 : 0)));
                DrawParallax(ppu, camX, camY, ".w");
                ppu.ClipTop = ct;
            }
            // decor
            var dp = ThemePal("decor");
            foreach (var d in Area.Decor)
            {
                int sx = d.X * 16 - camX, sy = d.Y * 16 - camY;
                if (sx < -16 || sx > 256 || sy < -16 || sy > 192) continue;
                if (d.X * 16 >= goalPx) continue;
                string img = d.Img;
                if (img == "torch.1" && (Frame / 8) % 2 == 1) img = "torch.2";
                // scenery never hides "behind" sprites (a piranha rising in front of a bush must stay visible)
                ppu.Back(Art.Get(img), sx, sy, DecorPal(img, dp));
            }
            // entities drawn behind tiles (piranhas in pipes etc.) are handled by the behind flag; tiles now
            for (int ty = ty0; ty <= ty0 + 12; ty++)
            {
                if (ty < 0 || ty >= H) continue;
                for (int tx = tx0; tx <= tx0 + 16; tx++)
                {
                    if (tx < 0 || tx >= W) continue;
                    int i = ty * W + tx;
                    T t = Tiles[i];
                    if (t == T.Empty || t == T.Barrier || t == T.HiddenBlock || Hidden[i] != 0) continue;
                    if (t == T.Ground && tx * 16 >= goalPx) { ppu.Tile(Art.Get("goal.floor"), tx * 16 - camX, ty * 16 - camY, ThemePal("goal")); continue; }
                    DrawTileImage(ppu, t, tx, ty, tx * 16 - camX, ty * 16 - camY, false);
                }
            }
            // entities: behind-BG ones first (they only show where the background is transparent)
            foreach (var e in Ents) if (e.Behind && !e.Remove && e != P.Carrying) e.Draw(ppu, camX, camY);
            foreach (var e in Ents) if (!e.Behind && !e.Remove && e.Class == EntClass.Platform) e.Draw(ppu, camX, camY);
            foreach (var e in Ents) if (!e.Behind && !e.Remove && e.Class != EntClass.Effect && e.Class != EntClass.Platform && e != P.Carrying && e.Class != EntClass.Special) e.Draw(ppu, camX, camY);
            foreach (var e in Ents) if (!e.Behind && !e.Remove && e.Class == EntClass.Special) e.Draw(ppu, camX, camY);
            if (P.State != PState.Dying) P.Draw(ppu, camX, camY);
            if (P.Carrying != null) P.Carrying.Draw(ppu, camX, camY);
            foreach (var e in Ents) if (!e.Behind && !e.Remove && e.Class == EntClass.Effect) e.Draw(ppu, camX, camY);
            if (P.State == PState.Dying) P.Draw(ppu, camX, camY);
            ppu.ResetClip();
        }
    }
}
