using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SMB4.Engine;

namespace SMB4.Tools
{
    /// <summary>
    /// Screens converter core (screens agent): RGB layer images -> SNES 4bpp tile pool + palettes + tilemaps,
    /// sprite images -> OBJ tiles + palettes + metasprites, a first-fit ROM bank packer and the ca65 writer.
    /// Generic: any 256-wide picture drawn with the C# Ppu/Hud code can become a SNES screen with it.
    /// </summary>
    static class ScrGfx
    {
        public const int Transparent = -1;

        // ------------------------------------------------------------------ colors
        /// <summary>24-bit RGB -> BGR555 exactly like SnesExport.Bgr555 (truncating).</summary>
        public static int Bgr(int rgb)
        {
            int r = (rgb >> 19) & 31, g = (rgb >> 11) & 31, b = (rgb >> 3) & 31;
            return r | (g << 5) | (b << 10);
        }
        public static int Rgb(int bgr)
        {
            int r = bgr & 31, g = (bgr >> 5) & 31, b = (bgr >> 10) & 31;
            return (r << 19 | r << 14 & 0x070000) | (g << 11 | g << 6 & 0x0700) | (b << 3 | b >> 2);
        }
        public static double Dist(int a, int b)
        {
            int dr = (a & 31) - (b & 31), dg = ((a >> 5) & 31) - ((b >> 5) & 31), db = ((a >> 10) & 31) - ((b >> 10) & 31);
            return dr * dr * 3 + dg * dg * 4 + db * db * 2;
        }

        // ------------------------------------------------------------------ layer images
        /// <summary>A BGR555 picture with transparency (-1), usually 256 x 224 (or 256 x 512 for a tall BG2).</summary>
        public sealed class Layer
        {
            public readonly int W, H;
            public readonly int[] Px;
            public Layer(int w, int h) { W = w; H = h; Px = new int[w * h]; for (int i = 0; i < Px.Length; i++) Px[i] = Transparent; }
            public int At(int x, int y) { return x < 0 || y < 0 || x >= W || y >= H ? Transparent : Px[y * W + x]; }
        }

        static int keyIdx = -1;
        /// <summary>Color index used as "transparent" when drawing a layer with the C# Ppu.</summary>
        public static int Key { get { if (keyIdx < 0) keyIdx = NesPalette.Color(0xFE01FD); return keyIdx; } }

        public static Ppu NewPpu() { var p = new Ppu(); p.Clear(Key); return p; }

        /// <summary>Ppu picture rows [y0, y0+h) -> layer (key color = transparent).</summary>
        public static Layer FromPpu(Ppu ppu, int y0, int h)
        {
            var l = new Layer(256, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < 256; x++)
                {
                    int sy = y + y0;
                    if (sy < 0 || sy >= Ppu.H) continue;
                    int c = ppu.Px[sy * Ppu.W + x];
                    if (c == Key) continue;
                    l.Px[y * 256 + x] = Bgr(NesPalette.RgbOf(c));
                }
            return l;
        }

        // ------------------------------------------------------------------ BG tile pool
        /// <summary>A named tilemap to generate: a rectangle (in tiles) of a layer.</summary>
        public sealed class MapReq
        {
            public string Name; public Layer L; public int Tx, Ty, Tw, Th; public bool Prio;
            public ushort[] Words;
        }

        /// <summary>
        /// Joint conversion of several layer rectangles into one 4bpp tile pool with shared palettes (palettes
        /// FirstPal..FirstPal+NPal-1). Colors are merged (closest pair, weighted by pixel count) until every tile's
        /// color set packs into the palettes.
        /// </summary>
        public sealed class BgPool
        {
            public int FirstPal = 1, NPal = 7, MaxTiles = 832;
            public readonly List<MapReq> Maps = new List<MapReq>();
            public readonly List<byte[]> Tiles = new List<byte[]>();     // 64 indices each
            public int[][] Pals;                                          // [NPal][16] BGR555
            public string Report = "";
            public double Error;

            public MapReq Add(string name, Layer l, int tx, int ty, int tw, int th, bool prio)
            {
                var m = new MapReq { Name = name, L = l, Tx = tx, Ty = ty, Tw = tw, Th = th, Prio = prio };
                Maps.Add(m);
                return m;
            }
            public MapReq AddFull(string name, Layer l, bool prio) { return Add(name, l, 0, 0, l.W / 8, l.H / 8, prio); }

