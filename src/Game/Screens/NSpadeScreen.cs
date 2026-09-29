using System;
using System.Collections.Generic;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    /// <summary>
    /// SMB3's N-Spade: 18 face-down cards (9 pairs). Flip two at a time; a pair pays out its prize, and the
    /// game ends after the second miss or when every pair is found. The panel appears on the map every 80,000 points.
    /// </summary>
    public sealed class NSpadeScreen : Screen
    {
        enum Face { Mushroom, Flower, Star, OneUp, Coins10, Coins20 }
        const int Cols = 6, Rows = 3, CardW = 32, CardH = 40, GapX = 6, GapY = 8;
        const int X0 = (256 - (Cols * CardW + (Cols - 1) * GapX)) / 2, Y0 = 34;

        readonly MapScreen map; readonly int nodeX, nodeY;
        readonly Face[] cards = new Face[Cols * Rows];
        readonly bool[] faceUp = new bool[Cols * Rows];
        readonly MenuNav nav = new MenuNav();
        int cur, first = -1, second = -1, showT, misses, found, t, doneT;
        bool done, perfect;
        string msg = ""; int msgT;

        public NSpadeScreen(MapScreen map, int nodeX, int nodeY) { this.map = map; this.nodeX = nodeX; this.nodeY = nodeY; }

        public override void Enter()
        {
            var deck = new List<Face>
            {
                Face.Mushroom, Face.Mushroom, Face.Mushroom, Face.Mushroom, Face.Flower, Face.Flower, Face.Flower, Face.Flower,
                Face.Star, Face.Star, Face.Star, Face.Star, Face.OneUp, Face.OneUp, Face.Coins10, Face.Coins10, Face.Coins20, Face.Coins20,
            };
            var rng = new Random(Environment.TickCount);
            for (int i = 0; i < cards.Length; i++) { int k = rng.Next(deck.Count); cards[i] = deck[k]; deck.RemoveAt(k); }
            Sound.Music("bonus", true);
        }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (msgT > 0) msgT--;
            if (done)
            {
                if (++doneT > 200 || doneT > 40 && (any.P(Btn.A) || any.P(Btn.Start)))
                {
                    map.NSpadeDone(nodeX, nodeY);
                    G.Go(map);
                }
                return;
            }
            if (second >= 0)
            {
                // both cards are showing: resolve after a short look
                if (--showT > 0) return;
                if (cards[first] == cards[second])
                {
                    Award(cards[first]);
                    if (++found == cards.Length / 2) Finish(true);
                }
                else
                {
                    faceUp[first] = faceUp[second] = false;
                    if (++misses >= 2) Finish(false);
                    else { Sound.Sfx(SfxId.Error); msg = "ONE MORE MISS AND IT'S OVER!"; msgT = 90; }
                }
                first = second = -1;
                return;
            }
            int h = nav.Horizontal(any), v = nav.Vertical(any);
            if (h != 0 || v != 0)
            {
                int col = (cur % Cols + h + Cols) % Cols, row = (cur / Cols + v + Rows) % Rows;
                cur = row * Cols + col;
                Sound.Sfx(SfxId.MenuMove);
            }
            if (any.P(Btn.A))
            {
                if (faceUp[cur]) { Sound.Sfx(SfxId.Error); return; }
                faceUp[cur] = true;
                Sound.Sfx(SfxId.CardStop);
                if (first < 0) first = cur;
                else { second = cur; showT = 36; }
            }
        }

        void Award(Face f)
        {
            var s = G.Session;
            switch (f)
            {
                case Face.Mushroom: AddItem(Item.Mushroom); break;
                case Face.Flower: AddItem(Item.Flower); break;
                case Face.Star: AddItem(Item.Star); break;
                case Face.OneUp: s.AddLife(); Sound.Sfx(SfxId.OneUp); msg = "1UP!"; msgT = 90; break;
                case Face.Coins10:
                case Face.Coins20:
                {
                    int n = f == Face.Coins10 ? 10 : 20;
                    bool life = false;
                    for (int i = 0; i < n; i++) life |= s.AddCoin();
                    Sound.Sfx(life ? SfxId.OneUp : SfxId.Coin);
                    msg = n + " COINS!"; msgT = 90;
                    break;
                }
            }
        }

        void AddItem(Item it)
        {
            var items = G.Session.Cur.Items;
            if (items.Count < 28) items.Add(it);
            Sound.Sfx(SfxId.PowerUp);
            msg = MapScreen.ItemName(it) + "!"; msgT = 90;
        }

        void Finish(bool all)
        {
            done = true; perfect = all; doneT = 0;
            for (int i = 0; i < faceUp.Length; i++) faceUp[i] = true;   // reveal everything, like SMB3
            if (all) Sound.Music("bonus1up", true); else Sound.Sfx(SfxId.Error);
            G.Session.Save.Save();
        }

        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            // green felt card table with a soft vignette
            Hud.VGrad(ppu, 0, 0, 256, 192, 0x106030, 0x04200C, 2);
            for (int y = 4; y < 192; y += 8) for (int x = (y / 8) % 2 * 8; x < 256; x += 16) ppu.FillRect(x, y, 1, 1, Hud.Mix(0x30A050, 0x104820, y * 256 / 192));
            Hud.TxtC(ppu, "N-SPADE: FIND THE PAIRS!", 12, 0x30);
            for (int i = 0; i < cards.Length; i++)
            {
                int x = X0 + (i % Cols) * (CardW + GapX), y = Y0 + (i / Cols) * (CardH + GapY);
                DrawCard(ppu, i, x, y);
                if (!done && i == cur && (t / 8) % 3 != 0) Hud.Box(ppu, x - 2, y - 2, CardW + 4, CardH + 4, 0x28);
            }
            string line = done ? (perfect ? "PERFECT! ALL PAIRS FOUND!" : "TOO BAD! 2 MISSES.") : msgT > 0 ? msg : "MISSES LEFT: " + (2 - misses);
            Hud.TxtC(ppu, line, 180, done ? (perfect ? 0x28 : 0x16) : msgT > 0 ? 0x2A : 0x30);
            Hud.Draw(ppu, G.Session, G.Session.Save.World, 0, false, 0, t, false);
        }

        void DrawCard(Ppu ppu, int i, int x, int y)
        {
            ppu.FillRect(x + 2, y + CardH, CardW - 1, 2, Hud.C(0x021008));   // drop shadow
            ppu.FillRect(x + CardW, y + 2, 2, CardH - 1, Hud.C(0x021008));
            if (!faceUp[i])
            {
                // card back: bevelled orange card with a navy spade panel
                Hud.Panel(ppu, x, y, CardW, CardH, 0xFFB050, 0xD06818, 0xFFE0A0, 0x803008, 0x200800);
                Hud.Panel(ppu, x + 3, y + 3, CardW - 6, CardH - 6, 0xE88830, 0xB85010, 0xFFC878, 0x803008, 0x582000);
                ppu.Spr(Art.Get("m.spade"), x + 8, y + 12, Art.PN(Hud.C(0x100818), Hud.C(0xF8F0E0), Hud.C(0xFFFFFF), Hud.C(0xC8C0B0),
                    Hud.C(0x807868), Hud.C(0xFFFFFF), Hud.C(0xE83020), Hud.C(0x901010), Hud.C(0x182048), Hud.C(0x5068B0)));
                return;
            }
            Hud.Panel(ppu, x, y, CardW, CardH, 0xFFFFFF, 0xC8D0E8, 0xFFFFFF, 0x8088A8, 0x101020);
            int ix = x + 8, iy = y + 8;
            switch (cards[i])
            {
                case Face.Mushroom: ppu.Spr(Art.Get("mushroom"), ix, iy, Art.Pal("mushroom")); break;
                case Face.Flower: ppu.Spr(Art.Get("flower.1"), ix, iy, Art.Pal("flower")); break;
                case Face.Star: ppu.Spr(Art.Get("star"), ix, iy, Art.Pal("star")); break;
                case Face.OneUp: ppu.Spr(Art.Get("mushroom"), ix, iy, Art.Pal("oneup")); Hud.GradText(ppu, "1UP", x + 4, y + 28, Hud.C(0x50D040), Hud.C(0x209020), Hud.C(0x106010), -1); break;
                case Face.Coins10: ppu.Spr(Art.Get("coin.1"), ix, iy, Art.Pal("coin")); Hud.GradText(ppu, "10", x + 8, y + 28, Hud.C(0x404058), Hud.C(0x202030), Hud.C(0x101018), -1); break;
                case Face.Coins20: ppu.Spr(Art.Get("coin.1"), ix, iy, Art.Pal("coin")); Hud.GradText(ppu, "20", x + 8, y + 28, Hud.C(0x404058), Hud.C(0x202030), Hud.C(0x101018), -1); break;
            }
        }
    }
}
