using System;
using SMB4.Audio;
using SMB4.Platform;

namespace SMB4.Game
{
    public enum PState { Normal, Pipe, Door, Dying, AutoWalk, Frozen, Vine }

    /// <summary>
    /// The player, implemented rule-for-rule from docs/01-game-feel-and-physics.md.
    /// Positions/velocities are integers in 1/16 px (per tick). (X, Y) is the top-left of a 16x32 box
    /// for every form; small Mario's graphics occupy the lower 16 px.
    /// </summary>
    public sealed partial class Player
    {
        public World W;
        public int X, Y, XVel, YVel;
        public bool InAir = true;
        public int Power, PMeterCnt, FlyTime, WagCount, AllowAirJump, SlideRate;
        public bool Ducking, Sliding;
        public int Slide;
        public int KillTally;
        public int Facing = 1;
        public Form Form = Form.Small;
        public bool Luigi;
        public PState State = PState.Normal;

        // timers
        public int HurtInv, Star, TailAttack, Statue, ThrowPose, KickPose, JumpBuffer;
        public int Transform, TransformKind; // freeze timer while changing form (1 grow, 2 shrink, 3 poof, 4 flower flash)
        public Form TransformFrom, TransformTo;
        public int DeathTimer, PipeTimer, PipeDir, PipePhase;
        public int PipeTargetArea, PipeTargetId;
        public bool PipeExiting;
        public int MicroGoombas;
        public bool Swimming, Climbing;
        public int SwimAnim, ClimbAnim;
        public Entity Carrying;
        public bool PWing;
        public int NoteRide; public bool NoteSuper, NoLowGrav;
        public int OnSlope;          // downhill push of the slope we stand on (-3/+3), 0 = flat
        public int Conveyor;         // -16/0/+16 position push from a conveyor belt
        public int PipeExitTicks = 64;
        public int Stun;
        public int Invisible;        // frames drawn hidden (e.g. behind a door)
        public bool BehindScenery;
        public int WhiteBlockTimer, BehindTimer;
        public int AutoWalkDir = 1;

        // animation
        public int AnimTick, AnimFrame, RunAnim;
        public bool Skidding;
        public bool PJumpPose;
        public int StompCombo;

        public PadState Pad;
        public bool Dead { get { return State == PState.Dying; } }
        public bool Big { get { return Form != Form.Small; } }
        public bool TailSuit { get { return Form == Form.Raccoon || Form == Form.Tanooki; } }
        public int Px { get { return X >> 4; } }
        public int Py { get { return Y >> 4; } }
        bool SmallBox { get { return !Big || Ducking || Sliding; } }

        public Player(World w, Form form) { W = w; Form = form; }

        /// <summary>Shallow copy of the whole physics state (used by the reachability checker).</summary>
        public Player Clone() { return (Player)MemberwiseClone(); }

        // ------------------------------------------------------------------------------ helpers
        bool SolidPx(int x, int y) { return TileInfo.Solid(W.TileAtPx(x, y)); }
        static int Sign(int v) { return v > 0 ? 1 : v < 0 ? -1 : 0; }

        public void GetHitbox(out int x, out int y, out int w, out int h)
        {
            if (SmallBox) { x = Px + 4; w = 8; y = Py + 17; h = 13; }
            else { x = Px + 3; w = 10; y = Py + 5; h = 25; }
        }

        public int CenterX { get { return Px + 8; } }
        public int FeetY { get { return Py + 32; } }