            public void Build()
            {
                // gather tiles
                var tiles = new List<int[]>();
                foreach (var m in Maps)
                    for (int ty = 0; ty < m.Th; ty++)
                        for (int tx = 0; tx < m.Tw; tx++)
                        {
                            var t = new int[64];
                            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) t[y * 8 + x] = m.L.At((m.Tx + tx) * 8 + x, (m.Ty + ty) * 8 + y);
                            tiles.Add(t);
                        }
                var remap = Quantize(tiles, NPal, out Pals, out Error);
                // encode
                var index = new Dictionary<string, int>();
                int ti = 0;
                foreach (var m in Maps)
                {
                    m.Words = new ushort[m.Tw * m.Th];
                    for (int k = 0; k < m.Words.Length; k++)
                    {
                        var t = tiles[ti++];
                        bool empty = true;
                        for (int i = 0; i < 64; i++) if (t[i] != Transparent) { empty = false; break; }
                        int pal = 0; byte[] px = new byte[64];
                        if (!empty)
                        {
                            pal = PalFor(t, remap);
                            for (int i = 0; i < 64; i++) px[i] = t[i] == Transparent ? (byte)0 : (byte)Array.IndexOf(Pals[pal], remap[t[i]], 1);
                        }
                        int flags;
                        int tile = Intern(px, index, out flags);
                        m.Words[k] = (ushort)(tile | ((FirstPal + pal) << 10) | (m.Prio ? 0x2000 : 0) | flags);
                    }
                }
                Report = string.Format("{0} tiles, {1} maps, color error {2:F1}", Tiles.Count, Maps.Count, Error);
                if (Tiles.Count > MaxTiles) throw new Exception("BG tile pool overflow: " + Tiles.Count + " > " + MaxTiles);
            }

            int PalFor(int[] t, Dictionary<int, int> remap)
            {
                for (int p = 0; p < Pals.Length; p++)
                {
                    bool ok = true;
                    for (int i = 0; i < 64 && ok; i++) if (t[i] != Transparent && Array.IndexOf(Pals[p], remap[t[i]], 1) < 0) ok = false;
                    if (ok) return p;
                }
                throw new Exception("tile without palette");
            }

            int Intern(byte[] px, Dictionary<string, int> index, out int flags)
            {
                for (int f = 0; f < 4; f++)
                {
                    var v = new byte[64];
                    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        v[y * 8 + x] = px[((f & 2) != 0 ? 7 - y : y) * 8 + ((f & 1) != 0 ? 7 - x : x)];
                    int id;
                    if (index.TryGetValue(Key64(v), out id)) { flags = ((f & 1) != 0 ? 0x4000 : 0) | ((f & 2) != 0 ? 0x8000 : 0); return id; }
                }
                index[Key64(px)] = Tiles.Count;
                Tiles.Add(px);
                flags = 0;
                return Tiles.Count - 1;
            }

            public byte[] ChrBytes() { return Chr4(Tiles); }
            public byte[] PalBytes()
            {
                var b = new byte[Pals.Length * 32];
                for (int p = 0; p < Pals.Length; p++) for (int i = 0; i < 16; i++) { b[p * 32 + i * 2] = (byte)Pals[p][i]; b[p * 32 + i * 2 + 1] = (byte)(Pals[p][i] >> 8); }
                return b;
            }
            public MapReq Map(string name) { foreach (var m in Maps) if (m.Name == name) return m; return null; }
        }

        public static string Key64(byte[] v) { return Convert.ToBase64String(v); }

        /// <summary>4bpp SNES tile format.</summary>
        public static byte[] Chr4(List<byte[]> tiles)
        {
            var o = new byte[tiles.Count * 32];
            for (int t = 0; t < tiles.Count; t++)
                for (int y = 0; y < 8; y++)
                {
                    int b0 = 0, b1 = 0, b2 = 0, b3 = 0;
                    for (int x = 0; x < 8; x++)
                    {
                        int v = tiles[t][y * 8 + x], m = 0x80 >> x;
                        if ((v & 1) != 0) b0 |= m; if ((v & 2) != 0) b1 |= m; if ((v & 4) != 0) b2 |= m; if ((v & 8) != 0) b3 |= m;
                    }
                    o[t * 32 + y * 2] = (byte)b0; o[t * 32 + y * 2 + 1] = (byte)b1;
                    o[t * 32 + 16 + y * 2] = (byte)b2; o[t * 32 + 16 + y * 2 + 1] = (byte)b3;
                }
            return o;
        }

