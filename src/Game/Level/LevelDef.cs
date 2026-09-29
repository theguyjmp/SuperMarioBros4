using System;
using System.Collections.Generic;
using System.Globalization;
using SMB4.Engine;

namespace SMB4.Game
{
    public struct SpawnDef { public char Code; public int X, Y; public int Param; }
    public struct DecorDef { public string Img; public int X, Y; }

    public sealed class AreaDef
    {
        public int W, H;
        public T[] Tiles;
        public byte[] Var;          // visual variant (auto-tiling piece / block color)
        public Content[] Contents;
        public sbyte[] Link;        // link marker digit per cell (-1 none)
        public List<SpawnDef> Spawns = new List<SpawnDef>();
        public List<DecorDef> Decor = new List<DecorDef>();
        public string Theme = "plains", Music = "overworld";
        public int Backdrop = -1;
        public string Scroll = "normal";   // normal | free | lock | vertical | auto
        public int AutoSpeed = 8;          // autoscroll speed in 1/16 px per tick
        public int WaterRow = -1;          // rows >= this are underwater (0 = whole area)
        public bool AutoDecor = true;
        public int GoalX = -1, GoalY = -1;
        public bool Dark;                  // unused visual hint
        public bool HasSlopes;             // SMB3 uses the sloped probe set in areas with slopes

        public T Get(int x, int y) { return x < 0 || y < 0 || x >= W || y >= H ? T.Empty : Tiles[y * W + x]; }
    }

    public sealed class LinkDef { public int FromArea, FromId, ToArea, ToId; }

    public sealed class LevelDef
    {
        public string Id = "", Title = "", Kind = "level";
        public int Time = 300;
        public int StartArea, StartX = 2, StartY = 10;
        public List<AreaDef> Areas = new List<AreaDef>();
        public List<LinkDef> Links = new List<LinkDef>();
        public List<string> Errors = new List<string>();

        public LinkDef FindLink(int area, int id)
        {
            foreach (var l in Links) if (l.FromArea == area && l.FromId == id) return l;
            return null;
        }
    }

    /// <summary>
    /// Parses data/levels/*.lvl (format documented in data/levels/README.md).
    /// </summary>
    public static class LevelLoader
    {
        static readonly Dictionary<string, LevelDef> cache = new Dictionary<string, LevelDef>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() { cache.Clear(); }

        public static LevelDef Load(string id)
        {
            LevelDef d;
            if (cache.TryGetValue(id, out d)) return d;
            string text = Data.ReadText("levels/" + id + ".lvl");
            if (text == null) return null;
            d = Parse(id, text);
            cache[id] = d;
            return d;
        }

