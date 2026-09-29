using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SMB4.Engine;
using SMB4.Game;

namespace SMB4.Tools
{
    public static partial class SnesExport
    {
        /// <summary>Export step: sprites (owner: sprites agent). See snes/SPRITES.md.</summary>
        static int ExportSprites(string outDir) { return SnesSprites.Export(outDir); }
    }

    /// <summary>
    /// SNES sprite converter (snes/SPRITES.md):
    ///  * gen/spr_ids.inc      metasprite ids (one per image+palette the C# game draws), FORM_*/POSE_*/PPAL_* ids
    ///  * gen/spr_data.s       data banks (BANK32-39): global enemy/item CHR (native palette slots, 4bpp "nibble chunky"),
    ///                         per-level OBJ sets (palettes 9-15, slot->color LUTs, VRAM load list, metasprite records),
    ///                         player frames (pre-composited with the real PlayerDraw code, 4bpp planar 16x16 pieces).
    /// </summary>
    static class SnesSprites
    {
        // sprites own BANK32..BANK39 (declared in snes/DESIGN.md API requests); filled from 39 downwards
        const int FirstBank = 32, LastBank = 39, BankSize = 0x8000;
        const int MaxCells = 120;      // 16x16 cells in OBJ tiles 32-511 (15 row pairs x 8)
        const int NBins = 7;           // OBJ palettes 9-15

        // ------------------------------------------------------------------ catalog
        sealed class Entry { public string Id, Img, Pal; public int Index; public bool Themed; }
        static readonly List<Entry> plain = new List<Entry>();
        static readonly List<Entry> themed = new List<Entry>();
        static readonly Dictionary<string, List<Entry>> features = new Dictionary<string, List<Entry>>();
        static readonly Dictionary<string, string> themedGroup = new Dictionary<string, string>();

        static void Def(string feature, string id, string img, string pal)
        {
            Entry e = plain.FirstOrDefault(x => x.Id == id);
            if (e == null) { e = new Entry { Id = id, Img = img, Pal = pal, Index = plain.Count }; plain.Add(e); }
            List<Entry> l;
            if (!features.TryGetValue(feature, out l)) features[feature] = l = new List<Entry>();
            if (!l.Contains(e)) l.Add(e);
        }
        static void Frames(string feature, string idPre, string imgPre, int n, string pal)
        {
            for (int i = 1; i <= n; i++) Def(feature, idPre + i, imgPre + i, pal);
        }
        static void DefT(string feature, string id, string img, string group)
        {
            Entry e = themed.FirstOrDefault(x => x.Id == id);
            if (e == null) { e = new Entry { Id = id, Img = img, Pal = group, Index = themed.Count, Themed = true }; themed.Add(e); }
            List<Entry> l;
            if (!features.TryGetValue(feature, out l)) features[feature] = l = new List<Entry>();
            if (!l.Contains(e)) l.Add(e);
        }

        /// <summary>id = SPR_ + art name uppercased ('.' -> '_') + suffix for a non-default palette (engine request, DESIGN.md).</summary>
        static void D(string feature, string img, string pal, string suffix = "")
        {
            Def(feature, "SPR_" + img.Replace('.', '_').ToUpperInvariant() + suffix, img, pal);
        }
        static void DN(string feature, string imgPre, int n, string pal, string suffix = "")
        {
            for (int i = 1; i <= n; i++) D(feature, imgPre + i, pal, suffix);
        }

        static void BuildCatalog()
        {
            plain.Clear(); themed.Clear(); features.Clear();
            // ---- always loaded (effects, player projectiles, coins, score digits)
            DN("common", "coin.", 4, "coin");
            for (int i = 0; i <= 9; i++) D("common", "tiny." + i, "fx");
            D("common", "tiny.up", "fx");
            DN("common", "puff.", 3, "fx");
            DN("common", "sparkle.", 2, "fx");
            D("common", "dust", "fx");
            DN("common", "fireball.", 4, "fireball");   // player + Venus fire
            DN("common", "hammer.", 4, "hammer");       // Hammer Suit + Hammer Bro
            D("common", "mushroom", "mushroom");
            // ---- block contents
            D("flower", "flower.1", "flower");
            D("flower", "flower.1", "flower.2", "_FLASH");
            D("leaf", "leaf", "leaf");
            D("star", "star", "star");
            D("star", "star", "star.2", "_2");
            D("star", "star", "star.3", "_3");
            D("star", "star", "star.4", "_4");
            D("oneup", "mushroom", "oneup", "_ONEUP");
            D("vine", "vine.sprout", Art.HasPal("vine.sprout") ? "vine.sprout" : "$T:vine");
            D("pswitch", "pswitch", "pswitch");
            D("pswitch", "pswitch.flat", "pswitch");
            D("goal", "card.mushroom", "mushroom");
            D("goal", "card.flower", "flower");
            D("goal", "card.star", "star");
            // ---- enemies (spawn codes, data/levels/README.md)
            D("g", "goomba.1", "goomba");
            D("g", "goomba.flat", "goomba");
            DN("wing", "wing.", 2, "wing");
            DN("kg", "koopa.", 2, "koopa.green");
            DN("kg", "shell.", 4, "koopa.green");
            DN("kr", "koopa.", 2, "koopa.red", "_RED");
            DN("kr", "shell.", 4, "koopa.red", "_RED");
            DN("z", "buzzy.", 2, "buzzy");
            DN("z", "bshell.", 4, "buzzy");
            DN("s", "spiny.", 2, "spiny");
            DN("i", "spiny.", 2, "spiny");
            DN("i", "spinyegg.", 2, "spiny");
            D("i", "lakitu", "lakitu");
            DN("e", "piranha.", 2, "plant");
            DN("v", "venus.", 2, "plant");
            DN("c", "cheep.", 2, "cheep");
            DN("q", "blooper.", 2, "blooper");
            DN("u", "boo.", 2, "boo");
            DN("t", "thwomp.", 2, "thwomp");
            DN("d", "bones.", 2, "bones");
            D("d", "bones.pile", "bones");
            D("x", "podoboo", "podoboo");
            DN("y", "bro.", 2, "bro");
            D("y", "bro.throw", "bro");
            DN("w", "rocky.", 2, "rocky");
            D("w", "wrench.1", "rocky");
            DN("a", "bobomb.", 2, "bobomb");
            D("a", "bobomb.1", "bobomb.flash", "_FLASH");
            DN("a", "puff.", 3, "$boom.hot", "_HOT");     // Explosion (fire-tinted fx palettes)
            DN("a", "puff.", 3, "$boom.warm", "_WARM");
            D("b", "bill", "bill");
            D("<", "cannonball", "cannonball");
            D("R", "rotodisc", "rotodisc");
            D("R", "rotodisc", "rotodisc.2", "_2");
            Def("hb", "SPR_CHEST", "$chest", "$chest");
            // ---- bosses
            foreach (var n in new[] { "stand", "run1", "run2", "jump", "hurt" }) D("Z", "boomboom." + n, "boomboom");
            D("Z", "boomboom.hurt", "boomboom.flash", "_FLASH");
            D("Z", "orb", Art.HasPal("orb") ? "orb" : "card");
            foreach (var n in new[] { "stand", "walk", "jump", "shell", "cast" }) D("K", "koopaling." + n, "$koopaling");
            foreach (var n in new[] { "stand", "walk", "jump", "shell", "cast" }) D("K", "koopaling." + n, "koopaling.flash", "_FLASH");
            Def("K", "SPR_KL", "$kl", "$kl");     // this world's Koopaling accessory (kl.lemmy ... kl.ludwig)
            D("K", "wand", "wand");
            DN("K", "ring.", 2, "ring.a");
            DN("K", "ring.", 2, "ring.b", "_B");
            foreach (var n in new[] { "stand", "walk", "jump", "breath" }) D("Y", "bowser." + n, "bowser");
            foreach (var n in new[] { "stand", "walk", "jump", "breath" }) D("Y", "bowser." + n, "bowser.flash", "_FLASH");
            DN("Y", "bowserfire.", 2, "bowserfire");
            // ---- theme-colored tile images drawn as sprites (resolved per area theme, see spr_area)
            DefT("brick", "SPR_BUMP_BRICK", "brick", "brick");
            DefT("brick", "SPR_DEBRIS", "debris", "brick");
            DefT("used", "SPR_BUMP_USED", "used", "used");
            DefT("note", "SPR_BUMP_NOTE", "note.1", "note");
            DefT("note", "SPR_BUMP_NOTE_2", "note.2", "note");
            DefT("wood", "SPR_BUMP_WOOD", "wood", "wood");
            DefT("water", "SPR_SPLASH_1", "splash.1", "water");
            DefT("water", "SPR_SPLASH_2", "splash.2", "water");
            DefT("lift", "SPR_SEMI_L", "semi.l", "semi");
            DefT("lift", "SPR_SEMI_C", "semi.c", "semi");
            DefT("lift", "SPR_SEMI_R", "semi.r", "semi");
            DefT("goal", "SPR_GOAL_BOX", "goal.box", "goal");
        }

