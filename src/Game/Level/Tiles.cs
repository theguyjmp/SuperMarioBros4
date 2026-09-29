namespace SMB4.Game
{
    /// <summary>Runtime tile types. Collision class and behaviour derive from the type.</summary>
    public enum T : byte
    {
        Empty = 0,
        Ground, Hard, Brick, QBlock, Used, Wood, Note, Ice, Muncher, Spike, Barrier,
        Semi, Cloud, BigBlock,             // semisolid (top only)
        PipeTL, PipeTR, PipeL, PipeR, PipeBL, PipeBR,
        HPipeMouthT, HPipeMouthB, HPipeT, HPipeB,
        CannonTop, CannonMid,
        Coin, HiddenBlock, Vine, DoorTop, DoorBot, Lava, LavaTop, GoalFloor,
        CannonL, CannonR,
        SlopeUp, SlopeDown,                 // 45-degree floor slopes (rising / falling to the right)
        ConveyorL, ConveyorR,               // solid belts that carry whatever stands on them
        BigBlockBody,                       // a big block below its top row: drawn, but passable
    }

    /// <summary>What a bumpable block releases.</summary>
    public enum Content : byte { None = 0, Coin, Flower, Leaf, Star, OneUp, Vine, PSwitch, MultiCoin }

    public static class TileInfo
    {
        public static bool Solid(T t)
        {
            switch (t)
            {
                case T.Ground: case T.Hard: case T.Brick: case T.QBlock: case T.Used: case T.Wood: case T.Note: case T.Ice:
                case T.Muncher: case T.Spike: case T.Barrier:
                case T.PipeTL: case T.PipeTR: case T.PipeL: case T.PipeR: case T.PipeBL: case T.PipeBR:
                case T.HPipeMouthT: case T.HPipeMouthB: case T.HPipeT: case T.HPipeB:
                case T.CannonTop: case T.CannonMid: case T.CannonL: case T.CannonR:
                case T.ConveyorL: case T.ConveyorR:
                    return true;
            }
            return false;
        }

        public static bool Slope(T t) { return t == T.SlopeUp || t == T.SlopeDown; }

        /// <summary>Absolute pixel Y of a slope's surface at absolute pixel X (inside tile tx,ty).</summary>
        public static int SlopeSurface(T t, int tx, int ty, int px)
        {
            int lx = px - tx * 16;
            if (lx < 0) lx = 0; else if (lx > 15) lx = 15;
            return ty * 16 + (t == T.SlopeUp ? 15 - lx : lx);
        }

        /// <summary>Position push for walking on a slope (points downhill), docs/01 §5.6.</summary>
        public static int SlopePush(T t) { return t == T.SlopeUp ? -3 : t == T.SlopeDown ? 3 : 0; }

        /// <summary>Stand-on-able from above (solid or semisolid).</summary>
        public static bool Floor(T t) { return Solid(t) || t == T.Semi || t == T.Cloud || t == T.BigBlock || t == T.GoalFloor; }

        public static bool Bumpable(T t) { return t == T.Brick || t == T.QBlock || t == T.Note || t == T.Wood || t == T.HiddenBlock; }

        public static bool Hurts(T t) { return t == T.Muncher || t == T.Spike; }

        public static bool Lava(T t) { return t == T.Lava || t == T.LavaTop; }

        public static bool Pipe(T t) { return t >= T.PipeTL && t <= T.HPipeB; }
    }
}