        // ------------------------------------------------------------------ quantizer / palette packer
        /// <summary>
        /// Finds a color remap (merging close colors) so that every tile's color set fits one of nPal palettes of
        /// 15 colors; returns the remap and the palettes (slot 0 = 0).
        /// </summary>
        public static Dictionary<int, int> Quantize(List<int[]> tiles, int nPal, out int[][] pals, out double err)
        {
            var count = new Dictionary<int, long>();
            foreach (var t in tiles) foreach (int c in t) if (c != Transparent) { long n; count.TryGetValue(c, out n); count[c] = n + 1; }
            var remap = new Dictionary<int, int>();
            foreach (var c in count.Keys) remap[c] = c;
            var live = new Dictionary<int, long>(count);   // representative -> weight
            err = 0;
            // tile color sets as representative lists
            while (true)
            {
                var sets = new Dictionary<string, List<int>>();
                foreach (var t in tiles)
                {
                    var s = new SortedSet<int>();
                    foreach (int c in t) if (c != Transparent) s.Add(remap[c]);
                    if (s.Count == 0) continue;
                    var l = new List<int>(s);
                    string k = string.Join(",", l);
                    if (!sets.ContainsKey(k)) sets[k] = l;
                }
                List<List<int>> packed;
                if (TryPack(new List<List<int>>(sets.Values), nPal, out packed))
                {
                    // unused slots stay 0 (black): a lookup of black that lands on one still yields black
                    pals = new int[nPal][];
                    for (int p = 0; p < nPal; p++)
                    {
                        pals[p] = new int[16];
                        if (p < packed.Count) for (int i = 0; i < packed[p].Count; i++) pals[p][i + 1] = packed[p][i];
                    }
                    return remap;
                }
                // merge the cheapest pair of live colors
                var keys = new List<int>(live.Keys);
                double best = double.MaxValue; int ba = -1, bb = -1;
                for (int i = 0; i < keys.Count; i++)
                    for (int j = i + 1; j < keys.Count; j++)
                    {
                        double d = Dist(keys[i], keys[j]) * Math.Min(live[keys[i]], live[keys[j]]);
                        if (d < best) { best = d; ba = keys[i]; bb = keys[j]; }
                    }
                if (ba < 0) throw new Exception("quantizer stuck");
                int keep = live[ba] >= live[bb] ? ba : bb, drop = keep == ba ? bb : ba;
                err += Dist(keep, drop) * live[drop];
                live[keep] += live[drop]; live.Remove(drop);
                var ks = new List<int>(remap.Keys);
                foreach (var k in ks) if (remap[k] == drop) remap[k] = keep;
            }
        }

        static bool TryPack(List<List<int>> sets, int nPal, out List<List<int>> pals)
        {
            pals = new List<List<int>>();
            sets.Sort((a, b) => b.Count.CompareTo(a.Count));
            var palSets = new List<HashSet<int>>();
            foreach (var s in sets)
            {
                if (s.Count > 15) return false;
                int best = -1, bestAdd = 99, bestSize = 0;
                for (int p = 0; p < palSets.Count; p++)
                {
                    int add = 0;
                    foreach (int c in s) if (!palSets[p].Contains(c)) add++;
                    if (palSets[p].Count + add > 15) continue;
                    if (add < bestAdd || add == bestAdd && palSets[p].Count > bestSize) { best = p; bestAdd = add; bestSize = palSets[p].Count; }
                }
                bool join = best >= 0 && (bestAdd < s.Count || palSets.Count >= nPal);
                if (!join)
                {
                    if (palSets.Count >= nPal) return false;
                    palSets.Add(new HashSet<int>(s));
                    continue;
                }
                foreach (int c in s) palSets[best].Add(c);
            }
            foreach (var p in palSets) { var l = new List<int>(p); l.Sort(); pals.Add(l); }
            return true;
        }

