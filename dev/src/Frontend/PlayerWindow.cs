using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SMB4.Engine;
using SMB4.Platform;
using N = SMB4.Platform.Native;

namespace SMB4.Frontend
{
    /// <summary>
    /// The player window and main loop. Pacing modes (docs/02 §4.2), same as the original game:
    ///  Lockstep  — display is a multiple of 60 Hz: swap interval N, exactly one emulated frame per present.
    ///  Accumulate— other refresh rates: vsync every refresh, frames run from the wall clock at the core's rate.
    ///  Timer     — G-Sync/FreeSync: no vsync, a high-resolution timer paces presents at the core's rate.
    /// Input is polled immediately before the retro_run that consumes it; frames are never queued ahead.
    /// </summary>
    public sealed class PlayerWindow : Form
    {
        GLPresenter gl;
        readonly Overlay overlay = new Overlay();
        readonly Ppu ovl = new Ppu();
        int[] display = new int[512 * 480];

        bool fullscreen, quit, cursorHidden, lockstepFailed, focusPaused, userPaused;
        Rectangle windowedBounds;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastFrame, nextTickAt;
        double acc;
        IntPtr hTimer;
        enum Mode { Lockstep, Accumulate, Timer }
        Mode mode = Mode.Accumulate;
        int lockN = 1, frameCounter, lockSamples, comboT;
        double lockSum;
        readonly double[] dts = new double[60]; int dtIdx;

        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_SYSCHAR = 0x106,
                  WM_ACTIVATEAPP = 0x1C, WM_SYSCOMMAND = 0x112, WM_ERASEBKGND = 0x14, SC_KEYMENU = 0xF100,
                  SC_SCREENSAVE = 0xF140, SC_MONITORPOWER = 0xF170;

        static PlayerSettings S { get { return Player.Settings; } }
        Emulator Emu { get { return Player.Emu; } }
        double CoreFps { get { return Emu != null ? Emu.Core.Fps : 60.0; } }

        public PlayerWindow()
        {
            Text = Player.Config.Title;
            BackColor = Color.Black;
            StartPosition = FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var wa = Screen.PrimaryScreen.WorkingArea;
            int scale = S.WindowScale;
            int maxScale = Math.Max(1, Math.Min((wa.Width - 40) / LogicalW(), (wa.Height - 80) / 224));
            if (scale <= 0 || scale > maxScale) scale = Math.Max(2, Math.Min(maxScale, 5));
            ClientSize = new Size(LogicalW() * scale, 224 * scale);
            MinimumSize = new Size(256 + 16, 224 + 39);
            hTimer = N.CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /*HIGH_RESOLUTION*/, 0x1F0003);
            if (hTimer == IntPtr.Zero) hTimer = N.CreateWaitableTimerExW(IntPtr.Zero, null, 0, 0x1F0003);
        }

        static int LogicalW() { return S.NtscAspect ? 292 : 256; }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20; /* CS_OWNDC for OpenGL */ return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            gl = new GLPresenter();
            if (!gl.Init(Handle)) throw new InvalidOperationException("Could not create an OpenGL context. Please update your graphics driver.");
            Player.GpuInfo = gl.Renderer + " / OpenGL " + gl.Version;
            Player.Log("gpu: " + Player.GpuInfo);
            ConfigurePacing();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (S.Fullscreen) SetFullscreen(true);
            N.SetThreadExecutionState(0x80000000 | 0x00000002); // ES_CONTINUOUS | ES_DISPLAY_REQUIRED
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void RequestQuit() { quit = true; }
        public void Toast(string s) { overlay.Toast(s); }

        public void Run()
        {
            Application.Idle += OnIdle;
            Application.Run(this);
        }

        void OnIdle(object sender, EventArgs e)
        {
            N.MSG msg;
            while (!N.PeekMessage(out msg, IntPtr.Zero, 0, 0, 0))
            {
                if (quit) { Close(); return; }
                Frame();
            }
        }

