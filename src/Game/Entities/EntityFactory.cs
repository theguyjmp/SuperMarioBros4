using System;

namespace SMB4.Game
{
    /// <summary>Creates objects from level spawn codes (legend in data/levels/README.md).</summary>
    public static class EntityFactory
    {
        public static Entity Create(World w, SpawnDef s)
        {
            int px = s.X * 16, py = s.Y * 16;
            switch (s.Code)
            {
                case 'g': return new Goomba(w, px, py, false);
                case 'p': return new Goomba(w, px, py, true);
                case 'k': return new Koopa(w, px, py, false, 0);
                case 'r': return new Koopa(w, px, py, true, 0);
                case 'j': return new Koopa(w, px, py, false, 1);
                case 'f': return new Koopa(w, px, py, true, 2);
                case 'n': return new Koopa(w, px, py, true, 3);
                case 'z': return new Buzzy(w, px, py);
                case 's': return new Spiny(w, px, py, false);
                case 'e': return new Piranha(w, px, py + 16, false);
                case 'v': return new Piranha(w, px, py + 16, true);
                case 'c': return new Cheep(w, px, py, false);
                case 'l': return new Cheep(w, px, py, true);
                case 'q': return new Blooper(w, px, py);
                case 'u': return new Boo(w, px, py);
                case 't': return new Thwomp(w, px, py);
                case 'd': return new DryBones(w, px, py);
                case 'x': return new Podoboo(w, px, py);
                case 'y': return new HammerBro(w, px, py);
                case 'w': return new RockyWrench(w, px, py);
                case 'a': return new BobOmb(w, px, py);
                case 'i': return new Lakitu(w, px, py);
                case 'b': return new BillBlaster(w, s.X, s.Y);
                case '<': return new AirshipCannon(w, s.X, s.Y, -1);
                case '>': return new AirshipCannon(w, s.X, s.Y, 1);
                case 'Z': return new BoomBoom(w, px, py);
                case 'K': return new Koopaling(w, px, py, WorldNumber(w));
                case 'Y': return new Bowser(w, px, py);
                case 'P': return new PSwitch(w, px, py);
                case 'R': return new RotoDisc(w, px, py, (s.X & 1) == 0 ? 1 : -1);
                case '-': return new DonutLift(w, px, py);
                case '_': return new MovingLift(w, px, py, false);
                case ':': return new MovingLift(w, px, py, true);
                case 'm': return new Mushroom(w, px, py + 16, false);
            }
            return null;
        }

        public static int WorldNumber(World w)
        {
            string id = w.Def.Id;
            int n;
            if (id.Length > 0 && int.TryParse(id.Substring(0, 1), out n)) return n;
            return 1;
        }
    }
}
