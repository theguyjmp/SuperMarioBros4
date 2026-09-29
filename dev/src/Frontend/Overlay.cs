using System;
using System.Collections.Generic;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Frontend
{
    /// <summary>
    /// Everything the player draws over the game: the options menu (Esc, or hold SELECT+START on a pad), toasts
    /// and the FPS readout. Drawn into a 256-wide Ppu in the game's logical resolution; the window composites it.
    /// The game is paused while the menu is open.
    /// </summary>
    public sealed class Overlay
    {
        sealed class Opt
        {
            public string Label, Help = "";
            public Func<string> Value;
            public Action<int> Change;
            public Action Activate;
        }

        public bool MenuOpen { get; private set; }
        int page, sel, scroll, t;
        readonly Stack<int> pageStack = new Stack<int>();
        readonly Stack<int> selStack = new Stack<int>();
        List<Opt> items = new List<Opt>();
        readonly MenuNav nav = new MenuNav();
        int capKind = -1, capAct, capWait, swallow;
        string confirmText; Action confirmYes;
        string toast = ""; int toastT;
        int rawPrev;
        int rows = 12;

        const int Main = 0, Video = 1, Timing = 2, AudioP = 3, Controls = 4, Keys = 5, Pad = 6, Joy = 7, Test = 8, CoreOpts = 9;
        static readonly string[] Titles = { "OPTIONS", "VIDEO", "TIMING AND LATENCY", "AUDIO", "CONTROLS", "KEYBOARD", "XBOX CONTROLLER", "OTHER CONTROLLERS", "CONTROLLER TEST", "EMULATOR CORE" };
        static readonly string[] ButtonNames = { "UP", "DOWN", "LEFT", "RIGHT", "B", "Y", "START", "SELECT", "A", "X", "L", "R" };

        static PlayerSettings S { get { return Player.Settings; } }

        // ---------------------------------------------------------------- public API
        public void Open()
        {
            if (MenuOpen) return;
            MenuOpen = true; page = Main; sel = 0; scroll = 0; pageStack.Clear(); selStack.Clear();
            capKind = -1; confirmText = null; swallow = 6;
            Player.Input.ClearKeyLatches();
            Build();
        }

        public void Close()
        {
            if (!MenuOpen) return;
            MenuOpen = false; capKind = -1; confirmText = null;
            S.Save(); Player.ApplyInputSettings();
            Player.Input.ClearKeyLatches();
        }

        public void Toast(string s, int frames = 120) { toast = s.ToUpperInvariant(); toastT = frames; }

        /// <summary>Escape key: cancel capture/confirm, go back a page, or open/close the menu.</summary>
        public void OnEscape()
        {
            if (!MenuOpen) { Open(); return; }
            if (capKind >= 0) { capKind = -1; return; }
            if (confirmText != null) { confirmText = null; return; }
            Pop();
        }

        // ---------------------------------------------------------------- pages
        static string OnOff(bool b) { return b ? "ON" : "OFF"; }
        static int Wrap(int v, int n) { return ((v % n) + n) % n; }

        void Build()
        {
            items = new List<Opt>();
            var emu = Player.Emu;
            switch (page)
            {
                case Main:
                    Add("RESUME", null, null, Close, "");
                    Add("VIDEO", null, null, () => Push(Video), "WINDOW, SCALING, PIXEL SHAPE, SCANLINES.");
                    Add("TIMING AND LATENCY", null, null, () => Push(Timing), "FRAME PACING, RUN-AHEAD, LOW LATENCY.");
                    Add("AUDIO", null, null, () => Push(AudioP), "VOLUME AND AUDIO BUFFER.");
                    Add("CONTROLS", null, null, () => Push(Controls), "KEYBOARD AND CONTROLLER SETTINGS.");
                    Add("SAVE STATE", null, null, () => { Player.Window.SaveState(); Close(); }, "F5 SAVES A STATE ANY TIME. YOUR NORMAL GAME SAVES ARE KEPT SEPARATELY.");
                    Add("LOAD STATE", null, null, () => { if (Player.Window.LoadState()) Close(); }, "F9 LOADS THE SAVED STATE.");
                    Add("EMULATOR CORE", null, null, () => Push(CoreOpts), (emu != null ? emu.Core.LibraryName + " " + emu.Core.LibraryVersion : "").ToUpperInvariant());
                    Add("RESET GAME", null, null, () => Ask("RESET THE GAME?", () => { Player.Emu.Reset(); Close(); Toast("RESET"); }), "LIKE PRESSING RESET ON THE CONSOLE.");
                    Add("QUIT", null, null, () => Ask("QUIT THE GAME?", () => Player.Window.RequestQuit()), "YOUR PROGRESS IN THE GAME'S OWN SAVE FILE IS KEPT.");
                    break;
                case Video:
                    Add("DISPLAY", () => S.Fullscreen ? "FULLSCREEN" : "WINDOWED", d => { S.Fullscreen = !S.Fullscreen; Player.Window.ApplyVideoSettings(); }, null, "F11 OR ALT+ENTER ALSO TOGGLES FULLSCREEN.");
                    Add("WINDOW SIZE", () => S.WindowScale <= 0 ? "AUTO" : S.WindowScale + "X", d => { S.WindowScale = Wrap((S.WindowScale <= 0 ? 4 : S.WindowScale) - 2 + d, 6) + 2; Player.Window.ApplyVideoSettings(); }, null, "SIZE OF THE WINDOW IN WINDOWED MODE.");
                    Add("SCALING", () => S.Scale == ScaleMode.Integer ? "PIXEL PERFECT" : S.Scale == ScaleMode.Fit ? "FIT (SHARP)" : "STRETCH", d => S.Scale = (ScaleMode)Wrap((int)S.Scale + d, 3), null, "PIXEL PERFECT KEEPS EVERY PIXEL THE SAME SIZE.");
                    Add("PIXEL SHAPE", () => S.NtscAspect ? "CRT 8:7" : "SQUARE", d => S.NtscAspect = !S.NtscAspect, null, "8:7 MATCHES HOW THE SNES LOOKED ON A TV.");
                    Add("SCANLINES", () => S.Scanlines == 0 ? "OFF" : S.Scanlines <= 25 ? "LIGHT" : S.Scanlines <= 40 ? "MEDIUM" : "HEAVY", d => { int[] v = { 0, 25, 40, 60 }; int i = Array.IndexOf(v, S.Scanlines); if (i < 0) i = 0; S.Scanlines = v[Wrap(i + d, 4)]; }, null, "CRT-STYLE SCANLINES (NEEDS A LARGE WINDOW).");
                    Add("SHOW FPS", () => OnOff(S.ShowFps), d => S.ShowFps = !S.ShowFps, null, "");
                    Add("BACK", null, null, Pop, "");
                    break;
                case Timing:
                    Add("FRAME PACING", () => new[] { "AUTO", "VSYNC LOCK", "SMOOTH", "VRR" }[S.Pacing], d => { S.Pacing = Wrap(S.Pacing + d, 4); Player.Window.ConfigurePacing(); }, null, "AUTO LOCKS TO 60/120/240HZ DISPLAYS. PICK VRR IF YOU USE G-SYNC OR FREESYNC.");
                    Add("RUN-AHEAD", () => emu != null && !emu.CanRunAhead ? "N/A" : emu != null && emu.RunAheadAutoOff ? "OFF (SLOW PC)" : S.RunAhead ? "1 FRAME" : "OFF", d => { S.RunAhead = !S.RunAhead; if (emu != null) emu.RunAheadAutoOff = false; }, null, "HIDES ONE FRAME OF THE GAME'S INPUT LAG. USES MORE CPU.");
                    Add("LOW LATENCY", () => OnOff(S.LowLatency), d => S.LowLatency = !S.LowLatency, null, "STOPS THE DRIVER QUEUEING FRAMES. KEEP ON.");
                    Add("PAUSE WHEN INACTIVE", () => OnOff(S.PauseOnFocusLoss), d => S.PauseOnFocusLoss = !S.PauseOnFocusLoss, null, "PAUSE WHEN THE WINDOW LOSES FOCUS.");
                    Add("BACK", null, null, Pop, "");
                    break;
                case AudioP:
                    Add("VOLUME", () => S.Volume + "%", d => { S.Volume = Math.Max(0, Math.Min(100, S.Volume + d * 10)); Player.ApplyAudioSettings(false); }, null, "");
                    Add("AUDIO BUFFER", () => new[] { "LOW", "NORMAL", "SAFE" }[S.AudioLatency], d => { S.AudioLatency = Wrap(S.AudioLatency + d, 3); Player.ApplyAudioSettings(true); }, null, "LOW = LESS DELAY. PICK SAFE IF YOU HEAR CRACKLING.");
                    Add("BACK", null, null, Pop, "");
                    break;
                case Controls:
                    Add("KEYBOARD", null, null, () => Push(Keys), "REBIND KEYBOARD KEYS.");
                    Add("XBOX CONTROLLER", null, null, () => Push(Pad), "REBIND BUTTONS ON XBOX-STYLE (XINPUT) CONTROLLERS.");
                    Add("OTHER CONTROLLERS", null, null, () => Push(Joy), "REBIND PLAYSTATION, SWITCH PRO, 8BITDO AND USB PADS.");
                    Add("BUTTON LAYOUT", () => S.PadPreset == 0 ? "SNES POSITION" : "BY LABEL", d => { S.PadPreset = 1 - S.PadPreset; S.Bind.SetPadPreset(S.PadPreset); Player.ApplyInputSettings(); }, null, S.PadPreset == 0 ? "XBOX A = SNES B, X = Y, B = A, Y = X (SAME PLACES AS A SNES PAD)." : "XBOX A = SNES A, B = B, X = X, Y = Y.");
                    Add("STICK DEADZONE", () => S.Input.Deadzone + "%", d => S.Input.Deadzone = Math.Max(10, Math.Min(70, S.Input.Deadzone + d * 5)), null, "HOW FAR THE STICK MUST MOVE BEFORE IT COUNTS.");
                    Add("STICK UP/DOWN ANGLE", () => S.Input.StickCone + " DEG", d => S.Input.StickCone = Math.Max(20, Math.Min(65, S.Input.StickCone + d * 5)), null, "HOW STRAIGHT DOWN/UP THE STICK MUST POINT. SMALLER = NO ACCIDENTAL DUCKING.");
                    Add("USE LEFT STICK", () => OnOff(S.Input.UseStick), d => S.Input.UseStick = !S.Input.UseStick, null, "THE D-PAD ALWAYS WORKS.");
                    Add("LEFT+RIGHT", () => S.Input.Socd == 0 ? "LAST WINS" : "NEUTRAL", d => S.Input.Socd = 1 - S.Input.Socd, null, "WHAT HAPPENS WHEN LEFT AND RIGHT ARE HELD TOGETHER (KEYBOARDS).");
                    Add("OTHER CONTROLLERS", () => OnOff(S.Input.GenericPads), d => S.Input.GenericPads = !S.Input.GenericPads, null, "SUPPORT FOR NON-XBOX CONTROLLERS.");
                    Add("TEST CONTROLLERS", null, null, () => Push(Test), "SEE WHAT THE GAME RECEIVES FROM EACH DEVICE.");
                    Add("RESET CONTROLS", null, null, () => Ask("RESET ALL CONTROLS?", () => { S.Bind = Bindings.Defaults(); S.Input = new InputOptions { Vibration = false }; S.PadPreset = 0; Player.ApplyInputSettings(); Toast("CONTROLS RESET", 90); }), "RESTORE ALL DEFAULT BINDINGS.");
                    Add("BACK", null, null, Pop, "");
                    break;
                case Keys:
                    for (int a = 0; a < Bindings.ActCount; a++) { int act = a; Add(ButtonNames[a], () => KeyList(act), null, () => BeginCapture(0, act), "CONFIRM TO REBIND. DELETE CLEARS."); }
                    Add("BACK", null, null, Pop, "");
                    break;
                case Pad:
                    for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) { int act = a; Add(ButtonNames[a], () => PadList(act), null, () => BeginCapture(1, act), "D-PAD AND LEFT STICK ALWAYS MOVE."); }
                    Add("BACK", null, null, Pop, "");
                    break;
                case Joy:
                    for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) { int act = a; Add(ButtonNames[a], () => JoyList(act), null, () => BeginCapture(2, act), "D-PAD/HAT AND LEFT STICK ALWAYS MOVE."); }
                    Add("BACK", null, null, Pop, "");
                    break;
                case Test:
                    Add("BACK", null, null, Pop, "");
                    break;
                case CoreOpts:
                    if (emu != null)
                        foreach (var kv in emu.Core.OptionValues)
                        {
                            string key = kv.Key; var vals = kv.Value;
                            if (key == emu.NativeRunAheadKey) continue;   // driven by TIMING > RUN-AHEAD
                            string desc = emu.Core.OptionDesc[key];
                            Add(desc.ToUpperInvariant(), () => CoreValue(key), d =>
                            {
                                string cur = CoreValue(key);
                                int i = Array.FindIndex(vals, v => v.Equals(cur, StringComparison.OrdinalIgnoreCase));
                                string nv = vals[Wrap(i + d, vals.Length)];
                                emu.Core.SetOption(key, nv); S.Core[key] = nv;
                            }, null, key.ToUpperInvariant());
                        }
                    Add("BACK", null, null, Pop, "");
                    break;
            }
            if (sel >= items.Count) sel = items.Count - 1;
            if (sel < 0) sel = 0;
        }

        static string CoreValue(string key) { string v; return Player.Emu.Core.Options.TryGetValue(key, out v) ? v : ""; }

        void Add(string label, Func<string> value, Action<int> change, Action activate, string help)
        {
            items.Add(new Opt { Label = label, Value = value ?? (() => ""), Change = change, Activate = activate, Help = help ?? "" });
        }

        void Ask(string text, Action yes) { confirmText = text; confirmYes = yes; }
        void Push(int p) { pageStack.Push(page); selStack.Push(sel); page = p; sel = 0; scroll = 0; Build(); }
        void Pop()
        {
            S.Save(); Player.ApplyInputSettings();
            if (pageStack.Count == 0) { Close(); return; }
            page = pageStack.Pop(); sel = selStack.Pop(); scroll = 0; Build();
        }

        string KeyList(int a) { var l = new List<string>(); foreach (var k in S.Bind.Keys[a]) l.Add(InputSystem.KeyName(k)); return l.Count == 0 ? "-" : string.Join("/", l); }
        string PadList(int a) { var l = new List<string>(); foreach (var k in S.Bind.XBtn[a]) l.Add(InputSystem.XButtonName(k)); return l.Count == 0 ? "-" : string.Join("/", l); }
        string JoyList(int a) { var l = new List<string>(); foreach (var k in S.Bind.JoyBtn[a]) l.Add((k + 1).ToString()); return l.Count == 0 ? "-" : "BTN " + string.Join("/", l); }

        void BeginCapture(int kind, int act)
        {
            capKind = kind; capAct = act; capWait = 12;
            Player.Input.LastKeyPressed = -1; Player.Input.LastPadButton = -1;
            Player.Input.ClearKeyLatches();
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
            var inp = Player.Input;
            if (capWait > 0) { capWait--; inp.LastKeyPressed = -1; inp.LastPadButton = -1; return; }
            if (capKind == 0)
            {
                if (inp.ConsumeKey(0x2E)) { S.Bind.Keys[capAct] = new int[0]; capKind = -1; Player.ApplyInputSettings(); return; }
                int k = inp.LastKeyPressed;
                if (k > 0)
                {
                    for (int a = 0; a < Bindings.ActCount; a++) S.Bind.Keys[a] = Without(S.Bind.Keys[a], k);
                    S.Bind.Keys[capAct] = Prepend(S.Bind.Keys[capAct], k);
                    capKind = -1; Player.ApplyInputSettings(); inp.ReleaseAllKeys(); inp.ClearKeyLatches();
                    Toast("BOUND " + InputSystem.KeyName(k), 60);
                    swallow = 10;
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
                        Toast("BOUND " + InputSystem.XButtonName(b), 60);
                    }
                    else
                    {
                        for (int a = (int)Act.Jump; a < Bindings.ActCount; a++) S.Bind.JoyBtn[a] = Without(S.Bind.JoyBtn[a], b);
                        S.Bind.JoyBtn[capAct] = Prepend(S.Bind.JoyBtn[capAct], b);
                        Toast("BOUND BUTTON " + (b + 1), 60);
                    }
                    capKind = -1; Player.ApplyInputSettings();
                    swallow = 20;
                }
            }
        }

        // ---------------------------------------------------------------- tick (60 Hz)
        /// <summary>Advances toasts; when the menu is open, handles navigation with the merged pad state.</summary>
        public void Tick(PadState any)
        {
            t++;
            if (toastT > 0) toastT--;
            if (!MenuOpen) return;
            var inp = Player.Input;
            // arrow keys / Enter / Backspace always drive the menu, whatever the bindings are
            int raw = (inp.IsKeyDown(0x26) ? Btn.Up : 0) | (inp.IsKeyDown(0x28) ? Btn.Down : 0) | (inp.IsKeyDown(0x25) ? Btn.Left : 0) | (inp.IsKeyDown(0x27) ? Btn.Right : 0);
            var p = any;
            p.Held |= raw; p.Pressed |= raw & ~rawPrev; rawPrev = raw;
            if (capKind >= 0) { CaptureTick(); return; }
            bool enter = inp.ConsumeKey(0x0D) | inp.ConsumeKey(0x20);
            bool back = inp.ConsumeKey(0x08);
            if (swallow > 0) { swallow--; return; }
            bool ok = enter || p.P(Btn.A) || p.P(Btn.Start);
            bool cancel = back || p.P(Btn.SnesA);
            if (confirmText != null)
            {
                if (ok) { var y = confirmYes; confirmText = null; y(); }
                else if (cancel) confirmText = null;
                return;
            }
            if (cancel) { Pop(); return; }
            if (items.Count == 0) return;
            int v = nav.Vertical(p);
            if (v != 0) sel = Wrap(sel + v, items.Count);
            var it = items[sel];
            int h = nav.Horizontal(p);
            if (h != 0 && it.Change != null) { it.Change(h); Build(); }
            if (ok)
            {
                if (it.Activate != null) it.Activate();
                else if (it.Change != null) { it.Change(1); Build(); }
            }
            if (sel < scroll) scroll = sel;
            if (sel >= scroll + rows) scroll = sel - rows + 1;
        }

        // ---------------------------------------------------------------- render
        /// <summary>Draws into ppu (cleared to Transparent) for a logical screen height h. Returns false if nothing to draw.</summary>
        public bool Render(Ppu ppu, int h, bool showFps, string fpsText)
        {
            bool any = MenuOpen || toastT > 0 || showFps;
            if (!any) return false;
            ppu.ResetClip();
            ppu.Clear(Ui.Transparent);
            if (MenuOpen) RenderMenu(ppu, h);
            if (showFps && !MenuOpen) { Ui.Strip(ppu, 0, 0, fpsText.Length * 8 + 6, 11); Ui.Txt(ppu, fpsText, 3, 2, 0x2A); }
            if (toastT > 0)
            {
                int w = toast.Length * 8 + 12, y = h - 20;
                Ui.Strip(ppu, (256 - w) / 2, y - 2, w, 12);
                Ui.TxtC(ppu, toast, y, 0x30);
            }
            return true;
        }

        void RenderMenu(Ppu ppu, int h)
        {
            Ui.Window(ppu, 8, 6, 240, h - 12);
            string title = Titles[page];
            Ui.Txt(ppu, title, (256 - title.Length * 8) / 2, 15, 0x28);
            int top = 30, helpY = h - 42;
            rows = Math.Max(4, (helpY - 12 - top) / 12);
            if (page == Test) { RenderTest(ppu, h); }
            else
            {
                if (sel >= scroll + rows) scroll = sel - rows + 1;
                for (int i = scroll; i < items.Count && i < scroll + rows; i++)
                {
                    int y = top + (i - scroll) * 12;
                    var it = items[i];
                    bool on = i == sel;
                    string val = it.Value();
                    string label = it.Label;
                    int maxLabel = 26 - (val.Length > 0 ? val.Length + 1 : 0);
                    if (label.Length > maxLabel) label = label.Substring(0, Math.Max(1, maxLabel));
                    Ui.Txt(ppu, label, 28, y, on ? 0x30 : 0x10);
                    if (val.Length > 0) Ui.Txt(ppu, val, 236 - val.Length * 8, y, on ? 0x28 : 0x10);
                    if (on) Ui.Cursor(ppu, 16, y, t);
                }
                if (scroll > 0) Ui.Txt(ppu, "...", 220, top - 9, 0x10);
                if (scroll + rows < items.Count) Ui.Txt(ppu, "...", 220, top + rows * 12 - 4, 0x10);
                var cur = items[Math.Min(sel, items.Count - 1)];
                if (cur.Help.Length > 0) WrapText(ppu, cur.Help, 20, helpY, 27, 0x21);
                string info = InfoLine();
                if (info.Length > 0) Ui.Txt(ppu, info.Length > 27 ? info.Substring(0, 27) : info, 20, helpY - 11, 0x00);
            }
            if (page == Test) { int y = h - 30; Ui.Cursor(ppu, 16, y, t); Ui.Txt(ppu, "BACK", 28, y, 0x30); }
            if (capKind >= 0)
            {
                Ui.Window(ppu, 24, h / 2 - 32, 208, 64);
                string what = capKind == 0 ? "PRESS A KEY" : capKind == 1 ? "PRESS A CONTROLLER BUTTON" : "PRESS A BUTTON";
                Ui.TxtC(ppu, what, h / 2 - 18, 0x30);
                Ui.TxtC(ppu, "FOR " + ButtonNames[capAct], h / 2 - 4, 0x28);
                Ui.TxtC(ppu, "ESC CANCEL  DEL CLEAR", h / 2 + 14, 0x10);
            }
            if (confirmText != null)
            {
                Ui.Window(ppu, 24, h / 2 - 24, 208, 48);
                Ui.TxtC(ppu, confirmText, h / 2 - 12, 0x30);
                Ui.TxtC(ppu, "CONFIRM: YES   BACK: NO", h / 2 + 4, 0x10);
            }
        }

        string InfoLine()
        {
            var emu = Player.Emu;
            if (page == Timing) return Player.RefreshHz + "HZ " + Player.PacingInfo + (emu != null ? " " + emu.EmuMs.ToString("0.0") + "MS" : "");
            if (page == AudioP && emu != null)
                return ((int)Math.Round(emu.Core.SampleRate)) + "HZ>48K X" + emu.Audio.Ratio.ToString("0.000") + " UNDERRUNS " + emu.Audio.Underruns;
            if (page == Main && emu != null) return "SAVE: " + (emu.SramSize > 0 ? (emu.SramSize / 1024) + "KB SRAM" : "NONE");
            return "";
        }

        static void WrapText(Ppu ppu, string s, int x, int y, int cols, int color)
        {
            var words = s.Split(' ');
            string line = "";
            int n = 0;
            foreach (var w in words)
            {
                if ((line + " " + w).Trim().Length > cols) { Ui.Txt(ppu, line.Trim(), x, y, color); y += 9; line = ""; if (++n == 3) return; }
                line += " " + w;
            }
            if (line.Trim().Length > 0) Ui.Txt(ppu, line.Trim(), x, y, color);
        }

        void RenderTest(Ppu ppu, int h)
        {
            var list = Player.Input.ConnectedControllers();
            int y = 30;
            Ui.Txt(ppu, "KEYBOARD  = PLAYER 1", 20, y, 0x10); y += 12;
            if (list.Count == 0) { Ui.Txt(ppu, "NO CONTROLLERS FOUND", 20, y, 0x16); y += 12; Ui.Txt(ppu, "PLUG ONE IN AT ANY TIME.", 20, y, 0x10); }
            for (int i = 0; i < list.Count && i < 3; i++)
            {
                var c = list[i];
                string name = (c.Kind == DeviceKind.XInput ? "XBOX: " : "PAD: ") + c.Name.ToUpperInvariant();
                if (name.Length > 27) name = name.Substring(0, 27);
                Ui.Txt(ppu, name, 20, y, 0x30); y += 10;
                Ui.Txt(ppu, "P" + (i + 1), 20, y, i < 2 ? 0x2A : 0x10);
                DrawPad(ppu, 44, y - 2, c.Dirs | c.Actions);
                y += 12;
                string raw = "BTN";
                if (c.Kind == DeviceKind.Generic) { for (int b = 0; b < 16; b++) if ((c.RawButtons & (1 << b)) != 0) raw += " " + (b + 1); }
                else foreach (int m in new[] { Bindings.XA, Bindings.XB, Bindings.XX, Bindings.XY, Bindings.XLB, Bindings.XRB, Bindings.XLT, Bindings.XRT, Bindings.XStart, Bindings.XBack }) if ((c.RawButtons & m) != 0) raw += " " + InputSystem.XButtonName(m);
                Ui.Txt(ppu, raw.Length > 27 ? raw.Substring(0, 27) : raw, 20, y, 0x21);
                y += 14;
                if (y > h - 60) break;
            }
        }

        static void DrawPad(Ppu ppu, int x, int y, int held)
        {
            ppu.FillRect(x, y, 180, 12, 0x0F);
            string[] n = { "UP", "DN", "<", ">", "B", "Y", "ST", "SE", "A", "X", "L", "R" };
            int cx = x + 2;
            for (int i = 0; i < 12; i++)
            {
                Ui.Txt(ppu, n[i], cx, y + 2, (held & (1 << i)) != 0 ? 0x28 : 0x00);
                cx += n[i].Length * 8 + 4;
            }
        }
    }

    /// <summary>Menu key-repeat (port of the original game's MenuNav).</summary>
    public sealed class MenuNav
    {
        int heldV, dirV, heldH, dirH;
        public int Vertical(PadState p)
        {
            int d = p.H(Btn.Up) ? -1 : p.H(Btn.Down) ? 1 : 0;
            return Repeat(d, p.P(Btn.Up) || p.P(Btn.Down), ref heldV, ref dirV);
        }
        public int Horizontal(PadState p)
        {
            int d = p.H(Btn.Left) ? -1 : p.H(Btn.Right) ? 1 : 0;
            return Repeat(d, p.P(Btn.Left) || p.P(Btn.Right), ref heldH, ref dirH);
        }
        static int Repeat(int d, bool pressed, ref int held, ref int dirHeld)
        {
            if (d == 0) { held = 0; dirHeld = 0; return 0; }
            if (pressed || d != dirHeld) { dirHeld = d; held = 0; return d; }
            held++;
            if (held > 18 && (held - 18) % 5 == 0) return d;
            return 0;
        }
    }
}