        // ------------------------------------------------------------------------------ main control (docs/01 §3 step 2)
        public void Control(PadState pad)
        {
            if (Stun > 0) { Stun--; pad = new PadState(); }
            Pad = pad;
            if (State != PState.Normal && State != PState.Vine) return;
            bool modern = W.Modern;

            // riding a note block: a new A press during the ride gives the super bounce
            if (NoteRide > 0)
            {
                if (pad.P(Btn.A)) NoteSuper = true;
                NoteRide--;
                X += XVel;
                YVel = 0; InAir = false;
                if (NoteRide == 0)
                {
                    YVel = NoteSuper ? Phys.NoteSuper : Phys.NoteLaunch;
                    InAir = true; NoLowGrav = true; NoteSuper = false;
                    Sound.Sfx(SfxId.Spring);
                }
                return;
            }

            // Modern: jump buffer — a press remembered while airborne counts on the first grounded tick
            bool aPressed = pad.P(Btn.A);
            bool buffered = false;
            if (!aPressed && modern && JumpBuffer > 0 && !InAir) { aPressed = true; buffered = true; }
            if (JumpBuffer > 0) JumpBuffer--;
            if (buffered) JumpBuffer = 0;

            if (State == PState.Vine) { ClimbControl(pad, aPressed); return; }

            // ---- 1. duck / low clearance / water / vine
            UpdateWaterState();
            if (!InAir)
            {
                bool canDuck = Big && Form != Form.Frog && Carrying == null && !Sliding && Statue == 0 && !Swimming;
                Ducking = canDuck && pad.H(Btn.Down);
            }
            bool lowClear = false;
            if (Big && !Ducking && !InAir && Statue == 0 && SolidPx(Px + 8, Py + 10))
            {
                // stuck under a 1-tile ceiling: SMB3 pushes Mario right 1 px/tick, jumping disabled
                lowClear = true;
                XVel = 0;
                X += 16;
            }
            if (!Swimming && TryGrabVine(pad)) return;

            // slope slide (docs/01 §5.6): Down on a slope, not Frog/Hammer; starts from ~0 speed
            if (!InAir && OnSlope != 0 && pad.H(Btn.Down) && !Sliding && Form != Form.Frog && Form != Form.Hammer && Carrying == null && Statue == 0 && !Swimming)
            {
                Sliding = true; Slide = 0; Ducking = false;
            }
            if (Sliding && (InAir || Swimming || pad.H(Btn.Left) || pad.H(Btn.Right) || pad.H(Btn.Up))) Sliding = false;

            // ---- 2. X move (SlideRate only moves the position)
            SlideRate = InAir ? 0 : (Sliding ? 0 : OnSlope) + (W.PSwitchTimer > 0 ? 0 : Conveyor);
            XVel += SlideRate;
            if (XVel > Phys.AbsCap) XVel = Phys.AbsCap; else if (XVel < -Phys.AbsCap) XVel = -Phys.AbsCap;
            X += XVel;
            XVel -= SlideRate;

            // ---- 3. direction / magnitude
            int moveDir = Sign(XVel);

            // ---- 4. Y move (only if in the air on the previous tick)
            if (InAir) Y += Math.Min(YVel, Phys.FallCap);

            // ---- 5. ground animations (skid)
            int dir = 0;
            if (pad.H(Btn.Left)) dir = -1; else if (pad.H(Btn.Right)) dir = 1;
            Skidding = !InAir && !Swimming && dir != 0 && moveDir != 0 && dir != moveDir && Math.Abs(XVel) >= 2;
            if (Skidding && W.Frame % 8 == 0) { Sound.Sfx(SfxId.Skid); W.SkidDust(this); }

            // facing follows L/R (also mid-air), except during a tail spin
            if (dir != 0 && TailAttack == 0 && Statue == 0) Facing = dir;

            // ---- 6. horizontal control
            if (Statue > 0) { XVel = 0; }
            else if (Sliding)
            {
                Slide += OnSlope;
                if (Slide > Phys.AbsCap) Slide = Phys.AbsCap; else if (Slide < -Phys.AbsCap) Slide = -Phys.AbsCap;
                Slide -= Sign(Slide);
                XVel = Slide;
                if (Slide != 0) Facing = Sign(Slide);
                if (OnSlope == 0 && Slide == 0) Sliding = false;
            }
            else if (Swimming) SwimHorizontal(dir, pad);
            else if (Form == Form.Frog && !InAir) FrogLand(dir, pad);
            else GroundHControl(dir, moveDir, pad);

            // ---- 7. jump / fly / flutter / gravity
            if (Swimming) SwimVertical(pad, aPressed);
            else JumpFlyFlutter(pad, aPressed && !lowClear, modern);

            // ---- 8. tail wag: flight/flutter air-speed limit (tail suits)
            if (TailSuit && InAir && (FlyTime != 0 || WagCount != 0) && Math.Abs(XVel) >= 0x18 && Statue == 0)
                XVel -= Sign(XVel);
            if (InAir && WagCount > 0) WagCount--;

            // actions: fire / tail / statue / carry
            Actions(pad);
        }

