using System;
using System.Collections.Generic;
using System.IO;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Game;
using SMB4.Platform;

namespace SMB4.Tools
{
    /// <summary>Game-level tool commands: level renders, physics self-test, scripted screenshots, validation.</summary>
    public static partial class Extra
    {
        public static int Run(string[] args)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "level": return RenderLevel(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 0);
                case "selftest": return SelfTest();
                case "shot": return Shot(args);
                case "map": return RenderMap(int.Parse(args[1]), args[2]);
                case "fuzz": return Fuzz(args[1], args.Length > 2 ? int.Parse(args[2]) : 50, args.Length > 3 ? args[3] : null);
                case "icon": return MakeIcon(args[1]);
                case "reach": return Reach(args[1], args.Length > 2 ? args[2] : "Big", args.Length > 3 ? int.Parse(args[3]) : 400000);
                case "screen": return ScreenShot(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 120, args.Length > 4 ? args[4] : null);
                case "flow": return Flow(args[1], args[2]);
                case "progress": return Progress();
                case "snes-export": return SnesExport.Run(args.Length > 1 ? args[1] : "snes/build/gen");
                case "trace": return SnesTrace.Run(args);
                case "snes-audio": case "snes-song": return SnesAudioTool.Run(args);   // sound agent (SnesAudioEmu.cs)
                case "poses": return Poses(args.Length > 1 ? args[1] : "Big", args.Length > 2 ? args[2] : "poses.png");
            }
            Console.WriteLine("unknown command: " + args[0]);
            return 1;
        }

        static void Init()
        {
            Host.Settings = new Settings();
            Host.Input = new InputSystem();
            Sound.Init(false);
        }

        // ------------------------------------------------------------------ validation
        public static int Validate()
        {
            int errs = 0;
            foreach (var path in Data.List("levels/"))
            {
                if (!path.EndsWith(".lvl", StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileNameWithoutExtension(path);
                if (id.StartsWith("_")) continue;   // tool-only test levels
                var d = LevelLoader.Parse(id, Data.ReadText(path));
                foreach (var e in d.Errors) { Console.WriteLine("level: " + e); errs++; }
                for (int a = 0; a < d.Areas.Count; a++)
                {
                    var ar = d.Areas[a];
                    for (int i = 0; i < ar.Link.Length; i++)
                        if (ar.Link[i] >= 0 && d.FindLink(a, ar.Link[i]) == null)
                        {
                            // exit-only markers are fine; warn only for pipe mouths/doors that can be entered
                            T t = ar.Tiles[i];
                            if (t == T.PipeTL || t == T.PipeTR || t == T.PipeBL || t == T.PipeBR || t == T.DoorBot || t == T.HPipeMouthT)
                                if (!IsTarget(d, a, ar.Link[i])) { Console.WriteLine("level: " + id + " area " + a + " marker " + ar.Link[i] + " has no link"); errs++; }
                        }
                    if (a == 0 && ar.GoalX < 0 && d.Kind == "level") { Console.WriteLine("level: " + id + " has no goal (G)"); errs++; }
                }
                foreach (var l in d.Links)
                {
                    if (l.ToArea >= d.Areas.Count) { Console.WriteLine("level: " + id + " link to missing area " + l.ToArea); errs++; continue; }
                    var ta = d.Areas[l.ToArea]; bool found = false;
                    for (int i = 0; i < ta.Link.Length; i++) if (ta.Link[i] == l.ToId) found = true;
                    if (!found) { Console.WriteLine("level: " + id + " link target " + l.ToArea + ":" + l.ToId + " not found"); errs++; }
                }
                var st = d.Areas[d.StartArea];
                if (d.StartX < 0 || d.StartX >= st.W || d.StartY < 0 || d.StartY >= st.H) { Console.WriteLine("level: " + id + " start outside area"); errs++; }
            }
            for (int w = 1; w <= 9; w++)
            {
                string text = Data.ReadText("maps/w" + w + ".map");
                if (text == null) continue;
                var m = MapDef.Parse(w, text);
                foreach (var e in m.Errors) { Console.WriteLine("map: " + e); errs++; }
                foreach (var n in m.Nodes)
                    if (n.Level != null && Data.ReadText("levels/" + n.Level + ".lvl") == null) { Console.WriteLine("map: w" + w + " node " + n.Code + " -> missing level " + n.Level); errs++; }
                // connectivity: every node reachable from S along paths (locks treated as open)
                var st = m.Find('S');
                if (st != null)
                {
                    var seen = new HashSet<int>();
                    var q = new Queue<int[]>();
                    q.Enqueue(new[] { st.X, st.Y }); seen.Add(st.Y * 1000 + st.X);
                    int[] ds = { 1, 0, -1, 0, 0, 1, 0, -1 };
                    while (q.Count > 0)
                    {
                        var c = q.Dequeue();
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = c[0] + ds[k * 2], ny = c[1] + ds[k * 2 + 1];
                            char ch = m.At(nx, ny);
                            if (!MapDef.IsPath(ch) && m.NodeAt(nx, ny) == null) continue;
                            if (seen.Add(ny * 1000 + nx)) q.Enqueue(new[] { nx, ny });
                        }
                    }
                    foreach (var n in m.Nodes)
                        if (!seen.Contains(n.Y * 1000 + n.X)) { Console.WriteLine("map: w" + w + " node '" + n.Code + "' at " + n.X + "," + n.Y + " is not connected to START"); errs++; }
                    bool hasEnd = false;
                    foreach (var n in m.Nodes) if (n.Kind == "airship" || n.Kind == "castle") hasEnd = true;
                    if (!hasEnd) { Console.WriteLine("map: w" + w + " has no airship/castle end node"); errs++; }
                    foreach (var b in m.Bros)
                        if (!MapDef.IsPath(m.At(b.X, b.Y))) { Console.WriteLine("map: w" + w + " hbro at " + b.X + "," + b.Y + " is not on a path"); errs++; }
                }
            }
            return errs;
        }

        // ------------------------------------------------------------------ player pose gallery
        /// <summary>
        /// poses FORM OUT.png — draws the player (with tail/ears/helmet overlays) in every pose the engine can show,
        /// one labelled cell per pose, exactly as the game draws it. Last row repeats key poses facing left.
        /// </summary>
        static int Poses(string formName, string png)
        {
            Init();
            Form f; if (!Enum.TryParse(formName, true, out f)) f = Form.Big;
            var sb = new System.Text.StringBuilder("time = 0\nstart = 2 12\narea 0 theme=plains decor=0\n");
            for (int i = 0; i < 13; i++) sb.Append("....................\n");
            sb.Append("####################\n####################\nend\n");
            var def = LevelLoader.Parse("poses", sb.ToString());
            var s = new Session(new SaveData()); s.Form = f;
            var w = new World(def, s);
            for (int i = 0; i < 10; i++) w.Tick(new PadState());
            var dummy = new Shell(w, 0, 0, ShellKind.Green, false);

            var names = new List<string>(); var setups = new List<Action<Player>>();
            Action<string, Action<Player>> add = (n, a) => { names.Add(n); setups.Add(a); };
            add("STAND", p => { });
            for (int k = 0; k < 4; k++) { int kk = k; add("WALK" + k, p => { p.XVel = 0x10; p.AnimFrame = kk; }); }
            for (int k = 0; k < 4; k++) { int kk = k; add("PRUN" + k, p => { p.XVel = 0x38; p.AnimFrame = kk; }); }
            add("JUMP", p => { p.InAir = true; p.YVel = -0x30; });
            add("FALL", p => { p.InAir = true; p.YVel = 0x30; });
            add("PJUMP", p => { p.InAir = true; p.PJumpPose = true; p.YVel = -0x20; });
            add("WAG", p => { p.InAir = true; p.YVel = 0x10; p.WagCount = 8; });
            add("FLY", p => { p.InAir = true; p.YVel = -0x18; p.FlyTime = 40; p.WagCount = 8; });
            add("SKID", p => { p.XVel = 0x10; p.Skidding = true; });
            add("DUCK", p => { p.Ducking = true; });
            add("SLIDE", p => { p.Sliding = true; p.XVel = 0x20; });
            add("KICK", p => { p.KickPose = 6; });
            add("THROW", p => { p.ThrowPose = 6; });
            add("HOLD0", p => { p.Carrying = dummy; p.AnimFrame = 0; });
            add("HOLD2", p => { p.Carrying = dummy; p.AnimFrame = 2; p.XVel = 0x10; });
            add("HOLDJ", p => { p.Carrying = dummy; p.InAir = true; });
            for (int k = 0; k < 3; k++) { int a = new[] { 10, 5, 0 }[k]; add("SWIM" + (k + 1), p => { p.Swimming = true; p.InAir = true; p.SwimAnim = a; }); }
            add("CLMB1", p => { p.State = PState.Vine; p.Climbing = true; p.ClimbAnim = 0; });
            add("CLMB2", p => { p.State = PState.Vine; p.Climbing = true; p.ClimbAnim = 8; });
            add("FRONT", p => { p.State = PState.Door; });
            foreach (int ta in new[] { 12, 8, 4 }) { int t = ta; add("SPIN" + t, p => { p.TailAttack = t; }); }
            add("STATUE", p => { p.Statue = 20; });
            add("DEATH", p => { p.State = PState.Dying; });
            // facing left
            add("<STND", p => { p.Facing = -1; });
            add("<WLK1", p => { p.Facing = -1; p.XVel = -0x10; p.AnimFrame = 1; });
            add("<JUMP", p => { p.Facing = -1; p.InAir = true; p.YVel = -0x30; });
            add("<WAG", p => { p.Facing = -1; p.InAir = true; p.YVel = 0x10; p.WagCount = 8; });
            add("<DUCK", p => { p.Facing = -1; p.Ducking = true; });

            const int CW = 48, CH = 72, COLS = 8;
            int rows = (names.Count + COLS - 1) / COLS, Wd = COLS * CW, Ht = rows * CH;
            var buf = new int[Wd * Ht];
            int lutFade = 0;
            var ppu = new Ppu();
            for (int k = 0; k < names.Count; k++)
            {
                var p = w.P;
                p.State = PState.Normal; p.Form = f; p.InAir = false; p.XVel = 0; p.YVel = 0; p.AnimFrame = 2; p.AnimTick = 0;
                p.Skidding = false; p.Ducking = false; p.Sliding = false; p.KickPose = 0; p.ThrowPose = 0; p.Carrying = null;
                p.Swimming = false; p.Climbing = false; p.PJumpPose = false; p.FlyTime = 0; p.WagCount = 0; p.TailAttack = 0;
                p.Statue = 0; p.Facing = 1; p.HurtInv = 0; p.Star = 0; p.Transform = 0; p.Invisible = 0; p.BehindScenery = false;
                p.SwimAnim = 0; p.ClimbAnim = 0;
                setups[k](p);
                p.X = 16 << 4; p.Y = 24 << 4;               // the 32-tall player box starts at (16,24) in the cell
                ppu.ResetClip();
                ppu.Clear(0x21);
                p.Draw(ppu, 0, 0);
                ppu.FillRect(0, 60, CW, 12, 0x0F);
                ppu.Text(names[k], 1, 62, 0x30);
                int cx = (k % COLS) * CW, cy = (k / COLS) * CH;
                for (int y = 0; y < CH; y++)
                    for (int x = 0; x < CW; x++)
                        buf[(cy + y) * Wd + cx + x] = NesPalette.Argb(ppu.Px[y * 256 + x], lutFade);
            }
            Host.WritePng(buf, Wd, Ht, 3, png);
            Console.WriteLine("wrote " + png + " (" + names.Count + " poses, form " + f + ")");
            foreach (var e in Art.Errors) Console.WriteLine("art: " + e);
            return 0;
        }

        // ------------------------------------------------------------------ whole-game map progression
        /// <summary>
        /// Plays every world map abstractly: from START, repeatedly clears every reachable uncleared node (SMB3 rule:
        /// an uncleared panel can't be walked through; locks open when their fortress is cleared) until the
        /// airship/castle falls. Fails if a world can't be finished or a playable node has no level.
        /// </summary>
        static int Progress()
        {
            Init();
            int errs = 0;
            int[] ds = { 1, 0, -1, 0, 0, 1, 0, -1 };
            for (int w = 1; w <= 8; w++)
            {
                var m = MapDef.Load(w);
                if (m == null) { Console.WriteLine("w" + w + ": NO MAP"); errs++; continue; }
                var cleared = new HashSet<char>();
                var open = new HashSet<char>();
                var order = new List<string>();
                MapNode end = null;
                foreach (var n in m.Nodes) if (n.Kind == "airship" || n.Kind == "castle") end = n;
                if (end == null) { Console.WriteLine("w" + w + ": no airship/castle"); errs++; continue; }
                var st = m.Find('S');
                bool progressed = true;
                while (!cleared.Contains(end.Code) && progressed)
                {
                    progressed = false;
                    // flood fill from START; blocking (uncleared) panels are reachable but not passable
                    var seen = new bool[m.W, m.H];
                    var q = new Queue<int>();
                    seen[st.X, st.Y] = true; q.Enqueue(st.Y * 1000 + st.X);
                    var frontier = new List<MapNode>();
                    while (q.Count > 0)
                    {
                        int v = q.Dequeue(), x = v % 1000, y = v / 1000;
                        var here = m.NodeAt(x, y);
                        bool playable = here != null && (here.Kind == "level" || here.Kind == "fortress" || here.Kind == "airship" || here.Kind == "castle");
                        if (playable && !cleared.Contains(here.Code)) { frontier.Add(here); continue; }
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + ds[k * 2], ny = y + ds[k * 2 + 1];
                            if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H || seen[nx, ny]) continue;
                            var nn = m.NodeAt(nx, ny);
                            bool ok = MapDef.IsPath(m.At(nx, ny)) || nn != null;
                            if (nn != null && nn.Kind == "lock" && !open.Contains(nn.Code)) ok = false;
                            if (!ok) continue;
                            seen[nx, ny] = true; q.Enqueue(ny * 1000 + nx);
                        }
                    }
                    foreach (var n in frontier)
                    {
                        if (n.Level == null || LevelLoader.Load(n.Level) == null) { Console.WriteLine("w" + w + ": node " + n.Code + " has no playable level (" + n.Level + ")"); errs++; continue; }
                        cleared.Add(n.Code); order.Add(n.Code + "=" + n.Level); progressed = true;
                        if (n.Kind == "fortress") foreach (var l in m.Nodes) if (l.Kind == "lock" && l.OpensWith == n.Code) open.Add(l.Code);
                    }
                }
                if (!cleared.Contains(end.Code)) { Console.WriteLine("w" + w + ": CANNOT FINISH (cleared " + string.Join(" ", order) + ")"); errs++; }
                else
                {
                    int total = 0; foreach (var n in m.Nodes) if (n.Level != null) total++;
                    Console.WriteLine("w" + w + ": ok  " + string.Join(" ", order) + "  (" + cleared.Count + "/" + total + " playable nodes needed or reachable)");
                }
            }
            Console.WriteLine(errs == 0 ? "PROGRESS OK" : errs + " problem(s)");
            return errs == 0 ? 0 : 1;
        }

        static bool IsTarget(LevelDef d, int area, int id)
        {
            foreach (var l in d.Links) if (l.ToArea == area && l.ToId == id) return true;
            return false;
        }

        // ------------------------------------------------------------------ level overview render
        static int RenderLevel(string id, string png, int area)
        {
            Init();
            var def = LevelLoader.Load(id);
            if (def == null) { Console.WriteLine("no level " + id); return 1; }
            foreach (var e in def.Errors) Console.WriteLine("level: " + e);
            var save = new SaveData(); var s = new Session(save);
            var w = new World(def, s);
            if (area != w.AreaIndex) w.LoadArea(area);
            int W = w.LevelPxW, H = w.LevelPxH + 48;
            var buf = new int[W * H];
            var ppu = new Ppu();
            int lutFade = 0;
            // spawn everything for the picture
            w.DebugSpawnAll();
            for (int cx = 0; cx < W; cx += 256)
                for (int cy = 0; cy < w.LevelPxH; cy += 192)
                {
                    w.CamX = Math.Min(cx, Math.Max(0, W - 256));
                    w.CamY = Math.Min(cy, Math.Max(0, w.LevelPxH - 192));
                    ppu.Clear(0x0F);
                    w.Render(ppu);
                    for (int y = 0; y < 192; y++)
                        for (int x = 0; x < 256; x++)
                        {
                            int dx = w.CamX + x, dy = w.CamY + y;
                            if (dx < W && dy < w.LevelPxH) buf[dy * W + dx] = NesPalette.Argb(ppu.Px[y * 256 + x], lutFade);
                        }
                }
            // grid ticks every 16 columns for measuring
            for (int x = 0; x < W; x += 16) for (int y = w.LevelPxH; y < w.LevelPxH + 6; y++) buf[y * W + x] = unchecked((int)0xFFFFFFFF);
            for (int x = 0; x < W; x += 160) for (int y = w.LevelPxH; y < w.LevelPxH + 16; y++) buf[y * W + x] = unchecked((int)0xFFFF4040);
            Host.WritePng(buf, W, H, 1, png);
            Console.WriteLine("wrote " + png + " (" + W + "x" + (H - 48) + ", " + def.Areas.Count + " areas)");
            foreach (var e in Art.Errors) Console.WriteLine("art: " + e);
            return 0;
        }

        static int RenderMap(int world, string png)
        {
            Init();
            var save = new SaveData(); var g = new GameMain2();
            var m = MapDef.Load(world);
            if (m == null) { Console.WriteLine("no map"); return 1; }
            foreach (var e in m.Errors) Console.WriteLine("map: " + e);
            var ms = new MapScreen(world, false);
            var gm = new GameMain(); gm.Session = new Session(save);
            ms.G = gm; ms.Enter();
            var ppu = new Ppu(); ms.Render(ppu);
            int lutFade = 0;
            var buf = new int[256 * 240];
            for (int i = 0; i < buf.Length; i++) buf[i] = NesPalette.Argb(ppu.Px[i], lutFade);
            Host.WritePng(buf, 256, 240, 3, png);
            Console.WriteLine("wrote " + png);
            return 0;
        }
        sealed class GameMain2 { }

        // ------------------------------------------------------------------ scripted screenshots
        /// <summary>shot LEVEL OUT.png SCRIPT — script tokens: R30 = hold right 30 ticks, J = jump (hold A 20), B+R60 = run right, W30 = wait.</summary>
        static int Shot(string[] args)
        {
            Init();
            string id = args[1], png = args[2];
            string script = args.Length > 3 ? args[3] : "W60";
            var def = LevelLoader.Load(id);
            var save = new SaveData(); var s = new Session(save);
            if (args.Length > 4) { Form f; if (Enum.TryParse(args[4], true, out f)) s.Form = f; }
            var w = new World(def, s);
            var ppu = new Ppu();
            int lutFade = 0;
            int frameNo = 0;
            foreach (var tokRaw in script.Split(','))
            {
                string tok = tokRaw.Trim().ToUpperInvariant();
                if (tok.Length == 0) continue;
                int held = 0; int n = 1;
                if (tok.StartsWith("SNAP")) { Snap(w, ppu, lutFade, png.Replace(".png", "_" + (frameNo++) + ".png"), s); continue; }
                if (tok.StartsWith("@"))
                {
                    // teleport: @x:y (tile coords of the feet) [optional :area]
                    var p = tok.Substring(1).Split(':');
                    if (p.Length > 2 && int.Parse(p[2]) != w.AreaIndex) w.LoadArea(int.Parse(p[2]));
                    w.P.X = (int)(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture) * 16) << 4; w.P.Y = ((int.Parse(p[1]) + 1) * 16 - 32) << 4; w.P.InAir = true; w.P.YVel = 0;
                    w.CenterCamera(false);
                    continue;
                }
                if (tok == "INFO") { Console.WriteLine("  tick " + w.Frame + ": player " + w.P.Px + "," + w.P.Py + " state=" + w.P.State + " area=" + w.AreaIndex + " form=" + w.P.Form + " xvel=" + w.P.XVel + " yvel=" + w.P.YVel + " inAir=" + w.P.InAir + " result=" + w.Result + " cam=" + w.CamX + "," + w.CamY); foreach (var e in w.Ents) Console.WriteLine("    " + e.GetType().Name + " at " + e.Px + "," + e.Py + (e.Behind ? " behind" : "")); continue; }
                int i = 0;
                while (i < tok.Length && !char.IsDigit(tok[i]))
                {
                    char c = tok[i++];
                    if (c == 'R') held |= Btn.Right; else if (c == 'L') held |= Btn.Left; else if (c == 'U') held |= Btn.Up; else if (c == 'D') held |= Btn.Down;
                    else if (c == 'J' || c == 'A') held |= Btn.A; else if (c == 'B') held |= Btn.B; else if (c == 'S') held |= Btn.Start;
                }
                if (i < tok.Length) n = int.Parse(tok.Substring(i));
                int prev = 0;
                for (int k = 0; k < n; k++)
                {
                    var pad = new PadState { Held = held, Pressed = held & ~prev, Released = prev & ~held };
                    prev = held;
                    w.Tick(pad);
                }
            }
            Snap(w, ppu, lutFade, png, s);
            Console.WriteLine("player at " + w.P.Px + "," + w.P.Py + " form=" + w.P.Form + " state=" + w.P.State + " result=" + w.Result + " ents=" + w.Ents.Count);
            return 0;
        }

        static void Snap(World w, Ppu ppu, int lutFade, string png, Session s)
        {
            ppu.Clear(0x0F);
            w.Render(ppu);
            Hud.Draw(ppu, s, 1, w.P.Power, false, w.Time, w.Frame);
            var buf = new int[256 * 240];
            for (int i = 0; i < buf.Length; i++) buf[i] = NesPalette.Argb(ppu.Px[i], lutFade);
            Host.WritePng(buf, 256, 240, 3, png);
        }

        // ------------------------------------------------------------------ reachability (can the goal be reached at all?)
        /// <summary>
        /// Breadth-first search over player physics states (enemies ignored, blocks untouched) using 8-tick
        /// input macros. Reports whether the goal / boss door / end pipe region of area 0 is reachable.
        /// </summary>
        static int Reach(string id, string formName, int maxStates)
        {
            Init();
            Host.Settings.ModernFeel = false;   // prove it with SMB3-exact physics (no leniency)
            var def = LevelLoader.Load(id);
            if (def == null) { Console.WriteLine("no level " + id); return 1; }
            var s = new Session(new SaveData());
            Form f; if (!Enum.TryParse(formName, true, out f)) f = Form.Big;
            s.Form = f;
            var w = new World(def, s);
            w.ReadOnly = true; w.GodMode = true;
            w.Ents.Clear();
            var area = w.Area;
            // moving lifts and donut lifts are entities; approximate them as semisolid ledges at their spawn point
            foreach (var sp in area.Spawns)
            {
                int width = sp.Code == '_' || sp.Code == ':' ? 3 : sp.Code == '-' ? 1 : 0;
                for (int k = 0; k < width; k++) if (w.TileAt(sp.X + k, sp.Y) == T.Empty) w.SetTile(sp.X + k, sp.Y, T.Semi);
            }
            // target: the goal box, or else the far right end of area 0
            int gx = area.GoalX >= 0 ? area.GoalX * 16 : w.LevelPxW - 48;
            int[] acts = { Btn.Right, Btn.Right | Btn.B, Btn.Right | Btn.A, Btn.Right | Btn.B | Btn.A, Btn.Left | Btn.A, Btn.Left, Btn.A, 0, Btn.Left | Btn.B | Btn.A, Btn.Right | Btn.Down };
            // water areas: Up + jump is how a swimmer leaps out of the water (SMB3)
            if (area.WaterRow >= 0) acts = new List<int>(acts) { Btn.Right | Btn.Up | Btn.A, Btn.Up | Btn.A }.ToArray();
            // best-first: states ordered by progress toward the target (right edge, or top for vertical levels)
            var open = new SortedDictionary<int, Stack<Player>>();
            var depth = new Dictionary<Player, int>();
            var seen = new HashSet<long>();
            w.P.InAir = true;
            var p0 = w.P;
            for (int i = 0; i < 30; i++) SimTick(w, p0, 0, 0);
            bool isVert = w.IsVertical;
            Func<Player, int> prio = pl => isVert ? pl.Py * 4 + Math.Abs(pl.Px - (area.GoalX >= 0 ? area.GoalX * 16 : pl.Px)) : -pl.Px * 4 + pl.Py / 8;
            Action<Player> push = pl => { int k = prio(pl); Stack<Player> st; if (!open.TryGetValue(k, out st)) { st = new Stack<Player>(); open[k] = st; } st.Push(pl); };
            push(p0); depth[p0] = 0;
            int explored = 0, bestX = 0;
            int bestY = int.MaxValue;
            while (open.Count > 0 && explored < maxStates)
            {
                var first = open.Keys.GetEnumerator(); first.MoveNext();
                int k0 = first.Current;
                var stk = open[k0];
                var p = stk.Pop();
                if (stk.Count == 0) open.Remove(k0);
                int d = depth[p]; depth.Remove(p);
                explored++;
                foreach (int act in acts)
                {
                    var q = p.Clone();
                    int prev = 0;
                    bool dead = false;
                    for (int t = 0; t < 8; t++)
                    {
                        int held = act;
                        SimTick(w, q, held, prev);
                        prev = held;
                        if (q.State == PState.Dying || q.Py > w.LevelPxH + 16) { dead = true; break; }
                    }
                    if (dead) continue;
                    bestX = Math.Max(bestX, q.Px);
                    bestY = Math.Min(bestY, q.Py);
                    bool reached = isVert ? (area.GoalY >= 0 && q.Py <= area.GoalY * 16 + 16 && Math.Abs(q.Px - area.GoalX * 16) < 48) : q.Px + 12 >= gx;
                    if (reached)
                    {
                        Console.WriteLine(id + " (" + f + "): REACHABLE in " + ((d + 1) * 8) + " ticks of macro play (" + explored + " states explored)");
                        return 0;
                    }
                    long key = ((long)(q.Px >> 2) << 40) ^ ((long)((q.Py + 512) >> 2) << 26) ^ ((long)((q.XVel + 128) >> 3) << 18) ^ ((long)((q.YVel + 256) >> 4) << 9) ^ ((q.InAir ? 1L : 0L) << 8) ^ (long)(q.Power >> 5);
                    if (!seen.Add(key)) continue;
                    depth[q] = d + 1;
                    push(q);
                }
            }
            Console.WriteLine(id + " (" + f + "): NOT REACHED — furthest x=" + bestX + " (tile " + (bestX / 16) + ") of target " + (gx / 16) + (isVert ? ", highest y tile " + (bestY / 16) : "") + ", explored " + explored);
            return 1;
        }

        static void SimTick(World w, Player p, int held, int prev)
        {
            w.P = p;
            w.Frame++;
            w.Wiggly = Phys.NextWiggly(w.Wiggly);
            var pad = new PadState { Held = held, Pressed = held & ~prev, Released = prev & ~held };
            p.Control(pad);
            p.PowerUpdate();
            p.DetectSolids();
            if (p.Px < 0) p.X = 0;
            if (p.Px > w.LevelPxW - 16) p.X = (w.LevelPxW - 16) << 4;
            p.Timers();
        }

        // ------------------------------------------------------------------ whole-game flow test
        /// <summary>
        /// flow OUTDIR SCRIPT — drives GameMain from the title screen with scripted input, saving PNGs at SNAP tokens.
        /// Tokens: A/B/S/U/D/L/R combos with a tick count (e.g. S1, W60, R40, BRA20), SNAP.
        /// Saves go to a temporary folder, never the user's.
        /// </summary>
        static int Flow(string outDir, string script)
        {
            Environment.SetEnvironmentVariable("SMB4_SAVE_DIR", Path.Combine(Path.GetTempPath(), "smb4_flowtest"));
            try { Directory.Delete(Path.Combine(Path.GetTempPath(), "smb4_flowtest"), true); } catch { }
            Init();
            Directory.CreateDirectory(outDir);
            var gm = new GameMain();
            var ppu = new Ppu();
            int lutFade = 0;
            int snap = 0, prev = 0;
            foreach (var raw in script.Split(','))
            {
                string tok = raw.Trim().ToUpperInvariant();
                if (tok.Length == 0) continue;
                if (tok == "SNAP")
                {
                    ppu.Clear(0x0F); gm.Render(ppu);
                    lutFade = ppu.Fade;
                    var buf = new int[256 * 240];
                    for (int i = 0; i < buf.Length; i++) buf[i] = NesPalette.Argb(ppu.Px[i], lutFade);
                    string f = Path.Combine(outDir, "flow_" + (snap++).ToString("00") + ".png");
                    Host.WritePng(buf, 256, 240, 2, f);
                    Console.WriteLine("snap " + f);
                    continue;
                }
                int held = 0, i2 = 0;
                while (i2 < tok.Length && !char.IsDigit(tok[i2]))
                {
                    char c = tok[i2++];
                    if (c == 'R') held |= Btn.Right; else if (c == 'L') held |= Btn.Left; else if (c == 'U') held |= Btn.Up; else if (c == 'D') held |= Btn.Down;
                    else if (c == 'A' || c == 'J') held |= Btn.A; else if (c == 'B') held |= Btn.B; else if (c == 'S') held |= Btn.Start; else if (c == 'E') held |= Btn.Select;
                }
                int n = i2 < tok.Length ? int.Parse(tok.Substring(i2)) : 1;
                for (int k = 0; k < n; k++)
                {
                    var pad = new PadState { Held = held, Pressed = held & ~prev, Released = prev & ~held };
                    prev = held;
                    gm.Tick(pad, new PadState());
                    if (gm.WantsQuit) { Console.WriteLine("game requested quit"); return 0; }
                }
            }
            Console.WriteLine("flow finished");
            return 0;
        }

        // ------------------------------------------------------------------ UI screen renders
        static int ScreenShot(string name, string png, int ticks, string formName)
        {
            Init();
            var gm = new GameMain();
            gm.Session = new Session(new SaveData { Exists = true });
            Form sf; if (formName != null && Enum.TryParse(formName, true, out sf)) gm.Session.Form = sf;
            gm.Session.Cur.Items.AddRange(new[] { Item.Mushroom, Item.Flower, Item.Leaf, Item.Star, Item.PWing });
            Screen s;
            bool entered = false;
            switch (name)
            {
                case "title": s = new TitleScreen(); break;
                case "files": s = new FileSelectScreen(false); break;
                case "help": s = new HelpScreen(null); break;
                case "options": s = new OptionsScreen(null); break;
                case "video": s = new OptionsScreen(null, 1); break;
                case "controls": s = new OptionsScreen(null, 3); break;
                case "keys": s = new OptionsScreen(null, 5); break;
                case "pad": s = new OptionsScreen(null, 6); break;
                case "test": s = new OptionsScreen(null, 8); break;
                case "gameover": s = new GameOverScreen(null); break;
                case "worldclear": s = new WorldClearScreen(1); break;
                case "ending": s = new EndingScreen(); break;
                case "help2": { var h = new HelpScreen(null); s = h; h.G = gm; h.Enter(); entered = true; var a = new PadState(); a.Pressed |= Btn.Right; h.Tick(a, a, a); h.Tick(a, a, a); break; }
                case "intro": s = new LevelIntroScreen(null, "1-1", "WORLD 1-1"); break;
                case "story": s = new StoryScreen(null); break;
                case "mapinv":
                {
                    // world 1 map with the item inventory open (B)
                    var m = new MapScreen(1, false); s = m; m.G = gm; m.Enter(); entered = true;
                    var bp = new PadState { Held = Btn.B, Pressed = Btn.B };
                    m.Tick(bp, new PadState(), bp);
                    break;
                }
                case "toad": s = new ToadHouseScreen(null, new MapNode { Kind = "toad", X = 5, Y = 5, Code = 'T' }); break;
                case "spade": s = new SpadeScreen(null, new MapNode { Kind = "spade", X = 5, Y = 5, Code = 'S' }); break;
                case "nspade":
                {
                    // flip the first two cards so both faces show in the picture
                    var ns = new NSpadeScreen(null, 0, 0); s = ns; ns.G = gm; ns.Enter(); entered = true;
                    var none0 = new PadState();
                    Func<int, PadState> press = b => new PadState { Held = b, Pressed = b };
                    ns.Tick(press(Btn.A), none0, press(Btn.A)); ns.Tick(none0, none0, none0);
                    ns.Tick(press(Btn.Right), none0, press(Btn.Right)); ns.Tick(none0, none0, none0);
                    ns.Tick(press(Btn.A), none0, press(Btn.A));
                    ticks = 0;
                    break;
                }
                default:
                    if (name.StartsWith("map")) { s = new MapScreen(int.Parse(name.Substring(3)), true); break; }
                    if (name.StartsWith("helppage"))
                    {
                        // helppageN: the how-to-play screen turned to page N (0-3)
                        var h = new HelpScreen(null); s = h; h.G = gm; h.Enter(); entered = true;
                        int n = int.Parse(name.Substring(8));
                        for (int k = 0; k < n; k++) { var a = new PadState { Held = Btn.Right, Pressed = Btn.Right }; h.Tick(a, new PadState(), a); h.Tick(new PadState(), new PadState(), new PadState()); }
                        break;
                    }
                    Console.WriteLine("unknown screen"); return 1;
            }
            s.G = gm;
            if (!entered) s.Enter();
            var none = new PadState();
            for (int i = 0; i < ticks; i++) s.Tick(none, none, none);
            var ppu = new Ppu();
            ppu.Clear(0x0F);
            s.Render(ppu);
            int lutFade = 0;
            var buf = new int[256 * 240];
            for (int i = 0; i < buf.Length; i++) buf[i] = NesPalette.Argb(ppu.Px[i], lutFade);
            Host.WritePng(buf, 256, 240, 3, png);
            Console.WriteLine("wrote " + png);
            return 0;
        }

        // ------------------------------------------------------------------ app icon
        static int MakeIcon(string path)
        {
            var img = Art.Get("ms.stand");
            var pal = Art.Pal("mario");
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            var pngs = new List<byte[]>();
            foreach (int size in sizes)
            {
                int scale = Math.Max(1, size / 16);
                using (var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    int ox = (size - 16 * scale) / 2, oy = (size - 16 * scale) / 2;
                    for (int y = 0; y < img.H; y++)
                        for (int x = 0; x < img.W; x++)
                        {
                            int v = img.P[y * img.W + x];
                            if (v == 0) continue;
                            var c = System.Drawing.Color.FromArgb(unchecked((int)0xFF000000) | NesPalette.RgbOf(pal[v]));
                            for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++) bmp.SetPixel(ox + x * scale + sx, oy + y * scale + sy, c);
                        }
                    using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); pngs.Add(ms.ToArray()); }
                }
            }
            using (var f = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(f))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
            Console.WriteLine("wrote " + path);
            return 0;
        }

        // ------------------------------------------------------------------ fuzzing
        /// <summary>Plays a level with biased random input (run right, jump often) to find crashes and soft-locks.</summary>
        static int Fuzz(string id, int episodes, string formName)
        {
            Init();
            var def = LevelLoader.Load(id);
            if (def == null) { Console.WriteLine("no level " + id); return 1; }
            int crashes = 0, clears = 0, deaths = 0, timeouts = 0, maxX = 0;
            var rng = new Random(12345);
            for (int ep = 0; ep < episodes; ep++)
            {
                var s = new Session(new SaveData());
                Form f = (Form)(ep % 7);          // every form: Small, Big, Fire, Raccoon, Tanooki, Frog, Hammer
                if (formName != null) Enum.TryParse(formName, true, out f);
                s.Form = f;
                World w = null;
                int prev = 0, hold = 0, ticks = 0;
                var fppu = new Ppu();
                try
                {
                    w = new World(def, s);
                    for (ticks = 0; ticks < 20000 && w.Result == LevelResult.None; ticks++)
                    {
                        if (ticks % 5 == 0) { fppu.Clear(0x0F); w.Render(fppu); }   // exercise every draw path too
                        if (hold <= 0)
                        {
                            int r = rng.Next(100);
                            int held = Btn.Right;
                            if (r < 55) held |= Btn.B;
                            if (r % 3 == 0) held |= Btn.A;
                            if (r > 92) held = Btn.Left | Btn.B;
                            if (r == 50) held = Btn.Down;
                            if (r == 51) held = Btn.Up;
                            if (r == 52) held = Btn.Down | Btn.Right;
                            prev = hold == 0 ? prev : prev;
                            hold = 4 + rng.Next(30);
                            curHeld = held;
                        }
                        hold--;
                        var pad = new PadState { Held = curHeld, Pressed = curHeld & ~prev, Released = prev & ~curHeld };
                        prev = curHeld;
                        w.Tick(pad);
                        maxX = Math.Max(maxX, w.P.Px);
                    }
                    if (w.Result == LevelResult.Cleared || w.Result == LevelResult.FortressCleared || w.Result == LevelResult.WorldCleared || w.Result == LevelResult.GameCleared) clears++;
                    else if (w.Result == LevelResult.Died) deaths++;
                    else timeouts++;
                }
                catch (Exception ex)
                {
                    crashes++;
                    Console.WriteLine("CRASH ep " + ep + " tick " + ticks + " form " + f + ": " + ex);
                    if (crashes > 3) break;
                }
            }
            Console.WriteLine(id + ": episodes=" + episodes + " clears=" + clears + " deaths=" + deaths + " timeouts=" + timeouts + " crashes=" + crashes + " furthestX=" + maxX);
            return crashes == 0 ? 0 : 1;
        }
        static int curHeld;

        // ------------------------------------------------------------------ physics self-test (docs/01 §15.2)
        const string TestLevel = @"
