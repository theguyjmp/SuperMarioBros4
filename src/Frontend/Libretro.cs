using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using N = SMB4.Platform.Native;

namespace SMB4.Frontend
{
    /// <summary>
    /// A libretro core loaded at runtime (LoadLibrary + GetProcAddress) and driven through P/Invoke delegates.
    /// Software-rendered cores only (bsnes, snes9x, ...). Everything runs on the caller's thread.
    /// Video frames are converted to ARGB; audio is collected per retro_run; input comes from <see cref="Pad"/>.
    /// </summary>
    public sealed unsafe class LibretroCore : IDisposable
    {
        // ---------------------------------------------------------------- libretro constants
        public const uint DEVICE_JOYPAD = 1;
        public const uint MEMORY_SAVE_RAM = 0;
        const uint ENV_EXPERIMENTAL = 0x10000;
        public const int JOY_B = 0, JOY_Y = 1, JOY_SELECT = 2, JOY_START = 3, JOY_UP = 4, JOY_DOWN = 5, JOY_LEFT = 6,
                         JOY_RIGHT = 7, JOY_A = 8, JOY_X = 9, JOY_L = 10, JOY_R = 11, JOY_MASK = 256;

        // ---------------------------------------------------------------- native entry points
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint UIntFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PtrFn(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PortDevFn(uint port, uint device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool LoadGameFn(IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr SizeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool BufFn(IntPtr data, UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr MemDataFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr MemSizeFn(uint id);

        // callbacks the core calls
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool EnvCb(uint cmd, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VideoCb(IntPtr data, uint w, uint h, UIntPtr pitch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void AudioCb(short l, short r);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr AudioBatchCb(IntPtr data, UIntPtr frames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InputPollCb();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate short InputStateCb(uint port, uint device, uint index, uint id);
        // retro_log_printf_t is variadic; on x64 the extra arguments are simply ignored (caller cleans up).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LogCb(int level, IntPtr fmt);

        IntPtr lib;
        VoidFn fInit, fDeinit, fRun, fReset, fUnload;
        UIntFn fApiVersion;
        PtrFn fSetEnv, fSetVideo, fSetAudio, fSetAudioBatch, fSetInputPoll, fSetInputState, fSysInfo, fAvInfo;
        PortDevFn fSetPortDevice;
        LoadGameFn fLoadGame;
        SizeFn fSerializeSize;
        BufFn fSerialize, fUnserialize;
        MemDataFn fMemData;
        MemSizeFn fMemSize;

        // kept alive for the lifetime of the core (the GC must not collect callback thunks)
        EnvCb envCb; VideoCb videoCb; AudioCb audioCb; AudioBatchCb audioBatchCb; InputPollCb pollCb; InputStateCb stateCb; LogCb logCb;
        IntPtr logIface;
        readonly List<IntPtr> allocations = new List<IntPtr>();
        readonly Dictionary<string, IntPtr> utf8Cache = new Dictionary<string, IntPtr>();

        // ---------------------------------------------------------------- public state
        public string LibraryName = "", LibraryVersion = "", ValidExtensions = "";
        public bool NeedFullpath;
        public double Fps = 60.0988, SampleRate = 32040;
        public int BaseWidth = 256, BaseHeight = 224;
        public float AspectRatio;
        public bool Loaded { get; private set; }
        public string SystemDir = "", SaveDir = "";
        public Action<string> Log = s => { };
        public Action<string> OnMessage = s => { };

        /// <summary>Held buttons per port, libretro joypad bit layout (bit n = JOY_n).</summary>
        public readonly int[] Pad = new int[2];
        public int PollCount;

        /// <summary>Last video frame, ARGB, FrameW x FrameH (null until the first frame).</summary>
        public int[] Frame;
        public int FrameW, FrameH;
        public bool CaptureVideo = true;      // false = ignore frames (run-ahead's hidden frame)
        public bool CaptureAudio = true;      // false = drop samples (run-ahead's speculative frame)
        public int FramesReceived;

        /// <summary>Interleaved stereo samples produced by the last retro_run(s) since ClearAudio().</summary>
        public short[] Audio = new short[8192];
        public int AudioFrames;
        public long TotalAudioFrames;

        int pixelFormat = 0;   // 0 = 0RGB1555, 1 = XRGB8888, 2 = RGB565
        bool avEnableVideo = true, avEnableAudio = true;

        /// <summary>Core option values: declared defaults, overridden by Overrides.</summary>
        public readonly Dictionary<string, string> Options = new Dictionary<string, string>();
        public readonly Dictionary<string, string[]> OptionValues = new Dictionary<string, string[]>();
        public readonly Dictionary<string, string> OptionDesc = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        bool optionsDirty;

        // ---------------------------------------------------------------- loading
        public static LibretroCore Open(string dllPath, string systemDir, string saveDir, Action<string> log)
        {
            var c = new LibretroCore();
            c.Log = log ?? (s => { });
            c.SystemDir = systemDir; c.SaveDir = saveDir;
            c.lib = N.LoadLibraryW(dllPath);
            if (c.lib == IntPtr.Zero) throw new InvalidOperationException("Could not load the emulator core:\n" + dllPath + "\n(Windows error " + Marshal.GetLastWin32Error() + ")");
            c.Bind();
            return c;
        }

        T Fn<T>(string name) where T : class
        {
            IntPtr p = N.GetProcAddress(lib, name);
            if (p == IntPtr.Zero) throw new InvalidOperationException("The emulator core has no " + name + " (not a libretro core?)");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        void Bind()
        {
            fApiVersion = Fn<UIntFn>("retro_api_version");
            fInit = Fn<VoidFn>("retro_init"); fDeinit = Fn<VoidFn>("retro_deinit");
            fRun = Fn<VoidFn>("retro_run"); fReset = Fn<VoidFn>("retro_reset"); fUnload = Fn<VoidFn>("retro_unload_game");
            fSetEnv = Fn<PtrFn>("retro_set_environment"); fSetVideo = Fn<PtrFn>("retro_set_video_refresh");
            fSetAudio = Fn<PtrFn>("retro_set_audio_sample"); fSetAudioBatch = Fn<PtrFn>("retro_set_audio_sample_batch");
            fSetInputPoll = Fn<PtrFn>("retro_set_input_poll"); fSetInputState = Fn<PtrFn>("retro_set_input_state");
            fSysInfo = Fn<PtrFn>("retro_get_system_info"); fAvInfo = Fn<PtrFn>("retro_get_system_av_info");
            fSetPortDevice = Fn<PortDevFn>("retro_set_controller_port_device");
            fLoadGame = Fn<LoadGameFn>("retro_load_game");
            fSerializeSize = Fn<SizeFn>("retro_serialize_size");
            fSerialize = Fn<BufFn>("retro_serialize"); fUnserialize = Fn<BufFn>("retro_unserialize");
            fMemData = Fn<MemDataFn>("retro_get_memory_data"); fMemSize = Fn<MemSizeFn>("retro_get_memory_size");

            uint api = fApiVersion();
            if (api != 1) throw new InvalidOperationException("Unsupported libretro API version " + api);

            envCb = Environment; videoCb = OnVideo; audioCb = OnAudio; audioBatchCb = OnAudioBatch; pollCb = OnPoll; stateCb = OnInputState;
            logCb = OnLog;
            logIface = Alloc(IntPtr.Size);
            Marshal.WriteIntPtr(logIface, Marshal.GetFunctionPointerForDelegate(logCb));

            fSetEnv(Marshal.GetFunctionPointerForDelegate(envCb));
            fSetVideo(Marshal.GetFunctionPointerForDelegate(videoCb));
            fSetAudio(Marshal.GetFunctionPointerForDelegate(audioCb));
            fSetAudioBatch(Marshal.GetFunctionPointerForDelegate(audioBatchCb));
            fSetInputPoll(Marshal.GetFunctionPointerForDelegate(pollCb));
            fSetInputState(Marshal.GetFunctionPointerForDelegate(stateCb));
            fInit();

            IntPtr si = Alloc(64);
            fSysInfo(si);
            LibraryName = Str(Marshal.ReadIntPtr(si, 0));
            LibraryVersion = Str(Marshal.ReadIntPtr(si, IntPtr.Size));
            ValidExtensions = Str(Marshal.ReadIntPtr(si, IntPtr.Size * 2));
            NeedFullpath = Marshal.ReadByte(si, IntPtr.Size * 3) != 0;
            Log("core: " + LibraryName + " " + LibraryVersion + " ext=" + ValidExtensions + " need_fullpath=" + NeedFullpath);
        }

        /// <summary>Loads the game. path may be null when the core doesn't need a path.</summary>
        public void LoadGame(string path, byte[] rom)
        {
            IntPtr data = Alloc(rom.Length);
            Marshal.Copy(rom, 0, data, rom.Length);
            IntPtr gi = Alloc(32);
            Marshal.WriteIntPtr(gi, 0, path != null ? Utf8(path) : IntPtr.Zero);
            Marshal.WriteIntPtr(gi, IntPtr.Size, data);
            Marshal.WriteIntPtr(gi, IntPtr.Size * 2, new IntPtr(rom.Length));
            Marshal.WriteIntPtr(gi, IntPtr.Size * 3, IntPtr.Zero);
            if (!fLoadGame(gi)) throw new InvalidOperationException("The emulator core could not load the game ROM.");
            Loaded = true;
            ReadAvInfo();
            fSetPortDevice(0, DEVICE_JOYPAD);
            fSetPortDevice(1, DEVICE_JOYPAD);
        }

        void ReadAvInfo()
        {
            IntPtr av = Alloc(64);
            fAvInfo(av);
            ReadGeometry(av);
            double fps = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(av, 24));
            double rate = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(av, 32));
            if (fps > 1 && fps < 1000) Fps = fps;
            if (rate > 1000 && rate < 400000) SampleRate = rate;
            Log("av: " + BaseWidth + "x" + BaseHeight + " fps=" + Fps.ToString("0.0000") + " rate=" + SampleRate.ToString("0.0"));
        }

        void ReadGeometry(IntPtr g)
        {
            int bw = Marshal.ReadInt32(g, 0), bh = Marshal.ReadInt32(g, 4);
            if (bw > 0 && bh > 0) { BaseWidth = bw; BaseHeight = bh; }
            AspectRatio = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(g, 16)), 0);
        }

        /// <summary>Converts a <see cref="SMB4.Platform.Btn"/> mask (the shared input system) to the libretro joypad mask.</summary>
        public static int JoypadMask(int held)
        {
            int m = 0;
            if ((held & 1) != 0) m |= 1 << JOY_UP;
            if ((held & 2) != 0) m |= 1 << JOY_DOWN;
            if ((held & 4) != 0) m |= 1 << JOY_LEFT;
            if ((held & 8) != 0) m |= 1 << JOY_RIGHT;
            if ((held & 16) != 0) m |= 1 << JOY_B;
            if ((held & 32) != 0) m |= 1 << JOY_Y;
            if ((held & 64) != 0) m |= 1 << JOY_START;
            if ((held & 128) != 0) m |= 1 << JOY_SELECT;
            if ((held & 256) != 0) m |= 1 << JOY_A;
            if ((held & 512) != 0) m |= 1 << JOY_X;
            if ((held & 1024) != 0) m |= 1 << JOY_L;
            if ((held & 2048) != 0) m |= 1 << JOY_R;
            return m;
        }

        // ---------------------------------------------------------------- running
        public void Run() { fRun(); }
        public void Reset() { fReset(); }

        public void ClearAudio() { AudioFrames = 0; }

        /// <summary>Tells the core (if it asks) which outputs matter for the next retro_run (run-ahead).</summary>
        public void SetAvEnable(bool video, bool audio) { avEnableVideo = video; avEnableAudio = audio; }

        public int SerializeSize { get { return (int)fSerializeSize().ToUInt64(); } }

        public bool Serialize(byte[] buf)
        {
            fixed (byte* p = buf) return fSerialize((IntPtr)p, new UIntPtr((uint)buf.Length));
        }

        public bool Unserialize(byte[] buf)
        {
            fixed (byte* p = buf) return fUnserialize((IntPtr)p, new UIntPtr((uint)buf.Length));
        }

        public IntPtr MemoryData(uint id) { return fMemData(id); }
        public int MemorySize(uint id) { return (int)fMemSize(id).ToUInt64(); }

        // ---------------------------------------------------------------- callbacks
        void OnPoll() { PollCount++; }

        short OnInputState(uint port, uint device, uint index, uint id)
        {
            if (port > 1 || (device & 0xFF) != DEVICE_JOYPAD) return 0;
            int held = Pad[port];
            if (id == JOY_MASK) return (short)(held & 0xFFF);
            if (id > 15) return 0;
            return (short)((held >> (int)id) & 1);
        }

        void OnVideo(IntPtr data, uint w, uint h, UIntPtr pitchU)
        {
            try
            {
                if (!CaptureVideo || data == IntPtr.Zero || w == 0 || h == 0) return;   // NULL = duplicate frame
                int W = (int)w, H = (int)h, pitch = (int)pitchU.ToUInt64();
                if (Frame == null || Frame.Length < W * H) Frame = new int[Math.Max(W * H, 512 * 480)];
                FrameW = W; FrameH = H;
                FramesReceived++;
                byte* src = (byte*)data;
                fixed (int* dst = Frame)
                {
                    for (int y = 0; y < H; y++)
                    {
                        int* d = dst + y * W;
                        if (pixelFormat == 1)
                        {
                            uint* s = (uint*)(src + y * pitch);
                            for (int x = 0; x < W; x++) d[x] = (int)(s[x] | 0xFF000000u);
                        }
                        else if (pixelFormat == 2)
                        {
                            ushort* s = (ushort*)(src + y * pitch);
                            for (int x = 0; x < W; x++)
                            {
                                int c = s[x];
                                int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
                                d[x] = unchecked((int)0xFF000000) | ((r << 3 | r >> 2) << 16) | ((g << 2 | g >> 4) << 8) | (b << 3 | b >> 2);
                            }
                        }
                        else
                        {
                            ushort* s = (ushort*)(src + y * pitch);
                            for (int x = 0; x < W; x++)
                            {
                                int c = s[x];
                                int r = (c >> 10) & 31, g = (c >> 5) & 31, b = c & 31;
                                d[x] = unchecked((int)0xFF000000) | ((r << 3 | r >> 2) << 16) | ((g << 3 | g >> 2) << 8) | (b << 3 | b >> 2);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log("video cb: " + ex.Message); }
        }

        void EnsureAudio(int frames)
        {
            int need = (AudioFrames + frames) * 2;
            if (need > Audio.Length) { var n = new short[Math.Max(need, Audio.Length * 2)]; Buffer.BlockCopy(Audio, 0, n, 0, AudioFrames * 4); Audio = n; }
        }

        void OnAudio(short l, short r)
        {
            if (!CaptureAudio) return;
            EnsureAudio(1);
            Audio[AudioFrames * 2] = l; Audio[AudioFrames * 2 + 1] = r;
            AudioFrames++; TotalAudioFrames++;
        }

        UIntPtr OnAudioBatch(IntPtr data, UIntPtr framesU)
        {
            int frames = (int)framesU.ToUInt64();
            if (CaptureAudio && frames > 0 && data != IntPtr.Zero)
            {
                EnsureAudio(frames);
                Marshal.Copy(data, Audio, AudioFrames * 2, frames * 2);
                AudioFrames += frames; TotalAudioFrames += frames;
            }
            return framesU;
        }

        void OnLog(int level, IntPtr fmt)
        {
            try { if (level >= 1) Log("core log[" + level + "]: " + Str(fmt).TrimEnd()); } catch { }
        }

        bool Environment(uint cmd, IntPtr data)
        {
            try { return Env(cmd, data); }
            catch (Exception ex) { Log("env " + cmd + ": " + ex.Message); return false; }
        }

        bool Env(uint cmd, IntPtr data)
        {
            switch (cmd)
            {
                case 1: return true;                                                        // SET_ROTATION
                case 2: if (data != IntPtr.Zero) Marshal.WriteByte(data, 0); return true;  // GET_OVERSCAN (deprecated): no
                case 3: if (data != IntPtr.Zero) Marshal.WriteByte(data, 1); return true;  // GET_CAN_DUPE
                case 6:                                                                     // SET_MESSAGE
                    if (data != IntPtr.Zero) OnMessage(Str(Marshal.ReadIntPtr(data)));
                    return true;
                case 7: return false;                                                       // SHUTDOWN
                case 8: return true;                                                        // SET_PERFORMANCE_LEVEL
                case 9: Marshal.WriteIntPtr(data, Utf8(SystemDir)); return true;            // GET_SYSTEM_DIRECTORY
                case 10:                                                                    // SET_PIXEL_FORMAT
                {
                    int f = Marshal.ReadInt32(data);
                    if (f < 0 || f > 2) return false;
                    pixelFormat = f; Log("pixel format " + f); return true;
                }
                case 11: return true;                                                       // SET_INPUT_DESCRIPTORS
                case 15:                                                                    // GET_VARIABLE
                {
                    string key = Str(Marshal.ReadIntPtr(data, 0));
                    string v;
                    if (key == null || (!Options.TryGetValue(key, out v) && !Overrides.TryGetValue(key, out v))) { Marshal.WriteIntPtr(data, IntPtr.Size, IntPtr.Zero); return false; }
                    Marshal.WriteIntPtr(data, IntPtr.Size, Utf8(v));
                    return true;
                }
                case 16: ParseVariables(data); return true;                                 // SET_VARIABLES
                case 17: Marshal.WriteByte(data, (byte)(optionsDirty ? 1 : 0)); optionsDirty = false; return true; // GET_VARIABLE_UPDATE
                case 18: return true;                                                       // SET_SUPPORT_NO_GAME
                case 27: Marshal.WriteIntPtr(data, Marshal.ReadIntPtr(logIface)); return true; // GET_LOG_INTERFACE
                case 31: Marshal.WriteIntPtr(data, Utf8(SaveDir)); return true;             // GET_SAVE_DIRECTORY
                case 32:                                                                    // SET_SYSTEM_AV_INFO
                {
                    ReadGeometry(data);
                    double fps = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, 24));
                    double rate = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, 32));
                    if (fps > 1 && fps < 1000) Fps = fps;
                    if (rate > 1000 && rate < 400000) SampleRate = rate;
                    return true;
                }
                case 34: case 35: case 44: return true;                                     // SUBSYSTEM_INFO, CONTROLLER_INFO, SERIALIZATION_QUIRKS
                case 36 | ENV_EXPERIMENTAL: ParseMemoryMaps(data); return true;            // SET_MEMORY_MAPS
                case 37: ReadGeometry(data); return true;                                   // SET_GEOMETRY
                case 39: Marshal.WriteInt32(data, 0); return true;                          // GET_LANGUAGE: English
                case 47 | ENV_EXPERIMENTAL:                                                 // GET_AUDIO_VIDEO_ENABLE
                    if (data != IntPtr.Zero) Marshal.WriteInt32(data, (avEnableVideo ? 1 : 0) | (avEnableAudio ? 2 : 0));
                    return true;
                case 49 | ENV_EXPERIMENTAL: if (data != IntPtr.Zero) Marshal.WriteByte(data, 0); return true;   // GET_FASTFORWARDING
                case 51 | ENV_EXPERIMENTAL: return true;                                    // GET_INPUT_BITMASKS
                case 55: return true;                                                       // SET_CORE_OPTIONS_DISPLAY
                default: return false;   // incl. GET_CORE_OPTIONS_VERSION → cores fall back to SET_VARIABLES
            }
        }

        void ParseVariables(IntPtr arr)
        {
            for (int i = 0; ; i++)
            {
                IntPtr k = Marshal.ReadIntPtr(arr, i * IntPtr.Size * 2);
                if (k == IntPtr.Zero) break;
                string key = Str(k), val = Str(Marshal.ReadIntPtr(arr, i * IntPtr.Size * 2 + IntPtr.Size)) ?? "";
                int semi = val.IndexOf(';');
                string desc = semi >= 0 ? val.Substring(0, semi).Trim() : key;
                var opts = (semi >= 0 ? val.Substring(semi + 1) : val).Trim().Split('|');
                OptionValues[key] = opts; OptionDesc[key] = desc;
                string ov;
                Options[key] = Overrides.TryGetValue(key, out ov) ? ov : opts[0];
            }
            optionsDirty = true;
            Log("core declared " + OptionValues.Count + " options");
        }

        /// <summary>Save RAM found through SET_MEMORY_MAPS (for cores whose retro_get_memory_* return nothing).</summary>
        public IntPtr MapSaveRam; public int MapSaveRamSize;

        void ParseMemoryMaps(IntPtr map)
        {
            if (map == IntPtr.Zero) return;
            IntPtr descs = Marshal.ReadIntPtr(map, 0);
            int n = Marshal.ReadInt32(map, IntPtr.Size);
            const int DescSize = 64;   // flags(8) ptr offset start select disconnect len addrspace (7 x 8)
            for (int i = 0; i < n && i < 256; i++)
            {
                IntPtr d = descs + i * DescSize;
                long flags = Marshal.ReadInt64(d, 0);
                IntPtr ptr = Marshal.ReadIntPtr(d, 8);
                long offset = Marshal.ReadInt64(d, 16), start = Marshal.ReadInt64(d, 24), len = Marshal.ReadInt64(d, 48);
                string space = Str(Marshal.ReadIntPtr(d, 56)) ?? "";
                Log("memmap " + i + ": flags=" + flags.ToString("X") + " start=" + start.ToString("X6") + " len=" + len.ToString("X") + " offset=" + offset.ToString("X") + " ptr=" + (ptr == IntPtr.Zero ? "null" : "set") + " space=" + space);
                if ((flags & 8) != 0 && ptr != IntPtr.Zero && len > 0 && MapSaveRam == IntPtr.Zero && offset == 0)
                { MapSaveRam = ptr; MapSaveRamSize = (int)len; }
            }
        }

        /// <summary>Changes a core option at runtime (the core sees it on its next GET_VARIABLE_UPDATE).</summary>
        public void SetOption(string key, string value)
        {
            Overrides[key] = value;
            Options[key] = value;
            optionsDirty = true;
        }

        // ---------------------------------------------------------------- helpers
        IntPtr Alloc(int bytes)
        {
            IntPtr p = Marshal.AllocHGlobal(Math.Max(1, bytes));
            for (int i = 0; i < Math.Min(bytes, 64); i++) Marshal.WriteByte(p, i, 0);
            allocations.Add(p);
            return p;
        }

        IntPtr Utf8(string s)
        {
            IntPtr p;
            if (utf8Cache.TryGetValue(s, out p)) return p;
            var b = Encoding.UTF8.GetBytes(s);
            p = Alloc(b.Length + 1);
            Marshal.Copy(b, 0, p, b.Length);
            Marshal.WriteByte(p, b.Length, 0);
            utf8Cache[s] = p;
            return p;
        }

        static string Str(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0 && n < 65536) n++;
            var b = new byte[n];
            Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public void Dispose()
        {
            if (lib == IntPtr.Zero) return;
            try
            {
                if (Loaded) fUnload();
                fDeinit();
            }
            catch { }
            Loaded = false;
            // The library stays mapped until process exit: some cores keep threads/handlers alive after deinit.
            lib = IntPtr.Zero;
            foreach (var p in allocations) Marshal.FreeHGlobal(p);
            allocations.Clear(); utf8Cache.Clear();
        }
    }
}
