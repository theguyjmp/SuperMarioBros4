using System;
using SMB4.Audio;
using SMB4.Engine;

namespace SMB4.Game
{
    // ======================================================================== block bump animation
    public sealed class BumpBlock : Entity
    {
        readonly int tx, ty, dir; readonly T final;
        int t;
        public BumpBlock(World w, int tx, int ty, int dir, T final) : base(w, tx * 16, ty * 16)
        {
            this.tx = tx; this.ty = ty; this.dir = dir; this.final = final;
            Class = EntClass.Special; Hurts = false; UsesSlot = false;
            w.Hidden[ty * w.W + tx] = 1;
            w.SetTile(tx, ty, final);
        }
        public override void Update()
        {
            t++;
            if (t >= 8) { Remove = true; if (tx >= 0 && ty >= 0 && tx < W.W && ty < W.H) W.Hidden[ty * W.W + tx] = 0; }
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            int off = t < 4 ? t * 2 : (8 - t) * 2;
            W.DrawTileImage(ppu, final, tx, ty, tx * 16 - camX, ty * 16 - camY + off * dir, true);
        }
        public override void OnPlayerTouch(Player p) { }
    }

    // ======================================================================== coins popping out of blocks
    public sealed class CoinPop : Entity
    {
        int t;
        public CoinPop(World w, int px, int py) : base(w, px, py) { Class = EntClass.Effect; Hurts = false; YVel = -0x50; UsesSlot = false; }
        public override void Update()
        {
            t++; Y += YVel; YVel += 5;
            if (t > 26) { Remove = true; W.Add(new ScorePopup(W, Px, Py, 100)); W.S.AddScore(0); }
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get("coin." + (1 + (t / 3) % 4)), Px - camX, Py - camY, Art.Pal("coin"));
        }
    }

    // ======================================================================== power-ups
    public abstract class Rising : Entity
    {
        protected int rise = 16;       // pixels still inside the block
        protected Rising(World w, int px, int py) : base(w, px, py) { Class = EntClass.Item; Hurts = false; UsesSlot = false; Behind = true; }
        protected bool Emerging()
        {
            if (rise <= 0) { Behind = false; return false; }
            if (W.Frame % 2 == 0) { Y -= 16; rise--; }
            return true;
        }
    }

    public sealed class Mushroom : Rising
    {
        readonly bool oneUp;
        public Mushroom(World w, int px, int py, bool oneUp) : base(w, px, py) { this.oneUp = oneUp; Facing = 1; }
        public override void Update()
        {
            if (Emerging()) return;
            if (XVel == 0) XVel = 0x10 * Facing;
            MoveWalker(false);
            if (XVel != 0) Facing = Math.Sign(XVel);
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get("mushroom"), Px - camX, Py - camY, Art.Pal(oneUp ? "oneup" : "mushroom"), false, false, Behind);
        }
        public override void OnPlayerTouch(Player p)
        {
            if (rise > 8) return;
            Remove = true;
            if (oneUp) W.OneUp(Px, Py); else p.PowerUp(Form.Big);
        }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { YVel = -0x30; Facing = dir; XVel = 0x10 * dir; }
    }

    public sealed class FireFlowerItem : Rising
    {
        public FireFlowerItem(World w, int px, int py) : base(w, px, py) { }
        public override void Update() { Emerging(); }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            var pal = Art.Pal("flower");
            if ((W.Frame / 4) % 2 == 1 && Art.HasPal("flower.2")) pal = Art.Pal("flower.2");   // full 15-color flash palette
            ppu.Spr(Art.Get("flower.1"), Px - camX, Py - camY, pal, false, false, Behind);
        }
        public override void OnPlayerTouch(Player p) { if (rise > 8) return; Remove = true; p.PowerUp(Form.Fire); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    public sealed class LeafItem : Entity
    {
        int t, swayDir = 1;
        public LeafItem(World w, int px, int py) : base(w, px, py - 8) { Class = EntClass.Item; Hurts = false; UsesSlot = false; YVel = -0x40; }
        public override void Update()
        {
            t++;
            if (YVel < 0) { Y += YVel; YVel += 3; return; }
            // drift down in a swaying zig-zag
            int phase = t % 64;
            if (phase == 0) swayDir = -swayDir;
            XVel = swayDir * (phase < 32 ? 0x14 : 0x08);
            X += XVel;
            Y += phase < 32 ? 6 : 10;
            Facing = swayDir;
            if (Py > W.CamY + 240) Remove = true;
        }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("leaf"), Px - camX, Py - camY, Art.Pal("leaf"), swayDir < 0); }
        public override void OnPlayerTouch(Player p) { Remove = true; p.PowerUp(Form.Raccoon); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    public sealed class StarItem : Rising
    {
        public StarItem(World w, int px, int py) : base(w, px, py) { Facing = 1; }
        public override void Update()
        {
            if (Emerging()) return;
            XVel = 0x18 * Facing;
            X += XVel;
            if (XVel > 0 && SolidAt(Px + 14, Py + 8) || XVel < 0 && SolidAt(Px + 1, Py + 8)) Facing = -Facing;
            YVel += 3; if (YVel > 0x40) YVel = 0x40;
            Y += YVel;
            if (YVel > 0 && (FloorAt(Px + 4, Py + 16) || FloorAt(Px + 11, Py + 16))) { Y = ((Py + 16) & ~15) - 16 << 4; YVel = -0x48; }
            if (YVel < 0 && SolidAt(Px + 8, Py)) YVel = 0;
            if (Py > W.LevelPxH + 16) Remove = true;
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            int k = (W.Frame / 2) % 4;
            string pn = k == 0 ? "star" : "star." + (k + 1);   // star, star.2, star.3, star.4 (items.art)
            ushort[] pal = Art.HasPal(pn) ? Art.Pal(pn) : Art.Pal("star");
            ppu.Spr(Art.Get("star"), Px - camX, Py - camY, pal, false, false, Behind);
        }
        public override void OnPlayerTouch(Player p) { if (rise > 8) return; Remove = true; p.GetStar(); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { YVel = -0x40; Facing = dir; }
    }

    public sealed class PSwitch : Entity
    {
        bool pressed; int t;
        public PSwitch(World w, int px, int py) : base(w, px, py) { Class = EntClass.Item; Hurts = false; UsesSlot = false; HbY = 4; HbH = 12; }
        public override void Update() { if (pressed && ++t > 90) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get(pressed ? "pswitch.flat" : "pswitch"), Px - camX, Py - camY, Art.Pal("pswitch"));
        }
        public override void OnPlayerTouch(Player p)
        {
            if (pressed) return;
            if (p.YVel >= 0 && p.InAir && p.FeetY <= Py + 10)
            {
                pressed = true;
                p.Bounce(-0x20);
                W.StartPSwitch();
            }
            else
            {
                // push the player out horizontally (acts like a small solid)
                if (p.CenterX < Cx) { if (p.XVel > 0) p.XVel = 0; } else if (p.XVel < 0) p.XVel = 0;
            }
        }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    public sealed class VineSprout : Entity
    {
        readonly int tx; int ty, t;
        public VineSprout(World w, int tx, int ty) : base(w, tx * 16, ty * 16 - 16) { this.tx = tx; this.ty = ty - 1; Class = EntClass.Special; Hurts = false; UsesSlot = false; }
        public override void Update()
        {
            t++;
            if (t % 8 != 0) return;
            if (ty < 0 || W.TileAt(tx, ty) != T.Empty && W.TileAt(tx, ty) != T.Vine) { Remove = true; return; }
            W.SetTile(tx, ty, T.Vine);
            ty--;
            Y = (ty * 16) << 4;
            if (ty < -1) Remove = true;
        }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get("vine.sprout"), tx * 16 - camX, (ty + 1) * 16 - camY - (t % 8) * 2, Art.HasPal("vine.sprout") ? Art.Pal("vine.sprout") : W.ThemePal("vine")); }
        public override void OnPlayerTouch(Player p) { }
    }

    // ======================================================================== player projectiles
    public abstract class PlayerProjectile : Entity
    {
        protected PlayerProjectile(World w, int px, int py) : base(w, px, py) { Class = EntClass.Projectile; Hurts = false; UsesSlot = false; }
        public abstract bool HitEnemy(Entity e);
        public override void OnPlayerTouch(Player p) { }
    }

    public sealed class Fireball : PlayerProjectile
    {
        int t;
        public Fireball(World w, int px, int py, int dir) : base(w, px, py)
        {
            Wd = 8; Ht = 8; HbX = 0; HbY = 0; HbW = 8; HbH = 8;
            XVel = dir * 3 * 16; YVel = 3 * 16; Facing = dir;
        }
        public override void Update()
        {
            t++;
            X += XVel;
            if (SolidAt(Px + (XVel > 0 ? 7 : 0), Py + 4)) { Poof(); return; }
            Y += YVel;
            if ((t & 3) == 0 && YVel < 4 * 16) YVel += 16;
            if (YVel > 0 && FloorAt(Px + 4, Py + 8) && ((Py + 8) & 15) < 6) { Y = (((Py + 8) & ~15) - 8) << 4; YVel = -3 * 16; }
            if (W.InWater(Px + 4, Py + 4) && W.Area.WaterRow > 0) { }
            if (TileInfo.Lava(W.TileAtPx(Px + 4, Py + 4))) Poof();
        }
        void Poof() { Remove = true; W.Add(new Puff(W, Px - 4, Py - 4, true)); Sound.Sfx(SfxId.FireballHit); }
        public override bool HitEnemy(Entity e)
        {
            if (e.FireImmune) { Poof(); return true; }
            if (e.TakeHit(Dmg.Fire, Facing)) { W.MarkKilled(e); Sound.Sfx(SfxId.Kick); }
            Poof();
            return true;
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get("fireball." + (1 + (t / 2) % 4)), Px - camX, Py - camY, Art.Pal("fireball"), Facing < 0);
        }
    }

    public sealed class PlayerHammer : PlayerProjectile
    {
        int t;
        public PlayerHammer(World w, int px, int py, int dir, int pxvel) : base(w, px, py)
        {
            HbX = 2; HbY = 2; HbW = 12; HbH = 12;
            XVel = dir * 0x10 + (Math.Sign(pxvel) == dir ? pxvel : 0); YVel = -3 * 16; Facing = dir;
        }
        public override void Update()
        {
            t++;
            X += XVel; Y += YVel;
            if ((t & 7) == 0) YVel += 16;
            if (Py > W.CamY + 240) Remove = true;
        }
        public override bool HitEnemy(Entity e)
        {
            if (e.HammerImmune) return false;
            if (e.TakeHit(Dmg.Hammer, Facing)) { W.MarkKilled(e); Sound.Sfx(SfxId.Kick); }
            return false;
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            ppu.Spr(Art.Get("hammer." + (1 + (t / 3) % 4)), Px - camX, Py - camY, Art.Pal("hammer"), Facing < 0);
        }
    }

    // ======================================================================== goal, orb, wand
    public sealed class GoalBox : Entity
    {
        int card, t; bool taken;
        public GoalBox(World w, int px, int py) : base(w, px, py) { Class = EntClass.Special; Hurts = false; UsesSlot = false; Wd = 32; Ht = 32; HbX = 4; HbY = 4; HbW = 24; HbH = 24; }
        public override void Update() { t++; if (!taken && t % 7 == 0) card = (card + 1) % 3; }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            int sx = Px - camX, sy = Py - camY;
            ppu.Spr(Art.Get("goal.box"), sx, sy, W.ThemePal("goal"));
            if (!taken) ppu.Spr(Art.Get(CardName(card)), sx + 8, sy + 8, CardPal(card));
        }
        public static string CardName(int c) { return c == 0 ? "card.mushroom" : c == 1 ? "card.flower" : "card.star"; }
        public static ushort[] CardPal(int c) { return c == 0 ? Art.Pal("mushroom") : c == 1 ? Art.Pal("flower") : Art.Pal("star"); }
        public override void OnPlayerTouch(Player p)
        {
            if (taken) return;
            taken = true;
            W.Add(new CardFly(W, Px + 8, Py + 8, card));
            Sound.Sfx(SfxId.CardStop);
            W.StartClear(card, LevelResult.Cleared);
        }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    public sealed class CardFly : Entity
    {
        readonly int card; int t;
        public CardFly(World w, int px, int py, int card) : base(w, px, py) { this.card = card; Class = EntClass.Effect; Hurts = false; UsesSlot = false; }
        public override void Update() { t++; Y -= 32; if (t > 60) Remove = true; }
        public override void Draw(Ppu ppu, int camX, int camY) { ppu.Spr(Art.Get(GoalBox.CardName(card)), Px - camX, Py - camY, GoalBox.CardPal(card)); }
    }

    public sealed class Orb : Entity
    {
        readonly LevelResult result;
        public Orb(World w, int px, int py, LevelResult r) : base(w, px, py) { result = r; Class = EntClass.Special; Hurts = false; UsesSlot = false; YVel = -0x30; }
        public override void Update()
        {
            YVel += 3; if (YVel > 0x40) YVel = 0x40;
            Y += YVel;
            if (YVel > 0 && (FloorAt(Px + 4, Py + 16) || FloorAt(Px + 11, Py + 16))) { Y = ((Py + 16) & ~15) - 16 << 4; YVel = YVel > 0x20 ? -YVel / 2 : 0; }
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            string img = result == LevelResult.WorldCleared && Art.Has("wand") ? "wand" : "orb";
            ppu.Spr(Art.Get(img), Px - camX + (img == "wand" ? 4 : 0), Py - camY, Art.Pal(img == "wand" ? (Art.HasPal("wand") ? "wand" : "koopaling") : Art.HasPal("orb") ? "orb" : "card"));
        }
        public override void OnPlayerTouch(Player p) { Remove = true; W.StartClear(-1, result); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    /// <summary>Treasure chest dropped after a Hammer Bro battle.</summary>
    public sealed class TreasureChest : Entity
    {
        public TreasureChest(World w, int px, int py) : base(w, px, py) { Class = EntClass.Special; Hurts = false; UsesSlot = false; }
        public override void Update()
        {
            YVel += 3; if (YVel > 0x40) YVel = 0x40;
            Y += YVel;
            if (YVel > 0 && (FloorAt(Px + 4, Py + 16) || FloorAt(Px + 11, Py + 16))) { Y = (((Py + 16) & ~15) - 16) << 4; YVel = 0; }
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            int x = Px - camX, y = Py - camY;
            if (Art.Has("chest.closed")) { ppu.Spr(Art.Get("chest.closed"), x, y, Art.PreviewPalFor("chest.closed")); return; }
            ppu.FillRect(x, y + 2, 16, 14, 0x0F);
            ppu.FillRect(x + 1, y + 3, 14, 12, 0x17);
            ppu.FillRect(x + 1, y + 6, 14, 2, 0x28);
            ppu.FillRect(x + 6, y + 8, 4, 4, 0x38);
        }
        public override void OnPlayerTouch(Player p) { Remove = true; Sound.Sfx(SfxId.Chest); W.StartClear(-1, LevelResult.Cleared); }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
    }

    // ======================================================================== platforms
    public abstract class Platform : Entity
    {
        protected int prevX, prevY;
        protected bool playerOn;
        protected Platform(World w, int px, int py, int widthPx) : base(w, px, py)
        {
            Class = EntClass.Platform; Hurts = false; UsesSlot = false; Wd = widthPx; Ht = 8; HbX = 0; HbY = 0; HbW = widthPx; HbH = 8;
        }
        public bool Carrying(Player p) { return playerOn; }
        protected abstract void Move();
        public override void Update()
        {
            prevX = X; prevY = Y;
            var p = W.P;
            bool wasOn = playerOn && p.State == PState.Normal && !p.InAir;
            Move();
            int dx = X - prevX, dy = Y - prevY;
            if (wasOn)
            {
                p.X += dx; p.Y += dy;
                if (p.Px + 12 < Px || p.Px + 4 > Px + Wd) { playerOn = false; }
                else { p.Y = ((Py - 32) << 4) | (p.Y & 0); p.InAir = false; p.YVel = 0; }
            }
            // landing on the platform
            playerOn = false;
            if (p.State == PState.Normal && p.YVel >= 0)
            {
                int feet = p.Py + 32;
                if (p.Px + 12 >= Px && p.Px + 4 <= Px + Wd && feet >= Py && feet <= Py + 6 + Math.Max(0, (dy >> 4)))
                {
                    p.Y = (Py - 32) << 4;
                    if (p.InAir) { p.InAir = false; p.KillTally = 0; }
                    p.YVel = 0;
                    playerOn = true;
                    OnStood(p);
                }
            }
        }
        protected virtual void OnStood(Player p) { }
        public override void OnPlayerTouch(Player p) { }
        public override bool TakeHit(Dmg d, int dir) { return false; }
        public override void OnBumpBelow(int dir) { }
        protected void DrawPlank(Ppu ppu, int camX, int camY, string img)
        {
            for (int x = 0; x < Wd; x += 16) ppu.Spr(Art.Get(img), Px - camX + x, Py - camY - 8, W.ThemePal("semi"));
        }
    }

    public sealed class MovingLift : Platform
    {
        readonly bool vertical; readonly int ox, oy; int t;
        public MovingLift(World w, int px, int py, bool vertical) : base(w, px, py, 48) { this.vertical = vertical; ox = X; oy = Y; }
        protected override void Move()
        {
            t++;
            double s = Math.Sin(t * 2 * Math.PI / 240.0);
            if (vertical) Y = oy + (int)(s * 48 * 16); else X = ox + (int)(s * 56 * 16);
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            for (int x = 0; x < Wd; x += 16) ppu.Spr(Art.Get(x == 0 ? "semi.l" : x + 16 >= Wd ? "semi.r" : "semi.c"), Px - camX + x, Py - camY, W.ThemePal("semi"));
        }
    }

    public sealed class DonutLift : Platform
    {
        int stoodFor, fall;
        public DonutLift(World w, int px, int py) : base(w, px, py, 16) { }
        protected override void Move()
        {
            if (playerOn) stoodFor++; else if (fall == 0) stoodFor = Math.Max(0, stoodFor - 1);
            if (stoodFor > 30 && fall == 0) fall = 1;
            if (fall > 0) { fall++; YVel = Math.Min(0x30, YVel + 2); Y += YVel; if (Py > W.CamY + 260) Remove = true; }
        }
        public override void Draw(Ppu ppu, int camX, int camY)
        {
            int shake = stoodFor > 10 && fall == 0 ? ((W.Frame >> 1) & 1) : 0;
            ppu.Spr(Art.Get("used"), Px - camX + shake, Py - camY, W.ThemePal("used"));
        }
    }
}
