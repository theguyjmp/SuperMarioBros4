using System;
using SMB4.Engine;

namespace SMB4.Game
{
    public sealed partial class Player
    {
        static readonly int[] WalkTicks = { 7, 6, 5, 4, 3, 2, 1, 1, 1 };
        static readonly string[] BigCycle = { "mb.walk1", "mb.walk2", "mb.stand", "mb.walk2" };
        static readonly string[] SmallCycle = { "ms.walk", "ms.walk", "ms.stand", "ms.stand" };
        static readonly string[] BigRun = { "mb.run1", "mb.run2", "mb.run3", "mb.run2" };
        static readonly string[] SmallRun = { "ms.run1", "ms.run2", "ms.run1", "ms.run2" };

        /// <summary>Walk-cycle timing from docs/01 §13: ticks per frame by |XVel| >> 3; idle shows frame 2.</summary>
        public void Animate()
        {
            int ax = Math.Abs(XVel);
            if (InAir || State != PState.Normal) return;
            if (ax == 0 && !Skidding) { AnimFrame = 2; AnimTick = 0; return; }
            if (AnimTick > 0) { AnimTick--; return; }
            AnimFrame = (AnimFrame + 1) & 3;
            int t = WalkTicks[Math.Min(8, ax >> 3)];
            if (W.IceUnder(this) != 0) t += 1;
            AnimTick = t;
        }

        string Frame(out int yOff, out bool small)
        {
            Form f = Form;
            small = f == Form.Small;
            yOff = 0;
            if (State == PState.Dying) { small = true; return "ms.death"; }
            if (Statue > 0) { small = false; return "statue"; }
            // Frog Suit: frog frames everywhere except on vines (walk-off, pipes and doors included)
            if (f == Form.Frog && Art.Has("frog.stand") && State != PState.Vine)
            {
                if (State == PState.Door || State == PState.Pipe && (PipeDir == 2 || PipeDir == 8))
                    return Art.Has("frog.front") ? "frog.front" : "frog.stand";
                if (State == PState.Pipe) return (AnimFrame & 2) == 0 ? "frog.hop1" : "frog.hop2";
                if (Swimming && InAir) return "frog.swim" + (1 + (W.Frame / 6) % 3);
                if (InAir) return "frog.hop2";
                if (Math.Abs(XVel) > 0) return (W.Frame & 31) < 16 ? "frog.hop1" : "frog.hop2";
                return "frog.stand";
            }
            string p = small ? "ms." : "mb.";
            if (State == PState.Pipe && (PipeDir == 2 || PipeDir == 8)) return p + "front";
            if (State == PState.Door) return p + "front";
            if (State == PState.Vine || Climbing) return p + ((ClimbAnim >> 3) % 2 == 0 ? "climb1" : "climb2");
            if (Swimming && InAir)
            {
                int s = SwimAnim > 8 ? 1 : SwimAnim > 0 ? 2 : 3;
                return p + "swim" + s;
            }
            if (TailAttack > 0 && (f == Form.Raccoon || f == Form.Tanooki))   // not after the suit was knocked off mid-spin
            {
                int ph = TailAttack;
                if (ph > 13) return "mb.stand";
                if (ph > 9) return "mb.spinfront";
                if (ph > 5) return "mb.stand";
                return "mb.spinback";
            }
            if (Sliding) return small ? (Art.Has("ms.slide") ? "ms.slide" : "ms.jump") : "mb.slide";
            if (Ducking && !small) return "mb.duck";
            if (KickPose > 0) return p + "kick";
            if (ThrowPose > 0 && !small) return "mb.throw";
            if (InAir)
            {
                if (Carrying != null) return p + "hold1";
                if (PJumpPose || FlyTime > 0) return p + "pjump";
                return p + "jump";   // SMB3 holds one jump pose for the whole airtime (docs/01 §13)
            }
            if (Skidding && Carrying == null) return p + "skid";
            if (Carrying != null) return p + (AnimFrame < 2 ? "hold1" : "hold2");
            if (Math.Abs(XVel) >= 0x37 && FlyTime == 0) { RunAnim++; return (small ? SmallRun : BigRun)[AnimFrame & 3]; }
            return (small ? SmallCycle : BigCycle)[AnimFrame & 3];
        }