        void GroundHControl(int dir, int moveDir, PadState pad)
        {
            bool grounded = !InAir;
            bool runHeld = pad.H(Btn.B);
            Phys.Row row = Phys.RowFor(Form);
            if (grounded)
            {
                int ice = W.IceUnder(this);
                if (ice == 1) row = Phys.Ice1; else if (ice == 2) row = Phys.Ice2;
            }
            int cw = W.Wiggly;
            if (Ducking && grounded) dir = 0;
            if (dir == 0)
            {
                if (grounded && XVel != 0)
                {
                    int d = Phys.FricDec(row.FricFrac, cw);
                    if (XVel > 0) XVel = Math.Max(0, XVel - d); else XVel = Math.Min(0, XVel + d);
                }
            }
            else if (moveDir != 0 && dir != moveDir)
            {
                XVel += dir * Phys.Inc(runHeld ? row.SkidB : row.Skid, cw);
            }
            else
            {
                int cap = runHeld ? (Power == Phys.PowerFull ? Phys.PCap : Phys.RunCap) : Phys.WalkCap;
                if (Carrying != null && cap > Phys.RunCap) cap = Phys.RunCap;
                if (grounded && OnSlope != 0 && dir == -Sign(OnSlope)) cap = runHeld ? 0x16 : 0x0D;   // uphill caps
                int ax = Math.Abs(XVel);
                if (ax < cap) XVel += dir * Phys.Inc(row.Accel, cw);
                else if (ax > cap && grounded)
                {
                    int d = Phys.FricDec(row.FricFrac, cw);
                    int n = Math.Max(cap, ax - d);
                    XVel = Sign(XVel) * n;
                }
            }
        }

        void JumpFlyFlutter(PadState pad, bool aPressed, bool modern)
        {
            bool aHeld = pad.H(Btn.A);
            // ---- jump start
            if (aPressed && (!InAir || AllowAirJump > 0) && Statue == 0)
            {
                int idx = Math.Min(4, Math.Abs(XVel) >> 4);
                YVel = Phys.JumpBase - Phys.JumpSpeedBonus[idx];
                InAir = true;
                AllowAirJump = 0;
                JumpBuffer = 0;
                PJumpPose = Power == Phys.PowerFull;
                if (Power == Phys.PowerFull && FlyTime == 0) FlyTime = Phys.FlyTimeArm;
                if (PWing) FlyTime = Phys.FlyTimePWing;
                Sound.Sfx(SfxId.Jump);
                if (Star > 0 && Form != Form.Small && Form != Form.Frog && Carrying == null && Power != Phys.PowerFull) somersault = true;
                aPressed = false;   // the jump press itself doesn't count as a wag (keeps raccoon run-jumps identical)
            }
            else if (aPressed && InAir)
            {
                if (TailSuit && Statue == 0)
                {
                    WagCount = Phys.WagCount;
                    Sound.Sfx(SfxId.Flutter);
                }
                else if (modern) JumpBuffer = Phys.JumpBufferTicks;
            }

            if (!InAir) return;
            // ---- gravity
            if (Statue > 0) { YVel += 7; return; }
            if (YVel < Phys.LowGravBelow && aHeld && MicroGoombas == 0 && !NoLowGrav) YVel += Phys.GravLow;
            else YVel += Phys.GravHigh;

            // ---- flight / flutter (tail suits)
            if (TailSuit && WagCount > 0)
            {
                if (FlyTime > 0 && YVel >= Phys.FlyLift)
                {
                    if (FlyTime >= 0x0F) YVel = Phys.FlyLift;
                    else YVel = (FlyTime & 8) != 0 ? -0x10 : 0;
                }
                else if (FlyTime == 0 && YVel >= Phys.FlutterFallCap) YVel = Phys.FlutterFallCap;
            }
        }
        bool somersault;
        public bool Somersault { get { return somersault; } }