        public static LevelDef Parse(string id, string text)
        {
            var lv = new LevelDef { Id = id };
            var lines = text.Replace("\r", "").Split('\n');
            AreaDef area = null;
            List<string> rows = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                if (rows != null)
                {
                    if (raw.Trim() == "end") { Finish(lv, area, rows); area = null; rows = null; continue; }
                    rows.Add(raw.TrimEnd());
                    continue;
                }
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0 && (hash == 0 || line[hash - 1] == ' ' || line[hash - 1] == '\t')) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string key = parts[0].ToLowerInvariant();
                try
                {
                    if (key == "area")
                    {
                        area = new AreaDef();
                        for (int k = 2; k < parts.Length; k++) AreaOpt(lv, area, parts[k]);
                        rows = new List<string>();
                    }
                    else if (key == "link")
                    {
                        // link A:d -> B:e
                        var from = parts[1].Split(':'); var to = parts[3].Split(':');
                        lv.Links.Add(new LinkDef { FromArea = int.Parse(from[0]), FromId = int.Parse(from[1]), ToArea = int.Parse(to[0]), ToId = int.Parse(to[1]) });
                    }
                    else
                    {
                        string val = line.Contains("=") ? line.Substring(line.IndexOf('=') + 1).Trim() : string.Join(" ", parts, 1, parts.Length - 1);
                        if (key.EndsWith("=")) key = key.TrimEnd('=');
                        if (key.Contains("=")) key = key.Substring(0, key.IndexOf('='));
                        switch (key)
                        {
                            case "name": case "title": lv.Title = val; break;
                            case "kind": lv.Kind = val.ToLowerInvariant(); break;
                            case "time": lv.Time = int.Parse(val); break;
                            case "start":
                            {
                                var s = val.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                lv.StartX = int.Parse(s[0]); lv.StartY = int.Parse(s[1]);
                                if (s.Length > 2) lv.StartArea = int.Parse(s[2]);
                                break;
                            }
                            default: lv.Errors.Add(id + ":" + (i + 1) + " unknown key '" + key + "'"); break;
                        }
                    }
                }
                catch (Exception ex) { lv.Errors.Add(id + ":" + (i + 1) + " " + ex.Message); }
            }
            if (rows != null) lv.Errors.Add(id + ": area missing 'end'");
            if (lv.Areas.Count == 0) lv.Errors.Add(id + ": no areas");
            return lv;
        }

        static void AreaOpt(LevelDef lv, AreaDef a, string opt)
        {
            int eq = opt.IndexOf('=');
            if (eq < 0) { lv.Errors.Add(lv.Id + ": bad area option " + opt); return; }
            string k = opt.Substring(0, eq).ToLowerInvariant(), v = opt.Substring(eq + 1);
            switch (k)
            {
                case "theme": a.Theme = v; break;
                case "music": a.Music = v; break;
                case "bg": a.Backdrop = int.Parse(v, NumberStyles.HexNumber); break;
                case "scroll": a.Scroll = v.ToLowerInvariant(); break;
                case "speed": a.AutoSpeed = int.Parse(v); break;
                case "water": a.WaterRow = int.Parse(v); break;
                case "decor": a.AutoDecor = v != "0"; break;
                default: lv.Errors.Add(lv.Id + ": unknown area option " + k); break;
            }
        }

        static bool IsLinkDigit(char c) { return c >= '0' && c <= '9'; }

        static void Finish(LevelDef lv, AreaDef a, List<string> rows)
        {
            int h = rows.Count, w = 0;
            foreach (var r in rows) w = Math.Max(w, r.Length);
            a.W = w; a.H = h;
            a.Tiles = new T[w * h]; a.Var = new byte[w * h]; a.Contents = new Content[w * h];
            a.Link = new sbyte[w * h];
            for (int i = 0; i < a.Link.Length; i++) a.Link[i] = -1;
            var g = new char[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) g[x, y] = x < rows[y].Length ? rows[y][x] : '.';

            // resolve link digits into the pipe/door char they stand on
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = g[x, y];
                    if (!IsLinkDigit(c)) continue;
                    a.Link[y * w + x] = (sbyte)(c - '0');
                    char right = x + 1 < w ? g[x + 1, y] : '.', below = y + 1 < h ? g[x, y + 1] : '.', above = y > 0 ? g[x, y - 1] : '.';
                    char left = x > 0 ? g[x - 1, y] : '.';
                    if (right == ']') g[x, y] = '[';
                    else if (left == '[') g[x, y] = ']';
                    else if (below == '}' || right == '{' || left == '{') g[x, y] = '{';
                    else if (above == '}' || above == '{') g[x, y] = '}';
                    else if (above == 'D') g[x, y] = 'D';
                    else g[x, y] = '.';   // free-standing marker (an arrival point)
                }

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = g[x, y];
                    int i = y * w + x;
                    T t = T.Empty; Content ct = Content.None;
                    switch (c)
                    {
                        case '.': case ' ': break;
                        case '#': t = T.Ground; break;
                        case '%': t = T.Hard; break;
                        case '=': t = T.Semi; break;
                        case '&': t = T.Cloud; break;
                        case 'B': t = T.Brick; break;
                        case '?': t = T.QBlock; ct = Content.Coin; break;
                        case 'F': t = T.QBlock; ct = Content.Flower; break;
                        case 'L': t = T.QBlock; ct = Content.Leaf; break;
                        case '*': t = T.QBlock; ct = Content.OneUp; break;
                        case 'S': t = T.Brick; ct = Content.Star; break;
                        case '$': t = T.Brick; ct = Content.MultiCoin; break;
                        case '!': t = T.Brick; ct = Content.PSwitch; break;
                        case 'V': t = T.Brick; ct = Content.Vine; break;
                        case 'Q': t = T.Brick; ct = Content.Flower; break;
                        case 'O': t = T.Brick; ct = Content.Leaf; break;
                        case 'h': t = T.HiddenBlock; ct = Content.OneUp; break;
                        case 'H': t = T.HiddenBlock; ct = Content.Coin; break;
                        case 'N': t = T.Note; break;
                        case 'W': t = T.Wood; break;
                        case 'I': t = T.Ice; break;
                        case 'U': t = T.Used; break;
                        case 'M': t = T.Muncher; break;
                        case '^': t = T.Spike; break;
                        case 'X': t = T.Barrier; break;
                        case 'o': t = T.Coin; break;
                        case '~': t = T.Lava; break;
                        case '|': t = T.Vine; break;
                        case 'D': t = T.DoorBot; break;
                        case '[': t = T.PipeL; break;
                        case ']': t = T.PipeR; break;
                        case '{': t = T.HPipeT; break;
                        case '}': t = T.HPipeB; break;
                        case 'b': t = T.CannonMid; break;
                        case '<': t = T.CannonL; a.Spawns.Add(new SpawnDef { Code = '<', X = x, Y = y }); break;
                        case '>': t = T.CannonR; a.Spawns.Add(new SpawnDef { Code = '>', X = x, Y = y }); break;
                        case '/': t = T.SlopeUp; a.HasSlopes = true; break;
                        case '\\': t = T.SlopeDown; a.HasSlopes = true; break;
                        case '(': t = T.ConveyorL; break;
                        case ')': t = T.ConveyorR; break;
                        case 'A': t = T.BigBlock; a.Var[i] = 1; break;
                        case 'C': t = T.BigBlock; a.Var[i] = 2; break;
                        case 'E': t = T.BigBlock; a.Var[i] = 3; break;
                        case 'J': t = T.BigBlock; a.Var[i] = 4; break;
                        case 'G': a.GoalX = x; a.GoalY = y; break;
                        default:
                            if ((c >= 'a' && c <= 'z') || c == 'Z' || c == 'K' || c == 'Y' || c == 'P' || c == 'R' || c == '-' || c == '_' || c == ':' || c == 'T')
                                a.Spawns.Add(new SpawnDef { Code = c, X = x, Y = y });
                            else lv.Errors.Add(lv.Id + ": unknown tile '" + c + "' at " + x + "," + y);
                            break;
                    }
                    a.Tiles[i] = t;
                    a.Contents[i] = ct;
                }

            // second pass: pipes, doors, cannons, lava tops
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    T t = a.Tiles[i];
                    if (t == T.PipeL || t == T.PipeR)
                    {
                        bool left = t == T.PipeL;
                        T up = a.Get(x, y - 1), dn = a.Get(x, y + 1);
                        bool pipeUp = up == T.PipeL || up == T.PipeR || up == T.PipeTL || up == T.PipeTR;
                        bool pipeDn = dn == T.PipeL || dn == T.PipeR;
                        if (!pipeUp && !TileInfo.Solid(up)) a.Tiles[i] = left ? T.PipeTL : T.PipeTR;
                        else if (!pipeDn && !TileInfo.Floor(dn) && pipeUp) a.Tiles[i] = left ? T.PipeBL : T.PipeBR;
                    }
                    else if (t == T.HPipeT || t == T.HPipeB)
                    {
                        bool top = t == T.HPipeT;
                        T l = a.Get(x - 1, y), r = a.Get(x + 1, y);
                        bool pl = l == T.HPipeT || l == T.HPipeB || l == T.HPipeMouthT || l == T.HPipeMouthB;
                        bool pr = r == T.HPipeT || r == T.HPipeB;
                        if (!pl && !TileInfo.Solid(l)) { a.Tiles[i] = top ? T.HPipeMouthT : T.HPipeMouthB; a.Var[i] = 0; }      // mouth facing left
                        else if (!pr && !TileInfo.Solid(r) && x + 1 < w) { a.Tiles[i] = top ? T.HPipeMouthT : T.HPipeMouthB; a.Var[i] = 1; } // mouth facing right
                    }
                    else if (t == T.DoorBot)
                    {
                        if (a.Get(x, y + 1) == T.DoorBot) a.Tiles[i] = T.DoorTop;
                    }
                    else if (t == T.CannonMid)
                    {
                        if (a.Get(x, y - 1) != T.CannonMid && a.Get(x, y - 1) != T.CannonTop)
                        {
                            a.Tiles[i] = T.CannonTop;
                            a.Spawns.Add(new SpawnDef { Code = 'b', X = x, Y = y });
                        }
                    }
                    else if (t == T.Lava)
                    {
                        if (!TileInfo.Lava(a.Get(x, y - 1))) a.Tiles[i] = T.LavaTop;
                    }
                    else if (t == T.BigBlock && y > 0)
                    {
                        // only a big block's top row is a floor; the rows below it can be jumped through
                        T up = a.Get(x, y - 1);
                        if ((up == T.BigBlock || up == T.BigBlockBody) && a.Var[i - w] == a.Var[i]) a.Tiles[i] = T.BigBlockBody;
                    }
                }
            // door tops: a DoorBot whose lower neighbour is also a door became DoorTop above; make sure the bottom stays bottom
            lv.Areas.Add(a);
            Decorate(a);
            a.Spawns.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));
        }

        static uint Hash(int x, int y) { unchecked { uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h; } }

        static bool Outdoors(string theme)
        {
            switch (theme) { case "plains": case "desert": case "sea": case "jungle": case "sky": case "ice": case "airship": return true; }
            return false;
        }

        /// <summary>Automatic scenery: bushes on grass, hills and sky clouds outdoors, pillars/torches indoors.</summary>
        static void Decorate(AreaDef a)
        {
            if (!a.AutoDecor) return;
            bool outdoors = Outdoors(a.Theme);
            int skyRows = Math.Max(1, a.H - 8);
            for (int x = 0; x < a.W; x++)
            {
                // find the first surface: ground with empty above
                for (int y = 1; y < a.H; y++)
                {
                    if (a.Get(x, y) != T.Ground || a.Get(x, y - 1) != T.Empty) continue;
                    uint hsh = Hash(x, y);
                    if (outdoors && a.Theme != "sky" && a.Theme != "airship" && hsh % 9 == 0 && x + 2 < a.W &&
                        a.Get(x + 1, y) == T.Ground && a.Get(x + 2, y) == T.Ground &&
                        a.Get(x + 1, y - 1) == T.Empty && a.Get(x + 2, y - 1) == T.Empty && a.GoalX < 0 | x < a.GoalX - 2)
                    {
                        a.Decor.Add(new DecorDef { Img = "bush.l", X = x, Y = y - 1 });
                        a.Decor.Add(new DecorDef { Img = "bush.c", X = x + 1, Y = y - 1 });
                        a.Decor.Add(new DecorDef { Img = "bush.r", X = x + 2, Y = y - 1 });
                        x += 3;
                    }
                    else if (outdoors && (a.Theme == "plains" || a.Theme == "jungle" || a.Theme == "ice" || a.Theme == "desert") && hsh % 23 == 5 &&
                             x + 2 < a.W && y >= 3 && a.Get(x + 1, y) == T.Ground && a.Get(x + 2, y) == T.Ground &&
                             a.Get(x, y - 2) == T.Empty && a.Get(x + 1, y - 3) == T.Empty && a.Get(x + 2, y - 2) == T.Empty && (a.GoalX < 0 || x < a.GoalX - 3))
                    {
                        string top = a.Theme == "desert" ? "palm.top" : "hill.top";
                        a.Decor.Add(new DecorDef { Img = "hill.l", X = x, Y = y - 1 });
                        a.Decor.Add(new DecorDef { Img = "hill.c", X = x + 1, Y = y - 1 });
                        a.Decor.Add(new DecorDef { Img = "hill.r", X = x + 2, Y = y - 1 });
                        a.Decor.Add(new DecorDef { Img = "hill.c", X = x + 1, Y = y - 2 });
                        a.Decor.Add(new DecorDef { Img = top, X = x + 1, Y = y - 3 });
                        x += 3;
                    }
                    break;
                }
            }
            if (outdoors && a.WaterRow != 0)
            {
                for (int x = 3; x + 2 < a.W; x += 7 + (int)(Hash(x, 7) % 9))
                {
                    int y = 1 + (int)(Hash(x, 3) % (uint)Math.Max(1, Math.Min(4, skyRows)));
                    if (a.Get(x, y) == T.Empty && a.Get(x + 1, y) == T.Empty && a.Get(x + 2, y) == T.Empty && (a.GoalX < 0 || x < a.GoalX - 3))
                    {
                        a.Decor.Add(new DecorDef { Img = "skycloud.l", X = x, Y = y });
                        a.Decor.Add(new DecorDef { Img = "skycloud.c", X = x + 1, Y = y });
                        a.Decor.Add(new DecorDef { Img = "skycloud.r", X = x + 2, Y = y });
                    }
                }
            }
            if (a.Theme == "fortress" || a.Theme == "castle")
            {
                for (int x = 4; x < a.W; x += 8)
                {
                    int floor = -1;
                    for (int y = a.H - 1; y > 1; y--) if (a.Get(x, y) != T.Empty && a.Get(x, y - 1) == T.Empty) { floor = y; break; }
                    if (floor > 3 && a.Get(x, floor - 1) == T.Empty && a.Get(x, floor - 2) == T.Empty && a.Get(x, floor - 3) == T.Empty)
                    {
                        a.Decor.Add(new DecorDef { Img = (Hash(x, 1) & 1) == 0 ? "window" : "torch.1", X = x, Y = floor - 3 });
                    }
                }
            }
        }
    }
}
