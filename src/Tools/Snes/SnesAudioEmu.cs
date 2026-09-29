using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMB4.Audio;

namespace SMB4.Tools
{
    /// <summary>
    /// Sound-agent tool commands:
    ///   snes-audio OUTDIR                 run only the audio export step (gen/music.inc, gen/snd_data.s, blobs)
    ///   snes-song NAME out.wav [sec]      emulate the ROM's sound hardware offline: the real SPC700 driver image runs on an
    ///                                     SPC700 + S-DSP emulator, the song is uploaded through the driver's own receive
    ///                                     protocol exactly as snd.s does it, and 32 kHz stereo is written + level stats.
    ///   snes-song sfx out.wav             every sound effect in turn (0.8 s apart), like `smb4tools song sfx`
    ///   snes-song NAME out.wav sec hurry  (also: pause, fade) exercise the tempo/pause/fade commands
    /// </summary>
    public static class SnesAudioTool
    {
        public static int Run(string[] args)
        {
            if (args[0].ToLowerInvariant() == "snes-audio")
            {
                string outDir = args.Length > 1 ? args[1] : "snes/build/gen";
                Directory.CreateDirectory(outDir);
                return SnesAudio.Export(outDir);
            }
            if (args.Length < 3) { Console.WriteLine("snes-song NAME|sfx out.wav [seconds] [hurry|pause|fade|stats]"); return 1; }
            if (!SnesAudio.Build(false)) { foreach (var w in SnesAudio.Warnings) Console.WriteLine(w); return 1; }
            SnesAudio.FinalizeForEmu();
            string name = args[1]; string wav = args[2];
            double sec = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 30;
            string mode = args.Length > 4 ? args[4].ToLowerInvariant() : "";
            var h = new ApuHost();
            if (!h.Boot()) { Console.WriteLine("driver did not start"); return 2; }
            bool sfxReel = name.Equals("sfx", StringComparison.OrdinalIgnoreCase);
            if (!sfxReel)
            {
                var s = SnesAudio.Songs.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (s == null) { Console.WriteLine("no song " + name); return 1; }
                int t0 = h.Apu.Cycles;
                if (!h.PlaySong(s)) { Console.WriteLine("upload failed"); return 2; }
                Console.WriteLine("upload " + s.Name + ": " + h.LastUploadBytes + " bytes in " + ((h.Apu.Cycles - t0) / 1024000.0).ToString("0.000") + " s of SPC time");
            }
            int total = (int)(sec * 32000);
            var pcm = new short[total * 2];
            h.Apu.LongestTick = 0; h.Apu.busy = 0; h.Apu.winCycles = 0;
            if (Environment.GetEnvironmentVariable("SND_PROF") != null) h.Apu.Prof = new long[0x10000];
            string dbg = Environment.GetEnvironmentVariable("SND_DEBUGPC"); if (dbg != null) h.Apu.DebugPc = SnesAudio.Syms[dbg];
            int reel = 0; long maxTick = 0; int ticks0 = h.Apu.Read16(0x4C);
            for (int i = 0; i < total; i++)
            {
                if (sfxReel && i % 25600 == 0 && reel < (int)SfxId.Count) h.Sfx(++reel);
                if (mode == "hurry" && i == 32000 * 3) h.Command(3, 1);
                if (mode == "pause" && i == 32000 * 3) h.Command(5, 1);
                if (mode == "pause" && i == 32000 * 5) h.Command(5, 0);
                if (mode == "fade" && i == 32000 * 3) h.Command(4, 120);
                if (mode.StartsWith("then:") && (i == 32000 * 3 || i == 32000 * 8))
                {
                    var s2 = SnesAudio.Songs.First(x => x.Name == (i == 32000 * 3 ? mode.Substring(5) : name));
                    int c0 = h.Apu.Cycles; h.PlaySong(s2);
                    Console.WriteLine("switch to " + s2.Name + ": uploaded " + h.LastUploadBytes + " bytes (" + ((h.Apu.Cycles - c0) / 1024000.0).ToString("0.000") + " s)");
                }
                int l, r; h.Apu.RunSample(out l, out r);
                if (Environment.GetEnvironmentVariable("SND_TRACE") != null && i % 320 == 0 && i >= 28000 && i < 36000) Console.WriteLine(string.Format("t={0:0.000} pc={1:X4} flg={2:X2} mvol={3} env={4} out={5} ticks={6} evol={7} eon={8:X2} efb={9} edl={10} esa={11:X2} ew={12}", i / 32000.0, h.Apu.Pc, h.Apu.Dsp.R[0x6C], h.Apu.Dsp.R[0x0C], string.Join(",", Enumerable.Range(0, 8).Select(k => h.Apu.Dsp.Read(k * 16 + 8))), l, h.Apu.Read16(0x4C), h.Apu.Dsp.R[0x2C], h.Apu.Dsp.R[0x4D], h.Apu.Dsp.R[0x0D], h.Apu.Dsp.R[0x7D], h.Apu.Dsp.R[0x6D], h.Apu.Ram[0x23]));
                pcm[i * 2] = (short)l; pcm[i * 2 + 1] = (short)r;
                maxTick = Math.Max(maxTick, h.Apu.LongestTick);
            }
            WriteWav(wav, pcm, 32000);
            Console.WriteLine(string.Format("driver load: avg {0:0}% of a frame, {1} of {2} frames over 70%", h.Apu.BusyTotal * 100.0 / Math.Max(1, h.Apu.Windows) / 17067, h.Apu.Over, h.Apu.Windows));
            if (h.Apu.Prof != null)
            {
                var syms = SnesAudio.Syms.Where(k => !k.Key.Contains("@")).OrderBy(k => k.Value).ToList();
                var agg = new Dictionary<string, long>();
                for (int a = 0; a < 0x10000; a++) if (h.Apu.Prof[a] > 0) { string nm = "?"; foreach (var kv in syms) { if (kv.Value <= a) nm = kv.Key; else break; } long o; agg.TryGetValue(nm, out o); agg[nm] = o + h.Apu.Prof[a]; }
                long tot = agg.Values.Sum();
                foreach (var kv in agg.OrderByDescending(k => k.Value).Take(15)) Console.WriteLine(string.Format("  {0,-16} {1,5:0.0}%", kv.Key, kv.Value * 100.0 / tot));
            }
            if (h.Apu.DebugPc >= 0) Console.WriteLine("debug pc hits per channel: " + string.Join(" ", h.Apu.DebugHits));
            int peak = 0; double sum = 0; int clip = 0;
            foreach (var v in pcm) { int a = Math.Abs((int)v); peak = Math.Max(peak, a); sum += (double)v * v; if (a >= 32767) clip++; }
            double rms = Math.Sqrt(sum / Math.Max(1, pcm.Length));
            Console.WriteLine(string.Format("wrote {0}: peak {1} ({2:0.0} dBFS), rms {3:0.0} dBFS, clipped samples {4}, driver ticks {5}, busiest frame {6} cycles ({7:0}% CPU)",
                wav, peak, 20 * Math.Log10(Math.Max(1, peak) / 32768.0), 20 * Math.Log10(Math.Max(1, rms) / 32768.0), clip, h.Apu.Read16(0x4C) - ticks0, maxTick, maxTick * 100.0 / 17067));
            if (mode == "stats" || mode == "compare")
            {
                // C# reference level for the same song
                var refPcm = Sound.RenderOffline(sfxReel ? "sfx" : name, sec);
                double rs = 0; int rp = 0; foreach (var v in refPcm) { rs += (double)v * v; rp = Math.Max(rp, Math.Abs((int)v)); }
                Console.WriteLine(string.Format("C# reference: peak {0:0.0} dBFS, rms {1:0.0} dBFS", 20 * Math.Log10(Math.Max(1, rp) / 32768.0), 20 * Math.Log10(Math.Max(1, Math.Sqrt(rs / Math.Max(1, refPcm.Length))) / 32768.0)));
                // loudness envelopes in 50 ms windows (the ROM starts ~1 frame later): correlation of dB curves
                int nw = (int)(sec * 20) - 1;
                var ea = new double[nw]; var eb = new double[nw];
                for (int w = 0; w < nw; w++)
                {
                    double sa = 0, sb = 0;
                    for (int k = 0; k < 1600; k++) { int i = w * 1600 + k; if (i * 2 + 1 < pcm.Length) sa += (double)pcm[i * 2] * pcm[i * 2] + (double)pcm[i * 2 + 1] * pcm[i * 2 + 1]; }
                    for (int k = 0; k < 2400; k++) { int i = w * 2400 + k; if (i * 2 + 1 < refPcm.Length) sb += (double)refPcm[i * 2] * refPcm[i * 2] + (double)refPcm[i * 2 + 1] * refPcm[i * 2 + 1]; }
                    ea[w] = 10 * Math.Log10(sa / 3200 + 1); eb[w] = 10 * Math.Log10(sb / 4800 + 1);
                }
                double best = -2; int lagBest = 0;
                for (int lag = -4; lag <= 4; lag++)
                {
                    double ma = 0, mb = 0; int n = 0;
                    for (int w = 4; w < nw - 4; w++) { ma += ea[w + lag]; mb += eb[w]; n++; }
                    ma /= n; mb /= n; double cab = 0, caa = 0, cbb = 0;
                    for (int w = 4; w < nw - 4; w++) { double x = ea[w + lag] - ma, y = eb[w] - mb; cab += x * y; caa += x * x; cbb += y * y; }
                    double cor = cab / Math.Sqrt(caa * cbb + 1e-9);
                    if (cor > best) { best = cor; lagBest = lag; }
                }
                if (!sfxReel)
                {
                    var sg = Sound.GetSong(name); var mp = new MusicPlayer(); mp.Play(sg); var trig = new int[8];
                    for (int f = 0; f < sec * 60; f++) { mp.Frame(); for (int c = 0; c < 8; c++) if (mp.Out[c].Trigger) trig[c] += Math.Max(1, mp.Out[c].Chord); }
                    Console.WriteLine("note-ons per channel  C#: " + string.Join(" ", trig) + "   ROM voices: " + string.Join(" ", h.Apu.Dsp.KonCount));
                }
                Console.WriteLine(string.Format("loudness-envelope correlation ROM vs C#: {0:0.000} (lag {1} x 50 ms)", best, lagBest));
                if (Environment.GetEnvironmentVariable("SND_ENV") != null) for (int w = 0; w < Math.Min(nw, 120); w++) Console.WriteLine(string.Format("{0,5:0.00}s  rom {1,5:0.0}  c# {2,5:0.0}  {3}", w * 0.05, ea[w], eb[w], new string('#', (int)Math.Max(0, ea[w] - 40)) + "|" + new string('*', (int)Math.Max(0, eb[w] - 40))));
            }
            return 0;
        }

