using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using N = SMB4.Platform.Native;

namespace SMB4.Platform
{
    /// <summary>NES-pad style buttons. Bit index == Act index.</summary>
    public static class Btn
    {
        public const int Up = 1, Down = 2, Left = 4, Right = 8, A = 16, B = 32, Start = 64, Select = 128;
        public const int Dirs = Up | Down | Left | Right;
    }

    public enum Act { Up = 0, Down, Left, Right, Jump, Run, Start, Select }

    public struct PadState
    {
        public int Held, Pressed, Released;
        public bool H(int b) { return (Held & b) != 0; }
        public bool P(int b) { return (Pressed & b) != 0; }
        public bool R(int b) { return (Released & b) != 0; }
        public static PadState operator |(PadState a, PadState b)
        {
            var r = new PadState(); r.Held = a.Held | b.Held; r.Pressed = a.Pressed | b.Pressed; r.Released = a.Released | b.Released; return r;
        }
    }

    /// <summary>User-configurable bindings. Directions on controllers are always D-pad + (optional) left stick.</summary>
    public sealed class Bindings
    {
        public const int ActCount = 8;
        public int[][] Keys = new int[ActCount][];      // Windows virtual-key codes (L/R shift/ctrl/alt distinguished)
        public int[][] XBtn = new int[ActCount][];      // XInput button masks; 0x10000 = LT, 0x20000 = RT
        public int[][] JoyBtn = new int[ActCount][];    // generic controller button indices (0..31)

        public const int XA = 0x1000, XB = 0x2000, XX = 0x4000, XY = 0x8000, XLB = 0x0100, XRB = 0x0200,
                         XStart = 0x0010, XBack = 0x0020, XLT = 0x10000, XRT = 0x20000, XL3 = 0x40, XR3 = 0x80;

        public static Bindings Defaults()
        {
            var b = new Bindings();
            b.Keys[(int)Act.Up] = new[] { 0x26, 0x57 };
            b.Keys[(int)Act.Down] = new[] { 0x28, 0x53 };
            b.Keys[(int)Act.Left] = new[] { 0x25, 0x41 };
            b.Keys[(int)Act.Right] = new[] { 0x27, 0x44 };
            b.Keys[(int)Act.Jump] = new[] { 0x58, 0x4B, 0x20 };
            b.Keys[(int)Act.Run] = new[] { 0x5A, 0x4A, 0xA0 };
            b.Keys[(int)Act.Start] = new[] { 0x0D };
            b.Keys[(int)Act.Select] = new[] { 0xA1, 0x09 };
            b.SetPadPreset(0);
            b.JoyBtn[(int)Act.Up] = new int[0]; b.JoyBtn[(int)Act.Down] = new int[0];
            b.JoyBtn[(int)Act.Left] = new int[0]; b.JoyBtn[(int)Act.Right] = new int[0];
            b.JoyBtn[(int)Act.Jump] = new[] { 1, 2 };
            b.JoyBtn[(int)Act.Run] = new[] { 0, 3, 7 };
            b.JoyBtn[(int)Act.Start] = new[] { 9 };
            b.JoyBtn[(int)Act.Select] = new[] { 8 };
            return b;
        }

        /// <summary>0 = Modern (A/B jump, X/Y/RT/RB run), 1 = Nintendo layout (B jump, A/X run).</summary>
        public void SetPadPreset(int preset)
        {
            XBtn[(int)Act.Up] = new int[0]; XBtn[(int)Act.Down] = new int[0];
            XBtn[(int)Act.Left] = new int[0]; XBtn[(int)Act.Right] = new int[0];
            if (preset == 1) { XBtn[(int)Act.Jump] = new[] { XB }; XBtn[(int)Act.Run] = new[] { XA, XX, XRT }; }
            else { XBtn[(int)Act.Jump] = new[] { XA, XB }; XBtn[(int)Act.Run] = new[] { XX, XY, XRT, XRB }; }
            XBtn[(int)Act.Start] = new[] { XStart };
            XBtn[(int)Act.Select] = new[] { XBack };
        }

