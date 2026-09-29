namespace SMB4.Game
{
    public enum Form { Small = 0, Big, Fire, Raccoon, Tanooki, Frog, Hammer }

    /// <summary>SMB3 physics constants (docs/01). Velocities are 1/16 px per tick.</summary>
    public static class Phys
    {
        public const int WalkCap = 0x18, RunCap = 0x28, PCap = 0x38, AbsCap = 0x40;
        public const int WaterFloorCap = 0x08, SwimCap = 0x18;
        public const int JumpBase = -0x38;
        public static readonly int[] JumpSpeedBonus = { 0, 2, 4, 8, 0 };
        public const int GravLow = 1, GravHigh = 5, LowGravBelow = -0x20, FallCap = 0x40;
        public const int StompBounce = -0x40, NoteLaunch = -0x38, NoteSuper = -0x70;
        public const int PowerFull = 0x7F, PChargeReload = 8, PDecayReload = 0x18, PHoldReload = 0x10;
        public const int FlyTimeArm = 0x80, FlyTimePWing = 0xFF, WagCount = 0x10;
        public const int FlyLift = -0x18, FlutterFallCap = 0x10;
        public const int HurtInvTicks = 0x71, StarTicks = 448, GrowTicks = 0x2F, PoofTicks = 0x17, StarOffTicks = 0x1F;
        public const int TailSpinTicks = 0x12, StatueTicks = 0xC0, KickPose = 0x0C, ThrowPose = 0x0B;
        public const int CoyoteTicks = 5, JumpBufferTicks = 4;

        /// <summary>An 8.8 per-tick rate applied as whole + carry(frac + wiggle dither).</summary>
        public struct Rate { public int Whole, Frac; public Rate(int w, int f) { Whole = w; Frac = f; } }

        /// <summary>friction (magnitude as 1 - carry), accel, skid, skid with B.</summary>
        public struct Row
        {
            public int FricFrac; public Rate Accel, Skid, SkidB;
            public Row(int fricFrac, Rate accel, Rate skid, Rate skidB) { FricFrac = fricFrac; Accel = accel; Skid = skid; SkidB = skidB; }
        }

        public static readonly Row SmallRow = new Row(0x60, new Rate(0, 0xE0), new Rate(2, 0), new Rate(2, 0));
        public static readonly Row BigRow = new Row(0x20, new Rate(0, 0xE0), new Rate(2, 0), new Rate(2, 0));
        public static readonly Row FrogRow = new Row(0x00, new Rate(2, 0), new Rate(2, 0), new Rate(2, 0));
        public static readonly Row Ice1 = new Row(0xA0, new Rate(0, 0xE0), new Rate(0, 0xC0), new Rate(1, 0x20));
        public static readonly Row Ice2 = new Row(0xD0, new Rate(0, 0xE0), new Rate(0, 0x60), new Rate(0, 0xC0));
        public static readonly Row WaterFloor = new Row(0x30, new Rate(1, 0), new Rate(1, 0), new Rate(1, 0));
        public static readonly Row Swim = new Row(0xE0, new Rate(0, 0x30), new Rate(0, 0x80), new Rate(0, 0x80));

        public static Row RowFor(Form f)
        {
            switch (f)
            {
                case Form.Small: case Form.Tanooki: case Form.Hammer: return SmallRow;
                case Form.Frog: return FrogRow;
                default: return BigRow;
            }
        }

        public static int Carry(int frac, int wiggly) { return frac + wiggly > 255 ? 1 : 0; }
        public static int Inc(Rate r, int wiggly) { return r.Whole + Carry(r.Frac, wiggly); }
        public static int FricDec(int fricFrac, int wiggly) { return 1 - Carry(fricFrac, wiggly); }
        public static int NextWiggly(int cw) { return ((cw & 0xF0) - 0x90) & 0xFF; }
    }
}