        void Actions(PadState pad)
        {
            if (pad.P(Btn.B))
            {
                if (TailSuit && pad.H(Btn.Down) && Form == Form.Tanooki && Statue == 0 && Carrying == null)
                {
                    // Tanooki statue
                    Statue = Phys.StatueTicks;
                    XVel = 0;
                    W.Puff(Px, Py + 16);
                    Sound.Sfx(SfxId.Statue);
                }
                else if (TailSuit && !pad.H(Btn.Down) && TailAttack == 0 && Carrying == null && Statue == 0)
                {
                    TailAttack = Phys.TailSpinTicks;
                    Sound.Sfx(SfxId.Tail);
                }
                else if ((Form == Form.Fire || Form == Form.Hammer) && Carrying == null && !Ducking)
                {
                    if (W.CountPlayerProjectiles() < 2)
                    {
                        if (Form == Form.Fire) W.Add(new Fireball(W, Px + (Facing > 0 ? 8 : 0), Py + 14, Facing));
                        else W.Add(new PlayerHammer(W, Px + (Facing > 0 ? 6 : 2), Py + 6, Facing, XVel));
                        ThrowPose = Phys.ThrowPose;
                        Sound.Sfx(Form == Form.Fire ? SfxId.Fireball : SfxId.Hammer);
                    }
                }
            }
            if (Statue > 0 && !pad.H(Btn.Down)) EndStatue();
            // releasing B kicks whatever we carry
            if (Carrying != null && !pad.H(Btn.B)) W.KickCarried(this);
        }

        void EndStatue()
        {
            Statue = 0;
            W.Puff(Px, Py + 16);
            Sound.Sfx(SfxId.Poof);
        }

        // ------------------------------------------------------------------------------ swimming (docs/01 §11.6)
        public bool InWater;
        int swimFrameCounter;
        int leapT;          // ticks of an Up+A leap out of the water (the surface clamp must not cancel it)
        void UpdateWaterState()
        {
            int wr = W.Area.WaterRow;
            bool was = Swimming;
            InWater = wr >= 0 && ((Py + (SmallBox ? 24 : 20)) >> 4) >= wr;
            Swimming = InWater && Statue == 0;
            if (!Swimming) leapT = 0;
            if (Swimming && !was)
            {
                FlyTime = 0; WagCount = 0; Ducking = false;
                if (YVel > 0x10) YVel = 0x10;
                if (wr > 0) { W.Splash(Px, wr * 16 - 8); Sound.Sfx(SfxId.Splash); }
            }
        }

        void SwimHorizontal(int dir, PadState pad)
        {
            var row = InAir ? Phys.Swim : Phys.WaterFloor;
            int cap = InAir ? Phys.SwimCap : Phys.WaterFloorCap;
            int cw = W.Wiggly;
            int moveDir = Sign(XVel);
            if (dir == 0)
            {
                int d = Phys.FricDec(row.FricFrac, cw);
                if (XVel > 0) XVel = Math.Max(0, XVel - d); else if (XVel < 0) XVel = Math.Min(0, XVel + d);
            }
            else if (moveDir != 0 && dir != moveDir) XVel += dir * Phys.Inc(row.Skid, cw);
            else if (Math.Abs(XVel) < cap) XVel += dir * Phys.Inc(row.Accel, cw);
            else if (Math.Abs(XVel) > cap)
            {
                int d = Phys.FricDec(row.FricFrac, cw);
                XVel = Sign(XVel) * Math.Max(cap, Math.Abs(XVel) - d);
            }
        }