        /// <summary>
        /// Where the suit overlays sit on a big body frame (measured on the right-facing art).
        /// view: 0 = side view, 1 = front view, 2 = back view.
        /// (hx, hy): offset of the head overlays from their art origin (the overlays were drawn for a cap top on row 1);
        /// ears, Tanooki hood and Hammer helmet follow it. (rx, ry): rump point where the tail base attaches.
        /// </summary>
        static void Anchor(string name, out int view, out int hx, out int hy, out int rx, out int ry)
        {
            view = 0; hx = 0; hy = -4; rx = 4; ry = 21;             // cap top on box row -3 (head 12 rows above a 13-row torso)
            switch (name)
            {
                case "mb.walk1": hy = -3; ry = 22; break;                 // stride frame: body bobs 1 px down
                case "mb.run1": case "mb.run2": case "mb.run3": hx = 2; break;   // P-speed sprint: head leaned forward
                case "mb.skid": hx = -1; rx = 3; break;                  // leaning back against the slide
                case "mb.duck": hy = 9; rx = 3; ry = 27; break;
                case "mb.slide": hy = 11; rx = 3; ry = 28; break;
                case "mb.front": case "mb.spinfront": view = 1; break;
                case "mb.climb1": case "mb.climb2": case "mb.spinback": view = 2; break;
            }
        }

        /// <summary>
        /// Draws a body frame anchored to the 16-wide player box: frames wider than 16 px are centered on it and
        /// frames taller than the nominal height (boxH: 16 small, 32 big) extend upward, so poses with an outstretched arm
        /// or a trailing leg aren't cropped by the box (the hitbox is unchanged).
        /// </summary>
        public static void Body(Ppu ppu, Img img, int x, int y, int boxH, ushort[] pal, bool flip, bool flipV, bool behind)
        {
            ppu.Spr(img, x - (img.W - 16) / 2, flipV ? y : y + boxH - img.H, pal, flip, flipV, behind);
        }

        /// <summary>Point on each tail image (right-facing) that joins the rump.</summary>
        static void TailAttach(string tail, out int ax, out int ay)
        {
            ax = 14;
            ay = tail == "tail.up" ? 10 : tail == "tail.mid" ? 7 : 5;
        }

        /// <summary>The plain sprite palette of a form (no star or power-up flashing).</summary>
        public static ushort[] FormPalette(Form form, bool luigi)
        {
            switch (form)
            {
                case Form.Fire: return Art.Pal("fire");
                case Form.Tanooki: return Art.Pal("tanooki");
                case Form.Frog: return Art.Pal(Art.HasPal("frog") ? "frog" : "mario");
                case Form.Hammer: return Art.Pal(Art.HasPal("hammersuit") ? "hammersuit" : "mario");
            }
            return Art.Pal(luigi ? "luigi" : "mario");
        }

        /// <summary>A full 15-slot player palette from player.art (star / power-up flashes); plain Mario if missing.</summary>
        static ushort[] NamedPal(string name) { return Art.Pal(Art.HasPal(name) ? name : "mario"); }

        ushort[] BodyPalette()
        {
            // SMB3 shows plain small Mario/Luigi when the player goes down, whatever the form was
            if (State == PState.Dying) return Art.Pal(Luigi ? "luigi" : "mario");
            if (Star > 0)
            {
                int rate = Star < 96 ? 4 : 2;
                if (W.ReduceFlashing) rate *= 3;
                int k = (W.Frame / rate) & 3;
                switch (k)
                {
                    case 0: return Art.Pal(Luigi ? "luigi" : "mario");
                    case 1: return Art.Pal("fire");
                    case 2: return NamedPal("pstar.1");
                    default: return NamedPal("pstar.2");
                }
            }
            if (Transform > 0 && TransformKind == 4 && ((Transform >> 1) & 1) == 0) return NamedPal("pflash");
            return FormPalette(Form, Luigi);
        }