        public Bindings Clone()
        {
            var b = new Bindings();
            for (int i = 0; i < ActCount; i++) { b.Keys[i] = (int[])Keys[i].Clone(); b.XBtn[i] = (int[])XBtn[i].Clone(); b.JoyBtn[i] = (int[])JoyBtn[i].Clone(); }
            return b;
        }
    }

    public sealed class InputOptions
    {
        public int Deadzone = 40;        // percent
        public int StickCone = 30;       // half-angle in degrees for Up/Down on the stick (duck protection)
        public bool UseStick = true;
        public bool Vibration = true;
        public int VibrationStrength = 70;
        public int Socd = 0;             // 0 = last pressed wins, 1 = neutral
        public bool GenericPads = true;
    }

    public enum DeviceKind { Keyboard, XInput, Generic }

    public sealed class ControllerInfo
    {
        public DeviceKind Kind; public int Index; public string Name; public bool Connected;
        public int RawButtons;          // raw button bits (XInput masks or generic 0..31)
        public float StickX, StickY;    // -1..1, +Y = up
        public int Dirs;                // resolved d-pad + stick directions
        public int Actions;             // resolved non-direction actions
    }

    /// <summary>
    /// Keyboard (event-latched so sub-frame taps are never lost), XInput pads (with rumble), and
    /// DirectInput-class pads through winmm (DualShock/DualSense/Switch Pro/8BitDo/generic USB).
    /// </summary>
    public sealed class InputSystem
    {
        public Bindings Bind = Bindings.Defaults();
        public InputOptions Opt = new InputOptions();

        // ---- keyboard ----
        readonly bool[] keyDown = new bool[256];
        int kbLatchedPress, kbLatchedRelease;
        int lastHoriz = Btn.Right;
        public int LastKeyPressed = -1;          // for rebinding capture

        // ---- controllers ----
        readonly ControllerInfo[] x = new ControllerInfo[4];
        readonly int[] xNextProbe = new int[4];
        readonly ControllerInfo[] joys = new ControllerInfo[16];
        readonly N.JOYCAPS[] joyCaps = new N.JOYCAPS[16];
        volatile bool[] joyPresent = new bool[16];
        Thread joyScanThread;
        volatile bool scanning = true;
        int frame;
        public int LastPadButton = -1; public DeviceKind LastPadKind; // for rebinding capture

        // per player
        readonly int[] prevPadHeld = new int[2];
        readonly int[] prevHeld = new int[2];

        // rumble
        readonly float[] rumbleAmp = new float[4];
        readonly int[] rumbleTicks = new int[4];

        public InputSystem()
        {
            for (int i = 0; i < 4; i++) x[i] = new ControllerInfo { Kind = DeviceKind.XInput, Index = i, Name = "Xbox Controller " + (i + 1) };
            for (int i = 0; i < 16; i++) joys[i] = new ControllerInfo { Kind = DeviceKind.Generic, Index = i, Name = "" };
            joyScanThread = new Thread(ScanJoysticks) { IsBackground = true, Name = "JoyScan" };
            joyScanThread.Start();
        }

        public void Shutdown()
        {
            scanning = false;
            for (int i = 0; i < 4; i++) if (x[i].Connected) SetVibration(i, 0, 0);
        }

        // ---------------------------------------------------------------- keyboard events (from the window)
        readonly bool[] keyLatch = new bool[256];

        /// <summary>True once per physical press of a raw key (Escape, Backspace... for menus).</summary>
        public bool ConsumeKey(int vk)
        {
            if (vk < 0 || vk > 255 || !keyLatch[vk]) return false;
            keyLatch[vk] = false;
            return true;
        }

        public void ClearKeyLatches() { for (int i = 0; i < 256; i++) keyLatch[i] = false; LastKeyPressed = -1; }