        void SwimVertical(PadState pad, bool aPressed)
        {
            int wr = W.Area.WaterRow;
            bool headOut = wr > 0 && ((Py + (SmallBox ? 18 : 6)) >> 4) < wr;
            if (Form == Form.Frog)
            {
                // Frog: d-pad sets velocity directly, no sinking
                int sp = pad.H(Btn.A) ? 0x20 : 0x10;
                int tx = pad.H(Btn.Left) ? -sp : pad.H(Btn.Right) ? sp : 0;
                int ty = pad.H(Btn.Up) ? -sp : pad.H(Btn.Down) ? sp : 0;
                XVel = tx != 0 ? tx : XVel - Sign(XVel);
                YVel = ty != 0 ? ty : YVel - Sign(YVel);
                if (headOut && YVel < 0 && !(pad.H(Btn.Up) && pad.H(Btn.A))) YVel = 0;
                if (headOut && pad.H(Btn.Up) && aPressed) { YVel = -0x34; Swimming = false; }
                InAir = true;
                if (tx != 0 || ty != 0) SwimAnim++;
                return;
            }
            if (leapT > 0)
            {
                // still leaving the water after a leap: rise freely until the body clears the waterline
                leapT--;
                if (YVel < 0) { YVel += 1; return; }
                leapT = 0;
            }
            if (headOut && YVel < 0)
            {
                // surface bobbing: A/Up ignored unless both are held (leap out)
                if (pad.H(Btn.Up) && aPressed) { YVel = -0x34; InAir = true; leapT = 16; Sound.Sfx(SfxId.Jump); return; }
                if (YVel < -0x0C) YVel = -0x0C;
                if (W.Frame % 8 == 0) YVel++;
                return;
            }
            if (aPressed)
            {
                if (!InAir) YVel = -0x20; else YVel -= 0x20;
                if (YVel < -0x20) YVel = -0x20;
                InAir = true;
                SwimAnim = 12;
                Sound.Sfx(SfxId.Swim);
            }
            if (InAir)
            {
                if (YVel < 0) YVel += 1;
                else if ((swimFrameCounter++ & 3) < 2) YVel += 1;
                if (YVel > 0x20) YVel = 0x20;
                if (Py + 8 < W.CamY - 8) YVel += 0x10;
            }
            if (SwimAnim > 0) SwimAnim--;
        }

        void FrogLand(int dir, PadState pad)
        {
            // Frog on land: hop cycle every 32 ticks, speed resets each hop then accelerates by 2
            if (dir == 0) { XVel -= Sign(XVel) * Math.Min(Math.Abs(XVel), 1); return; }
            if ((W.Frame & 31) == 0) XVel = 0;
            int cap = pad.H(Btn.B) ? Phys.RunCap : Phys.WalkCap;
            if (Math.Abs(XVel) < cap) XVel += dir * 2;
        }

        // ------------------------------------------------------------------------------ vines (docs/01 §11.9)
        bool TryGrabVine(PadState pad)
        {
            if (Carrying != null || Statue > 0) return false;
            bool want = InAir ? (pad.H(Btn.Up) || pad.H(Btn.Down)) : pad.H(Btn.Up);
            if (!want) return false;
            if (W.TileAtPx(CenterX, Py + (SmallBox ? 24 : 16)) != T.Vine) return false;
            State = PState.Vine;
            Climbing = true;
            XVel = 0; YVel = 0; FlyTime = 0; WagCount = 0; Ducking = false;
            X = ((CenterX >> 4) * 16) << 4;
            return true;
        }

        void ClimbControl(PadState pad, bool aPressed)
        {
            int cx = CenterX, bodyY = Py + (SmallBox ? 24 : 16);
            if (aPressed)
            {
                State = PState.Normal; Climbing = false;
                YVel = Phys.JumpBase; InAir = true; Sound.Sfx(SfxId.Jump);
                return;
            }
            int vx = pad.H(Btn.Left) ? -0x10 : pad.H(Btn.Right) ? 0x10 : 0;
            int vy = 0;
            if (pad.H(Btn.Up) && VineAt(cx, bodyY - 8)) vy = -0x10;
            else if (pad.H(Btn.Down)) vy = 0x10;
            X += vx; Y += vy;
            if (vx != 0) Facing = vx > 0 ? 1 : -1;
            if (vx != 0 || vy != 0) ClimbAnim++;
            // leave the vine when it ends
            if (!VineAt(CenterX, Py + (SmallBox ? 24 : 16)))
            {
                State = PState.Normal; Climbing = false; InAir = true; YVel = 0;
            }
            if (vy > 0 && FloorBelow()) { State = PState.Normal; Climbing = false; }
            // climbing off the top of the area
            if (Py + 16 < 0 && W.OnClimbOffTop(this)) return;
        }

        /// <summary>A vine reaching row 0 continues above the area, so Big Mario can climb off the top too.</summary>
        bool VineAt(int px, int py)
        {
            if (py < 0 && py >= -48) return W.TileAtPx(px, 0) == T.Vine;
            return W.TileAtPx(px, py) == T.Vine;
        }

        bool FloorBelow()
        {
            return TileInfo.Floor(W.TileAtPx(Px + 4, Py + 32)) || TileInfo.Floor(W.TileAtPx(Px + 11, Py + 32));
        }