        // ------------------------------------------------------------------ colors
        static ushort Bgr(ushort colorIndex) { return SnesExport.Bgr555(colorIndex); }
        static double Dist(ushort a, ushort b)
        {
            int r1 = (a & 31) << 3, g1 = ((a >> 5) & 31) << 3, b1 = ((a >> 10) & 31) << 3;
            int r2 = (b & 31) << 3, g2 = ((b >> 5) & 31) << 3, b2 = ((b >> 10) & 31) << 3;
            double rm = (r1 + r2) / 2.0, dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return (2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db;
        }
        static ushort SlotColor(ushort[] pal, int v)
        {
            int last = pal.Length - 1;
            return Bgr(pal[v <= last ? v : last]);
        }

        // ------------------------------------------------------------------ palettes/images resolved per level
        static readonly Dictionary<string, Img> synthImg = new Dictionary<string, Img>();
        static readonly Dictionary<string, ushort[]> synthPal = new Dictionary<string, ushort[]>();

        static Img GetImg(string name)
        {
            Img i;
            if (synthImg.TryGetValue(name, out i)) return i;
            return Art.Get(name);
        }
        static ushort[] GetPal(string name)
        {
            ushort[] p;
            if (synthPal.TryGetValue(name, out p)) return p;
            return Art.Pal(name);
        }

        static void BuildSynth()
        {
            synthImg.Clear(); synthPal.Clear();
            // Explosion fire-tinted fx palettes (private static Explosion.BoomPal)
            var bm = typeof(Explosion).GetMethod("BoomPal", BindingFlags.NonPublic | BindingFlags.Static);
            synthPal["$boom.hot"] = bm != null ? (ushort[])bm.Invoke(null, new object[] { true }) : Art.Pal("fx");
            synthPal["$boom.warm"] = bm != null ? (ushort[])bm.Invoke(null, new object[] { false }) : Art.Pal("fx");
            // Treasure chest: C# fallback FillRect drawing (Items.cs TreasureChest.Draw) unless chest.closed exists
            if (Art.Has("chest.closed"))
            {
                synthImg["$chest"] = Art.Get("chest.closed");
                synthPal["$chest"] = Art.PreviewPalFor("chest.closed");
            }
            else
            {
                var img = new Img(16, 16);
                Action<int, int, int, int, byte> fill = (x, y, w, h, v) => { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) img.P[yy * 16 + xx] = v; };
                fill(0, 2, 16, 14, 1); fill(1, 3, 14, 12, 2); fill(1, 6, 14, 2, 3); fill(6, 8, 4, 4, 4);
                synthImg["$chest"] = img;
                synthPal["$chest"] = new ushort[] { 0, 0x0F, 0x17, 0x28, 0x38 };
            }
        }

        static string ThemePalName(string theme, string group)
        {
            string key = theme + "." + group;
            if (Art.HasPal(key)) return key;
            if (Art.HasPal("default." + group)) return "default." + group;
            return "$fallback." + group;
        }

        static readonly string[] KoopalingNames = { "LEMMY", "ROY", "IGGY", "WENDY", "MORTON", "LARRY", "LUDWIG" };

        sealed class Combo
        {
            public string Img, PalName; public ushort[] Pal; public Img Image;
            public Group G; public ImgChr Chr;
            public int C0, S0;             // VRAM allocation
            public string RecLabel;
        }
        sealed class Group
        {
            public string Name; public ushort[] Pal;
            public Dictionary<ushort, double> W = new Dictionary<ushort, double>();
            public int Bin; public byte[] Lut = new byte[16]; public int Index;
        }

        static Combo Resolve(string img, string pal, LevelDef def, string theme)
        {
            string wn = KoopalingNames[Math.Max(0, Math.Min(6, WorldNum(def) - 1))].ToLowerInvariant();
            if (img == "$kl") { img = "kl." + wn; pal = Art.PreviewPalName(img) ?? "koopaling"; }
            if (pal == "$koopaling") pal = Art.HasPal("koopaling." + wn) ? "koopaling." + wn : "koopaling";
            if (pal.StartsWith("$T:")) pal = ThemePalName(theme, pal.Substring(3));
            if (!img.StartsWith("$") && !Art.Has(img)) return null;
            ushort[] p;
            if (pal.StartsWith("$fallback.")) { p = Art.P(0x0F, 0x17, 0x27); }
            else if (pal.StartsWith("$")) p = GetPal(pal);
            else if (Art.HasPal(pal)) p = Art.Pal(pal);
            else return null;
            return new Combo { Img = img, PalName = pal, Pal = p, Image = GetImg(img) };
        }

        static int WorldNum(LevelDef d)
        {
            int n;
            if (d.Id.Length > 0 && int.TryParse(d.Id.Substring(0, 1), out n)) return n;
            return 1;
        }

        // ------------------------------------------------------------------ image slicing (enemy/item CHR)
        sealed class Piece { public int Dx, Dy; public bool Big; }
        sealed class ImgChr
        {
            public string Name; public int W, H; public int FirstTile, N16, N8;
            public List<Piece> Cells = new List<Piece>(), Singles = new List<Piece>();
        }
        static readonly Dictionary<string, ImgChr> chrByImg = new Dictionary<string, ImgChr>();
        static readonly List<byte[]> globalTiles = new List<byte[]>();   // 64 native pixel values each

