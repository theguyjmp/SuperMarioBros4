using System;
using System.Collections.Generic;
using System.IO;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Tools
{
    /// <summary>Headless developer tools (console exe): art sheets, level renders, validation, tests, audio renders.</summary>
    public static class ToolsProgram
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0) { Console.WriteLine("commands: sheet <png> | validate | level <name> <png> | selftest | shots <dir> | song <name> <wav> [sec]"); return 1; }
            Host.Settings = new Settings();
            Host.Input = null;
            try
            {
                Art.Load();
                switch (args[0].ToLowerInvariant())
                {
                    case "sheet": return Sheet(args.Length > 1 ? args[1] : "sheet.png", args.Length > 2 ? args[2] : null);
                    case "validate": return Validate();
                    case "song": return Song(args[1], args[2], args.Length > 3 ? double.Parse(args[3]) : 30);
                    default:
                        return Extra.Run(args);
                }
            }
            catch (Exception ex) { Console.WriteLine("ERROR: " + ex); return 2; }
        }

        static int Sheet(string png, string filter)
        {
            var names = new List<string>();
            foreach (var n in Art.Order) if (filter == null || n.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) names.Add(n);
            // shelf packing into a 640-wide sheet; each image gets its name printed underneath (tiny labels)
            int W = 640, x = 4, y = 4, rowH = 0;
            var pos = new List<int[]>();
            foreach (var n in names)
            {
                var img = Art.Get(n);
                string label = n.Length > 16 ? n.Substring(n.Length - 16) : n;
                int cw = Math.Max(img.W, label.Length * 4) + 6;
                int ch = img.H + 9;
                if (x + cw > W) { x = 4; y += rowH + 4; rowH = 0; }
                pos.Add(new[] { x, y });
                x += cw; rowH = Math.Max(rowH, ch);
            }
            int H = y + rowH + 4;
            var buf = new int[W * H];
            for (int i = 0; i < buf.Length; i++) buf[i] = unchecked((int)0xFF2A3A4A);
            for (int k = 0; k < names.Count; k++)
            {
                var img = Art.Get(names[k]);
                var pal = Art.PreviewPalFor(names[k]);
                for (int yy = 0; yy < img.H; yy++)
                    for (int xx = 0; xx < img.W; xx++)
                    {
                        int v = img.P[yy * img.W + xx];
                        int o = (pos[k][1] + yy) * W + pos[k][0] + xx;
                        buf[o] = v == 0 ? ((xx / 4 + yy / 4) % 2 == 0 ? unchecked((int)0xFF6A8AAA) : unchecked((int)0xFF5A7A9A)) : unchecked((int)0xFF000000) | NesPalette.RgbOf(pal[v]);
                    }
                string label = names[k].Length > 16 ? names[k].Substring(names[k].Length - 16) : names[k];
                TinyText(buf, W, H, label, pos[k][0], pos[k][1] + img.H + 2, unchecked((int)0xFFFFFFFF));
            }
            Host.WritePng(buf, W, H, 3, png);
            Console.WriteLine("wrote " + png + " (" + names.Count + " images)");
            foreach (var e in Art.Errors) Console.WriteLine("art: " + e);
            return 0;
        }

        /// <summary>3x5 pixel font for sheet labels (a-z, 0-9, '.', '_', '-').</summary>
        static readonly Dictionary<char, string> Tiny = new Dictionary<char, string>
        {
            {'a',"010101111101101"},{'b',"110101110101110"},{'c',"011100100100011"},{'d',"110101101101110"},{'e',"111100110100111"},
            {'f',"111100110100100"},{'g',"011100101101011"},{'h',"101101111101101"},{'i',"111010010010111"},{'j',"001001001101010"},
            {'k',"101101110101101"},{'l',"100100100100111"},{'m',"101111111101101"},{'n',"110101101101101"},{'o',"010101101101010"},
            {'p',"110101110100100"},{'q',"010101101111011"},{'r',"110101110101101"},{'s',"011100010001110"},{'t',"111010010010010"},
            {'u',"101101101101111"},{'v',"101101101101010"},{'w',"101101111111101"},{'x',"101101010101101"},{'y',"101101010010010"},
            {'z',"111001010100111"},{'0',"111101101101111"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},
            {'4',"101101111001001"},{'5',"111100110001110"},{'6',"011100110101010"},{'7',"111001010010010"},{'8',"010101010101010"},
            {'9',"010101011001110"},{'.',"000000000000010"},{'_',"000000000000111"},{'-',"000000111000000"},
        };

        static void TinyText(int[] buf, int W, int H, string s, int x, int y, int color)
        {
            s = s.ToLowerInvariant();
            for (int i = 0; i < s.Length; i++)
            {
                string g;
                if (!Tiny.TryGetValue(s[i], out g)) continue;
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                        if (g[r * 3 + c] == '1')
                        {
                            int px = x + i * 4 + c, py = y + r;
                            if (px >= 0 && px < W && py >= 0 && py < H) buf[py * W + px] = color;
                        }
            }
        }

        static int Validate()
        {
            Sound.Init(false);
            int errs = 0;
            foreach (var e in Art.Errors) { Console.WriteLine("art: " + e); errs++; }
            foreach (var e in Sound.Errors) { Console.WriteLine("music: " + e); errs++; }
            foreach (var name in Sound.SongNames)
            {
                var s = Sound.GetSong(name);
                int reference = -1;
                for (int c = 0; c < Ch.Count; c++)
                {
                    if (s.Tracks[c].Length == 0) continue;
                    int intro; int len = Mml.LoopLength(s.Tracks[c], out intro);
                    if (reference < 0) reference = len;
                    else if (s.Loops && len != reference) { Console.WriteLine("music: " + name + " " + Mml.ChName(c) + " loop length " + len + " != " + reference); errs++; }
                }
            }
            errs += Extra.Validate();
            Console.WriteLine(errs == 0 ? "OK" : errs + " problem(s)");
            return errs == 0 ? 0 : 1;
        }

        static int Song(string name, string wav, double seconds)
        {
            Sound.Init(false);
            var pcm = Sound.RenderOffline(name, seconds);
            using (var f = new FileStream(wav, FileMode.Create))
            using (var w = new BinaryWriter(f))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(AudioOut.Rate); w.Write(AudioOut.Rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(pcm.Length * 2);
                foreach (var s in pcm) w.Write(s);
            }
            int peak = 0; foreach (var s in pcm) peak = Math.Max(peak, Math.Abs((int)s));
            Console.WriteLine("wrote " + wav + " peak=" + peak);
            return 0;
        }
    }
}