        // ------------------------------------------------------------------------------ P-meter (docs/01 §7)
        public void PowerUpdate()
        {
            bool running = !InAir && Pad.H(Btn.B) && !Sliding && Math.Abs(XVel) >= Phys.RunCap && !Swimming;
            if (PWing) { Power = Phys.PowerFull; if (FlyTime == 0) FlyTime = Phys.FlyTimePWing; return; }
            if (FlyTime == 0 && Power == Phys.PowerFull && running) PMeterCnt = Phys.PHoldReload;
            else if (PMeterCnt == 0)
            {
                if (running) { Power = ((Power << 1) | 1) & Phys.PowerFull; PMeterCnt = Phys.PChargeReload; }
                else if (FlyTime == 0) { Power >>= 1; PMeterCnt = Phys.PDecayReload; }
            }
        }

        // ------------------------------------------------------------------------------ tile collision (docs/01 §10)
        public void DetectSolids()
        {
            if (State == PState.Vine) return;
            if (W.Area.HasSlopes) { DetectSolidsSloped(); return; }
            int px = Px, py = Py;
            bool small = SmallBox;
            // --- sample probes & tile reactions
            bool rightSide = (px & 15) < 8;
            int sx = rightSide ? (small ? 13 : 14) : (small ? 2 : 1);
            int sy1 = 27, sy2 = small ? 20 : 14;
            bool side = SolidPx(px + sx, py + sy1) || SolidPx(px + sx, py + sy2);

            W.TouchTiles(this);

            // --- walls: push 1 px per tick toward the free side
            if (side && Statue == 0)
            {
                if (rightSide)
                {
                    if (((px + (small ? 13 : 14)) & 15) != 0) X -= 16;
                    if (XVel > 0) XVel = 0;
                }
                else
                {
                    if (((px + (small ? 3 : 2)) & 15) != 0) X += 16;
                    if (XVel < 0) XVel = 0;
                }
                px = Px;
            }

            // --- vertical
            if (YVel < 0 && InAir)
            {
                int hy = py + (small ? 16 : 6);
                T head = W.TileAtPx(px + 8, hy);
                if (head == T.HiddenBlock || TileInfo.Solid(head))
                {
                    YVel = 0;
                    W.HeadBump(this, (px + 8) >> 4, World.FloorDiv(hy, 16));
                }
            }
            else
            {
                int fy = py + 32;
                T f1 = W.TileAtPx(px + 4, fy), f2 = W.TileAtPx(px + 11, fy);
                bool floor = TileInfo.Floor(f1) || TileInfo.Floor(f2);
                if (floor)
                {
                    int depth = fy & 15;
                    if (depth < (InAir ? 10 : 6)) // in the air: corners entered from the side (6-9 px deep) still land (ROM W1-3 fix)
                    {
                        if (depth >= 3) Y = ((fy & ~15) - 32) << 4; else if (depth == 1) Y -= 16; else if (depth == 2) Y -= 32;
                        if (InAir) OnLand();
                        InAir = false;
                        YVel = 0;
                        KillTally = 0;
                        Conveyor = f1 == T.ConveyorL || f2 == T.ConveyorL ? -16 : f1 == T.ConveyorR || f2 == T.ConveyorR ? 16 : 0;
                        W.StandOn(this, TileInfo.Floor(f1) ? f1 : f2, (px + (TileInfo.Floor(f1) ? 4 : 11)) >> 4, fy >> 4);
                    }
                    else if (!InAir) { InAir = true; }
                }
                else if (!InAir && !W.PlatformSupport(this))
                {
                    // walked off a ledge
                    InAir = true;
                    YVel = 0;
                    Conveyor = 0; OnSlope = 0;
                    if (W.Modern && AllowAirJump == 0) AllowAirJump = Phys.CoyoteTicks + 1;
                }
            }
        }

