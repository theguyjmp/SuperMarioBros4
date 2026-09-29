using System;
using System.IO;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    public abstract class Screen
    {
        public GameMain G;
        public virtual void Enter() { }
        public virtual void Leave() { }
        public abstract void Tick(PadState p1, PadState p2, PadState any);
        public abstract void Render(Ppu ppu);
        public virtual void FocusLost() { }
    }

    /// <summary>Menu cursor helper with key-repeat (independent state per axis, so both can be polled each frame).</summary>
    public sealed class MenuNav
    {
        int heldV, dirV, heldH, dirH;
        public int Vertical(PadState p)
        {
            int d = p.H(Btn.Up) ? -1 : p.H(Btn.Down) ? 1 : 0;
            return Repeat(d, p.P(Btn.Up) || p.P(Btn.Down), ref heldV, ref dirV);
        }
        public int Horizontal(PadState p)
        {
            int d = p.H(Btn.Left) ? -1 : p.H(Btn.Right) ? 1 : 0;
            return Repeat(d, p.P(Btn.Left) || p.P(Btn.Right), ref heldH, ref dirH);
        }
        static int Repeat(int d, bool pressed, ref int held, ref int dirHeld)
        {
            if (d == 0) { held = 0; dirHeld = 0; return 0; }
            if (pressed || d != dirHeld) { dirHeld = d; held = 0; return d; }
            held++;
            if (held > 18 && (held - 18) % 5 == 0) return d;
            return 0;
        }
    }

    /// <summary>Top-level game flow: owns the current screen, fades between screens, the session and save.</summary>
    public sealed class GameMain : IGame
    {
        Screen cur, pending;
        int fade;               // 0..4
        int fadeState;          // 0 idle, 1 fading out, 2 fading in
        int fadeTick;
        bool quit;
        public int Frame;
        public Session Session;
        public bool Focused = true;
        public bool BackPressed;   // Escape / Backspace this tick
        public bool WantsQuit { get { return quit; } }
        int autoExit = -1;

        public GameMain()
        {
            Art.Load();
            string ae = Program.ArgValue("--autoexit");
            int secs; if (ae != null && int.TryParse(ae, out secs)) autoExit = secs * 60;
            string lvl = Program.ArgValue("--level");
            if (lvl != null)
            {
                var save = new SaveData { Slot = 9 };
                Session = new Session(save);
                string form = Program.ArgValue("--form");
                if (form != null) { Form f; if (Enum.TryParse(form, true, out f)) Session.Form = f; }
                cur = new LevelScreen(this, lvl, r => Go(new LevelScreen(this, lvl, null) { TestMode = true }));
                ((LevelScreen)cur).TestMode = true;
            }
            else cur = new TitleScreen();
            cur.G = this;
            cur.Enter();
        }

        public void Quit() { quit = true; }

        public void Go(Screen s, bool fadeOut = true)
        {
            s.G = this;
            if (!fadeOut) { if (cur != null) cur.Leave(); cur = s; cur.Enter(); fade = 0; fadeState = 0; return; }
            pending = s;
            fadeState = 1; fadeTick = 0;
        }

        public void OnFocusChanged(bool focused)
        {
            Focused = focused;
            if (!focused && Host.Settings.PauseOnFocusLoss && cur != null) cur.FocusLost();
        }

        public void Tick(PadState p1, PadState p2)
        {
            Frame++;
            if (autoExit > 0 && Frame >= autoExit) quit = true;
            BackPressed = Host.Input.ConsumeKey(0x1B) | Host.Input.ConsumeKey(0x08);
            if (fadeState == 1)
            {
                if (++fadeTick % 3 == 0) fade++;
                if (fade >= 4)
                {
                    fade = 4;
                    if (cur != null) cur.Leave();
                    cur = pending; pending = null;
                    cur.Enter();
                    fadeState = 2; fadeTick = 0;
                }
                return;
            }
            if (fadeState == 2)
            {
                if (++fadeTick % 3 == 0) fade--;
                if (fade <= 0) { fade = 0; fadeState = 0; }
            }
            var any = p1 | p2;
            if (cur != null) cur.Tick(p1, p2, any);
        }

        public void Render(Ppu ppu)
        {
            ppu.ResetClip();
            if (cur != null) cur.Render(ppu);
            ppu.Fade = fade;
            if (Host.Settings.ShowFps) ppu.Text(((int)Math.Round(Host.MeasuredFps)).ToString(), 232, 2, 0x30);
        }
    }

    // ======================================================================== the level screen
    public sealed class LevelScreen : Screen
    {
        readonly string id;
        readonly Action<LevelResult> done;
        public World W;
        bool paused, resultSent;
        int pauseSel;
        readonly MenuNav nav = new MenuNav();
        public bool TestMode;
        public bool AllowExit;
        int world;

        public LevelScreen(GameMain g, string id, Action<LevelResult> done)
        {
            G = g; this.id = id; this.done = done;
        }

        public override void Enter()
        {
            var def = LevelLoader.Load(id);
            if (def == null) throw new InvalidOperationException("Level not found: " + id);
            W = new World(def, G.Session);
            int n; world = int.TryParse(id.Substring(0, 1), out n) ? n : 1;
        }

        public override void FocusLost() { if (!paused && W.Result == LevelResult.None && W.EndTimer == 0) Pause(); }

        public override void Leave() { W.StopLoopingSounds(); }

        void Pause()
        {
            W.StopLoopingSounds();
            paused = true; pauseSel = 0;
            Sound.PauseMusic(true);
            Sound.Sfx(SfxId.Pause);
        }

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            if (paused)
            {
                int v = nav.Vertical(any);
                int count = AllowExit || TestMode ? 3 : 2;
                if (v != 0) { pauseSel = (pauseSel + v + count) % count; Sound.Sfx(SfxId.MenuMove); }
                if (any.P(Btn.Start) || G.BackPressed || (any.P(Btn.A) && pauseSel == 0))
                {
                    paused = false; Sound.PauseMusic(false); Sound.Sfx(SfxId.Pause);
                }
                else if (any.P(Btn.A) && pauseSel == 1)
                {
                    G.Go(new OptionsScreen(this), false);
                }
                else if (any.P(Btn.A) && pauseSel == 2)
                {
                    paused = false; Sound.PauseMusic(false);
                    resultSent = true;
                    G.Session.Form = W.P.Form;
                    if (done != null) done(LevelResult.Exited);
                }
                return;
            }
            bool lost = Host.Input.ControllerLost; Host.Input.ControllerLost = false;
            if ((any.P(Btn.Start) || G.BackPressed || lost) && W.Result == LevelResult.None && W.EndTimer == 0 && W.P.State != PState.Dying) { Pause(); return; }
            var pad = G.Session.PlayerIndex == 1 ? (p1 | p2) : p1;
            W.Tick(pad);
            if (W.Result != LevelResult.None && !resultSent)
            {
                resultSent = true;
                // the power-up form carries over to the map unless the player died
                if (W.Result != LevelResult.Died && W.Result != LevelResult.TimeUp) G.Session.Form = W.P.PWing ? Form.Raccoon : W.P.Form;
                if (done != null) done(W.Result);
            }
        }

        public override void Render(Ppu ppu)
        {
            W.Render(ppu);
            Hud.Draw(ppu, G.Session, world, W.P.Power, W.P.FlyTime > 0 || W.P.Power == 0x7F, W.Time, W.Frame, W.Def.Time > 0);
            if (W.Banner != "")
            {
                ppu.TextShadow(W.Banner, (256 - W.Banner.Length * 8) / 2, 48, 0x30, 0x0F);
                if (W.Banner2 != "")
                {
                    int x = (256 - W.Banner2.Length * 8) / 2 - (W.BannerCard >= 0 ? 12 : 0);
                    ppu.TextShadow(W.Banner2, x, 68, 0x30, 0x0F);
                    if (W.BannerCard >= 0) ppu.Spr(Art.Get(GoalBox.CardName(W.BannerCard)), x + W.Banner2.Length * 8 + 8, 64, GoalBox.CardPal(W.BannerCard));
                }
            }
            if (paused)
            {
                int count = AllowExit || TestMode ? 3 : 2;
                Hud.Window(ppu, 72, 64, 112, 24 + count * 12);
                ppu.Text("PAUSED", 104, 72, 0x30);
                string[] items = { "CONTINUE", "OPTIONS", "EXIT LEVEL" };
                for (int i = 0; i < count; i++)
                {
                    ppu.Text(items[i], 96, 88 + i * 12, i == pauseSel ? 0x30 : 0x10);
                    if (i == pauseSel) ppu.Text(">", 84, 88 + i * 12, 0x28);
                }
            }
            if (Host.Settings.ShowInputDisplay) InputDisplay(ppu, W.P.Pad);
        }

        static void InputDisplay(Ppu ppu, PadState p)
        {
            int x = 200, y = 172;
            ppu.FillRect(x, y, 52, 18, 0x0F);
            ppu.FillRect(x + 8, y + 3, 4, 12, p.H(Btn.Up) || p.H(Btn.Down) ? 0x10 : 0x00);
            ppu.FillRect(x + 4, y + 7, 12, 4, 0x00);
            if (p.H(Btn.Up)) ppu.FillRect(x + 8, y + 3, 4, 4, 0x30);
            if (p.H(Btn.Down)) ppu.FillRect(x + 8, y + 11, 4, 4, 0x30);
            if (p.H(Btn.Left)) ppu.FillRect(x + 4, y + 7, 4, 4, 0x30);
            if (p.H(Btn.Right)) ppu.FillRect(x + 12, y + 7, 4, 4, 0x30);
            ppu.FillRect(x + 30, y + 6, 7, 7, p.H(Btn.B) ? 0x16 : 0x06);
            ppu.FillRect(x + 41, y + 6, 7, 7, p.H(Btn.A) ? 0x16 : 0x06);
        }
    }
}
