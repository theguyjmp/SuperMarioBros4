using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using SMB4.Engine;
using N = SMB4.Platform.Native;

namespace SMB4.Platform
{
    /// <summary>
    /// The game window and main loop. Pacing modes (docs/02 §4.2):
    ///  Lockstep  — display is a multiple of 60 Hz: swap interval N, exactly one tick per present.
    ///  Accumulate— other refresh rates: vsync every refresh, ticks run from the wall clock.
    ///  Timer     — G-Sync/FreeSync: no vsync, a high-resolution timer paces 60 Hz presents.
    /// Input is polled right before the ticks that consume it; frames are never queued ahead.
    /// </summary>
    public sealed class GameWindow : Form
    {
        readonly IGame game;
        GLPresenter gl;
        readonly Ppu ppu = new Ppu();


        bool fullscreen, quit, cursorHidden, lockstepFailed;
        Rectangle windowedBounds;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastFrame, nextTickAt;
        double acc;
        IntPtr hTimer;
        enum Mode { Lockstep, Accumulate, Timer }
        Mode mode = Mode.Accumulate;
        int lockN = 1, frameCounter, lockSamples;
        double lockSum;
        readonly double[] dts = new double[60]; int dtIdx;

        const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_SYSCHAR = 0x106,
                  WM_ACTIVATEAPP = 0x1C, WM_SYSCOMMAND = 0x112, WM_ERASEBKGND = 0x14, SC_KEYMENU = 0xF100,
                  SC_SCREENSAVE = 0xF140, SC_MONITORPOWER = 0xF170;

        public GameWindow(IGame game)
        {
            this.game = game;
            Text = "Super Mario Bros. 4";
            BackColor = Color.Black;
            StartPosition = FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            int scale = Host.Settings.WindowScale;
            var wa = Screen.PrimaryScreen.WorkingArea;
            int maxScale = Math.Max(1, Math.Min((wa.Width - 40) / 256, (wa.Height - 80) / 240));
            if (scale <= 0 || scale > maxScale) scale = Math.Max(2, Math.Min(maxScale, 5));
            ClientSize = new Size(256 * scale, 240 * scale);
            MinimumSize = new Size(256 + 16, 240 + 39);
            hTimer = N.CreateWaitableTimerExW(IntPtr.Zero, null, 0x2 /*HIGH_RESOLUTION*/, 0x1F0003);
            if (hTimer == IntPtr.Zero) hTimer = N.CreateWaitableTimerExW(IntPtr.Zero, null, 0, 0x1F0003);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20; /* CS_OWNDC for OpenGL */ return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            gl = new GLPresenter();
            if (!gl.Init(Handle)) throw new InvalidOperationException("Could not create an OpenGL context. Please update your graphics driver.");
            Host.GpuInfo = gl.Renderer + " / OpenGL " + gl.Version;
            ConfigurePacing();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Host.Settings.Fullscreen) SetFullscreen(true);
            N.SetThreadExecutionState(0x80000000 | 0x00000002); // ES_CONTINUOUS | ES_DISPLAY_REQUIRED
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void RequestQuit() { quit = true; }

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
            int hz = QueryRefresh();
            Host.RefreshHz = hz;
            int p = Host.Settings.Pacing;
            double ratio = hz / 60.0;
            int n = (int)Math.Round(ratio);
            bool multiple = n >= 1 && n <= 4 && Math.Abs(ratio - n) < 0.03 * n;
            if (p == 3 || !gl.HasSwapControl)
            {
                mode = Mode.Timer; gl.SetSwapInterval(0);
                nextTickAt = clock.ElapsedTicks;
                Host.PacingInfo = "TIMER 60HZ (VRR)";
            }
            else if (p == 2 || !multiple || lockstepFailed)
            {
                mode = Mode.Accumulate; gl.SetSwapInterval(1);
                Host.PacingInfo = "VSYNC " + hz + "HZ + CLOCK";
            }
            else
            {
                mode = Mode.Lockstep; lockN = n; gl.SetSwapInterval(n);
                lockSamples = 0; lockSum = 0;
                Host.PacingInfo = "LOCKED " + hz + "HZ / " + n;
            }
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
            if (frameCounter % 180 == 0 && QueryRefresh() != Host.RefreshHz) ConfigurePacing();

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
                long period = Stopwatch.Frequency / 60;
                WaitUntil(nextTickAt);
                ticks = 1;
                nextTickAt += period;
                long after = clock.ElapsedTicks;
                if (after - nextTickAt > period * 4) nextTickAt = after + period;
            }
            else
            {
                acc += dt * 60.0;
                if (acc > 6) acc = 1;
                ticks = (int)acc;
                acc -= ticks;
            }

            Host.Input.PollDevices();
            for (int i = 0; i < ticks; i++)
            {
                var p1 = Host.Input.Sample(0, true);
                var p2 = Host.Input.Sample(1, true);
                game.Tick(p1, p2);
                Host.TickCount++;
            }
            if (game.WantsQuit) quit = true;

            double sum = 0; for (int i = 0; i < dts.Length; i++) sum += dts[i];
            Host.MeasuredFps = sum > 0 ? dts.Length / sum : 0;

            game.Render(ppu);
            var lut = NesPalette.Lut(ppu.Fade, ppu.Grayscale);
            gl.Convert(ppu.Px, lut);
            var s = Host.Settings;
            gl.Present(ClientSize.Width, ClientSize.Height, s.Scale, s.NtscAspect, s.Scanlines, s.LowLatency);
        }

        // ------------------------------------------------------------------ window management
        public void ApplyVideoSettings()
        {
            SetFullscreen(Host.Settings.Fullscreen);
            if (!fullscreen && Host.Settings.WindowScale > 0)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                int k = Host.Settings.WindowScale;
                var size = new Size(256 * k, 240 * k);
                if (size.Width <= wa.Width && size.Height <= wa.Height && ClientSize != size)
                {
                    ClientSize = size;
                    Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
                }
            }
            ConfigurePacing();
        }

        public bool IsFullscreen { get { return fullscreen; } }

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
            Host.Settings.Fullscreen = fs;
            ConfigurePacing();
        }

        public void ToggleFullscreen() { SetFullscreen(!fullscreen); Host.Settings.Save(); }

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
                    if (vk == 0x7A) { if (!repeat) ToggleFullscreen(); return; }
                    if (vk == 0x7B) { if (!repeat) Host.SaveScreenshot(gl.LastFrame); return; }
                    Host.Input.KeyDown(vk);
                    return;
                }
                case WM_KEYUP:
                case WM_SYSKEYUP:
                    Host.Input.KeyUp(TranslateVk(m.WParam.ToInt32(), m.LParam.ToInt64()));
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
                    if (!active) Host.Input.ReleaseAllKeys();
                    game.OnFocusChanged(active);
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