        /// <summary>
        /// SMB3's sloped probe set (docs/01 §10.2): one center foot probe that follows slope surfaces,
        /// sides at y+24/y+12 (big) or y+23 (small), head at (8,5)/(8,18).
        /// </summary>
        void DetectSolidsSloped()
        {
            int px = Px, py = Py;
            bool small = SmallBox;
            bool rightSide = (px & 15) < 8;
            int sx = rightSide ? 13 : 3;
            int sy1 = small ? 23 : 24, sy2 = small ? 23 : 12;
            bool side = SolidPx(px + sx, py + sy1) || SolidPx(px + sx, py + sy2);
            W.TouchTiles(this);
            if (side && Statue == 0)
            {
                if (rightSide) { if (((px + 13) & 15) != 0) X -= 16; if (XVel > 0) { XVel = 0; Slide = 0; } }
                else { if (((px + 4) & 15) != 0) X += 16; if (XVel < 0) { XVel = 0; Slide = 0; } }
                px = Px;
            }
            if (YVel < 0 && InAir)
            {
                int hy = py + (small ? 18 : 5);
                T head = W.TileAtPx(px + 8, hy);
                if (head == T.HiddenBlock || TileInfo.Solid(head)) { YVel = 0; W.HeadBump(this, (px + 8) >> 4, World.FloorDiv(hy, 16)); }
                OnSlope = 0;
                return;
            }
            int cx = px + 8, fy = py + 32;
            int surf; T ft;
            if (FindSurface(cx, fy, !InAir, out surf, out ft))
            {
                int depth = fy - surf;
                bool slope = TileInfo.Slope(ft);
                int above = !InAir ? 8 : 0, below = slope ? 12 : (InAir ? 10 : 6);
                if (depth >= -above && depth < below)
                {
                    if (slope || OnSlope != 0 || depth <= 0 || depth >= 3) Y = (surf - 32) << 4;
                    else if (depth == 1) Y -= 16; else if (depth >= 2) Y -= 32;
                    if (InAir) OnLand();
                    InAir = false; YVel = 0; KillTally = 0;
                    OnSlope = slope ? TileInfo.SlopePush(ft) : 0;
                    Conveyor = ft == T.ConveyorL ? -16 : ft == T.ConveyorR ? 16 : 0;
                    if (!slope) W.StandOn(this, ft, cx >> 4, World.FloorDiv(surf, 16));
                    return;
                }
            }
            if (!InAir && !W.PlatformSupport(this))
            {
                InAir = true; YVel = 0; OnSlope = 0; Conveyor = 0;
                if (W.Modern && AllowAirJump == 0) AllowAirJump = Phys.CoyoteTicks + 1;
            }
            else if (InAir) OnSlope = 0;
        }

        bool FindSurface(int cx, int fy, bool grounded, out int surf, out T ft)
        {
            surf = int.MaxValue; ft = T.Empty;
            int tx = World.FloorDiv(cx, 16);
            for (int k = 0; k < (grounded ? 3 : 2); k++)
            {
                int yy = k == 0 ? fy - 16 : k == 1 ? fy : fy + 8;
                int ty = World.FloorDiv(yy, 16);
                T t = W.TileAt(tx, ty);
                int s;
                if (TileInfo.Slope(t)) s = TileInfo.SlopeSurface(t, tx, ty, cx);
                else if (TileInfo.Floor(t) && k != 0) s = ty * 16;
                else continue;
                if (s < fy - 16) continue;
                if (s < surf) { surf = s; ft = t; }
            }
            return surf != int.MaxValue;
        }

        void OnLand()
        {
            somersault = false;
            PJumpPose = false;
            NoLowGrav = false;
            WagCount = 0;
            // Non-flying suits end a P-jump on landing; tail suits keep their flight timer (they can take off again).
            if (!TailSuit && !PWing) FlyTime = 0;
            if (!Pad.H(Btn.Down)) Ducking = false;
            if (Statue > 0) W.StatueLanded(this);
        }

        // ------------------------------------------------------------------------------ timers (docs/01 §3 step 5)
        public void Timers()
        {
            if (PMeterCnt > 0) PMeterCnt--;
            if (AllowAirJump > 0) AllowAirJump--;
            if (HurtInv > 0) HurtInv--;
            if (Star > 0) { Star--; if (Star == 64) W.StarEnding(); if (Star == 0) W.StarEnded(); }
            if (TailAttack > 0) { TailAttack--; if (TailAttack == 0x0B || TailAttack == 0x03) Facing = -Facing; if (TailAttack == 0x0C || TailAttack == 0x09) W.TailHit(this, TailAttack); }
            if (Statue > 0) { Statue--; if (Statue == 0) EndStatue(); }
            if (ThrowPose > 0) ThrowPose--;
            if (KickPose > 0) KickPose--;
            if (Invisible > 0) Invisible--;
            if (FlyTime > 0 && FlyTime != Phys.FlyTimePWing && (W.Frame & 1) == 1)
            {
                FlyTime--;
                if (FlyTime == 0) Power = 0;
            }
            // white-block secret: duck on a white block for ~6 s
            if (BehindTimer > 0 && (W.Frame & 1) == 0) { BehindTimer--; if (BehindTimer == 0 && W.TileAtPx(CenterX, Py + 24) == T.Empty) BehindScenery = false; else if (BehindTimer == 0) BehindTimer = 1; }
        }