        public void KeyDown(int vk)
        {
            if (vk < 0 || vk > 255) return;
            if (keyDown[vk]) return;                 // ignore auto-repeat
            keyDown[vk] = true;
            keyLatch[vk] = true;
            LastKeyPressed = vk;
            int acts = ActionsForKey(vk);
            kbLatchedPress |= acts;
            if ((acts & Btn.Left) != 0) lastHoriz = Btn.Left;
            if ((acts & Btn.Right) != 0) lastHoriz = Btn.Right;
        }

        public void KeyUp(int vk)
        {
            if (vk < 0 || vk > 255 || !keyDown[vk]) return;
            keyDown[vk] = false;
            kbLatchedRelease |= ActionsForKey(vk);
        }

        public void ReleaseAllKeys()
        {
            for (int i = 0; i < 256; i++) if (keyDown[i]) { keyDown[i] = false; kbLatchedRelease |= ActionsForKey(i); }
        }

        public bool IsKeyDown(int vk) { return vk >= 0 && vk < 256 && keyDown[vk]; }

        int ActionsForKey(int vk)
        {
            int r = 0;
            for (int a = 0; a < Bindings.ActCount; a++)
            {
                var ks = Bind.Keys[a];
                for (int i = 0; i < ks.Length; i++) if (ks[i] == vk) { r |= 1 << a; break; }
            }
            return r;
        }

        int KeyboardHeld()
        {
            int r = 0;
            for (int a = 0; a < Bindings.ActCount; a++)
            {
                var ks = Bind.Keys[a];
                for (int i = 0; i < ks.Length; i++) if (ks[i] >= 0 && ks[i] < 256 && keyDown[ks[i]]) { r |= 1 << a; break; }
            }
            return r;
        }

        // ---------------------------------------------------------------- controllers
        void ScanJoysticks()
        {
            int size = Marshal.SizeOf(typeof(N.JOYCAPS));
            while (scanning)
            {
                try
                {
                    int n = Math.Min(16, N.joyGetNumDevs());
                    for (int i = 0; i < n; i++)
                    {
                        var info = new N.JOYINFOEX(); info.dwSize = Marshal.SizeOf(typeof(N.JOYINFOEX)); info.dwFlags = N.JOY_RETURNALL;
                        bool present = N.joyGetPosEx(i, ref info) == 0;
                        if (present && !joyPresent[i])
                        {
                            var caps = new N.JOYCAPS();
                            if (N.joyGetDevCaps((IntPtr)i, ref caps, size) == 0) { joyCaps[i] = caps; joyPresent[i] = true; }
                        }
                        else if (!present) joyPresent[i] = false;
                    }
                }
                catch { }
                for (int s = 0; s < 30 && scanning; s++) Thread.Sleep(100);
            }
        }

        static bool LooksLikeXInput(string name, int vid)
        {
            string n = (name ?? "").ToUpperInvariant();
            return n.Contains("XINPUT") || n.Contains("XBOX") || n.Contains("X-BOX") || vid == 0x045E;
        }

