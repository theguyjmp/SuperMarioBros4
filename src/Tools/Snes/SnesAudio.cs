using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SMB4.Audio;

namespace SMB4.Tools
{
    public static partial class SnesExport
    {
        /// <summary>Export step: audio (SPC700 driver + BRR samples + songs + SFX). See snes/SOUND.md.</summary>
        static int ExportAudio(string outDir) { return SnesAudio.Export(outDir); }
    }

    /// <summary>One BRR sample in APU RAM (an instrument key-zone, a drum piece, or silence).</summary>
    public sealed class SSample
    {
        public string Key = ""; public int Id;
        public int Inst = -1, Zone = -1, Piece = -1; public bool Silence;
        public int Quality;
        public short[] Pcm; public int Loop = -1; public double Rate; public double BaseHz;
        public byte[] Brr; public int Size { get { return (Brr.Length + 1) & ~1; } }
        public int Adsr1, Adsr2, Rel, Tune;
        public double Gain = 1;   // level compensation (flattened drum tails are normalised)
        public bool Resident; public int Addr = -1; public int DynIndex = -1;
        public readonly HashSet<int> Songs = new HashSet<int>();
        public int LoopByte { get { return Loop < 0 ? 0 : Loop / 16 * 9; } }
    }

    /// <summary>A converted song: sequence blob + the samples it needs + its APU layout.</summary>
    public sealed class SSong
    {
        public string Name = ""; public int Id; public Song Src;
        public int Edl, Efb, Evol; public int EchoStart;       // echo region [EchoStart, $10000)
        public byte[] Blob; public int Addr;                  // blob at Addr (header first)
        public readonly List<SSample> Samples = new List<SSample>();
        public readonly List<int> GlobalInsts = new List<int>();
        public int SfxV0 = 7, SfxV1 = 6;
        public int Limit { get { return Addr; } }
        public readonly List<SSample> Preload = new List<SSample>();   // partner jingles' samples loaded along with this song
        public List<SSample> UploadList { get { return Samples.Where(x => !x.Resident).Concat(Preload).ToList(); } }
    }

    /// <summary>
    /// Converts the Windows game's SNES-style audio (src/Audio: code-generated instruments, MML songs, SFX programs)
    /// into data for the SPC700 driver in snes/spc/driver.asm, and writes gen/music.inc + gen/snd_data.s + blobs.
    /// </summary>
    public static class SnesAudio
    {
        public const int SrcRate = 48000;
        public const int DriverOrg = 0x0600;
        public const double VolScale = 60.0;     // VOLL/VOLR units per 1.0 of C# linear music volume at centre pan
        public const double SfxBoost = 1.65;     // C# SfxGain/MusicGain at default settings (0.81/0.49)
        public const int MVol = 96;              // master volume: matches the C# mix level (0.49 * 0.44 per unit)
        public static readonly int[] Fir = { 5, 12, 19, 28, 28, 19, 12, 5 };
        public static readonly int[] RatePeriod = { 1 << 30, 2048, 1536, 1280, 1024, 768, 640, 512, 384, 320, 256, 192, 160, 128, 96, 80, 64, 48, 40, 32, 24, 20, 16, 12, 10, 8, 6, 5, 4, 3, 2, 1 };
        public const int ResidentBudget = 9 * 1024;
        public const int FirstBank = 16, MaxBanks = 12;

        // ------------------------------------------------------------------ results (also used by snes-song)
        public static readonly List<SSample> Samples = new List<SSample>();
        public static readonly List<SSong> Songs = new List<SSong>();
        public static readonly List<string> Warnings = new List<string>();
        public static byte[] Image;       // resident APU image [DriverOrg, ResEnd)
        public static int ResEnd, DirAddr;
        public static Dictionary<string, int> Syms;
        static readonly Dictionary<string, SSample> byKey = new Dictionary<string, SSample>();
        static SSample silence;
        static string root;

        static string kitLetters;
        static string KitLetters { get { if (kitLetters == null) { Bank.EnsureBuilt(); kitLetters = new string(Bank.Kit.Keys.OrderBy(c => c).ToArray()); } return kitLetters; } }
        public static int PieceIndex(char c) { return KitLetters.IndexOf(c); }

        // ================================================================== entry
        public static int Export(string outDir)
        {
            if (!Build(true)) { foreach (var w in Warnings.Distinct()) Console.WriteLine("  audio: " + w); return 1; }
            WriteOutputs(outDir);
            Console.WriteLine("  audio: " + Songs.Count + " songs, " + (int)SfxId.Count + " sfx, " + Samples.Count + " samples, resident " +
                (ResEnd - DriverOrg) + " B (driver+tables " + (DirAddr - DriverOrg) + " B), ROM " + romBytes / 1024 + " KB, " + PairInfo);
            foreach (var w in Warnings.Distinct()) Console.WriteLine("  audio warning: " + w);
            return 0;
        }

        public static bool Build(bool verbose)
        {
            Samples.Clear(); Songs.Clear(); Warnings.Clear(); byKey.Clear();
            root = FindRoot();
            Sound.Init(false);
            Bank.EnsureBuilt();
            foreach (var e in Sound.Errors) Warnings.Add("mml: " + e);
            silence = new SSample { Key = "silence", Silence = true, Pcm = new short[16], Loop = 0, Rate = 32000, Resident = true, Adsr1 = 0x8F, Adsr2 = 0xE0, Rel = 0x9F };
            AddSample(silence);

            // songs (alphabetical ids from 1)
            var names = Sound.SongNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var n in names) Songs.Add(new SSong { Name = n, Id = Songs.Count + 1, Src = Sound.GetSong(n) });
            foreach (var s in Songs) AnalyzeSong(s);
            // sfx samples are resident
            ComputeSfxZones();
            sfxParts = new List<SfxPart>[(int)SfxId.Count];
            for (int i = 0; i < (int)SfxId.Count; i++) sfxParts[i] = AnalyzeSfx((SfxId)i);

            // render all samples, then fit memory by lowering quality where needed
            foreach (var s in Samples) Render(s);
            while (true)
            {
                int rs = Samples.Where(x => x.Resident).Sum(x => x.Size);
                if (rs <= ResidentBudget) break;
                var big = Samples.Where(x => x.Resident && !x.Silence && x.Quality < 7).OrderByDescending(x => x.Size).FirstOrDefault();
                if (big == null) { Warnings.Add("resident samples over budget: " + rs); break; }
                big.Quality++; Render(big);
            }
            if (verbose) foreach (var x in Samples.Where(y => y.Resident)) Console.WriteLine("    resident " + x.Key + " " + Name(x) + " " + x.Size + " B q" + x.Quality);
            bool resChanged = true;
            for (int iter = 0; iter < 3000; iter++)
            {
                if (resChanged) { BuildImage(); resChanged = false; }
                int resSize = Samples.Where(x => x.Resident).Sum(x => x.Size);
                if (resSize > ResidentBudget)
                {
                    var big = Samples.Where(x => x.Resident && !x.Silence && x.Quality < 7).OrderByDescending(x => x.Size).FirstOrDefault();
                    if (big != null) { big.Quality++; Render(big); resChanged = true; continue; }
                }
                var fail = Allocate();
                if (fail == null) break;
                // shrink the largest dynamic sample of the failing song
                var victim = fail.Samples.Where(x => !x.Resident && x.Quality < 7 && (x.Piece < 0 || x.Quality < 2)).OrderByDescending(x => x.Size).FirstOrDefault()
                    ?? fail.Samples.Where(x => !x.Resident && x.Quality < 7).OrderByDescending(x => x.Size).FirstOrDefault();
                if (victim == null)
                {
                    Warnings.Add("song " + fail.Name + " does not fit APU RAM even at lowest quality: resident end $" + ResEnd.ToString("X4") + ", blob " + fail.Blob.Length + " at $" + fail.Addr.ToString("X4") + ", echo $" + fail.EchoStart.ToString("X4") + ", dynamic samples " + fail.Samples.Where(x => !x.Resident).Sum(x => x.Size) + " B: " + string.Join(" ", fail.Samples.Where(x => !x.Resident).Select(x => Name(x) + "=" + x.Size + "q" + x.Quality)));
                    return false;
                }
                victim.Quality++; Render(victim);
                if (iter == 2999) { Warnings.Add("memory fit did not converge"); return false; }
            }
            AddSoftPairs();
            // upgrade pass: give back quality wherever the layout still fits (drums first)
            for (int round = 0; round < 4; round++)
            {
                bool any = false;
                foreach (var s in Samples.Where(x => !x.Resident && x.Quality > 0).OrderBy(x => x.Piece >= 0 ? 0 : 1).ThenByDescending(x => x.Quality).ToList())
                {
                    s.Quality--; Render(s);
                    if (Allocate() != null) { s.Quality++; Render(s); } else any = true;
                }
                if (!any) break;
            }
            if (Allocate() != null) { Warnings.Add("layout failed after upgrade pass"); return false; }
            ComputePreload();
            BuildImage();
            if (verbose)
                foreach (var s in Songs)
                    Console.WriteLine(string.Format("    {0,-14} seq {1,5} B  samples {2,6} B  echo {3,5} B  free {4,6} B", s.Name, s.Blob.Length,
                        s.Samples.Where(x => !x.Resident).Sum(x => x.Size), 0x10000 - s.EchoStart, FreeFor(s)));
            return true;
        }

