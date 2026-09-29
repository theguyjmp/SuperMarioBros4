using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SMB4.Frontend
{
    /// <summary>
    /// One running game: core + ROM + SRAM persistence + save states + run-ahead + audio stream.
    /// Window-independent, so the self-test drives exactly the same code as the player.
    /// </summary>
    public sealed class Emulator : IDisposable
    {
        public LibretroCore Core;
        public readonly AudioStream Audio = new AudioStream(16384);
        public string RomLabel = "", RomPath = "";
        public int RomSize;
        public Action<string> Log = s => { };
        public Action<string> Notify = s => { };

        byte[] stateBuf;
        public bool CanSerialize { get; private set; }
        public string NativeRunAheadKey;          // core option that does run-ahead inside the core, if it has one
        string nativeOn, nativeOff;
        public bool CanRunAhead { get { return NativeRunAheadKey != null || CanSerialize; } }
        public bool RunAheadAutoOff;               // set when the machine is too slow for run-ahead
        public long Frames;

        // timing
        readonly Stopwatch sw = new Stopwatch();
        public double EmuMs;                       // smoothed cost of the last emulated frame(s), ms
        double raSum; int raCount;

        // SRAM
        string srmPath, coreSrmPath, coreSaveDir;
        public string SavePath { get { return srmPath; } }
        byte[] sramShadow;
        public int SramSize;
        public int SramWrites;

        // ---------------------------------------------------------------- ROM discovery
        /// <summary>--rom path, then &lt;exe dir&gt;\&lt;RomFile&gt;, then the ROM embedded in the exe.</summary>
        public static byte[] FindRom(GameConfig cfg, string argRom, out string label, out string filePath)
        {
            label = ""; filePath = null;
            if (!string.IsNullOrEmpty(argRom))
            {
                filePath = Path.GetFullPath(argRom); label = "file " + filePath;
                return File.ReadAllBytes(filePath);
            }
            string side = Path.Combine(PlayerProgram.ExeDir, cfg.RomFile);
            if (File.Exists(side)) { filePath = side; label = "override " + side; return File.ReadAllBytes(side); }
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("rom.sfc"))
            {
                if (s == null) return null;
                var b = new byte[s.Length];
                int off = 0; while (off < b.Length) { int n = s.Read(b, off, b.Length - off); if (n <= 0) break; off += n; }
                label = "embedded";
                return b;
            }
        }

        public static string FindCore(GameConfig cfg, string argCore)
        {
            if (!string.IsNullOrEmpty(argCore)) return Path.GetFullPath(argCore);
            // next to the exe, then any ancestor's bin\cores (dev builds in .agentbin\x find the repo's bin\cores)
            string dir = PlayerProgram.ExeDir;
            string p = Path.Combine(dir, "cores", cfg.Core);
            if (File.Exists(p)) return p;
            p = Path.Combine(dir, cfg.Core);
            if (File.Exists(p)) return p;
            for (int i = 0; i < 5 && dir != null; i++)
            {
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
                p = Path.Combine(dir, "bin", "cores", cfg.Core);
                if (File.Exists(p)) return p;
            }
            return Path.Combine(PlayerProgram.ExeDir, "cores", cfg.Core);
        }

        // ---------------------------------------------------------------- start / stop
        public void Start(GameConfig cfg, PlayerSettings settings, string corePath, string argRom, string saveDir)
        {
            string label, path;
            var rom = FindRom(cfg, argRom, out label, out path);
            if (rom == null) throw new InvalidOperationException("No game ROM found.\nPut " + cfg.RomFile + " next to the exe or rebuild with a ROM embedded.");
            RomLabel = label; RomSize = rom.Length;
            if (!File.Exists(corePath))
                throw new InvalidOperationException("The SNES emulator core is missing:\n" + corePath + "\n\nRun tools\\fetch-core.ps1 (downloads bsnes_libretro.dll into bin\\cores).");

            string sys = Path.Combine(saveDir, "system");
            try { Directory.CreateDirectory(sys); } catch { }
            coreSaveDir = Path.Combine(saveDir, "core");
            try { Directory.CreateDirectory(coreSaveDir); } catch { }
            Core = LibretroCore.Open(corePath, sys, coreSaveDir, Log);
            Core.OnMessage = m => Notify(m);
            foreach (var kv in cfg.CoreOptions) Core.Overrides[kv.Key] = kv.Value;
            foreach (var kv in settings.Core) Core.Overrides[kv.Key] = kv.Value;

            // The core always gets the ROM at <save>\rom\<name>: cores that manage their own battery file (bsnes) then
            // name it after that file, inside <save>\core, where we can sync it with our save file.
            bool custom = !string.IsNullOrEmpty(argRom);
            string romName = custom ? Path.GetFileName(argRom) : cfg.RomFile;
            srmPath = Path.Combine(saveDir, custom ? Path.GetFileNameWithoutExtension(argRom) + ".srm" : cfg.SaveFile);
            string romDir = Path.Combine(saveDir, "rom");
            Directory.CreateDirectory(romDir);
            path = Path.Combine(romDir, romName);
            bool same = File.Exists(path) && new FileInfo(path).Length == rom.Length && Same(File.ReadAllBytes(path), rom);
            if (!same) File.WriteAllBytes(path, rom);
            coreSrmPath = Path.Combine(coreSaveDir, Path.GetFileNameWithoutExtension(romName) + ".srm");
            try
            {
                if (File.Exists(srmPath)) File.Copy(srmPath, coreSrmPath, true);
                else if (File.Exists(coreSrmPath)) File.Delete(coreSrmPath);
            }
            catch (Exception ex) { Log("sram pre-copy failed: " + ex.Message); }
            RomPath = path;
            Core.LoadGame(path, rom);

            int ss = 0;
            try { ss = Core.SerializeSize; } catch { }
            if (ss > 0) { stateBuf = new byte[ss]; CanSerialize = true; }
            Log("serialize size " + ss);
            // Prefer the core's own run-ahead (bsnes: bsnes_run_ahead_frames). bsnes resets its audio stream on every
            // unserialize, so frontend-side run-ahead would silence it; its native run-ahead doesn't have that problem.
            foreach (var kv in Core.OptionValues)
                if (kv.Key.EndsWith("run_ahead_frames", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith("runahead", StringComparison.OrdinalIgnoreCase))
                {
                    NativeRunAheadKey = kv.Key;
                    nativeOff = kv.Value[0];
                    nativeOn = Array.IndexOf(kv.Value, "1") >= 0 ? "1" : kv.Value.Length > 1 ? kv.Value[1] : kv.Value[0];
                    Log("native run-ahead: " + kv.Key + " = " + string.Join("|", kv.Value));
                }

            SetupSram(rom);
        }

        // ---------------------------------------------------------------- SRAM discovery
        enum SramMode { None, Direct, StateScan }
        SramMode sramMode;
        IntPtr sramPtr;
        public int SramStateOffset = -1;
        public string SramInfo = "none";

        /// <summary>
        /// Direct: the core exposes SRAM (retro_get_memory_data or a SAVE_RAM memory map) → compared every 30 frames.
        /// StateScan: the core keeps SRAM private and only writes its own .srm on unload (bsnes) → SRAM is located once
        /// inside the serialized state (it holds exactly the bytes the core just loaded) and compared every 60 frames.
        /// Either way a change is written to the save file at once, and again on exit.
        /// </summary>
        void SetupSram(byte[] rom)
        {
            SramSize = Core.MemorySize(LibretroCore.MEMORY_SAVE_RAM);
            IntPtr mem = Core.MemoryData(LibretroCore.MEMORY_SAVE_RAM);
            if ((SramSize <= 0 || mem == IntPtr.Zero) && Core.MapSaveRam != IntPtr.Zero) { mem = Core.MapSaveRam; SramSize = Core.MapSaveRamSize; }
            if (SramSize > 0 && mem != IntPtr.Zero)
            {
                if (File.Exists(srmPath))
                {
                    var b = File.ReadAllBytes(srmPath);
                    Marshal.Copy(b, 0, mem, Math.Min(b.Length, SramSize));
                    Log("sram loaded " + b.Length + " bytes from " + srmPath);
                }
                sramPtr = mem; sramMode = SramMode.Direct;
                sramShadow = new byte[SramSize];
                Marshal.Copy(mem, sramShadow, 0, SramSize);
                SramInfo = "core memory";
                return;
            }
            int hdr = HeaderSramSize(rom);
            SramSize = 0;
            if (hdr <= 0 || !CanSerialize) { Log("no save RAM (header " + hdr + ")"); return; }
            if (!Core.Serialize(stateBuf)) return;
            var expect = new byte[hdr];
            for (int i = 0; i < hdr; i++) expect[i] = 0xFF;          // what a fresh cartridge RAM looks like in bsnes
            if (File.Exists(srmPath)) { var b = File.ReadAllBytes(srmPath); Array.Copy(b, expect, Math.Min(b.Length, hdr)); }
            int off = Find(stateBuf, expect);
            if (off < 0 && !File.Exists(srmPath)) { Array.Clear(expect, 0, hdr); off = Find(stateBuf, expect); }
            if (off < 0) { Log("sram: could not locate " + hdr + " bytes of save RAM in the core state"); SramInfo = "core file only"; return; }
            SramSize = hdr; SramStateOffset = off; sramMode = SramMode.StateScan;
            sramShadow = new byte[hdr];
            Array.Copy(stateBuf, off, sramShadow, 0, hdr);
            SramInfo = "state offset " + off;
            Log("sram: " + hdr + " bytes located in the core state at offset " + off);
        }

        /// <summary>SRAM size from the internal header (LoROM/HiROM/ExHiROM, copier header tolerated), 0 if none.</summary>
        public static int HeaderSramSize(byte[] rom)
        {
            int skip = rom.Length % 1024 == 512 ? 512 : 0;
            foreach (int h in new[] { 0x7FC0, 0xFFC0, 0x40FFC0 })
            {
                int a = skip + h;
                if (a + 0x20 > rom.Length) continue;
                int comp = rom[a + 0x1C] | rom[a + 0x1D] << 8, sum = rom[a + 0x1E] | rom[a + 0x1F] << 8;
                if ((comp ^ sum) != 0xFFFF) continue;
                int n = rom[a + 0x18];
                return n >= 1 && n <= 8 ? 1024 << n : 0;
            }
            return 0;
        }

        static int Find(byte[] hay, byte[] needle)
        {
            int n = needle.Length;
            for (int i = 0; i + n <= hay.Length; i++)
            {
                if (hay[i] != needle[0]) continue;
                int k = 1;
                while (k < n && hay[i + k] == needle[k]) k++;
                if (k == n) return i;
            }
            return -1;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ---------------------------------------------------------------- frame
        /// <summary>
        /// Emulates one frame with the given pads (<see cref="SMB4.Platform.Btn"/> masks). With run-ahead the frame is
        /// run for real (audio kept), the state saved, the next frame run speculatively with the same input (video kept),
        /// and the state restored: what you see is one frame ahead of the real game, which cancels one frame of
        /// the game's own input lag.
        /// </summary>
        public void RunFrame(int p1, int p2, bool runAhead, double speed, bool pushAudio = true)
        {
            Core.Pad[0] = LibretroCore.JoypadMask(p1);
            Core.Pad[1] = LibretroCore.JoypadMask(p2);
            Core.ClearAudio();
            sw.Restart();
            bool ra = runAhead && CanRunAhead && !RunAheadAutoOff;
            if (NativeRunAheadKey != null)
            {
                string want = ra ? nativeOn : nativeOff, cur;
                if (!Core.Options.TryGetValue(NativeRunAheadKey, out cur) || cur != want) Core.SetOption(NativeRunAheadKey, want);
                Core.CaptureVideo = true; Core.CaptureAudio = true; Core.SetAvEnable(true, true);
                Core.Run();
            }
            else if (ra)
            {
                Core.CaptureVideo = false; Core.CaptureAudio = true; Core.SetAvEnable(false, true);
                Core.Run();
                bool ok = Core.Serialize(stateBuf);
                if (!ok)
                {
                    // size may have changed; try once with a fresh size, then give up on run-ahead
                    int ss = Core.SerializeSize;
                    if (ss > 0 && ss != stateBuf.Length) { stateBuf = new byte[ss]; ok = Core.Serialize(stateBuf); }
                    if (!ok) { CanSerialize = false; Notify("RUN-AHEAD UNAVAILABLE"); }
                }
                if (ok)
                {
                    Core.CaptureVideo = true; Core.CaptureAudio = false; Core.SetAvEnable(true, false);
                    Core.Run();
                    Core.Unserialize(stateBuf);
                }
            }
            else
            {
                Core.CaptureVideo = true; Core.CaptureAudio = true; Core.SetAvEnable(true, true);
                Core.Run();
            }
            Core.CaptureVideo = true; Core.CaptureAudio = true; Core.SetAvEnable(true, true);
            double ms = sw.Elapsed.TotalMilliseconds;
            EmuMs = EmuMs <= 0 ? ms : EmuMs * 0.95 + ms * 0.05;
            if (ra && Frames > 120)
            {
                raSum += ms; raCount++;
                if (raCount == 240)
                {
                    if (raSum / raCount > 12.0) { RunAheadAutoOff = true; Notify("RUN-AHEAD OFF: PC TOO SLOW"); }
                    raSum = 0; raCount = 0;
                }
            }
            if (pushAudio) Audio.Push(Core.Audio, Core.AudioFrames, Core.SampleRate, speed);
            Frames++;
            if (sramMode == SramMode.Direct ? Frames % 30 == 0 : Frames % 60 == 0) FlushSram(false);
        }

        // ---------------------------------------------------------------- SRAM / states
        /// <summary>Writes the save file if SRAM changed since the last write (force: also when no file exists yet).</summary>
        public void FlushSram(bool force)
        {
            if (sramShadow == null || Core == null || !Core.Loaded) return;
            bool changed = force && !File.Exists(srmPath);
            if (sramMode == SramMode.Direct)
            {
                unsafe
                {
                    byte* p = (byte*)sramPtr;
                    for (int i = 0; i < sramShadow.Length && !changed; i++) if (p[i] != sramShadow[i]) changed = true;
                }
                if (!changed) return;
                Marshal.Copy(sramPtr, sramShadow, 0, sramShadow.Length);
            }
            else if (sramMode == SramMode.StateScan)
            {
                if (!Core.Serialize(stateBuf)) return;
                int off = SramStateOffset;
                for (int i = 0; i < sramShadow.Length && !changed; i++) if (stateBuf[off + i] != sramShadow[i]) changed = true;
                if (!changed) return;
                Array.Copy(stateBuf, off, sramShadow, 0, sramShadow.Length);
            }
            else return;
            WriteSave(sramShadow);
        }

        void WriteSave(byte[] data)
        {
            try
            {
                string tmp = srmPath + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(srmPath)) File.Delete(srmPath);
                File.Move(tmp, srmPath);
                SramWrites++;
                Log("save written (" + data.Length + " bytes)");
            }
            catch (Exception ex) { Log("sram write failed: " + ex.Message); }
        }

        public string StatePath(int slot) { return Path.ChangeExtension(srmPath, ".state" + slot); }

        public bool SaveState(int slot)
        {
            if (!CanSerialize) return false;
            var b = new byte[Core.SerializeSize];
            if (!Core.Serialize(b)) return false;
            try { File.WriteAllBytes(StatePath(slot), b); return true; } catch { return false; }
        }

        public bool LoadState(int slot)
        {
            if (!CanSerialize || !File.Exists(StatePath(slot))) return false;
            var b = File.ReadAllBytes(StatePath(slot));
            bool ok = Core.Unserialize(b);
            if (ok) Audio.Reset();
            return ok;
        }

        public void Reset() { Core.Reset(); Audio.Reset(); }

        /// <summary>Flushes SRAM, unloads the game (cores like bsnes write their own .srm now) and adopts that file.</summary>
        public void Dispose()
        {
            if (Core == null) return;
            FlushSram(true);
            Core.Dispose();
            Core = null;
            try
            {
                if (coreSrmPath != null && File.Exists(coreSrmPath))
                {
                    var b = File.ReadAllBytes(coreSrmPath);
                    if (!File.Exists(srmPath) || !Same(File.ReadAllBytes(srmPath), b)) { WriteSave(b); Log("adopted the core's save file"); }
                }
            }
            catch (Exception ex) { Log("sram sync failed: " + ex.Message); }
        }
    }
}