        /// <summary>
        /// Draws a 16-wide overlay placed at (dx, dy) on the upright, right-facing 16x32 body at (sx, sy),
        /// mirrored and flipped together with the body.
        /// </summary>
        static void Overlay(Ppu ppu, string img, int sx, int sy, int dx, int dy, ushort[] pal, bool flip, bool flipV, bool behind)
        {
            if (!Art.Has(img)) return;
            var im = Art.Get(img);
            ppu.Spr(im, flip ? sx - dx : sx + dx, flipV ? sy + 32 - dy - im.H : sy + dy, pal, flip, flipV, behind);
        }

        /// <summary>
        /// Draws a big (mb.*) body frame at (sx, sy) together with its form's suit overlays: the Raccoon/Tanooki
        /// tail and ears, the Tanooki hood, the Hammer Suit shell and helmet.
        /// tail: side-view tail image to show (null = none; on back views any non-null value shows tail.back);
        /// spinSide: +1/-1 = tail.side sticking out to the right/left (front/back spin frames), 0 = none.
        /// </summary>
        static void DrawSuited(Ppu ppu, Form form, string name, int sx, int sy, ushort[] p, bool flip, bool flipV, bool behind, string tail, int spinSide)
        {
            int view, hx, hy, rx, ry;
            Anchor(name, out view, out hx, out hy, out rx, out ry);
            bool tailSuit = form == Form.Raccoon || form == Form.Tanooki;
            ushort[] tp = form == Form.Tanooki ? p : Art.Pal("tail");

            // raccoon/tanooki tail behind the body
            if (tailSuit)
            {
                if (spinSide != 0) Overlay(ppu, "tail.side", sx, sy, 12, 20, tp, spinSide < 0, flipV, behind);
                else if (view == 0 && tail != null)
                {
                    int ax, ay;
                    TailAttach(tail, out ax, out ay);
                    Overlay(ppu, tail, sx, sy, rx - ax, ry - ay, tp, flip, flipV, behind);
                }
            }
            // Hammer Suit shell seen from the side: a dome on the back, behind the body so arms swung back stay in front
            if (form == Form.Hammer && view == 0 && name != "mb.duck") Overlay(ppu, "shell", sx, sy, rx - 8, ry - 9, p, flip, flipV, behind);

            Body(ppu, Art.Get(name), sx, sy, 32, p, flip, flipV, behind);

            if (tailSuit)
            {
                // seen from behind (climbing) the tail hangs down over the legs
                if (view == 2 && tail != null && spinSide == 0) Overlay(ppu, "tail.back", sx, sy, 0, 16, tp, flip, flipV, behind);
                // Tanooki: the suit's hood wraps the back of the head
                if (form == Form.Tanooki && view == 0) Overlay(ppu, "hood", sx, sy, hx, hy, p, flip, flipV, behind);
                // ears on top of the cap / hood
                if (view == 0) Overlay(ppu, "ears", sx, sy, hx, hy - 3, tp, flip, flipV, behind);
                else Overlay(ppu, Art.Has("ears.front") ? "ears.front" : "ears", sx, sy, 0, hy - 3, tp, flip, flipV, behind);
            }

            // Hammer Suit: shell over the crouched body / over the back (climbing view), and the Hammer Bro helmet
            if (form == Form.Hammer)
            {
                if (name == "mb.duck") Overlay(ppu, "shell.duck", sx, sy, 0, 16, p, flip, flipV, behind);
                else if (view == 2) Overlay(ppu, "shell.back", sx, sy, 0, 12, p, flip, flipV, behind);
                if (view == 0) Overlay(ppu, "helmet", sx, sy, hx, hy, p, flip, flipV, behind);
                else Overlay(ppu, Art.Has("helmet.front") ? "helmet.front" : "helmet", sx, sy, 0, hy, p, flip, flipV, behind);
            }
        }

