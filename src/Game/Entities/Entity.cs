using System;
using SMB4.Audio;
using SMB4.Engine;

namespace SMB4.Game
{
    public enum Dmg { Stomp, Fire, Tail, Shell, Star, Hammer, Bump, Statue, Lava, Boomerang }
    public enum EntClass { Enemy, Item, Projectile, EnemyProjectile, Effect, Special, Platform }

    /// <summary>Base for every object in a level (enemies, items, projectiles, effects, platforms).</summary>
    public abstract class Entity
    {
        public World W;
        public int X, Y, XVel, YVel;           // 1/16 px, top-left of the sprite box
        public int Wd = 16, Ht = 16;           // sprite size (px)
        public int HbX = 2, HbY = 3, HbW = 12, HbH = 13;
        public bool Remove, Killed, Dying;
        public int Facing = -1;
        public int Timer, State, Anim;
        public int SpawnIndex = -1;
        public EntClass Class = EntClass.Enemy;
        public bool StompImmune, FireImmune, TailImmune, ShellImmune, StarImmune, HammerImmune;
        public bool Hurts = true, Stompable = true;
        public int StompH = 19;
        public bool OnGround;
        public bool Behind;
        public int KnockTimer;
        public bool Carryable;                 // stunned shells, bob-ombs...
        public bool IsShell;                   // moving shells kill other enemies
        public int ShellChain;
        public bool UsesSlot = true;           // counts toward SMB3's 5 general object slots
        public int Points = 100;

        public int Px { get { return X >> 4; } }
        public int Py { get { return Y >> 4; } }
        public int Cx { get { return Px + Wd / 2; } }
        public int Bottom { get { return Py + Ht; } }

        protected Entity(World w, int px, int py) { W = w; X = px << 4; Y = py << 4; }

        public virtual void Update() { }
        public abstract void Draw(Ppu ppu, int camX, int camY);
        public virtual void OnPlayerTouch(Player p) { if (Hurts) p.Hurt(); }
        /// <summary>Returns true if the hit affected the object.</summary>
        public virtual bool TakeHit(Dmg d, int dir) { if (d == Dmg.Fire && FireImmune || d == Dmg.Tail && TailImmune || d == Dmg.Shell && ShellImmune || d == Dmg.Star && StarImmune || d == Dmg.Hammer && HammerImmune) return false; KnockOff(dir); return true; }
        public virtual void OnBumpBelow(int dir) { TakeHit(Dmg.Bump, dir); }

        public bool Overlaps(int x, int y, int w, int h)
        {
            int ex = Px + HbX, ey = Py + HbY;
            return ex < x + w && ex + HbW > x && ey < y + h && ey + HbH > y;
        }

        public bool Overlaps(Entity o)
        {
            return Overlaps(o.Px + o.HbX, o.Py + o.HbY, o.HbW, o.HbH);
        }

        /// <summary>Flip upside down and fall off the screen (fire/tail/shell/star/bump kills).</summary>
        public void KnockOff(int dir)
        {
            if (Dying) return;
            Dying = true; Killed = true;
            Hurts = false;
            YVel = -0x30;
            XVel = (dir == 0 ? -Facing : dir) * 0x08;
            KnockTimer = 1;
            W.AddScore(Points, Px, Py);
        }

        /// <summary>Knocked-off motion: no tile collision, falls off-screen.</summary>
        protected bool UpdateKnocked()
        {
            if (!Dying || KnockTimer == 0) return false;
            X += XVel; Y += YVel;
            YVel += 3; if (YVel > 0x40) YVel = 0x40;
            if (Py > W.CamY + 260) Remove = true;
            return true;
        }

        protected void DrawKnocked(Ppu ppu, Img img, ushort[] pal, int camX, int camY, int yOffset = 0)
        {
            ppu.Spr(img, Px - camX, Py - camY + yOffset, pal, Facing < 0, true);
        }

        // ---------------------------------------------------------------- shared physics
        public bool SolidAt(int px, int py) { return TileInfo.Solid(W.TileAtPx(px, py)); }
        public bool FloorAt(int px, int py) { return TileInfo.Floor(W.TileAtPx(px, py)); }

        /// <summary>SMB3 object gravity: +3/tick, max $40 (in water +1, max $10).</summary>
        protected void Gravity()
        {
            if (W.InWater(Cx, Py + Ht / 2)) { YVel += 1; if (YVel > 0x10) YVel = 0x10; }
            else { YVel += 3; if (YVel > 0x40) YVel = 0x40; }
        }

        /// <summary>Walker movement with tile collision. Returns true if it hit a wall this tick.</summary>
        protected bool MoveWalker(bool turnAtLedges, bool turnAtWalls = true)
        {
            bool hitWall = false;
            X += XVel;
            int px = Px, py = Py;
            int midY = py + Ht - 8;
            if (XVel > 0 && SolidAt(px + Wd - 2, midY)) { X = ((((px + Wd - 2) >> 4) << 4) - Wd + 1) << 4; hitWall = true; }
            else if (XVel < 0 && SolidAt(px + 1, midY)) { X = (((((px + 1) >> 4) + 1) << 4) - 1) << 4; hitWall = true; }
            if (hitWall && turnAtWalls) { XVel = -XVel; Facing = XVel > 0 ? 1 : XVel < 0 ? -1 : Facing; }

            Gravity();
            Y += YVel;
            py = Py; px = Px;
            int feet = py + Ht;
            // slopes: walkers follow the surface under their center
            if (YVel >= 0 && W.Area.HasSlopes)
            {
                int cxp = px + Wd / 2, stx = World.FloorDiv(cxp, 16);
                for (int k = 0; k < 3; k++)
                {
                    int yy = k == 0 ? feet - 16 : k == 1 ? feet : feet + 8;
                    int sty = World.FloorDiv(yy, 16);
                    T st = W.TileAt(stx, sty);
                    if (!TileInfo.Slope(st)) continue;
                    int s = TileInfo.SlopeSurface(st, stx, sty, cxp);
                    if (feet >= s - (OnGround ? 8 : 0) && feet - s < 12)
                    {
                        Y = (s - Ht) << 4; YVel = 0; OnGround = true;
                        if (Py > W.LevelPxH + 32) { Remove = true; Killed = true; }
                        return hitWall;
                    }
                }
            }
            if (YVel >= 0)
            {
                bool f = FloorAt(px + 3, feet) || FloorAt(px + Wd - 4, feet);
                if (f && (feet & 15) < 8)
                {
                    Y = ((feet & ~15) - Ht) << 4;
                    YVel = 0;
                    OnGround = true;
                    T under = W.TileAtPx(px + Wd / 2, feet);
                    if (W.PSwitchTimer == 0) { if (under == T.ConveyorL) X -= 16; else if (under == T.ConveyorR) X += 16; }
                }
                else OnGround = false;
            }
            else
            {
                OnGround = false;
                if (SolidAt(px + Wd / 2, py + 2)) { YVel = 0; Y = ((((py + 2) >> 4) + 1) << 4) - 2 << 4; }
            }
            if (OnGround && turnAtLedges)
            {
                int ahead = XVel > 0 ? px + Wd : px - 1;
                if (!FloorAt(ahead, Py + Ht + 2)) { XVel = -XVel; Facing = -Facing; X += XVel; }
            }
            if (Py > W.LevelPxH + 32) { Remove = true; Killed = true; }
            return hitWall;
        }

        protected int FaceToward(Player p) { return p.CenterX < Cx ? -1 : 1; }

        public virtual void DrawCarried(Ppu ppu, int camX, int camY) { Draw(ppu, camX, camY); }
    }
}