        public static string Name(SSample x)
        {
            if (x.Silence) return "silence";
            if (x.Piece >= 0) return "kit" + x.Piece;
            var ins = Bank.Get(x.Inst); return ins.Name + "#" + x.Zone;
        }

        static int FreeFor(SSong s)
        {
            int top = ResEnd; foreach (var x in s.Samples) if (!x.Resident) top = Math.Max(top, x.Addr + x.Size);
            return s.Addr - top;
        }

        static string FindRoot()
        {
            string d = Directory.GetCurrentDirectory();
            for (int i = 0; i < 6 && d != null; i++)
            {
                if (File.Exists(Path.Combine(d, "snes", "spc", "driver.asm"))) return d;
                d = Path.GetDirectoryName(d);
            }
            string exe = Path.GetDirectoryName(typeof(SnesAudio).Assembly.Location);
            d = exe;
            for (int i = 0; i < 6 && d != null; i++)
            {
                if (File.Exists(Path.Combine(d, "snes", "spc", "driver.asm"))) return d;
                d = Path.GetDirectoryName(d);
            }
            throw new Exception("snes/spc/driver.asm not found (run from the repository)");
        }

        static SSample AddSample(SSample s) { s.Id = Samples.Count; Samples.Add(s); byKey[s.Key] = s; return s; }

        static SSample ZoneSample(int inst, int zone)
        {
            string k = "i" + inst + "z" + zone; SSample s;
            if (byKey.TryGetValue(k, out s)) return s;
            var ins = Bank.Get(inst);
            s = new SSample { Key = k, Inst = inst, Zone = zone };
            FitAdsr(ins, s);
            return AddSample(s);
        }

        static SSample PieceSample(int kitSample)
        {
            string k = "k" + kitSample; SSample s;
            if (byKey.TryGetValue(k, out s)) return s;
            s = new SSample { Key = k, Piece = kitSample };
            return AddSample(s);
        }

        static int ZoneOf(Instrument ins, double hz)
        {
            var smp = ins.Pick(hz);
            return Array.IndexOf(ins.Zones, smp);
        }

        // ================================================================== song analysis (runs the C# player)
        static void AnalyzeSong(SSong s)
        {
            var song = s.Src;
            s.Edl = Math.Max(0, Math.Min(15, song.EchoDelay));
            s.Efb = Clamp((int)Math.Round(song.EchoFb * 128), -128, 127);
            s.Evol = Clamp((int)Math.Round(song.EchoVol * MVol), 0, 127);
            s.EchoStart = 0x10000 - Math.Max(256, s.Edl * 2048);
            var mp = new MusicPlayer();
            mp.Play(song);
            var used = new HashSet<SSample>();
            int maxFrames = 60 * 60 * 6;
            for (int f = 0; f < maxFrames && !mp.Finished; f++)
            {
                mp.Frame();
                for (int c = 0; c < Ch.Count; c++)
                {
                    var o = mp.Out[c];
                    if (!o.Trigger) continue;
                    var ins = Bank.Get(o.Inst);
                    if (ins == null) continue;
                    if (ins.Kit)
                    {
                        KitPiece p;
                        if (Bank.Kit.TryGetValue((char)o.Piece, out p)) used.Add(PieceSample(p.Sample));
                        continue;
                    }
                    if (ins.Name == "noise") { Warnings.Add(s.Name + ": @noise is not supported in music (skipped)"); continue; }
                    used.Add(ZoneSample(ins.Id, ZoneOf(ins, o.Freq)));
                    if (o.Chord > 1) used.Add(ZoneSample(ins.Id, ZoneOf(ins, o.Freq2)));
                    if (o.Chord > 2) used.Add(ZoneSample(ins.Id, ZoneOf(ins, o.Freq3)));
                }
            }
            foreach (var x in used) { x.Songs.Add(s.Id); s.Samples.Add(x); }
            s.Samples.Sort((a, b) => a.Id.CompareTo(b.Id));
            // instruments referenced by the tracks
            foreach (var tr in song.Tracks)
                if (tr != null) foreach (var e in tr) if (e.T == EvT.Inst && !s.GlobalInsts.Contains(e.A)) s.GlobalInsts.Add(e.A);
            int piano = Bank.Find("piano");
            if (!s.GlobalInsts.Contains(piano)) s.GlobalInsts.Insert(0, piano); else { s.GlobalInsts.Remove(piano); s.GlobalInsts.Insert(0, piano); }
            // SFX voices: the two least busy channels
            var busy = new List<int[]>();
            for (int c = 0; c < Ch.Count; c++)
            {
                int n = 0; var tr = song.Tracks[c];
                if (tr != null) foreach (var e in tr) if (e.T == EvT.Note || e.T == EvT.Drum) n++;
                busy.Add(new[] { n, c });
            }
            busy.Sort((a, b) => a[0] != b[0] ? a[0].CompareTo(b[0]) : b[1].CompareTo(a[1]));
            s.SfxV0 = busy[0][1]; s.SfxV1 = busy[1][1];
        }

        // ================================================================== SFX analysis
        sealed class SfxPart { public int Prio, Slot; public SfxDef Def; public byte[] Code; }
        static List<SfxPart>[] sfxParts;

        // SFX use one key zone per instrument (the one closest to all their notes) to keep resident APU RAM small
        static readonly Dictionary<int, int> sfxZone = new Dictionary<int, int>();
        static void ComputeSfxZones()
        {
            sfxZone.Clear();
            var fr = new Dictionary<int, List<double>>();
            foreach (var defs in SfxBank.Defs)
                if (defs != null) foreach (var d in defs) foreach (var f in d.Frames)
                    {
                        var ins = Bank.Get(f.Inst);
                        if (!f.Trigger || ins == null || ins.Kit || ins.Name == "noise" || f.Freq <= 0) continue;
                        List<double> l; if (!fr.TryGetValue(ins.Id, out l)) fr[ins.Id] = l = new List<double>();
                        l.Add(f.Freq);
                    }
            foreach (var kv in fr)
            {
                var ins = Bank.Get(kv.Key); int best = 0; double bd = 1e18;
                for (int z = 0; z < ins.Zones.Length; z++)
                {
                    double d = 0; foreach (var f in kv.Value) { double r = Math.Log(f / ins.ZoneHz[z], 2); d += r > 1.9 ? 100 : Math.Abs(r); }
                    if (d < bd) { bd = d; best = z; }
                }
                sfxZone[kv.Key] = best;
            }
        }
        static int SfxZoneOf(Instrument ins) { int z; return sfxZone.TryGetValue(ins.Id, out z) ? z : 0; }

        static List<SfxPart> AnalyzeSfx(SfxId id)
        {
            var defs = SfxBank.Defs[(int)id];
            var parts = new List<SfxPart>();
            if (defs == null) return parts;
            var order = defs.Select((d, i) => new { d, i }).OrderByDescending(x => x.d.Priority).ThenBy(x => x.i).Take(2).ToList();
            foreach (var o in order)
            {
                var p = new SfxPart { Prio = o.d.Priority, Slot = o.d.Ch, Def = o.d };
                // mark samples used (resident)
                foreach (var f in o.d.Frames)
                {
                    if (!f.Trigger) continue;
                    var ins = Bank.Get(f.Inst); if (ins == null || f.Vol <= 0) continue;
                    if (ins.Kit) { KitPiece kp; if (Bank.Kit.TryGetValue((char)f.Piece, out kp)) PieceSample(kp.Sample).Resident = true; }
                    else if (ins.Name != "noise") ZoneSample(ins.Id, SfxZoneOf(ins)).Resident = true;
                }
                parts.Add(p);
            }
            return parts;
        }

        // ================================================================== sample rendering
        static void Render(SSample s)
        {
            if (s.Silence) { s.Brr = EncodeBrr(s.Pcm, 0); return; }
            if (s.Piece >= 0) RenderDrum(s); else RenderZone(s);
            s.Brr = EncodeBrr(s.Pcm, s.Loop);
            if (s.BaseHz > 0)
            {
                double f0s = s.BaseHz / s.Rate; // cycles per sample
                double c = 12 * Math.Log(4096.0 * 440.0 / (32000.0 * f0s), 2) - 69;
                s.Tune = (int)Math.Round(c * 256);
            }
        }

