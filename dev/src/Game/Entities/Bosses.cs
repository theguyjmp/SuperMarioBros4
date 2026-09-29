using System;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    public abstract class Boss : Entity
    {
        /// <summary>A named full palette from bosses.art (hurt flashes, ring colors), with a plain white fallback.</summary>
        public static ushort[] Flash(string name) { return Art.HasPal(name) ? Art.Pal(name) : Art.P(0x0F, 0x30, 0x30); }
        protected bool active;
        protected int hurt;          // invulnerable spin/hurt timer
        protected int hurtFlash;     // brief white flash when a projectile lands (no invulnerability)
        protected int t;
        protected Boss(World w, int px, int py) : base(w, px, py) { Class = EntClass.Enemy; UsesSlot = false; Points = 0; }

        protected void Activate(string music)
        {
            if (active) return;
            active = true;
            W.BossArena = true;
            Sound.Music(music, true);
        }

        protected bool PlayerNear(int dist) { return Math.Abs(W.P.CenterX - Cx) < dist; }

        protected void Defeated(LevelResult r, int orbX, int orbY)
        {
            KnockOff(Facing);
            Sound.Sfx(SfxId.BossHit);
            Sound.Music("bosswin", true);
            W.AddScore(10000, Px, Py);
            W.Add(new Orb(W, orbX, orbY, r));
            W.MarkKilled(this);
        }
    }

    // ======================================================================== Boom Boom (fortress)
    public sealed class BoomBoom : Boss
    {
        int stomps = 3, fireHits = 5, jumps;
        public BoomBoom(World w, int px, int py) : base(w, px - 8, py - 16)
        {
            Wd = 32; Ht = 32; HbX = 5; HbY = 6; HbW = 22; HbH = 26; StompH = 19;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++; Anim++;
            var p = W.P;
            if (!active) { if (PlayerNear(170)) Activate("boss"); else return; }
            if (hurt > 0)
            {
                hurt--;
                XVel = (hurt / 16) % 2 == 0 ? 0x28 : -0x28;
                MoveWalker(false, true);
                return;
            }
            Facing = FaceToward(p);
            int target = Facing * 0x14;
            if (XVel < target) XVel += 2; else if (XVel > target) XVel -= 2;
            if (OnGround && t % 80 == 0)
            {
                jumps++;
                YVel = jumps % 3 == 0 ? -0x58 : -0x38;
                OnGround = false;
            }
            MoveWalker(false, true);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (hurt == 0 && W.CanStomp(p, this))
            {
                p.Bounce(Phys.StompBounce);
                Sound.Sfx(SfxId.BossHit);
                if (--stomps <= 0) { Defeated(LevelResult.FortressCleared, W.CamX + 120, W.CamY + 40); return; }
                hurt = 90;
                return;
            }
            p.Hurt();
        }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (hurt > 0) return false;
            if (d == Dmg.Fire || d == Dmg.Hammer || d == Dmg.Tail)
            {
                Sound.Sfx(SfxId.BossHit);
                if (--fireHits <= 0) { Defeated(LevelResult.FortressCleared, W.CamX + 120, W.CamY + 40); return true; }
                hurt = 30;
                return false;
            }
            if (d == Dmg.Star) { Defeated(LevelResult.FortressCleared, W.CamX + 120, W.CamY + 40); return true; }
            return false;
        }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("boomboom");
            if (Dying) { DrawKnocked(ppu, Art.Get("boomboom.hurt"), pal, camX, camY); return; }
            string img = hurt > 0 ? "boomboom.hurt" : !OnGround ? "boomboom.jump" : (Anim / 6) % 2 == 0 ? "boomboom.run1" : "boomboom.run2";
            if (!active) img = "boomboom.stand";
            if (hurt > 0 && (hurt & (W.ReduceFlashing ? 8 : 2)) != 0) pal = Flash("boomboom.flash");
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    // ======================================================================== Koopalings (airship cabins)
    public sealed class Koopaling : Boss
    {
        readonly int variant;      // world 1..7
        int stomps = 3, fireHits = 10, castT, stunLand;
        static readonly string[] Names = { "LEMMY", "ROY", "IGGY", "WENDY", "MORTON", "LARRY", "LUDWIG" };
        // full 15-color body palettes per Koopaling live in bosses.art (koopaling.lemmy ... koopaling.ludwig):
        // slots 2-5 = shell (each Koopaling's signature colour), 6-9 skin, 10-12 ivory, 13 eye, 14 mouth
        static ushort[] BodyPal(int variant)
        {
            string n = "koopaling." + NameFor(variant).ToLowerInvariant();
            return Art.HasPal(n) ? Art.Pal(n) : Art.Pal("koopaling");
        }
        public static string NameFor(int world) { return Names[Math.Max(0, Math.Min(6, world - 1))]; }

        public Koopaling(World w, int px, int py, int world) : base(w, px, py - 16)
        {
            variant = Math.Max(1, Math.Min(7, world));
            Wd = 16; Ht = 32; HbX = 2; HbY = 8; HbW = 12; HbH = 24;
            castT = 60;
        }
        int Speed { get { return 0x0C + variant; } }
        bool HeavyLander { get { return variant == 2 || variant == 5 || variant == 7; } }

        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++; Anim++;
            if (hurtFlash > 0) hurtFlash--;
            var p = W.P;
            if (!active) { if (PlayerNear(180)) Activate("boss"); else return; }
            if (hurt > 0)
            {
                hurt--;
                XVel = Facing * 0x30;
                bool wall = MoveWalker(false, true);
                if (wall) Facing = -Facing;
                return;
            }
            Facing = FaceToward(p);
            int target = Facing * Speed;
            if (Math.Abs(p.CenterX - Cx) < 20) target = 0;
            if (XVel < target) XVel++; else if (XVel > target) XVel--;
            if (OnGround && t % (70 - variant * 3) == 0)
            {
                YVel = HeavyLander ? -0x60 : -0x48 - variant * 2;
                OnGround = false;
            }
            bool wasAir = !OnGround;
            MoveWalker(false, true);
            if (wasAir && OnGround && HeavyLander)
            {
                Sound.Sfx(SfxId.Thwomp); W.Shake(12);
                if (!p.InAir) p.Stun = 40;
            }
            if (--castT <= 0)
            {
                castT = 110 - variant * 6;
                int dx = p.CenterX - Cx, dy = p.Py + 20 - (Py + 8);
                double len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                int sp = 0x14 + variant;
                W.Add(new MagicRing(W, Cx - 4, Py + 6, (int)(dx / len * sp), (int)(dy / len * sp), variant == 4));
                Sound.Sfx(SfxId.Magic);
            }
        }
        public override void OnPlayerTouch(Player p)
        {
            if (hurt == 0 && W.CanStomp(p, this))
            {
                p.Bounce(Phys.StompBounce);
                Sound.Sfx(SfxId.BossHit);
                if (--stomps <= 0) { Defeated(LevelResult.WorldCleared, Cx - 8, Py); return; }
                hurt = 80;
                return;
            }
            p.Hurt();
        }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (hurt > 0) return false;
            if (d == Dmg.Fire || d == Dmg.Hammer || d == Dmg.Tail)
            {
                Sound.Sfx(SfxId.BossHit);
                if (--fireHits <= 0) { Defeated(LevelResult.WorldCleared, Cx - 8, Py); return true; }
                hurtFlash = 10;
                return false;
            }
            if (d == Dmg.Star) { Defeated(LevelResult.WorldCleared, Cx - 8, Py); return true; }
            return false;
        }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = BodyPal(variant);
            if (Dying) { DrawKnocked(ppu, Art.Get("koopaling.shell"), pal, camX, camY); return; }
            string img = hurt > 0 ? "koopaling.shell" : castT < 12 ? "koopaling.cast" : !OnGround ? "koopaling.jump" : Math.Abs(XVel) > 2 && (Anim / 8) % 2 == 0 ? "koopaling.walk" : "koopaling.stand";
            if (hurtFlash > 0 && (hurtFlash & 2) != 0 && !W.ReduceFlashing) pal = Flash("koopaling.flash");
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, pal, Facing < 0);
            // each Koopaling's signature look (hair, bow, glasses...) over the shared body; hidden inside the shell
            string acc = "kl." + NameFor(variant).ToLowerInvariant();
            if (img != "koopaling.shell" && Art.Has(acc))
                ppu.Spr(Art.Get(acc), Px - camX, Py - camY, Art.PreviewPalFor(acc), Facing < 0);
            if (img == "koopaling.cast" && Art.Has("wand")) ppu.Spr(Art.Get("wand"), Px - camX + (Facing > 0 ? 12 : -4), Py - camY + 6, Art.HasPal("wand") ? Art.Pal("wand") : pal, Facing < 0);
        }
    }

    public sealed class MagicRing : Entity
    {
        int t; readonly bool bounce;
        public MagicRing(World w, int px, int py, int vx, int vy, bool bounce) : base(w, px, py)
        {
            Class = EntClass.EnemyProjectile; UsesSlot = false; Wd = 8; Ht = 8; HbX = 1; HbY = 1; HbW = 6; HbH = 6; XVel = vx; YVel = vy; this.bounce = bounce;
            StarImmune = true; Stompable = false;
        }
        public override void Update()
        {
            t++; X += XVel; Y += YVel;
            if (bounce)
            {
                if (SolidAt(Px + 4, Py) || SolidAt(Px + 4, Py + 8)) YVel = -YVel;
                if (SolidAt(Px, Py + 4) || SolidAt(Px + 8, Py + 4)) XVel = -XVel;
                if (t > 400) Remove = true;
            }
            else if (t > 300) Remove = true;
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Boss.Flash((t / 3) % 2 == 0 ? "ring.a" : "ring.b");
            ppu.Spr(Art.Get((t / 4) % 2 == 0 ? "ring.1" : "ring.2"), Px - camX, Py - camY, pal);
        }
    }

    // ======================================================================== Bowser (final battle)
    public sealed class Bowser : Boss
    {
        int fireHits = 24, state, breathT = 120, jumpT = 200, fallTimer;
        bool fell;
        public Bowser(World w, int px, int py) : base(w, px - 8, py - 24)
        {
            Wd = 32; Ht = 40; HbX = 4; HbY = 8; HbW = 24; HbH = 32; Stompable = false; FireImmune = false; TailImmune = true; ShellImmune = true;
        }
        public override void Update()
        {
            if (fell)
            {
                Y += YVel; YVel = Math.Min(0x40, YVel + 3);
                if (++fallTimer == 90) { Sound.Music("bosswin", true); }
                if (fallTimer == 260) W.Result = LevelResult.GameCleared;
                return;
            }
            if (UpdateKnocked()) { if (Py > W.CamY + 240 && W.Result == LevelResult.None) { fell = true; fallTimer = 0; } return; }
            t++; Anim++;
            if (hurtFlash > 0) hurtFlash--;
            var p = W.P;
            if (!active) { if (PlayerNear(190)) Activate("bowser"); else return; }
            Facing = FaceToward(p);
            switch (state)
            {
                case 0: // walk & breathe fire
                    XVel = Math.Abs(p.CenterX - Cx) > 40 ? Facing * 0x08 : 0;
                    if (--breathT <= 0)
                    {
                        breathT = 140;
                        int fy = p.Py + (p.Big ? 8 : 20);
                        W.Add(new BowserFire(W, Facing > 0 ? Px + 28 : Px - 20, Py + 8, Facing, fy));
                        Sound.Sfx(SfxId.BowserFire);
                        anim = 20;
                    }
                    if (--jumpT <= 0 && OnGround) { state = 1; YVel = -0x70; OnGround = false; jumpT = 220; }
                    MoveWalker(false, true);
                    break;
                case 1: // leap toward the player, then pound
                    XVel = Math.Sign(p.CenterX - Cx) * 0x14;
                    if (Math.Abs(p.CenterX - Cx) < 8) XVel = 0;
                    bool air = !OnGround;
                    MoveWalker(false, true);
                    if (YVel >= 0 && air && Math.Abs(p.CenterX - Cx) < 12) { YVel = 0x40; }
                    if (air && OnGround)
                    {
                        state = 2; t = 0;
                        Sound.Sfx(SfxId.Thwomp); W.Shake(20);
                        Host.Input.Rumble(W.S.PlayerIndex, 0.8f, 16);
                        if (!p.InAir) p.Stun = 30;
                        BreakFloor();
                    }
                    break;
                case 2:
                    XVel = 0;
                    MoveWalker(false, true);
                    if (!OnGround && Py > W.CamY + 150) { Dying = true; KnockTimer = 1; Hurts = false; YVel = 0x10; XVel = 0; W.MarkKilled(this); Sound.Sfx(SfxId.BossHit); }
                    if (t > 50) state = 0;
                    break;
            }
            if (anim > 0) anim--;
        }
        int anim;

        void BreakFloor()
        {
            int ty = World.FloorDiv(Py + Ht, 16);
            for (int x = Px + 2; x < Px + Wd - 2; x += 8)
            {
                int tx = World.FloorDiv(x, 16);
                if (W.TileAt(tx, ty) == T.Brick) W.BreakBrick(tx, ty);
            }
        }

        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (d == Dmg.Fire || d == Dmg.Hammer)
            {
                Sound.Sfx(SfxId.BossHit);
                if (--fireHits <= 0) { KnockOff(dir); fell = false; W.MarkKilled(this); return true; }
                hurtFlash = 10;
                return false;
            }
            if (d == Dmg.Star) return false;
            return false;
        }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            // one 15-color palette: mane, skin, shell, belly and horns each have their own slots
            var pal = Art.Pal("bowser");
            if (hurtFlash > 0 && (hurtFlash & 2) != 0 && !W.ReduceFlashing) pal = Flash("bowser.flash");
            string img = anim > 0 ? "bowser.breath" : !OnGround ? "bowser.jump" : Math.Abs(XVel) > 0 && (Anim / 10) % 2 == 0 ? "bowser.walk" : "bowser.stand";
            if (Dying || fell) { ppu.Spr(Art.Get("bowser.jump"), Px - camX, Py - camY, pal, Facing < 0, true); return; }
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    public sealed class BowserFire : Entity
    {
        int t; readonly int targetY;
        public BowserFire(World w, int px, int py, int dir, int targetY) : base(w, px, py)
        {
            Class = EntClass.EnemyProjectile; UsesSlot = false; Wd = 24; Ht = 8; HbX = 2; HbY = 1; HbW = 20; HbH = 6; Facing = dir; XVel = dir * 0x18; this.targetY = targetY;
            StarImmune = true; Stompable = false;
        }
        public override void Update()
        {
            t++; X += XVel;
            if (Py < targetY) Y += 8; else if (Py > targetY) Y -= 8;
            if (t > 400) Remove = true;
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get((t / 4) % 2 == 0 ? "bowserfire.1" : "bowserfire.2"), Px - camX, Py - camY, Art.Pal("bowserfire"), Facing < 0); }
    }
}