        public static void WriteWav(string path, short[] pcm, int rate)
        {
            using (var f = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(f))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(pcm.Length * 2);
                foreach (var s in pcm) w.Write(s);
            }
        }
    }

    /// <summary>The 65816 side of the sound protocol (mirrors snes/src/snd.s) driving an emulated APU.</summary>
    public sealed class ApuHost
    {
        public readonly Spc700 Apu = new Spc700();
        readonly bool[] resident = new bool[256];
        int lastCmd;
        readonly int[] sfxLast = new int[2];
        public int LastUploadBytes;

        public bool Boot()
        {
            // IPL upload is emulated by placing the image; the driver then starts at SND_ENTRY with the IPL's port values
            Array.Copy(SnesAudio.Image, 0, Apu.Ram, SnesAudio.DriverOrg, SnesAudio.Image.Length);
            Apu.In[0] = 0x55; Apu.Out[0] = 0x55; Apu.Out[1] = 0xBB;
            Apu.Pc = SnesAudio.Syms["start"];
            Apu.In[0] = Apu.In[1] = Apu.In[2] = Apu.In[3] = 0;
            return Apu.RunUntil(() => Apu.Out[1] == 0x80 && Apu.Out[0] == 0, 200000);
        }

        public bool Command(int cmd, int arg)
        {
            int v = cmd | ((lastCmd & 0x80) ^ 0x80);
            Apu.In[1] = (byte)arg; Apu.In[0] = (byte)v;
            lastCmd = v;
            return Apu.RunUntil(() => Apu.Out[0] == v, 200000);
        }

        public void Sfx(int id)
        {
            for (int p = 0; p < 2; p++)
                if (Apu.Out[2 + p] == sfxLast[p]) { int v = id | ((sfxLast[p] & 0x80) ^ 0x80); Apu.In[2 + p] = (byte)v; sfxLast[p] = v; return; }
        }

        int hk;
        bool Header(int type, int addr)
        {
            Apu.In[1] = (byte)type; Apu.In[2] = (byte)(addr & 255); Apu.In[3] = (byte)(addr >> 8); Apu.In[0] = (byte)hk;
            int want = hk; hk ^= 2;
            lastCmd = want;
            return Apu.RunUntil(() => Apu.Out[0] == want, 200000);
        }

        bool Block(int addr, byte[] data)
        {
            if (!Header(1, addr)) return false;
            for (int i = 0; i < data.Length; i += 2)
            {
                Apu.In[1] = data[i]; Apu.In[2] = i + 1 < data.Length ? data[i + 1] : (byte)0; Apu.In[0] = (byte)(i & 255);
                int want = i & 255;
                if (!Apu.RunUntil(() => Apu.Out[0] == want, 20000)) return false;
            }
            Apu.In[0] = 0xFF;
            LastUploadBytes += data.Length;
            return Apu.RunUntil(() => Apu.Out[0] == 0xFF, 20000);
        }

        public bool PlaySong(SSong s)
        {
            LastUploadBytes = 0;
            if (!Command(6, 0)) return false;
            hk = 0x41;
            int seqEnd = 0xFFFF;
            var dyn = SnesAudio.Samples.Where(x => !x.Resident).ToList();
            foreach (var x in dyn) if (resident[x.DynIndex] && x.Addr + x.Size - 1 >= s.Addr && x.Addr <= seqEnd) resident[x.DynIndex] = false;
            foreach (var x in s.UploadList)
            {
                if (resident[x.DynIndex]) continue;
                var bb = x.Brr.Length % 2 == 0 ? x.Brr : x.Brr.Concat(new byte[] { 0 }).ToArray();
                if (!Block(x.Addr, bb)) return false;
                foreach (var o in dyn) if (o != x && resident[o.DynIndex] && o.Addr <= x.Addr + x.Size - 1 && x.Addr <= o.Addr + o.Size - 1) resident[o.DynIndex] = false;
                resident[x.DynIndex] = true;
            }
            if (!Block(s.Addr, s.Blob)) return false;
            if (!Header(0, s.Addr)) return false;
            Apu.In[2] = 0; Apu.In[3] = 0; sfxLast[0] = sfxLast[1] = 0;
            return Apu.RunUntil(() => Apu.Out[2] == 0 && Apu.Out[3] == 0 && Apu.Out[1] == 0x81, 400000);
        }
    }

    /// <summary>SPC700 CPU + S-DSP emulator (enough fidelity for level/timing checks of our own driver).</summary>
    public sealed class Spc700
    {
        public readonly byte[] Ram = new byte[0x10000];
        public readonly byte[] In = new byte[4], Out = new byte[4];
        public int A, X, Y, Sp = 0xEF, Pc; bool N, V, P, B, H, I, Z, C;
        public int Cycles;
        int control, dspAddr;
        readonly int[] tTarget = new int[3], tStage = new int[3], tOut = new int[3];
        int tDiv8, tDiv64;
        public readonly Dsp Dsp;
        int sampleCycles;
        public long LongestTick; int tickStartCycle = -1;
        public long[] Prof;
        public int DebugPc = -1; public readonly int[] DebugHits = new int[8];

        public Spc700() { Dsp = new Dsp(Ram); }

        public int Read16(int a) { return Ram[a] | (Ram[a + 1] << 8); }

        int Rd(int a)
        {
            a &= 0xFFFF;
            if (a >= 0xF0 && a <= 0xFF)
            {
                switch (a)
                {
                    case 0xF2: return dspAddr;
                    case 0xF3: return Dsp.Read(dspAddr & 0x7F);
                    case 0xF4: case 0xF5: case 0xF6: case 0xF7: return In[a - 0xF4];
                    case 0xFD: case 0xFE: case 0xFF: { int t = a - 0xFD; int v = tOut[t]; tOut[t] = 0; if (t == 0 && v > 0) tickStartCycle = Cycles; return v; }
                    default: return Ram[a];
                }
            }
            return Ram[a];
        }
        void Wr(int a, int v)
        {
            a &= 0xFFFF; v &= 0xFF;
            if (a >= 0xF0 && a <= 0xFF)
            {
                switch (a)
                {
                    case 0xF1:
                        for (int t = 0; t < 3; t++) if ((v & (1 << t)) != 0 && (control & (1 << t)) == 0) { tStage[t] = 0; tOut[t] = 0; }
                        if ((v & 0x10) != 0) { In[0] = 0; In[1] = 0; }
                        if ((v & 0x20) != 0) { In[2] = 0; In[3] = 0; }
                        control = v; return;
                    case 0xF2: dspAddr = v; return;
                    case 0xF3: if (dspAddr < 0x80) Dsp.Write(dspAddr, v); return;
                    case 0xF4: case 0xF5: case 0xF6: case 0xF7: Out[a - 0xF4] = (byte)v; return;
                    case 0xFA: case 0xFB: case 0xFC: tTarget[a - 0xFA] = v; return;
                }
            }
            Ram[a] = (byte)v;
        }

        int Dp(int d) { return (P ? 0x100 : 0) | (d & 0xFF); }
        int F8() { int v = Ram[Pc]; Pc = (Pc + 1) & 0xFFFF; return v; }
        int F16() { int lo = F8(); return lo | (F8() << 8); }
        int RdW(int d) { return Rd(Dp(d)) | (Rd(Dp(d + 1)) << 8); }
        void Push(int v) { Ram[0x100 | Sp] = (byte)v; Sp = (Sp - 1) & 0xFF; }
        int Pop() { Sp = (Sp + 1) & 0xFF; return Ram[0x100 | Sp]; }
        void NZ(int v) { N = (v & 0x80) != 0; Z = (v & 0xFF) == 0; }
        int Psw() { return (N ? 0x80 : 0) | (V ? 0x40 : 0) | (P ? 0x20 : 0) | (B ? 0x10 : 0) | (H ? 8 : 0) | (I ? 4 : 0) | (Z ? 2 : 0) | (C ? 1 : 0); }
        void SetPsw(int v) { N = (v & 0x80) != 0; V = (v & 0x40) != 0; P = (v & 0x20) != 0; B = (v & 0x10) != 0; H = (v & 8) != 0; I = (v & 4) != 0; Z = (v & 2) != 0; C = (v & 1) != 0; }

        int Adc(int a, int b)
        {
            int r = a + b + (C ? 1 : 0);
            V = (~(a ^ b) & (a ^ r) & 0x80) != 0;
            H = ((a ^ b ^ r) & 0x10) != 0;
            C = r > 0xFF; NZ(r); return r & 0xFF;
        }
        int Sbc(int a, int b) { return Adc(a, b ^ 0xFF); }
        void Cmp(int a, int b) { int r = a - b; C = r >= 0; NZ(r); }
        int Alu(int op, int a, int b)
        {
            switch (op)
            {
                case 0: a |= b; NZ(a); return a;
                case 1: a &= b; NZ(a); return a;
                case 2: a ^= b; NZ(a); return a;
                case 3: Cmp(a, b); return a;
                case 4: return Adc(a, b);
                default: return Sbc(a, b);
            }
        }

        static readonly byte[] Cyc = {
            2,8,4,5,3,4,3,6,2,6,5,4,5,4,6,8, 2,8,4,5,4,5,5,6,5,5,6,5,2,2,4,6,
            2,8,4,5,3,4,3,6,2,6,5,4,5,4,5,4, 2,8,4,5,4,5,5,6,5,5,6,5,2,2,3,8,
            2,8,4,5,3,4,3,6,2,6,4,4,5,4,6,6, 2,8,4,5,4,5,5,6,5,5,4,5,2,2,4,3,
            2,8,4,5,3,4,3,6,2,6,4,4,5,4,5,5, 2,8,4,5,4,5,5,6,5,5,5,5,2,2,3,6,
            2,8,4,5,3,4,3,6,2,6,5,4,5,2,4,5, 2,8,4,5,4,5,5,6,5,5,5,5,2,2,12,5,
            3,8,4,5,3,4,3,6,2,6,4,4,5,2,4,4, 2,8,4,5,4,5,5,6,5,5,5,5,2,2,3,4,
            3,8,4,5,4,5,4,7,2,5,6,4,5,2,4,9, 2,8,4,5,5,6,6,7,4,5,5,5,2,2,6,3,
            2,8,4,5,3,4,3,6,2,4,5,3,4,3,4,3, 2,8,4,5,4,5,5,6,3,4,5,4,2,2,4,3 };

        void Branch(bool cond) { int r = (sbyte)F8(); if (cond) { Pc = (Pc + r) & 0xFFFF; Cycles += 2; } }

        public int Step()
        {
            int op = F8();
            int c0 = Cycles;
            Cycles += Cyc[op];
            int lo = op & 0x0F, hi = op >> 4;
            if (hi <= 0xB && lo >= 4 && lo <= 9)
            {
                int alu = hi >> 1; bool odd = (hi & 1) != 0;
                int addr, v;
                switch (lo)
                {
                    case 4: addr = odd ? Dp(F8() + X) : Dp(F8()); A = Alu(alu, A, Rd(addr)); break;
                    case 5: addr = F16(); if (odd) addr = (addr + X) & 0xFFFF; A = Alu(alu, A, Rd(addr)); break;
                    case 6: if (odd) addr = (F16() + Y) & 0xFFFF; else addr = Dp(X); A = Alu(alu, A, Rd(addr)); break;
                    case 7:
                        if (odd) { int d = F8(); addr = (RdW(d) + Y) & 0xFFFF; }
                        else { int d = F8(); addr = RdW((d + X) & 0xFF); }
                        A = Alu(alu, A, Rd(addr)); break;
                    case 8:
                        if (!odd) { A = Alu(alu, A, F8()); break; }
                        v = F8(); addr = Dp(F8());
                        { int r = Alu(alu, Rd(addr), v); if (alu != 3) Wr(addr, r); }
                        break;
                    default:
                        if (!odd) { int s = Rd(Dp(F8())); addr = Dp(F8()); int r = Alu(alu, Rd(addr), s); if (alu != 3) Wr(addr, r); }
                        else { int s = Rd(Dp(Y)); addr = Dp(X); int r = Alu(alu, Rd(addr), s); if (alu != 3) Wr(addr, r); }
                        break;
                }
                return Cycles - c0;
            }
            if (lo == 1) { int n = hi; Push(Pc >> 8); Push(Pc & 255); Pc = Read16(0xFFDE - n * 2); return Cycles - c0; }
            if (lo == 2) { int d = Dp(F8()); int bit = 1 << (hi >> 1); int v = Rd(d); Wr(d, (hi & 1) == 0 ? v | bit : v & ~bit); return Cycles - c0; }
            if (lo == 3) { int d = Dp(F8()); int bit = 1 << (hi >> 1); bool set = (Rd(d) & bit) != 0; Branch((hi & 1) == 0 ? set : !set); return Cycles - c0; }
            switch (op)
            {
                case 0x00: break;
                case 0x10: Branch(!N); break;
                case 0x30: Branch(N); break;
                case 0x50: Branch(!V); break;
                case 0x70: Branch(V); break;
                case 0x90: Branch(!C); break;
                case 0xB0: Branch(C); break;
                case 0xD0: Branch(!Z); break;
                case 0xF0: Branch(Z); break;
                case 0x2F: Branch(true); break;
                case 0x20: P = false; break;
                case 0x40: P = true; break;
                case 0x60: C = false; break;
                case 0x80: C = true; break;
                case 0xED: C = !C; break;
                case 0xE0: V = false; H = false; break;
                case 0xA0: I = true; break;
                case 0xC0: I = false; break;
                // compare X/Y
                case 0xC8: Cmp(X, F8()); break;
                case 0x3E: Cmp(X, Rd(Dp(F8()))); break;
                case 0x1E: Cmp(X, Rd(F16())); break;
                case 0xAD: Cmp(Y, F8()); break;
                case 0x7E: Cmp(Y, Rd(Dp(F8()))); break;
                case 0x5E: Cmp(Y, Rd(F16())); break;
                // mov loads
                case 0xE8: A = F8(); NZ(A); break;
                case 0xE6: A = Rd(Dp(X)); NZ(A); break;
                case 0xBF: A = Rd(Dp(X)); X = (X + 1) & 255; NZ(A); break;
                case 0xE4: A = Rd(Dp(F8())); NZ(A); break;
                case 0xF4: A = Rd(Dp(F8() + X)); NZ(A); break;
                case 0xE5: A = Rd(F16()); NZ(A); break;
                case 0xF5: A = Rd((F16() + X) & 0xFFFF); NZ(A); break;
                case 0xF6: A = Rd((F16() + Y) & 0xFFFF); NZ(A); break;
                case 0xE7: { int d = F8(); A = Rd(RdW((d + X) & 0xFF)); NZ(A); break; }
                case 0xF7: { int d = F8(); A = Rd((RdW(d) + Y) & 0xFFFF); NZ(A); break; }
                case 0xCD: X = F8(); NZ(X); break;
                case 0xF8: X = Rd(Dp(F8())); NZ(X); break;
                case 0xF9: X = Rd(Dp(F8() + Y)); NZ(X); break;
                case 0xE9: X = Rd(F16()); NZ(X); break;
                case 0x8D: Y = F8(); NZ(Y); break;
                case 0xEB: Y = Rd(Dp(F8())); NZ(Y); break;
                case 0xFB: Y = Rd(Dp(F8() + X)); NZ(Y); break;
                case 0xEC: Y = Rd(F16()); NZ(Y); break;
                // mov stores
                case 0xC6: Wr(Dp(X), A); break;
                case 0xAF: Wr(Dp(X), A); X = (X + 1) & 255; break;
                case 0xC4: Wr(Dp(F8()), A); break;
                case 0xD4: Wr(Dp(F8() + X), A); break;
                case 0xC5: Wr(F16(), A); break;
                case 0xD5: Wr((F16() + X) & 0xFFFF, A); break;
                case 0xD6: Wr((F16() + Y) & 0xFFFF, A); break;
                case 0xC7: { int d = F8(); Wr(RdW((d + X) & 0xFF), A); break; }
                case 0xD7: { int d = F8(); Wr((RdW(d) + Y) & 0xFFFF, A); break; }
                case 0xD8: Wr(Dp(F8()), X); break;
                case 0xD9: Wr(Dp(F8() + Y), X); break;
                case 0xC9: Wr(F16(), X); break;
                case 0xCB: Wr(Dp(F8()), Y); break;
                case 0xDB: Wr(Dp(F8() + X), Y); break;
                case 0xCC: Wr(F16(), Y); break;
                case 0x7D: A = X; NZ(A); break;
                case 0xDD: A = Y; NZ(A); break;
                case 0x5D: X = A; NZ(X); break;
                case 0xFD: Y = A; NZ(Y); break;
                case 0x9D: X = Sp; NZ(X); break;
                case 0xBD: Sp = X; break;
                case 0xFA: { int s = Rd(Dp(F8())); Wr(Dp(F8()), s); break; }
                case 0x8F: { int v = F8(); Wr(Dp(F8()), v); break; }
                // 16-bit
                case 0xBA: { int d = F8(); A = Rd(Dp(d)); Y = Rd(Dp(d + 1)); N = (Y & 0x80) != 0; Z = A == 0 && Y == 0; break; }
                case 0xDA: { int d = F8(); Wr(Dp(d), A); Wr(Dp(d + 1), Y); break; }
                case 0x3A: case 0x1A:
                    {
                        int d = F8(); int w = RdW(d); w = (w + (op == 0x3A ? 1 : -1)) & 0xFFFF;
                        Wr(Dp(d), w & 255); Wr(Dp(d + 1), w >> 8); N = (w & 0x8000) != 0; Z = w == 0; break;
                    }
                case 0x7A: case 0x9A: case 0x5A:
                    {
                        int d = F8(); int w = RdW(d); int ya = (Y << 8) | A;
                        if (op == 0x5A) { int r = ya - w; C = r >= 0; N = (r & 0x8000) != 0; Z = (r & 0xFFFF) == 0; break; }
                        int res;
                        if (op == 0x7A) { res = ya + w; C = res > 0xFFFF; V = (~(ya ^ w) & (ya ^ res) & 0x8000) != 0; H = ((ya ^ w ^ res) & 0x1000) != 0; }
                        else { res = ya - w; C = res >= 0; V = ((ya ^ w) & (ya ^ res) & 0x8000) != 0; H = ((ya ^ w ^ res) & 0x1000) == 0; }
                        res &= 0xFFFF; A = res & 255; Y = res >> 8; N = (res & 0x8000) != 0; Z = res == 0; break;
                    }
                // inc/dec
                case 0xBC: A = (A + 1) & 255; NZ(A); break;
                case 0x3D: X = (X + 1) & 255; NZ(X); break;
                case 0xFC: Y = (Y + 1) & 255; NZ(Y); break;
                case 0x9C: A = (A - 1) & 255; NZ(A); break;
                case 0x1D: X = (X - 1) & 255; NZ(X); break;
                case 0xDC: Y = (Y - 1) & 255; NZ(Y); break;
                case 0xAB: case 0xBB: case 0xAC: case 0x8B: case 0x9B: case 0x8C:
                    {
                        int addr = (op == 0xAB || op == 0x8B) ? Dp(F8()) : (op == 0xBB || op == 0x9B) ? Dp(F8() + X) : F16();
                        int v = (Rd(addr) + (op >= 0xAB ? 1 : -1)) & 255; Wr(addr, v); NZ(v); break;
                    }
                // shifts
                case 0x1C: C = (A & 0x80) != 0; A = (A << 1) & 255; NZ(A); break;
                case 0x5C: C = (A & 1) != 0; A >>= 1; NZ(A); break;
                case 0x3C: { int c = C ? 1 : 0; C = (A & 0x80) != 0; A = ((A << 1) | c) & 255; NZ(A); break; }
                case 0x7C: { int c = C ? 0x80 : 0; C = (A & 1) != 0; A = (A >> 1) | c; NZ(A); break; }
                case 0x0B: case 0x1B: case 0x0C: case 0x4B: case 0x5B: case 0x4C: case 0x2B: case 0x3B: case 0x2C: case 0x6B: case 0x7B: case 0x6C:
                    {
                        int l2 = op & 0x0F, h2 = op >> 4;
                        int addr = l2 == 0xC ? F16() : ((h2 & 1) != 0 ? Dp(F8() + X) : Dp(F8()));
                        int v = Rd(addr); int kind = h2 >> 1; int c = C ? 1 : 0;
                        switch (kind)
                        {
                            case 0: C = (v & 0x80) != 0; v = (v << 1) & 255; break;
                            case 1: C = (v & 0x80) != 0; v = ((v << 1) | c) & 255; break;
                            case 2: C = (v & 1) != 0; v >>= 1; break;
                            default: C = (v & 1) != 0; v = (v >> 1) | (c << 7); break;
                        }
                        Wr(addr, v); NZ(v); break;
                    }
                case 0x9F: A = ((A >> 4) | (A << 4)) & 255; NZ(A); break;
                case 0xCF: { int r = Y * A; A = r & 255; Y = r >> 8; NZ(Y); break; }
                case 0x9E:
                    {
                        int ya = (Y << 8) | A;
                        H = (Y & 15) >= (X & 15); V = Y >= X;
                        if (Y < (X << 1)) { A = X == 0 ? 255 : ya / X; Y = X == 0 ? A : ya % X; }
                        else { A = 255 - (ya - (X << 9)) / (256 - X); Y = X + (ya - (X << 9)) % (256 - X); }
                        A &= 255; Y &= 255; NZ(A); break;
                    }
                case 0xDF: if (C || A > 0x99) { A += 0x60; C = true; } if (H || (A & 15) > 9) A += 6; A &= 255; NZ(A); break;
                case 0xBE: if (!C || A > 0x99) { A -= 0x60; C = false; } if (!H || (A & 15) > 9) A -= 6; A &= 255; NZ(A); break;
                // branches with operands
                case 0x2E: { int v = Rd(Dp(F8())); Branch(A != v); break; }
                case 0xDE: { int v = Rd(Dp(F8() + X)); Branch(A != v); break; }
                case 0x6E: { int d = Dp(F8()); int v = (Rd(d) - 1) & 255; Wr(d, v); Branch(v != 0); break; }
                case 0xFE: Y = (Y - 1) & 255; Branch(Y != 0); break;
                case 0x5F: Pc = F16(); break;
                case 0x1F: { int a = (F16() + X) & 0xFFFF; Pc = Read16(a); break; }
                case 0x3F: { int a = F16(); Push(Pc >> 8); Push(Pc & 255); Pc = a; break; }
                case 0x4F: { int u = F8(); Push(Pc >> 8); Push(Pc & 255); Pc = 0xFF00 | u; break; }
                case 0x0F: Push(Pc >> 8); Push(Pc & 255); Push(Psw()); B = true; I = false; Pc = Read16(0xFFDE); break;
                case 0x6F: { int l = Pop(); Pc = l | (Pop() << 8); break; }
                case 0x7F: { SetPsw(Pop()); int l = Pop(); Pc = l | (Pop() << 8); break; }
                case 0x2D: Push(A); break;
                case 0x4D: Push(X); break;
                case 0x6D: Push(Y); break;
                case 0x0D: Push(Psw()); break;
                case 0xAE: A = Pop(); break;
                case 0xCE: X = Pop(); break;
                case 0xEE: Y = Pop(); break;
                case 0x8E: SetPsw(Pop()); break;
                case 0x0E: case 0x4E: { int a = F16(); int v = Rd(a); NZ((A - v) & 255); Wr(a, op == 0x0E ? v | A : v & ~A); break; }
                case 0x4A: case 0x6A: case 0x0A: case 0x2A: case 0x8A: case 0xEA: case 0xAA: case 0xCA:
                    {
                        int w = F16(); int a = w & 0x1FFF, bit = w >> 13; bool m = (Rd(a) & (1 << bit)) != 0;
                        switch (op)
                        {
                            case 0x4A: C = C && m; break;
                            case 0x6A: C = C && !m; break;
                            case 0x0A: C = C || m; break;
                            case 0x2A: C = C || !m; break;
                            case 0x8A: C = C ^ m; break;
                            case 0xEA: Wr(a, Rd(a) ^ (1 << bit)); break;
                            case 0xAA: C = m; break;
                            case 0xCA: Wr(a, C ? Rd(a) | (1 << bit) : Rd(a) & ~(1 << bit)); break;
                        }
                        break;
                    }
                case 0xEF: case 0xFF: throw new Exception("SPC700 halted (SLEEP/STOP) at $" + ((Pc - 1) & 0xFFFF).ToString("X4"));
                default: throw new Exception("SPC700: unhandled opcode $" + op.ToString("X2") + " at $" + ((Pc - 1) & 0xFFFF).ToString("X4"));
            }
            return Cycles - c0;
        }

        void Clock(int cyc)
        {
            // timers: T0/T1 at 8 kHz (every 128 cycles), T2 at 64 kHz (every 16)
            tDiv8 += cyc; tDiv64 += cyc;
            while (tDiv64 >= 16)
            {
                tDiv64 -= 16;
                if ((control & 4) != 0) { tStage[2]++; if (tStage[2] >= (tTarget[2] == 0 ? 256 : tTarget[2])) { tStage[2] = 0; tOut[2] = (tOut[2] + 1) & 15; } }
            }
            while (tDiv8 >= 128)
            {
                tDiv8 -= 128;
                for (int t = 0; t < 2; t++)
                    if ((control & (1 << t)) != 0) { tStage[t]++; if (tStage[t] >= (tTarget[t] == 0 ? 256 : tTarget[t])) { tStage[t] = 0; tOut[t] = (tOut[t] + 1) & 15; } }
            }
        }

        int pendingL, pendingR; bool havePending;
        /// <summary>Runs until the next 32 kHz output sample.</summary>
        public void RunSample(out int l, out int r)
        {
            while (sampleCycles < 32)
            {
                int c = Step(); Clock(c); sampleCycles += c;
                if (DebugPc >= 0 && Pc == DebugPc) DebugHits[Ram[0x30] & 7]++;
                if (Prof != null) Prof[Pc] += c;
                // measure driver tick length: from the T0OUT read that found ticks until the main loop polls again with none
                if (Pc < mainLoop || Pc >= mainEnd) busy += c;
                winCycles += c;
                if (winCycles >= 17067) { LongestTick = Math.Max(LongestTick, busy); BusyTotal += busy; Windows++; if (busy > 12000) Over++; busy = 0; winCycles -= 17067; }
            }
            sampleCycles -= 32;
            Dsp.Sample(out l, out r);
        }
        int mainLoop = -1, mainEnd = -1; public int busy, winCycles; public long BusyTotal, Windows, Over;

        public bool RunUntil(Func<bool> cond, int maxSamples)
        {
            if (mainLoop < 0 && SnesAudio.Syms != null) { mainLoop = SnesAudio.Syms["main"]; mainEnd = SnesAudio.Syms["command"]; }
            for (int i = 0; i < maxSamples; i++)
            {
                if (cond()) return true;
                int l, r; RunSample(out l, out r);
            }
            return cond();
        }
    }

    /// <summary>S-DSP: BRR voices with Gaussian-style interpolation, ADSR/GAIN envelopes, noise, echo with FIR.</summary>
    public sealed class Dsp
    {
        readonly byte[] ram;
        public readonly byte[] R = new byte[128];
        sealed class Vc
        {
            public int Addr, Hdr, BlockPos, Pos; public int[] Buf = new int[12]; public int Wr, Rdp;
            public int Env, Mode, KonDelay; public bool On; public int Hidden;
            public int P1, P2;
        }
        readonly Vc[] v = new Vc[8];
        public readonly int[] KonCount = new int[8];
        int counter;
        int noise = 0x4000;
        int echoOff, echoLen;
        readonly int[,] hist = new int[8, 2]; int histPos;
        static readonly int[] Gauss = BuildGauss();
        const int Release = 0, Attack = 1, Decay = 2, Sustain = 3;

        public Dsp(byte[] ram) { this.ram = ram; for (int i = 0; i < 8; i++) v[i] = new Vc(); R[0x6C] = 0xE0; }

        static int[] BuildGauss()
        {
            // 4-tap Gaussian-like kernel, 256 phases; weights sum to 2048 (approximates the S-DSP table)
            var g = new int[256 * 4];
            for (int p = 0; p < 256; p++)
            {
                double f = p / 256.0; double[] w = new double[4]; double s = 0;
                for (int k = 0; k < 4; k++) { double d = (k - 1) - f; w[k] = Math.Exp(-d * d / (2 * 0.52 * 0.52)); s += w[k]; }
                for (int k = 0; k < 4; k++) g[p * 4 + k] = (int)Math.Round(w[k] / s * 2048);
            }
            return g;
        }

        public int Read(int a)
        {
            if ((a & 0xF) == 8) return v[a >> 4].Env >> 4;
            return R[a];
        }
        public void Write(int a, int val)
        {
            R[a] = (byte)val;
            if (a == 0x4C) for (int i = 0; i < 8; i++) if ((val & (1 << i)) != 0) { KeyOn(i); KonCount[i]++; }
            if (a == 0x5C) for (int i = 0; i < 8; i++) if ((val & (1 << i)) != 0) v[i].Mode = Release;
            if (a == 0x7C) R[0x7C] = 0;
        }

        void KeyOn(int i)
        {
            var x = v[i];
            int dir = R[0x5D] << 8; int srcn = R[i * 16 + 4];
            x.Addr = ram[(dir + srcn * 4) & 0xFFFF] | (ram[(dir + srcn * 4 + 1) & 0xFFFF] << 8);
            x.BlockPos = 0; x.Pos = 0; x.Env = 0; x.Mode = Attack; x.KonDelay = 5; x.On = true; x.P1 = x.P2 = 0;
            x.Hdr = ram[x.Addr]; x.Wr = 0; x.Rdp = 0;
            for (int k = 0; k < 12; k++) x.Buf[k] = 0;
            R[0x7C] &= (byte)~(1 << i);
            DecodeBlockHalf(i); // prime 4 samples
            DecodeBlockHalf(i);
            DecodeBlockHalf(i);
        }

        // decode 4 samples into the ring buffer
        void DecodeBlockHalf(int i)
        {
            var x = v[i];
            int hdr = ram[x.Addr]; int shift = hdr >> 4, filter = (hdr >> 2) & 3;
            for (int k = 0; k < 4; k++)
            {
                int bi = x.BlockPos + k;
                int byt = ram[(x.Addr + 1 + (bi >> 1)) & 0xFFFF];
                int nyb = (bi & 1) == 0 ? byt >> 4 : byt & 15; if (nyb >= 8) nyb -= 16;
                int pred = SnesAudio.BrrPredict(filter, x.P1, x.P2);
                int o = SnesAudio.BrrOut(pred, nyb, shift);
                x.P2 = x.P1; x.P1 = o;
                x.Buf[x.Wr % 12] = o; x.Wr++;
            }
            x.BlockPos += 4;
            if (x.BlockPos >= 16)
            {
                x.BlockPos = 0;
                if ((hdr & 1) != 0)
                {
                    R[0x7C] |= (byte)(1 << i);
                    if ((hdr & 2) != 0) { int dir = R[0x5D] << 8; int srcn = R[i * 16 + 4]; x.Addr = ram[(dir + srcn * 4 + 2) & 0xFFFF] | (ram[(dir + srcn * 4 + 3) & 0xFFFF] << 8); }
                    else { x.Mode = Release; x.Env = 0; x.On = false; }
                }
                else x.Addr = (x.Addr + 9) & 0xFFFF;
            }
        }

        static readonly int[] Period = SnesAudio.RatePeriod;
        bool Tick(int rate) { if (rate == 0) return false; return (counter % Period[rate]) == 0; }

        void Envelope(int i)
        {
            var x = v[i]; int b = i * 16;
            int env = x.Env;
            if (x.Mode == Release) { env -= 8; if (env < 0) env = 0; x.Env = env; return; }
            int adsr0 = R[b + 5], adsr1 = R[b + 6], rate;
            if ((adsr0 & 0x80) != 0)
            {
                if (x.Mode >= Decay)
                {
                    env--; env -= env >> 8;
                    rate = adsr1 & 0x1F;
                    if (x.Mode == Decay) rate = ((adsr0 >> 3) & 0x0E) + 0x10;
                }
                else { rate = (adsr0 & 0x0F) * 2 + 1; env += rate < 31 ? 0x20 : 0x400; }
            }
            else
            {
                int g = R[b + 7]; int mode = g >> 5;
                if (mode < 4) { env = g * 0x10; rate = 31; }
                else
                {
                    rate = g & 0x1F;
                    if (mode == 4) env -= 0x20;
                    else if (mode < 6) { env--; env -= env >> 8; }
                    else { env += 0x20; if (mode > 6 && x.Hidden >= 0x600) env += 0x8 - 0x20; }
                }
            }
            if ((env >> 8) == (adsr1 >> 5) && x.Mode == Decay) x.Mode = Sustain;
            x.Hidden = env;
            if (env < 0 || env > 0x7FF) { env = env < 0 ? 0 : 0x7FF; if (x.Mode == Attack) x.Mode = Decay; }
            if (Tick(rate)) x.Env = env;
        }

        public void Sample(out int outL, out int outR)
        {
            counter = (counter + 1) % 30720;
            int flg = R[0x6C];
            // noise
            int nrate = flg & 0x1F;
            if (Tick(nrate)) { int fb = (noise << 13) ^ (noise << 14); noise = (fb & 0x4000) ^ (noise >> 1); }
            int mainL = 0, mainR = 0, echoL = 0, echoR = 0;
            int non = R[0x3D], eon = R[0x4D];
            for (int i = 0; i < 8; i++)
            {
                var x = v[i]; int b = i * 16;
                if (x.KonDelay > 0) { x.KonDelay--; continue; }
                if (!x.On && x.Env == 0) continue;
                // interpolate
                int frac = (x.Pos >> 4) & 0xFF;
                int idx = x.Rdp;
                int s0 = x.Buf[idx % 12], s1 = x.Buf[(idx + 1) % 12], s2 = x.Buf[(idx + 2) % 12], s3 = x.Buf[(idx + 3) % 12];
                int outv = (Gauss[frac * 4] * (s0 >> 1) + Gauss[frac * 4 + 1] * (s1 >> 1) + Gauss[frac * 4 + 2] * (s2 >> 1) + Gauss[frac * 4 + 3] * (s3 >> 1)) >> 10;
                if ((non & (1 << i)) != 0) outv = (short)(noise * 2);
                if (outv > 32767) outv = 32767; if (outv < -32768) outv = -32768;
                Envelope(i);
                outv = (outv * x.Env) >> 11;
                R[b + 9] = (byte)(outv >> 8);
                int vl = (sbyte)R[b], vr = (sbyte)R[b + 1];
                int l = (outv * vl) >> 7, r = (outv * vr) >> 7;
                mainL = Clamp16(mainL + l); mainR = Clamp16(mainR + r);
                if ((eon & (1 << i)) != 0) { echoL = Clamp16(echoL + l); echoR = Clamp16(echoR + r); }
                // advance pitch
                int pitch = (R[b + 2] | (R[b + 3] << 8)) & 0x3FFF;
                x.Pos += pitch;
                while (x.Pos >= 0x1000) { x.Pos -= 0x1000; if (x.On || x.Env > 0) AdvanceOne(i); }
            }
            // echo
            int esa = R[0x6D] << 8;
            if (echoOff == 0) echoLen = (R[0x7D] & 15) * 0x800; if (echoLen == 0 && echoOff == 0) echoLen = 4;
            int ea = (esa + echoOff) & 0xFFFF;
            hist[histPos, 0] = (short)(ram[ea] | (ram[(ea + 1) & 0xFFFF] << 8)) >> 1;
            hist[histPos, 1] = (short)(ram[(ea + 2) & 0xFFFF] | (ram[(ea + 3) & 0xFFFF] << 8)) >> 1;
            int fl = 0, fr = 0;
            for (int k = 0; k < 8; k++)
            {
                int hp = (histPos + 1 + k) & 7;   // oldest first
                int coef = (sbyte)R[k * 16 + 0x0F];
                fl += (hist[hp, 0] * coef) >> 6; fr += (hist[hp, 1] * coef) >> 6;
            }
            histPos = (histPos + 1) & 7;
            fl = Clamp16(fl); fr = Clamp16(fr);
            int mvl = (sbyte)R[0x0C], mvr = (sbyte)R[0x1C], evl = (sbyte)R[0x2C], evr = (sbyte)R[0x3C], efb = (sbyte)R[0x0D];
            outL = Clamp16(((mainL * mvl) >> 7) + ((fl * evl) >> 7));
            outR = Clamp16(((mainR * mvr) >> 7) + ((fr * evr) >> 7));
            if ((flg & 0x40) != 0) { outL = 0; outR = 0; }
            if ((flg & 0x20) == 0)
            {
                int wl = Clamp16(echoL + ((fl * efb) >> 7)) & ~1, wr = Clamp16(echoR + ((fr * efb) >> 7)) & ~1;
                ram[ea] = (byte)wl; ram[(ea + 1) & 0xFFFF] = (byte)(wl >> 8); ram[(ea + 2) & 0xFFFF] = (byte)wr; ram[(ea + 3) & 0xFFFF] = (byte)(wr >> 8);
            }
            echoOff += 4; if (echoOff >= echoLen) echoOff = 0;
        }

        // one more decoded sample consumed; decode 4 more whenever 4 have been used
        void AdvanceOne(int i)
        {
            var x = v[i];
            x.Rdp++;
            while (x.On && x.Wr - x.Rdp < 8) DecodeBlockHalf(i);
        }

        static int Clamp16(int x) { return x > 32767 ? 32767 : x < -32768 ? -32768 : x; }
    }
}