        public void PollDevices()
        {
            frame++;
            float dz = Opt.Deadzone / 100f;
            for (int i = 0; i < 4; i++)
            {
                var c = x[i];
                if (!c.Connected && frame < xNextProbe[i]) continue;
                N.XINPUT_STATE st;
                uint res;
                try { res = N.XInputGetState((uint)i, out st); } catch { res = 1167; st = new N.XINPUT_STATE(); }
                if (res != 0) { if (c.Connected) { c.Connected = false; c.RawButtons = 0; c.Dirs = 0; c.Actions = 0; } xNextProbe[i] = frame + 120; continue; }
                if (!c.Connected) { c.Connected = true; rumbleTicks[i] = 0; }
                var g = st.Gamepad;
                int raw = g.wButtons;
                if (g.bLeftTrigger > 64) raw |= Bindings.XLT;
                if (g.bRightTrigger > 64) raw |= Bindings.XRT;
                if ((raw & ~c.RawButtons & ~0xF) != 0) { LastPadButton = LowestBit(raw & ~c.RawButtons & ~0xF); LastPadKind = DeviceKind.XInput; }
                c.RawButtons = raw;
                c.StickX = Math.Max(-1f, g.sThumbLX / 32767f); c.StickY = Math.Max(-1f, g.sThumbLY / 32767f);
                int dirs = 0;
                if ((raw & 0x1) != 0) dirs |= Btn.Up;
                if ((raw & 0x2) != 0) dirs |= Btn.Down;
                if ((raw & 0x4) != 0) dirs |= Btn.Left;
                if ((raw & 0x8) != 0) dirs |= Btn.Right;
                if (Opt.UseStick) dirs |= StickToDirs(c.StickX, c.StickY, dz, c.Dirs);
                c.Dirs = dirs;
                c.Actions = MapButtons(raw, Bind.XBtn, true);
                UpdateRumble(i);
            }
            bool anyX = x[0].Connected || x[1].Connected || x[2].Connected || x[3].Connected;
            for (int i = 0; i < 16; i++)
            {
                var c = joys[i];
                if (!Opt.GenericPads || !joyPresent[i]) { c.Connected = false; c.RawButtons = 0; c.Dirs = 0; c.Actions = 0; continue; }
                var caps = joyCaps[i];
                if (anyX && LooksLikeXInput(caps.szPname, caps.wMid)) { c.Connected = false; continue; }
                var info = new N.JOYINFOEX(); info.dwSize = Marshal.SizeOf(typeof(N.JOYINFOEX)); info.dwFlags = N.JOY_RETURNALL | N.JOY_RETURNPOVCTS;
                if (N.joyGetPosEx(i, ref info) != 0) { c.Connected = false; joyPresent[i] = false; continue; }
                c.Connected = true;
                c.Name = string.IsNullOrEmpty(caps.szPname) ? "Controller" : caps.szPname;
                int raw = info.dwButtons;
                if ((raw & ~c.RawButtons) != 0) { LastPadButton = BitIndex(raw & ~c.RawButtons); LastPadKind = DeviceKind.Generic; }
                c.RawButtons = raw;
                c.StickX = Norm(info.dwXpos, caps.wXmin, caps.wXmax);
                c.StickY = -Norm(info.dwYpos, caps.wYmin, caps.wYmax);
                int dirs = 0;
                int pov = info.dwPOV & 0xFFFF;
                if (pov != 0xFFFF && pov < 36000)
                {
                    if (pov >= 31500 || pov <= 4500) dirs |= Btn.Up;
                    if (pov >= 4500 && pov <= 13500) dirs |= Btn.Right;
                    if (pov >= 13500 && pov <= 22500) dirs |= Btn.Down;
                    if (pov >= 22500 && pov <= 31500) dirs |= Btn.Left;
                }
                // Many retro-style pads report the D-pad on the X/Y axes, so the stick path always applies to them.
                dirs |= StickToDirs(c.StickX, c.StickY, dz, c.Dirs);
                c.Dirs = dirs;
                int acts = 0;
                for (int a = (int)Act.Jump; a < Bindings.ActCount; a++)
                {
                    var bs = Bind.JoyBtn[a];
                    for (int k = 0; k < bs.Length; k++) if (bs[k] >= 0 && bs[k] < 32 && (raw & (1 << bs[k])) != 0) { acts |= 1 << a; break; }
                }
                c.Actions = acts;
            }
            TrackConnections();
        }

        static float Norm(int v, uint min, uint max)
        {
            if (max <= min) return 0;
            float f = (v - (float)min) / (max - (float)min) * 2f - 1f;
            return f < -1 ? -1 : f > 1 ? 1 : f;
        }