        // ------------------------------------------------------------------ sprites (OBJ)
        /// <summary>One sprite image (top-left anchored) and its palette variants (same shape, other colors).</summary>
        public sealed class SprImg
        {
            public string Id;
            public Layer[] Variants;     // variant 0 = default
            public int Slot = -1;        // fixed OBJ palette slot (0-7) or -1 = packed
            public int FlipW;            // mirror width for hflip (pieces are relative to the anchor)
            public bool Dynamic;         // palette per variant loaded at runtime into Slot
            public int X0, Y0, W, H;     // bounding box inside the layer
            public List<int[]> Pieces = new List<int[]>();   // dx, dy, tile, big(0/1)
            public int PalSlot;          // resolved palette slot
            public List<int[]> VarPals = new List<int[]>(); // per variant: 16 colors (dynamic / fixed-variant sprites)
            public byte[] Idx;           // W*H indices into the palette
        }

        /// <summary>
        /// A screen's OBJ set: images -> 16x16/8x8 pieces in OBJ tiles (VRAM $6000, 512 tiles), 8 palettes (CGRAM 128+).
        /// Images without a fixed slot are packed into shared palettes; images with variants get one slot (the
        /// variants' colors are aligned index by index, so switching palettes recolors them).
        /// </summary>
        public sealed class ObjSet
        {
            public readonly List<SprImg> Imgs = new List<SprImg>();
            public int[][] Pals = new int[8][];
            public readonly byte[][] Cells = new byte[512][];   // 8x8 tiles by OBJ tile number
            public int Used16, Used8;
            public string Report = "";

            public SprImg Add(string id, params Layer[] variants) { var s = new SprImg { Id = id, Variants = variants }; Imgs.Add(s); return s; }

