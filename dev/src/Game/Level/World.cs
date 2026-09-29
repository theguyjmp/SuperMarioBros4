using System;
using System.Collections.Generic;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    public enum LevelResult { None, Cleared, Died, TimeUp, Exited, FortressCleared, WorldCleared, GameCleared }

    /// <summary>Runtime state of one level (all its areas) with the player in it.</summary>
    public sealed partial class World
    {
        public readonly LevelDef Def;
        public readonly Session S;
        public AreaDef Area;
        public int AreaIndex;
        public T[] Tiles; public Content[] Contents; public byte[] Var; public byte[] Hidden;
        public int W, H;
        public int LevelPxW { get { return W * 16; } }
        public int LevelPxH { get { return H * 16; } }
        public Player P;
        public readonly List<Entity> Ents = new List<Entity>();
        readonly List<Entity> adds = new List<Entity>();
        public int CamX, CamY;
        public int Frame, Wiggly;
        public int HaltGame;
        public bool Modern;
        public bool ReduceFlashing;
        public bool GodMode;
        public Entity Shoe;
        public LevelResult Result = LevelResult.None;
        public int Time, TimeTick = 40;
        public bool HurryPlayed;
        public int PSwitchTimer;
        readonly List<int> pswitched = new List<int>();
        readonly List<int> pswitchedMunchers = new List<int>();
        bool[] spawned, killed;
        int lastColR = int.MinValue, lastColL = int.MaxValue, lastRowD = int.MinValue, lastRowU = int.MaxValue;
        int prevCamX, prevCamY;
        public bool CamLockedV = true;
        public int ShakeTimer;
        public int EndTimer;
        public bool Clearing, Tallying;
        public int CardGot = -1;
        public int GoalRoulette;
        public string Banner = "", Banner2 = "";
        public int BannerCard = -1;
        public bool BossArena;
        public int AutoScrollX16;
        readonly Dictionary<int, bool[]> areaKilled = new Dictionary<int, bool[]>();

        public World(LevelDef def, Session s)
        {
            Def = def; S = s;
            Modern = Host.Settings == null || Host.Settings.ModernFeel;
            ReduceFlashing = Host.Settings != null && Host.Settings.ReduceFlashing;
            Time = def.Time;
            P = new Player(this, s.Form);
            P.Luigi = s.PlayerIndex == 1;
            P.PWing = s.UsePWing;
            if (s.UsePWing) { P.Form = Form.Raccoon; P.Power = Phys.PowerFull; P.FlyTime = Phys.FlyTimePWing; }
            if (s.StartStar) { P.Star = Phys.StarTicks; }
            s.UsePWing = false; s.StartStar = false;
            LoadArea(def.StartArea);
            P.X = (def.StartX * 16) << 4;
            P.Y = ((def.StartY + 1) * 16 - 32) << 4;
            P.InAir = true;
            CenterCamera(true);
            SpawnInitial();
            PlayAreaMusic();
            if (P.Star > 0) Sound.Music("star", true);
        }

        public static int FloorDiv(int a, int b) { return a >= 0 ? a / b : -((-a + b - 1) / b); }

        // ------------------------------------------------------------------ areas
        public void LoadArea(int index)
        {
            if (Area != null) areaKilled[AreaIndex] = killed;
            AreaIndex = index;
            Area = Def.Areas[index];
            W = Area.W; H = Area.H;
            Tiles = (T[])Area.Tiles.Clone();
            Contents = (Content[])Area.Contents.Clone();
            Var = (byte[])Area.Var.Clone();
            Hidden = new byte[W * H];
            Ents.Clear(); adds.Clear();
            spawned = new bool[Area.Spawns.Count];
            bool[] k;
            killed = areaKilled.TryGetValue(index, out k) ? k : new bool[Area.Spawns.Count];
            lastColR = int.MinValue; lastColL = int.MaxValue; lastRowD = int.MinValue; lastRowU = int.MaxValue;
            BossArena = false;
            BossArenaFrozen = false;
            AutoScrollX16 = 0;
            pswitched.Clear(); pswitchedMunchers.Clear();
            if (PSwitchTimer > 0) { PSwitchTimer = 0; }
            palCache.Clear();
            if (Area.GoalX >= 0) Ents.Add(new GoalBox(this, Area.GoalX * 16, Area.GoalY * 16));
        }

        public void PlayAreaMusic()
        {
            if (P.Star > 0) return;
            if (PSwitchTimer > 0) { Sound.Music("pswitch"); return; }
            string m = Area.Music;
            if (!Sound.HasSong(m)) m = "overworld";
            Sound.Music(m);
            Sound.SetMusicSpeed(HurryPlayed ? 1.3 : 1.0);
        }

        public bool IsVertical { get { return Area.Scroll == "vertical"; } }
        public bool IsAuto { get { return Area.Scroll == "auto"; } }

        public void CenterCamera(bool snapV)
        {
            CamX = Clamp(P.Px + 8 - 128, 0, Math.Max(0, LevelPxW - 256));
            int maxY = LevelPxH - 192;
            if (IsVertical || Area.Scroll == "free") CamY = Clamp(P.Py - 72, Math.Min(0, maxY), maxY);
            else CamY = maxY;
            // autoscroll resumes from wherever the player arrives (level start or a pipe back from a detour)
            if (IsAuto) AutoScrollX16 = CamX << 4;
            prevCamX = CamX; prevCamY = CamY;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        // ------------------------------------------------------------------ tiles
        public T TileAt(int tx, int ty)
        {
            if (tx < 0 || tx >= W) return ty < 0 ? T.Empty : T.Barrier;
            if (ty < 0 || ty >= H) return T.Empty;
            return Tiles[ty * W + tx];
        }

        public T TileAtPx(int px, int py) { return TileAt(FloorDiv(px, 16), FloorDiv(py, 16)); }

        public void SetTile(int tx, int ty, T t)
        {
            if (tx < 0 || ty < 0 || tx >= W || ty >= H) return;
            Tiles[ty * W + tx] = t;
        }

        public Content ContentAt(int tx, int ty) { return tx < 0 || ty < 0 || tx >= W || ty >= H ? Content.None : Contents[ty * W + tx]; }

        public bool InWater(int px, int py)
        {
            if (Area.WaterRow < 0) return false;
            return FloorDiv(py, 16) >= Area.WaterRow;
        }

        /// <summary>0 = normal ground, 1 = snow/ice-world ground, 2 = ice block (docs/01 §5.5).</summary>
        public int IceUnder(Player p)
        {
            if (p.InAir) return 0;
            T a = TileAtPx(p.Px + 4, p.Py + 32), b = TileAtPx(p.Px + 11, p.Py + 32);
            if (a == T.Ice || b == T.Ice) return 2;
            if (Area.Theme == "ice" && (a == T.Ground || b == T.Ground)) return 1;
            return 0;
        }

        // ------------------------------------------------------------------ main tick
        public void Tick(PadState pad)
        {
            Frame++;
            if (EndTimer > 0) { EndTick(pad); return; }
            if (P.State == PState.Dying)
            {
                P.DyingTick();
                if (P.DeathTimer > 0x30 + 150 || (P.DeathTimer > 0x30 && P.Py - CamY > 260 && P.DeathTimer > 0x30 + 100)) Result = LevelResult.Died;
                return;
            }
            if (P.State == PState.Pipe || P.State == PState.Door) { PipeTick(); return; }
            if (HaltGame > 0)
            {
                HaltGame--;
                if (P.Transform > 0) { P.Transform--; }
                return;
            }
            Wiggly = Phys.NextWiggly(Wiggly);

            // Player: control -> P-meter -> camera -> collision (docs/01 §3)
            if (P.State == PState.AutoWalk) AutoWalkTick();
            else { P.Control(pad); CheckTransitions(P, pad); if (P.State == PState.Pipe || P.State == PState.Door) return; }
            P.PowerUpdate();
            // P-meter whistle: loops while running at full P on the ground
            bool pSound = P.Power == Phys.PowerFull && !P.InAir && P.State == PState.Normal && Math.Abs(P.XVel) >= Phys.RunCap;
            if (pSound != pSoundOn) { if (pSound) Sound.Sfx(SfxId.PMeter); else Sound.StopSfx(SfxId.PMeter); pSoundOn = pSound; }
            UpdateCamera();
            P.DetectSolids();
            ClampPlayerToScreen();
            P.Timers();
            P.Animate();
            if (P.State == PState.Normal && P.Py > LevelPxH + 16) { P.Die(true); return; }

            Spawner();
            for (int i = 0; i < Ents.Count; i++)
            {
                var e = Ents[i];
                if (!e.Remove) e.Update();
            }
            FlushAdds();
            Collisions();
            Despawn();
            Ents.RemoveAll(e => e.Remove);
            TimersTick();
            if (Def.Kind == "battle" && !battleWon && Frame > 60 && !Clearing)
            {
                bool any = false;
                foreach (var e in Ents) if (e.Class == EntClass.Enemy && !e.Dying && !e.Remove) { any = true; break; }
                if (!any) { battleWon = true; Add(new TreasureChest(this, CamX + 120, CamY + 16)); Sound.Music("bosswin", true); }
            }
            if (ShakeTimer > 0) ShakeTimer--;
        }

        void FlushAdds()
        {
            if (adds.Count == 0) return;
            Ents.AddRange(adds);
            adds.Clear();
        }

        public void Add(Entity e) { adds.Add(e); }

        public int CountClass(EntClass c)
        {
            int n = 0;
            foreach (var e in Ents) if (e.Class == c && !e.Remove) n++;
            foreach (var e in adds) if (e.Class == c) n++;
            return n;
        }

        public int CountPlayerProjectiles() { return CountClass(EntClass.Projectile); }

        void TimersTick()
        {
            if (Clearing) return;
            // level timer: one unit per 41 ticks (reload 40, fire on underflow); pauses in pipes
            if (Time > 0 && Def.Time > 0 && !BossArenaFrozen)
            {
                if (--TimeTick < 0)
                {
                    TimeTick = 40;
                    Time--;
                    if (Time == 100 && !HurryPlayed)
                    {
                        HurryPlayed = true;
                        Sound.Music("hurry", true);
                        hurryWait = 150;
                    }
                    if (Time == 0 && P.State == PState.Normal) { P.Die(false); Result = LevelResult.None; }
                }
            }
            if (hurryWait > 0 && --hurryWait == 0) { PlayAreaMusic(); Sound.SetMusicSpeed(1.3); }
            if (PSwitchTimer > 0 && (Frame & 3) == 0)
            {
                PSwitchTimer--;
                if (PSwitchTimer == 0) EndPSwitch();
            }
        }
        int hurryWait;
        bool pSoundOn, battleWon;
        public bool BossArenaFrozen;

        public void StopLoopingSounds() { if (pSoundOn) { Sound.StopSfx(SfxId.PMeter); pSoundOn = false; } }

        // ------------------------------------------------------------------ camera (docs/01 §12)
        void UpdateCamera()
        {
            prevCamX = CamX; prevCamY = CamY;
            int maxX = Math.Max(0, LevelPxW - 256);
            if (IsAuto && !BossArena)
            {
                AutoScrollX16 += Area.AutoSpeed;
                CamX = Math.Min(maxX, AutoScrollX16 >> 4);
            }
            else if (!BossArena)
            {
                int sx = P.Px - CamX;
                if (sx > 0x80) CamX = P.Px - 0x80;
                else if (sx < 0x70) CamX = P.Px - 0x70;
                CamX = Clamp(CamX, 0, maxX);
            }
            int maxY = LevelPxH - 192;
            int minY = Math.Min(0, maxY);
            string mode = Area.Scroll;
            if (mode == "lock" || maxY <= 0) { CamY = maxY; return; }
            bool free = mode == "free" || mode == "vertical" || P.FlyTime != 0 || P.State == PState.Vine;
            if (mode == "normal" || mode == "auto")
            {
                if (CamY >= maxY && !free) { CamY = maxY; return; }
            }
            int sy = P.Py - CamY;
            if (!P.Dead)
            {
                if (sy < 48) CamY = Math.Max(CamY - 3, P.Py - 48);
                else if (sy > 88) CamY = mode == "vertical" ? Math.Min(CamY + 4, P.Py - 88) : P.Py - 88;
            }
            CamY = Clamp(CamY, Math.Max(minY, -128), maxY);
        }

        void ClampPlayerToScreen()
        {
            if (P.State != PState.Normal) return;
            int sx = P.Px - CamX;
            if (IsAuto && !BossArena)
            {
                if (sx < 16) { P.X = (CamX + 16) << 4; if (TileInfo.Solid(TileAtPx(P.Px + 14, P.Py + 20))) { P.Die(false); return; } }
                if (sx > 224) P.X = (CamX + 224) << 4;
                return;
            }
            // SMB3: the player's screen X stays within [16, 232] (only reachable at the level's edges,
            // since the camera otherwise keeps him in its 112-128 dead zone). Whole-pixel velocity is dropped.
            int lo = CamX + 16, hi = CamX + 232;
            if (P.Px < lo) { P.X = lo << 4; if (P.XVel < 0) P.XVel %= 16; }
            if (P.Px > hi) { P.X = hi << 4; if (P.XVel > 0) P.XVel %= 16; }
            if (BossArena)
            {
                if (P.Px < CamX + 8) { P.X = (CamX + 8) << 4; if (P.XVel < 0) P.XVel = 0; }
                if (P.Px > CamX + 232) { P.X = (CamX + 232) << 4; if (P.XVel > 0) P.XVel = 0; }
            }
            if (P.Py < -128) P.Y = -128 << 4;
        }

        // ------------------------------------------------------------------ spawning (docs/03 §7.1)
        void SpawnInitial()
        {
            int c0 = FloorDiv(CamX - 32, 16), c1 = FloorDiv(CamX + 272, 16);
            int r0 = FloorDiv(CamY - 32, 16), r1 = FloorDiv(CamY + 208, 16);
            for (int i = 0; i < Area.Spawns.Count; i++)
            {
                var s = Area.Spawns[i];
                if (s.X >= c0 && s.X <= c1 && (!IsVertical || (s.Y >= r0 && s.Y <= r1))) TrySpawn(i);
            }
            lastColR = c1; lastColL = c0; lastRowD = r1; lastRowU = r0;
            FlushAdds();
        }

        void Spawner()
        {
            if (IsVertical)
            {
                int rd = FloorDiv(CamY + 288 - 96, 16), ru = FloorDiv(CamY - 32, 16);
                if (CamY > prevCamY || rd > lastRowD) { for (int r = Math.Max(lastRowD + 1, rd - 2); r <= rd; r++) SpawnRow(r); lastRowD = Math.Max(lastRowD, rd); }
                if (CamY < prevCamY || ru < lastRowU) { for (int r = Math.Min(lastRowU - 1, ru + 2); r >= ru; r--) SpawnRow(r); lastRowU = Math.Min(lastRowU, ru); }
                lastRowD = Math.Min(lastRowD, rd + 1); lastRowU = Math.Max(lastRowU, ru - 1);
                // also cover horizontal within the single screen width
                return;
            }
            int cr = FloorDiv(CamX + 272, 16), cl = FloorDiv(CamX - 32, 16);
            if (cr > lastColR) { for (int c = Math.Max(lastColR + 1, cr - 3); c <= cr; c++) SpawnColumn(c); lastColR = cr; }
            if (cl < lastColL) { for (int c = Math.Min(lastColL - 1, cl + 3); c >= cl; c--) SpawnColumn(c); lastColL = cl; }
            // the windows follow the camera, so moving back re-arms the columns behind us
            if (cr < lastColR) lastColR = cr;
            if (cl > lastColL) lastColL = cl;
        }

        void SpawnColumn(int col)
        {
            var sp = Area.Spawns;
            for (int i = 0; i < sp.Count; i++) if (sp[i].X == col) TrySpawn(i);
        }

        void SpawnRow(int row)
        {
            var sp = Area.Spawns;
            for (int i = 0; i < sp.Count; i++) if (sp[i].Y == row) TrySpawn(i);
        }

        void TrySpawn(int i)
        {
            if (spawned[i] || killed[i]) return;
            var s = Area.Spawns[i];
            Entity e = EntityFactory.Create(this, s);
            if (e == null) { spawned[i] = true; return; }
            if (e.UsesSlot && e.Class == EntClass.Enemy && CountClass(EntClass.Enemy) >= 5) return; // SMB3: spawn silently fails
            e.SpawnIndex = i;
            spawned[i] = true;
            Add(e);
        }

        void Despawn()
        {
            foreach (var e in Ents)
            {
                if (e.Remove || e.Class == EntClass.Effect || e.Class == EntClass.Special || e.Class == EntClass.Platform && e.SpawnIndex < 0) continue;
                if (e == P.Carrying) continue;
                bool off;
                if (IsVertical) off = e.Py < CamY - 80 || e.Py > CamY + 320;
                else off = e.Px < CamX - 128 || e.Px > CamX + 384;
                if (e.Class == EntClass.Projectile || e.Class == EntClass.EnemyProjectile)
                    off = e.Px < CamX - 32 || e.Px > CamX + 288 || e.Py > CamY + 240 || e.Py < CamY - 128;
                if (!off) continue;
                e.Remove = true;
                if (e.SpawnIndex >= 0 && e.SpawnIndex < spawned.Length)
                {
                    if (e.Killed) killed[e.SpawnIndex] = true;
                    else spawned[e.SpawnIndex] = false;
                }
            }
            foreach (var e in Ents)
                if (e.Remove && e.Killed && e.SpawnIndex >= 0 && e.SpawnIndex < killed.Length) killed[e.SpawnIndex] = true;
        }

        public void MarkKilled(Entity e) { e.Killed = true; if (e.SpawnIndex >= 0 && e.SpawnIndex < killed.Length) killed[e.SpawnIndex] = true; }

        /// <summary>Tools: spawn every object in the area (for overview renders).</summary>
        public void DebugSpawnAll()
        {
            Ents.Clear();
            if (Area.GoalX >= 0) Ents.Add(new GoalBox(this, Area.GoalX * 16, Area.GoalY * 16));
            for (int i = 0; i < Area.Spawns.Count; i++) { var e = EntityFactory.Create(this, Area.Spawns[i]); if (e != null) Ents.Add(e); }
        }
    }
}
