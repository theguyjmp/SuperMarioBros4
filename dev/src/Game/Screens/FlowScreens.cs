using System;
using System.Collections.Generic;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    /// <summary>"WORLD 1-1" card before a level.</summary>
    public sealed class LevelIntroScreen : Screen
    {
        readonly MapScreen map; readonly string level, title; int t;
        string subtitle = "";
        public LevelIntroScreen(MapScreen map, string level, string title) { this.map = map; this.level = level; this.title = title; }
        public override void Enter()
        {
            t = 0; Sound.Music("enterlevel", true);
            var def = LevelLoader.Load(level);
            if (def != null && def.Title != null) subtitle = def.Title.ToUpperInvariant();
            if (subtitle.Length > 18) subtitle = subtitle.Substring(0, 18);
        }
        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            if (++t == 80 || t > 10 && (any.P(Btn.A) || any.P(Btn.Start)) && t < 80)
            {
                t = 999;
                var m = map; var id = level;
                G.Go(new LevelScreen(G, id, r => m.LevelDone(id, r)));
            }
        }
        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.Backdrop(ppu, t);
            Hud.Window(ppu, 40, 64, 176, 88);
            Hud.Txt(ppu, title, (256 - title.Length * 8) / 2, 78, 0x30);
            if (subtitle.Length > 0) Hud.Txt(ppu, subtitle, (256 - subtitle.Length * 8) / 2, 92, 0x28);
            var s = G.Session;
            // the player in the current form, with its tail/ears/hood/helmet (feet at y=140)
            Player.DrawIdle(ppu, s.Form, s.PlayerIndex == 1, 96, 108, false);
            Hud.Txt(ppu, "*  " + s.Cur.Lives, 120, 128, 0x30);
        }
    }

    // ======================================================================== Toad house
    public sealed class ToadHouseScreen : Screen
    {
        readonly MapScreen map; readonly MapNode node;
        readonly Item[] chests = new Item[3];
        int sel = 1, t, opened = -1, openT;
        readonly MenuNav nav = new MenuNav();
        public ToadHouseScreen(MapScreen map, MapNode node) { this.map = map; this.node = node; }

        static Item Parse(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "mushroom": return Item.Mushroom; case "flower": return Item.Flower; case "leaf": return Item.Leaf;
                case "star": return Item.Star; case "pwing": return Item.PWing; case "tanooki": return Item.Tanooki;
                case "frog": return Item.Frog; case "hammer": return Item.Hammer; case "cloud": return Item.Cloud;
            }
            return Item.Mushroom;
        }

        public override void Enter()
        {
            var rng = new Random(Environment.TickCount);
            var pool = node.Items ?? new[] { "mushroom", "flower", "leaf" };
            var list = new List<string>(pool);
            for (int i = 0; i < 3; i++)
            {
                int k = rng.Next(list.Count);
                chests[i] = Parse(list[k]);
                if (list.Count > 1) list.RemoveAt(k);
            }
            Sound.Music("toadhouse", true);
        }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (opened >= 0)
            {
                openT++;
                if (openT > 150 || openT > 40 && (any.P(Btn.A) || any.P(Btn.Start)))
                {
                    map.MarkToadUsed(node);
                    G.Go(map);
                }
                return;
            }
            int h = nav.Horizontal(any);
            if (h != 0) { sel = (sel + h + 3) % 3; Sound.Sfx(SfxId.MenuMove); }
            if (any.P(Btn.A) || any.P(Btn.Start))
            {
                opened = sel; openT = 0;
                var items = G.Session.Cur.Items;
                if (items.Count < 28) items.Add(chests[sel]);
                Sound.Sfx(SfxId.Chest);
            }
            if (G.BackPressed || any.P(Btn.B)) G.Go(map);
        }

        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            // wooden room
            // warm wooden room: bevelled wall planks with grain, lantern-lit, and a polished floor
            for (int y = 0; y < 160; y += 16)
                for (int x = -((y / 16) % 2) * 16; x < 256; x += 32)
                {
                    Hud.VGrad(ppu, x, y, 32, 16, 0xA86030, 0x6A3818);
                    ppu.FillRect(x, y, 32, 1, Hud.C(0xD89050));
                    ppu.FillRect(x, y + 15, 32, 1, Hud.C(0x301008));
                    ppu.FillRect(x + 31, y, 1, 16, Hud.C(0x401808));
                    ppu.FillRect(x + 6 + (y * 7) % 13, y + 6, 9, 1, Hud.C(0x804020));
                    ppu.FillRect(x + 3 + (y * 5) % 17, y + 10, 7, 1, Hud.C(0x804020));
                    ppu.FillRect(x + 2, y + 3, 1, 1, Hud.C(0xE8C080));
                    ppu.FillRect(x + 29, y + 3, 1, 1, Hud.C(0xE8C080));
                }
            ppu.FillRect(231, 58, 1, 8, Hud.C(0x301008));
            Lantern.Draw(ppu, 224, 64, true, t);
            Hud.VGrad(ppu, 0, 160, 256, 32, 0xD89048, 0x804018);
            ppu.FillRect(0, 160, 256, 1, Hud.C(0xFFD890));
            ppu.FillRect(0, 161, 256, 1, Hud.C(0xE8A860));
            for (int x = 0; x < 256; x += 48) ppu.FillRect(x, 162, 1, 30, Hud.C(0x703810));
            ppu.FillRect(0, 176, 256, 1, Hud.C(0x703810));
            Hud.Window(ppu, 24, 16, 208, 40);
            Hud.Txt(ppu, opened < 0 ? "PICK A CHEST!" : "YOU GOT", 40, 26, 0x30);
            Hud.Txt(ppu, opened < 0 ? "ONE ITEM TO KEEP." : MapScreen.ItemName(chests[opened]) + "!", 40, 38, 0x28);
            var ses = G.Session;
            if (Art.Has("chest.closed") && Art.Has("toad.1"))
            {
                // SMB3-style room: Toad on the left, three 16x16 chests on the floor, Mario beside the chosen one
                string toad = opened < 0 && (t / 30) % 2 == 1 ? "toad.2" : "toad.1";
                Art.DrawSplit(ppu, toad, "toad", 28, 160 - 24, false);
                for (int i = 0; i < 3; i++)
                {
                    int x = 96 + i * 48, y = 144;
                    bool open = opened == i;
                    string img = open ? "chest.open" : "chest.closed";
                    ppu.Spr(Art.Get(img), x, y, Art.PreviewPalFor(img));
                    if (open) MapScreen.DrawItemIcon(ppu, chests[i], x, y - 18 - Math.Min(24, openT / 2), t);
                    if (opened < 0 && i == sel) Pointer(ppu, x + 8, y - 10 + (t / 12) % 2, (t / 8) % 2 == 0 ? 0x30 : 0x28);
                }
                Player.DrawIdle(ppu, ses.Form, ses.PlayerIndex == 1, 96 + sel * 48 - 20, 128, false);
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    int x = 56 + i * 64, y = 128;
                    bool open = opened == i;
                    Hud.Panel(ppu, x, y, 32, 32, 0xC87838, 0x804018, 0xF0B070, 0x401808, 0x200800);
                    ppu.FillRect(x + 1, y + (open ? 1 : 10), 30, 3, Hud.C(0xF8C840));
                    ppu.FillRect(x + 13, y + 12, 6, 6, Hud.C(0xFFF0A0));
                    if (open) { MapScreen.DrawItemIcon(ppu, chests[i], x + 8, y - 20 - Math.Min(20, openT / 2), t); }
                    if (opened < 0 && i == sel) Pointer(ppu, x + 16, y - 10 + (t / 12) % 2, (t / 8) % 2 == 0 ? 0x30 : 0x28);
                }
                Player.DrawIdle(ppu, ses.Form, ses.PlayerIndex == 1, 64 + sel * 64 - 8 + 16, 128, false);
            }
            Hud.Draw(ppu, G.Session, G.Session.Save.World, 0, false, 0, t, false);
        }

        /// <summary>A small down-pointing triangle centred on cx (the font's '^' is the P-meter arrow).</summary>
        static void Pointer(Ppu ppu, int cx, int y, int color)
        {
            int o = Hud.C(0x200800);
            for (int r = 0; r < 5; r++) ppu.FillRect(cx - 4 + r, y - 1 + r, 9 - r * 2, 1, o);
            for (int r = 0; r < 4; r++) ppu.FillRect(cx - 3 + r, y + r, 7 - r * 2, 1, r == 0 ? Hud.C(0xFFFFFF) : color);
        }
    }

    // ======================================================================== spade panel slot machine
    public sealed class SpadeScreen : Screen
    {
        readonly MapScreen map; readonly MapNode node;
        readonly int[] pos = new int[3];
        readonly int[] speed = { 3, 5, 4 };
        int row, t, doneT; bool finished; int prize;
        public SpadeScreen(MapScreen map, MapNode node) { this.map = map; this.node = node; }
        public override void Enter() { Sound.Music("bonus", true); }
        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (finished)
            {
                if (++doneT > 180 || doneT > 40 && any.P(Btn.A)) { map.MarkToadUsed(node); G.Go(map); }
                return;
            }
            for (int i = row; i < 3; i++) pos[i] = (pos[i] + speed[i]) % (3 * 48);
            if (any.P(Btn.A) || any.P(Btn.Start))
            {
                pos[row] = (pos[row] + 24) / 48 * 48 % (3 * 48);
                Sound.Sfx(SfxId.CardStop);
                row++;
                if (row == 3)
                {
                    finished = true;
                    int a = pos[0] / 48, b = pos[1] / 48, c = pos[2] / 48;
                    if (a == b && b == c)
                    {
                        prize = a == 0 ? 2 : a == 1 ? 3 : 5;
                        for (int i = 0; i < prize; i++) G.Session.AddLife();
                        Sound.Music("bonus1up", true);
                    }
                    else Sound.Sfx(SfxId.Error);
                }
            }
        }
        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.Backdrop(ppu, t);
            Hud.Window(ppu, 64, 24, 128, 152);
            for (int r = 0; r < 3; r++)
            {
                int y = 40 + r * 44;
                Hud.Inset(ppu, 80, y, 96, 40, 0xE0E8F8, 0xFFFFFF);
                int icon = (pos[r] / 48) % 3;
                int off = pos[r] % 48;
                // two neighbouring icons scroll horizontally through the window
                for (int k = -1; k <= 1; k++)
                {
                    int ic = ((icon + k) % 3 + 3) % 3;
                    int x = 120 - off + k * 48;
                    if (x < 72 || x > 168) continue;
                    ppu.Spr(Art.Get(GoalBox.CardName(ic)), x, y + 12, GoalBox.CardPal(ic));
                }
                if (r == row && !finished) Hud.Cursor(ppu, 68, y + 16, t);
            }
            if (finished) Hud.TxtC(ppu, prize > 0 ? prize + "UP!" : "NO MATCH...", 180, prize > 0 ? 0x28 : 0x10);
            else Hud.TxtC(ppu, "PRESS A TO STOP", 180, 0x30);
            Hud.Draw(ppu, G.Session, G.Session.Save.World, 0, false, 0, t, false);
        }
    }

    // ======================================================================== game over
    public sealed class GameOverScreen : Screen
    {
        readonly MapScreen map; int sel, t;
        readonly MenuNav nav = new MenuNav();
        public GameOverScreen(MapScreen map) { this.map = map; }
        public override void Enter() { Sound.Music("gameover", true); }
        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (t < 90) return;
            int v = nav.Vertical(any);
            if (v != 0) { sel = 1 - sel; Sound.Sfx(SfxId.MenuMove); }
            if (any.P(Btn.A) || any.P(Btn.Start))
            {
                var save = G.Session.Save;
                if (sel == 0) { map.Continue(); G.Go(map); }
                else
                {
                    int other = 1 - G.Session.PlayerIndex;
                    if (save.TwoPlayer && !save.Players[other].GameOver) { G.Session.PlayerIndex = other; save.Turn = other; save.Save(); G.Go(map); }
                    else { save.Save(); G.Go(new TitleScreen()); }
                }
            }
        }
        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.VGrad(ppu, 0, 0, 256, 240, 0x000000, 0x380810, 4);
            Hud.Window(ppu, 56, 64, 144, 88);
            Hud.GradText(ppu, "GAME OVER", 92, 80, Hud.C(0xFFB0A0), Hud.C(0xF84830), Hud.C(0xB01818), Hud.C(0x100008));
            if (t >= 90)
            {
                Hud.Txt(ppu, "CONTINUE", 100, 108, sel == 0 ? 0x30 : 0x10);
                Hud.Txt(ppu, "END", 100, 124, sel == 1 ? 0x30 : 0x10);
                Hud.Cursor(ppu, 84, 108 + sel * 16, t);
            }
        }
    }

    // ======================================================================== world clear (king's room)
    public sealed class WorldClearScreen : Screen
    {
        readonly int world; int t;
        static readonly string[] Kingdoms = { "MEADOW", "CANYON", "CORAL", "JUNGLE", "CLOUDTOP", "FROSTBITE", "GEARWORKS", "VOLCANO" };
        public WorldClearScreen(int world) { this.world = world; }
        public override void Enter() { if (Sound.CurrentMusic != "worldclear") Sound.Music("worldclear", true); }
        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (t > 120 && (any.P(Btn.A) || any.P(Btn.Start)) || t > 900)
                G.Go(new MapScreen(Math.Min(8, world + 1), true));
        }
        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.VGrad(ppu, 0, 0, 256, 240, 0x080820, 0x302060, 4);
            for (int i = 0; i < 40; i++) { int x = (i * 97 + t / 2) % 256, y = (i * 53) % 180; ppu.FillRect(x, y, 1, 1, (i + t / 8) % 3 == 0 ? 0x30 : 0x28); }
            Hud.Window(ppu, 16, 48, 224, 112);
            string k = Kingdoms[Math.Max(0, Math.Min(7, world - 1))];
            Hud.TxtC(ppu, "WORLD " + world + " CLEAR!", 64, 0x28);
            Hud.TxtC(ppu, "THE " + k + " LANTERN", 88, 0x30);
            Hud.TxtC(ppu, "SHINES AGAIN!", 100, 0x30);
            Hud.TxtC(ppu, "THANK YOU, " + (G.Session.PlayerIndex == 1 ? "LUIGI!" : "MARIO!"), 120, 0x2A);
            if (t > 120 && (t / 16) % 2 == 0) Hud.TxtC(ppu, "PRESS A", 142, 0x10);
            // the recovered lantern, lit again
            int lx = 120, ly = 16;
            if (!Lantern.Draw(ppu, lx, ly, true, t))
            {
                ppu.FillRect(lx, ly + 4, 16, 20, 0x28);
                ppu.FillRect(lx + 2, ly + 6, 12, 16, (t / 6) % 2 == 0 ? 0x38 : 0x30);
                ppu.FillRect(lx + 4, ly, 8, 4, 0x17);
            }
        }
    }

    /// <summary>The story's lantern sprite (scenes.art); returns false when the art isn't available.</summary>
    public static class Lantern
    {
        public static bool Draw(Ppu ppu, int x, int y, bool lit, int t)
        {
            string img = lit ? "lantern.lit" : "lantern.dark";
            if (!Art.Has(img)) return false;
            // a lit lantern flickers between its normal and glow palettes
            ushort[] pal = lit && Art.HasPal("lantern.glow") && (t / 8) % 3 == 0 ? Art.Pal("lantern.glow") : Art.PreviewPalFor(img);
            ppu.Spr(Art.Get(img), x, y, pal);
            return true;
        }
    }

    // ======================================================================== prologue (new games)
    public sealed class StoryScreen : Screen
    {
        readonly Screen next; int t;
        static readonly string[] Lines =
        {
            "EIGHT GREAT LANTERNS LIGHT", "THE EIGHT KINGDOMS OF THE", "MUSHROOM WORLD.", "",
            "BUT BOWSER AND HIS SEVEN", "KOOPALINGS HAVE STOLEN", "THEM, AND DARKNESS IS", "CREEPING ACROSS THE LAND.", "",
            "IT'S UP TO MARIO AND LUIGI", "TO BRING THE LIGHT BACK!",
        };
        public StoryScreen(Screen next) { this.next = next; }
        public override void Enter() { Sound.Music("select", true); }
        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (t > 20 && (any.P(Btn.A) || any.P(Btn.Start) || G.BackPressed) || t > 900) G.Go(next);
        }
        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.VGrad(ppu, 0, 0, 256, 240, 0x04040C, 0x201840, 4);
            for (int i = 0; i < 48; i++) { int x = (i * 97 + 13) % 256, y = (i * 61 + 7) % 200; ppu.FillRect(x, y, 1, 1, (i + t / 12) % 5 == 0 ? 0x30 : Hud.C(0x6060A0)); }
            // text types in line by line
            int shown = Math.Min(Lines.Length, t / 24 + 1);
            for (int i = 0; i < shown; i++) Hud.TxtC(ppu, Lines[i], 40 + i * 12, i >= 9 ? 0x28 : 0x30);
            // the lanterns: lit at first, then they go dark as they're stolen
            for (int i = 0; i < 8; i++)
            {
                int x = 36 + i * 24, y = 180;
                bool lit = t < 40 + i * 6;
                if (Lantern.Draw(ppu, x, y, lit, t + i * 5)) continue;
                ppu.FillRect(x + 5, y + 2, 6, 3, 0x00);
                ppu.FillRect(x + 2, y + 6, 12, 14, 0x00);
                ppu.FillRect(x + 4, y + 8, 8, 10, lit ? ((t / 6 + i) % 2 == 0 ? 0x38 : 0x28) : 0x0F);
            }
            if (t > 60 && (t / 20) % 2 == 0) Hud.TxtC(ppu, "PRESS A", 220, 0x10);
        }
    }

    // ======================================================================== ending & credits
    public sealed class EndingScreen : Screen
    {
        int t;
        const int WalkEnd = 160, StoryEnd = 760, CreditSpeed = 3;   // credits scroll 1 px every CreditSpeed ticks
        static readonly string[] Story =
        {
            "THANK YOU, {0}!", "", "THE LANTERNS SHINE AGAIN", "AND ALL EIGHT KINGDOMS", "ARE BRIGHT ONCE MORE.", "",
            "BOWSER WILL THINK TWICE", "BEFORE HE TRIES THAT AGAIN!",
        };
        static readonly string[] Credits =
        {
            "SUPER MARIO BROS. 4", "THE LANTERN TOUR", "", "", "A FAN-MADE TRIBUTE", "TO SUPER MARIO BROS. 3", "", "",
            "GAME DESIGN, CODE,", "ART AND MUSIC", "MADE WITH CLAUDE", "", "",
            "MARIO AND ALL RELATED", "CHARACTERS ARE PROPERTY", "OF NINTENDO.", "", "THIS IS A NON-COMMERCIAL", "FAN PROJECT.", "", "",
            "THANK YOU FOR PLAYING!", "", "", "", "", "THE END",
        };
        const int Top = 40, Bottom = 164;   // credit scroll window (above the heroes)

        int CreditsDoneT { get { return StoryEnd + (Bottom - Top + Credits.Length * 14 - 60) * CreditSpeed; } }

        public override void Enter() { Sound.Music("ending", true); }
        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (t == StoryEnd && Sound.HasSong("credits")) Sound.Music("credits", true);
            bool press = any.P(Btn.Start) || any.P(Btn.A);
            if (t > CreditsDoneT + 600 || t > CreditsDoneT && press || t > StoryEnd && any.P(Btn.Start) && t < CreditsDoneT) G.Go(new TitleScreen());
        }

        public override void Render(Ppu ppu)
        {
            // night sky, the eight lanterns relit, a castle floor
            ppu.Clear(0x0F);
            Hud.VGrad(ppu, 0, 0, 256, 176, 0x080828, 0x483878, 4);
            for (int i = 0; i < 50; i++) { int x = (i * 83 + 11) % 256, y = (i * 37) % 170; ppu.FillRect(x, y, 1, 1, (i + t / 16) % 4 == 0 ? 0x30 : 0x21); }
            for (int i = 0; i < 8; i++)
            {
                int x = 12 + i * 31, y = 6 + ((i & 1) == 0 ? 0 : 4);
                if (!Lantern.Draw(ppu, x, y, true, t + i * 7)) { ppu.FillRect(x + 2, y + 6, 12, 14, 0x28); ppu.FillRect(x + 4, y + 8, 8, 10, 0x38); }
            }
            // castle floor: brick courses down to the bottom of the screen
            ppu.FillRect(0, 176, 256, 64, Hud.C(0x301008));
            for (int row = 0; row < 8; row++)
                for (int x = (row & 1) * -8; x < 256; x += 16)
                {
                    int y = 176 + row * 8;   // FillRect clips at the left edge
                    Hud.VGrad(ppu, x, y, 15, 7, row == 0 ? 0xF0B060 : 0xC87038, row == 0 ? 0xC87838 : 0x8A4820);
                    ppu.FillRect(x, y, 15, 1, Hud.C(row == 0 ? 0xFFE0A0 : 0xE0985A));
                    ppu.FillRect(x + 14, y, 1, 7, Hud.C(0x602810));
                }

            // Mario walks in to the Princess
            var s = G.Session;
            string hero = s.PlayerIndex == 1 ? "LUIGI" : "MARIO";
            int mx = t < WalkEnd ? -16 + t * 136 / WalkEnd : 120;
            string mimg = t < WalkEnd ? ((t / 6) % 2 == 0 ? "mb.walk1" : "mb.walk2") : "mb.stand";
            Player.Body(ppu, Art.Get(mimg), mx, 144, 32, Art.Pal(s.PlayerIndex == 1 ? "luigi" : "mario"), false, false, false);
            if (Art.Has("princess.1"))
                Art.DrawSplit(ppu, t > WalkEnd && (t / 40) % 3 == 1 ? "princess.2" : "princess.1", "princess", 148, 144, true);
            else ppu.Spr(Art.Get("mb.stand"), 148, 144, Art.P(0x0F, 0x25, 0x37), true);

            if (t >= WalkEnd && t < StoryEnd)
            {
                // the Princess's thanks, typed out
                Hud.Window(ppu, 8, 34, 240, 104);
                int chars = (t - WalkEnd) / 2;
                for (int i = 0; i < Story.Length && chars > 0; i++)
                {
                    string line = string.Format(Story[i], hero);
                    string part = line.Substring(0, Math.Min(line.Length, chars));
                    Hud.TxtC(ppu, part.PadRight(line.Length), 46 + i * 11, i == 0 ? 0x28 : 0x30);
                    chars -= line.Length;
                }
            }
            else if (t >= StoryEnd)
            {
                int ct = ppu.ClipTop, cb = ppu.ClipBottom;
                ppu.ClipTop = Top; ppu.ClipBottom = Bottom;
                int y0 = Bottom - (t - StoryEnd) / CreditSpeed;
                int endY = (Top + Bottom) / 2 - 4;
                for (int i = 0; i < Credits.Length; i++)
                {
                    int y = y0 + i * 14;
                    if (i == Credits.Length - 1) y = Math.Max(y, endY);   // THE END stops in the middle
                    if (y < Top - 8 || y > Bottom) continue;
                    Hud.TxtC(ppu, Credits[i], y, i == Credits.Length - 1 ? 0x28 : i < 2 ? 0x2A : 0x30);
                }
                ppu.ClipTop = ct; ppu.ClipBottom = cb;
            }
            if (t > CreditsDoneT && (t / 24) % 2 == 0) Hud.TxtC(ppu, "PRESS START", 208, 0x10);
        }
    }
}