        static void RenderZone(SSample s)
        {
            var ins = Bank.Get(s.Inst);
            var z = ins.Zones[s.Zone];
            int q = s.Quality;
            double[] x = new double[z.Len + 1];
            for (int i = 0; i <= z.Len; i++) x[i] = z.D[i];
            int A = z.LoopStart, L = z.Len - A;
            double f0 = z.BaseHz;
            int pd = (int)Math.Round(SrcRate / f0);
            // quality knobs
            double rate = Math.Min(32000, Math.Max(16000, f0 * 20)) * Math.Pow(0.87, q);
            rate = Math.Max(rate, Math.Min(32000, f0 * 5));
            rate = Math.Max(rate, 5000);
            double attCap = Math.Max(0.015, 0.16 * Math.Pow(0.72, q));
            double loopCap = Math.Max(0.012, 0.12 * Math.Pow(0.78, q));
            int A2 = Math.Min(A, (int)(attCap * SrcRate));
            int k = Math.Max(1, (int)Math.Round(loopCap * f0));
            int Ls = Math.Min(L, k * pd);
            if (Ls < 1) Ls = L;
            Func<int, double> S = i => { int m = ((i - A) % L + L) % L; return x[A + m]; };
            // attack with its decaying part faded out before A2 (so it joins the steady loop without a click)
            var att = new double[A2];
            int F = Math.Min(A2, (int)(0.012 * SrcRate));
            for (int i = 0; i < A2; i++)
            {
                double st = S(i), w = 1;
                if (i >= A2 - F && F > 0) { double u = (i - (A2 - F)) / (double)F; w = 0.5 + 0.5 * Math.Cos(Math.PI * u); }
                att[i] = st + (x[i] - st) * w;
            }
            // loop segment [A2, A2+Ls), cross-faded into what precedes A2 so the wrap is seamless
            var seg = new double[Ls];
            int Fx = Ls == L ? 0 : Ls / 2;
            for (int m = 0; m < Ls; m++)
            {
                double a = S(A2 + m);
                if (Fx > 0 && m >= Ls - Fx) { double w = (m - (Ls - Fx) + 1) / (double)Fx; a = a * (1 - w) + S(A2 + m - Ls) * w; }
                seg[m] = a;
            }
            int Ln = Math.Max(16, (int)Math.Round(Ls * rate / SrcRate / 16.0) * 16);
            double r = Ln / (double)Ls;
            int An = (int)Math.Ceiling(A2 * r);
            int pad = (16 - An % 16) % 16;
            var outp = new double[pad + An + Ln];
            Func<int, double> X = i => i < 0 ? 0 : i < A2 ? att[i] : seg[(i - A2) % Ls];
            Func<int, double> Xp = i => seg[((i % Ls) + Ls) % Ls];
            double fc = Math.Min(1.0, r) * 0.92;
            int W = (int)Math.Ceiling(10 / fc);
            for (int j = 0; j < An; j++) outp[pad + j] = Interp(X, j / r, fc, W);
            for (int m = 0; m < Ln; m++) outp[pad + An + m] = Interp(Xp, (An + m) / r - A2, fc, W);
            s.Pcm = ToPcm(outp);
            s.Loop = pad + An;
            s.Rate = SrcRate * r;
            s.BaseHz = f0;
        }

