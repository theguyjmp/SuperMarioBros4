using System;
using System.Collections.Generic;
using System.Globalization;

namespace SMB4.Engine
{
    /// <summary>
    /// Loads data/art/*.art. Format:
    ///   pal NAME c1 c2 c3            (NES color indices in hex for slots 1..3; slot 0 = transparent)
    ///   img NAME WxH [pal=NAME]      followed by H rows of W characters: '.'/'0' = 0, '1' '2' '3' = slots
    /// Images are 2-bit; palettes are chosen at draw time (so one image serves Mario, Fire Mario, Luigi...).
    /// </summary>
    public static class Art
    {
        static readonly Dictionary<string, Img> imgs = new Dictionary<string, Img>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ushort[]> pals = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> previewPal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> splits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Order = new List<string>();
        static readonly HashSet<string> missingReported = new HashSet<string>();
        static Img missing;

        public static void Load()
        {
            imgs.Clear(); pals.Clear(); previewPal.Clear(); splits.Clear(); Errors.Clear(); Order.Clear();
            missing = new Img(8, 8);
            for (int i = 0; i < 64; i++) missing.P[i] = (byte)(((i / 8 + i % 8) & 1) + 1);
            foreach (var path in Data.List("art/"))
                if (path.EndsWith(".art", StringComparison.OrdinalIgnoreCase)) Parse(path, Data.ReadText(path));
        }

        static void Parse(string file, string text)
        {
            if (text == null) return;
            var lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == "pal")
                {
                    // pal NAME c1 .. c15 : each color is an NES index (2 hex digits) or a 24-bit #RRGGBB
                    if (parts.Length < 3 || parts.Length > 17) { Errors.Add(file + ":" + (i + 1) + " bad pal line (1..15 colors)"); continue; }
                    var p = new ushort[parts.Length - 1];
                    bool ok = true;
                    for (int k = 2; k < parts.Length; k++)
                    {
                        string c = parts[k];
                        int v;
                        if (c.StartsWith("#") && c.Length == 7 && int.TryParse(c.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) p[k - 1] = (ushort)NesPalette.Color(v);
                        else if (c.Length <= 2 && int.TryParse(c, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) && v < 64) p[k - 1] = (ushort)v;
                        else { Errors.Add(file + ":" + (i + 1) + " bad color '" + c + "'"); ok = false; }
                    }
                    if (ok) pals[parts[1]] = p;
                }
                else if (parts[0] == "img")
                {
                    if (parts.Length < 3) { Errors.Add(file + ":" + (i + 1) + " bad img line"); continue; }
                    string name = parts[1];
                    var wh = parts[2].Split('x');
                    int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
                    for (int k = 3; k < parts.Length; k++) if (parts[k].StartsWith("pal=")) previewPal[name] = parts[k].Substring(4);
                    var img = new Img(w, h);
                    for (int y = 0; y < h; y++)
                    {
                        i++;
                        if (i >= lines.Length) { Errors.Add(file + ": image " + name + " truncated"); break; }
                        string row = lines[i].TrimEnd();
                        int lead = 0; while (lead < row.Length && row[lead] == ' ') lead++;
                        row = row.Substring(lead);
                        if (row.Length != w) Errors.Add(file + ":" + (i + 1) + " image " + name + " row " + y + " has width " + row.Length + ", expected " + w);
                        for (int x = 0; x < w && x < row.Length; x++)
                        {
                            char c = row[x];
                            // '.'/'0' transparent, '1'-'9' and 'a'-'f' = palette slots 1..15
                            img.P[y * w + x] = (byte)(c >= '1' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : 0);
                        }
                    }
                    if (imgs.ContainsKey(name)) Errors.Add(file + ": duplicate image " + name);
                    imgs[name] = img;
                    Order.Add(name);
                }
                else if (parts[0] == "split")
                {
                    // split GROUP ROW: GROUP.* images draw rows < ROW with GROUP.head, the rest with GROUP.body
                    int row;
                    if (parts.Length < 3 || !int.TryParse(parts[2], out row)) { Errors.Add(file + ":" + (i + 1) + " bad split line"); continue; }
                    splits[parts[1]] = row;
                }
                else Errors.Add(file + ":" + (i + 1) + " unknown line: " + line);
            }
        }

        public static bool Has(string name) { return imgs.ContainsKey(name); }

        public static Img Get(string name)
        {
            Img i;
            if (imgs.TryGetValue(name, out i)) return i;
            if (missingReported.Add(name)) Errors.Add("missing image: " + name);
            return missing;
        }

        public static ushort[] Pal(string name)
        {
            ushort[] p;
            if (pals.TryGetValue(name, out p)) return p;
            if (missingReported.Add("pal:" + name)) Errors.Add("missing palette: " + name);
            return new ushort[] { 0, 0x0F, 0x16, 0x30 };
        }

        public static bool HasPal(string name) { return pals.ContainsKey(name); }

        /// <summary>Palette-split row declared with "split GROUP ROW", or -1.</summary>
        public static int SplitRow(string group)
        {
            int r;
            return splits.TryGetValue(group, out r) ? r : -1;
        }

        /// <summary>Draws GROUP.* images that use two palettes (GROUP.head above the split row, GROUP.body below).</summary>
        public static void DrawSplit(Ppu ppu, string img, string group, int x, int y, bool flipH = false)
        {
            int s = SplitRow(group);
            var top = HasPal(group + ".head") ? Pal(group + ".head") : PreviewPalFor(img);
            if (s < 0 || !HasPal(group + ".body")) ppu.Spr(Get(img), x, y, top, flipH);
            else ppu.SprSplit(Get(img), x, y, top, Pal(group + ".body"), s, flipH);
        }

        /// <summary>The palette name an image was authored with (its pal= tag), or null.</summary>
        public static string PreviewPalName(string img)
        {
            string p;
            return previewPal.TryGetValue(img, out p) ? p : null;
        }

        public static ushort[] PreviewPalFor(string img)
        {
            string p;
            if (previewPal.TryGetValue(img, out p)) return Pal(p);
            return new ushort[] { 0, 0x0F, 0x00, 0x30 };
        }

        /// <summary>Makes a palette array from three NES colors.</summary>
        public static ushort[] P(int c1, int c2, int c3) { return new ushort[] { 0, (ushort)c1, (ushort)c2, (ushort)c3 }; }

        /// <summary>Makes a palette from any number of color indices (slot 0 = transparent is added).</summary>
        public static ushort[] PN(params int[] c) { var p = new ushort[c.Length + 1]; for (int i = 0; i < c.Length; i++) p[i + 1] = (ushort)c[i]; return p; }

        /// <summary>Color index for a 24-bit RGB color (0xRRGGBB).</summary>
        public static int Rgb(int rgb) { return NesPalette.Color(rgb); }
    }
}