            public void Build()
            {
                for (int p = 0; p < 8; p++) Pals[p] = null;
                // bounding boxes
                foreach (var s in Imgs)
                {
                    var l = s.Variants[0];
                    int x0 = l.W, y0 = l.H, x1 = -1, y1 = -1;
                    for (int y = 0; y < l.H; y++) for (int x = 0; x < l.W; x++) if (l.Px[y * l.W + x] != Transparent) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                    if (x1 < 0) { x0 = y0 = 0; x1 = y1 = 0; }
                    s.X0 = x0; s.Y0 = y0; s.W = x1 - x0 + 1; s.H = y1 - y0 + 1;
                }
                // 1) images with variants or dynamic palettes: index by color tuple
                foreach (var s in Imgs)
                {
                    if (s.Variants.Length == 1 && !s.Dynamic) continue;
                    var tuples = new Dictionary<string, int>();
                    var tupleList = new List<int[]>();
                    s.Idx = new byte[s.W * s.H];
                    for (int y = 0; y < s.H; y++)
                        for (int x = 0; x < s.W; x++)
                        {
                            if (s.Variants[0].At(s.X0 + x, s.Y0 + y) == Transparent) continue;
                            var tup = new int[s.Variants.Length];
                            for (int v = 0; v < tup.Length; v++) tup[v] = s.Variants[v].At(s.X0 + x, s.Y0 + y);
                            string k = string.Join(",", tup);
                            int id;
                            if (!tuples.TryGetValue(k, out id)) { id = tupleList.Count + 1; tuples[k] = id; tupleList.Add(tup); }
                            s.Idx[y * s.W + x] = (byte)Math.Min(15, id);
                        }
                    // more than 15 color tuples: merge the closest ones (weighted by pixel count)
                    var cnt = new List<int>();
                    for (int i = 0; i < tupleList.Count; i++) cnt.Add(0);
                    var ids = new int[s.W * s.H];
                    for (int y = 0; y < s.H; y++)
                        for (int x = 0; x < s.W; x++)
                        {
                            if (s.Variants[0].At(s.X0 + x, s.Y0 + y) == Transparent) continue;
                            var tup = new int[s.Variants.Length];
                            for (int v = 0; v < tup.Length; v++) tup[v] = s.Variants[v].At(s.X0 + x, s.Y0 + y);
                            ids[y * s.W + x] = tuples[string.Join(",", tup)];
                            cnt[ids[y * s.W + x] - 1]++;
                        }
                    while (tupleList.Count > 15)
                    {
                        double best = double.MaxValue; int bi = 0, bj = 1;
                        for (int i = 0; i < tupleList.Count; i++)
                            for (int j = i + 1; j < tupleList.Count; j++)
                            {
                                double d = 0;
                                for (int v = 0; v < s.Variants.Length; v++) d += Dist(tupleList[i][v], tupleList[j][v]);
                                d *= Math.Min(cnt[i], cnt[j]);
                                if (d < best) { best = d; bi = i; bj = j; }
                            }
                        if (cnt[bj] > cnt[bi]) { int t = bi; bi = bj; bj = t; }
                        cnt[bi] += cnt[bj];
                        tupleList.RemoveAt(bj); cnt.RemoveAt(bj);
                        for (int k = 0; k < ids.Length; k++)
                        {
                            if (ids[k] == bj + 1) ids[k] = bi + 1;
                            if (ids[k] > bj + 1) ids[k]--;
                        }
                        if (bi > bj) bi--;
                    }
                    for (int k = 0; k < ids.Length; k++) s.Idx[k] = (byte)ids[k];
                    for (int v = 0; v < s.Variants.Length; v++)
                    {
                        var pal = new int[16];
                        for (int i = 0; i < tupleList.Count && i < 15; i++) pal[i + 1] = tupleList[i][v];
                        s.VarPals.Add(pal);
                    }
                    if (s.Slot < 0) throw new Exception("sprite " + s.Id + " with variants needs a fixed slot");
                    s.PalSlot = s.Slot;
                    if (Pals[s.Slot] == null) Pals[s.Slot] = s.VarPals[0];
                }
                // 2) packed images: color sets -> free slots (greedy, merge colors if needed)
                var packedImgs = new List<SprImg>();
                foreach (var s in Imgs) if (s.Variants.Length == 1 && !s.Dynamic) packedImgs.Add(s);
                var free = new List<int>();
                for (int p = 0; p < 8; p++) if (Pals[p] == null) free.Add(p);
                if (packedImgs.Count > 0)
                {
                    // fixed-slot single images are forced into their slot's set
                    var tiles = new List<int[]>();
                    foreach (var s in packedImgs)
                    {
                        var t = new int[s.W * s.H];
                        for (int y = 0; y < s.H; y++) for (int x = 0; x < s.W; x++) t[y * s.W + x] = s.Variants[0].At(s.X0 + x, s.Y0 + y);
                        tiles.Add(t);
                    }
                    int[][] pals; double err;
                    var remap = Quantize(tiles, free.Count, out pals, out err);
                    for (int i = 0; i < packedImgs.Count; i++)
                    {
                        var s = packedImgs[i]; var t = tiles[i];
                        int pal = -1;
                        for (int p = 0; p < pals.Length && pal < 0; p++)
                        {
                            bool ok = true;
                            foreach (int c in t) if (c != Transparent && Array.IndexOf(pals[p], remap[c], 1) < 0) { ok = false; break; }
                            if (ok) pal = p;
                        }
                        s.PalSlot = free[pal];
                        s.Idx = new byte[s.W * s.H];
                        for (int k = 0; k < t.Length; k++) s.Idx[k] = t[k] == Transparent ? (byte)0 : (byte)Array.IndexOf(pals[pal], remap[t[k]], 1);
                    }
                    for (int p = 0; p < free.Count; p++) Pals[free[p]] = pals[p];
                    Report += string.Format("obj color error {0:F0}; ", err);
                }
                for (int p = 0; p < 8; p++) if (Pals[p] == null) Pals[p] = new int[16];
                // 3) pieces: cover each image with 16x16 cells (grid anchored at the bbox), 8x8 where a quadrant suffices
                var cellIndex = new Dictionary<string, int>();
                var tileIndex = new Dictionary<string, int>();
                int next16 = 0, next8 = 511;   // 16x16 cells from the front (tile = (n%8)*2 + (n/8)*32), 8x8 from the back
                foreach (var s in Imgs)
                {
                    for (int cy = 0; cy < s.H; cy += 16)
                        for (int cx = 0; cx < s.W; cx += 16)
                        {
                            var q = new byte[4][];
                            int nonEmpty = 0, lastQ = -1;
                            for (int k = 0; k < 4; k++)
                            {
                                q[k] = new byte[64];
                                bool any = false;
                                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                                {
                                    int ix = cx + (k & 1) * 8 + x, iy = cy + (k >> 1) * 8 + y;
                                    byte v = ix < s.W && iy < s.H ? s.Idx[iy * s.W + ix] : (byte)0;
                                    q[k][y * 8 + x] = v; if (v != 0) any = true;
                                }
                                if (any) { nonEmpty++; lastQ = k; }
                            }
                            if (nonEmpty == 0) continue;
                            if (nonEmpty == 1)
                            {
                                string key = s.PalSlot + ":" + Key64(q[lastQ]);
                                int tn;
                                if (!tileIndex.TryGetValue(key, out tn))
                                {
                                    tn = next8--; tileIndex[key] = tn; Cells[tn] = q[lastQ]; Used8++;
                                }
                                s.Pieces.Add(new[] { cx + (lastQ & 1) * 8, cy + (lastQ >> 1) * 8, tn, 0 });
                                continue;
                            }
                            string ck = s.PalSlot + ":" + Key64(q[0]) + Key64(q[1]) + Key64(q[2]) + Key64(q[3]);
                            int cn;
                            if (!cellIndex.TryGetValue(ck, out cn))
                            {
                                cn = next16++; cellIndex[ck] = cn;
                                int t0 = (cn % 8) * 2 + (cn / 8) * 32;
                                Cells[t0] = q[0]; Cells[t0 + 1] = q[1]; Cells[t0 + 16] = q[2]; Cells[t0 + 17] = q[3];
                                Used16++;
                            }
                            s.Pieces.Add(new[] { cx, cy, (cn % 8) * 2 + (cn / 8) * 32, 1 });
                        }
                }
                int top16 = ((next16 + 7) / 8) * 32;   // tiles below this belong to 16x16 cell rows
                if (next8 + 1 < top16) throw new Exception("OBJ set overflow: " + next16 + " cells, " + (511 - next8) + " singles");
                Report += string.Format("{0} cells + {1} singles", Used16, Used8);
            }