        static int LowestBit(int v) { return v & -v; }
        static int BitIndex(int v) { for (int i = 0; i < 32; i++) if ((v & (1 << i)) != 0) return i; return -1; }

        int MapButtons(int raw, int[][] map, bool xinput)
        {
            int acts = 0;
            for (int a = (int)Act.Jump; a < Bindings.ActCount; a++)
            {
                var bs = map[a];
                for (int k = 0; k < bs.Length; k++) if ((raw & bs[k]) != 0) { acts |= 1 << a; break; }
            }
            return acts;
        }

        /// <summary>
        /// Radial deadzone with angular cones: left/right within ±67.5° of horizontal, up/down only within
        /// ±StickCone° of vertical so a running diagonal never ducks by accident. 5% hysteresis.
        /// </summary>
        int StickToDirs(float sx, float sy, float dz, int prev)
        {
            float mag = (float)Math.Sqrt(sx * sx + sy * sy);
            int prevStick = prev;
            float need = dz;
            if (mag < need - 0.05f || (mag < need && prevStick == 0)) return 0;
            double ang = Math.Atan2(sy, sx) * 180.0 / Math.PI;   // 0 = right, 90 = up
            int r = 0;
            double hcone = 67.5, vcone = Opt.StickCone;
            double hyst = 5;
            if (Math.Abs(ang) <= hcone + ((prev & Btn.Right) != 0 ? hyst : 0)) r |= Btn.Right;
            if (Math.Abs(ang) >= 180 - hcone - ((prev & Btn.Left) != 0 ? hyst : 0)) r |= Btn.Left;
            if (Math.Abs(ang - 90) <= vcone + ((prev & Btn.Up) != 0 ? hyst : 0)) r |= Btn.Up;
            if (Math.Abs(ang + 90) <= vcone + ((prev & Btn.Down) != 0 ? hyst : 0)) r |= Btn.Down;
            return r;
        }

        readonly List<ControllerInfo> connected = new List<ControllerInfo>();
        int lastConnectedCount = -1;
        /// <summary>Set when a controller disconnects (the game auto-pauses); cleared by the reader.</summary>
        public bool ControllerLost;

        /// <summary>Controllers in stable order: XInput 1-4, then generic pads (list is reused; don't keep it).</summary>
        public List<ControllerInfo> ConnectedControllers()
        {
            connected.Clear();
            for (int i = 0; i < 4; i++) if (x[i].Connected) connected.Add(x[i]);
            for (int i = 0; i < 16; i++) if (joys[i].Connected) connected.Add(joys[i]);
            return connected;
        }

        void TrackConnections()
        {
            int n = ConnectedControllers().Count;
            if (lastConnectedCount >= 0 && n < lastConnectedCount) ControllerLost = true;
            lastConnectedCount = n;
        }

        int PadHeldFor(int player)
        {
            var l = ConnectedControllers();
            if (player < l.Count) return l[player].Dirs | l[player].Actions;
            return 0;
        }

        /// <summary>
        /// Called exactly once per simulation tick per player. Player 0 = keyboard + first controller,
        /// player 1 = second controller. Applies SOCD cleaning and edge detection.
        /// </summary>
        public PadState Sample(int player, bool consumeKeyboard)
        {
            int kb = player == 0 ? KeyboardHeld() : 0;
            int pad = PadHeldFor(player);
            int newPad = pad & ~prevPadHeld[player];
            if ((newPad & Btn.Left) != 0) lastHoriz = Btn.Left;
            if ((newPad & Btn.Right) != 0) lastHoriz = Btn.Right;
            prevPadHeld[player] = pad;

            int held = kb | pad;
            if ((held & (Btn.Left | Btn.Right)) == (Btn.Left | Btn.Right))
                held &= Opt.Socd == 1 ? ~(Btn.Left | Btn.Right) : ~((Btn.Left | Btn.Right) & ~lastHoriz);
            if ((held & (Btn.Up | Btn.Down)) == (Btn.Up | Btn.Down)) held &= ~(Btn.Up | Btn.Down);

            var s = new PadState();
            s.Held = held;
            s.Pressed = (held & ~prevHeld[player]);
            s.Released = (~held & prevHeld[player]);
            if (player == 0)
            {
                // keyboard latches: a tap that started and ended between ticks still counts as a press
                s.Pressed |= kbLatchedPress & ~Btn.Dirs | (kbLatchedPress & Btn.Dirs & held);
                s.Released |= kbLatchedRelease & ~held;
                if (consumeKeyboard) { kbLatchedPress = 0; kbLatchedRelease = 0; }
            }
            prevHeld[player] = held;
            return s;
        }