        static double Interp(Func<int, double> x, double t, double fc, int W)
        {
            int c = (int)Math.Floor(t); double sum = 0;
            for (int i = c - W + 1; i <= c + W; i++)
            {
                double d = t - i;
                double a = d * fc;
                double sinc = Math.Abs(a) < 1e-9 ? 1 : Math.Sin(Math.PI * a) / (Math.PI * a);
                double wv = Math.Abs(d) >= W ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * d / W);
                sum += x(i) * fc * sinc * wv;
            }
            return sum;
        }

        static short[] ToPcm(double[] d)
        {
            var p = new short[d.Length];
            for (int i = 0; i < d.Length; i++) p[i] = (short)Clamp((int)Math.Round(d[i] * 32000), -32000, 32000);
            return p;
        }

        // drum pieces: rate, max length, optional flatten+loop (tau) for cymbal-like tails
        sealed class DrumCfg { public double Rate, Len, Tau, LoopAt, LoopLen; public DrumCfg(double r, double l, double tau = 0, double at = 0, double ll = 0) { Rate = r; Len = l; Tau = tau; LoopAt = at; LoopLen = ll; } }
        static DrumCfg Cfg(int piece)
        {
            switch (piece)
            {
                case Bank.KKick: return new DrumCfg(14000, 0.34);
                case Bank.KSnare: return new DrumCfg(20000, 0.26);
                case Bank.KHat: return new DrumCfg(24000, 0.08);
                case Bank.KOpenHat: return new DrumCfg(24000, 0, 0.13, 0.02, 0.07);
                case Bank.KCrash: return new DrumCfg(24000, 0, 0.55, 0.04, 0.12);
                case Bank.KRide: return new DrumCfg(24000, 0, 0.34, 0.05, 0.1);
                case Bank.KTom: return new DrumCfg(11000, 0.42);
                case Bank.KConga: return new DrumCfg(12000, 0.3);
                case Bank.KBongo: return new DrumCfg(16000, 0.2);
                case Bank.KClap: return new DrumCfg(16000, 0.3);
                case Bank.KShaker: return new DrumCfg(22000, 0.12);
                case Bank.KRim: return new DrumCfg(18000, 0.09);
                case Bank.KCowbell: return new DrumCfg(16000, 0.34);
                case Bank.KBoom: return new DrumCfg(8000, 0.7);
                case Bank.KThud: return new DrumCfg(8000, 0.38);
            }
            return new DrumCfg(16000, 0.3);
        }

        static void RenderDrum(SSample s)
        {
            var src = Bank.KitSamples[s.Piece];
            var cfg = Cfg(s.Piece);
            int q = s.Quality;
            double rate = Math.Max(6000, cfg.Rate * Math.Pow(0.88, q));
            double[] x = new double[src.Len];
            for (int i = 0; i < src.Len; i++) x[i] = src.D[i];
            if (cfg.Tau > 0)
            {
                // flatten the exponential decay; the SNES envelope (sustain rate) re-applies it
                for (int i = 0; i < x.Length; i++) x[i] *= Math.Exp(i / (cfg.Tau * SrcRate));
                double loopAt = cfg.LoopAt, ll = cfg.LoopLen * Math.Pow(0.85, q);
                int la = (int)(loopAt * SrcRate), ln = Math.Max(256, (int)(ll * SrcRate));
                // normalise the flattened level to the level at loop start
                double peak = 1e-6; for (int i = 0; i < la + ln; i++) peak = Math.Max(peak, Math.Abs(x[i]));
                int Ln = Math.Max(32, (int)Math.Round(ln * rate / SrcRate / 16.0) * 16);
                double r = Ln / (double)ln;
                var seg = new double[ln];
                int Fx = Math.Min(ln / 3, la);
                for (int m = 0; m < ln; m++)
                {
                    double a = x[la + m];
                    if (m >= ln - Fx) { double w = (m - (ln - Fx) + 1) / (double)Fx; a = a * (1 - w) + x[la + m - ln] * w; }
                    seg[m] = a / peak;
                }
                int An = (int)Math.Ceiling(la * r);
                int pad = (16 - An % 16) % 16;
                var outp = new double[pad + An + Ln];
                double fc = Math.Min(1.0, r) * 0.92; int W = (int)Math.Ceiling(10 / fc);
                Func<int, double> X = i => i < 0 ? 0 : i < la ? x[i] / peak : seg[(i - la) % ln];
                Func<int, double> Xp = i => seg[((i % ln) + ln) % ln];
                for (int j = 0; j < An; j++) outp[pad + j] = Interp(X, j / r, fc, W);
                for (int m = 0; m < Ln; m++) outp[pad + An + m] = Interp(Xp, (An + m) / r - la, fc, W);
                s.Pcm = ToPcm(outp); s.Loop = pad + An; s.Rate = SrcRate * r;
                // ADSR: instant attack, sustain level max, sustain rate = decay tau (x0.85 to account for the level peak)
                int best = 0; double bd = 1e9;
                for (int rr = 1; rr < 32; rr++) { double tau = RatePeriod[rr] / 125.0; double d = Math.Abs(Math.Log(tau / cfg.Tau)); if (d < bd) { bd = d; best = rr; } }
                s.Adsr1 = 0x8F; s.Adsr2 = 0xE0 | best; s.Rel = 0xA0 | 28;
                s.Gain = peak;   // the stored sample was divided by peak: play it louder by the same factor
                s.Tune = 0; s.BaseHz = 0;
                return;
            }
            double len = cfg.Len * Math.Pow(0.85, q);
            int n = Math.Min(x.Length, (int)(len * SrcRate));
            int fade = Math.Min(n / 4, (int)(0.03 * SrcRate));
            for (int i = n - fade; i < n; i++) { double u = (i - (n - fade)) / (double)fade; x[i] *= 0.5 + 0.5 * Math.Cos(Math.PI * u); }
            int N = (int)Math.Ceiling(n * rate / SrcRate / 16.0) * 16;
            double rr2 = N / (double)n;
            double fc2 = Math.Min(1.0, rr2) * 0.92; int W2 = (int)Math.Ceiling(10 / fc2);
            var o = new double[N];
            Func<int, double> Xd = i => i < 0 || i >= n ? 0 : x[i];
            for (int j = 0; j < N; j++) o[j] = Interp(Xd, j / rr2, fc2, W2);
            s.Pcm = ToPcm(o); s.Loop = -1; s.Rate = SrcRate * rr2;
            s.Adsr1 = 0x8F; s.Adsr2 = 0xE0; s.Rel = 0xA0 | 28;
            s.BaseHz = 0;
        }

        /// <summary>u256 (1/256 semitone pitch index, P = 2^(u/12)) for playing a drum sample at a playback-rate factor.</summary>
        public static int DrumU(SSample s, double factor)
        {
            double p = 4096.0 * s.Rate / 32000.0 * factor;
            return (int)Math.Round(12 * Math.Log(p, 2) * 256);
        }

        // ================================================================== ADSR fit (C# envelope -> SNES ADSR + GAIN release)
        static void FitAdsr(Instrument ins, SSample s)
        {
            // AR
            int ar = 15; double bd = 1e9;
            for (int a = 0; a < 16; a++)
            {
                double t = a == 15 ? 0.0001 : 64.0 * RatePeriod[a * 2 + 1] / 32000.0;
                double d = Math.Abs(Math.Log(t / Math.Max(0.0003, ins.Attack)));
                if (d < bd) { bd = d; ar = a; }
            }
            // reference decay curve (after the attack), 5 ms steps for 3 s
            int npts = 600; double dt = 0.005;
            var refDb = new double[npts];
            {
                double env = 1, sus = ins.Sustain;
                double dC = ins.Decay <= 0 ? 0 : Math.Exp(-1.0 / (ins.Decay * SrcRate));
                double sC = ins.SusDecay > 0 ? Math.Exp(-1.0 / (ins.SusDecay * SrcRate)) : 1;
                int per = (int)(dt * SrcRate);
                for (int i = 0; i < npts; i++)
                {
                    refDb[i] = Db(env);
                    for (int k = 0; k < per; k++) { sus *= sC; env = sus + (env - sus) * dC; }
                }
            }
            int bestDr = 0, bestSl = 7, bestSr = 0; bd = 1e18;
            for (int dr = 0; dr < 8; dr++)
                for (int sl = 0; sl < 8; sl++)
                    for (int sr = 0; sr < 32; sr++)
                    {
                        double err = 0;
                        int env = 0x7FF; bool sustain = sl == 7;
                        int dper = RatePeriod[dr * 2 + 16], sper = RatePeriod[sr];
                        long t = 0; int next = dper; int pt = 0; long ptT = 0;
                        // walk samples by events
                        while (pt < npts)
                        {
                            long evT = t + (sustain ? (sr == 0 ? long.MaxValue / 4 : sper) : dper);
                            while (pt < npts && ptT < evT)
                            {
                                double e = Db(env / 2047.0) - refDb[pt];
                                double w = pt * dt < 1.0 ? 1 : 0.35;
                                if (refDb[pt] < -45 && env < 8) w *= 0.1;
                                err += e * e * w;
                                pt++; ptT = (long)(pt * dt * 32000);
                            }
                            t = evT;
                            env = env - 1 - ((env - 1) >> 8); if (env < 0) env = 0;
                            if (!sustain && (env >> 8) == sl) sustain = true;
                            if (env == 0) { while (pt < npts) { double e = Db(0) - refDb[pt]; double w = pt * dt < 1.0 ? 1 : 0.35; if (refDb[pt] < -45) w *= 0.1; err += e * e * w; pt++; } }
                            if (err > bd) break;
                        }
                        if (err < bd) { bd = err; bestDr = dr; bestSl = sl; bestSr = sr; }
                    }
            s.Adsr1 = 0x80 | (bestDr << 4) | ar;
            s.Adsr2 = (bestSl << 5) | bestSr;
            int rb = 31; double rd = 1e9;
            for (int r = 1; r < 32; r++) { double tau = RatePeriod[r] / 125.0; double d = Math.Abs(Math.Log(tau / Math.Max(0.004, ins.Release))); if (d < rd) { rd = d; rb = r; } }
            s.Rel = 0xA0 | rb;
        }
        static double Db(double x) { return 20 * Math.Log10(Math.Max(x, 0.003)); }

        // ================================================================== BRR
        public static byte[] EncodeBrr(short[] pcm, int loop)
        {
            int nb = pcm.Length / 16;
            var o = new byte[nb * 9];
            int p1 = 0, p2 = 0;
            int loopBlock = loop < 0 ? -1 : loop / 16;
            for (int b = 0; b < nb; b++)
            {
                int bestF = 0, bestS = 0; long bestE = long.MaxValue; var bestN = new int[16]; int bp1 = 0, bp2 = 0;
                int fmax = (b == 0 || b == loopBlock) ? 0 : 3;
                for (int f = 0; f <= fmax; f++)
                    for (int sh = 0; sh <= 12; sh++)
                    {
                        int q1 = p1, q2 = p2; long err = 0; var ns = new int[16];
                        for (int i = 0; i < 16; i++)
                        {
                            int target = pcm[b * 16 + i];
                            int pred = BrrPredict(f, q1, q2);
                            // out = (int16)(clamp16(pred + ((n << sh) >> 1)) * 2) ~ target
                            double want = (target / 2.0 - pred) * 2.0 / (1 << sh);
                            int n = (int)Math.Round(want); if (n < -8) n = -8; if (n > 7) n = 7;
                            int bestLocal = n; long be = long.MaxValue; int bo = 0;
                            for (int dn = -1; dn <= 1; dn++)
                            {
                                int nn = n + dn; if (nn < -8 || nn > 7) continue;
                                int outv = BrrOut(pred, nn, sh);
                                long e = (long)(outv - target) * (outv - target);
                                if (e < be) { be = e; bestLocal = nn; bo = outv; }
                            }
                            ns[i] = bestLocal; err += be;
                            q2 = q1; q1 = bo;
                            if (err >= bestE) break;
                        }
                        if (err < bestE) { bestE = err; bestF = f; bestS = sh; Array.Copy(ns, bestN, 16); bp1 = q1; bp2 = q2; }
                    }
                int flags = 0;
                if (b == nb - 1) flags |= 1 | (loop >= 0 ? 2 : 0);
                o[b * 9] = (byte)((bestS << 4) | (bestF << 2) | flags);
                for (int i = 0; i < 8; i++) o[b * 9 + 1 + i] = (byte)(((bestN[2 * i] & 15) << 4) | (bestN[2 * i + 1] & 15));
                // replay exactly to get history
                int r1 = p1, r2 = p2;
                for (int i = 0; i < 16; i++) { int pred = BrrPredict(bestF, r1, r2); int ov = BrrOut(pred, bestN[i], bestS); r2 = r1; r1 = ov; }
                p1 = r1; p2 = r2;
            }
            return o;
        }

        /// <summary>blargg SPC_DSP BRR filter: p1/p2 are previous decoded 16-bit outputs. Returns the 15-bit-domain prediction.</summary>
        public static int BrrPredict(int filter, int p1, int p2)
        {
            int s = 0; int q2 = p2 >> 1;
            switch (filter)
            {
                case 1: s += p1 >> 1; s += (-p1) >> 5; break;
                case 2: s += p1; s -= q2; s += q2 >> 4; s += (p1 * -3) >> 6; break;
                case 3: s += p1; s -= q2; s += (p1 * -13) >> 7; s += (q2 * 3) >> 4; break;
            }
            return s;
        }
        public static int BrrOut(int pred, int nyb, int shift)
        {
            int s = (nyb << shift) >> 1;
            if (shift >= 13) s = nyb < 0 ? -2048 : 0;
            s += pred;
            if (s > 32767) s = 32767; if (s < -32768) s = -32768;
            return (short)(s * 2);
        }

        // ================================================================== song -> sequence blob
        const int OpRest = 0x80, OpTie = 0x81, OpFull = 0x82, OpChord = 0x83, OpDrum = 0x84, OpVol = 0x85, OpPan = 0x86, OpInst = 0x87,
            OpGate = 0x88, OpVib = 0x89, OpArp = 0x8A, OpEcho = 0x8B, OpPorta = 0x8C, OpDetune = 0x8D, OpTransp = 0x8E, OpTempo = 0x8F,
            OpLoopS = 0x90, OpLoopE = 0x91, OpLoopP = 0x92, OpEnd = 0x93;
        public const int HeaderSize = 29;

        static byte[] BuildSongBlob(SSong s, int addr)
        {
            var song = s.Src;
            var b = new List<byte>();
            // header placeholder
            for (int i = 0; i < HeaderSize; i++) b.Add(0);
            int tickinc = (int)Math.Round(song.Tempo * 192.0 * 256.0 / 14400.0);
            Put16(b, 0, tickinc);
            b[2] = (byte)(song.Loops ? 1 : 0);
            b[3] = (byte)s.Edl; b[4] = (byte)(s.Efb & 255); b[5] = (byte)s.Evol; b[6] = (byte)(s.EchoStart >> 8);
            b[7] = (byte)s.SfxV0; b[8] = (byte)s.SfxV1;
            // instrument table
            int itab = b.Count; Put16(b, 9, addr + itab);
            var recOffs = new List<int>();
            for (int i = 0; i < s.GlobalInsts.Count; i++) { b.Add(0); b.Add(0); }
            for (int i = 0; i < s.GlobalInsts.Count; i++)
            {
                Put16(b, itab + i * 2, addr + b.Count);
                var ins = Bank.Get(s.GlobalInsts[i]);
                if (ins.Kit) { b.Add(0x80); b.Add(0); b.Add(0); continue; }
                b.Add(0); b.Add((byte)Clamp((int)Math.Round(ins.Gain * 232), 0, 255));
                int nz = ins.Zones.Length;
                b.Add((byte)nz);
                // zone samples; zones this song never triggers fall back to the nearest one it does
                var smp = new SSample[nz];
                for (int z = 0; z < nz; z++) { SSample x; byKey.TryGetValue("i" + ins.Id + "z" + z, out x); smp[z] = (x != null && (x.Songs.Contains(s.Id) || x.Resident)) ? x : null; }
                for (int z = 0; z < nz; z++)
                    if (smp[z] == null)
                        for (int d = 1; d < nz && smp[z] == null; d++)
                        {
                            if (z - d >= 0 && smp[z - d] != null && smp[z - d].Zone == z - d) smp[z] = smp[z - d];
                            else if (z + d < nz && smp[z + d] != null && smp[z + d].Zone == z + d) smp[z] = smp[z + d];
                        }
                for (int z = 0; z < nz; z++) if (smp[z] == null) smp[z] = silence;
                for (int z = 0; z < nz - 1; z++)
                {
                    double hz = Math.Sqrt(ins.ZoneHz[z] * ins.ZoneHz[z + 1]);
                    int bound = (int)Math.Round((69 + 12 * Math.Log(hz / 440.0, 2)) * 256);
                    b.Add((byte)smp[z].Id); b.Add((byte)(bound & 255)); b.Add((byte)(bound >> 8));
                }
                b.Add((byte)smp[nz - 1].Id);
            }
            // warp table (swing)
            Put16(b, 11, addr + b.Count);
            double sw = song.Swing / 100.0;
            for (int p = 0; p < 48; p++)
            {
                double w = sw <= 0.5 ? p : (p < 24 ? p * 2 * sw : 48 * sw + (p - 24) * 2 * (1 - sw));
                int v = (int)Math.Round(w * 256);
                b.Add((byte)(v & 255)); b.Add((byte)(v >> 8));
            }
            // tracks
            for (int c = 0; c < Ch.Count; c++)
            {
                var tr = song.Tracks[c];
                bool any = tr != null && tr.Any(e => e.T == EvT.Note || e.T == EvT.Drum || e.T == EvT.Rest || e.T == EvT.Tie);
                if (!any) { Put16(b, 13 + c * 2, 0); continue; }
                Put16(b, 13 + c * 2, addr + b.Count);
                b.AddRange(ConvertTrack(s, tr));
            }
            return b.ToArray();
        }

        static List<byte> ConvertTrack(SSong s, Ev[] evs)
        {
            var o = new List<byte>();
            o.Add(OpInst); o.Add(0);   // default instrument: piano (local 0)
            int depth = 0, maxDepth = 0;
            for (int i = 0; i < evs.Length; i++)
            {
                var e = evs[i];
                switch (e.T)
                {
                    case EvT.Note:
                        if (e.B != 0)
                        {
                            int n2 = (e.B & 255) - 1, n3 = ((e.B >> 8) & 255) - 1;
                            o.Add(OpChord); o.Add((byte)(sbyte)Clamp(n2 - e.A, -127, 127)); o.Add((byte)(n3 >= 0 ? (sbyte)Clamp(n3 - e.A, -127, 127) : -128));
                        }
                        if (NextIsTie(evs, i + 1)) o.Add(OpFull);
                        o.Add((byte)Clamp(e.A, 0, 127)); Dur(o, e.Dur, s); break;
                    case EvT.Rest: o.Add(OpRest); Dur(o, e.Dur, s); break;
                    case EvT.Tie: if (NextIsTie(evs, i + 1)) o.Add(OpFull); o.Add(OpTie); Dur(o, e.Dur, s); break;
                    case EvT.Drum:
                        {
                            int pi = PieceIndex((char)e.A);
                            if (pi < 0) { Warnings.Add(s.Name + ": unknown drum " + (char)e.A); o.Add(OpRest); Dur(o, e.Dur, s); break; }
                            o.Add(OpDrum); o.Add((byte)pi); Dur(o, e.Dur, s); break;
                        }
                    case EvT.Vol: o.Add(OpVol); o.Add((byte)Clamp(e.A, 0, 15)); break;
                    case EvT.Pan: o.Add(OpPan); o.Add((byte)Clamp(20 + e.A * 2, 0, 40)); break;
                    case EvT.Inst: o.Add(OpInst); o.Add((byte)s.GlobalInsts.IndexOf(e.A)); break;
                    case EvT.Gate: o.Add(OpGate); o.Add((byte)e.A); break;
                    case EvT.Vib: o.Add(OpVib); o.Add((byte)e.A); break;
                    case EvT.Arp: o.Add(OpArp); o.Add((byte)e.A); break;
                    case EvT.Echo: o.Add(OpEcho); o.Add((byte)(e.A != 0 ? 1 : 0)); break;
                    case EvT.Porta: o.Add(OpPorta); o.Add((byte)e.A); break;
                    case EvT.Detune: o.Add(OpDetune); o.Add((byte)(sbyte)Clamp((int)Math.Round(e.A * 256 / 100.0), -127, 127)); break;
                    case EvT.Transpose: o.Add(OpTransp); o.Add((byte)(sbyte)e.A); break;
                    case EvT.Tempo: { int ti = (int)Math.Round(e.A * 192.0 * 256.0 / 14400.0); o.Add(OpTempo); o.Add((byte)(ti & 255)); o.Add((byte)(ti >> 8)); break; }
                    case EvT.LoopStart: o.Add(OpLoopS); depth++; maxDepth = Math.Max(maxDepth, depth); break;
                    case EvT.LoopEnd: o.Add(OpLoopE); o.Add((byte)Clamp(e.A, 1, 255)); depth--; break;
                    case EvT.LoopPoint: o.Add(OpLoopP); break;
                }
            }
            if (maxDepth > 4) Warnings.Add(s.Name + ": repeat nesting " + maxDepth + " > 4 (driver limit)");
            o.Add(OpEnd);
            return o;
        }

        static bool NextIsTie(Ev[] evs, int from)
        {
            for (int i = from; i < evs.Length; i++)
            {
                var k = evs[i].T;
                if (k == EvT.Tie) return true;
                if (k == EvT.Note || k == EvT.Rest || k == EvT.Drum || k == EvT.LoopEnd || k == EvT.LoopStart || k == EvT.LoopPoint) return false;
            }
            return false;
        }

        static void Dur(List<byte> o, int d, SSong s)
        {
            if (d > 12000) { Warnings.Add(s.Name + ": note length " + d + " ticks clamped"); d = 12000; }
            if (d < 1) d = 1;
            if (d <= 255) o.Add((byte)d); else { o.Add(0); o.Add((byte)(d & 255)); o.Add((byte)(d >> 8)); }
        }

        static void Put16(List<byte> b, int at, int v) { b[at] = (byte)(v & 255); b[at + 1] = (byte)((v >> 8) & 255); }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        // ================================================================== SFX -> driver programs
        const int SfEnd = 0, SfWait = 1, SfKeyOn = 2, SfRamp = 3, SfRelease = 4, SfLoop = 5;

        struct SfFrame { public bool On, Trig, Noise; public int Samp, U; public double Vol, Pan; }

        static byte[] SfxCode(SfxDef d)
        {
            var fr = new SfFrame[d.Frames.Length];
            int curSamp = -1; bool curNoise = false; double curTune = 0; Instrument curIns = null; KitPiece curKp = new KitPiece();
            for (int i = 0; i < d.Frames.Length; i++)
            {
                var f = d.Frames[i];
                bool trig = f.Trigger || i == 0;
                var ins = Bank.Get(f.Inst);
                var x = new SfFrame();
                bool on = f.Gate && ins != null && !(trig && (f.Vol <= 0 || (f.Freq <= 0 && !ins.Kit)));
                if (!on) { fr[i] = x; if (trig) curSamp = -1; continue; }
                if (trig)
                {
                    curIns = ins;
                    if (ins.Kit)
                    {
                        Bank.Kit.TryGetValue((char)f.Piece, out curKp);
                        var sm = PieceSample(curKp.Sample); curSamp = sm.Id; curNoise = false;
                    }
                    else if (ins.Name == "noise") { curSamp = silence.Id; curNoise = true; }
                    else { var sm = ZoneSample(ins.Id, SfxZoneOf(ins)); curSamp = sm.Id; curNoise = false; curTune = sm.Tune; }
                }
                if (curSamp < 0) { fr[i] = x; continue; }
                x.On = true; x.Trig = trig; x.Samp = curSamp; x.Noise = curNoise;
                if (curIns.Kit)
                {
                    var sm = Samples[curSamp];
                    x.U = DrumU(sm, curKp.Pitch * (f.Freq > 0 ? f.Freq / 440.0 : 1.0));
                    x.Vol = f.Vol * curKp.Vol * curIns.Gain * sm.Gain; x.Pan = f.Pan + curKp.Pan;
                }
                else if (curNoise)
                {
                    x.U = NoiseClockU(f.Freq);
                    x.Vol = f.Vol * curIns.Gain; x.Pan = f.Pan;
                }
                else
                {
                    x.U = (int)Math.Round((69 + 12 * Math.Log(Math.Max(1.0, f.Freq) / 440.0, 2)) * 256 + curTune);
                    x.Vol = f.Vol * curIns.Gain; x.Pan = f.Pan;
                }
                fr[i] = x;
            }
            var o = new List<byte>();
            int j = 0;
            while (j < fr.Length)
            {
                var f = fr[j];
                if (!f.On)
                {
                    int k = j; while (k < fr.Length && !fr[k].On) k++;
                    o.Add(SfRelease);
                    int n = k - j; while (n > 0) { int m = Math.Min(255, n); o.Add(SfWait); o.Add((byte)m); n -= m; }
                    j = k; continue;
                }
                if (f.Trig)
                {
                    o.Add(SfKeyOn); o.Add((byte)f.Samp); o.Add((byte)(f.U & 255)); o.Add((byte)((f.U >> 8) & 255));
                    o.Add((byte)SfxVol8(f.Vol)); o.Add((byte)PanIdx(f.Pan)); o.Add((byte)((d.Frames[j].Echo ? 1 : 0) | (f.Noise ? 2 : 0)));
                    j++; continue;
                }
                // run of held frames: linear segments
                int start = j; var prev = fr[j - 1];
                int end = j;
                double du = f.U - prev.U, dv = SfxVol8(f.Vol) - (double)SfxVol8(prev.Vol), dp = PanIdx(f.Pan) - (double)PanIdx(prev.Pan);
                while (end + 1 < fr.Length && fr[end + 1].On && !fr[end + 1].Trig)
                {
                    int m = end + 1 - start + 1;
                    var g = fr[end + 1];
                    if (Math.Abs(g.U - (prev.U + du * m)) > 3 || Math.Abs(SfxVol8(g.Vol) - (SfxVol8(prev.Vol) + dv * m)) > 1.5 || Math.Abs(PanIdx(g.Pan) - (PanIdx(prev.Pan) + dp * m)) > 0.75) break;
                    end++;
                }
                int len = end - start + 1;
                var last = fr[end];
                double tdu = (last.U - prev.U) / (double)len, tdv = (SfxVol8(last.Vol) - SfxVol8(prev.Vol)) / (double)len, tdp = (PanIdx(last.Pan) - PanIdx(prev.Pan)) / (double)len;
                if (Math.Abs(tdu) < 0.01 && Math.Abs(tdv) < 0.01 && Math.Abs(tdp) < 0.01)
                {
                    int n = len; while (n > 0) { int m = Math.Min(255, n); o.Add(SfWait); o.Add((byte)m); n -= m; }
                }
                else
                {
                    int iu = (int)Math.Round(tdu), iv = (int)Math.Round(tdv * 256), ip = (int)Math.Round(tdp * 256);
                    int n = len;
                    while (n > 0)
                    {
                        int m = Math.Min(255, n);
                        o.Add(SfRamp); o.Add((byte)m); o.Add((byte)(iu & 255)); o.Add((byte)((iu >> 8) & 255));
                        o.Add((byte)(iv & 255)); o.Add((byte)((iv >> 8) & 255)); o.Add((byte)(ip & 255)); o.Add((byte)((ip >> 8) & 255));
                        n -= m;
                    }
                }
                j = end + 1;
            }
            o.Add(d.Loop ? (byte)SfLoop : (byte)SfEnd);
            return o.ToArray();
        }

        public static int SfxVol8(double v) { return Clamp((int)Math.Round(v * 231.0 * SfxBoost / 2.0 * 1.0), 0, 255); }
        public static int PanIdx(double pan) { return Clamp((int)Math.Round(20 + 20 * Math.Max(-1, Math.Min(1, pan))), 0, 40); }

        static readonly double[] NoiseHz = { 0, 16, 21, 25, 31, 42, 50, 63, 83, 100, 125, 167, 200, 250, 333, 400, 500, 667, 800, 1000, 1300, 1600, 2000, 2700, 3200, 4000, 5300, 6400, 8000, 10700, 16000, 32000 };
        static int NoiseClockU(double hz)
        {
            double want = SrcRate * hz / 440.0 / 2.0;   // noise "sample rate" -> the LFSR clock that sounds alike
            // interpolate on log scale between clock indices so ramps can glide
            if (want <= NoiseHz[1]) return 256;
            if (want >= 32000) return 31 * 256;
            for (int i = 1; i < 31; i++)
                if (want <= NoiseHz[i + 1])
                {
                    double f = Math.Log(want / NoiseHz[i]) / Math.Log(NoiseHz[i + 1] / NoiseHz[i]);
                    return (int)Math.Round((i + f) * 256);
                }
            return 31 * 256;
        }

        // ================================================================== resident image (driver + tables + DIR + resident samples)
        static string GenTables(Dictionary<int, int> dynAddr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; ---- generated by SnesAudio.cs ----");
            // pitch table (top octave: P in [8192,16384))
            var T = new int[193];
            for (int i = 0; i <= 192; i++) T[i] = (int)Math.Round(8192 * Math.Pow(2, i / 192.0));
            T[192] = 16384;
            Row(sb, "ptab_lo", Enumerable.Range(0, 193).Select(i => T[i] & 255));
            Row(sb, "ptab_hi", Enumerable.Range(0, 193).Select(i => T[i] >> 8));
            Row(sb, "ptab_d", Enumerable.Range(0, 192).Select(i => T[i + 1] - T[i]));
            Row(sb, "sintab", Enumerable.Range(0, 256).Select(i => (int)Math.Round(127 * Math.Sin(2 * Math.PI * i / 256)) & 255));
            Row(sb, "volc", Enumerable.Range(0, 16).Select(i => (int)Math.Round(255 * Math.Pow(i / 15.0, 1.7))));
            double panUnit = VolScale * 256.0 * 256.0 / (255.0 * 232.0);   // so that volc(15)*gain8(1.0)*pan(centre) -> VolScale
            Row(sb, "panl", Enumerable.Range(0, 41).Select(i => (int)Math.Round(panUnit * Math.Cos(((i - 20) / 20.0 + 1) * Math.PI / 4) * Math.Sqrt(2))));
            Row(sb, "panr", Enumerable.Range(0, 41).Select(i => (int)Math.Round(panUnit * Math.Sin(((i - 20) / 20.0 + 1) * Math.PI / 4) * Math.Sqrt(2))));
            Row(sb, "portat", new[] { 255, 15, 23, 33, 46, 64, 84, 115, 154, 205 });
            Row(sb, "rampt", Enumerable.Range(0, 12).Select(i => (int)Math.Round(i * 256 / 12.0)));
            // vibrato: delay (frames), depth (1/256 semitone)
            Row(sb, "vibdel", new[] { 0, 20, 12, 6 });
            Row(sb, "vibdep", new[] { 0, 38, 64, 115 });
            // arpeggios
            var arps = ArpTable.Arps;
            Row(sb, "arplen", arps.Select(a => a.Length));
            Row(sb, "arpoff", Enumerable.Range(0, arps.Length).Select(i => i * 4));
            var flat = new List<int>();
            foreach (var a in arps) for (int k = 0; k < 4; k++) flat.Add(k < a.Length ? a[k] : 0);
            Row(sb, "arpdat", flat);
            // sample info
            int ns = Samples.Count;
            Row(sb, "s_adsr1", Samples.Select(x => x.Adsr1));
            Row(sb, "s_adsr2", Samples.Select(x => x.Adsr2));
            Row(sb, "s_rel", Samples.Select(x => x.Rel));
            Row(sb, "s_tlo", Samples.Select(x => x.Tune & 255));
            Row(sb, "s_thi", Samples.Select(x => (x.Tune >> 8) & 255));
            // kit
            var kp = new int[KitLetters.Length, 6];
            for (int i = 0; i < KitLetters.Length; i++)
            {
                KitPiece p = Bank.Kit[KitLetters[i]];
                SSample sm; byKey.TryGetValue("k" + p.Sample, out sm);
                int u = sm != null ? DrumU(sm, p.Pitch) : 0;
                kp[i, 0] = sm != null ? sm.Id : silence.Id; kp[i, 1] = u & 255; kp[i, 2] = (u >> 8) & 255;
                kp[i, 3] = Clamp((int)Math.Round(p.Vol * 232 * (sm != null ? sm.Gain : 1)), 0, 255); kp[i, 4] = (int)Math.Round(p.Pan * 20) & 255; kp[i, 5] = p.Group;
            }
            string[] kn = { "kp_samp", "kp_ulo", "kp_uhi", "kp_vol", "kp_pan", "kp_grp" };
            for (int f = 0; f < 6; f++) Row(sb, kn[f], Enumerable.Range(0, KitLetters.Length).Select(i => kp[i, f]));
            // sfx: record per id (1-based): count, then per part prio, slot, ptr
            int nsfx = (int)SfxId.Count;
            sb.AppendLine("sfx_lo: .db 0" + string.Concat(Enumerable.Range(0, nsfx).Select(i => ", <sfxr_" + i)));
            sb.AppendLine("sfx_hi: .db 0" + string.Concat(Enumerable.Range(0, nsfx).Select(i => ", >sfxr_" + i)));
            for (int i = 0; i < nsfx; i++)
            {
                var parts = sfxParts[i];
                var line = new StringBuilder("sfxr_" + i + ": .db " + parts.Count);
                for (int k = 0; k < parts.Count; k++) line.Append(", " + parts[k].Prio + ", " + parts[k].Slot + ", <sfxp_" + i + "_" + k + ", >sfxp_" + i + "_" + k);
                sb.AppendLine(line.ToString());
                for (int k = 0; k < parts.Count; k++)
                {
                    parts[k].Code = SfxCode(parts[k].Def);
                    Row(sb, "sfxp_" + i + "_" + k, parts[k].Code);
                }
            }
            // sample directory (page aligned)
            sb.AppendLine(".align 256");
            sb.AppendLine("dir:");
            foreach (var x in Samples)
            {
                if (x.Resident) sb.AppendLine(" .dw smp_" + x.Id + ", smp_" + x.Id + "+" + x.LoopByte);
                else { int a; dynAddr.TryGetValue(x.Id, out a); sb.AppendLine(" .dw " + a + ", " + (a + x.LoopByte)); }
            }
            sb.AppendLine("res_samples:");
            foreach (var x in Samples.Where(y => y.Resident))
            {
                sb.AppendLine("smp_" + x.Id + ":");
                var bb = x.Brr.ToList(); if ((bb.Count & 1) != 0) bb.Add(0);
                for (int o = 0; o < bb.Count; o += 32) Row(sb, null, bb.Skip(o).Take(32).Select(v => (int)v));
            }
            sb.AppendLine(".align 2");
            sb.AppendLine("res_end:");
            return sb.ToString();
        }

        static void Row(StringBuilder sb, string label, IEnumerable<int> vals)
        {
            var l = vals.ToList();
            if (label != null) sb.Append(label + ":");
            if (l.Count == 0) { sb.AppendLine(); return; }
            for (int o = 0; o < l.Count; o += 24)
            {
                sb.Append(o == 0 && label != null ? " .db " : "  .db ");
                sb.AppendLine(string.Join(",", l.Skip(o).Take(24).Select(v => ((int)v & 255).ToString(CultureInfo.InvariantCulture))));
            }
        }
        static void Row(StringBuilder sb, string label, IEnumerable<byte> vals) { Row(sb, label, vals.Select(v => (int)v)); }

        static Dictionary<int, int> lastDyn = new Dictionary<int, int>();

        static void BuildImage()
        {
            string drv = File.ReadAllText(Path.Combine(root, "snes", "spc", "driver.asm"));
            string head = "MVOL = " + MVol + "\n" + string.Concat(Fir.Select((f, i) => "FIR" + i + " = " + f + "\n")) +
                "DRIVER_ORG = $" + DriverOrg.ToString("X4") + "\n" + "NSFX = " + (int)SfxId.Count + "\n";
            string text = head + drv + "\n" + GenTables(lastDyn);
            string tmp = Path.Combine(Path.GetTempPath(), "smb4_spc_driver_full.asm");
            File.WriteAllText(tmp, text);
            var asm = SpcAsm.Assemble(tmp, null);
            if (asm.Errors.Count > 0) { foreach (var e in asm.Errors.Take(30)) Console.WriteLine("  spcasm: " + e); throw new Exception("SPC700 driver assembly failed"); }
            Syms = asm.Symbols;
            ResEnd = Syms["res_end"]; DirAddr = Syms["dir"];
            Image = new byte[ResEnd - DriverOrg];
            Array.Copy(asm.Mem, DriverOrg, Image, 0, Image.Length);
            if (ResEnd > 0x8000) throw new Exception("resident APU image too big: $" + ResEnd.ToString("X4"));
            asmText = text;
        }
        static string asmText;

        // ================================================================== APU memory allocation
        /// <summary>Places song blobs under each song's echo buffer and dynamic samples at fixed addresses so that samples a song
        /// needs never overlap each other, its blob or its echo buffer. Returns the first song that does not fit, or null.</summary>
        // song pairs that should be able to alternate without re-uploading samples (level theme <-> in-level jingles)
        static readonly List<int[]> softPairs = new List<int[]>();
        static readonly string[] LevelSongs = { "overworld", "athletic", "underground", "underwater", "desert", "snow", "jungle", "fortress", "airship", "castle", "bonus", "boss", "bowser" };
        static readonly string[] InLevelJingles = { "star", "pswitch", "hurry", "death", "clear" };
        static void AddSoftPairs()
        {
            softPairs.Clear();
            int ok = 0, tried = 0;
            foreach (var j in InLevelJingles)
                foreach (var l in LevelSongs)
                {
                    var a = Songs.FirstOrDefault(s => s.Name == l); var b = Songs.FirstOrDefault(s => s.Name == j);
                    if (a == null || b == null) continue;
                    tried++;
                    softPairs.Add(new[] { a.Id, b.Id });
                    bool important = j != "death" && j != "clear";
                    var changed = new List<SSample>();
                    SSong fail;
                    while ((fail = Allocate()) != null && important)
                    {
                        // lower the quality of the largest sample of either song (bounded) and retry
                        var v = a.Samples.Concat(b.Samples).Where(x => !x.Resident && x.Quality < (x.Piece >= 0 ? 3 : 5)).OrderByDescending(x => x.Size).FirstOrDefault();
                        if (v == null) break;
                        v.Quality++; Render(v); changed.Add(v);
                    }
                    if (fail != null)
                    {
                        softPairs.RemoveAt(softPairs.Count - 1);
                        foreach (var v in changed) { v.Quality--; }
                        foreach (var v in changed.Distinct()) Render(v);
                    }
                    else ok++;
                }
            Allocate();
            PairInfo = ok + "/" + tried + " level/jingle pairs co-resident";
        }
        public static string PairInfo = "";
        static void ComputePreload()
        {
            // level songs also bring in the samples of their co-resident jingles (when those do not collide)
            foreach (var s in Songs) s.Preload.Clear();
            foreach (var pr in softPairs)
            {
                var L = Songs[pr[0] - 1]; var J = Songs[pr[1] - 1];
                var cand = J.Samples.Where(x => !x.Resident && !L.Samples.Contains(x) && !L.Preload.Contains(x)).ToList();
                bool clash = cand.Any(c => L.Preload.Any(p => p.Addr < c.Addr + c.Size && c.Addr < p.Addr + p.Size));
                if (!clash) L.Preload.AddRange(cand);
            }
        }

        static SSong Allocate()
        {
            // blobs (their size depends on nothing allocated)
            foreach (var s in Songs)
            {
                var tmp = BuildSongBlob(s, 0);
                int size = (tmp.Length + 1) & ~1;
                s.Addr = (s.EchoStart - size) & ~1;
                s.Blob = BuildSongBlob(s, s.Addr);
                if (s.Blob.Length % 2 != 0) s.Blob = s.Blob.Concat(new byte[] { 0 }).ToArray();
            }
            var dyn = Samples.Where(x => !x.Resident).ToList();
            foreach (var x in dyn) x.Addr = -1;
            // most shared first, then biggest
            dyn.Sort((a, b) => a.Songs.Count != b.Songs.Count ? b.Songs.Count.CompareTo(a.Songs.Count) : b.Size.CompareTo(a.Size));
            var placed = new List<SSample>();
            foreach (var x in dyn)
            {
                if (x.Songs.Count == 0) { x.Addr = ResEnd; continue; }
                var ext = new HashSet<int>(x.Songs);
                foreach (var pr in softPairs) { if (x.Songs.Contains(pr[0])) ext.Add(pr[1]); if (x.Songs.Contains(pr[1])) ext.Add(pr[0]); }
                int limit = ext.Min(id => Songs[id - 1].Limit);
                // intervals of co-used samples already placed (co-used = same song, or a song it is paired with)
                var busy = placed.Where(p => p.Songs.Overlaps(ext)).Select(p => new[] { p.Addr, p.Addr + p.Size }).OrderBy(p => p[0]).ToList();
                int a = ResEnd;
                foreach (var iv in busy) { if (a + x.Size <= iv[0]) break; a = Math.Max(a, iv[1]); }
                if (a + x.Size > limit)
                {
                    // report the song with the least room
                    return x.Songs.Select(id => Songs[id - 1]).OrderBy(sg => sg.Limit).First();
                }
                x.Addr = a; placed.Add(x);
            }
            var nd = new Dictionary<int, int>();
            foreach (var x in dyn) nd[x.Id] = x.Addr;
            bool changed = nd.Count != lastDyn.Count || nd.Any(kv => !lastDyn.ContainsKey(kv.Key) || lastDyn[kv.Key] != kv.Value);
            lastDyn = nd;
            return null;
        }

        // ================================================================== outputs
        static int romBytes;

        public static void FinalizeForEmu()
        {
            BuildImage();   // with final dynamic addresses in DIR
            var d = Samples.Where(x => !x.Resident).OrderBy(x => x.Id).ToList();
            for (int i = 0; i < d.Count; i++) d[i].DynIndex = i;
        }

        static void WriteOutputs(string outDir)
        {
            FinalizeForEmu();
            var inc = new StringBuilder();
            inc.AppendLine("; generated by smb4tools snes-export (SnesAudio.cs) - song and sound-effect ids for snd_music / snd_sfx");
            inc.AppendLine("SONG_NONE = 0");
            foreach (var s in Songs) inc.AppendLine("SONG_" + s.Name.ToUpperInvariant() + " = " + s.Id);
            inc.AppendLine("SND_NSONGS = " + Songs.Count);
            for (int i = 0; i < (int)SfxId.Count; i++) inc.AppendLine("SFX_" + ((SfxId)i).ToString().ToUpperInvariant() + " = " + (i + 1));
            inc.AppendLine("SND_NSFX = " + (int)SfxId.Count);
            inc.AppendLine("SFX_STOP = $80   ; snd_sfx with A = SFX_STOP | id stops that (looping) effect");
            File.WriteAllText(Path.Combine(outDir, "music.inc"), inc.ToString());

            // blobs into banks BANK16.. (tables first); banks 32-63 belong to sprites/levels/backgrounds
            var dyn = Samples.Where(x => !x.Resident).OrderBy(x => x.Id).ToList();
            for (int i = 0; i < dyn.Count; i++) dyn[i].DynIndex = i;
            var blobs = new List<KeyValuePair<string, byte[]>>();
            blobs.Add(new KeyValuePair<string, byte[]>("snd_res", Image));
            foreach (var s in Songs) blobs.Add(new KeyValuePair<string, byte[]>("snd_song_" + s.Id, s.Blob));
            foreach (var x in dyn) { var bb = x.Brr; if ((bb.Length & 1) != 0) bb = bb.Concat(new byte[] { 0 }).ToArray(); blobs.Add(new KeyValuePair<string, byte[]>("snd_smp_" + x.Id, bb)); }
            int tableSize = 16 + Songs.Count * 10 + dyn.Count * 10 + Songs.Sum(s => s.Samples.Count(x => !x.Resident)) + 64;
            var bankFill = new List<int>(); var bankItems = new List<List<string>>();
            var where = new Dictionary<string, int>();
            bankFill.Add(tableSize); bankItems.Add(new List<string>());
            foreach (var kv in blobs.OrderByDescending(k => k.Value.Length))
            {
                int b = -1;
                for (int i = 0; i < bankFill.Count; i++) if (bankFill[i] + kv.Value.Length <= 0x8000) { b = i; break; }
                if (b < 0) { bankFill.Add(0); bankItems.Add(new List<string>()); b = bankFill.Count - 1; }
                bankFill[b] += kv.Value.Length; bankItems[b].Add(kv.Key); where[kv.Key] = b;
                File.WriteAllBytes(Path.Combine(outDir, kv.Key + ".bin"), kv.Value);
            }
            romBytes = bankFill.Sum();
            if (bankFill.Count > MaxBanks) throw new Exception("sound data needs " + bankFill.Count + " banks");
            var sb = new StringBuilder();
            sb.AppendLine("; generated by smb4tools snes-export (SnesAudio.cs): SPC700 driver image, songs, samples + upload tables for snd.s");
            sb.AppendLine(".p816");
            sb.AppendLine(".export snd_res, snd_song_tab, snd_smp_tab, snd_song_smp");
            sb.AppendLine("SND_RES_ORG = $" + DriverOrg.ToString("X4"));
            sb.AppendLine("SND_RES_LEN = " + Image.Length);
            sb.AppendLine("SND_ENTRY = $" + Syms["start"].ToString("X4"));
            sb.AppendLine("SND_NDYN = " + dyn.Count);
            sb.AppendLine(".export SND_RES_ORG:abs, SND_RES_LEN:abs, SND_ENTRY:abs, SND_NDYN:abs");
            for (int b = 0; b < bankFill.Count; b++)
            {
                sb.AppendLine(".segment \"BANK" + (FirstBank + b) + "\": far");
                if (b == 0)
                {
                    // song table: blob(3) len(2) dest(2) listptr(2) count(1) = 10 bytes; index 0 = unused
                    sb.AppendLine("snd_song_tab:");
                    sb.AppendLine("  .byte 0,0,0,0,0,0,0,0,0,0");
                    foreach (var s in Songs)
                        sb.AppendLine("  .faraddr snd_song_" + s.Id + "\n  .word " + s.Blob.Length + ", $" + s.Addr.ToString("X4") + ", .loword(snd_ssl_" + s.Id + ")\n  .byte " + s.Samples.Count(x => !x.Resident));
                    sb.AppendLine("snd_smp_tab:   ; blob(3) len(2) addr(2) last(2) pad(1)");
                    foreach (var x in dyn)
                        sb.AppendLine("  .faraddr snd_smp_" + x.Id + "\n  .word " + x.Size + ", $" + x.Addr.ToString("X4") + ", $" + (x.Addr + x.Size - 1).ToString("X4") + "\n  .byte 0");
                    sb.AppendLine("snd_song_smp:");
                    foreach (var s in Songs)
                    {
                        var l = s.Samples.Where(x => !x.Resident).Select(x => x.DynIndex).ToList();
                        sb.AppendLine("snd_ssl_" + s.Id + ": .byte " + (l.Count == 0 ? "0" : string.Join(",", l)));
                    }
                }
                foreach (var k in bankItems[b]) sb.AppendLine(k + ": .incbin \"" + k + ".bin\"");
            }
            File.WriteAllText(Path.Combine(outDir, "snd_data.s"), sb.ToString());
            File.WriteAllText(Path.Combine(outDir, "spc_driver_full.asm"), asmText);
            // symbol listing for tests (Lua reads SPC RAM by these addresses)
            var sym = new StringBuilder();
            foreach (var kv in Syms.OrderBy(k => k.Value)) if (!kv.Key.Contains("@") && !kv.Key.StartsWith("sfxp_") && !kv.Key.StartsWith("smp_")) sym.AppendLine(kv.Key + " = $" + kv.Value.ToString("X4"));
            File.WriteAllText(Path.Combine(outDir, "spc_driver.sym"), sym.ToString());
        }
    }
}
