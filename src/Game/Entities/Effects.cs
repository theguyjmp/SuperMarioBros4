using System;
using SMB4.Engine;

namespace SMB4.Game
{
    public abstract class Effect : Entity
    {
        protected int t;
        protected Effect(World w, int px, int py) : base(w, px, py) { Class = EntClass.Effect; Hurts = false; UsesSlot = false; }
        public override void OnPlayerTouch(Player p) { }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    /// <summary>Floating "100" / "1UP" numbers.</summary>
    public sealed class ScorePopup : Effect
    {
        readonly string text;
        public ScorePopup(World w, int px, int py, int pts) : base(w, px, py) { text = pts < 0 ? "U" : pts.ToString(); }
        public override void Update() { t++; if (t < 40) Y -= t < 20 ? 16 : 8; if (t > 56) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            int x = Px - camX, y = Py - camY;
            var pal = Art.Pal("fx");
            if (text == "U")
            {
                ppu.Spr(Art.Get("tiny.1"), x, y, pal);
                ppu.Spr(Art.Get("tiny.up"), x + 5, y, pal);
                return;
            }
            for (int i = 0; i < text.Length; i++) ppu.Spr(Art.Get("tiny." + text[i]), x + i * 5, y, pal);
        }
    }

    public sealed class Puff : Effect
    {
        readonly bool small;
        public Puff(World w, int px, int py, bool small = false) : base(w, px, py) { this.small = small; }
        public override void Update() { t++; if (t >= 18) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            string img = "puff." + (1 + Math.Min(2, t / 6));
            if (small) img = "sparkle." + (1 + (t / 4) % 2);
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, Art.Pal("fx"));
        }
    }

    public sealed class SplashFx : Effect
    {
        public SplashFx(World w, int px, int py) : base(w, px, py) { }
        public override void Update() { t++; if (t >= 16) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get(t < 8 ? "splash.1" : "splash.2"), Px - camX, Py - camY, W.ThemePal("water")); }
    }

    public sealed class Dust : Effect
    {
        public Dust(World w, int px, int py) : base(w, px, py) { }
        public override void Update() { t++; Y -= 4; if (t >= 12) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY) { if ((t & 2) == 0 || t < 6) ppu.Spr(Art.Get("dust"), Px - camX, Py - camY, Art.Pal("fx")); }
    }

    public sealed class Sparkle : Effect
    {
        public Sparkle(World w, int px, int py) : base(w, px, py) { }
        public override void Update() { t++; if (t >= 12) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("sparkle." + (1 + (t / 3) % 2)), Px - camX, Py - camY, Art.Pal("fx")); }
    }

    public sealed class Debris : Effect
    {
        public Debris(World w, int px, int py, int xv, int yv) : base(w, px, py) { XVel = xv; YVel = yv; }
        public override void Update()
        {
            t++; X += XVel; Y += YVel; YVel += 4;
            if (Py > W.CamY + 250) Remove = true;
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get("debris"), Px - camX, Py - camY, W.ThemePal("brick"), ((t >> 2) & 1) == 1, ((t >> 3) & 1) == 1);
        }
    }

    public sealed class FlashText : Effect
    {
        readonly string s; readonly int color, life;
        public FlashText(World w, int px, int py, string s, int color, int life) : base(w, px, py) { this.s = s; this.color = color; this.life = life; }
        public override void Update() { t++; if (t > life) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.TextShadow(s, Px - camX, Py - camY, color, 0x0F); }
    }
}