        /// <summary>
        /// Draws a standing player of the given form with all its suit overlays (tail, ears, hood, helmet, shell),
        /// for screens outside the level (lives card, Toad House, ending). (x, y) is the top-left of the 16x32
        /// player box, like Player.Px/Py: small Mario stands in its lower half, big forms fill it.
        /// Facing right, the Raccoon/Tanooki tail reaches about 11 px left of x (mirrored when facing left).
        /// </summary>
        public static void DrawIdle(Ppu ppu, Form form, bool luigi, int x, int y, bool faceLeft)
        {
            ushort[] p = FormPalette(form, luigi);
            if (form == Form.Small) { Body(ppu, Art.Get("ms.stand"), x, y + 16, 16, p, faceLeft, false, false); return; }
            if (form == Form.Frog && Art.Has("frog.stand")) { ppu.Spr(Art.Get("frog.stand"), x, y, p, faceLeft); return; }
            DrawSuited(ppu, form, "mb.stand", x, y, p, faceLeft, false, false, "tail.down", 0);
        }

        public void Draw(Ppu ppu, int camX, int camY)
        {
            if (Invisible > 0) return;
            if (HurtInv > 0 && State == PState.Normal && ((HurtInv >> 1) & 1) == 1 && Transform == 0) return;
            int sx = Px - camX, sy = Py - camY;
            bool behind = State == PState.Pipe || BehindScenery;

            // transformation effects: grow/shrink flicker between the two sizes; poof shows smoke only
            if (Transform > 0 && (TransformKind == 1 || TransformKind == 2))
            {
                bool showBig = ((Transform >> 2) & 1) == (TransformKind == 1 ? 1 : 0);
                string img = showBig ? (InAir ? "mb.jump" : "mb.stand") : (InAir ? "ms.jump" : "ms.stand");
                var pal = BodyPalette();
                if (showBig) Body(ppu, Art.Get(img), sx, sy, 32, pal, Facing < 0, false, behind);
                else Body(ppu, Art.Get(img), sx, sy + 16, 16, pal, Facing < 0, false, behind);
                return;
            }
            if (Transform > 0 && TransformKind == 3)
            {
                if (((Transform >> 2) & 1) == 0) return; // flicker while the puff plays
            }

            int yOff; bool small;
            string name = Frame(out yOff, out small);
            var body = Art.Get(name);
            var p = BodyPalette();
            if (name == "statue" && Star == 0 && Art.HasPal("statue")) p = Art.Pal("statue");
            bool flip = Facing < 0;
            bool flipV = false;
            if (somersault && InAir && State == PState.Normal)
            {
                int k = (W.Frame / 3) & 3;
                flip = k == 1 || k == 2 ? !flip : flip;
                flipV = k == 2 || k == 3;
            }
            int by = small ? sy + 16 : sy;
            if (State == PState.Dying) by = sy + 16;

            if (!name.StartsWith("mb.")) { Body(ppu, body, sx, by, name == "statue" || name.StartsWith("frog.") ? 32 : 16, p, flip, flipV, behind); return; }

            // big body + suit overlays; pick the tail pose from the player's state
            string tail = "tail.down";
            if (TailAttack > 0 || Ducking || Sliding) tail = "tail.mid";
            else if (InAir && WagCount > 0) tail = ((W.Frame >> 2) & 1) == 0 ? "tail.up" : "tail.mid";
            else if (InAir && YVel < 0) tail = "tail.mid";
            else if (!InAir && Math.Abs(XVel) >= 0x28) tail = ((W.Frame >> 2) & 1) == 0 ? "tail.mid" : "tail.down";
            int spinSide = 0;
            if (TailAttack > 0 && (name == "mb.spinfront" || name == "mb.spinback"))
            {
                // front/back spin frames: the tail sticks straight out to one side
                tail = null;
                bool out1 = TailAttack <= 13 && TailAttack > 9, out2 = TailAttack <= 5;
                if (out1 || out2) spinSide = (out1 ? Facing > 0 : Facing < 0) ? 1 : -1;
            }
            DrawSuited(ppu, Form, name, sx, sy, p, flip, flipV, behind, tail, spinSide);

            // carried object is drawn by the object itself (in front)
        }
    }
}