            public int TileCount { get { int n = 0; for (int i = 0; i < 512; i++) if (Cells[i] != null) n = i + 1; return n; } }

            /// <summary>OBJ CHR, part 1: tile 0 up to the end of the 16x16 cell rows.</summary>
            public byte[] ChrBytes()
            {
                int top = 0;
                for (int i = 0; i < 512; i++) if (Cells[i] != null && i < FirstSingle) top = (i / 32 + 1) * 32;
                var l = new List<byte[]>();
                for (int i = 0; i < top; i++) l.Add(Cells[i] ?? new byte[64]);
                return Chr4(l);
            }
            /// <summary>First 8x8 single (they are allocated from tile 511 down).</summary>
            public int FirstSingle { get { int f = 512; for (int i = 511; i >= 0 && Cells[i] != null; i--) f = i; return f; } }
            /// <summary>OBJ CHR, part 2: the singles (tiles FirstSingle..511).</summary>
            public byte[] Chr2Bytes()
            {
                var l = new List<byte[]>();
                for (int i = FirstSingle; i < 512; i++) l.Add(Cells[i] ?? new byte[64]);
                return Chr4(l);
            }
            public byte[] PalBytes()
            {
                var b = new byte[256];
                for (int p = 0; p < 8; p++) for (int i = 0; i < 16; i++) { b[p * 32 + i * 2] = (byte)Pals[p][i]; b[p * 32 + i * 2 + 1] = (byte)(Pals[p][i] >> 8); }
                return b;
            }
        }

        // ------------------------------------------------------------------ previews (debug)
        public static void Preview(string path, int w, int h, Func<int, int, int> bgrAt)
        {
            var buf = new int[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int c = bgrAt(x, y);
                buf[y * w + x] = c < 0 ? unchecked((int)0xFF000000) : unchecked((int)0xFF000000) | Rgb(c);
            }
            SMB4.Platform.Host.WritePng(buf, w, h, 2, path);
        }

