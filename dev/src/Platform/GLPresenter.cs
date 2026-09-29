using System;
using System.Runtime.InteropServices;
using N = SMB4.Platform.Native;

namespace SMB4.Platform
{
    public enum ScaleMode { Integer = 0, Fit = 1, Stretch = 2 }

    /// <summary>
    /// Presents an ARGB frame of any size through OpenGL 1.1: one texture upload, one quad. The frame has a
    /// "logical" size used for aspect and integer scaling (e.g. a 512x448 SNES hi-res/interlaced frame is logically
    /// 256x224). Integer scaling by default; "Fit" uses an integer prescale + linear filtering (sharp-bilinear) so
    /// pixels stay crisp at non-integer sizes (including the 8:7 pixel shape).
    /// </summary>
    public sealed unsafe class GLPresenter : IDisposable
    {
        public const int W = 256, H = 240;              // legacy fixed frame (Convert)
        IntPtr hwnd, hdc, hglrc;
        uint baseTex, bigTex;
        int baseTexSize;
        int[] frame = new int[W * H];                   // ARGB (little-endian BGRA)
        int fw = W, fh = H, lw = W, lh = H;             // frame size and logical size
        int[] big;                                      // prescaled frame for sharp fit
        int bigFactor, bigW, bigH, bigTexSize;
        N.SwapIntervalFn swapInterval;
        int currentInterval = -1;
        public string Renderer = "", Version = "";
        public bool HasSwapControl { get { return swapInterval != null; } }

        public bool Init(IntPtr windowHandle)
        {
            hwnd = windowHandle;
            hdc = N.GetDC(hwnd);
            var pfd = new N.PIXELFORMATDESCRIPTOR();
            pfd.nSize = (ushort)Marshal.SizeOf(typeof(N.PIXELFORMATDESCRIPTOR));
            pfd.nVersion = 1;
            pfd.dwFlags = N.PFD_DRAW_TO_WINDOW | N.PFD_SUPPORT_OPENGL | N.PFD_DOUBLEBUFFER;
            pfd.iPixelType = 0; // RGBA
            pfd.cColorBits = 32;
            pfd.cAlphaBits = 8;
            int fmt = N.ChoosePixelFormat(hdc, ref pfd);
            if (fmt == 0 || !N.SetPixelFormat(hdc, fmt, ref pfd)) return false;
            hglrc = N.wglCreateContext(hdc);
            if (hglrc == IntPtr.Zero || !N.wglMakeCurrent(hdc, hglrc)) return false;

            Renderer = Marshal.PtrToStringAnsi(N.glGetString(N.GL_RENDERER)) ?? "";
            Version = Marshal.PtrToStringAnsi(N.glGetString(N.GL_VERSION)) ?? "";
            IntPtr p = N.wglGetProcAddress("wglSwapIntervalEXT");
            if (p != IntPtr.Zero && p.ToInt64() > 3 && p.ToInt64() != -1)
                swapInterval = (N.SwapIntervalFn)Marshal.GetDelegateForFunctionPointer(p, typeof(N.SwapIntervalFn));

            N.glDisable(N.GL_DEPTH_TEST);
            N.glDisable(N.GL_DITHER);
            N.glPixelStorei(N.GL_UNPACK_ALIGNMENT, 4);
            var t = new uint[2];
            N.glGenTextures(2, t);
            baseTex = t[0]; bigTex = t[1];
            baseTexSize = 512;
            CreateTexture(baseTex, baseTexSize, baseTexSize);
            return true;
        }

        static void CreateTexture(uint tex, int w, int h)
        {
            N.glBindTexture(N.GL_TEXTURE_2D, tex);
            N.glTexParameteri(N.GL_TEXTURE_2D, N.GL_TEXTURE_MIN_FILTER, (int)N.GL_NEAREST);
            N.glTexParameteri(N.GL_TEXTURE_2D, N.GL_TEXTURE_MAG_FILTER, (int)N.GL_NEAREST);
            N.glTexParameteri(N.GL_TEXTURE_2D, N.GL_TEXTURE_WRAP_S, (int)N.GL_CLAMP_TO_EDGE);
            N.glTexParameteri(N.GL_TEXTURE_2D, N.GL_TEXTURE_WRAP_T, (int)N.GL_CLAMP_TO_EDGE);
            N.glTexImage2D(N.GL_TEXTURE_2D, 0, (int)N.GL_RGBA, w, h, 0, N.GL_BGRA_EXT, N.GL_UNSIGNED_BYTE, IntPtr.Zero);
        }

