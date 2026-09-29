using System;
using System.Collections.Generic;
using SMB4.Engine;

namespace SMB4.Game
{
    public sealed class MapBro { public int X, Y; public string Item = "hammer"; }

    public sealed class MapNode
    {
        public char Code; public int X, Y;
        public string Kind = "";    // level, fortress, airship, castle, toad, spade, start, dot, lock
        public string Level;        // level id for playable nodes
        public string[] Items;      // toad house contents
        public char OpensWith;      // for locks: node code that opens it
    }

    /// <summary>World map (data/maps/wN.map). Grid 16x12; see data/maps/README.md.</summary>
    public sealed class MapDef
    {
        public int World;
        public string Name = "", Music = "map1", Pal = "map.w1";
        public int W = 16, H = 12;
        public char[,] Grid;
        public readonly List<MapNode> Nodes = new List<MapNode>();
        public readonly List<string> Errors = new List<string>();
        /// <summary>Wandering Hammer Bros: start tile and the item they guard.</summary>
        public readonly List<MapBro> Bros = new List<MapBro>();

        public MapNode NodeAt(int x, int y)
        {
            foreach (var n in Nodes) if (n.X == x && n.Y == y) return n;
            return null;
        }

        public MapNode Find(char code)
        {
            foreach (var n in Nodes) if (n.Code == code) return n;
            return null;
        }

        public char At(int x, int y) { return x < 0 || y < 0 || x >= W || y >= H ? ' ' : Grid[x, y]; }

        public static bool IsPath(char c) { return c == '-' || c == '|' || c == '=' || c == '!' || c == '+'; }
        public static bool IsNodeChar(char c) { return c == 'S' || (c >= '1' && c <= '9') || c == 'F' || c == 'G' || c == 'H' || c == 'A' || c == 'B' || c == 'P' || c == 'L' || c == 'M' || c == '+' || c == 'K'; }

        static readonly Dictionary<int, MapDef> cache = new Dictionary<int, MapDef>();
        public static void ClearCache() { cache.Clear(); }

        public static MapDef Load(int world)
        {
            MapDef m;
            if (cache.TryGetValue(world, out m)) return m;
            string text = Data.ReadText("maps/w" + world + ".map");
            if (text == null) return null;
            m = Parse(world, text);
            cache[world] = m;
            return m;
        }

        public static MapDef Parse(int world, string text)
        {
            var m = new MapDef { World = world, Pal = "map.w" + world, Music = "map" + world };
            var lines = text.Replace("\r", "").Split('\n');
            var rows = new List<string>();
            bool inGrid = false;
            var defs = new Dictionary<char, string>();
            var locks = new Dictionary<char, char>();
            foreach (var raw in lines)
            {
                if (inGrid)
                {
                    if (raw.Trim() == "end") { inGrid = false; continue; }
                    rows.Add(raw.TrimEnd());
                    continue;
                }
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line == "grid") { inGrid = true; continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) { m.Errors.Add("w" + world + ": bad line " + line); continue; }
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k == "name") m.Name = v;
                else if (k == "music") m.Music = v;
                else if (k == "pal") m.Pal = v;
                else if (k.StartsWith("node ") && k.Length == 6) defs[k[5]] = v;
                else if (k.StartsWith("lock ") && k.Length == 6) locks[k[5]] = v.Length > 0 ? v[0] : 'F';
                else if (k == "hbro")
                {
                    var p = v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    int bx, by;
                    if (p.Length >= 2 && int.TryParse(p[0], out bx) && int.TryParse(p[1], out by))
                        m.Bros.Add(new MapBro { X = bx, Y = by, Item = p.Length > 2 ? p[2] : "hammer" });
                    else m.Errors.Add("w" + world + ": bad hbro line");
                }
                else m.Errors.Add("w" + world + ": unknown key " + k);
            }
            m.H = Math.Max(12, rows.Count);
            m.W = 16;
            foreach (var r in rows) m.W = Math.Max(m.W, r.Length);
            m.Grid = new char[m.W, m.H];
            for (int y = 0; y < m.H; y++) for (int x = 0; x < m.W; x++) m.Grid[x, y] = y < rows.Count && x < rows[y].Length ? rows[y][x] : '.';
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    char c = m.Grid[x, y];
                    if (!IsNodeChar(c)) continue;
                    var n = new MapNode { Code = c, X = x, Y = y };
                    if (c == 'S') n.Kind = "start";
                    else if (c == '+') n.Kind = "dot";
                    else if (c == 'L') { n.Kind = "lock"; char o; n.OpensWith = locks.TryGetValue('L', out o) ? o : 'F'; }
                    else if (c == 'M') { n.Kind = "lock"; char o; n.OpensWith = locks.TryGetValue('M', out o) ? o : 'G'; }
                    else
                    {
                        string d;
                        if (!defs.TryGetValue(c, out d)) { m.Errors.Add("w" + world + ": node '" + c + "' has no definition"); d = ""; }
                        if (d.StartsWith("toad"))
                        {
                            n.Kind = "toad";
                            int colon = d.IndexOf(':');
                            n.Items = colon >= 0 ? d.Substring(colon + 1).Split(',') : new[] { "mushroom", "flower", "leaf" };
                        }
                        else if (d.StartsWith("spade")) n.Kind = "spade";
                        else
                        {
                            n.Level = d;
                            n.Kind = c == 'F' || c == 'G' ? "fortress" : c == 'A' ? "airship" : c == 'B' || c == 'K' ? "castle" : "level";
                        }
                    }
                    m.Nodes.Add(n);
                }
            if (m.Find('S') == null) m.Errors.Add("w" + world + ": no start node");
            return m;
        }
    }
}
