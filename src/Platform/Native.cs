using System;
using System.Runtime.InteropServices;
using System.Security;

namespace SMB4.Platform
{
    /// <summary>All Win32 / OpenGL / XInput / winmm interop in one place.</summary>
    [SuppressUnmanagedCodeSecurity]
    internal static class Native
    {
        // ------------------------------------------------------------------ user32 / gdi32 / kernel32
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hWnd; public uint msg; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX, ptY; }

        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize, nVersion; public uint dwFlags;
            public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift,
                        cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits,
                        cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }
        public const uint PFD_DRAW_TO_WINDOW = 0x4, PFD_SUPPORT_OPENGL = 0x20, PFD_DOUBLEBUFFER = 0x1;
        [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hdc);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll")]
        public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();

        // ------------------------------------------------------------------ OpenGL 1.1
        public const uint GL_TEXTURE_2D = 0x0DE1, GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800,
            GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_NEAREST = 0x2600, GL_LINEAR = 0x2601,
            GL_CLAMP = 0x2900, GL_CLAMP_TO_EDGE = 0x812F, GL_REPEAT = 0x2901, GL_RGBA = 0x1908, GL_BGRA_EXT = 0x80E1,
            GL_UNSIGNED_BYTE = 0x1401, GL_COLOR_BUFFER_BIT = 0x4000, GL_QUADS = 0x0007, GL_PROJECTION = 0x1701,
            GL_MODELVIEW = 0x1700, GL_BLEND = 0x0BE2, GL_SRC_ALPHA = 0x0302, GL_ONE_MINUS_SRC_ALPHA = 0x0303,
            GL_VENDOR = 0x1F00, GL_RENDERER = 0x1F01, GL_VERSION = 0x1F02, GL_UNPACK_ALIGNMENT = 0x0CF5,
            GL_DEPTH_TEST = 0x0B71, GL_DITHER = 0x0BD0;

        [DllImport("opengl32.dll")] public static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport("opengl32.dll")] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
        [DllImport("opengl32.dll")] public static extern bool wglDeleteContext(IntPtr hglrc);
        [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] public static extern IntPtr wglGetProcAddress(string name);
        [DllImport("opengl32.dll")] public static extern void glViewport(int x, int y, int w, int h);
        [DllImport("opengl32.dll")] public static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] public static extern void glClear(uint mask);
        [DllImport("opengl32.dll")] public static extern void glEnable(uint cap);
        [DllImport("opengl32.dll")] public static extern void glDisable(uint cap);
        [DllImport("opengl32.dll")] public static extern void glGenTextures(int n, uint[] textures);
        [DllImport("opengl32.dll")] public static extern void glDeleteTextures(int n, uint[] textures);
        [DllImport("opengl32.dll")] public static extern void glBindTexture(uint target, uint texture);
        [DllImport("opengl32.dll")] public static extern void glTexParameteri(uint target, uint pname, int param);
        [DllImport("opengl32.dll")] public static extern void glTexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, IntPtr pixels);
        [DllImport("opengl32.dll")] public static extern void glTexSubImage2D(uint target, int level, int x, int y, int w, int h, uint format, uint type, IntPtr pixels);
        [DllImport("opengl32.dll")] public static extern void glBegin(uint mode);
        [DllImport("opengl32.dll")] public static extern void glEnd();
        [DllImport("opengl32.dll")] public static extern void glTexCoord2f(float s, float t);
        [DllImport("opengl32.dll")] public static extern void glVertex2f(float x, float y);
        [DllImport("opengl32.dll")] public static extern void glColor4f(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] public static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")] public static extern void glLoadIdentity();
        [DllImport("opengl32.dll")] public static extern void glOrtho(double l, double r, double b, double t, double n, double f);
        [DllImport("opengl32.dll")] public static extern void glBlendFunc(uint s, uint d);
        [DllImport("opengl32.dll")] public static extern void glFinish();
        [DllImport("opengl32.dll")] public static extern void glPixelStorei(uint pname, int param);
        [DllImport("opengl32.dll")] public static extern IntPtr glGetString(uint name);
        [DllImport("opengl32.dll")] public static extern uint glGetError();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int SwapIntervalFn(int interval);

        // ------------------------------------------------------------------ XInput
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION { public ushort wLeftMotorSpeed, wRightMotorSpeed; }
        [DllImport("xinput1_4.dll")] public static extern uint XInputGetState(uint index, out XINPUT_STATE state);
        [DllImport("xinput1_4.dll")] public static extern uint XInputSetState(uint index, ref XINPUT_VIBRATION vibration);

        // ------------------------------------------------------------------ winmm joystick (DirectInput-class pads: DualShock, DualSense, Switch Pro, 8BitDo, generic USB)
        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX { public int dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax,
                        wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }
        public const int JOY_RETURNALL = 0xFF, JOY_RETURNPOVCTS = 0x200;
        [DllImport("winmm.dll")] public static extern int joyGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")] public static extern int joyGetDevCaps(IntPtr id, ref JOYCAPS caps, int size);
        [DllImport("winmm.dll")] public static extern int joyGetPosEx(int id, ref JOYINFOEX info);

        // ------------------------------------------------------------------ winmm waveOut
        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        public const uint WHDR_DONE = 1;
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr hwo, int deviceId, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr hwo);
    }
}
