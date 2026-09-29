using System;
using System.Collections.Generic;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    public sealed partial class World
    {
        static readonly int[] ChainPts = { 100, 200, 400, 800, 1000, 2000, 4000, 8000 };

        // ------------------------------------------------------------------ score, coins, effects
        public void AddScore(int pts, int px, int py)
        {
            if (pts <= 0) return;
            S.AddScore(pts);
            Add(new ScorePopup(this, px, py, pts));
        }

        public void OneUp(int px, int py)
        {
            S.AddLife();
            Sound.Sfx(SfxId.OneUp);
            Add(new ScorePopup(this, px, py, -1));
        }

        /// <summary>Score for the n-th consecutive kill in a chain (stomps or one shell): 100 … 8000, then 1UP.</summary>
        public void ChainScore(int n, int px, int py)
        {
            if (n < 1) n = 1;
            if (n <= ChainPts.Length) AddScore(ChainPts[n - 1], px, py);
            else OneUp(px, py);
        }

        public void AddCoin()
        {
            S.AddScore(50);
            if (S.AddCoin()) { OneUp(P.Px, P.Py); }
            Sound.Sfx(SfxId.Coin);
        }

        public void Puff(int px, int py) { Add(new Puff(this, px, py)); }
        public void Splash(int px, int py) { Add(new SplashFx(this, px, py)); }
        public void SkidDust(Player p) { Add(new Dust(this, p.Px + (p.Facing > 0 ? 10 : -2), p.Py + 26)); }

        public void Shake(int ticks) { if (Host.Settings == null || Host.Settings.ScreenShake) ShakeTimer = ticks; }

        // ------------------------------------------------------------------ tiles touching the player
        public void TouchTiles(Player p)
        {
            if (ReadOnly)
            {
                int hx, hy, hw, hh; p.GetHitbox(out hx, out hy, out hw, out hh);
                for (int ty = FloorDiv(hy, 16); ty <= FloorDiv(hy + hh - 1, 16); ty++)
                    for (int tx = FloorDiv(hx, 16); tx <= FloorDiv(hx + hw - 1, 16); tx++)
                        if (TileInfo.Lava(TileAt(tx, ty)) || TileInfo.Hurts(TileAt(tx, ty))) { p.State = PState.Dying; return; }
                return;
            }
            int x, y, w, h;
            p.GetHitbox(out x, out y, out w, out h);
            int tx0 = FloorDiv(x, 16), tx1 = FloorDiv(x + w - 1, 16), ty0 = FloorDiv(y, 16), ty1 = FloorDiv(y + h - 1, 16);
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    T t = TileAt(tx, ty);
                    if (t == T.Coin) { SetTile(tx, ty, T.Empty); AddCoin(); Add(new Sparkle(this, tx * 16 + 4, ty * 16 + 4)); }
                    else if (TileInfo.Lava(t) && y + h - 4 > ty * 16 + 4) { if (!GodMode) { p.Die(false); return; } }
                }
            // hurting tiles: anything touching the body or directly under the feet
            if (p.Star == 0 && p.Statue == 0)
            {
                bool hurt = false;
                for (int ty = ty0; ty <= FloorDiv(p.Py + 32, 16) && !hurt; ty++)
                    for (int tx = FloorDiv(x - 1, 16); tx <= FloorDiv(x + w, 16); tx++)
                        if (TileInfo.Hurts(TileAt(tx, ty))) { hurt = true; break; }
                if (hurt) p.Hurt();
            }
        }

        public void StandOn(Player p, T t, int tx, int ty)
        {
            if (t == T.Note && p.NoteRide == 0 && p.State == PState.Normal)
            {
                p.NoteRide = 8;
                p.NoteSuper = p.JumpBuffer > 0 || p.Pad.P(Btn.A);
                if (ReadOnly) return;
                Add(new BumpBlock(this, tx, ty, 1, T.Note));
                Sound.Sfx(SfxId.Note);
                BumpAbove(tx, ty, 0);
            }
        }

        public bool PlatformSupport(Player p)
        {
            foreach (var e in Ents)
            {
                var pl = e as Platform;
                if (pl != null && pl.Carrying(p)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ blocks
        public void HeadBump(Player p, int tx, int ty)
        {
            T t = TileAt(tx, ty);
            HitBlock(tx, ty, p.Big, 1, true);
            if (t == T.Note) { p.YVel = 0x20; }
        }

        /// <summary>A block hit from below (head/shell/tail). Returns true if something reacted.</summary>
        public bool ReadOnly;   // reachability search: never modify the level

        public bool HitBlock(int tx, int ty, bool big, int fromBelow, bool byPlayer)
        {
            if (ReadOnly) return false;
            if (tx < 0 || ty < 0 || tx >= W || ty >= H) return false;
            int i = ty * W + tx;
            T t = Tiles[i];
            Content c = Contents[i];
            if (Hidden[i] != 0) return false;
            switch (t)
            {
                case T.HiddenBlock:
                    Tiles[i] = T.Used;
                    Release(tx, ty, c, big);
                    Contents[i] = Content.None;
                    Add(new BumpBlock(this, tx, ty, -1, T.Used));
                    BumpAbove(tx, ty, 0);
                    return true;
                case T.QBlock:
                    Release(tx, ty, c, big);
                    Contents[i] = Content.None;
                    Add(new BumpBlock(this, tx, ty, -1, T.Used));
                    BumpAbove(tx, ty, 0);
                    return true;
                case T.Brick:
                    if (c == Content.MultiCoin)
                    {
                        AddCoin(); Add(new CoinPop(this, tx * 16, ty * 16 - 16));
                        if (multiCoinStart < 0 || multiCoinTile != i) { multiCoinTile = i; multiCoinStart = Frame; multiCoinCount = 0; }
                        multiCoinCount++;
                        bool last = multiCoinCount >= 10 || Frame - multiCoinStart > 240;
                        if (last) { Contents[i] = Content.None; multiCoinTile = -1; }
                        Add(new BumpBlock(this, tx, ty, -1, last ? T.Used : T.Brick));
                        BumpAbove(tx, ty, 0);
                        return true;
                    }
                    if (c != Content.None)
                    {
                        Release(tx, ty, c, big);
                        Contents[i] = Content.None;
                        Add(new BumpBlock(this, tx, ty, -1, T.Used));
                        BumpAbove(tx, ty, 0);
                        return true;
                    }
                    if (big || !byPlayer)
                    {
                        BreakBrick(tx, ty);
                        return true;
                    }
                    Add(new BumpBlock(this, tx, ty, -1, T.Brick));
                    Sound.Sfx(SfxId.Bump);
                    BumpAbove(tx, ty, 0);
                    return true;
                case T.Note:
                case T.Wood:
                    Add(new BumpBlock(this, tx, ty, -1, t));
                    if (c != Content.None) { Release(tx, ty, c, big); Contents[i] = Content.None; }
                    Sound.Sfx(SfxId.Bump);
                    BumpAbove(tx, ty, 0);
                    return true;
                default:
                    if (TileInfo.Solid(t) && byPlayer) Sound.Sfx(SfxId.Bump);
                    return false;
            }
        }
        int multiCoinTile = -1, multiCoinStart = -1, multiCoinCount;

        public void BreakBrick(int tx, int ty)
        {
            SetTile(tx, ty, T.Empty);
            Contents[ty * W + tx] = Content.None;
            int px = tx * 16, py = ty * 16;
            Add(new Debris(this, px, py, -0x10, -0x40));
            Add(new Debris(this, px + 8, py, 0x10, -0x40));
            Add(new Debris(this, px, py + 8, -0x10, -0x28));
            Add(new Debris(this, px + 8, py + 8, 0x10, -0x28));
            S.AddScore(10);
            Sound.Sfx(SfxId.Break);
            BumpAbove(tx, ty, 0);
        }

        /// <summary>Objects standing on a bumped block are flipped/killed; coins on top are collected.</summary>
        public void BumpAbove(int tx, int ty, int dir)
        {
            int top = ty * 16;
            if (TileAt(tx, ty - 1) == T.Coin) { SetTile(tx, ty - 1, T.Empty); AddCoin(); Add(new CoinPop(this, tx * 16, (ty - 1) * 16)); }
            foreach (var e in Ents)
            {
                if (e.Remove || e.Dying) continue;
                if (e.Bottom >= top - 2 && e.Bottom <= top + 4 && e.Px + e.Wd > tx * 16 && e.Px < tx * 16 + 16)
                {
                    int d = e.Cx < tx * 16 + 8 ? -1 : 1;
                    e.OnBumpBelow(d);
                }
            }
        }

        void Release(int tx, int ty, Content c, bool big)
        {
            int px = tx * 16, py = ty * 16;
            switch (c)
            {
                case Content.Coin: AddCoin(); Add(new CoinPop(this, px, py - 16)); break;
                case Content.Flower: Add(big ? (Entity)new FireFlowerItem(this, px, py) : new Mushroom(this, px, py, false)); Sound.Sfx(SfxId.Sprout); break;
                case Content.Leaf: if (big) Add(new LeafItem(this, px, py)); else Add(new Mushroom(this, px, py, false)); Sound.Sfx(SfxId.Sprout); break;
                case Content.Star: Add(new StarItem(this, px, py)); Sound.Sfx(SfxId.Sprout); break;
                case Content.OneUp: Add(new Mushroom(this, px, py, true)); Sound.Sfx(SfxId.Sprout); break;
                case Content.Vine: Add(new VineSprout(this, tx, ty)); Sound.Sfx(SfxId.Vine); break;
                case Content.PSwitch: Add(new PSwitch(this, px, py - 16)); Sound.Sfx(SfxId.Sprout); break;
                default: Sound.Sfx(SfxId.Bump); break;
            }
        }

        // ------------------------------------------------------------------ P-switch
        public void StartPSwitch()
        {
            if (PSwitchTimer > 0) { PSwitchTimer = 128; return; }
            PSwitchTimer = 128;   // decremented every 4 ticks -> 512 ticks
            pswitched.Clear(); pswitchedMunchers.Clear();
            for (int i = 0; i < Tiles.Length; i++)
            {
                if (Tiles[i] == T.Brick && Contents[i] == Content.None) { Tiles[i] = T.Coin; pswitched.Add(i); }
                else if (Tiles[i] == T.Coin) { Tiles[i] = T.Brick; pswitched.Add(-i - 1); }
                else if (Tiles[i] == T.Muncher) { Tiles[i] = T.Coin; pswitchedMunchers.Add(i); }
            }
            Sound.Sfx(SfxId.PSwitch);
            Sound.Music("pswitch", true);
            Shake(8);
        }

        void EndPSwitch()
        {
            foreach (int k in pswitched)
            {
                if (k >= 0) { if (Tiles[k] == T.Coin) Tiles[k] = T.Brick; }
                else { int i = -k - 1; if (Tiles[i] == T.Brick) Tiles[i] = T.Coin; }
            }
            // SMB3: the swap is symmetric, so munchers whose coins weren't collected come back
            foreach (int i in pswitchedMunchers) if (Tiles[i] == T.Coin) Tiles[i] = T.Muncher;
            pswitched.Clear(); pswitchedMunchers.Clear();
            PlayAreaMusic();
        }

        // ------------------------------------------------------------------ star
        public void StarStarted() { Sound.Music("star", true); }
        public void StarEnding() { if (!Clearing) PlayAreaMusic(); }
        public void StarEnded() { }

        public void OnPlayerDying(bool pit)
        {
            Sound.StopAllSfx();
            Sound.Music("death", true);
        }

        public void LoseShoe() { Shoe = null; }

        // ------------------------------------------------------------------ player <-> objects
        void Collisions()
        {
            if (P.State != PState.Normal && P.State != PState.Vine && P.State != PState.AutoWalk) return;
            int x, y, w, h;
            P.GetHitbox(out x, out y, out w, out h);
            for (int i = 0; i < Ents.Count; i++)
            {
                var e = Ents[i];
                if (e.Remove || e.Dying) continue;
                if (e == P.Carrying) continue;
                if (e.Class == EntClass.Projectile)
                {
                    // player fireballs / hammers vs enemies
                    foreach (var o in Ents)
                    {
                        if (o.Remove || o.Dying || o.Class != EntClass.Enemy || !e.Overlaps(o)) continue;
                        var pr = e as PlayerProjectile;
                        if (pr != null && pr.HitEnemy(o)) break;
                    }
                    continue;
                }
                if (e.Class == EntClass.Effect) continue;
                if (!e.Overlaps(x, y, w, h)) continue;
                if (P.Star > 0 && e.Class == EntClass.Enemy)
                {
                    if (!e.StarImmune && e.TakeHit(Dmg.Star, P.Facing))
                    {
                        P.KillTally++;
                        Sound.Sfx(SfxId.Kick);
                        MarkKilled(e);
                        continue;
                    }
                    if (e.StarImmune && !e.Hurts) { e.OnPlayerTouch(P); }
                    continue;
                }
                if (P.Statue > 0 && e.Class == EntClass.Enemy)
                {
                    if (P.YVel > 0 && e.TakeHit(Dmg.Statue, P.Facing)) { MarkKilled(e); Sound.Sfx(SfxId.Kick); }
                    continue;
                }
                if (P.Sliding && e.Class == EntClass.Enemy && !e.ShellImmune && !(e is Boss))
                {
                    // slope sliding knocks enemies away like a kicked shell
                    if (e.TakeHit(Dmg.Shell, Math.Sign(P.XVel) == 0 ? P.Facing : Math.Sign(P.XVel))) { MarkKilled(e); P.KillTally++; Sound.Sfx(SfxId.Kick); }
                    continue;
                }
                e.OnPlayerTouch(P);
                P.GetHitbox(out x, out y, out w, out h);
            }
            // moving shells and thrown objects vs other enemies
            for (int i = 0; i < Ents.Count; i++)
            {
                var s = Ents[i];
                if (s.Remove || s.Dying || !(s.IsShell || s == P.Carrying)) continue;
                for (int j = 0; j < Ents.Count; j++)
                {
                    var o = Ents[j];
                    if (o == s || o.Remove || o.Dying || o.Class != EntClass.Enemy || o == P.Carrying) continue;
                    if (!s.Overlaps(o)) continue;
                    if (s == P.Carrying)
                    {
                        // carried object hitting an enemy: both die
                        if (o.TakeHit(Dmg.Shell, P.Facing)) { MarkKilled(o); }
                        s.KnockOff(-P.Facing); MarkKilled(s); P.Carrying = null;
                        Sound.Sfx(SfxId.Kick);
                        break;
                    }
                    if (o.IsShell && o.XVel != 0)
                    {
                        s.KnockOff(Math.Sign(s.XVel)); o.KnockOff(Math.Sign(o.XVel)); MarkKilled(s); MarkKilled(o);
                        Sound.Sfx(SfxId.Kick);
                        break;
                    }
                    if (o.ShellImmune) continue;
                    // SMB3: each enemy one shell takes out scores 100, 200, 400 … 8000, then 1UPs
                    int n = s.ShellChain + 1, basePts = o.Points;
                    o.Points = n <= ChainPts.Length ? ChainPts[n - 1] : 0;
                    if (o.TakeHit(Dmg.Shell, Math.Sign(s.XVel)))
                    {
                        s.ShellChain = n;
                        if (n > ChainPts.Length) OneUp(o.Px, o.Py);
                        MarkKilled(o);
                        Sound.Sfx(SfxId.Kick);
                    }
                    else o.Points = basePts;
                }
            }
        }

        /// <summary>SMB3 stomp rule (docs/01 §10.6). The caller decides what a stomp does.</summary>
        public bool CanStomp(Player p, Entity e)
        {
            if (p.Swimming || !e.Stompable) return false;
            bool rising = p.InAir && p.YVel < 0;
            if (rising && p.FlyTime == 0 && p.KillTally == 0) return false;
            if (!p.InAir && !(e.YVel > 0 && !e.OnGround && e.YVel < 0x0A)) return false;
            int refY = e.Bottom - 16;
            return p.Py <= refY - e.StompH;
        }

        /// <summary>Applies the stomp bounce and the chain score. Returns the chain index.</summary>
        public int StompBounce(Player p, Entity e)
        {
            p.Bounce(Phys.StompBounce);
            p.KillTally++;
            ChainScore(p.KillTally, e.Px, e.Py);
            Sound.Sfx(SfxId.Stomp);
            Host.Input.Rumble(S.PlayerIndex, 0.25f, 4);
            return p.KillTally;
        }

        public void TailHit(Player p, int timer)
        {
            int bx = p.Facing > 0 ? p.Px - 10 : p.Px + 17;
            int by = p.Py + 16;
            foreach (var e in Ents)
            {
                if (e.Remove || e.Dying || e.Class != EntClass.Enemy) continue;
                if (e.Overlaps(bx, by, 10, 15) && e.TakeHit(Dmg.Tail, -p.Facing * -1 * (p.Facing > 0 ? -1 : 1)))
                {
                    MarkKilled(e);
                    Sound.Sfx(SfxId.Kick);
                    Add(new Sparkle(this, e.Cx - 4, e.Py));
                }
            }
            if (timer == 9)
            {
                int tx = FloorDiv(p.Facing > 0 ? p.Px - 6 : p.Px + 21, 16), ty = FloorDiv(p.Py + 28, 16);
                T t = TileAt(tx, ty);
                if (t == T.Brick && ContentAt(tx, ty) == Content.None) BreakBrick(tx, ty);
                else if (TileInfo.Bumpable(t)) HitBlock(tx, ty, true, 0, true);
            }
        }

        public void StatueLanded(Player p)
        {
            foreach (var e in Ents)
            {
                if (e.Remove || e.Dying || e.Class != EntClass.Enemy) continue;
                if (e.Overlaps(p.Px + 2, p.Py + 28, 12, 8) && e.TakeHit(Dmg.Statue, p.Facing)) MarkKilled(e);
            }
        }

        public void KickCarried(Player p)
        {
            var e = p.Carrying;
            p.Carrying = null;
            if (e == null) return;
            var sh = e as Shell;
            int px = p.Px + (p.Facing > 0 ? 12 : -12);
            e.X = px << 4;
            e.Y = (p.Py + (p.Big ? 16 : 18)) << 4;
            if (TileInfo.Solid(TileAtPx(e.Cx, e.Py + 8)))
            {
                e.KnockOff(p.Facing); MarkKilled(e); Sound.Sfx(SfxId.Kick);
                return;
            }
            if (sh != null) sh.Kick(p.Facing, p.XVel);
            else { e.XVel = p.Facing * 0x30; e.YVel = -0x10; }
            p.KickPose = Phys.KickPose;
            Sound.Sfx(SfxId.Kick);
        }

        public void DropCarried(Player p)
        {
            var e = p.Carrying;
            p.Carrying = null;
            if (e == null) return;
            e.X = (p.Px + (p.Facing > 0 ? 10 : -10)) << 4;
            e.XVel = 0;
        }

        // ------------------------------------------------------------------ pipes & doors
        public void CheckTransitions(Player p, PadState pad)
        {
            if (p.State != PState.Normal || p.Carrying != null && false) return;
            int cx = p.CenterX;
            if (!p.InAir && pad.H(Btn.Down))
            {
                int fy = p.Py + 32;
                int tx = FloorDiv(cx, 16), ty = FloorDiv(fy, 16);
                T t = TileAt(tx, ty);
                if (t == T.PipeTL || t == T.PipeTR)
                {
                    int left = t == T.PipeTL ? tx : tx - 1;
                    int center = left * 16 + 16;
                    int id = LinkAt(left, ty); if (id < 0) id = LinkAt(left + 1, ty);
                    if (id >= 0 && Math.Abs(cx - center) <= 5) { StartPipe(p, 2, id, center - 8, p.Py); return; }
                }
            }
            if (pad.H(Btn.Up))
            {
                int hy = p.Py + (p.Big ? 4 : 14);
                int tx = FloorDiv(cx, 16), ty = FloorDiv(hy - 2, 16);
                T t = TileAt(tx, ty);
                if ((t == T.PipeBL || t == T.PipeBR) && (p.InAir || true))
                {
                    int left = t == T.PipeBL ? tx : tx - 1;
                    int center = left * 16 + 16;
                    int id = LinkAt(left, ty); if (id < 0) id = LinkAt(left + 1, ty);
                    if (id >= 0 && Math.Abs(cx - center) <= 5 && hy - 2 - (ty * 16 + 15) <= 2) { StartPipe(p, 8, id, center - 8, p.Py); return; }
                }
                if (!p.InAir && pad.P(Btn.Up))
                {
                    int by = p.Py + 24;
                    int dx = FloorDiv(cx, 16), dy = FloorDiv(by, 16);
                    if (TileAt(dx, dy) == T.DoorBot || TileAt(dx, dy) == T.DoorTop)
                    {
                        if (TileAt(dx, dy) == T.DoorTop) dy++;
                        int id = LinkAt(dx, dy);
                        if (id >= 0) { StartDoor(p, id, dx * 16); return; }
                    }
                }
            }
            if (!p.InAir && (pad.H(Btn.Right) || pad.H(Btn.Left)))
            {
                int dir = pad.H(Btn.Right) ? 1 : -1;
                int sx = dir > 0 ? p.Px + 16 : p.Px - 1;
                int fy = p.Py + 31;
                int tx = FloorDiv(sx, 16), ty = FloorDiv(fy, 16);
                T t = TileAt(tx, ty);
                if (t == T.HPipeMouthB || t == T.HPipeMouthT)
                {
                    int topRow = t == T.HPipeMouthB ? ty - 1 : ty;
                    bool facesLeft = Var[topRow * W + tx] == 0;
                    if ((dir > 0 && facesLeft) || (dir < 0 && !facesLeft))
                    {
                        int id = LinkAt(tx, topRow); if (id < 0) id = LinkAt(tx, topRow + 1);
                        if (id >= 0 && t == T.HPipeMouthB) { StartPipe(p, dir > 0 ? 6 : 4, id, p.Px, p.Py); return; }
                    }
                }
            }
        }

        int LinkAt(int tx, int ty) { return tx < 0 || ty < 0 || tx >= W || ty >= H ? -1 : Area.Link[ty * W + tx]; }

        void StartPipe(Player p, int dir, int id, int px, int py)
        {
            var l = Def.FindLink(AreaIndex, id);
            if (l == null) return;
            p.State = PState.Pipe;
            p.PipeDir = dir; p.PipeTimer = 0; p.PipeExiting = false;
            p.PipeTargetArea = l.ToArea; p.PipeTargetId = l.ToId;
            p.X = px << 4; p.XVel = 0; p.YVel = 0;
            p.Ducking = false;
            if (p.Carrying != null) { p.Carrying.Remove = true; p.Carrying = null; }
            Sound.Sfx(SfxId.Pipe);
        }

        void StartDoor(Player p, int id, int px)
        {
            var l = Def.FindLink(AreaIndex, id);
            if (l == null) return;
            p.State = PState.Door; p.PipeTimer = 0; p.PipeExiting = false;
            p.PipeTargetArea = l.ToArea; p.PipeTargetId = l.ToId;
            p.X = px << 4; p.XVel = 0; p.YVel = 0;
            Sound.Sfx(SfxId.Door);
        }

        void PipeTick()
        {
            var p = P;
            p.PipeTimer++;
            if (p.State == PState.Door)
            {
                if (!p.PipeExiting)
                {
                    if (p.PipeTimer >= 24) { p.Invisible = 2; }
                    if (p.PipeTimer >= 40) ArriveAt(p.PipeTargetArea, p.PipeTargetId, true);
                }
                else if (p.PipeTimer >= 20) { p.State = PState.Normal; p.InAir = false; }
                return;
            }
            int step = 8; // 0.5 px per tick
            int dx = 0, dy = 0;
            switch (p.PipeDir) { case 2: dy = step; break; case 8: dy = -step; break; case 6: dx = step; break; case 4: dx = -step; break; }
            if (!p.PipeExiting)
            {
                p.X += dx; p.Y += dy;
                if (p.PipeDir == 6 || p.PipeDir == 4) { p.AnimTick--; if (p.AnimTick <= 0) { p.AnimTick = 4; p.AnimFrame = (p.AnimFrame + 1) & 3; } }
                if (p.PipeTimer >= 60) ArriveAt(p.PipeTargetArea, p.PipeTargetId, false);
            }
            else
            {
                p.X += dx; p.Y += dy;
                if (p.PipeDir == 6 || p.PipeDir == 4) { p.AnimTick--; if (p.AnimTick <= 0) { p.AnimTick = 4; p.AnimFrame = (p.AnimFrame + 1) & 3; } }
                if (p.PipeTimer >= p.PipeExitTicks) { p.State = PState.Normal; p.InAir = true; p.YVel = 0; }
            }
        }

        /// <summary>Moves the player to link marker 'id' in area 'area' and starts the exit animation.</summary>
        void ArriveAt(int area, int id, bool door)
        {
            var p = P;
            if (area != AreaIndex || true)
            {
                LoadArea(area);
            }
            // find the marker
            int mx = -1, my = -1;
            for (int y = 0; y < H && mx < 0; y++) for (int x = 0; x < W; x++) if (Area.Link[y * W + x] == id) { mx = x; my = y; break; }
            if (mx < 0) { mx = 2; my = H - 3; }
            T t = TileAt(mx, my);
            p.PipeExiting = true;
            p.PipeTimer = 0;
            p.Invisible = 0;
            int big = p.Big ? 1 : 0;
            if (t == T.PipeTL || t == T.PipeTR)
            {
                int left = t == T.PipeTL ? mx : mx - 1;
                p.X = (left * 16 + 8) << 4;
                p.Y = (my * 16 - (big == 1 ? 0 : 16)) << 4;       // inside the pipe, head just below the lip
                p.Y = (my * 16) << 4;
                p.PipeDir = 8; p.PipeExitTicks = 64;
                p.State = PState.Pipe;
                Sound.Sfx(SfxId.Pipe);
            }
            else if (t == T.PipeBL || t == T.PipeBR)
            {
                int left = t == T.PipeBL ? mx : mx - 1;
                p.X = (left * 16 + 8) << 4;
                p.Y = ((my + 1) * 16 - 32 - 8) << 4;
                p.PipeDir = 2; p.PipeExitTicks = 40;
                p.State = PState.Pipe;
                Sound.Sfx(SfxId.Pipe);
            }
            else if (t == T.HPipeMouthT || t == T.HPipeMouthB)
            {
                int topRow = t == T.HPipeMouthB ? my - 1 : my;
                bool facesLeft = Var[topRow * W + mx] == 0;
                p.X = (facesLeft ? mx * 16 + 8 : mx * 16 - 8) << 4;
                p.Y = ((topRow + 2) * 16 - 32) << 4;
                p.PipeDir = facesLeft ? 4 : 6; p.PipeExitTicks = 40;
                p.Facing = facesLeft ? -1 : 1;
                p.State = PState.Pipe;
                Sound.Sfx(SfxId.Pipe);
            }
            else if (t == T.DoorBot || t == T.DoorTop)
            {
                if (t == T.DoorTop) my++;
                p.X = (mx * 16) << 4;
                p.Y = ((my + 1) * 16 - 32) << 4;
                p.State = PState.Door;
                Sound.Sfx(SfxId.Door);
            }
            else
            {
                p.X = (mx * 16) << 4;
                p.Y = ((my + 1) * 16 - 32) << 4;
                p.State = PState.Normal;
                p.InAir = true;
            }
            CenterCamera(false);
            UpdateCameraAfterArrive();
            SpawnInitial();
            PlayAreaMusic();
        }

        void UpdateCameraAfterArrive()
        {
            int maxY = LevelPxH - 192;
            if (Area.Scroll == "normal" || Area.Scroll == "auto" || Area.Scroll == "lock")
            {
                CamY = maxY;
                if (P.Py - CamY < 16 && Area.Scroll != "lock") CamY = Math.Max(Math.Min(0, maxY), P.Py - 72);
            }
        }

        public bool OnClimbOffTop(Player p)
        {
            // vines that leave the top of an area can link to a sky/bonus area via marker 9
            var l = Def.FindLink(AreaIndex, 9);
            if (l == null) { p.Y = -16 << 4; return false; }
            p.State = PState.Normal; p.Climbing = false;
            ArriveAt(l.ToArea, l.ToId, false);
            return true;
        }

        // ------------------------------------------------------------------ goal / course clear
        public void StartClear(int card, LevelResult result)
        {
            if (Clearing) return;
            Clearing = true;
            CardGot = card;
            clearResult = result;
            Sound.StopAllSfx();
            string jingle = result == LevelResult.FortressCleared ? "fortressclear" : result == LevelResult.WorldCleared ? "worldclear" : "clear";
            Sound.Music(jingle, true);
            if (result == LevelResult.Cleared && Def.Kind != "battle") { P.State = PState.AutoWalk; }
            EndTimer = 1;
            tallyDone = false;
            if (P.Star > 0) P.Star = 0;
            P.HurtInv = 0;
        }
        LevelResult clearResult;
        bool tallyDone;
        int bonusLives;

        void AutoWalkTick()
        {
            var pad = new PadState();
            pad.Held = Btn.Right;
            P.State = PState.Normal;
            P.Control(pad);
            P.State = PState.AutoWalk;
        }

        void EndTick(PadState pad)
        {
            EndTimer++;
            Wiggly = Phys.NextWiggly(Wiggly);
            // keep the player walking off (course clear) with normal physics
            if (clearResult == LevelResult.Cleared && Def.Kind != "battle" && P.Px - CamX < 300)
            {
                AutoWalkTick();
                P.PowerUpdate();
                P.DetectSolids();
                P.Animate();
                P.Timers();
            }
            foreach (var e in Ents) if (e.Class == EntClass.Effect || e is CardFly) e.Update();
            FlushAdds();
            Ents.RemoveAll(e => e.Remove);
            if (EndTimer == 70)
            {
                Banner = clearResult == LevelResult.FortressCleared ? "FORTRESS CLEAR!" : clearResult == LevelResult.WorldCleared ? "AIRSHIP CLEAR!" : Def.Kind == "battle" ? "BATTLE WON!" : "COURSE CLEAR!";
                if (CardGot >= 0) { Banner2 = "YOU GOT A CARD"; BannerCard = CardGot; }
            }
            if (EndTimer > 200 && !tallyDone)
            {
                if (Time > 0)
                {
                    int n = Math.Min(Time, 3);
                    Time -= n;
                    S.AddScore(50 * n);
                    if ((EndTimer & 3) == 0) Sound.Sfx(SfxId.Tally);
                }
                else
                {
                    tallyDone = true;
                    tallyEnd = EndTimer;
                    if (CardGot >= 0)
                    {
                        bonusLives = S.AddCard(CardGot);
                        if (bonusLives > 0) { Sound.Music("bonus1up", true); Banner2 = bonusLives + "UP BONUS!"; for (int i = 0; i < bonusLives; i++) S.AddLife(); }
                    }
                }
            }
            if (tallyDone && EndTimer > tallyEnd + (bonusLives > 0 ? 200 : 60) && (Sound.MusicFinished || EndTimer > tallyEnd + 400))
                Result = clearResult;
        }
        int tallyEnd;
    }
}