        static byte Px(Img img, int x, int y) { return x < 0 || y < 0 || x >= img.W || y >= img.H ? (byte)0 : img.P[y * img.W + x]; }
        static bool QuadEmpty(Img img, int x0, int y0)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (Px(img, x0 + x, y0 + y) != 0) return false;
            return true;
        }
        static byte[] Tile(Img img, int x0, int y0)
        {
            var t = new byte[64];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) t[y * 8 + x] = Px(img, x0 + x, y0 + y);
            return t;
        }

        static ImgChr Chr(string name, Img img)
        {
            ImgChr c;
            if (chrByImg.TryGetValue(name, out c)) return c;
            c = new ImgChr { Name = name, W = img.W, H = img.H };
            for (int cy = 0; cy < img.H; cy += 16)
                for (int cx = 0; cx < img.W; cx += 16)
                {
                    bool[] ne = new bool[4]; int n = 0;
                    for (int q = 0; q < 4; q++) { ne[q] = !QuadEmpty(img, cx + (q & 1) * 8, cy + (q >> 1) * 8); if (ne[q]) n++; }
                    if (n >= 3) c.Cells.Add(new Piece { Dx = cx, Dy = cy, Big = true });   // singles: transparent halves would still count toward the 34-slivers-per-line limit
                    else for (int q = 0; q < 4; q++) if (ne[q]) c.Singles.Add(new Piece { Dx = cx + (q & 1) * 8, Dy = cy + (q >> 1) * 8 });
                }
            c.FirstTile = globalTiles.Count;
            foreach (var p in c.Cells)
                for (int q = 0; q < 4; q++) globalTiles.Add(Tile(img, p.Dx + (q & 1) * 8, p.Dy + (q >> 1) * 8));
            foreach (var p in c.Singles) globalTiles.Add(Tile(img, p.Dx, p.Dy));
            c.N16 = c.Cells.Count; c.N8 = c.Singles.Count;
            chrByImg[name] = c;
            return c;
        }

        // ------------------------------------------------------------------ palette packing
        sealed class Cluster { public List<Group> Groups = new List<Group>(); public Dictionary<ushort, double> W = new Dictionary<ushort, double>(); }

        static Cluster Merge(Cluster a, Cluster b)
        {
            var c = new Cluster();
            c.Groups.AddRange(a.Groups); c.Groups.AddRange(b.Groups);
            foreach (var kv in a.W) c.W[kv.Key] = kv.Value;
            foreach (var kv in b.W) { double w; c.W.TryGetValue(kv.Key, out w); c.W[kv.Key] = w + kv.Value; }
            return c;
        }

        /// <summary>Cost of absorbing b's colors into a: weight * distance to a's nearest color for colors a lacks.</summary>
        static double AbsorbCost(Cluster a, Cluster b)
        {
            int union = a.W.Count + b.W.Keys.Count(k => !a.W.ContainsKey(k));
            if (union <= 15) return 0;
            double cost = 0;
            var big = a.W.Count >= b.W.Count ? a : b; var small = big == a ? b : a;
            foreach (var kv in small.W)
            {
                if (big.W.ContainsKey(kv.Key)) continue;
                double best = double.MaxValue;
                foreach (var k in big.W.Keys) best = Math.Min(best, Dist(kv.Key, k));
                cost += kv.Value * best;
            }
            return cost * (union - 15);
        }

        /// <summary>Reduces a color set to at most n colors by repeatedly merging the cheapest pair into the heavier color.</summary>
        static Dictionary<ushort, ushort> Quantize(Dictionary<ushort, double> w, int n, out List<ushort> reps, ref double err)
        {
            var map = new Dictionary<ushort, ushort>();
            var live = new Dictionary<ushort, double>(w);
            foreach (var k in w.Keys) map[k] = k;
            while (live.Count > n)
            {
                ushort ba = 0, bb = 0; double best = double.MaxValue;
                var keys = live.Keys.ToList();
                for (int i = 0; i < keys.Count; i++)
                    for (int j = i + 1; j < keys.Count; j++)
                    {
                        double c = Dist(keys[i], keys[j]) * Math.Min(live[keys[i]], live[keys[j]]);
                        if (c < best) { best = c; ba = keys[i]; bb = keys[j]; }
                    }
                ushort keep = live[ba] >= live[bb] ? ba : bb, drop = keep == ba ? bb : ba;
                err += best;
                live[keep] += live[drop]; live.Remove(drop);
                foreach (var k in map.Keys.ToList()) if (map[k] == drop) map[k] = keep;
            }
            reps = live.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            return map;
        }

        // ------------------------------------------------------------------ per-level OBJ set
        sealed class LevelSet
        {
            public string Id; public int NAreas, StartArea;
            public List<Group> Groups = new List<Group>();
            public List<Combo> Combos = new List<Combo>();
            public ushort[][] Bins = new ushort[NBins][];
            public Combo[] ById;                  // plain ids
            public Combo[,] Themed;               // [area, themed id]
            public int CellsUsed, SinglesUsed; public double QuantErr; public List<string> Warn = new List<string>();
        }

        static LevelSet BuildLevel(string id, LevelDef def)
        {
            var ls = new LevelSet { Id = id, NAreas = Math.Max(1, def.Areas.Count), StartArea = def.StartArea };
            // ---- features used by the level
            var feats = new HashSet<string> { "common" };
            var tiles = new HashSet<T>();
            foreach (var a in def.Areas)
            {
                foreach (var s in a.Spawns)
                {
                    char c = s.Code;
                    switch (c)
                    {
                        case 'g': feats.Add("g"); break;
                        case 'p': feats.Add("g"); feats.Add("wing"); break;
                        case 'k': feats.Add("kg"); break;
                        case 'r': feats.Add("kr"); break;
                        case 'j': feats.Add("kg"); feats.Add("wing"); break;
                        case 'f': case 'n': feats.Add("kr"); feats.Add("wing"); break;
                        case 'l': feats.Add("c"); break;
                        case '>': feats.Add("<"); break;
                        case 'P': feats.Add("pswitch"); break;
                        case '-': feats.Add("used"); break;
                        case '_': case ':': feats.Add("lift"); break;
                        case 'm': break;   // mushroom is common
                        default: feats.Add(c.ToString()); break;
                    }
                }
                foreach (var t in a.Tiles) tiles.Add(t);
                foreach (var ct in a.Contents)
                    switch (ct)
                    {
                        case Content.Flower: feats.Add("flower"); break;
                        case Content.Leaf: feats.Add("leaf"); break;
                        case Content.Star: feats.Add("star"); break;
                        case Content.OneUp: feats.Add("oneup"); break;
                        case Content.Vine: feats.Add("vine"); break;
                        case Content.PSwitch: feats.Add("pswitch"); break;
                    }
                if (a.GoalX >= 0) feats.Add("goal");
                if (a.WaterRow >= 0) feats.Add("water");
            }
            if (tiles.Contains(T.Brick)) { feats.Add("brick"); feats.Add("used"); }
            if (tiles.Contains(T.QBlock) || tiles.Contains(T.HiddenBlock) || tiles.Contains(T.Used)) feats.Add("used");
            if (tiles.Contains(T.Note)) feats.Add("note");
            if (tiles.Contains(T.Wood)) feats.Add("wood");
            if (feats.Contains("y") && id == "hb") feats.Add("hb");

            // ---- resolve entries -> combos
            var byKey = new Dictionary<string, Combo>();
            Func<string, string, string, Combo> get = (img, pal, theme) =>
            {
                var c = Resolve(img, pal, def, theme);
                if (c == null) return null;
                string key = c.Img + "|" + c.PalName;
                Combo old;
                if (byKey.TryGetValue(key, out old)) return old;
                byKey[key] = c; ls.Combos.Add(c);
                return c;
            };
            ls.ById = new Combo[plain.Count];
            ls.Themed = new Combo[ls.NAreas, themed.Count];
            var order = new List<string> { "common" };
            order.AddRange(features.Keys.Where(k => k != "common"));
            foreach (var f in order)
            {
                if (!feats.Contains(f)) continue;
                foreach (var e in features[f])
                {
                    if (!e.Themed) { if (ls.ById[e.Index] == null) ls.ById[e.Index] = get(e.Img, e.Pal, def.Areas.Count > 0 ? def.Areas[0].Theme : "plains"); }
                    else for (int a = 0; a < ls.NAreas; a++)
                            if (ls.Themed[a, e.Index] == null)
                                ls.Themed[a, e.Index] = get(e.Img, "$T:" + e.Pal, a < def.Areas.Count ? def.Areas[a].Theme : "plains");
                }
            }

            // ---- palette groups with color weights
            var groups = new Dictionary<string, Group>();
            foreach (var c in ls.Combos)
            {
                Group g;
                if (!groups.TryGetValue(c.PalName, out g)) { g = new Group { Name = c.PalName, Pal = c.Pal }; groups[c.PalName] = g; ls.Groups.Add(g); }
                c.G = g;
                foreach (var v in c.Image.P)
                {
                    if (v == 0) continue;
                    ushort col = SlotColor(c.Pal, v);
                    double w; g.W.TryGetValue(col, out w); g.W[col] = w + 1;
                }
            }
            for (int i = 0; i < ls.Groups.Count; i++) ls.Groups[i].Index = i;

            // ---- pack groups into 7 OBJ palettes
            var clusters = ls.Groups.Select(g => { var cl = new Cluster(); cl.Groups.Add(g); foreach (var kv in g.W) cl.W[kv.Key] = kv.Value; return cl; }).ToList();
            while (true)   // phase 1: free merges (union fits in 15), most shared colors first
            {
                int bi = -1, bj = -1, bestShared = -1, bestUnion = 99;
                for (int i = 0; i < clusters.Count; i++)
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        int shared = clusters[j].W.Keys.Count(k => clusters[i].W.ContainsKey(k));
                        int union = clusters[i].W.Count + clusters[j].W.Count - shared;
                        if (union > 15) continue;
                        if (shared > bestShared || shared == bestShared && union < bestUnion) { bestShared = shared; bestUnion = union; bi = i; bj = j; }
                    }
                if (bi < 0 || clusters.Count <= 1) break;
                if (clusters.Count <= NBins && bestShared == 0) break;   // enough bins: keep unrelated palettes apart
                var m = Merge(clusters[bi], clusters[bj]); clusters.RemoveAt(bj); clusters[bi] = m;
            }
            while (clusters.Count > NBins)   // phase 2: cheapest lossy merges
            {
                int bi = 0, bj = 1; double best = double.MaxValue;
                for (int i = 0; i < clusters.Count; i++)
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        double c = AbsorbCost(clusters[i], clusters[j]);
                        if (c < best) { best = c; bi = i; bj = j; }
                    }
                var m = Merge(clusters[bi], clusters[bj]); clusters.RemoveAt(bj); clusters[bi] = m;
            }
            for (int b = 0; b < NBins; b++)
            {
                var pal = new ushort[16];
                if (b < clusters.Count)
                {
                    List<ushort> reps; double err = 0;
                    var map = Quantize(clusters[b].W, 15, out reps, ref err);
                    ls.QuantErr += err;
                    for (int i = 0; i < reps.Count; i++) pal[i + 1] = reps[i];
                    foreach (var g in clusters[b].Groups)
                    {
                        g.Bin = b;
                        for (int s = 1; s < 16; s++)
                        {
                            ushort col = SlotColor(g.Pal, s);
                            ushort rep;
                            if (!map.TryGetValue(col, out rep))
                            {   // slot unused by any drawn image: nearest palette color
                                double best = double.MaxValue; rep = reps.Count > 0 ? reps[0] : (ushort)0;
                                foreach (var r in reps) { double d = Dist(r, col); if (d < best) { best = d; rep = r; } }
                            }
                            g.Lut[s] = (byte)(reps.IndexOf(rep) + 1);
                        }
                    }
                }
                ls.Bins[b] = pal;
            }

            // ---- VRAM allocation: 16x16 cells from the front, 8x8 singles from the back
            int cells = 0, singles = 0;
            foreach (var c in ls.Combos.ToList())
            {
                c.Chr = Chr(c.Img, c.Image);
                int nc = cells + c.Chr.N16, ns = singles + c.Chr.N8;
                if (nc + (ns + 3) / 4 > MaxCells)
                {
                    ls.Warn.Add("VRAM full, dropped " + c.Img + "/" + c.PalName);
                    ls.Combos.Remove(c);
                    for (int i = 0; i < ls.ById.Length; i++) if (ls.ById[i] == c) ls.ById[i] = null;
                    for (int a = 0; a < ls.NAreas; a++) for (int i = 0; i < themed.Count; i++) if (ls.Themed[a, i] == c) ls.Themed[a, i] = null;
                    continue;
                }
                c.C0 = cells; c.S0 = singles; cells = nc; singles = ns;
            }
            ls.CellsUsed = cells; ls.SinglesUsed = singles;
            return ls;
        }

        static int CellTile(int c) { return 32 + (c >> 3) * 32 + (c & 7) * 2; }
        static int SingleTile(int s) { int t = CellTile(MaxCells - 1 - (s >> 2)); int q = s & 3; return t + (q & 1) + (q >> 1) * 16; }

        // ------------------------------------------------------------------ player frames
        static readonly string[] PalVariants = { "DEFAULT", "LUIGI", "MARIO", "LUIGI_PLAIN", "FIRE", "PSTAR1", "PSTAR2", "PFLASH", "STATUE" };
        sealed class PFrame { public int Hv; public List<int[]> Pieces = new List<int[]>(); public string Key; }
        /// <summary>What to draw for one (form, pose, tail variant): image, the form passed to DrawSuited, tail, spin side.</summary>
        sealed class PSpec { public string Img; public Form DrawForm; public string Tail; public int Side; public string Key; }

        /// <summary>Pose names = PlayerDraw frame names without the ms./mb. prefix; frog.X -> FROG_X; statue.</summary>
        static List<string> PoseNames()
        {
            var l = new List<string>();
            foreach (var n in Art.Order.Where(x => x.StartsWith("ms.") || x.StartsWith("mb.")))
            { string s = n.Substring(3).Replace('.', '_').ToUpperInvariant(); if (!l.Contains(s)) l.Add(s); }
            foreach (var n in Art.Order.Where(x => x.StartsWith("frog."))) l.Add("FROG_" + n.Substring(5).Replace('.', '_').ToUpperInvariant());
            l.Add("STATUE");
            return l;
        }

        static PSpec Spec(Form form, string pose, int tv)
        {
            string lo = pose.ToLowerInvariant();
            string ms = "ms." + lo, mb = "mb." + lo;
            var s = new PSpec();
            if (pose == "STATUE") s.Img = Art.Has("statue") ? "statue" : "mb.stand";
            else if (pose.StartsWith("FROG_")) s.Img = "frog." + lo.Substring(5);
            else if (form == Form.Small)
            {
                // big-only poses fall back to the nearest small frame (PlayerDraw never asks for them when small)
                string alias = lo == "walk1" || lo == "walk2" ? "walk" : lo == "run3" ? "run1" : lo == "fall" ? "jump" :
                               lo == "duck" || lo == "throw" || lo == "spinfront" || lo == "spinback" ? "stand" : lo;
                s.Img = Art.Has("ms." + alias) ? "ms." + alias : mb;
            }
            else if (lo == "walk") s.Img = "mb.walk1";
            else s.Img = Art.Has(mb) ? mb : ms;   // DEATH stays ms.death (SMB3 shows small Mario going down)
            if (!Art.Has(s.Img)) s.Img = form == Form.Small ? "ms.stand" : "mb.stand";
            s.DrawForm = form == Form.Small ? Form.Big : form;
            if (s.Img.StartsWith("mb."))
            {
                bool spin = s.Img == "mb.spinfront" || s.Img == "mb.spinback";
                if (spin) { s.Tail = null; s.Side = tv == 3 ? (s.Img == "mb.spinfront" ? 1 : -1) : 0; }
                else s.Tail = tv == 1 ? "tail.mid" : tv == 2 ? "tail.up" : "tail.down";
            }
            s.Key = s.Img + "|" + s.DrawForm + "|" + s.Tail + "|" + s.Side;
            return s;
        }

        static MethodInfo bodyMethod;
        /// <summary>Renders like PlayerDraw.Draw: mb.* via DrawSuited, other frames via Body() (centered, bottom-aligned).</summary>
        static void RenderSpec(Ppu ppu, PSpec s, ushort[] codes, MethodInfo drawSuited, int sx, int sy)
        {
            ppu.ResetClip(); ppu.Clear(0);
            if (s.Img.StartsWith("mb.")) { drawSuited.Invoke(null, new object[] { ppu, s.DrawForm, s.Img, sx, sy, codes, false, false, false, s.Tail, s.Side }); return; }
            bool small = s.Img.StartsWith("ms.");
            if (bodyMethod == null) bodyMethod = typeof(Player).GetMethod("Body", BindingFlags.NonPublic | BindingFlags.Static);
            var img = Art.Get(s.Img);
            if (bodyMethod != null) bodyMethod.Invoke(null, new object[] { ppu, img, sx, small ? sy + 16 : sy, small ? 16 : 32, codes, false, false, false });
            else ppu.Spr(img, sx, small ? sy + 16 : sy, codes);
        }

        /// <summary>Grid offset (0-15, 0-15) relative to the player box that covers the frame with the fewest 16x16 cells.</summary>
        static void BestGrid(ushort[] px, int sx, int sy, out int ox, out int oy, out int count)
        {
            const int R = 64, N = 2 * R + 32;   // window [s-R, s+R+32)
            var sum = new int[N + 1, N + 1];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int X = sx - R + x, Y = sy - R + y;
                    int v = X >= 0 && Y >= 0 && X < Ppu.W && Y < Ppu.H && px[Y * Ppu.W + X] != 0 ? 1 : 0;
                    sum[y + 1, x + 1] = v + sum[y, x + 1] + sum[y + 1, x] - sum[y, x];
                }
            Func<int, int, int> cell = (x0, y0) =>
            {
                int a0 = Math.Max(0, x0), b0 = Math.Max(0, y0), a1 = Math.Min(N, x0 + 16), b1 = Math.Min(N, y0 + 16);
                if (a0 >= a1 || b0 >= b1) return 0;
                return sum[b1, a1] - sum[b0, a1] - sum[b1, a0] + sum[b0, a0];
            };
            ox = 0; oy = 0; count = int.MaxValue;
            for (int k = 0; k < 256; k++)
            {
                int tx = k & 15, ty = k >> 4, n = 0;   // (0,0) first: keeps the common box-aligned grid on ties (better dedupe)
                for (int y0 = ty - 16; y0 < N; y0 += 16)
                    for (int x0 = tx - 16; x0 < N; x0 += 16)
                        if (cell(x0, y0) > 0) n++;
                if (n < count) { count = n; ox = tx; oy = ty; }
            }
        }

        static ushort[] FormPal(Form f, bool luigi) { return Player.FormPalette(f, luigi); }
        static ushort[] NamedPal(string n) { return Art.Pal(Art.HasPal(n) ? n : "mario"); }

        // ------------------------------------------------------------------ export
        sealed class Blob { public string Label; public int Size; public StringBuilder Asm = new StringBuilder(); public bool Head; }

        public static int Export(string outDir)
        {
            BuildCatalog();
            BuildSynth();
            chrByImg.Clear(); globalTiles.Clear();
            var blobs = new List<Blob>();
            var log = new StringBuilder();

            // ---------------- levels: index = engine order (SnesLevels.LevelOrder, gen/levels.inc LVL_*), extra levels (hb) after it
            var ids = new List<string>(SnesLevels.LevelOrder());
            var extra = new List<string>();
            foreach (var path in Data.List("levels/"))
            {
                if (!path.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase)) continue;
                string lid = Path.GetFileNameWithoutExtension(path);
                if (lid.StartsWith("_") || ids.Contains(lid)) continue;
                extra.Add(lid);
            }
            extra.Sort(StringComparer.OrdinalIgnoreCase);
            ids.AddRange(extra);
            var sets = new List<LevelSet>();
            foreach (var lid in ids)
            {
                var def = LevelLoader.Load(lid);
                if (def == null) { Console.WriteLine("  sprites: cannot load level " + lid); return 1; }
                sets.Add(BuildLevel(lid, def));
            }

            // level set blobs
            int li = 0;
            foreach (var ls in sets)
            {
                var b = new Blob { Label = "spr_set_" + li };
                var a = b.Asm;
                string L = b.Label;
                a.AppendLine(L + ":   ; level " + ls.Id);
                a.AppendLine("    .word " + string.Join(", ", new[] { "_pal", "_lut", "_load", "_ids", "_areas" }.Select(x => ".loword(" + L + x + ")")));
                a.AppendLine("    .byte " + ls.Combos.Count + ", " + ls.NAreas + ", " + ls.StartArea);
                int size = 13;
                a.AppendLine(L + "_pal:");
                for (int bn = 0; bn < NBins; bn++) { a.AppendLine("    .word " + string.Join(",", ls.Bins[bn].Select(x => "$" + x.ToString("X4")))); size += 32; }
                a.AppendLine(L + "_lut:");
                foreach (var g in ls.Groups) { a.AppendLine("    .byte " + string.Join(",", g.Lut.Select(x => x.ToString())) + "   ; " + g.Name + " -> pal " + (9 + g.Bin)); size += 16; }
                a.AppendLine(L + "_load:   ; src tile, n16, n8, cell0, single0, lut");
                foreach (var c in ls.Combos.OrderBy(x => x.G.Index))   // grouped: the loader rebuilds its tables per group
                {
                    a.AppendLine("    .word " + c.Chr.FirstTile + "\n    .byte " + c.Chr.N16 + "," + c.Chr.N8 + "," + c.C0 + "," + c.S0 + "," + c.G.Index + "   ; " + c.Img + "/" + c.PalName);
                    size += 7;
                }
                // records
                int ri = 0;
                foreach (var c in ls.Combos)
                {
                    c.RecLabel = L + "_r" + (ri++);
                    var pcs = new List<string>();
                    int attrPal = (c.G.Bin + 1) << 1;
                    for (int k = 0; k < c.Chr.Cells.Count; k++)
                    {
                        int t = CellTile(c.C0 + k);
                        pcs.Add(c.Chr.Cells[k].Dx + "," + c.Chr.Cells[k].Dy + "," + (t & 255) + ",$" + ((t >> 8) | attrPal | 0x10).ToString("X2"));
                    }
                    for (int k = 0; k < c.Chr.Singles.Count; k++)
                    {
                        int t = SingleTile(c.S0 + k);
                        pcs.Add(c.Chr.Singles[k].Dx + "," + c.Chr.Singles[k].Dy + "," + (t & 255) + ",$" + ((t >> 8) | attrPal).ToString("X2"));
                    }
                    a.AppendLine(c.RecLabel + ":   ; " + c.Img + "/" + c.PalName);
                    a.AppendLine("    .byte " + pcs.Count + "," + c.Image.W + "," + c.Image.H);
                    foreach (var p in pcs) a.AppendLine("    .byte " + p);
                    size += 3 + 4 * pcs.Count;
                }
                a.AppendLine(L + "_ids:");
                for (int i = 0; i < plain.Count; i += 8)
                {
                    var row = new List<string>();
                    for (int j = i; j < Math.Min(plain.Count, i + 8); j++) row.Add(ls.ById[j] == null ? "0" : ".loword(" + ls.ById[j].RecLabel + ")");
                    a.AppendLine("    .word " + string.Join(",", row));
                }
                size += plain.Count * 2;
                a.AppendLine(L + "_areas:");
                for (int ar = 0; ar < ls.NAreas; ar++)
                {
                    var row = new List<string>();
                    for (int j = 0; j < themed.Count; j++) row.Add(ls.Themed[ar, j] == null ? "0" : ".loword(" + ls.Themed[ar, j].RecLabel + ")");
                    a.AppendLine("    .word " + string.Join(",", row));
                    size += themed.Count * 2;
                }
                b.Size = size;
                blobs.Add(b);
                log.AppendLine(string.Format("{0,-5} combos {1,3} groups {2,2} cells {3,3} singles {4,3} quantErr {5,10:F0} {6}", ls.Id, ls.Combos.Count, ls.Groups.Count, ls.CellsUsed, ls.SinglesUsed, ls.QuantErr, string.Join("; ", ls.Warn)));
                foreach (var w in ls.Warn) Console.WriteLine("  sprites: level " + ls.Id + ": " + w);
                li++;
            }

            // ---------------- global CHR (nibble chunky: hi nibble = left pixel), chunks of 1024 tiles
            var chrChunks = new List<Blob>();
            for (int k = 0; k * 1024 < globalTiles.Count; k++)
            {
                int n = Math.Min(1024, globalTiles.Count - k * 1024);
                var bytes = new byte[n * 32];
                for (int t = 0; t < n; t++)
                {
                    var tile = globalTiles[k * 1024 + t];
                    for (int i = 0; i < 32; i++) bytes[t * 32 + i] = (byte)((tile[i * 2] << 4) | tile[i * 2 + 1]);
                }
                string fn = "spr_chr" + k + ".bin";
                File.WriteAllBytes(Path.Combine(outDir, fn), bytes);
                var b = new Blob { Label = "spr_chr" + k, Size = bytes.Length };
                b.Asm.AppendLine(b.Label + ":\n    .incbin \"" + fn + "\"");
                blobs.Add(b); chrChunks.Add(b);
            }

            // ---------------- player frames
            var drawSuited = typeof(Player).GetMethod("DrawSuited", BindingFlags.NonPublic | BindingFlags.Static);
            var palsField = typeof(Art).GetField("pals", BindingFlags.NonPublic | BindingFlags.Static);
            if (drawSuited == null || palsField == null) { Console.WriteLine("  sprites: PlayerDraw.DrawSuited / Art.pals not found"); return 1; }
            var pals = (Dictionary<string, ushort[]>)palsField.GetValue(null);
            var poseNames = PoseNames();
            int NP = poseNames.Count * 4;   // [pose][tail variant 0-3]
            var forms = (Form[])Enum.GetValues(typeof(Form));
            var codes = new ushort[16]; for (int i = 0; i < 16; i++) codes[i] = (ushort)i;
            var tailCodes = new ushort[16]; for (int i = 1; i < 16; i++) tailCodes[i] = (ushort)(100 + i);
            ushort[] realTail = Art.HasPal("tail") ? Art.Pal("tail") : null;
            var pool = new List<byte[]>(); var poolIdx = new Dictionary<string, int>();
            var frames = new List<PFrame>(); var frameIdx = new Dictionary<string, int>();
            var formFrame = new int[forms.Length, NP];
            var formPal = new ushort[forms.Length, PalVariants.Length, 16];
            var ppu = new Ppu();
            const int SX = 96, SY = 96;
            int maxPieces = 0;
            try
            {
                if (realTail != null) pals["tail"] = tailCodes;
                foreach (var form in forms)
                {
                    // pass 1: render every distinct pose once, collect body slots and overlay (tail palette) colors
                    var specOf = new PSpec[NP];
                    var renders = new Dictionary<string, ushort[]>();
                    var usedBody = new HashSet<int>(); var tailFreq = new Dictionary<int, int>();
                    for (int k = 0; k < NP; k++)
                    {
                        var s = specOf[k] = Spec(form, poseNames[k / 4], k & 3);
                        if (renders.ContainsKey(s.Key)) continue;
                        RenderSpec(ppu, s, codes, drawSuited, SX, SY);
                        renders[s.Key] = (ushort[])ppu.Px.Clone();
                        foreach (var v in ppu.Px) { if (v == 0) continue; if (v < 16) usedBody.Add(v); else { int n; tailFreq.TryGetValue(v, out n); tailFreq[v] = n + 1; } }
                    }
                    // overlay colors -> body slots: exact color match, else a free slot, else nearest
                    var basePal = FormPal(form, false);
                    var remap = new Dictionary<int, int>(); var extras = new Dictionary<int, ushort>();
                    var free = Enumerable.Range(1, 15).Where(s => !usedBody.Contains(s)).ToList();
                    foreach (var kv in tailFreq.OrderByDescending(x => x.Value))
                    {
                        ushort col = SlotColor(realTail, kv.Key - 100);
                        int slot = -1;
                        for (int s = 1; s < 16 && slot < 0; s++) if (usedBody.Contains(s) && SlotColor(basePal, s) == col) slot = s;
                        if (slot < 0) foreach (var e in extras) if (e.Value == col) slot = e.Key;
                        if (slot < 0 && free.Count > 0) { slot = free[0]; free.RemoveAt(0); extras[slot] = col; }
                        if (slot < 0)
                        {
                            double best = double.MaxValue;
                            for (int s = 1; s < 16; s++)
                            {
                                ushort sc = extras.ContainsKey(s) ? extras[s] : SlotColor(basePal, s);
                                double d = Dist(sc, col); if (d < best) { best = d; slot = s; }
                            }
                        }
                        remap[kv.Key] = slot;
                    }
                    if (tailFreq.Count > 0) log.AppendLine("player " + form + ": body slots " + string.Join(",", usedBody.OrderBy(x => x)) + "; overlay colors " + tailFreq.Count + " -> extra slots " + string.Join(",", extras.Keys));
                    // palettes
                    for (int v = 0; v < PalVariants.Length; v++)
                    {
                        ushort[] src;
                        switch (v)
                        {
                            case 0: src = FormPal(form, false); break;
                            case 1: src = FormPal(form, true); break;
                            case 2: src = Art.Pal("mario"); break;
                            case 3: src = Art.Pal("luigi"); break;
                            case 4: src = Art.Pal("fire"); break;
                            case 5: src = NamedPal("pstar.1"); break;
                            case 6: src = NamedPal("pstar.2"); break;
                            case 7: src = NamedPal("pflash"); break;
                            default: src = Art.HasPal("statue") ? Art.Pal("statue") : FormPal(form, false); break;
                        }
                        for (int s = 1; s < 16; s++) formPal[(int)form, v, s] = extras.ContainsKey(s) ? extras[s] : SlotColor(src, s);
                    }
                    // pass 2: slice into 16x16 pieces on a grid anchored at the player box
                    var frameOfSpec = new Dictionary<string, int>();
                    for (int k = 0; k < NP; k++)
                    {
                        var sp = specOf[k];
                        int fi;
                        if (frameOfSpec.TryGetValue(sp.Key, out fi)) { formFrame[(int)form, k] = fi; continue; }
                        var px = renders[sp.Key];
                        var fr = new PFrame { Hv = sp.Img.StartsWith("ms.") ? 48 : 32 };
                        var key = new StringBuilder(fr.Hv + ":");
                        int gox, goy, gn;
                        BestGrid(px, SX, SY, out gox, out goy, out gn);
                        for (int cy = goy - 80; cy < 96; cy += 16)
                            for (int cx = gox - 80; cx < 96; cx += 16)
                            {
                                var idx = new byte[256]; bool any = false;
                                for (int y = 0; y < 16; y++)
                                    for (int x = 0; x < 16; x++)
                                    {
                                        int X = SX + cx + x, Y = SY + cy + y;
                                        int v = X < 0 || Y < 0 || X >= Ppu.W || Y >= Ppu.H ? 0 : px[Y * Ppu.W + X];
                                        int o = v == 0 ? 0 : v < 16 ? v : (remap.ContainsKey(v) ? remap[v] : 1);
                                        idx[y * 16 + x] = (byte)o; if (o != 0) any = true;
                                    }
                                if (!any) continue;
                                var chr = Planar16(idx);
                                string h = Convert.ToBase64String(chr);
                                int pix;
                                if (!poolIdx.TryGetValue(h, out pix)) { pix = pool.Count; pool.Add(chr); poolIdx[h] = pix; }
                                fr.Pieces.Add(new[] { cx, cy, pix });
                                key.Append(cx + "," + cy + "," + pix + ";");
                            }
                        if (fr.Pieces.Count > 8)
                        {
                            Console.WriteLine("  sprites: player " + form + " " + sp.Key + " needs " + fr.Pieces.Count + " pieces (max 8), truncated");
                            fr.Pieces.RemoveRange(8, fr.Pieces.Count - 8);
                        }
                        maxPieces = Math.Max(maxPieces, fr.Pieces.Count);
                        fr.Key = key.ToString();
                        if (!frameIdx.TryGetValue(fr.Key, out fi)) { fi = frames.Count; frames.Add(fr); frameIdx[fr.Key] = fi; }
                        frameOfSpec[sp.Key] = fi;
                        formFrame[(int)form, k] = fi;
                    }
                }
            }
            finally { if (realTail != null) pals["tail"] = realTail; }

            // player pool chunks (256 pieces x 128 bytes per bank chunk)
            var poolChunks = new List<Blob>();
            for (int k = 0; k * 256 < pool.Count; k++)
            {
                int n = Math.Min(256, pool.Count - k * 256);
                var bytes = new byte[n * 128];
                for (int i = 0; i < n; i++) Array.Copy(pool[k * 256 + i], 0, bytes, i * 128, 128);
                string fn = "spr_ppool" + k + ".bin";
                File.WriteAllBytes(Path.Combine(outDir, fn), bytes);
                var b = new Blob { Label = "spr_ppool" + k, Size = bytes.Length };
                b.Asm.AppendLine(b.Label + ":\n    .incbin \"" + fn + "\"");
                blobs.Add(b); poolChunks.Add(b);
            }

            // player tables + top-level tables (one blob, exported)
            var pb = new Blob { Label = "spr_player_data", Head = true };
            {
                var a = pb.Asm; int size = 0;
                a.AppendLine(".export spr_level_tab, spr_chr_tab, spr_ppool_tab, spr_pform_tab, spr_ppal_tab");
                a.AppendLine("spr_player_data:");
                a.AppendLine("spr_level_tab:   ; long pointer per level index");
                for (int i = 0; i < sets.Count; i++) { a.AppendLine("    .faraddr spr_set_" + i + "   ; " + sets[i].Id); size += 3; }
                a.AppendLine("spr_chr_tab:");
                foreach (var c in chrChunks) { a.AppendLine("    .faraddr " + c.Label); size += 3; }
                a.AppendLine("spr_ppool_tab:");
                foreach (var c in poolChunks) { a.AppendLine("    .faraddr " + c.Label); size += 3; }
                a.AppendLine("spr_ppal_tab:   ; [form][variant] 16 colors");
                for (int f = 0; f < forms.Length; f++)
                    for (int v = 0; v < PalVariants.Length; v++)
                    {
                        var row = new List<string>();
                        for (int s = 0; s < 16; s++) row.Add("$" + formPal[f, v, s].ToString("X4"));
                        a.AppendLine("    .word " + string.Join(",", row) + "   ; " + forms[f] + " " + PalVariants[v]);
                        size += 32;
                    }
                a.AppendLine("spr_pform_tab:   ; [form][pose] -> frame record (same bank)");
                for (int f = 0; f < forms.Length; f++)
                {
                    for (int i = 0; i < NP; i += 12)
                    {
                        var row = new List<string>();
                        for (int j = i; j < Math.Min(NP, i + 12); j++) row.Add(".loword(spr_pf" + formFrame[f, j] + ")");
                        a.AppendLine("    .word " + string.Join(",", row));
                    }
                    size += NP * 2;
                }
                for (int i = 0; i < frames.Count; i++)
                {
                    var fr = frames[i];
                    a.AppendLine("spr_pf" + i + ":");
                    a.AppendLine("    .byte " + fr.Pieces.Count + "," + fr.Hv);
                    foreach (var p in fr.Pieces) a.AppendLine("    .byte " + (p[0] & 255) + "," + (p[1] & 255) + "\n    .word " + p[2]);
                    size += 2 + 4 * fr.Pieces.Count;
                }
                pb.Size = size;
            }
            blobs.Add(pb);

            // ---------------- pack blobs into banks (63 downwards), first fit decreasing
            var banks = new List<List<Blob>>(); var free2 = new List<int>();
            for (int bn = LastBank; bn >= FirstBank; bn--) { banks.Add(new List<Blob>()); free2.Add(BankSize); }
            foreach (var b in blobs.OrderByDescending(x => x.Head ? int.MaxValue : x.Size))
            {
                int k = -1;
                for (int i = 0; i < banks.Count; i++) if (free2[i] >= b.Size) { k = i; break; }
                if (k < 0) { Console.WriteLine("  sprites: out of ROM banks (" + b.Label + ", " + b.Size + " bytes); blobs: " + string.Join(" ", blobs.Select(x => x.Label + "=" + x.Size)) + "; player " + frames.Count + " frames " + pool.Count + " pieces"); return 1; }
                banks[k].Add(b); free2[k] -= b.Size;
            }
            // clean old generated bank files
            foreach (var f in Directory.GetFiles(outDir, "spr_bank*.s")) File.Delete(f);
            int used = 0;
            var sb = new StringBuilder();
            sb.AppendLine("; generated by smb4tools snes-export (SnesSprites.cs) - do not edit. Sprite data banks.");
            for (int i = 0; i < banks.Count; i++)
            {
                if (banks[i].Count == 0) continue;
                int bn = LastBank - i; used++;
                sb.AppendLine(".segment \"BANK" + bn + "\"");
                foreach (var b in banks[i]) sb.Append(b.Asm);
                log.AppendLine("BANK" + bn + ": " + (BankSize - free2[i]) + " bytes: " + string.Join(" ", banks[i].Select(x => x.Label)));
            }
            File.WriteAllText(Path.Combine(outDir, "spr_data.s"), sb.ToString());

            // ---------------- ids include
            var inc = new StringBuilder();
            inc.AppendLine("; generated by smb4tools snes-export (SnesSprites.cs) - sprite ids, see snes/SPRITES.md");
            inc.AppendLine("SPR_NPLAIN = " + plain.Count + "   ; plain ids 0..SPR_NPLAIN-1");
            inc.AppendLine("SPR_NTHEMED = " + themed.Count + "   ; theme-colored ids SPR_NPLAIN.. (resolved by spr_area)");
            inc.AppendLine("SPR_NIDS = " + (plain.Count + themed.Count));
            foreach (var e in plain) inc.AppendLine(e.Id + " = " + e.Index + "   ; " + e.Img + " / " + e.Pal);
            foreach (var e in themed) inc.AppendLine(e.Id + " = " + (plain.Count + e.Index) + "   ; " + e.Img + " / THEME." + e.Pal);
            inc.AppendLine("; player forms (C# Form enum order)");
            foreach (var f in forms) inc.AppendLine("FORM_" + f.ToString().ToUpperInvariant() + " = " + (int)f);
            inc.AppendLine("; player poses for spr_player X: PlayerDraw.Frame names without ms./mb. (small form -> ms.*, big forms -> mb.*),");
            inc.AppendLine("; frog.* -> POSE_FROG_*, statue -> POSE_STATUE");
            for (int i = 0; i < poseNames.Count; i++) inc.AppendLine("POSE_" + poseNames[i] + " = " + i);
            inc.AppendLine("SPR_NPOSES = " + poseNames.Count);
            inc.AppendLine("; spr_arg_flags (spr_meta and spr_player)");
            inc.AppendLine("SPR_HFLIP = $0001   ; face left / mirror");
            inc.AppendLine("SPR_VFLIP = $0002");
            inc.AppendLine("SPR_BEHIND = $0004  ; OBJ priority 2: hidden by high-priority BG1 tiles (pipes, ground, blocks)");
            inc.AppendLine("SPR_PAL_SHIFT = 4   ; spr_meta: bits 4-7 = OBJ palette 8-15 override ($80-$F0), 0 = default");
            inc.AppendLine("; spr_player only:");
            inc.AppendLine("SPR_TAIL_DOWN = $0000   ; bits 8-9 tail pose (Raccoon/Tanooki big poses)");
            inc.AppendLine("SPR_TAIL_MID = $0100");
            inc.AppendLine("SPR_TAIL_UP = $0200");
            inc.AppendLine("SPR_TAIL_SPIN = $0300   ; POSE_SPINFRONT/SPINBACK: tail sticks out sideways (else: no side tail)");
            inc.AppendLine("SPR_STARPAL = $0400     ; star flash: bits 11-12 = BodyPalette k (0 mario/luigi, 1 fire, 2 pstar.1, 3 pstar.2)");
            inc.AppendLine("SPR_STARPAL_SHIFT = 11");
            inc.AppendLine("SPR_PFLASH = $2000      ; power-up transform flash palette (pflash)");
            inc.AppendLine("SPR_LUIGI = $4000       ; Luigi palettes");
            inc.AppendLine("; player palette variants (spr_ppal_tab columns)");
            for (int v = 0; v < PalVariants.Length; v++) inc.AppendLine("PPAL_" + PalVariants[v] + " = " + v);
            inc.AppendLine("SPR_NPPAL = " + PalVariants.Length);
            inc.AppendLine("; level index for spr_level_load = LVL_* of gen/levels.inc (engine order); extra levels (hb) follow");
            inc.AppendLine("SPR_NLEVELS = " + sets.Count);
            for (int i = 0; i < sets.Count; i++) inc.AppendLine("SPR_LEVEL_" + sets[i].Id.Replace('-', '_').ToUpperInvariant() + " = " + i);
            inc.AppendLine("SPR_NCHRCHUNKS = " + chrChunks.Count);
            File.WriteAllText(Path.Combine(outDir, "spr_ids.inc"), inc.ToString());
            File.WriteAllText(Path.Combine(outDir, "spr_report.txt"), log.ToString());

            // test aid: SMB4_SPR_REF=DIR writes ref_<level>.png (quantized, as the ROM should show it) and
            // exact_<level>.png (C# colors) using the sprite test ROM's flow layout
            string refDir = Environment.GetEnvironmentVariable("SMB4_SPR_REF");
            if (!string.IsNullOrEmpty(refDir))
            {
                Directory.CreateDirectory(refDir);
                foreach (var ls in sets) { RefSheet(ls, refDir, true); RefSheet(ls, refDir, false); }
            }

            Console.WriteLine(string.Format("  sprites: {0} ids + {1} themed, {2} levels, {3} CHR tiles, player {4} frames / {5} pieces (max {6}/frame), {7} banks",
                plain.Count, themed.Count, sets.Count, globalTiles.Count, frames.Count, pool.Count, maxPieces, used));
            return 0;
        }

        static int Rgb555(ushort c)
        {
            int r = c & 31, g = (c >> 5) & 31, b = (c >> 10) & 31;
            r = (r << 3) | (r >> 2); g = (g << 3) | (g >> 2); b = (b << 3) | (b >> 2);
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        /// <summary>Same flow layout as the sprite test ROM (x from 4, wrap before 253, stop at 190; ids in order).</summary>
        static void RefSheet(LevelSet ls, string dir, bool quantized)
        {
            const int W = 256, H = 224;
            var px = new int[W * H];
            int bg = Rgb555(0x550C);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var list = new List<Combo>();
            for (int i = 0; i < plain.Count; i++) list.Add(ls.ById[i]);
            int area = Math.Min(ls.StartArea, ls.NAreas - 1);
            for (int i = 0; i < themed.Count; i++) list.Add(ls.Themed[area, i]);
            int x = 4, y = 4, rowh = 0;
            foreach (var c in list)
            {
                if (c == null) continue;
                int w = c.Image.W, h = c.Image.H;
                if (x + w >= 253) { x = 4; y += rowh + 4; rowh = 0; }
                if (y + h >= 190) break;
                for (int yy = 0; yy < h; yy++)
                    for (int xx = 0; xx < w; xx++)
                    {
                        int v = c.Image.P[yy * w + xx];
                        if (v == 0) continue;
                        ushort col = quantized ? ls.Bins[c.G.Bin][c.G.Lut[v]] : SlotColor(c.Pal, v);
                        px[(y + yy) * W + x + xx] = Rgb555(col);
                    }
                x += w + 4;
                if (h > rowh) rowh = h;
            }
            SMB4.Platform.Host.WritePng(px, W, H, 1, Path.Combine(dir, (quantized ? "ref_" : "exact_") + ls.Id + ".png"));
        }

        /// <summary>16x16 indexed pixels -> 4 SNES 4bpp tiles in order TL, TR, BL, BR (128 bytes).</summary>
        static byte[] Planar16(byte[] idx)
        {
            var o = new byte[128];
            for (int q = 0; q < 4; q++)
            {
                int x0 = (q & 1) * 8, y0 = (q >> 1) * 8;
                for (int y = 0; y < 8; y++)
                {
                    int p0 = 0, p1 = 0, p2 = 0, p3 = 0;
                    for (int x = 0; x < 8; x++)
                    {
                        int v = idx[(y0 + y) * 16 + x0 + x], bit = 7 - x;
                        p0 |= (v & 1) << bit; p1 |= ((v >> 1) & 1) << bit; p2 |= ((v >> 2) & 1) << bit; p3 |= ((v >> 3) & 1) << bit;
                    }
                    o[q * 32 + y * 2] = (byte)p0; o[q * 32 + y * 2 + 1] = (byte)p1;
                    o[q * 32 + 16 + y * 2] = (byte)p2; o[q * 32 + 16 + y * 2 + 1] = (byte)p3;
                }
            }
            return o;
        }
    }
}
