using System;
using SMB4.Audio;
using SMB4.Engine;

namespace SMB4.Game
{
    // ======================================================================== Goomba / Paragoomba
    public sealed class Goomba : Entity
    {
        bool flat, winged; int flatT, hopT, hops;
        public Goomba(World w, int px, int py, bool winged) : base(w, px, py)
        {
            this.winged = winged;
            Facing = w.P.CenterX < px + 8 ? -1 : 1;
            XVel = Facing * 0x08;
            HbX = 2; HbY = 4; HbW = 12; HbH = 12;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            if (flat) { if (++flatT > 30) Remove = true; return; }
            Anim++;
            if (winged && OnGround)
            {
                if (++hopT > (hops < 3 ? 8 : 40))
                {
                    hopT = 0; hops = (hops + 1) % 4;
                    YVel = hops == 3 ? -0x30 : -0x20;
                    Facing = FaceToward(W.P);
                    OnGround = false;
                }
            }
            if (winged)
            {
                int target = FaceToward(W.P) * 0x14;
                if (XVel < target) XVel++; else if (XVel > target) XVel--;
            }
            MoveWalker(false);
            if (XVel != 0) Facing = Math.Sign(XVel);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (flat) return;
            if (W.CanStomp(p, this))
            {
                W.StompBounce(p, this);
                if (winged) { winged = false; XVel = Facing * 0x08; }
                else { flat = true; Hurts = false; W.MarkKilled(this); }
            }
            else p.Hurt();
        }
        public override bool TakeHit(Dmg d, int dir) { if (flat) return false; KnockOff(dir); return true; }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("goomba");
            int sx = Px - camX, sy = Py - camY;
            if (Dying) { DrawKnocked(ppu, Art.Get("goomba.1"), pal, camX, camY); return; }
            if (flat) { ppu.Spr(Art.Get("goomba.flat"), sx, sy + 8, pal); return; }
            bool f2 = (Anim / 8) % 2 == 1;
            if (winged)
            {
                // wings behind the body: the art is the left wing (root at its lower right), mirrored for the right
                string wimg = OnGround ? ((Anim / 8) % 2 == 0 ? "wing.1" : "wing.2") : ((Anim / 3) % 2 == 0 ? "wing.1" : "wing.2");
                var wpal = Art.Pal("wing");
                ppu.Spr(Art.Get(wimg), sx - 4, sy - 2, wpal, false);
                ppu.Spr(Art.Get(wimg), sx + 12, sy - 2, wpal, true);
            }
            // SMB3 walk: one front-facing frame, mirrored every 8 ticks (only the feet differ)
            ppu.Spr(Art.Get("goomba.1"), sx, sy, pal, f2);
        }
    }

    // ======================================================================== Koopa Troopa / Paratroopa
    public sealed class Koopa : Entity
    {
        public bool Red; int wingMode; readonly int ox, oy; int t;
        public Koopa(World w, int px, int py, bool red, int wingMode) : base(w, px, py - 8)
        {
            Red = red; this.wingMode = wingMode;
            Ht = 24; HbX = 2; HbY = 10; HbW = 12; HbH = 14;
            Facing = w.P.CenterX < px + 8 ? -1 : 1;
            XVel = wingMode == 2 ? 0 : Facing * 0x08;
            ox = X; oy = Y;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            Anim++; t++;
            if (wingMode == 2)
            {
                // red paratroopa: flies up and down around its spawn point
                Y = oy + (int)(Math.Sin(t * 2 * Math.PI / 180.0) * 40 * 16);
                Facing = FaceToward(W.P);
                return;
            }
            if (wingMode == 3)
            {
                X = ox + (int)(Math.Sin(t * 2 * Math.PI / 240.0) * 48 * 16);
                Facing = Math.Cos(t * 2 * Math.PI / 240.0) > 0 ? 1 : -1;
                return;
            }
            if (wingMode == 1 && OnGround) { YVel = -0x30; OnGround = false; }
            MoveWalker(Red && wingMode == 0);
            if (XVel != 0) Facing = Math.Sign(XVel);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (W.CanStomp(p, this))
            {
                W.StompBounce(p, this);
                if (wingMode != 0) { wingMode = 0; XVel = Facing * 0x08; YVel = 0; return; }
                var s = new Shell(W, Px, Py + 8, Red ? ShellKind.Red : ShellKind.Green, false);
                s.SpawnIndex = SpawnIndex; SpawnIndex = -1;
                W.Add(s);
                Remove = true;
            }
            else p.Hurt();
        }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (d == Dmg.Tail || d == Dmg.Bump)
            {
                var s = new Shell(W, Px, Py + 8, Red ? ShellKind.Red : ShellKind.Green, true);
                s.SpawnIndex = SpawnIndex; SpawnIndex = -1;
                s.YVel = -0x30; s.XVel = dir * 0x08;
                W.Add(s);
                Remove = true;
                W.AddScore(100, Px, Py);
                return true;
            }
            KnockOff(dir); return true;
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal(Red ? "koopa.red" : "koopa.green");
            if (Dying) { DrawKnocked(ppu, Art.Get("shell.1"), pal, camX, camY, 8); return; }
            int sx = Px - camX, sy = Py - camY;
            bool f2 = (Anim / 8) % 2 == 1;
            ppu.Spr(Art.Get(f2 ? "koopa.2" : "koopa.1"), sx, sy, pal, Facing < 0);
            if (wingMode != 0)
            {
                string wimg = (Anim / 4) % 2 == 0 ? "wing.1" : "wing.2";
                ppu.Spr(Art.Get(wimg), Facing > 0 ? sx - 2 : sx + 10, sy + 4, Art.Pal("wing"), Facing < 0);
            }
        }
    }

    public enum ShellKind { Green, Red, Buzzy }

    // ======================================================================== shells (kickable, carryable)
    public sealed class Shell : Entity
    {
        public ShellKind Kind; public bool Flipped;
        int wakeT, kickGrace;
        public Shell(World w, int px, int py, ShellKind kind, bool flipped) : base(w, px, py)
        {
            Kind = kind; Flipped = flipped;
            HbX = 2; HbY = 3; HbW = 12; HbH = 13;
            Carryable = true;
            FireImmune = kind == ShellKind.Buzzy;
            Hurts = false;
            Points = 100;
        }
        public bool Moving { get { return XVel != 0 && W.P.Carrying != this; } }

        public void Kick(int dir, int playerXVel)
        {
            XVel = dir * 0x30 + (Math.Sign(playerXVel) == dir ? playerXVel / 2 : 0);
            IsShell = true; Hurts = true; ShellChain = 0; kickGrace = 16; wakeT = 0;
        }

        public override void Update()
        {
            if (UpdateKnocked()) return;
            Anim++;
            if (kickGrace > 0) kickGrace--;
            var p = W.P;
            if (p.Carrying == this)
            {
                X = (p.Px + (p.Facing > 0 ? 11 : -11)) << 4;
                Y = (p.Py + (p.Big && !p.Ducking ? 13 : 17)) << 4;
                XVel = 0; YVel = 0;
                if (++wakeT > 420) WakeUp(true);
                return;
            }
            if (XVel == 0) { IsShell = false; Hurts = false; if (++wakeT > 420) { WakeUp(false); return; } }
            bool hitWall = MoveWalker(false, true);
            if (hitWall && XVel != 0)
            {
                Sound.Sfx(SfxId.Ricochet);
                // XVel is already reversed: the wall we hit is on the other side
                int bx = World.FloorDiv(XVel > 0 ? Px - 3 : Px + Wd + 2, 16);
                int ty = World.FloorDiv(Py + 8, 16);
                if (Px + 8 - W.CamX > -16 && Px - W.CamX < 272)
                {
                    T bt = W.TileAt(bx, ty);
                    if (TileInfo.Bumpable(bt)) W.HitBlock(bx, ty, true, 0, false);
                }
            }
            if (OnGround && XVel == 0 && YVel == 0 && Flipped) { }
        }

        void WakeUp(bool carried)
        {
            if (Kind == ShellKind.Buzzy) { var b = new Buzzy(W, Px, Py); b.SpawnIndex = SpawnIndex; W.Add(b); }
            else { var k = new Koopa(W, Px, Py, Kind == ShellKind.Red, 0); k.SpawnIndex = SpawnIndex; W.Add(k); }
            SpawnIndex = -1;
            Remove = true;
            if (carried) { W.P.Carrying = null; W.P.Hurt(); }
        }

        public override void OnPlayerTouch(Player p)
        {
            if (p.Carrying == this) return;
            if (!Moving)
            {
                if (p.Pad.H(SMB4.Platform.Btn.B) && p.Carrying == null && p.Statue == 0 && !p.Swimming)
                {
                    p.Carrying = this; wakeT = Math.Min(wakeT, 300);
                    return;
                }
                bool fromAbove = W.CanStomp(p, this);
                int dir = p.CenterX < Cx ? 1 : -1;
                Kick(dir, 0);
                p.KickPose = Phys.KickPose;
                if (fromAbove) { p.Bounce(Phys.StompBounce); }
                Sound.Sfx(SfxId.Kick);
                W.AddScore(100, Px, Py);
                return;
            }
            if (kickGrace > 0) return;
            if (W.CanStomp(p, this))
            {
                W.StompBounce(p, this);
                XVel = 0; IsShell = false; Hurts = false; wakeT = 0;
                return;
            }
            p.Hurt();
        }

        public override bool TakeHit(Dmg d, int dir)
        {
            if (d == Dmg.Fire && FireImmune) return false;
            if (d == Dmg.Tail || d == Dmg.Bump) { Flipped = !Flipped; YVel = -0x30; XVel = 0; IsShell = false; wakeT = 0; return true; }
            KnockOff(dir); return true;
        }

        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal(Kind == ShellKind.Red ? "koopa.red" : Kind == ShellKind.Buzzy ? "buzzy" : "koopa.green");
            string pre = Kind == ShellKind.Buzzy ? "bshell." : "shell.";
            int frame = 1;
            if (Moving) frame = 1 + (Anim / 2) % 4;
            int shake = !Moving && wakeT > 330 ? ((Anim >> 1) & 1) : 0;
            if (Dying) { DrawKnocked(ppu, Art.Get(pre + "1"), pal, camX, camY); return; }
            ppu.Spr(Art.Get(pre + frame), Px - camX + shake, Py - camY, pal, false, Flipped);
        }
    }

    // ======================================================================== Buzzy Beetle
    public sealed class Buzzy : Entity
    {
        public Buzzy(World w, int px, int py) : base(w, px, py)
        {
            FireImmune = true; HbX = 2; HbY = 4; HbW = 12; HbH = 12;
            Facing = w.P.CenterX < px + 8 ? -1 : 1; XVel = Facing * 0x08;
        }
        public override void Update() { if (UpdateKnocked()) return; Anim++; MoveWalker(false); if (XVel != 0) Facing = Math.Sign(XVel); }
        public override void OnPlayerTouch(Player p)
        {
            if (W.CanStomp(p, this))
            {
                W.StompBounce(p, this);
                var s = new Shell(W, Px, Py, ShellKind.Buzzy, false); s.SpawnIndex = SpawnIndex; SpawnIndex = -1;
                W.Add(s); Remove = true;
            }
            else p.Hurt();
        }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (d == Dmg.Fire) return false;
            if (d == Dmg.Tail || d == Dmg.Bump)
            {
                var s = new Shell(W, Px, Py, ShellKind.Buzzy, true); s.SpawnIndex = SpawnIndex; SpawnIndex = -1; s.YVel = -0x30;
                W.Add(s); Remove = true; return true;
            }
            KnockOff(dir); return true;
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("buzzy");
            if (Dying) { DrawKnocked(ppu, Art.Get("bshell.1"), pal, camX, camY); return; }
            ppu.Spr(Art.Get((Anim / 8) % 2 == 0 ? "buzzy.1" : "buzzy.2"), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    // ======================================================================== Spiny (and eggs)
    public sealed class Spiny : Entity
    {
        bool egg;
        public Spiny(World w, int px, int py, bool egg) : base(w, px, py)
        {
            this.egg = egg; Stompable = false; HbX = 2; HbY = 4; HbW = 12; HbH = 12;
            Facing = w.P.CenterX < px + 8 ? -1 : 1; XVel = egg ? 0 : Facing * 0x08;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            Anim++;
            MoveWalker(false);
            if (egg && OnGround) { egg = false; Facing = FaceToward(W.P); XVel = Facing * 0x08; }
            if (XVel != 0) Facing = Math.Sign(XVel);
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("spiny");
            if (Dying) { DrawKnocked(ppu, Art.Get("spiny.1"), pal, camX, camY); return; }
            string img = egg ? ((Anim / 4) % 2 == 0 ? "spinyegg.1" : "spinyegg.2") : ((Anim / 8) % 2 == 0 ? "spiny.1" : "spiny.2");
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    // ======================================================================== Piranha Plant / Venus Fire Trap
    public sealed class Piranha : Entity
    {
        readonly bool venus; readonly int homeY, height; int phase, t;
        public Piranha(World w, int px, int pipeTopPy, bool venus) : base(w, px + 8, pipeTopPy)
        {
            this.venus = venus;
            height = venus ? 32 : 24;
            Ht = height;
            homeY = pipeTopPy;
            Y = homeY << 4;              // fully hidden inside the pipe
            Stompable = false; Behind = true;
            HbX = 3; HbY = 2; HbW = 10; HbH = height - 4;
            UsesSlot = true;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++; Anim++;
            switch (phase)
            {
                case 0:
                    if (t > 60 && Math.Abs(W.P.CenterX - Cx) > 24) { phase = 1; t = 0; }
                    break;
                case 1:
                    Y -= 16;
                    if (Py <= homeY - height) { Y = (homeY - height) << 4; phase = 2; t = 0; }
                    break;
                case 2:
                    if (venus && t == 30) Shoot();
                    if (t > 60) { phase = 3; t = 0; }
                    break;
                case 3:
                    Y += 16;
                    if (Py >= homeY) { Y = homeY << 4; phase = 0; t = 0; }
                    break;
            }
            Hurts = Py < homeY - 4;
        }
        void Shoot()
        {
            var p = W.P;
            int dx = p.CenterX - Cx, dy = p.Py + 16 - (Py + 8);
            int vx = dx < 0 ? -0x10 : 0x10;
            int vy = Math.Abs(dy) < 24 ? 0 : dy < 0 ? -0x0C : 0x0C;
            W.Add(new EnemyFire(W, Cx - 4, Py + 6, vx, vy));
        }
        public override void OnPlayerTouch(Player p) { if (Hurts) p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (Py >= homeY - 2) return false;
            Remove = true; Killed = true; W.Puff(Px, Py); W.AddScore(100, Px, Py); return true;
        }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("plant");
            if (venus)
            {
                bool facingRight = W.P.CenterX > Cx;
                ppu.Spr(Art.Get(phase == 2 && t > 22 && t < 40 ? "venus.2" : "venus.1"), Px - camX, Py - camY, pal, !facingRight, false, true);
            }
            else ppu.Spr(Art.Get((Anim / 8) % 2 == 0 ? "piranha.1" : "piranha.2"), Px - camX, Py - camY, pal, false, false, true);
        }
    }

    public sealed class EnemyFire : Entity
    {
        int t;
        public EnemyFire(World w, int px, int py, int vx, int vy) : base(w, px, py)
        {
            Class = EntClass.EnemyProjectile; UsesSlot = false; Wd = 8; Ht = 8; HbX = 1; HbY = 1; HbW = 6; HbH = 6; XVel = vx; YVel = vy; Stompable = false;
            StarImmune = true;
        }
        public override void Update() { t++; X += XVel; Y += YVel; if (t > 400) Remove = true; }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("fireball." + (1 + (t / 2) % 4)), Px - camX, Py - camY, Art.Pal("fireball")); }
    }

    // ======================================================================== Bullet Bill + Bill Blaster
    public sealed class BillBlaster : Entity
    {
        readonly int tx, ty; int t, cooldown = 90;
        public BillBlaster(World w, int tx, int ty) : base(w, tx * 16, ty * 16) { this.tx = tx; this.ty = ty; Class = EntClass.Special; Hurts = false; UsesSlot = false; SpawnIndex = -1; }
        public override void Update()
        {
            t++;
            if (--cooldown > 0) return;
            var p = W.P;
            int sx = tx * 16 - W.CamX, sy = ty * 16 - W.CamY;
            if (sx < -8 || sx > 256 || sy < -8 || sy > 192 || Math.Abs(p.CenterX - (tx * 16 + 8)) < 28) { cooldown = 20; return; }
            int dir = p.CenterX < tx * 16 + 8 ? -1 : 1;
            if (W.CountClass(EntClass.Enemy) >= 5) { cooldown = 40; return; }
            W.Add(new BulletBill(W, tx * 16 + (dir < 0 ? -8 : 8), ty * 16, dir));
            W.Puff(tx * 16 + (dir < 0 ? -12 : 12), ty * 16);
            Sound.Sfx(SfxId.Cannon);
            cooldown = 150 + (int)((uint)(t * 2654435761u) % 120);
        }
        public override void Draw(Ppu ppu, int camX, int camY) { }
        public override void OnPlayerTouch(Player p) { }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    public sealed class BulletBill : Entity
    {
        public BulletBill(World w, int px, int py, int dir) : base(w, px, py)
        {
            Facing = dir; FireImmune = true; HbX = 1; HbY = 2; HbW = 14; HbH = 12; Behind = true; UsesSlot = true; Points = 100;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            Anim++;
            if (Anim > 16) Behind = false;
            int target = Facing * 0x18;
            if (XVel < target) XVel++; else if (XVel > target) XVel--;
            X += XVel;
        }
        public override void OnPlayerTouch(Player p)
        {
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); Dying = true; Killed = true; KnockTimer = 1; YVel = 0; XVel = XVel / 2; Hurts = false; }
            else p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("bill"), Px - camX, Py - camY, Art.Pal("bill"), Facing < 0, Dying, Behind); }
    }

    public sealed class AirshipCannon : Entity
    {
        readonly int tx, ty, dir; int cooldown = 100, t;
        public AirshipCannon(World w, int tx, int ty, int dir) : base(w, tx * 16, ty * 16) { this.tx = tx; this.ty = ty; this.dir = dir; Class = EntClass.Special; Hurts = false; UsesSlot = false; }
        public override void Update()
        {
            t++;
            if (--cooldown > 0) return;
            int sx = tx * 16 - W.CamX, sy = ty * 16 - W.CamY;
            if (sx < 0 || sx > 256 || sy < -8 || sy > 192) { cooldown = 30; return; }
            W.Add(new Cannonball(W, tx * 16 + dir * 12, ty * 16, dir * 0x14, 0));
            W.Puff(tx * 16 + dir * 14, ty * 16);
            Sound.Sfx(SfxId.Cannon);
            cooldown = 140 + (tx * 37) % 60;
        }
        public override void Draw(Ppu ppu, int camX, int camY) { }
        public override void OnPlayerTouch(Player p) { }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    public sealed class Cannonball : Entity
    {
        public Cannonball(World w, int px, int py, int vx, int vy) : base(w, px, py)
        {
            Class = EntClass.EnemyProjectile; UsesSlot = false; XVel = vx; YVel = vy; FireImmune = true; HbX = 2; HbY = 2; HbW = 12; HbH = 12; Points = 100;
        }
        public override void Update() { if (UpdateKnocked()) return; X += XVel; Y += YVel; }
        public override void OnPlayerTouch(Player p)
        {
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); Dying = true; KnockTimer = 1; Hurts = false; YVel = 0; }
            else p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("cannonball"), Px - camX, Py - camY, Art.Pal("cannonball")); }
    }

    // ======================================================================== water enemies
    public sealed class Cheep : Entity
    {
        readonly bool leaper; readonly int oy; int t, wait;
        public Cheep(World w, int px, int py, bool leaper) : base(w, px, py)
        {
            this.leaper = leaper; oy = py;
            Facing = w.P.CenterX < px ? -1 : 1;
            XVel = Facing * (leaper ? 0x10 : 0x08);
            HbX = 2; HbY = 3; HbW = 12; HbH = 11;
            Stompable = leaper;
            if (leaper) { Y = (w.LevelPxH + 8) << 4; wait = 30 + (px * 7) % 90; }
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++; Anim++;
            if (leaper)
            {
                if (wait > 0) { wait--; return; }
                if (Py >= W.LevelPxH + 8 && YVel >= 0) { Y = (W.LevelPxH + 8) << 4; YVel = -0x58; X = (W.CamX + (W.P.CenterX - W.CamX < 128 ? 200 : 40)) << 4; Facing = FaceToward(W.P); XVel = Facing * 0x10; }
                X += XVel; Y += YVel; YVel += 2; if (YVel > 0x40) YVel = 0x40;
                if (Py > W.LevelPxH + 8) wait = 60;
                return;
            }
            X += XVel;
            Y = (oy << 4) + (int)(Math.Sin(t * 2 * Math.PI / 90.0) * 6 * 16);
            if (XVel > 0 && SolidAt(Px + 15, Py + 8) || XVel < 0 && SolidAt(Px, Py + 8)) { XVel = -XVel; Facing = -Facing; }
        }
        public override void OnPlayerTouch(Player p)
        {
            if (Stompable && !p.Swimming && W.CanStomp(p, this)) { W.StompBounce(p, this); KnockOff(Facing); return; }
            p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("cheep");
            if (Dying) { DrawKnocked(ppu, Art.Get("cheep.1"), pal, camX, camY); return; }
            ppu.Spr(Art.Get((Anim / 8) % 2 == 0 ? "cheep.1" : "cheep.2"), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    public sealed class Blooper : Entity
    {
        int t, push;
        public Blooper(World w, int px, int py) : base(w, px, py - 8) { Ht = 24; HbX = 2; HbY = 2; HbW = 12; HbH = 18; Stompable = false; }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++;
            var p = W.P;
            if (push > 0) { push--; X += XVel; Y += YVel; YVel += 1; if (YVel > 0) YVel = 0; }
            else
            {
                Y += 6;
                if (t % 50 == 0 && p.Py + 8 < Py + 8)
                {
                    push = 18; YVel = -0x1C; XVel = p.CenterX < Cx ? -0x10 : 0x10;
                }
            }
            if (W.Area.WaterRow > 0 && Py < W.Area.WaterRow * 16) Y = (W.Area.WaterRow * 16) << 4;
            if (Py > W.LevelPxH - 40) Y = (W.LevelPxH - 40) << 4;
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("blooper");
            if (Dying) { DrawKnocked(ppu, Art.Get("blooper.2"), pal, camX, camY); return; }
            if (push > 0) ppu.Spr(Art.Get("blooper.2"), Px - camX, Py - camY + 8, pal);
            else ppu.Spr(Art.Get("blooper.1"), Px - camX, Py - camY, pal);
        }
    }

    // ======================================================================== fortress enemies
    public sealed class Boo : Entity
    {
        bool shy;
        public Boo(World w, int px, int py) : base(w, px, py)
        {
            Stompable = false; FireImmune = true; TailImmune = true; HbX = 2; HbY = 2; HbW = 12; HbH = 12; UsesSlot = true;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            var p = W.P;
            bool facingBoo = (p.Facing > 0 && Cx > p.CenterX) || (p.Facing < 0 && Cx < p.CenterX);
            shy = facingBoo;
            if (shy) { XVel -= Math.Sign(XVel); YVel -= Math.Sign(YVel); }
            else
            {
                int tx = p.CenterX > Cx ? 0x0C : -0x0C, ty = p.Py + 12 > Py ? 0x08 : -0x08;
                if (XVel < tx) XVel++; else if (XVel > tx) XVel--;
                if ((W.Frame & 1) == 0) { if (YVel < ty) YVel++; else if (YVel > ty) YVel--; }
                Facing = XVel > 0 ? 1 : -1;
            }
            X += XVel; Y += YVel;
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("boo");
            if (Dying) { DrawKnocked(ppu, Art.Get("boo.2"), pal, camX, camY); return; }
            ppu.Spr(Art.Get(shy ? "boo.2" : "boo.1"), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    public sealed class Thwomp : Entity
    {
        readonly int homeY; int phase, t;
        public Thwomp(World w, int px, int py) : base(w, px - 4, py - 16)
        {
            Wd = 24; Ht = 32; HbX = 2; HbY = 2; HbW = 20; HbH = 30;
            homeY = Py; Stompable = false; FireImmune = true; TailImmune = true; ShellImmune = true;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++;
            var p = W.P;
            switch (phase)
            {
                case 0:
                    if (Math.Abs(p.CenterX - Cx) < 36 && p.Py + 16 > Py) { phase = 1; t = 0; YVel = 0; }
                    break;
                case 1:
                    YVel += 6; if (YVel > 0x60) YVel = 0x60;
                    Y += YVel;
                    if (FloorAt(Px + 4, Bottom) || FloorAt(Px + Wd - 4, Bottom) || Py > W.LevelPxH)
                    {
                        Y = (((Bottom) & ~15) - Ht) << 4; phase = 2; t = 0;
                        Sound.Sfx(SfxId.Thwomp); W.Shake(16);
                        SMB4.Platform.Host.Input.Rumble(W.S.PlayerIndex, 0.5f, 10);
                    }
                    break;
                case 2:
                    if (t > 60) phase = 3;
                    break;
                case 3:
                    Y -= 12;
                    if (Py <= homeY) { Y = homeY << 4; phase = 0; t = 0; }
                    break;
            }
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { if (d == Dmg.Star || d == Dmg.Hammer || d == Dmg.Statue) { KnockOff(dir); return true; } return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get(phase == 1 || phase == 2 ? "thwomp.2" : "thwomp.1"), Px - camX, Py - camY, Art.Pal("thwomp"), false, Dying);
        }
    }

    public sealed class DryBones : Entity
    {
        int collapsed;
        public DryBones(World w, int px, int py) : base(w, px, py - 8)
        {
            Ht = 24; HbX = 2; HbY = 8; HbW = 12; HbH = 16; FireImmune = true; TailImmune = true;
            Facing = w.P.CenterX < px ? -1 : 1; XVel = Facing * 0x08;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            Anim++;
            if (collapsed > 0)
            {
                collapsed--;
                if (collapsed == 0) { Hurts = true; XVel = FaceToward(W.P) * 0x08; }
                return;
            }
            MoveWalker(true);
            if (XVel != 0) Facing = Math.Sign(XVel);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (collapsed > 0) return;
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); collapsed = 240; Hurts = false; XVel = 0; Sound.Sfx(SfxId.Break); }
            else p.Hurt();
        }
        public override bool TakeHit(Dmg d, int dir)
        {
            if (d == Dmg.Star || d == Dmg.Hammer || d == Dmg.Statue) { KnockOff(dir); return true; }
            return false;
        }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("bones");
            if (Dying) { DrawKnocked(ppu, Art.Get("bones.1"), pal, camX, camY); return; }
            if (collapsed > 0) { int shake = collapsed < 50 ? ((Anim >> 1) & 1) : 0; ppu.Spr(Art.Get("bones.pile"), Px - camX + shake, Py - camY + 8, pal, Facing < 0); return; }
            ppu.Spr(Art.Get((Anim / 8) % 2 == 0 ? "bones.1" : "bones.2"), Px - camX, Py - camY, pal, Facing < 0);
        }
    }

    public sealed class Podoboo : Entity
    {
        readonly int homeY; int wait;
        public Podoboo(World w, int px, int py) : base(w, px, py)
        {
            homeY = w.LevelPxH + 8; Y = homeY << 4; Stompable = false; FireImmune = true; TailImmune = true; ShellImmune = true; UsesSlot = false;
            wait = 30 + (px * 13) % 90; HbX = 3; HbY = 2; HbW = 10; HbH = 12;
            jumpVel = -(int)Math.Sqrt(2 * 3 * ((homeY - py) + 8) * 16.0);
        }
        readonly int jumpVel;
        public override void Update()
        {
            if (UpdateKnocked()) return;
            if (wait > 0) { wait--; if (wait == 0) { YVel = jumpVel; Sound.Sfx(SfxId.Lava); } return; }
            Y += YVel; YVel += 3;
            if (Py >= homeY && YVel > 0) { Y = homeY << 4; wait = 110; }
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { if (d == Dmg.Star || d == Dmg.Hammer) { KnockOff(dir); return true; } return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            if (wait > 0) return;
            ppu.Spr(Art.Get("podoboo"), Px - camX, Py - camY, Art.Pal("podoboo"), false, YVel > 0);
        }
    }

    public sealed class RotoDisc : Entity
    {
        readonly int cx, cy; readonly double speed; double a;
        public RotoDisc(World w, int px, int py, int dirSign) : base(w, px, py)
        {
            cx = px; cy = py; speed = dirSign * 2 * Math.PI / 150.0;
            Stompable = false; FireImmune = true; TailImmune = true; ShellImmune = true; StarImmune = true; HammerImmune = true;
            HbX = 3; HbY = 3; HbW = 10; HbH = 10; UsesSlot = false;
        }
        public override void Update()
        {
            a += speed;
            X = (cx + (int)(Math.Cos(a) * 40)) << 4;
            Y = (cy + (int)(Math.Sin(a) * 40)) << 4;
        }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal((W.Frame / 3) % 2 == 0 ? "rotodisc" : "rotodisc.2");
            ppu.Spr(Art.Get("rotodisc"), Px - camX, Py - camY, pal);
        }
    }

    // ======================================================================== Hammer Bro, Rocky Wrench, Bob-omb, Lakitu
    public sealed class HammerBro : Entity
    {
        readonly int homeX; int t, throwT, jumpT, throwing;
        public HammerBro(World w, int px, int py) : base(w, px, py - 8)
        {
            Ht = 24; HbX = 2; HbY = 6; HbW = 12; HbH = 18; homeX = px; Points = 1000;
            XVel = 0x08; throwT = 60; jumpT = 180;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++; Anim++;
            var p = W.P;
            Facing = FaceToward(p);
            if (Px > homeX + 16) XVel = -0x08; else if (Px < homeX - 16) XVel = 0x08;
            if (--throwT <= 0)
            {
                throwing = 12;
                W.Add(new EnemyHammer(W, Px + (Facing > 0 ? 8 : 0), Py, Facing));
                throwT = 50 + (int)((uint)(t * 2246822519u) % 60);
            }
            if (throwing > 0) throwing--;
            if (--jumpT <= 0 && OnGround)
            {
                YVel = (t / 180) % 2 == 0 ? -0x50 : -0x28;
                OnGround = false;
                jumpT = 150 + (t % 90);
            }
            MoveWalker(false, true);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); KnockTimer = 1; Dying = true; Killed = true; Hurts = false; YVel = 0; W.MarkKilled(this); }
            else p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("bro");
            if (Dying) { DrawKnocked(ppu, Art.Get("bro.1"), pal, camX, camY); return; }
            string img = throwing > 0 ? "bro.throw" : (Anim / 10) % 2 == 0 ? "bro.1" : "bro.2";
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, pal, Facing < 0);
            if (throwing == 0 && throwT < 20) ppu.Spr(Art.Get("hammer.1"), Px - camX + (Facing > 0 ? 6 : -2), Py - camY - 10, Art.Pal("hammer"), Facing < 0);
        }
    }

    public sealed class EnemyHammer : Entity
    {
        int t;
        public EnemyHammer(World w, int px, int py, int dir) : base(w, px, py)
        {
            Class = EntClass.EnemyProjectile; UsesSlot = false; Facing = dir; XVel = dir * 0x12; YVel = -0x48; HbX = 3; HbY = 3; HbW = 10; HbH = 10;
            StarImmune = true; Stompable = false;
        }
        public override void Update() { t++; X += XVel; Y += YVel; YVel += 3; if (Py > W.CamY + 240) Remove = true; }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("hammer." + (1 + (t / 3) % 4)), Px - camX, Py - camY, Art.Pal("hammer"), Facing < 0); }
    }

    public sealed class RockyWrench : Entity
    {
        readonly int homeY; int t, phase;
        public RockyWrench(World w, int px, int py) : base(w, px, py)
        {
            homeY = py + 12; Y = homeY << 4; Behind = true; HbX = 2; HbY = 2; HbW = 12; HbH = 12; t = (px * 7) % 60;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++;
            switch (phase)
            {
                case 0: if (t > 90 && Math.Abs(W.P.CenterX - Cx) < 120) { phase = 1; t = 0; } break;
                case 1: Y -= 16; if (Py <= homeY - 12) { phase = 2; t = 0; } break;
                case 2:
                    Facing = FaceToward(W.P);
                    if (t == 30) W.Add(new Wrench(W, Px + 4, Py, Facing));
                    if (t > 60) { phase = 3; t = 0; }
                    break;
                case 3: Y += 16; if (Py >= homeY) { phase = 0; t = 0; } break;
            }
            Hurts = phase != 0;
        }
        public override void OnPlayerTouch(Player p)
        {
            if (!Hurts) return;
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); KnockTimer = 1; Dying = true; Killed = true; Hurts = false; YVel = -0x20; }
            else p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            if (phase == 0 && !Dying) return;
            ppu.Spr(Art.Get(phase == 2 ? "rocky.2" : "rocky.1"), Px - camX, Py - camY, Art.Pal("rocky"), Facing < 0, Dying, !Dying);
        }
    }

    public sealed class Wrench : Entity
    {
        int t;
        public Wrench(World w, int px, int py, int dir) : base(w, px, py)
        {
            Class = EntClass.EnemyProjectile; UsesSlot = false; Wd = 8; Ht = 8; HbX = 0; HbY = 0; HbW = 8; HbH = 8; XVel = dir * 0x18; Stompable = false; StarImmune = true;
        }
        public override void Update() { t++; X += XVel; if (t > 300) Remove = true; }
        public override void OnPlayerTouch(Player p) { p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("wrench.1"), Px - camX, Py - camY, Art.Pal("rocky"), (t / 4) % 2 == 0, (t / 8) % 2 == 0); }
    }

    public sealed class BobOmb : Entity
    {
        int fuse;
        public BobOmb(World w, int px, int py) : base(w, px, py)
        {
            FireImmune = true; HbX = 2; HbY = 3; HbW = 12; HbH = 13;
            Facing = w.P.CenterX < px ? -1 : 1; XVel = Facing * 0x08;
        }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            Anim++;
            if (fuse > 0)
            {
                if (W.P.Carrying == this) { X = (W.P.Px + (W.P.Facing > 0 ? 11 : -11)) << 4; Y = (W.P.Py + (W.P.Big ? 13 : 17)) << 4; }
                else MoveWalker(false, true);
                if (--fuse == 0) Explode();
                return;
            }
            MoveWalker(false);
            if (XVel != 0) Facing = Math.Sign(XVel);
        }
        void Explode()
        {
            if (W.P.Carrying == this) W.P.Carrying = null;
            Remove = true; Killed = true;
            W.Add(new Explosion(W, Cx - 16, Py - 8));
            Sound.Sfx(SfxId.Explode);
            W.Shake(12);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (fuse > 0)
            {
                if (W.P.Carrying == this) return;
                if (p.Pad.H(SMB4.Platform.Btn.B) && p.Carrying == null) { p.Carrying = this; return; }
                XVel = (p.CenterX < Cx ? 1 : -1) * 0x20; Sound.Sfx(SfxId.Kick); p.KickPose = Phys.KickPose;
                return;
            }
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); fuse = 240; XVel = 0; Hurts = false; Carryable = true; }
            else p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("bobomb");
            if (fuse > 0 && fuse < 60 && (fuse & 4) != 0) pal = Art.Pal("bobomb.flash");
            if (Dying) { DrawKnocked(ppu, Art.Get("bobomb.1"), pal, camX, camY); return; }
            ppu.Spr(Art.Get((Anim / 8) % 2 == 0 || fuse > 0 ? "bobomb.1" : "bobomb.2"), Px - camX, Py - camY, pal, Facing < 0, fuse > 0);
        }
    }

    public sealed class Explosion : Entity
    {
        int t;
        // fire-tinted copy of the (15-color) fx palette: maps each color's brightness onto a red->orange->yellow->white ramp,
        // so the explosion keeps working whatever slot layout the puff art uses
        static ushort[] boomSrc, boomHot, boomWarm;
        static ushort[] BoomPal(bool hot)
        {
            var src = Art.Pal("fx");
            if (src != boomSrc) { boomSrc = src; boomHot = FireTint(src, 0); boomWarm = FireTint(src, 50); }
            return hot ? boomHot : boomWarm;
        }
        static ushort[] FireTint(ushort[] src, int warm)
        {
            var p = new ushort[src.Length];
            for (int i = 1; i < src.Length; i++)
            {
                int c = NesPalette.RgbOf(src[i]);
                int l = Math.Min(255, (((c >> 16) & 255) * 3 + ((c >> 8) & 255) * 6 + (c & 255)) / 10 + warm);
                int r = Math.Min(255, 110 + l * 2), g = Math.Max(0, Math.Min(255, (l - 50) * 3 / 2)), b = Math.Max(0, Math.Min(255, (l - 170) * 3));
                p[i] = (ushort)Art.Rgb((r << 16) | (g << 8) | b);
            }
            return p;
        }
        public Explosion(World w, int px, int py) : base(w, px, py) { Class = EntClass.EnemyProjectile; UsesSlot = false; Wd = 32; Ht = 32; HbX = 2; HbY = 2; HbW = 28; HbH = 28; StarImmune = true; Stompable = false; }
        public override void Update()
        {
            t++;
            if (t == 2)
                foreach (var e in W.Ents) if (e != this && e.Class == EntClass.Enemy && !e.Dying && e.Overlaps(this)) { e.KnockOff(e.Cx < Cx + 16 ? -1 : 1); W.MarkKilled(e); }
            if (t > 20) Remove = true;
        }
        public override void OnPlayerTouch(Player p) { if (t < 14) p.Hurt(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            string img = "puff." + (1 + Math.Min(2, t / 7));
            var pal = BoomPal((t / 2) % 2 == 0);
            ppu.Spr(Art.Get(img), Px - camX, Py - camY, pal);
            ppu.Spr(Art.Get(img), Px - camX + 16, Py - camY, pal, true);
            ppu.Spr(Art.Get(img), Px - camX, Py - camY + 16, pal, false, true);
            ppu.Spr(Art.Get(img), Px - camX + 16, Py - camY + 16, pal, true, true);
        }
    }

    public sealed class Lakitu : Entity
    {
        int t;
        public Lakitu(World w, int px, int py) : base(w, px, py - 8) { Ht = 24; HbX = 2; HbY = 2; HbW = 12; HbH = 20; Points = 800; }
        public override void Update()
        {
            if (UpdateKnocked()) return;
            t++;
            var p = W.P;
            int target = p.CenterX + (int)(Math.Sin(t * 2 * Math.PI / 200.0) * 60);
            int tv = target > Cx ? 0x18 : -0x18;
            if (XVel < tv) XVel++; else if (XVel > tv) XVel--;
            X += XVel;
            Facing = p.CenterX < Cx ? -1 : 1;
            int ty = W.CamY + 24;
            Y += Math.Sign(ty - Py) * 8;
            if (t % 150 == 0 && W.CountClass(EntClass.Enemy) < 5) W.Add(new Spiny(W, Px, Py, true) { YVel = -0x30, XVel = 0 });
        }
        public override void OnPlayerTouch(Player p)
        {
            if (W.CanStomp(p, this)) { W.StompBounce(p, this); KnockOff(Facing); W.MarkKilled(this); }
            else p.Hurt();
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("lakitu");
            if (Dying) { DrawKnocked(ppu, Art.Get("lakitu"), pal, camX, camY); return; }
            ppu.Spr(Art.Get("lakitu"), Px - camX, Py - camY, pal, Facing < 0);
        }
    }
}