        public void SetSwapInterval(int interval)
        {
            if (swapInterval == null || interval == currentInterval) return;
            swapInterval(interval);
            currentInterval = interval;
        }

        /// <summary>Latest frame (ARGB, FrameWidth x FrameHeight), used for screenshots.</summary>
        public int[] LastFrame { get { return frame; } }
        public int FrameWidth { get { return fw; } }
        public int FrameHeight { get { return fh; } }

        /// <summary>Legacy path: a 256x240 indexed frame through a palette lookup.</summary>
        public void Convert(ushort[] idx, int[] lut)
        {
            if (frame.Length < W * H) frame = new int[W * H];
            fw = lw = W; fh = lh = H;
            int n = lut.Length;
            fixed (ushort* s = idx) fixed (int* d = frame) fixed (int* l = lut)
            {
                for (int i = 0; i < W * H; i++) { int c = s[i]; d[i] = l[c < n ? c : 0x0F]; }
            }
        }

        /// <summary>Sets the frame to present (copied). logicalW/H drive aspect and integer scaling.</summary>
        public void SetFrame(int[] argb, int w, int h, int logicalW, int logicalH)
        {
            if (w < 1 || h < 1) return;
            if (frame.Length < w * h) frame = new int[w * h];
            Buffer.BlockCopy(argb, 0, frame, 0, w * h * 4);
            fw = w; fh = h; lw = Math.Max(1, logicalW); lh = Math.Max(1, logicalH);
        }