        // ------------------------------------------------------------------------------ damage & power
        public bool Invulnerable { get { return HurtInv > 0 || Star > 0 || Statue > 0 || Transform > 0 || State != PState.Normal && State != PState.Vine; } }

        public void Hurt()
        {
            if (Invulnerable || W.GodMode) return;
            if (W.Shoe != null) { W.LoseShoe(); HurtInv = Phys.HurtInvTicks; return; }
            if (PWing) PWing = false;
            if (Form == Form.Small) { Die(false); return; }
            if (Form == Form.Big) { StartTransform(Form.Small, 2, Phys.GrowTicks); Sound.Sfx(SfxId.Pipe); }
            else { StartTransform(Form.Big, 3, Phys.PoofTicks); W.Puff(Px, Py + 8); Sound.Sfx(SfxId.Pipe); }
            HurtInv = Phys.HurtInvTicks;
            if (Carrying != null) W.DropCarried(this);
            Host.Input.Rumble(W.S.PlayerIndex, 0.6f, 12);
        }

        public void StartTransform(Form to, int kind, int ticks)
        {
            TransformFrom = Form; TransformTo = to; TransformKind = kind; Transform = ticks;
            Form = to;
            if (!Big) Ducking = false;
            if (!TailSuit) { WagCount = 0; }
            W.HaltGame = ticks;
        }

        public void PowerUp(Form to)
        {
            W.AddScore(1000, Px, Py + (Big ? 0 : 16));
            if (to == Form.Big)
            {
                if (Form == Form.Small) { StartTransform(Form.Big, 1, Phys.GrowTicks); Sound.Sfx(SfxId.PowerUp); }
                else Sound.Sfx(SfxId.PowerUp);
                return;
            }
            if (Form == to) { Sound.Sfx(SfxId.PowerUp); return; }
            // Flower on a Big or Small player: palette flash; suits: smoke poof (SMB3)
            if (to == Form.Fire && (Form == Form.Big || Form == Form.Small)) { StartTransform(Form.Fire, 4, Phys.StarOffTicks); Sound.Sfx(SfxId.PowerUp); return; }
            StartTransform(to, 3, Phys.PoofTicks);
            W.Puff(Px, Py + 8);
            Sound.Sfx(to == Form.Raccoon ? SfxId.Poof : SfxId.PowerUp);
        }

        public void GetStar()
        {
            Star = Phys.StarTicks;
            W.AddScore(1000, Px, Py + (Big ? 0 : 16));
            W.StarStarted();
        }

        public void Die(bool pit)
        {
            if (State == PState.Dying) return;
            State = PState.Dying;
            DeathTimer = 0;
            XVel = 0; YVel = pit ? 0 : -0x40;
            Form = Form.Small;
            Ducking = false; Star = 0; FlyTime = 0; Power = 0;
            if (Carrying != null) W.DropCarried(this);
            W.OnPlayerDying(pit);
            Host.Input.Rumble(W.S.PlayerIndex, 0.9f, 20);
        }

        /// <summary>Death animation: 48 frozen ticks, then a hop and a fall with +2 gravity (docs/01 §10.7).</summary>
        public void DyingTick()
        {
            DeathTimer++;
            if (DeathTimer < 0x30) return;
            Y += YVel;
            YVel += 2;
            if (YVel > 0x40) YVel = 0x40;
        }

        /// <summary>Stomp bounce: -$40; holding A extends it through the low-gravity rule.</summary>
        public void Bounce(int yvel)
        {
            YVel = yvel;
            InAir = true;
            JumpBuffer = 0;
            AllowAirJump = 0;
        }
    }
}