        // ---------------------------------------------------------------- rumble
        public void Rumble(int player, float amp, int ticks)
        {
            if (!Opt.Vibration) return;
            var l = ConnectedControllers();
            if (player >= l.Count || l[player].Kind != DeviceKind.XInput) return;
            int i = l[player].Index;
            float a = amp * Opt.VibrationStrength / 100f;
            if (a >= rumbleAmp[i] || rumbleTicks[i] <= 0) { rumbleAmp[i] = a; rumbleTicks[i] = ticks; }
        }

        void UpdateRumble(int i)
        {
            if (rumbleTicks[i] > 0)
            {
                rumbleTicks[i]--;
                ushort v = (ushort)(Math.Min(1f, rumbleAmp[i]) * 65535);
                SetVibration(i, v, (ushort)(v * 3 / 4));
                if (rumbleTicks[i] == 0) SetVibration(i, 0, 0);
            }
        }

        void SetVibration(int i, ushort l, ushort r)
        {
            try { var v = new N.XINPUT_VIBRATION { wLeftMotorSpeed = l, wRightMotorSpeed = r }; N.XInputSetState((uint)i, ref v); } catch { }
        }

        // ---------------------------------------------------------------- names for the controls UI
        public static string KeyName(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69) return "NUM" + (vk - 0x60);
            switch (vk)
            {
                case 0x26: return "UP"; case 0x28: return "DOWN"; case 0x25: return "LEFT"; case 0x27: return "RIGHT";
                case 0x20: return "SPACE"; case 0x0D: return "ENTER"; case 0x09: return "TAB"; case 0x08: return "BACKSPACE";
                case 0xA0: return "LSHIFT"; case 0xA1: return "RSHIFT"; case 0xA2: return "LCTRL"; case 0xA3: return "RCTRL";
                case 0xA4: return "LALT"; case 0xA5: return "RALT"; case 0x1B: return "ESC"; case 0x14: return "CAPS";
                case 0xBA: return ";"; case 0xBB: return "="; case 0xBC: return ","; case 0xBD: return "-"; case 0xBE: return ".";
                case 0xBF: return "/"; case 0xC0: return "`"; case 0xDB: return "["; case 0xDC: return "\\"; case 0xDD: return "]"; case 0xDE: return "'";
                case 0x2D: return "INSERT"; case 0x2E: return "DELETE"; case 0x24: return "HOME"; case 0x23: return "END";
                case 0x21: return "PGUP"; case 0x22: return "PGDN";
            }
            return "KEY" + vk.ToString("X2");
        }

        public static string XButtonName(int mask)
        {
            switch (mask)
            {
                case Bindings.XA: return "A"; case Bindings.XB: return "B"; case Bindings.XX: return "X"; case Bindings.XY: return "Y";
                case Bindings.XLB: return "LB"; case Bindings.XRB: return "RB"; case Bindings.XLT: return "LT"; case Bindings.XRT: return "RT";
                case Bindings.XStart: return "MENU"; case Bindings.XBack: return "VIEW"; case Bindings.XL3: return "L3"; case Bindings.XR3: return "R3";
            }
            return "?";
        }

        public static string JoyButtonName(int idx) { return "BTN " + (idx + 1); }
    }
}