time = 0
start = 4 12
area 0 theme=plains decor=0
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
................................................................................................................................................................
################################################################################################################################################################
################################################################################################################################################################
end
";

        static int fails;
        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            if (!ok) fails++;
        }

        static World NewTestWorld(Form f, bool modern)
        {
            Host.Settings.ModernFeel = modern;
            var def = LevelLoader.Parse("test", TestLevel);
            var s = new Session(new SaveData());
            s.Form = f;
            var w = new World(def, s);
            // settle on the ground
            for (int i = 0; i < 20; i++) w.Tick(new PadState());
            return w;
        }

        static PadState Pad(int held, int prev) { return new PadState { Held = held, Pressed = held & ~prev, Released = prev & ~held }; }

        static int SelfTest()
        {
            Init();
            fails = 0;
            // 1. standing full jump: apex 1131 subpixels at tick 30, lands tick 54
            {
                var w = NewTestWorld(Form.Big, false);
                int y0 = w.P.Y, minY = y0, apexT = 0, land = -1, prev = 0;
                for (int t = 0; t < 120; t++)
                {
                    int held = Btn.A;
                    w.Tick(Pad(held, prev)); prev = held;
                    if (w.P.Y < minY) { minY = w.P.Y; apexT = t; }
                    if (t > 2 && !w.P.InAir && land < 0) land = t;
                }
                Check("standing full jump apex", y0 - minY == 1131, "apex=" + (y0 - minY) + " at tick " + apexT + ", landed tick " + land);
                Check("standing full jump airtime", land == 54, "landed " + land);
            }
            // 2. tap jump: apex 330
            {
                var w = NewTestWorld(Form.Big, false);
                int y0 = w.P.Y, minY = y0, prev = 0;
                for (int t = 0; t < 60; t++)
                {
                    int held = t == 0 ? Btn.A : 0;
                    w.Tick(Pad(held, prev)); prev = held;
                    if (w.P.Y < minY) minY = w.P.Y;
                }
                Check("tap jump apex", y0 - minY == 330, "apex=" + (y0 - minY));
            }
            // 3. walk acceleration to $18
            {
                var w = NewTestWorld(Form.Big, false);
                int t = 0, prev = 0;
                while (w.P.XVel < 0x18 && t < 100) { w.Tick(Pad(Btn.Right, prev)); prev = Btn.Right; t++; }
                Check("rest -> walk speed", t >= 26 && t <= 29, t + " ticks");
            }
            // 4. run acceleration to $28 and P-meter
            {
                var w = NewTestWorld(Form.Big, false);
                int t = 0, prev = 0, held = Btn.Right | Btn.B;
                while (w.P.XVel < 0x28 && t < 200) { w.Tick(Pad(held, prev)); prev = held; t++; }
                Check("rest -> run speed", t >= 44 && t <= 47, t + " ticks");
                int pt = 0;
                while (w.P.Power != 0x7F && pt < 200) { w.Tick(Pad(held, prev)); pt++; }
                Check("P-meter fill after run speed", pt >= 48 && pt <= 73, pt + " ticks");
                int st = 0;
                while (w.P.XVel < 0x38 && st < 100) { w.Tick(Pad(held, prev)); st++; }
                Check("run -> P speed", st >= 17 && st <= 20 && w.P.XVel == 0x38, st + " ticks, xvel=" + w.P.XVel);
            }
            // 5. skid from $28 in 20 ticks
            {
                var w = NewTestWorld(Form.Big, false);
                int prev = 0, held = Btn.Right | Btn.B;
                for (int t = 0; t < 60; t++) { w.Tick(Pad(held, prev)); prev = held; }
                int t2 = 0; held = Btn.Left | Btn.B;
                while (w.P.XVel > 0 && t2 < 60) { w.Tick(Pad(held, prev)); prev = held; t2++; }
                Check("skid from run speed", t2 == 20, t2 + " ticks");
            }
            // 6. running jump apex (85.1 px = 1361 subpx)
            {
                var w = NewTestWorld(Form.Big, false);
                int prev = 0, held = Btn.Right | Btn.B;
                for (int t = 0; t < 60; t++) { w.Tick(Pad(held, prev)); prev = held; }
                int y0 = w.P.Y, minY = y0;
                held |= Btn.A;
                for (int t = 0; t < 80; t++) { w.Tick(Pad(held, prev)); prev = held; if (w.P.Y < minY) minY = w.P.Y; }
                Check("running jump apex", y0 - minY == 1361 || y0 - minY == 1362, "apex=" + (y0 - minY));
            }
            // 7. Modern coyote time: walk off a ledge and jump 4 ticks late
            {
                string lv = TestLevel.Replace("################################################################################################################################################################\r\n################################################################################################################################################################",
                                              "##########......................................................................................................................................................\r\n##########......................................................................................................................................................");
                lv = lv.Replace("\n################################################################################################################################################################\n################################################################################################################################################################",
                                "\n##########......................................................................................................................................................\n##########......................................................................................................................................................");
                Host.Settings.ModernFeel = true;
                var def = LevelLoader.Parse("coyote", lv);
                var s = new Session(new SaveData()); s.Form = Form.Big;
                var w = new World(def, s);
                for (int i = 0; i < 20; i++) w.Tick(new PadState());
                int prev = 0; int offTick = -1; bool jumped = false;
                for (int t = 0; t < 200 && !jumped; t++)
                {
                    int held = Btn.Right;
                    if (offTick >= 0 && t == offTick + 4) held |= Btn.A;
                    w.Tick(Pad(held, prev)); prev = held;
                    if (offTick < 0 && w.P.InAir) offTick = t;
                    if (offTick >= 0 && w.P.YVel < -0x30) jumped = true;
                }
                Check("modern coyote jump (4 ticks late)", jumped, "walked off at tick " + offTick);
            }
            // 8. slopes: walk up and over a hill without leaving the ground; slide down gains +2/tick
            {
                string hill = "time = 0\nstart = 3 12\narea 0 theme=plains decor=0\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "..........................................\n" +
                    "............/##\\..........................\n" +
                    ".........../####\\.........................\n" +
                    "........../######\\........................\n" +
                    "##########################################\n" +
                    "##########################################\nend\n";
                Host.Settings.ModernFeel = false;
                var def = LevelLoader.Parse("hill", hill);
                var s = new Session(new SaveData()); s.Form = Form.Big;
                var w = new World(def, s);
                for (int i = 0; i < 20; i++) w.Tick(new PadState());
                int prev = 0, maxAir = 0, air = 0, minFeet = int.MaxValue;
                for (int t = 0; t < 260; t++)
                {
                    w.Tick(Pad(Btn.Right, prev)); prev = Btn.Right;
                    if (w.P.InAir) { air++; maxAir = Math.Max(maxAir, air); } else air = 0;
                    minFeet = Math.Min(minFeet, w.P.Py + 32);
                }
                Check("slope walk stays grounded", maxAir <= 2 && minFeet == 160, "max consecutive air ticks " + maxAir + ", highest feet y " + minFeet + ", ended x " + w.P.Px);
                // slide: stand near the top of the down-slope and press Down
                w.P.X = (15 * 16 + 4) << 4; w.P.Y = (160 + 4 - 32) << 4; w.P.XVel = 0; w.P.InAir = true;
                for (int i = 0; i < 6; i++) w.Tick(new PadState());
                prev = 0; int v1 = 0, v2 = 0; bool slid = false;
                for (int t = 0; t < 12; t++)
                {
                    w.Tick(Pad(Btn.Down, prev)); prev = Btn.Down;
                    if (w.P.Sliding) slid = true;
                    if (t == 4) v1 = w.P.XVel; if (t == 8) v2 = w.P.XVel;
                }
                Check("slope slide accelerates +2/tick", slid && v2 - v1 == 8, "sliding=" + slid + " xvel " + v1 + " -> " + v2);
            }
            // 9. Boom Boom: fire Mario defeats him, the orb drops, touching it clears the fortress
            {
                string arena = "kind = fortress\ntime = 300\nstart = 2 10\narea 0 theme=fortress scroll=lock decor=0\n" +
                    "%%%%%%%%%%%%%%%%\n%..............%\n%..............%\n%..............%\n%..............%\n%..............%\n" +
                    "%..............%\n%..............%\n%..............%\n%..............%\n%..........Z...%\n%%%%%%%%%%%%%%%%\nend\n";
                Host.Settings.ModernFeel = true;
                var def = LevelLoader.Parse("bossarena", arena);
                var s = new Session(new SaveData()); s.Form = Form.Fire;
                var w = new World(def, s);
                w.GodMode = true;
                int prev = 0; bool orb = false; int t;
                for (t = 0; t < 3000 && w.Result == LevelResult.None; t++)
                {
                    Entity boss = null, o = null;
                    foreach (var e in w.Ents) { if (e is BoomBoom && !e.Dying) boss = e; if (e is Orb) o = e; }
                    int held = 0;
                    if (o != null)
                    {
                        orb = true;
                        held = o.Cx < w.P.CenterX ? Btn.Left : Btn.Right;
                    }
                    else if (boss != null)
                    {
                        int dir = boss.Cx < w.P.CenterX ? Btn.Left : Btn.Right;
                        held = (t % 12 < 2) ? (dir | Btn.B) : (t % 40 < 20 ? dir : 0);
                        if (Math.Abs(boss.Cx - w.P.CenterX) < 40) held = (dir == Btn.Left ? Btn.Right : Btn.Left) | ((t % 12 < 2) ? Btn.B : 0);
                    }
                    w.Tick(Pad(held, prev)); prev = held;
                }
                Check("Boom Boom defeated and fortress cleared", orb && w.Result == LevelResult.FortressCleared, "orb=" + orb + " result=" + w.Result + " after " + t + " ticks");
            }
            // 10. Koopaling (fire) -> wand -> world clear; 11. Bowser falls through the brick floor -> game clear
            foreach (var bossCode in new[] { 'K', 'Y' })
            {
                string floor = bossCode == 'Y' ? "%%%BBBBBBBBBB%%%\n...BBBBBBBBBB...\n" : "%%%%%%%%%%%%%%%%\n";
                string arena = "kind = " + (bossCode == 'Y' ? "castle" : "airship") + "\ntime = 400\nstart = 1 9\narea 0 theme=" + (bossCode == 'Y' ? "castle" : "airship") + " scroll=lock decor=0\n" +
                    "%..............%\n%..............%\n%..............%\n%..............%\n%..............%\n%..............%\n" +
                    "%..............%\n%..............%\n%..............%\n%........." + bossCode + "....%\n" + floor + "end\n";
                var def = LevelLoader.Parse("1-a", arena);
                var s = new Session(new SaveData()); s.Form = Form.Fire;
                var w = new World(def, s);
                w.GodMode = true;
                int prev = 0; int t;
                for (t = 0; t < 6000 && w.Result == LevelResult.None; t++)
                {
                    Entity boss = null, o = null;
                    foreach (var e in w.Ents) { if (e is Boss && !e.Dying) boss = e; if (e is Orb) o = e; }
                    int held = 0;
                    if (o != null) held = o.Cx < w.P.CenterX ? Btn.Left : Btn.Right;
                    else if (boss != null)
                    {
                        int dir = boss.Cx < w.P.CenterX ? Btn.Left : Btn.Right;
                        held = (t % 14 < 2) ? (dir | Btn.B) : 0;
                        if (bossCode == 'Y' && w.P.Px > 40) held |= Btn.Left;   // stay on the safe ledge; Bowser comes to us
                    }
                    w.Tick(Pad(held, prev)); prev = held;
                }
                var want = bossCode == 'Y' ? LevelResult.GameCleared : LevelResult.WorldCleared;
                Check(bossCode == 'Y' ? "Bowser defeated -> game clear" : "Koopaling defeated -> world clear", w.Result == want, "result=" + w.Result + " after " + t + " ticks");
            }
            // 12. big bolted blocks: jump up through the body, land only on the top row
            {
                string lv = "time = 0\nstart = 8 12\narea 0 theme=plains decor=0\n" +
                    "....................\n....................\n....................\n....................\n" +
                    "....................\n....................\n....................\n....................\n" +
                    "......AAAA..........\n......AAAA..........\n......AAAA..........\n" +
                    "....................\n....................\n" +
                    "####################\n####################\nend\n";
                Host.Settings.ModernFeel = false;
                var def = LevelLoader.Parse("blocks", lv);
                var s = new Session(new SaveData()); s.Form = Form.Big;
                var w = new World(def, s);
                for (int i = 0; i < 20; i++) w.Tick(new PadState());
                int prev = 0, maxFeetUp = int.MaxValue;
                for (int t = 0; t < 120; t++)
                {
                    int held = t < 30 ? Btn.A : 0;
                    w.Tick(Pad(held, prev)); prev = held;
                    maxFeetUp = Math.Min(maxFeetUp, w.P.Py + 32);
                }
                bool fellThrough = !w.P.InAir && w.P.Py + 32 == 208;
                // dropped onto the top from above: stands at y=128
                w.P.X = (7 * 16) << 4; w.P.Y = (124 - 32) << 4; w.P.YVel = 0; w.P.InAir = true;
                for (int t = 0; t < 30; t++) w.Tick(new PadState());
                bool standsOnTop = !w.P.InAir && w.P.Py + 32 == 128;
                Check("big block: jump through its body, stand on its top", fellThrough && standsOnTop,
                    "apex feet " + maxFeetUp + ", after jump feet " + (w.P.Py + 32) + " fellThrough=" + fellThrough + " standsOnTop=" + standsOnTop);
            }
            // 14. Up + A at the water's surface leaps clear of the water (the surface clamp must not cancel it)
            {
                string lv = "time = 0\nstart = 5 12\narea 0 theme=sea water=10 decor=0\n" +
                    "....................\n....................\n....................\n....................\n" +
                    "....................\n....................\n....................\n....................\n" +
                    "....................\n....................\n....................\n....................\n....................\n" +
                    "####################\n####################\nend\n";
                Host.Settings.ModernFeel = false;
                var def = LevelLoader.Parse("pool", lv);
                var s = new Session(new SaveData()); s.Form = Form.Big;
                var w = new World(def, s);
                for (int i = 0; i < 10; i++) w.Tick(new PadState());
                // put Mario at the surface, still rising (bobbing)
                w.P.Y = (146) << 4; w.P.YVel = -8; w.P.InAir = true;
                int prev = 0, minFeet = int.MaxValue; bool leftWater = false;
                for (int t = 0; t < 60; t++)
                {
                    int held = t < 20 ? Btn.Up | Btn.A : 0;
                    if (t == 1) held = Btn.Up;                        // re-press A on tick 2
                    w.Tick(Pad(held, prev)); prev = held;
                    minFeet = Math.Min(minFeet, w.P.Py + 32);
                    if (!w.P.Swimming) leftWater = true;
                }
                Check("water: Up+A leaps out of the water", leftWater && minFeet <= 160 - 12, "left water=" + leftWater + ", highest feet y " + minFeet + " (surface 160)");
            }
            // 15. P-switch: bricks <-> coins and munchers -> coins, all swapped back when the timer runs out
            {
                string lv = "time = 0\nstart = 2 12\narea 0 theme=plains decor=0\n" +
                    "....................\n....................\n....................\n....................\n" +
                    "....................\n....................\n....................\n....................\n" +
                    "....................\n..........BBB.......\n..........ooo.......\n....................\n" +
                    "..............MMMM..\n####################\n####################\nend\n";
                var def = LevelLoader.Parse("pswitch", lv);
                var s = new Session(new SaveData()); s.Form = Form.Big;
                var w = new World(def, s);
                for (int i = 0; i < 5; i++) w.Tick(new PadState());
                w.StartPSwitch();
                bool swapped = w.TileAt(10, 9) == T.Coin && w.TileAt(10, 10) == T.Brick && w.TileAt(14, 12) == T.Coin;
                for (int i = 0; i < 700; i++) w.Tick(new PadState());
                bool restored = w.TileAt(10, 9) == T.Brick && w.TileAt(10, 10) == T.Coin && w.TileAt(14, 12) == T.Muncher;
                Check("P-switch swaps blocks and swaps them back", swapped && restored, "swapped=" + swapped + " restored=" + restored);
            }
            // 13. the N-Spade panel appears on the map once the score passes 80,000 (saves go to a temp folder)
            {
                string saveDir = Path.Combine(Path.GetTempPath(), "smb4_selftest");
                Environment.SetEnvironmentVariable("SMB4_SAVE_DIR", saveDir);
                Directory.CreateDirectory(saveDir);
                var gm = new GameMain();
                gm.Session = new Session(new SaveData { Slot = 2 });
                gm.Session.Cur.Score = 85000;
                var ms = new MapScreen(1, false); ms.G = gm; ms.Enter();
                ms.LevelDone("1-1", LevelResult.Cleared);
                bool flag = false;
                foreach (var f in gm.Session.Save.Flags) if (f.StartsWith("nspade:1:")) flag = true;
                Check("N-Spade panel appears at 80,000 points", flag && gm.Session.Cur.NSpadeAt == 160000,
                    "flag=" + flag + " next=" + gm.Session.Cur.NSpadeAt);
                try { Directory.Delete(saveDir, true); } catch { }
            }
            Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            return fails == 0 ? 0 : 1;
        }
    }
}