        public void Present(int clientW, int clientH, ScaleMode mode, bool ntscAspect, int scanlines, bool finishAfterSwap)
        {
            if (clientW < 1 || clientH < 1) return;
            N.glViewport(0, 0, clientW, clientH);
            N.glMatrixMode(N.GL_PROJECTION); N.glLoadIdentity();
            N.glOrtho(0, clientW, clientH, 0, -1, 1);
            N.glMatrixMode(N.GL_MODELVIEW); N.glLoadIdentity();
            N.glClearColor(0f, 0f, 0f, 1f);
            N.glClear(N.GL_COLOR_BUFFER_BIT);

            double par = ntscAspect ? 8.0 / 7.0 : 1.0;
            double dw, dh;
            if (mode == ScaleMode.Integer)
            {
                int k = (int)Math.Min(Math.Floor(clientW / (lw * par)), Math.Floor(clientH / (double)lh));
                if (k < 1) k = 1;
                dw = Math.Round(lw * par * k); dh = lh * k;
                if (dw > clientW || dh > clientH) { double s = Math.Min(clientW / (lw * par), clientH / (double)lh); dw = lw * par * s; dh = lh * s; }
            }
            else if (mode == ScaleMode.Fit)
            {
                double s = Math.Min(clientW / (lw * par), clientH / (double)lh);
                dw = lw * par * s; dh = lh * s;
            }
            else { dw = clientW; dh = clientH; }
            double dx = Math.Floor((clientW - dw) / 2), dy = Math.Floor((clientH - dh) / 2);

            bool exact = IsInt(dw / fw) && IsInt(dh / fh);
            N.glEnable(N.GL_TEXTURE_2D);
            float u1, v1;
            if (exact || dh < fh * 1.5 || dw < fw * 1.5)
            {
                if (fw > baseTexSize || fh > baseTexSize)
                {
                    baseTexSize = NextPow2(Math.Max(fw, fh));
                    CreateTexture(baseTex, baseTexSize, baseTexSize);
                }
                N.glBindTexture(N.GL_TEXTURE_2D, baseTex);
                fixed (int* f = frame) N.glTexSubImage2D(N.GL_TEXTURE_2D, 0, 0, 0, fw, fh, N.GL_BGRA_EXT, N.GL_UNSIGNED_BYTE, (IntPtr)f);
                u1 = fw / (float)baseTexSize; v1 = fh / (float)baseTexSize;
            }
            else
            {
                // Sharp-bilinear: nearest-neighbour prescale by the largest integer <= scale, then linear to the final size.
                int p = (int)Math.Min(4, Math.Max(1, Math.Floor(Math.Min(dw / fw, dh / fh))));
                if (p != bigFactor || big == null || bigW != fw || bigH != fh)
                {
                    bigFactor = p; bigW = fw; bigH = fh;
                    big = new int[fw * p * fh * p];
                    int texSize = NextPow2(Math.Max(fw * p, fh * p));
                    if (texSize != bigTexSize)
                    {
                        CreateTexture(bigTex, texSize, texSize);
                        N.glTexParameteri(N.GL_TEXTURE_2D, N.GL_TEXTURE_MIN_FILTER, (int)N.GL_LINEAR);
                        N.glTexParameteri(N.GL_TEXTURE_2D, N.GL_TEXTURE_MAG_FILTER, (int)N.GL_LINEAR);
                        bigTexSize = texSize;
                    }
                }
                int bw = fw * p;
                fixed (int* f = frame) fixed (int* b = big)
                {
                    for (int y = 0; y < fh; y++)
                    {
                        int* row = b + y * p * bw;
                        int* src = f + y * fw;
                        for (int x = 0; x < fw; x++) { int c = src[x]; int* o = row + x * p; for (int i = 0; i < p; i++) o[i] = c; }
                        for (int r = 1; r < p; r++) Buffer.MemoryCopy(row, row + r * bw, bw * 4, bw * 4);
                    }
                    N.glBindTexture(N.GL_TEXTURE_2D, bigTex);
                    N.glTexSubImage2D(N.GL_TEXTURE_2D, 0, 0, 0, bw, fh * p, N.GL_BGRA_EXT, N.GL_UNSIGNED_BYTE, (IntPtr)b);
                }
                u1 = bw / (float)bigTexSize; v1 = fh * p / (float)bigTexSize;
            }

            N.glColor4f(1, 1, 1, 1);
            N.glBegin(N.GL_QUADS);
            N.glTexCoord2f(0, 0); N.glVertex2f((float)dx, (float)dy);
            N.glTexCoord2f(u1, 0); N.glVertex2f((float)(dx + dw), (float)dy);
            N.glTexCoord2f(u1, v1); N.glVertex2f((float)(dx + dw), (float)(dy + dh));
            N.glTexCoord2f(0, v1); N.glVertex2f((float)dx, (float)(dy + dh));
            N.glEnd();
            N.glDisable(N.GL_TEXTURE_2D);

            if (scanlines > 0 && dh >= lh * 2)
            {
                // Darken the lower part of every logical row, like a CRT's gaps between scanlines.
                N.glEnable(N.GL_BLEND);
                N.glBlendFunc(N.GL_SRC_ALPHA, N.GL_ONE_MINUS_SRC_ALPHA);
                N.glColor4f(0, 0, 0, scanlines / 100f);
                double rowH = dh / lh;
                N.glBegin(N.GL_QUADS);
                for (int r = 0; r < lh; r++)
                {
                    float y0 = (float)(dy + r * rowH + rowH * 0.55), y1 = (float)(dy + (r + 1) * rowH);
                    N.glVertex2f((float)dx, y0); N.glVertex2f((float)(dx + dw), y0);
                    N.glVertex2f((float)(dx + dw), y1); N.glVertex2f((float)dx, y1);
                }
                N.glEnd();
                N.glDisable(N.GL_BLEND);
            }

            N.SwapBuffers(hdc);
            if (finishAfterSwap) N.glFinish();   // keeps the driver from queueing frames ahead (lower latency)
        }

        static bool IsInt(double v) { return Math.Abs(v - Math.Round(v)) < 1e-6; }
        static int NextPow2(int v) { int p = 1; while (p < v) p <<= 1; return p; }

        public void Dispose()
        {
            if (hglrc != IntPtr.Zero) { N.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); N.wglDeleteContext(hglrc); hglrc = IntPtr.Zero; }
            if (hdc != IntPtr.Zero) { N.ReleaseDC(hwnd, hdc); hdc = IntPtr.Zero; }
        }
    }
}
