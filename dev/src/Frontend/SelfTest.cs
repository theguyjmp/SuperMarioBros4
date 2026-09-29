using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using SMB4.Platform;

namespace SMB4.Frontend
{
    /// <summary>
    /// Headless check of the whole emulation path (no window, no audio device):
    ///   SuperMarioBros4.exe --selftest [--out dir] [--frames 300] [--rom x.sfc] [--core x.dll] [--script "a-b:BTN+BTN;..."] [--no-runahead] [--keep-save]
    /// Loads the core + ROM, runs the frames with scripted input (run-ahead on by default), drains audio through the
    /// real resampler at exactly 800 frames per tick like a 48 kHz device, verifies run-ahead shows frame N+1, a
    /// save-state round trip and SRAM persistence (flush on change + after unload), and writes selftest.png (last frame,
    /// 3x), shot_NNNN.png every 60 frames, selftest.wav (48 kHz output), selftest.txt and selftest.log.
    /// Exit code 0 = all checks passed.
    /// </summary>
    public static class SelfTest
    {
        const string DefaultScript = "100-105:START;160-165:START;220-225:START;240-300:RIGHT;250-262:B;280-292:B+Y";

        public static int Run(string unusedSaveDir)
        {
            try { Native.AttachConsole(-1); } catch { }
            string outDir = Cli.ArgValue("--out");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(PlayerProgram.ExeDir, "selftest");
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            string saveDir = Path.Combine(outDir, "save");
            if (!Cli.HasArg("--keep-save")) try { if (Directory.Exists(saveDir)) Directory.Delete(saveDir, true); } catch { }
            Directory.CreateDirectory(saveDir);
            PlayerSettings.Folder = saveDir;
            Player.OpenLog(Path.Combine(outDir, "selftest.log"));
            var report = new StringBuilder();
            Action<string> say = s => { report.AppendLine(s); Console.WriteLine(s); Player.Log(s); };
            int frames = 300;
            int.TryParse(Cli.ArgValue("--frames") ?? "300", out frames);
            bool runAhead = !Cli.HasArg("--no-runahead");
            var script = ParseScript(Cli.ArgValue("--script") ?? DefaultScript);
            bool pass = true;
            try
            {
                Player.Settings = new PlayerSettings();
                var emu = new Emulator();
                emu.Log = Player.Log;
                emu.Notify = s => say("notice: " + s);
                Player.Emu = emu;
                string core = Emulator.FindCore(Player.Config, Cli.ArgValue("--core"));
                var sw = Stopwatch.StartNew();
                emu.Start(Player.Config, Player.Settings, core, Cli.ArgValue("--rom"), saveDir);
                say("game      : " + Player.Config.Title + " (" + Player.Config.RomFile + ")");
                say("core      : " + emu.Core.LibraryName + " " + emu.Core.LibraryVersion + "  [" + core + "]  need_fullpath=" + emu.Core.NeedFullpath);
                say("rom       : " + emu.RomLabel + ", " + emu.RomSize + " bytes");
                say("load      : " + sw.ElapsedMilliseconds + " ms");
                say("av info   : " + emu.Core.BaseWidth + "x" + emu.Core.BaseHeight + " @ " + emu.Core.Fps.ToString("0.0000", CultureInfo.InvariantCulture) + " fps, audio " + emu.Core.SampleRate.ToString("0.0", CultureInfo.InvariantCulture) + " Hz");
                say("save ram  : " + emu.SramSize + " bytes; serialize " + (emu.CanSerialize ? emu.Core.SerializeSize + " bytes" : "NO"));
                say("options   : " + emu.Core.OptionValues.Count + " core options declared");

                var audio = emu.Audio;
                audio.TargetFrames = 1536;
                var pull = new short[800 * 2];
                var wav = new List<short>(frames * 1700);
                long peak = 0, nonzero = 0, count = 0; double sumSq = 0;
                long underrunsAfterWarmup = 0, u0 = 0;
                double emuMsTotal = 0; double emuMsMax = 0;
                int shotEvery = 60;
                for (int f = 1; f <= frames; f++)
                {
                    int pad = 0;
                    foreach (var s in script) if (f >= s.From && f <= s.To) pad |= s.Mask;
                    var t0 = Stopwatch.GetTimestamp();
                    emu.RunFrame(pad, 0, runAhead, 60.0 / emu.Core.Fps);
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    emuMsTotal += ms; if (f > 10 && ms > emuMsMax) emuMsMax = ms;
                    var a = emu.Core.Audio;
                    for (int i = 0; i < emu.Core.AudioFrames * 2; i++)
                    {
                        int v = a[i]; if (v < 0) v = -v;
                        if (v > peak) peak = v;
                        if (v != 0) nonzero++;
                        sumSq += (double)a[i] * a[i]; count++;
                    }
                    // a 48 kHz device at a 60 Hz display pulls exactly 800 frames per tick
                    audio.Render(pull, 800);
                    wav.AddRange(pull);
                    if (f == 30) u0 = audio.Underruns;
                    if (f % shotEvery == 0 && emu.Core.Frame != null) SaveShot(emu, Path.Combine(outDir, "shot_" + f.ToString("0000") + ".png"));
                }
                underrunsAfterWarmup = audio.Underruns - u0;
                double secs = frames / emu.Core.Fps;
                say("frames    : " + frames + " run, " + emu.Core.FramesReceived + " video frames, last " + emu.Core.FrameW + "x" + emu.Core.FrameH + ", input polls " + emu.Core.PollCount);
                say("emulation : avg " + (emuMsTotal / frames).ToString("0.00", CultureInfo.InvariantCulture) + " ms/frame, max " + emuMsMax.ToString("0.00", CultureInfo.InvariantCulture) + " ms (run-ahead " + (runAhead && emu.CanRunAhead ? "ON via " + (emu.NativeRunAheadKey ?? "frontend save/load") : "OFF") + ")");
                say("audio in  : " + emu.Core.TotalAudioFrames + " frames = " + (emu.Core.TotalAudioFrames / secs).ToString("0", CultureInfo.InvariantCulture) + " Hz effective; peak " + peak + ", rms " + (count > 0 ? Math.Sqrt(sumSq / count) : 0).ToString("0.0", CultureInfo.InvariantCulture) + ", non-silent " + (count > 0 ? 100.0 * nonzero / count : 0).ToString("0.0", CultureInfo.InvariantCulture) + "%");
                say("audio out : " + audio.PushedOut + " frames at 48000 Hz, DRC x" + audio.Ratio.ToString("0.0000", CultureInfo.InvariantCulture) + ", ring fill " + audio.LastFill + "/" + audio.TargetFrames + ", underruns after warm-up " + underrunsAfterWarmup + ", dropped " + audio.Dropped);

                // run-ahead: from the same state with the same input, the run-ahead picture of tick N must equal the
                // plain picture of tick N+1 (one frame of the game's own lag removed)
                if (emu.CanRunAhead && emu.CanSerialize)
                {
                    var s0 = new byte[emu.Core.SerializeSize];
                    emu.Core.Serialize(s0);
                    const int n = 40;
                    var plain = new long[n]; var ra = new long[n];
                    for (int i = 0; i < n; i++) { emu.RunFrame(Btn.Right | Btn.B, 0, false, 1.0, false); plain[i] = FrameHash(emu); }
                    emu.Core.Unserialize(s0);
                    for (int i = 0; i < n; i++) { emu.RunFrame(Btn.Right | Btn.B, 0, true, 1.0, false); ra[i] = FrameHash(emu); }
                    emu.RunFrame(0, 0, false, 1.0, false);
                    emu.Core.Unserialize(s0);
                    int ahead = 0, behind = 0, checkedN = 0;
                    for (int i = 5; i < n - 1; i++)
                    {
                        if (plain[i] == plain[i + 1]) continue;   // ambiguous (static picture)
                        checkedN++;
                        if (ra[i] == plain[i + 1]) ahead++;
                        if (ra[i] == plain[i]) behind++;
                    }
                    say("run-ahead : " + (emu.NativeRunAheadKey != null ? "core option " + emu.NativeRunAheadKey : "frontend save/load") +
                        " - shows frame N+1 in " + ahead + "/" + checkedN + " moving frames (frame N in " + behind + ")");
                    if (checkedN > 0 && ahead * 2 < checkedN) { say("FAIL: run-ahead is not one frame ahead"); pass = false; }
                }

                // save-state round trip: the same inputs from the same state must give the same picture
                if (emu.CanSerialize)
                {
                    emu.RunFrame(0, 0, false, 1.0, false);   // settle any option change (run-ahead off) before saving
                    var st = new byte[emu.Core.SerializeSize];
                    emu.Core.Serialize(st);
                    var h1 = RunHashes(emu, 20, Btn.Right | Btn.B);
                    emu.Core.Unserialize(st);
                    var h2 = RunHashes(emu, 20, Btn.Right | Btn.B);
                    int bad = -1;
                    for (int i = 0; i < h1.Length; i++) if (h1[i] != h2[i]) { bad = i; break; }
                    say("state     : round trip " + (bad < 0 ? "OK" : "MISMATCH from frame " + bad) + " (hash " + h1[h1.Length - 1].ToString("X") + ")");
                    if (bad >= 0) pass = false;
                    emu.Core.Unserialize(st);
                }

                // SRAM persistence: write a test pattern into the game's save RAM (through a state), then check that it
                // reaches the save file on change and again after unload (when a core such as bsnes writes its own file)
                string srm = emu.SavePath;
                byte[] pattern = null;
                say("sram      : " + (emu.SramSize > 0 ? emu.SramSize + " bytes, via " + emu.SramInfo : "none (" + emu.SramInfo + ")"));
                if (emu.SramStateOffset >= 0 && emu.CanSerialize)
                {
                    var st = new byte[emu.Core.SerializeSize];
                    emu.Core.Serialize(st);
                    pattern = new byte[emu.SramSize];
                    for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 7 + 3);
                    Array.Copy(pattern, 0, st, emu.SramStateOffset, pattern.Length);
                    emu.Core.Unserialize(st);
                    emu.RunFrame(0, 0, false, 1.0, false);
                    emu.FlushSram(false);
                    bool okFlush = File.Exists(srm) && Same(File.ReadAllBytes(srm), pattern);
                    say("sram flush: change written to " + Path.GetFileName(srm) + " " + (okFlush ? "OK" : "FAILED"));
                    if (!okFlush) pass = false;
                }
                else emu.FlushSram(true);

                if (emu.Core.Frame != null) SaveShot(emu, Path.Combine(outDir, "selftest.png"));
                WriteWav(Path.Combine(outDir, "selftest.wav"), wav);
                if (emu.Core.FramesReceived == 0) { say("FAIL: no video frames"); pass = false; }
                if (emu.Core.TotalAudioFrames == 0) { say("FAIL: no audio"); pass = false; }
                emu.Dispose();
                if (pattern != null)
                {
                    bool okExit = File.Exists(srm) && Same(File.ReadAllBytes(srm), pattern);
                    say("sram exit : save file after unload " + (okExit ? "OK" : "DIFFERS") + " (" + (File.Exists(srm) ? new FileInfo(srm).Length : 0) + " bytes)");
                    if (!okExit) pass = false;
                }
            }
            catch (Exception ex) { say("FAIL: " + ex); pass = false; }
            say(pass ? "SELFTEST PASSED" : "SELFTEST FAILED");
            say("output    : " + outDir);
            try { File.WriteAllText(Path.Combine(outDir, "selftest.txt"), report.ToString()); } catch { }
            return pass ? 0 : 1;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static long[] RunHashes(Emulator emu, int n, int pad)
        {
            var r = new long[n];
            for (int i = 0; i < n; i++) { emu.RunFrame(pad, 0, false, 1.0, false); r[i] = FrameHash(emu); }
            return r;
        }