        // ------------------------------------------------------------------ pacing
        int QueryRefresh()
        {
            try
            {
                var dm = new N.DEVMODE();
                dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(N.DEVMODE));
                string dev = Screen.FromControl(this).DeviceName;
                if (N.EnumDisplaySettings(dev, -1, ref dm) && dm.dmDisplayFrequency > 1) return dm.dmDisplayFrequency;
            }
            catch { }
            return 60;
        }

        public void ConfigurePacing()
        {
            if (gl == null) return;
            int hz = QueryRefresh();
            Player.RefreshHz = hz;
            int p = S.Pacing;
            double ratio = hz / 60.0;
            int n = (int)Math.Round(ratio);
            bool multiple = n >= 1 && n <= 4 && Math.Abs(ratio - n) < 0.03 * n;
            if (p == 3 || !gl.HasSwapControl)
            {
                mode = Mode.Timer; gl.SetSwapInterval(0);
                nextTickAt = clock.ElapsedTicks;
                Player.PacingInfo = "TIMER (VRR)";
            }
            else if (p == 2 || !multiple || lockstepFailed)
            {
                mode = Mode.Accumulate; gl.SetSwapInterval(1);
                Player.PacingInfo = "VSYNC+CLOCK";
            }
            else
            {
                mode = Mode.Lockstep; lockN = n; gl.SetSwapInterval(n);
                lockSamples = 0; lockSum = 0;
                Player.PacingInfo = "LOCKED /" + n;
            }
            Player.Log("pacing: " + hz + " Hz → " + Player.PacingInfo);
        }

        void WaitUntil(long target)
        {
            long now = clock.ElapsedTicks;
            long remain = target - now;
            if (remain <= 0) return;
            double ms = remain * 1000.0 / Stopwatch.Frequency;
            if (ms > 1.5 && hTimer != IntPtr.Zero)
            {
                long due = -(long)((ms - 1.0) * 10000.0);   // relative, 100 ns units
                N.SetWaitableTimer(hTimer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
                N.WaitForSingleObject(hTimer, 50);
            }
            while (clock.ElapsedTicks < target) { System.Threading.Thread.SpinWait(50); }
        }

        void Frame()
        {
            long now = clock.ElapsedTicks;
            double dt = (now - lastFrame) / (double)Stopwatch.Frequency;
            lastFrame = now;
            dts[dtIdx = (dtIdx + 1) % dts.Length] = dt;
            frameCounter++;

            if (WindowState == FormWindowState.Minimized) { System.Threading.Thread.Sleep(15); }
            if (frameCounter % 180 == 0 && QueryRefresh() != Player.RefreshHz) ConfigurePacing();

            double fps = CoreFps;
            int ticks;
            if (mode == Mode.Lockstep)
            {
                ticks = 1;
                // Verify the driver really honours the swap interval; otherwise fall back to clock pacing.
                if (frameCounter > 30 && lockSamples < 120) { lockSum += dt; lockSamples++; }
                if (lockSamples == 120)
                {
                    double avg = lockSum / 120.0; lockSamples++;
                    if (avg < 0.0150 || avg > 0.0185) { lockstepFailed = true; ConfigurePacing(); }
                }
            }
            else if (mode == Mode.Timer)
            {
                long period = (long)(Stopwatch.Frequency / fps);
                WaitUntil(nextTickAt);
                ticks = 1;
                nextTickAt += period;
                long after = clock.ElapsedTicks;
                if (after - nextTickAt > period * 4) nextTickAt = after + period;
            }
            else
            {
                acc += dt * fps;
                if (acc > 6) acc = 1;
                ticks = (int)acc;
                acc -= ticks;
            }

            double sum = 0; for (int i = 0; i < dts.Length; i++) sum += dts[i];
            Player.MeasuredFps = sum > 0 ? dts.Length / sum : 0;
            // emulated seconds per wall second, for the audio resampler (DRC corrects the rest)
            double speed = 1.0;
            if (mode == Mode.Lockstep)
            {
                double tickRate = frameCounter > 90 && Player.MeasuredFps > 1 ? Player.MeasuredFps : Player.RefreshHz / (double)lockN;
                speed = Math.Max(0.95, Math.Min(1.05, tickRate / fps));
            }

            var input = Player.Input;
            input.PollDevices();
            if (input.ControllerLost)
            {
                input.ControllerLost = false;
                if (frameCounter > 60) { overlay.Open(); overlay.Toast("CONTROLLER DISCONNECTED", 180); }
            }
            bool paused = focusPaused || userPaused;
            for (int i = 0; i < ticks; i++)
            {
                var p1 = input.Sample(0, true);
                var p2 = input.Sample(1, true);
                int both = Btn.Select | Btn.Start;
                if (((p1.Held & both) == both || (p2.Held & both) == both) && !overlay.MenuOpen) { if (++comboT == 30) overlay.Open(); }
                else comboT = 0;
                overlay.Tick(p1 | p2);
                if (!overlay.MenuOpen && !paused && Emu != null) Emu.RunFrame(p1.Held, p2.Held, S.RunAhead, speed);
            }
            if (paused && !overlay.MenuOpen) overlay.Toast(focusPaused ? "PAUSED - CLICK TO RESUME" : "PAUSED", 2);
            if (quit) return;
            Compose();
            gl.Present(ClientSize.Width, ClientSize.Height, S.Scale, S.NtscAspect, S.Scanlines, S.LowLatency);
        }

        // ------------------------------------------------------------------ composition
        void Compose()
        {
            var core = Emu != null ? Emu.Core : null;
            int fw = 256, fh = 224;
            int[] src = null;
            if (core != null && core.Frame != null && core.FrameW > 0) { src = core.Frame; fw = core.FrameW; fh = core.FrameH; }
            int lw = fw > 300 ? fw / 2 : fw, lh = fh > 300 ? fh / 2 : fh;
            string fpsText = Player.MeasuredFps.ToString("0.0") + " FPS " + (Emu != null ? Emu.EmuMs.ToString("0.0") + "MS" : "");
            bool ov = overlay.Render(ovl, Math.Min(lh, Ppu.H), S.ShowFps, fpsText);
            if (display.Length < fw * fh) display = new int[fw * fh];
            if (!ov && src != null) { gl.SetFrame(src, fw, fh, lw, lh); return; }
            var lut = NesPalette.Lut(0, false);
            bool dim = overlay.MenuOpen;
            var px = ovl.Px;
            for (int y = 0; y < fh; y++)
            {
                int oy = Math.Min(Ppu.H - 1, y * lh / fh);
                int orow = oy * Ppu.W, row = y * fw;
                for (int x = 0; x < fw; x++)
                {
                    int ox = x * lw / fw; if (ox > 255) ox = 255;
                    int idx = px[orow + ox];
                    if (idx == Ui.Transparent || idx >= lut.Length)
                    {
                        int c = src != null ? src[row + x] : unchecked((int)0xFF000000);
                        display[row + x] = dim ? (int)(((uint)c >> 1 & 0x7F7F7Fu) | 0xFF000000u) : c;
                    }
                    else display[row + x] = lut[idx];
                }
            }
            gl.SetFrame(display, fw, fh, lw, lh);
        }

        // ------------------------------------------------------------------ actions
        public void SaveState()
        {
            if (Emu == null) return;
            if (!Emu.CanSerialize) { overlay.Toast("THIS CORE CAN'T SAVE STATES"); return; }
            overlay.Toast(Emu.SaveState(1) ? "STATE SAVED" : "STATE SAVE FAILED");
        }

        public bool LoadState()
        {
            if (Emu == null) return false;
            if (!File.Exists(Emu.StatePath(1))) { overlay.Toast("NO SAVED STATE YET (F5 SAVES)"); return false; }
            bool ok = Emu.LoadState(1);
            overlay.Toast(ok ? "STATE LOADED" : "STATE LOAD FAILED");
            return ok;
        }

        void Screenshot()
        {
            var core = Emu != null ? Emu.Core : null;
            if (core == null || core.Frame == null) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Player.Config.ScreenshotFolder);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, Player.Config.ScreenshotPrefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                int fw = core.FrameW, fh = core.FrameH, lw = fw > 300 ? fw / 2 : fw, lh = fh > 300 ? fh / 2 : fh;
                Png.Write(core.Frame, fw, fh, lw * 3, lh * 3, path);
                overlay.Toast("SCREENSHOT SAVED");
            }
            catch { overlay.Toast("SCREENSHOT FAILED"); }
        }

        // ------------------------------------------------------------------ window management
        public void ApplyVideoSettings()
        {
            SetFullscreen(S.Fullscreen);
            if (!fullscreen && S.WindowScale > 0)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                int k = S.WindowScale;
                var size = new Size(LogicalW() * k, 224 * k);
                if (size.Width <= wa.Width && size.Height <= wa.Height && ClientSize != size)
                {
                    ClientSize = size;
                    Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
                }
            }
            ConfigurePacing();
        }

        public void SetFullscreen(bool fs)
        {
            if (fs == fullscreen) return;
            if (fs)
            {
                windowedBounds = Bounds;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
                if (!cursorHidden) { Cursor.Hide(); cursorHidden = true; }
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                if (windowedBounds.Width > 0) Bounds = windowedBounds;
                if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
            }
            fullscreen = fs;
            S.Fullscreen = fs;
            ConfigurePacing();
        }

        public void ToggleFullscreen() { SetFullscreen(!fullscreen); S.Save(); }

        static int TranslateVk(int vk, long lParam)
        {
            int scan = (int)((lParam >> 16) & 0xFF);
            bool ext = ((lParam >> 24) & 1) != 0;
            if (vk == 0x10) return scan == 0x36 ? 0xA1 : 0xA0;
            if (vk == 0x11) return ext ? 0xA3 : 0xA2;
            if (vk == 0x12) return ext ? 0xA5 : 0xA4;
            return vk & 0xFF;
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                {
                    long lp = m.LParam.ToInt64();
                    int vk = TranslateVk(m.WParam.ToInt32(), lp);
                    bool alt = (lp & (1L << 29)) != 0;
                    bool repeat = (lp & (1L << 30)) != 0;
                    if (vk == 0x0D && alt) { if (!repeat) ToggleFullscreen(); return; }
                    if (vk == 0x73 && alt) { quit = true; return; }
                    if (!repeat)
                    {
                        switch (vk)
                        {
                            case 0x7A: ToggleFullscreen(); return;         // F11
                            case 0x7B: Screenshot(); return;               // F12
                            case 0x74: SaveState(); return;                // F5
                            case 0x78: LoadState(); return;                // F9
                            case 0x1B: overlay.OnEscape(); return;         // Esc
                            case 0x13: userPaused = !userPaused; return;   // Pause/Break
                        }
                    }
                    else if (vk == 0x7A || vk == 0x7B || vk == 0x74 || vk == 0x78 || vk == 0x1B || vk == 0x13) return;
                    Player.Input.KeyDown(vk);
                    return;
                }
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    Player.Input.KeyUp(TranslateVk(m.WParam.ToInt32(), m.LParam.ToInt64()));
                    return;
                case WM_SYSCHAR:
                    return;
                case WM_SYSCOMMAND:
                {
                    int cmd = m.WParam.ToInt32() & 0xFFF0;
                    if (cmd == SC_KEYMENU || cmd == SC_SCREENSAVE || cmd == SC_MONITORPOWER) return;
                    break;
                }
                case WM_ACTIVATEAPP:
                {
                    bool active = m.WParam != IntPtr.Zero;
                    if (!active) { Player.Input.ReleaseAllKeys(); if (S.PauseOnFocusLoss) focusPaused = true; if (Emu != null) Emu.FlushSram(false); }
                    else focusPaused = false;
                    break;
                }
                case WM_ERASEBKGND:
                    m.Result = (IntPtr)1;
                    return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
            if (gl != null) gl.Dispose();
            if (hTimer != IntPtr.Zero) N.CloseHandle(hTimer);
            N.SetThreadExecutionState(0x80000000);
        }
    }
}