        /// <summary>Renders a map (as the SNES would show it) for previews.</summary>
        public static int MapPixel(BgPool pool, ushort[] words, int tw, int x, int y)
        {
            int tx = x / 8, ty = y / 8;
            if (tx >= tw || ty * tw + tx >= words.Length) return Transparent;
            int w = words[ty * tw + tx];
            int t = w & 0x3FF, pal = ((w >> 10) & 7) - pool.FirstPal;
            int px = x & 7, py = y & 7;
            if ((w & 0x4000) != 0) px = 7 - px;
            if ((w & 0x8000) != 0) py = 7 - py;
            int v = pool.Tiles[t][py * 8 + px];
            if (v == 0) return Transparent;
            return pool.Pals[pal][v];
        }
    }

    // ====================================================================== ROM blob packer + asm writer
    sealed class ScrAsm
    {
        sealed class BlobRec { public string Label; public byte[] Data; public int Bank = -1; }
        readonly List<BlobRec> blobs = new List<BlobRec>();
        readonly Dictionary<string, BlobRec> byHash = new Dictionary<string, BlobRec>();
        public readonly StringBuilder Code = new StringBuilder();   // RODATA-ish tables (placed in CODE7)
        public readonly StringBuilder Inc = new StringBuilder();
        readonly string dir;
        public readonly int FirstBank, LastBank;
        public ScrAsm(string dir, int firstBank, int lastBank) { this.dir = dir; FirstBank = firstBank; LastBank = lastBank; }

        /// <summary>Adds a binary blob (deduplicated) and returns its label.</summary>
        public string Blob(string label, byte[] data)
        {
            string h = Convert.ToBase64String(System.Security.Cryptography.MD5.Create().ComputeHash(data)) + data.Length;
            BlobRec b;
            if (byHash.TryGetValue(h, out b)) return b.Label;
            if (data.Length > 0x8000) throw new Exception("blob too big: " + label);
            b = new BlobRec { Label = label, Data = data };
            blobs.Add(b); byHash[h] = b;
            return label;
        }

        public void Write(string asmName, string incName)
        {
            // first-fit decreasing into banks
            var order = new List<BlobRec>(blobs);
            order.Sort((a, b) => b.Data.Length.CompareTo(a.Data.Length));
            var used = new int[LastBank - FirstBank + 1];
            foreach (var b in order)
            {
                for (int k = 0; k < used.Length; k++)
                    if (used[k] + b.Data.Length <= 0x8000) { b.Bank = FirstBank + k; used[k] += b.Data.Length; break; }
                if (b.Bank < 0) throw new Exception("screens data does not fit BANK" + FirstBank + "-" + LastBank);
            }
            var sb = new StringBuilder();
            sb.AppendLine("; generated by smb4tools snes-export (SnesScreens.cs) - do not edit");
            sb.AppendLine(".p816");
            sb.AppendLine(".include \"scr_ids.inc\"");
            sb.AppendLine(".include \"levels.inc\"");
            sb.AppendLine(".include \"music.inc\"");
            var exports = new List<string>();
            for (int k = 0; k < used.Length; k++)
            {
                if (used[k] == 0) continue;
                sb.AppendLine(".segment \"BANK" + (FirstBank + k) + "\"");
                foreach (var b in blobs)
                {
                    if (b.Bank != FirstBank + k) continue;
                    string file = "scr_" + b.Label + ".bin";
                    File.WriteAllBytes(Path.Combine(dir, file), b.Data);
                    sb.AppendLine(b.Label + ": .incbin \"" + file + "\"");
                }
            }
            sb.AppendLine(".segment \"CODE7\"");
            sb.Append(Code);
            File.WriteAllText(Path.Combine(dir, asmName), sb.ToString());
            File.WriteAllText(Path.Combine(dir, incName), "; generated by smb4tools snes-export (SnesScreens.cs) - do not edit\n" + Inc.ToString());
            int total = 0; foreach (var b in blobs) total += b.Data.Length;
            int nb = 0; foreach (int u in used) if (u > 0) nb++;
            Console.WriteLine("  screens: " + blobs.Count + " blobs, " + (total / 1024) + " KB in " + nb + " banks (BANK" + FirstBank + "+)");
        }
    }
}
