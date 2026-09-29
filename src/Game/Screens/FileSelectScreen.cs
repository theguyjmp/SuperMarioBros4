using System;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    /// <summary>Three save slots; start/continue or erase.</summary>
    public sealed class FileSelectScreen : Screen
    {
        readonly bool twoPlayer;
        readonly SaveData[] slots = new SaveData[3];
        int sel, t;
        bool confirmErase, eraseMode;
        readonly MenuNav nav = new MenuNav();

        public FileSelectScreen(bool twoPlayer) { this.twoPlayer = twoPlayer; }

        public override void Enter()
        {
            for (int i = 0; i < 3; i++) slots[i] = SaveData.Load(i);
            Sound.Music("select");
        }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (G.BackPressed || any.P(Btn.B) && !eraseMode) { if (confirmErase) confirmErase = false; else G.Go(new TitleScreen()); Sound.Sfx(SfxId.MenuBack); return; }
            if (confirmErase)
            {
                if (any.P(Btn.A) || any.P(Btn.Start))
                {
                    SaveData.Erase(sel); slots[sel] = SaveData.Load(sel);
                    confirmErase = false; eraseMode = false; Sound.Sfx(SfxId.Break);
                }
                return;
            }
            int v = nav.Vertical(any);
            if (v != 0) { sel = (sel + v + 4) % 4; Sound.Sfx(SfxId.MenuMove); }
            if (any.P(Btn.B) && eraseMode) { eraseMode = false; return; }
            if (any.P(Btn.A) || any.P(Btn.Start))
            {
                if (sel == 3) { eraseMode = !eraseMode; Sound.Sfx(SfxId.MenuSelect); sel = eraseMode ? 0 : 3; return; }
                if (eraseMode) { if (slots[sel].Exists) confirmErase = true; else eraseMode = false; return; }
                var s = slots[sel];
                bool fresh = !s.Exists;
                if (fresh)
                {
                    s = new SaveData { Slot = sel, World = 1, TwoPlayer = twoPlayer };
                    s.Save();
                }
                else if (twoPlayer && !s.TwoPlayer) { s.TwoPlayer = true; }
                G.Session = new Session(s);
                Sound.Sfx(SfxId.MapEnter);
                var map = new MapScreen(s.World, true);
                G.Go(fresh ? (Screen)new StoryScreen(map) : map);
            }
        }

        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.Backdrop(ppu, t);
            Hud.Txt(ppu, twoPlayer ? "2 PLAYER GAME" : "1 PLAYER GAME", 76, 20, 0x30);
            Hud.Txt(ppu, eraseMode ? "ERASE WHICH FILE?" : "SELECT A FILE", eraseMode ? 60 : 76, 36, eraseMode ? 0x26 : 0x28);
            for (int i = 0; i < 3; i++)
            {
                int y = 60 + i * 44;
                Hud.Window(ppu, 24, y, 208, 38);
                var s = slots[i];
                Hud.Txt(ppu, "FILE " + (i + 1), 36, y + 8, i == sel ? 0x30 : 0x10);
                if (s.Exists)
                {
                    Hud.Txt(ppu, "WORLD " + s.World, 112, y + 8, 0x30);
                    ppu.Spr(Art.Get("mp.mario1"), 36, y + 18, Art.Pal("mario"));
                    Hud.Txt(ppu, "*" + s.Players[0].Lives, 54, y + 22, 0x30);
                    Hud.Txt(ppu, s.Players[0].Score.ToString().PadLeft(7, '0'), 112, y + 22, 0x10);
                    if (s.Beaten) ppu.Spr(Art.Get("star"), 204, y + 11, Art.Pal("star"));   // game completed
                }
                else Hud.Txt(ppu, "NEW GAME", 112, y + 16, 0x2A);
                if (i == sel) Hud.Cursor(ppu, 14, y + 16, t);
            }
            Hud.Txt(ppu, eraseMode ? "CANCEL" : "ERASE A FILE", 36, 196, sel == 3 ? 0x30 : 0x10);
            if (sel == 3) Hud.Cursor(ppu, 14, 196, t);
            if (confirmErase)
            {
                Hud.Window(ppu, 40, 96, 176, 48);
                Hud.Txt(ppu, "ERASE FILE " + (sel + 1) + "?", 72, 108, 0x30);
                Hud.Txt(ppu, "A: YES   B: NO", 72, 124, 0x10);
            }
        }
    }
}