        static long FrameHash(Emulator emu)
        {
            long h = 1469598103934665603L;
            var f = emu.Core.Frame;
            for (int k = 0; k < emu.Core.FrameW * emu.Core.FrameH; k++) h = (h ^ f[k]) * 1099511628211L;
            return h;
        }

        static void SaveShot(Emulator emu, string path)
        {
            var c = emu.Core;
            int lw = c.FrameW > 300 ? c.FrameW / 2 : c.FrameW, lh = c.FrameH > 300 ? c.FrameH / 2 : c.FrameH;
            Png.Write(c.Frame, c.FrameW, c.FrameH, lw * 3, lh * 3, path);
        }

        struct Step { public int From, To, Mask; }

        static readonly Dictionary<string, int> Names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "UP", Btn.Up }, { "DOWN", Btn.Down }, { "LEFT", Btn.Left }, { "RIGHT", Btn.Right },
            { "B", Btn.A }, { "Y", Btn.B }, { "START", Btn.Start }, { "SELECT", Btn.Select },
            { "A", Btn.SnesA }, { "X", Btn.SnesX }, { "L", Btn.L }, { "R", Btn.R },
        };

        /// <summary>"from-to:BTN+BTN;..." with SNES button names (frames are 1-based, inclusive).</summary>
        static List<Step> ParseScript(string s)
        {
            var l = new List<Step>();
            foreach (var part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                var r = kv[0].Split('-');
                int a, b;
                if (!int.TryParse(r[0], out a)) continue;
                if (r.Length < 2 || !int.TryParse(r[1], out b)) b = a;
                int m = 0;
                foreach (var n in kv[1].Split('+')) { int v; if (Names.TryGetValue(n.Trim(), out v)) m |= v; }
                l.Add(new Step { From = a, To = b, Mask = m });
            }
            return l;
        }

        static void WriteWav(string path, List<short> samples)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Count * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
                w.Write(AudioOut.Rate); w.Write(AudioOut.Rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var s in samples) w.Write(s);
            }
        }
    }
}
