using System;
using System.Collections.Generic;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    public sealed class MapScreen : Screen
    {
        readonly int world;
        readonly bool showIntro;
        MapDef map;
        int px, py;                 // tile position of the player
        int moveDx, moveDy, movePix;// current walk
        int camF = -1;              // smoothed camera x (1/16 px), presentation only
        int walkT;                  // frames spent walking (walk-cycle phase)
        bool faceLeft;              // last horizontal travel direction (sprite mirror)
        int arriveDx, arriveDy;     // direction we entered the current node from
        int intro, t;
        bool invOpen; int invSel;
        string message = ""; int messageT;
        bool cloudActive;
        readonly MenuNav nav = new MenuNav();
        SaveData Save { get { return G.Session.Save; } }
        // wandering Hammer Bros
        int[] bx, by; bool[] broAlive;
        int pendingBro = -1;
        readonly Random rng = new Random();

        void LoadBros()
        {
            int n = map.Bros.Count;
            bx = new int[n]; by = new int[n]; broAlive = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bx[i] = map.Bros[i].X; by[i] = map.Bros[i].Y;
                broAlive[i] = !Save.Flags.Contains("hbdead:" + world + ":" + i);
                string pre = "hb:" + world + ":" + i + ":";
                foreach (var f in Save.Flags)
                    if (f.StartsWith(pre)) { var p = f.Substring(pre.Length).Split(':'); int x, y; if (p.Length == 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)) { bx[i] = x; by[i] = y; } }
            }
        }

        void SaveBro(int i)
        {
            string pre = "hb:" + world + ":" + i + ":";
            Save.Flags.RemoveWhere(f => f.StartsWith(pre));
            Save.Flags.Add(pre + bx[i] + ":" + by[i]);
        }

        bool BroWalkable(int x, int y)
        {
            char c = map.At(x, y);
            if (!MapDef.IsPath(c)) return false;
            if (x == px && y == py) return true;
            return true;
        }

        /// <summary>After each level, every living Hammer Bro takes one or two steps along the paths (SMB3).</summary>
        void MoveBros()
        {
            if (bx == null) return;
            int[] ds = { 1, 0, -1, 0, 0, 1, 0, -1 };
            for (int i = 0; i < bx.Length; i++)
            {
                if (!broAlive[i]) continue;
                int steps = 1 + rng.Next(2);
                for (int s = 0; s < steps; s++)
                {
                    int start = rng.Next(4);
                    for (int k = 0; k < 4; k++)
                    {
                        int d = (start + k) % 4;
                        int nx = bx[i] + ds[d * 2], ny = by[i] + ds[d * 2 + 1];
                        if (BroWalkable(nx, ny)) { bx[i] = nx; by[i] = ny; break; }
                    }
                }
                SaveBro(i);
            }
        }

        int BroAt(int x, int y)
        {
            if (bx == null) return -1;
            for (int i = 0; i < bx.Length; i++) if (broAlive[i] && bx[i] == x && by[i] == y) return i;
            return -1;
        }

        void StartBattle(int i)
        {
            pendingBro = i;
            Sound.Sfx(SfxId.MapEnter);
            Save.MapX = px; Save.MapY = py; Save.Save();
            G.Go(new LevelIntroScreen(this, "hb", "HAMMER BRO BATTLE"));
        }

        public MapScreen(int world, bool showIntro) { this.world = world; this.showIntro = showIntro; }

        public override void Enter()
        {
            if (map == null)
            {
                map = MapDef.Load(world);
                if (map == null) throw new InvalidOperationException("Missing map for world " + world);
                var st = map.Find('S');
                px = st.X; py = st.Y;
                if (Save.World == world && Save.MapX >= 0 && map.NodeAt(Save.MapX, Save.MapY) != null) { px = Save.MapX; py = Save.MapY; }
                intro = showIntro ? 150 : 0;
                if (showIntro && Save.TwoPlayer) message = (G.Session.PlayerIndex == 0 ? "MARIO" : "LUIGI") + " START!";
                LoadBros();
            }
            Save.World = world;
            Save.HighestWorld = Math.Max(Save.HighestWorld, world);
            Sound.Music(map.Music, false);
            if (!Sound.HasSong(map.Music)) Sound.Music("map1", false);
        }

        string Key(char c) { return world + ":" + c; }
        bool Cleared(MapNode n) { return n != null && Save.Cleared.Contains(Key(n.Code)); }
        bool LockOpen(MapNode n) { return Save.Flags.Contains("open:" + Key(n.Code)); }
        bool ToadUsed(MapNode n) { return Save.Flags.Contains("toad:" + Key(n.Code)); }
        bool Blocking(MapNode n)
        {
            if (n == null) return false;
            if (n.Kind == "level" || n.Kind == "fortress" || n.Kind == "airship" || n.Kind == "castle") return !Cleared(n);
            return false;
        }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (G.Session.Save.TwoPlayer && G.Session.PlayerIndex == 1) any = p1 | p2;
            Save.PlayFrames++;
            if (messageT > 0) messageT--;
            if (intro > 0) { intro--; if (any.P(Btn.A) || any.P(Btn.Start)) intro = 0; return; }
            UpdateCamera();
            if (movePix > 0) { walkT++; if (moveDx != 0) faceLeft = moveDx < 0; } else walkT = 0;
            if (movePix > 0) { StepMove(); return; }
            // a Hammer Bro walked onto us
            int meet = BroAt(px, py);
            if (meet >= 0 && pendingBro < 0 && messageT == 0) { StartBattle(meet); return; }
            if (invOpen) { InventoryTick(any); return; }
            if (menuOpen) { MenuTick(any); return; }
            if (G.BackPressed || any.P(Btn.Start)) { menuOpen = true; menuSel = 0; worldPick = world; Sound.Sfx(SfxId.Pause); return; }

            int dx = 0, dy = 0;
            if (any.H(Btn.Left)) dx = -1; else if (any.H(Btn.Right)) dx = 1; else if (any.H(Btn.Up)) dy = -1; else if (any.H(Btn.Down)) dy = 1;
            if (dx != 0 || dy != 0) { TryMove(dx, dy); if (movePix > 0) StepMove(); }   // start moving this frame (no 1-frame stall)
            else if (any.P(Btn.A)) EnterNode();
            else if (any.P(Btn.B) || any.P(Btn.Select)) { invOpen = true; invSel = 0; Sound.Sfx(SfxId.Pause); }
        }

        // ------------------------------------------------------------------ map menu (Start)
        bool menuOpen; int menuSel, worldPick;
        string[] MenuItems()
        {
            return Save.HighestWorld > 1 ? new[] { "CONTINUE", "ITEMS", "WORLD SELECT", "OPTIONS", "SAVE & QUIT" } : new[] { "CONTINUE", "ITEMS", "OPTIONS", "SAVE & QUIT" };
        }

        void MenuTick(PadState any)
        {
            var items = MenuItems();
            int v = nav.Vertical(any);
            if (v != 0) { menuSel = (menuSel + v + items.Length) % items.Length; Sound.Sfx(SfxId.MenuMove); }
            string cur = items[menuSel];
            if (cur == "WORLD SELECT")
            {
                int h = nav.Horizontal(any);
                if (h != 0) { worldPick = Math.Max(1, Math.Min(Save.HighestWorld, worldPick + h)); Sound.Sfx(SfxId.MenuMove); }
            }
            if (any.P(Btn.B) || G.BackPressed || any.P(Btn.Start) && cur == "CONTINUE") { menuOpen = false; Sound.Sfx(SfxId.MenuBack); return; }
            if (!(any.P(Btn.A) || any.P(Btn.Start))) return;
            switch (cur)
            {
                case "CONTINUE": menuOpen = false; Sound.Sfx(SfxId.MenuBack); break;
                case "ITEMS": menuOpen = false; invOpen = true; invSel = 0; Sound.Sfx(SfxId.MenuSelect); break;
                case "OPTIONS": menuOpen = false; G.Go(new OptionsScreen(this), false); break;
                case "WORLD SELECT":
                    menuOpen = false;
                    if (worldPick != world)
                    {
                        Save.World = worldPick; Save.MapX = -1; Save.MapY = -1; Save.Save();
                        Sound.Sfx(SfxId.MapEnter);
                        G.Go(new MapScreen(worldPick, true));
                    }
                    break;
                case "SAVE & QUIT": menuOpen = false; SaveAndQuitPrompt(); break;
            }
        }

        void DrawMenu(Ppu ppu)
        {
            var items = MenuItems();
            int h = 24 + items.Length * 12;
            Hud.Window(ppu, 64, 40, 128, h);
            for (int i = 0; i < items.Length; i++)
            {
                string s = items[i];
                if (s == "WORLD SELECT") s = "WORLD < " + worldPick + " >";
                Hud.Txt(ppu, s, 84, 52 + i * 12, i == menuSel ? 0x30 : 0x10);
                if (i == menuSel) Hud.Cursor(ppu, 72, 52 + i * 12, t);
            }
        }

        void SaveAndQuitPrompt()
        {
            Save.MapX = px; Save.MapY = py; Save.Save();
            Sound.Sfx(SfxId.MenuBack);
            G.Go(new TitleScreen());
        }

        void TryMove(int dx, int dy)
        {
            var here = map.NodeAt(px, py);
            if (Blocking(here) && !cloudActive && !(dx == -arriveDx && dy == -arriveDy && (arriveDx != 0 || arriveDy != 0))) { return; }
            char next = map.At(px + dx, py + dy);
            var nn = map.NodeAt(px + dx, py + dy);
            bool ok = MapDef.IsPath(next) || nn != null;
            if (nn != null && nn.Kind == "lock" && !LockOpen(nn)) ok = false;
            if (!ok) return;
            moveDx = dx; moveDy = dy; movePix = 16;
            Sound.Sfx(SfxId.MapStep);
        }

        void StepMove()
        {
            movePix -= 2;
            if (movePix > 0) return;
            px += moveDx; py += moveDy;
            int bro = BroAt(px, py);
            if (bro >= 0) { arriveDx = moveDx; arriveDy = moveDy; StartBattle(bro); return; }
            var n = map.NodeAt(px, py);
            bool stop = n != null && n.Kind != "lock";
            if (n != null && n.Kind == "lock") stop = false;
            if (stop)
            {
                arriveDx = moveDx; arriveDy = moveDy;
                if (Blocking(n) && cloudActive) { }
                Save.MapX = px; Save.MapY = py;
                return;
            }
            // continue along the path
            char c = map.At(px + moveDx, py + moveDy);
            var nn = map.NodeAt(px + moveDx, py + moveDy);
            if (MapDef.IsPath(c) || nn != null && (nn.Kind != "lock" || LockOpen(nn))) { movePix = 16; return; }
            // path turns: find the single continuation that isn't where we came from
            int[] ds = { 1, 0, -1, 0, 0, 1, 0, -1 };
            for (int k = 0; k < 4; k++)
            {
                int ndx = ds[k * 2], ndy = ds[k * 2 + 1];
                if (ndx == -moveDx && ndy == -moveDy) continue;
                char cc = map.At(px + ndx, py + ndy);
                var n2 = map.NodeAt(px + ndx, py + ndy);
                if (MapDef.IsPath(cc) || n2 != null && (n2.Kind != "lock" || LockOpen(n2)))
                {
                    moveDx = ndx; moveDy = ndy; movePix = 16; return;
                }
            }
            arriveDx = moveDx; arriveDy = moveDy;
        }

        void EnterNode()
        {
            int nsx, nsy;
            if (NSpadePos(out nsx, out nsy) && nsx == px && nsy == py)
            {
                Sound.Sfx(SfxId.MapEnter);
                Save.MapX = px; Save.MapY = py;
                G.Go(new NSpadeScreen(this, px, py));
                return;
            }
            var n = map.NodeAt(px, py);
            if (n == null) return;
            if ((n.Kind == "level" || n.Kind == "fortress" || n.Kind == "airship" || n.Kind == "castle"))
            {
                if (Cleared(n)) { Sound.Sfx(SfxId.Error); return; }
                if (LevelLoader.Load(n.Level) == null) { message = "COMING SOON"; messageT = 90; Sound.Sfx(SfxId.Error); return; }
                cloudActive = false;
                Sound.Sfx(SfxId.MapEnter);
                Save.MapX = px; Save.MapY = py; Save.Save();
                var node = n;
                G.Go(new LevelIntroScreen(this, node.Level, IntroTitle(node)));
            }
            else if (n.Kind == "toad")
            {
                if (ToadUsed(n)) { Sound.Sfx(SfxId.Error); return; }
                Sound.Sfx(SfxId.MapEnter);
                G.Go(new ToadHouseScreen(this, n));
            }
            else if (n.Kind == "spade")
            {
                if (ToadUsed(n)) { Sound.Sfx(SfxId.Error); return; }
                Sound.Sfx(SfxId.MapEnter);
                G.Go(new SpadeScreen(this, n));
            }
        }

        string IntroTitle(MapNode n)
        {
            if (n.Kind == "fortress") return "WORLD " + world + "  FORTRESS";
            if (n.Kind == "airship") return "WORLD " + world + "  AIRSHIP";
            if (n.Kind == "castle") return "BOWSER'S CASTLE";
            return "WORLD " + n.Level.ToUpperInvariant();
        }

        public void MarkToadUsed(MapNode n) { Save.Flags.Add("toad:" + Key(n.Code)); Save.Save(); }

        /// <summary>Called by the level screen when a level ends.</summary>
        public void LevelDone(string levelId, LevelResult r)
        {
            var s = G.Session;
            if (levelId == "hb")
            {
                int i = pendingBro; pendingBro = -1;
                if (r == LevelResult.Cleared && i >= 0)
                {
                    broAlive[i] = false; Save.Flags.Add("hbdead:" + world + ":" + i);
                    Item it = ParseItem(map.Bros[i].Item);
                    if (s.Cur.Items.Count < 28) s.Cur.Items.Add(it);
                    message = "GOT " + ItemName(it) + "!"; messageT = 150;
                    Save.Save(); G.Go(this); return;
                }
                if (r == LevelResult.Exited) { G.Go(this); return; }
                // lost the battle: normal death handling below, the Hammer Bro stays
                levelId = "";
            }
            var n = FindByLevel(levelId);
            if (r != LevelResult.Exited && r != LevelResult.WorldCleared && r != LevelResult.GameCleared) MoveBros();
            switch (r)
            {
                case LevelResult.Cleared:
                case LevelResult.FortressCleared:
                    if (n != null) Save.Cleared.Add(Key(n.Code));
                    if (n != null && n.Kind == "fortress")
                    {
                        foreach (var l in map.Nodes) if (l.Kind == "lock" && l.OpensWith == n.Code) Save.Flags.Add("open:" + Key(l.Code));
                        message = "A LOCK HAS OPENED!"; messageT = 150;
                    }
                    CheckNSpade();
                    NextTurn();
                    Save.MapX = px; Save.MapY = py; Save.Save();
                    G.Go(this);
                    break;
                case LevelResult.WorldCleared:
                    if (n != null) Save.Cleared.Add(Key(n.Code));
                    Save.World = Math.Min(8, world + 1);
                    Save.MapX = -1; Save.MapY = -1;
                    Save.Save();
                    G.Go(new WorldClearScreen(world));
                    break;
                case LevelResult.GameCleared:
                    if (n != null) Save.Cleared.Add(Key(n.Code));
                    Save.Beaten = true; Save.Save();
                    G.Go(new EndingScreen());
                    break;
                case LevelResult.Exited:
                    G.Go(this);
                    break;
                default: // died / time up
                    s.Form = Form.Small;
                    s.LoseLife();
                    if (s.Cur.Lives <= 0 && !Host.Settings.InfiniteLives)
                    {
                        s.Cur.GameOver = true;
                        G.Go(new GameOverScreen(this));
                    }
                    else { CheckNSpade(); NextTurn(); Save.MapX = px; Save.MapY = py; Save.Save(); G.Go(this); }
                    break;
            }
        }

        // ------------------------------------------------------------------ N-Spade panel (every 80,000 points)
        const string NSpadeFlag = "nspade:";

        /// <summary>Tile of this world's N-Spade panel, or false if none is showing.</summary>
        bool NSpadePos(out int x, out int y)
        {
            x = y = -1;
            string pre = NSpadeFlag + world + ":";
            foreach (var f in Save.Flags)
                if (f.StartsWith(pre))
                {
                    var p = f.Substring(pre.Length).Split(':');
                    if (p.Length == 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)) return true;
                }
            return false;
        }

        void CheckNSpade()
        {
            var cur = G.Session.Cur;
            if (cur.Score < cur.NSpadeAt) return;
            while (cur.NSpadeAt <= cur.Score) cur.NSpadeAt += 80000;
            int ox, oy;
            if (NSpadePos(out ox, out oy)) return;   // one at a time
            var spots = new List<MapNode>();
            foreach (var n in map.Nodes)
                if (n.Kind == "dot" && !(n.X == px && n.Y == py) && BroAt(n.X, n.Y) < 0) spots.Add(n);
            if (spots.Count == 0) return;
            var s = spots[rng.Next(spots.Count)];
            Save.Flags.Add(NSpadeFlag + world + ":" + s.X + ":" + s.Y);
            message = "AN N-SPADE PANEL APPEARED!"; messageT = 150;
            Sound.Sfx(SfxId.OneUp);
        }

        public void NSpadeDone(int x, int y)
        {
            Save.Flags.Remove(NSpadeFlag + world + ":" + x + ":" + y);
            Save.MapX = px; Save.MapY = py; Save.Save();
        }

        static Item ParseItem(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "mushroom": return Item.Mushroom; case "flower": return Item.Flower; case "leaf": return Item.Leaf;
                case "star": return Item.Star; case "pwing": return Item.PWing; case "tanooki": return Item.Tanooki;
                case "frog": return Item.Frog; case "cloud": return Item.Cloud;
            }
            return Item.Hammer;
        }

        MapNode FindByLevel(string id)
        {
            foreach (var n in map.Nodes) if (n.Level != null && string.Equals(n.Level, id, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        void NextTurn()
        {
            if (!Save.TwoPlayer) return;
            int other = 1 - G.Session.PlayerIndex;
            if (Save.Players[other].GameOver) return;
            G.Session.PlayerIndex = other; Save.Turn = other;
            message = (other == 0 ? "MARIO" : "LUIGI") + "'S TURN"; messageT = 120;
        }

        /// <summary>SMB3 continue: back to START, 4 lives, score 0, cards lost; cleared levels reset except fortresses.</summary>
        public void Continue()
        {
            var cur = G.Session.Cur;
            cur.Lives = 4; cur.Score = 0; cur.NSpadeAt = 80000; cur.Cards.Clear(); cur.GameOver = false; cur.Form = Form.Small;
            var keep = new List<string>();
            foreach (var k in Save.Cleared)
            {
                bool thisWorld = k.StartsWith(world + ":");
                char c = k[k.Length - 1];
                if (!thisWorld || c == 'F' || c == 'G') keep.Add(k);
            }
            Save.Cleared.Clear(); foreach (var k in keep) Save.Cleared.Add(k);
            foreach (var f in new List<string>(Save.Flags)) if (f.StartsWith("toad:" + world + ":")) Save.Flags.Remove(f);
            var st = map.Find('S'); px = st.X; py = st.Y; arriveDx = arriveDy = 0;
            Save.MapX = px; Save.MapY = py; Save.Save();
        }

        // ------------------------------------------------------------------ inventory
        void InventoryTick(PadState any)
        {
            var items = G.Session.Cur.Items;
            int h = nav.Horizontal(any) + nav.Vertical(any) * 7;
            if (h != 0 && items.Count > 0) { invSel = Math.Max(0, Math.Min(items.Count - 1, invSel + h)); Sound.Sfx(SfxId.MenuMove); }
            if (any.P(Btn.B) || any.P(Btn.Select) || G.BackPressed) { invOpen = false; Sound.Sfx(SfxId.MenuBack); return; }
            if ((any.P(Btn.A) || any.P(Btn.Start)) && items.Count > 0)
            {
                var it = items[invSel];
                if (UseItem(it)) { items.RemoveAt(invSel); invSel = Math.Max(0, Math.Min(invSel, items.Count - 1)); Sound.Sfx(SfxId.ItemUse); Save.Save(); invOpen = false; }
                else Sound.Sfx(SfxId.Error);
            }
        }

        bool UseItem(Item it)
        {
            var s = G.Session;
            switch (it)
            {
                case Item.Mushroom: if (s.Form != Form.Small) return false; s.Form = Form.Big; return true;
                case Item.Flower: if (s.Form == Form.Fire) return false; s.Form = Form.Fire; return true;
                case Item.Leaf: if (s.Form == Form.Raccoon) return false; s.Form = Form.Raccoon; return true;
                case Item.Tanooki: if (s.Form == Form.Tanooki) return false; s.Form = Form.Tanooki; return true;
                case Item.Frog: if (s.Form == Form.Frog) return false; s.Form = Form.Frog; return true;
                case Item.Hammer: if (s.Form == Form.Hammer) return false; s.Form = Form.Hammer; return true;
                case Item.Star: if (s.StartStar) return false; s.StartStar = true; message = "STARMAN READY!"; messageT = 90; return true;
                case Item.PWing: if (s.UsePWing) return false; s.UsePWing = true; message = "P-WING READY!"; messageT = 90; return true;
                case Item.Cloud:
                {
                    var here = map.NodeAt(px, py);
                    if (!Blocking(here)) return false;
                    cloudActive = true; message = "CLOUD: PASS THIS PANEL"; messageT = 120; return true;
                }
            }
            return false;
        }

        public static string ItemName(Item it)
        {
            switch (it)
            {
                case Item.Mushroom: return "SUPER MUSHROOM"; case Item.Flower: return "FIRE FLOWER"; case Item.Leaf: return "SUPER LEAF";
                case Item.Star: return "STARMAN"; case Item.PWing: return "P-WING"; case Item.Tanooki: return "TANOOKI SUIT";
                case Item.Frog: return "FROG SUIT"; case Item.Hammer: return "HAMMER SUIT"; case Item.Cloud: return "CLOUD";
            }
            return "";
        }

        public static void DrawItemIcon(Ppu ppu, Item it, int x, int y, int frame)
        {
            // dedicated inventory icons (scenes.art) when present
            string icon = it == Item.PWing ? "icon.pwing" : it == Item.Tanooki ? "icon.tanooki" : it == Item.Frog ? "icon.frog"
                : it == Item.Hammer ? "icon.hammer" : it == Item.Cloud ? "icon.cloud" : null;
            if (icon != null && Art.Has(icon)) { ppu.Spr(Art.Get(icon), x, y, Art.PreviewPalFor(icon)); return; }
            switch (it)
            {
                case Item.Mushroom: ppu.Spr(Art.Get("mushroom"), x, y, Art.Pal("mushroom")); break;
                case Item.Flower: ppu.Spr(Art.Get("flower.1"), x, y, Art.Pal("flower")); break;
                case Item.Leaf: ppu.Spr(Art.Get("leaf"), x, y, Art.Pal("leaf")); break;
                case Item.Star: ppu.Spr(Art.Get("star"), x, y, Art.Pal("star")); break;
                case Item.PWing: ppu.Spr(Art.Get("wing.1"), x + 4, y + 4, Art.Pal("wing")); Hud.Txt(ppu, "P", x + 4, y + 4, 0x16); break;
                case Item.Tanooki: ppu.Spr(Art.Get("leaf"), x, y, Art.Pal("leaf")); break;
                case Item.Frog: ppu.Spr(Art.Get("mushroom"), x, y, Art.HasPal("frog") ? Art.Pal("frog") : Art.Pal("oneup")); break;
                case Item.Hammer: ppu.Spr(Art.Get("hammer.1"), x, y, Art.Pal("hammer")); break;
                case Item.Cloud: ppu.Spr(Art.Get("cloud.ride"), x, y, Art.HasPal("lakitu") ? Art.Pal("lakitu") : Art.PreviewPalFor("cloud.ride")); break;
            }
        }

        // ------------------------------------------------------------------ rendering
        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            ppu.ClipBottom = 192;
            var land = Art.Pal(map.Pal);
            ppu.FillRect(0, 0, 256, 192, land[0] != 0 ? land[0] : (Art.HasPal(map.Pal + ".bg") ? Art.Pal(map.Pal + ".bg")[1] : 0x0F));
            if (camF < 0) camF = CamTarget() * 16;
            int camX = camF / 16;
            string water = (t / 32) % 2 == 0 ? "m.water1" : "m.water2";
            // pass 1: opaque terrain
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                {
                    int sx = x * 16 - camX, sy = y * 16;
                    if (sx < -16 || sx > 256) continue;
                    string baseImg;
                    switch (BaseTerrain(x, y))
                    {
                        case '~': case '=': case '!': baseImg = water; break;
                        case ',': baseImg = "m.land2"; break;
                        case 's': baseImg = "m.sand"; break;
                        case 'w': baseImg = "m.snow"; break;
                        case 'c': baseImg = "m.cloudland"; break;
                        case 'v': baseImg = "m.lava"; break;
                        default: baseImg = "m.land"; break;
                    }
                    ppu.Tile(Art.Get(baseImg), sx, sy, MapPal(baseImg));
                }
            // pass 2: 16-bit shorelines - cliff faces under land, foam where liquid meets ground
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                {
                    int sx = x * 16 - camX, sy = y * 16;
                    if (sx < -16 || sx > 256) continue;
                    if (Liquid(map.Grid[x, y])) DrawShore(ppu, land, x, y, sx, sy);
                    else DrawPatchRim(ppu, land, x, y, sx, sy);
                }
            // pass 3: overlays with transparent edges
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                {
                    int sx = x * 16 - camX, sy = y * 16;
                    if (sx < -16 || sx > 256) continue;
                    string ov = null;
                    switch (map.Grid[x, y])
                    {
                        case 'T': ov = "m.tree"; break;
                        case '^': ov = "m.hill"; break;
                        case 'r': ov = "m.rock"; break;
                        case '*': ov = "m.flower"; break;
                        case 'p': ov = "m.palm"; break;
                        case 'k': ov = "m.skull"; break;
                        case '-': ov = "m.path.h"; break;
                        case '|': ov = "m.path.v"; break;
                        case '=': ov = "m.bridge.h"; break;
                        case '!': ov = "m.bridge.v"; break;
                    }
                    if (ov != null) ppu.Tile(Art.Get(ov), sx, sy, MapPal(ov));
                }
            foreach (var n in map.Nodes)
            {
                int sx = n.X * 16 - camX, sy = n.Y * 16;
                string img;
                switch (n.Kind)
                {
                    case "start": img = "m.start"; break;
                    case "dot": img = "m.dot"; break;
                    case "lock":
                    {
                        bool horiz = map.At(n.X - 1, n.Y) == '-' || map.At(n.X + 1, n.Y) == '-';
                        img = LockOpen(n) ? (horiz ? "m.path.h" : "m.path.v") : (horiz ? "m.lock.v" : "m.lock.h");
                        break;
                    }
                    case "toad": img = ToadUsed(n) ? "m.toad.used" : "m.toad"; break;
                    case "spade": img = ToadUsed(n) ? "m.dot" : "m.spade"; break;
                    case "fortress": img = Cleared(n) ? "m.fortress.ruin" : "m.fortress"; break;
                    case "airship": img = "m.airship"; sy -= (t / 16) % 2; break;
                    case "castle": img = "m.castle"; break;
                    default: img = Cleared(n) ? "m.panel.clear" : "m.panel"; break;
                }
                ppu.Tile(Art.Get(img), sx, sy, MapPal(img));
                if (img == "m.panel") Hud.GradText(ppu, n.Code.ToString(), sx + 4, sy + 4, Hud.C(0x482008), Hud.C(0x301000), Hud.C(0x200800), Hud.C(0xFFE8A0));
            }
            // N-Spade panel (blue spade)
            int nspX, nspY;
            if (NSpadePos(out nspX, out nspY))
                ppu.Tile(Art.Get("m.spade"), nspX * 16 - camX, nspY * 16, NSpadePal((t / 16) % 2 == 0));
            // wandering Hammer Bros
            if (bx != null)
                for (int i = 0; i < bx.Length; i++)
                    if (broAlive[i]) ppu.Spr(Art.Get("m.hbro"), bx[i] * 16 - camX, by[i] * 16 - 2 - ((t / 10 + i) % 2), MapPal("m.hbro"), (t / 40) % 2 == 0);
            // player icon
            DrawPlayer(ppu, camX);
            ppu.ResetClip();
            Hud.Draw(ppu, G.Session, world, 0, false, 0, t, false);

            if (intro > 0)
            {
                Hud.Window(ppu, 40, 60, 176, 56);
                string w = "WORLD " + world;
                Hud.TxtC(ppu, w, 72, 0x30);
                Hud.TxtC(ppu, map.Name, 88, 0x28);
                if (message != "") Hud.TxtC(ppu, message, 100, 0x2A);
            }
            else if (messageT > 0 && message != "")
            {
                Hud.Window(ppu, 16, 8, 224, 24);
                Hud.TxtC(ppu, message, 16, 0x30);
            }
            if (invOpen) DrawInventory(ppu);
            if (menuOpen) DrawMenu(ppu);
        }

        /// <summary>The blue N-Spade panel: m.spade (map.panel slots) recoloured with a blue face that pulses.</summary>
        public static ushort[] NSpadePal(bool bright)
        {
            return Art.PN(Hud.C(0x080828), Hud.C(bright ? 0x4878F0 : 0x3060D8), Hud.C(bright ? 0xB0D0FF : 0x88B0F8), Hud.C(0x2040A8),
                Hud.C(0x101860), Hud.C(0xFFFFFF), Hud.C(0xE83020), Hud.C(0x901010), Hud.C(0xFFFFFF), Hud.C(0xC8D8F8));
        }

        static bool Ground(char c) { return c == '.' || c == ',' || c == 's' || c == 'w' || c == 'c'; }

        /// <summary>The ground a cell stands on: terrain cells are themselves; paths, nodes and scenery take the most
        /// common ground around them (so a node in the snow sits on snow, not on a square of grass).</summary>
        char BaseTerrain(int x, int y)
        {
            char c = map.At(x, y);
            if (Ground(c) || Liquid(c)) return c;
            for (int r = 1; r <= 3; r++)
            {
                int best = 0; char pick = '.';
                foreach (char g in ".,swc")
                {
                    int n = 0;
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) if (map.At(x + dx, y + dy) == g) n += (dx == 0 || dy == 0) ? 2 : 1;
                    if (n > best) { best = n; pick = g; }
                }
                if (best > 0) return pick == ',' ? '.' : pick;
            }
            return '.';
        }

        /// <summary>A sand/snow/cloud patch next to plain land gets a 1-px shaded rim so it reads as a raised patch.</summary>
        void DrawPatchRim(Ppu ppu, ushort[] pal, int x, int y, int sx, int sy)
        {
            if (pal.Length < 11) return;
            char me = BaseTerrain(x, y);
            if (me != 's' && me != 'w' && me != 'c') return;
            Func<int, int, bool> other = (gx, gy) =>
            {
                if (gx < 0 || gy < 0 || gx >= map.W || gy >= map.H) return false;
                char o = BaseTerrain(gx, gy);
                return o == '.' || o == ',';
            };
            int rim = pal[10], lit = pal[8];
            if (other(x, y - 1)) ppu.FillRect(sx, sy, 16, 1, lit);
            if (other(x - 1, y)) ppu.FillRect(sx, sy, 1, 16, lit);
            if (other(x, y + 1)) { ppu.FillRect(sx, sy + 15, 16, 1, rim); ppu.FillRect(sx, sy + 14, 16, 1, pal[9]); }
            if (other(x + 1, y)) ppu.FillRect(sx + 15, sy, 1, 16, rim);
        }

        /// <summary>Interpolated player position in map pixels (top-left of the tile it is on/between).</summary>
        void VisualPos(out int vx, out int vy)
        {
            int off = movePix > 0 ? 16 - movePix : 0;   // after arriving, movePix is 0 and px/py already hold the node
            vx = px * 16 + moveDx * off; vy = py * 16 + moveDy * off;
        }

        int CamTarget()
        {
            int vx, vy; VisualPos(out vx, out vy);
            return Math.Max(0, Math.Min(map.W * 16 - 256, vx + 8 - 128));
        }

        /// <summary>Camera eases toward the walker instead of jumping a tile at a time.</summary>
        void UpdateCamera()
        {
            int target = CamTarget() * 16;
            if (camF < 0) { camF = target; return; }
            int d = target - camF;
            camF += d / 6 != 0 ? d / 6 : Math.Sign(d) * Math.Min(Math.Abs(d), 16);
        }

        /// <summary>The map walker: centred on its tile with the feet on the tile's lower middle, a soft shadow,
        /// a quick two-step walk cycle with a 1-px bounce while moving and a slow breathing bob while idle.</summary>
        void DrawPlayer(Ppu ppu, int camX)
        {
            int vx, vy; VisualPos(out vx, out vy);
            bool moving = movePix > 0;
            int frame, bob;
            if (moving) { frame = (walkT / 4) % 2; bob = frame == 1 ? -1 : 0; }
            else { frame = (t / 24) % 2; bob = (t / 24) % 2 == 1 ? -1 : 0; }
            int sx = vx - camX, feetY = vy + 13;
            // shadow: darken whatever is underneath (an ellipse 12x3 under the feet)
            int[] half = { 4, 6, 4 };
            for (int r = 0; r < 3; r++)
                for (int x = sx + 8 - half[r]; x < sx + 8 + half[r]; x++)
                {
                    int y = feetY - 1 + r;
                    if (x < 0 || x >= 256 || y < 0 || y >= ppu.ClipBottom) continue;
                    int under = ppu.Px[y * Ppu.W + x];
                    ppu.FillRect(x, y, 1, 1, Hud.Mix(NesPalette.RgbOf(under), 0x000010, 96));
                }
            var ppal = G.Session.PlayerIndex == 1 ? Art.Pal("luigi") : Art.Pal("mario");
            var img = Art.Get(frame == 0 ? "mp.mario1" : "mp.mario2");
            ppu.Spr(img, sx + 8 - img.W / 2, feetY - img.H + bob, ppal, faceLeft);
        }

        static bool Liquid(char c) { return c == '~' || c == '=' || c == '!' || c == 'v'; }

        /// <summary>Edge shading of a liquid cell next to ground: a cliff face (ground shade + outline) hanging into the
        /// liquid below land, darker rims on the sides, and a light foam line along every shore.</summary>
        void DrawShore(Ppu ppu, ushort[] pal, int x, int y, int sx, int sy)
        {
            if (pal.Length < 11) return;
            int foam = pal[4], outline = pal[1];
            Func<int, int, int> faceOf = (gx, gy) =>
            {
                char c = map.At(gx, gy);
                c = BaseTerrain(gx, gy);
                return c == 's' || c == 'w' || c == 'c' ? pal[10] : pal[7];
            };
            Func<int, int, bool> ground = (gx, gy) => gx >= 0 && gy >= 0 && gx < map.W && gy < map.H && !Liquid(map.At(gx, gy));
            bool up = ground(x, y - 1), dn = ground(x, y + 1), lf = ground(x - 1, y), rt = ground(x + 1, y);
            if (up)
            {
                int f = faceOf(x, y - 1);
                ppu.FillRect(sx, sy, 16, 3, f);
                ppu.FillRect(sx, sy + 3, 16, 1, outline);
                for (int i = 0; i < 16; i += 4) ppu.FillRect(sx + i + ((x + y) & 1) * 2, sy + 1, 1, 2, outline);
                ppu.FillRect(sx, sy + 4, 16, 1, foam);
            }
            if (lf)
            {
                ppu.FillRect(sx, sy, 1, 16, faceOf(x - 1, y));
                ppu.FillRect(sx + 1, sy + (up ? 4 : 0), 1, up ? 12 : 16, foam);
            }
            if (rt)
            {
                ppu.FillRect(sx + 15, sy, 1, 16, outline);
                ppu.FillRect(sx + 14, sy + (up ? 4 : 0), 1, up ? 12 : 16, foam);
            }
            if (dn) ppu.FillRect(sx, sy + 15, 16, 1, foam);
            // outer corners: land diagonally up-left/up-right continues the cliff a little
            if (!up && ground(x - 1, y - 1) && !lf) { ppu.FillRect(sx, sy, 2, 3, faceOf(x - 1, y - 1)); ppu.FillRect(sx, sy + 3, 2, 1, outline); }
            if (!up && ground(x + 1, y - 1) && !rt) { ppu.FillRect(sx + 14, sy, 2, 3, faceOf(x + 1, y - 1)); ppu.FillRect(sx + 14, sy + 3, 2, 1, outline); }
        }

        /// <summary>Map tiles authored with a map.wN palette use this world's palette; others keep their own.</summary>
        ushort[] MapPal(string img)
        {
            string p = Art.PreviewPalName(img);
            if (p == null || p.StartsWith("map.w")) return Art.Pal(map.Pal);
            return Art.Pal(p);
        }

        void DrawInventory(Ppu ppu)
        {
            var items = G.Session.Cur.Items;
            Hud.Window(ppu, 8, 104, 240, 84);
            Hud.Txt(ppu, "ITEMS", 16, 112, 0x28);
            if (items.Count == 0) Hud.Txt(ppu, "NO ITEMS", 96, 140, 0x10);
            for (int i = 0; i < items.Count && i < 28; i++)
            {
                int x = 20 + (i % 7) * 32, y = 124 + (i / 7) * 18;
                if (i == invSel) Hud.Box(ppu, x - 2, y - 2, 20, 20, (t / 8) % 2 == 0 ? 0x28 : 0x30);
                DrawItemIcon(ppu, items[i], x, y, t);
            }
            if (items.Count > 0) Hud.Txt(ppu, ItemName(items[invSel]), 64, 112, 0x30);
        }
    }
}
