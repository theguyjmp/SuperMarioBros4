using System;
using System.Collections.Generic;
using SMB4.Audio;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Game
{
    /// <summary>Options: video, audio, controls (keyboard / Xbox / other controllers), gameplay.</summary>
    public sealed class OptionsScreen : Screen
    {
        sealed class Opt
        {
            public string Label, Help = "";
            public Func<string> Value;
            public Action<int> Change;
            public Action Activate;
        }

        readonly Screen back;
        int page, sel, scroll, t;
        readonly Stack<int> pageStack = new Stack<int>();
        List<Opt> items = new List<Opt>();
        readonly MenuNav nav = new MenuNav();
        // rebinding capture
        int capKind = -1;      // 0 keyboard, 1 xinput, 2 generic
        int capAct;
        int capWait;
        bool confirmReset;
        string toast = ""; int toastT;

        const int Main = 0, Video = 1, AudioP = 2, Controls = 3, Gameplay = 4, Keys = 5, Pad = 6, Joy = 7, Test = 8;

        public OptionsScreen(Screen back) { this.back = back; }
        public OptionsScreen(Screen back, int startPage) { this.back = back; page = startPage; }

        Settings S { get { return Host.Settings; } }

        public override void Enter() { Build(); }

        static string OnOff(bool b) { return b ? "ON" : "OFF"; }
        static int Wrap(int v, int n) { return ((v % n) + n) % n; }

        void Build()
        {
            items = new List<Opt>();
            switch (page)
            {
                case Main:
                    Add("VIDEO", () => "", null, () => Push(Video), "DISPLAY MODE, SCALING, SCANLINES, FRAME PACING.");
                    Add("AUDIO", () => "", null, () => Push(AudioP), "MUSIC AND SOUND EFFECT VOLUME.");
                    Add("CONTROLS", () => "", null, () => Push(Controls), "KEYBOARD AND CONTROLLER SETTINGS.");
                    Add("GAMEPLAY", () => "", null, () => Push(Gameplay), "FEEL PROFILE AND ASSISTS.");
                    Add("BACK", () => "", null, Close, "");
                    break;
                case Video:
                    Add("DISPLAY", () => S.Fullscreen ? "FULLSCREEN" : "WINDOWED", d => { S.Fullscreen = !S.Fullscreen; Host.ApplyVideo(); }, null, "F11 OR ALT+ENTER ALSO TOGGLES FULLSCREEN.");
                    Add("WINDOW SIZE", () => S.WindowScale <= 0 ? "AUTO" : S.WindowScale + "X", d => { S.WindowScale = Wrap((S.WindowScale <= 0 ? 4 : S.WindowScale) - 2 + d, 6) + 2; Host.ApplyVideo(); }, null, "SIZE OF THE WINDOW IN WINDOWED MODE.");
                    Add("SCALING", () => S.Scale == ScaleMode.Integer ? "PIXEL PERFECT" : S.Scale == ScaleMode.Fit ? "FIT (SHARP)" : "STRETCH", d => { S.Scale = (ScaleMode)Wrap((int)S.Scale + d, 3); }, null, "PIXEL PERFECT KEEPS EVERY PIXEL THE SAME SIZE.");
                    Add("PIXEL SHAPE", () => S.NtscAspect ? "CRT 8:7" : "SQUARE", d => S.NtscAspect = !S.NtscAspect, null, "8:7 MATCHES HOW 16-BIT CONSOLES LOOKED ON A TV.");
                    Add("SCANLINES", () => S.Scanlines == 0 ? "OFF" : S.Scanlines <= 25 ? "LIGHT" : S.Scanlines <= 40 ? "MEDIUM" : "HEAVY", d => { int[] v = { 0, 25, 40, 60 }; int i = Array.IndexOf(v, S.Scanlines); if (i < 0) i = 0; S.Scanlines = v[Wrap(i + d, 4)]; }, null, "CRT-STYLE SCANLINES (NEEDS A LARGE WINDOW).");
                    Add("FRAME PACING", () => new[] { "AUTO", "VSYNC LOCK", "SMOOTH", "VRR" }[S.Pacing], d => { S.Pacing = Wrap(S.Pacing + d, 4); Host.ApplyVideo(); }, null, "AUTO LOCKS TO 60/120/240HZ DISPLAYS. PICK VRR IF YOU USE G-SYNC OR FREESYNC.");
                    Add("LOW LATENCY", () => OnOff(S.LowLatency), d => S.LowLatency = !S.LowLatency, null, "STOPS THE DRIVER QUEUEING FRAMES. KEEP ON.");
                    Add("SHOW FPS", () => OnOff(S.ShowFps), d => S.ShowFps = !S.ShowFps, null, "");
                    Add("SCREEN SHAKE", () => OnOff(S.ScreenShake), d => S.ScreenShake = !S.ScreenShake, null, "");
                    Add("REDUCE FLASHING", () => OnOff(S.ReduceFlashing), d => S.ReduceFlashing = !S.ReduceFlashing, null, "SLOWS STAR, P-SWITCH AND BOSS FLASHING.");
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case AudioP:
                    Add("MUSIC", () => S.MusicVolume + "%", d => { S.MusicVolume = Math.Max(0, Math.Min(100, S.MusicVolume + d * 10)); Sound.ApplyVolumes(); }, null, "");
                    Add("SOUND EFFECTS", () => S.SfxVolume + "%", d => { S.SfxVolume = Math.Max(0, Math.Min(100, S.SfxVolume + d * 10)); Sound.ApplyVolumes(); Sound.Sfx(SfxId.Coin); }, null, "");
                    Add("SOUND STYLE", () => S.AuthenticFilter ? "WARM 16-BIT" : "CLEAN", d => { S.AuthenticFilter = !S.AuthenticFilter; Sound.ApplyVolumes(); }, null, "WARM ADDS SNES-STYLE CONSOLE FILTERING.");
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case Controls:
                    Add("KEYBOARD", () => "", null, () => Push(Keys), "REBIND KEYBOARD KEYS.");
                    Add("XBOX CONTROLLER", () => "", null, () => Push(Pad), "REBIND BUTTONS ON XBOX-STYLE (XINPUT) CONTROLLERS.");
                    Add("OTHER CONTROLLERS", () => "", null, () => Push(Joy), "REBIND PLAYSTATION, SWITCH PRO, 8BITDO AND USB PADS.");
                    Add("BUTTON LAYOUT", () => S.PadPreset == 0 ? "MODERN" : "NINTENDO", d => { S.PadPreset = 1 - S.PadPreset; S.Bind.SetPadPreset(S.PadPreset); }, null, S.PadPreset == 0 ? "MODERN: A/B JUMP, X/Y/RT/RB RUN." : "NINTENDO: B JUMP, A/X/RT RUN.");
                    Add("STICK DEADZONE", () => S.Input.Deadzone + "%", d => S.Input.Deadzone = Math.Max(10, Math.Min(70, S.Input.Deadzone + d * 5)), null, "HOW FAR THE STICK MUST MOVE BEFORE IT COUNTS.");
                    Add("DUCK ANGLE", () => S.Input.StickCone + " DEG", d => S.Input.StickCone = Math.Max(20, Math.Min(65, S.Input.StickCone + d * 5)), null, "HOW STRAIGHT DOWN/UP THE STICK MUST POINT. SMALLER = NO ACCIDENTAL DUCKING.");
                    Add("USE LEFT STICK", () => OnOff(S.Input.UseStick), d => S.Input.UseStick = !S.Input.UseStick, null, "THE D-PAD ALWAYS WORKS.");
                    Add("VIBRATION", () => OnOff(S.Input.Vibration), d => { S.Input.Vibration = !S.Input.Vibration; if (S.Input.Vibration) Host.Input.Rumble(0, 0.8f, 12); }, null, "RUMBLE ON XBOX-STYLE CONTROLLERS.");
                    Add("VIBRATION STRENGTH", () => S.Input.VibrationStrength + "%", d => { S.Input.VibrationStrength = Math.Max(10, Math.Min(100, S.Input.VibrationStrength + d * 10)); Host.Input.Rumble(0, 0.8f, 12); }, null, "");
                    Add("LEFT+RIGHT", () => S.Input.Socd == 0 ? "LAST WINS" : "NEUTRAL", d => S.Input.Socd = 1 - S.Input.Socd, null, "WHAT HAPPENS WHEN LEFT AND RIGHT ARE HELD TOGETHER (KEYBOARDS).");
                    Add("OTHER CONTROLLERS", () => OnOff(S.Input.GenericPads), d => S.Input.GenericPads = !S.Input.GenericPads, null, "SUPPORT FOR NON-XBOX CONTROLLERS.");
                    Add("TEST CONTROLLERS", () => "", null, () => Push(Test), "SEE WHAT THE GAME RECEIVES FROM EACH DEVICE.");
                    Add("RESET CONTROLS", () => "", null, () => { confirmReset = true; }, "RESTORE ALL DEFAULT BINDINGS.");
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case Gameplay:
                    Add("FEEL", () => S.ModernFeel ? "MODERN" : "CLASSIC", d => S.ModernFeel = !S.ModernFeel, null, S.ModernFeel ? "MODERN: SMB3 PHYSICS + COYOTE TIME AND JUMP BUFFER." : "CLASSIC: EXACT SMB3 BEHAVIOUR, NO LENIENCY.");
                    Add("PAUSE WHEN INACTIVE", () => OnOff(S.PauseOnFocusLoss), d => S.PauseOnFocusLoss = !S.PauseOnFocusLoss, null, "PAUSE WHEN THE WINDOW LOSES FOCUS.");
                    Add("INFINITE LIVES", () => OnOff(S.InfiniteLives), d => S.InfiniteLives = !S.InfiniteLives, null, "ASSIST: NEVER RUN OUT OF LIVES.");
                    Add("INPUT DISPLAY", () => OnOff(S.ShowInputDisplay), d => S.ShowInputDisplay = !S.ShowInputDisplay, null, "SHOWS YOUR INPUTS ON SCREEN.");
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case Keys:
                    for (int a = 0; a < Bindings.ActCount; a++) { int act = a; Add(((Act)a).ToString().ToUpperInvariant(), () => KeyList(act), null, () => BeginCapture(0, act), "PRESS A TO REBIND. DELETE CLEARS."); }
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case Pad:
                    for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) { int act = a; Add(((Act)a).ToString().ToUpperInvariant(), () => PadList(act), null, () => BeginCapture(1, act), "D-PAD AND LEFT STICK ALWAYS MOVE."); }
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case Joy:
                    for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) { int act = a; Add(((Act)a).ToString().ToUpperInvariant(), () => JoyList(act), null, () => BeginCapture(2, act), "D-PAD/HAT AND LEFT STICK ALWAYS MOVE."); }
                    Add("BACK", () => "", null, Pop, "");
                    break;
                case Test:
                    Add("BACK", () => "", null, Pop, "");
                    break;
            }
            sel = Math.Min(sel, items.Count - 1);
        }

        void Add(string label, Func<string> value, Action<int> change, Action activate, string help)
        {
            items.Add(new Opt { Label = label, Value = value, Change = change, Activate = activate, Help = help });
        }

        void Push(int p) { pageStack.Push(page); page = p; sel = 0; scroll = 0; Build(); Sound.Sfx(SfxId.MenuSelect); }
        void Pop()
        {
            Sound.Sfx(SfxId.MenuBack);
            S.Save(); Host.ApplyInputSettings();
            if (pageStack.Count == 0) { Close(); return; }
            page = pageStack.Pop(); sel = 0; scroll = 0; Build();
        }
        void Close()
        {
            S.Save(); Host.ApplyInputSettings();
            G.Go(back, false);
        }

        string KeyList(int a) { var l = new List<string>(); foreach (var k in S.Bind.Keys[a]) l.Add(InputSystem.KeyName(k)); return l.Count == 0 ? "-" : string.Join("/", l); }
        string PadList(int a) { var l = new List<string>(); foreach (var k in S.Bind.XBtn[a]) l.Add(InputSystem.XButtonName(k)); return l.Count == 0 ? "-" : string.Join("/", l); }
        string JoyList(int a) { var l = new List<string>(); foreach (var k in S.Bind.JoyBtn[a]) l.Add((k + 1).ToString()); return l.Count == 0 ? "-" : "BTN " + string.Join("/", l); }

        void BeginCapture(int kind, int act)
        {
            capKind = kind; capAct = act; capWait = 12;
            Host.Input.LastKeyPressed = -1; Host.Input.LastPadButton = -1;
            Host.Input.ClearKeyLatches();
        }

        static int[] Prepend(int[] list, int v)
        {
            var l = new List<int> { v };
            foreach (var x in list) if (x != v && l.Count < 3) l.Add(x);
            return l.ToArray();
        }

        static int[] Without(int[] list, int v) { var l = new List<int>(); foreach (var x in list) if (x != v) l.Add(x); return l.ToArray(); }

        void CaptureTick()
        {
            if (capWait > 0) { capWait--; Host.Input.LastKeyPressed = -1; Host.Input.LastPadButton = -1; return; }
            var inp = Host.Input;
            if (inp.ConsumeKey(0x1B)) { capKind = -1; Sound.Sfx(SfxId.MenuBack); return; }
            if (capKind == 0)
            {
                if (inp.ConsumeKey(0x2E)) { S.Bind.Keys[capAct] = new int[0]; capKind = -1; Host.ApplyInputSettings(); return; }
                int k = inp.LastKeyPressed;
                if (k > 0 && k != 0x7A && k != 0x7B)
                {
                    for (int a = 0; a < Bindings.ActCount; a++) S.Bind.Keys[a] = Without(S.Bind.Keys[a], k);
                    S.Bind.Keys[capAct] = Prepend(S.Bind.Keys[capAct], k);
                    capKind = -1; Sound.Sfx(SfxId.MenuSelect); Host.ApplyInputSettings(); inp.ReleaseAllKeys();
                    toast = "BOUND " + InputSystem.KeyName(k); toastT = 60;
                }
            }
            else
            {
                if (inp.ConsumeKey(0x2E)) { if (capKind == 1) S.Bind.XBtn[capAct] = new int[0]; else S.Bind.JoyBtn[capAct] = new int[0]; capKind = -1; return; }
                int b = inp.LastPadButton;
                if (b >= 0 && ((capKind == 1 && inp.LastPadKind == DeviceKind.XInput) || (capKind == 2 && inp.LastPadKind == DeviceKind.Generic)))
                {
                    if (capKind == 1)
                    {
                        for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) S.Bind.XBtn[a] = Without(S.Bind.XBtn[a], b);
                        S.Bind.XBtn[capAct] = Prepend(S.Bind.XBtn[capAct], b);
                        toast = "BOUND " + InputSystem.XButtonName(b);
                    }
                    else
                    {
                        for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) S.Bind.JoyBtn[a] = Without(S.Bind.JoyBtn[a], b);
                        S.Bind.JoyBtn[capAct] = Prepend(S.Bind.JoyBtn[capAct], b);
                        toast = "BOUND BUTTON " + (b + 1);
                    }
                    toastT = 60;
                    capKind = -1; capWait = 0; Sound.Sfx(SfxId.MenuSelect); Host.ApplyInputSettings();
                    swallow = 20;
                }
            }
        }
        int swallow;

        public override void Tick(PadState p1, PadState p2, PadState any)
        {
            t++;
            if (toastT > 0) toastT--;
            if (capKind >= 0) { CaptureTick(); return; }
            if (swallow > 0) { swallow--; return; }
            if (confirmReset)
            {
                if (any.P(Btn.A) || any.P(Btn.Start))
                {
                    S.Bind = Bindings.Defaults(); S.Input = new InputOptions(); S.PadPreset = 0;
                    Host.ApplyInputSettings(); confirmReset = false; toast = "CONTROLS RESET"; toastT = 90; Sound.Sfx(SfxId.MenuSelect);
                }
                else if (any.P(Btn.B) || G.BackPressed) confirmReset = false;
                return;
            }
            if (G.BackPressed || any.P(Btn.B)) { Pop(); return; }
            int v = nav.Vertical(any);
            if (v != 0) { sel = Wrap(sel + v, items.Count); Sound.Sfx(SfxId.MenuMove); }
            var it = items[sel];
            int h = nav.Horizontal(any);
            if (h != 0 && it.Change != null) { it.Change(h); Sound.Sfx(SfxId.MenuMove); Build(); }
            if (any.P(Btn.A) || any.P(Btn.Start))
            {
                if (it.Activate != null) it.Activate();
                else if (it.Change != null) { it.Change(1); Sound.Sfx(SfxId.MenuMove); Build(); }
            }
            if (sel < scroll) scroll = sel;
            if (sel >= scroll + 13) scroll = sel - 12;
        }

        static readonly string[] Titles = { "OPTIONS", "VIDEO", "AUDIO", "CONTROLS", "GAMEPLAY", "KEYBOARD", "XBOX CONTROLLER", "OTHER CONTROLLERS", "CONTROLLER TEST" };

        public override void Render(Ppu ppu)
        {
            ppu.Clear(0x0F);
            Hud.Backdrop(ppu, t);
            Hud.Window(ppu, 8, 8, 240, 224);
            string title = Titles[page];
            Hud.Txt(ppu, title, (256 - title.Length * 8) / 2, 18, 0x28);
            if (page == Test) { RenderTest(ppu); }
            for (int i = scroll; i < items.Count && i < scroll + 13; i++)
            {
                int y = 36 + (i - scroll) * 12;
                if (page == Test) y = 204;
                var it = items[i];
                bool on = i == sel;
                Hud.Txt(ppu, it.Label, 28, y, on ? 0x30 : 0x10);
                string val = it.Value();
                if (val.Length > 0) Hud.Txt(ppu, val, 236 - val.Length * 8, y, on ? 0x28 : 0x00 + 0x10);
                if (on) Hud.Cursor(ppu, 16, y, t);
            }
            // help line
            var cur = items[Math.Min(sel, items.Count - 1)];
            if (cur.Help.Length > 0 && page != Test) WrapText(ppu, cur.Help, 20, 198, 27, 0x21);
            if (page == Video) Hud.Txt(ppu, Host.RefreshHz + "HZ  " + Host.PacingInfo, 20, 188, 0x00);
            if (capKind >= 0)
            {
                Hud.Window(ppu, 24, 84, 208, 64);
                string what = capKind == 0 ? "PRESS A KEY" : capKind == 1 ? "PRESS A CONTROLLER BUTTON" : "PRESS A BUTTON";
                Hud.TxtC(ppu, what, 98, 0x30);
                Hud.TxtC(ppu, "FOR " + ((Act)capAct).ToString().ToUpperInvariant(), 112, 0x28);
                Hud.TxtC(ppu, "ESC CANCEL  DEL CLEAR", 130, 0x10);
            }
            if (confirmReset)
            {
                Hud.Window(ppu, 32, 90, 192, 48);
                Hud.TxtC(ppu, "RESET ALL CONTROLS?", 102, 0x30);
                Hud.TxtC(ppu, "A: YES   B: NO", 118, 0x10);
            }
            if (toastT > 0) { ppu.FillRect(0, 222, 256, 10, 0x0F); Hud.TxtC(ppu, toast, 223, 0x2A); }
        }

        static void WrapText(Ppu ppu, string s, int x, int y, int cols, int color)
        {
            var words = s.Split(' ');
            string line = "";
            foreach (var w in words)
            {
                if ((line + " " + w).Trim().Length > cols) { Hud.Txt(ppu, line.Trim(), x, y, color); y += 9; line = ""; }
                line += " " + w;
            }
            if (line.Trim().Length > 0) Hud.Txt(ppu, line.Trim(), x, y, color);
        }

        void RenderTest(Ppu ppu)
        {
            var list = Host.Input.ConnectedControllers();
            int y = 34;
            Hud.Txt(ppu, "KEYBOARD  = PLAYER 1", 20, y, 0x10); y += 12;
            if (list.Count == 0) { Hud.Txt(ppu, "NO CONTROLLERS FOUND", 20, y, 0x16); y += 12; Hud.Txt(ppu, "PLUG ONE IN AT ANY TIME.", 20, y, 0x10); }
            for (int i = 0; i < list.Count && i < 4; i++)
            {
                var c = list[i];
                string name = (c.Kind == DeviceKind.XInput ? "XBOX: " : "PAD: ") + c.Name.ToUpperInvariant();
                if (name.Length > 27) name = name.Substring(0, 27);
                Hud.Txt(ppu, name, 20, y, 0x30); y += 10;
                Hud.Txt(ppu, "PLAYER " + (i + 1), 20, y, i < 2 ? 0x2A : 0x10);
                DrawPad(ppu, 110, y - 2, c.Dirs | c.Actions);
                y += 12;
                Hud.Txt(ppu, "STICK " + (c.StickX >= 0 ? "+" : "") + c.StickX.ToString("0.00") + " " + (c.StickY >= 0 ? "+" : "") + c.StickY.ToString("0.00"), 20, y, 0x10);
                y += 10;
                string raw = "BTN";
                if (c.Kind == DeviceKind.Generic) { for (int b = 0; b < 16; b++) if ((c.RawButtons & (1 << b)) != 0) raw += " " + (b + 1); }
                else foreach (int m in new[] { Bindings.XA, Bindings.XB, Bindings.XX, Bindings.XY, Bindings.XLB, Bindings.XRB, Bindings.XLT, Bindings.XRT, Bindings.XStart, Bindings.XBack }) if ((c.RawButtons & m) != 0) raw += " " + InputSystem.XButtonName(m);
                Hud.Txt(ppu, raw.Length > 27 ? raw.Substring(0, 27) : raw, 20, y, 0x21);
                y += 14;
            }
        }

        static void DrawPad(Ppu ppu, int x, int y, int held)
        {
            ppu.FillRect(x, y, 64, 12, 0x00);
            string[] n = { "U", "D", "L", "R", "A", "B", "S", "E" };
            int[] bits = { Btn.Up, Btn.Down, Btn.Left, Btn.Right, Btn.A, Btn.B, Btn.Start, Btn.Select };
            for (int i = 0; i < 8; i++) Hud.Txt(ppu, n[i], x + i * 8, y + 2, (held & bits[i]) != 0 ? 0x28 : 0x0F);
        }
    }
}
