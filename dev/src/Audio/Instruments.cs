using System;
using System.Collections.Generic;
using SMB4.Platform;

namespace SMB4.Audio
{
    /// <summary>A mono PCM sample at the output rate. Played back with a resampling step, SPC700-style.</summary>
    public sealed class Sample
    {
        public float[] D;          // Len + 1 floats (guard sample for interpolation)
        public int Len;
        public int LoopStart = -1; // >= 0: loops [LoopStart, Len)
        public double BaseHz;      // pitch at step 1.0 (0 = unpitched: step is a playback-rate multiplier)
    }

    /// <summary>An instrument: one or more key-zones of generated samples plus an ADSR (seconds).</summary>
    public sealed class Instrument
    {
        public string Name = ""; public int Id;
        public Sample[] Zones; public double[] ZoneHz;
        public float Attack = 0.002f, Decay = 0.3f, Sustain = 1f, SusDecay, Release = 0.1f, Gain = 1f;
        public bool Kit;

        public Sample Pick(double hz)
        {
            if (Zones.Length == 1 || hz <= 0) return Zones[0];
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < Zones.Length; i++)
            {
                double d = Math.Abs(Math.Log(hz / ZoneHz[i]));
                if (d < bd) { bd = d; best = i; }
            }
            return Zones[best];
        }
    }

    /// <summary>One piece of the drum kit: sample, playback rate, level, pan, and which of the channel voices it rings on.</summary>
    public struct KitPiece
    {
        public int Sample; public float Pitch, Vol, Pan; public int Group; // 0 drums, 1 hats/shaker/cowbell, 2 crash/ride (each group has its own voice)
        public KitPiece(int s, float pitch, float vol, float pan, int group) { Sample = s; Pitch = pitch; Vol = vol; Pan = pan; Group = group; }
    }

    /// <summary>
    /// The instrument bank. Every sound is generated in code at start-up (no sample files): band-limited additive
    /// synthesis with per-partial envelopes (piano, mallets, bells, pads, choir, brass, reeds, plucked strings in the
    /// Karplus-Strong spirit), two-operator FM (e-piano), and noise/sine modelling for the drum kit.
    /// Pitched samples are "attack + seamless loop": every partial sits on an exact bin of the loop length, so the
    /// loop is click-free and detuned ensemble copies beat naturally inside it. The ADSR shapes the rest, like the SNES.
    /// </summary>
    public static class Bank
    {
        const int Rate = AudioOut.Rate;
        public static readonly List<Instrument> List = new List<Instrument>();
        static readonly Dictionary<string, Instrument> byName = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
        public static Sample[] KitSamples;
        public static readonly Dictionary<char, KitPiece> Kit = new Dictionary<char, KitPiece>();
        public static int KitId = -1;
        static readonly Dictionary<int, float[]> sinCache = new Dictionary<int, float[]>();
        static bool built;
        static readonly object buildLock = new object();

        public static Instrument Get(int id) { return id >= 0 && id < List.Count ? List[id] : null; }
        public static int Find(string name) { EnsureBuilt(); Instrument i; return byName.TryGetValue(name, out i) ? i.Id : -1; }
        public static IEnumerable<string> Names { get { EnsureBuilt(); return byName.Keys; } }

        public static void EnsureBuilt()
        {
            if (built) return;
            lock (buildLock)
            {
                if (built) return;
                Build();
                sinCache.Clear();
                built = true;
            }
        }

        // ---------------------------------------------------------------- zone builder
        public sealed class ZB
        {
            public double F0; public int Lc, Pd, L, A; public double MaxHz = 11000; public int MaxHarm = 40;
            internal readonly List<double[]> parts = new List<double[]>();   // ratio, amp, final, tau, det
            internal readonly List<double[]> noises = new List<double[]>();  // amp, tau, lp, hp
            internal readonly List<double[]> fms = new List<double[]>();     // rc, rm, amp, i0, i1, tau, det
            public void H(double ratio, double amp, double final, double tau = 0.1, double det = 0) { parts.Add(new[] { ratio, amp, final, tau, det }); }
            public void H(double ratio, double amp) { H(ratio, amp, amp, 1, 0); }
            public void Noise(double amp, double tau, double lp, double hp) { noises.Add(new[] { amp, tau, lp, hp }); }
            public void FM(double rc, double rm, double amp, double i0, double i1, double tau, double det = 0) { fms.Add(new[] { rc, rm, amp, i0, i1, tau, det }); }
            /// <summary>Adds harmonics 1..n: amp(h, freq), final fraction(h), tau(h).</summary>
            public void Harm(Func<int, double, double> amp, Func<int, double> finalFrac, Func<int, double> tau, double det = 0, double gain = 1)
            {
                for (int h = 1; h <= MaxHarm; h++)
                {
                    double f = h * F0; if (f > MaxHz) break;
                    double a = amp(h, f) * gain; if (Math.Abs(a) < 1e-4) continue;
                    H(h, a, a * finalFrac(h), tau(h), det);
                }
            }
            public void Harm(Func<int, double, double> amp, double det = 0, double gain = 1) { Harm(amp, h => 1, h => 1, det, gain); }
        }

        static float[] SinTable(int n)
        {
            float[] t;
            if (sinCache.TryGetValue(n, out t)) return t;
            t = new float[n];
            for (int i = 0; i < n; i++) t[i] = (float)Math.Sin(2 * Math.PI * i / n);
            sinCache[n] = t;
            return t;
        }

        static double MidiHz(double m) { return 440.0 * Math.Pow(2, (m - 69) / 12.0); }

        static Sample RenderZone(double centerMidi, double attackSec, double loopSec, int minCycles, int seed, Action<ZB> build)
        {
            var z = new ZB();
            double fc = MidiHz(centerMidi);
            z.Pd = Math.Max(8, (int)Math.Round(Rate / fc));
            z.F0 = Rate / (double)z.Pd;
            z.Lc = Math.Max(minCycles, (int)Math.Round(loopSec * z.F0));
            z.L = z.Lc * z.Pd;
            z.A = (int)(attackSec * Rate);
            build(z);
            int total = z.A + z.L;
            var buf = new float[total + 1];
            var sin = SinTable(z.L);
            var rng = new Random(seed);
            int L = z.L, A = z.A;
            foreach (var p in z.parts)
            {
                double ratio = p[0], amp = p[1], fin = p[2], tau = p[3], det = p[4];
                if (ratio * z.F0 > z.MaxHz * 1.02 || ratio * z.F0 < 8) continue;
                int n = (int)Math.Round(ratio * (z.Lc + det));
                if (n <= 0 || n >= L / 2) continue;
                int idx = rng.Next(L);
                if (amp == fin || tau <= 0)
                {
                    float a = (float)fin;
                    for (int i = 0; i < total; i++) { buf[i] += a * sin[idx]; idx += n; if (idx >= L) idx -= L; }
                }
                else
                {
                    double k = Math.Exp(-1.0 / (tau * Rate)), e = 1, eA = Math.Pow(k, A), inv = 1.0 / (1.0 - eA);
                    for (int i = 0; i < A; i++)
                    {
                        float a = (float)(fin + (amp - fin) * (e - eA) * inv); e *= k;
                        buf[i] += a * sin[idx]; idx += n; if (idx >= L) idx -= L;
                    }
                    float f = (float)fin;
                    if (f != 0) for (int i = A; i < total; i++) { buf[i] += f * sin[idx]; idx += n; if (idx >= L) idx -= L; }
                }
            }
            foreach (var p in z.fms)
            {
                double rc = p[0], rm = p[1], amp = p[2], i0 = p[3], i1 = p[4], tau = p[5], det = p[6];
                int nc = (int)Math.Round(rc * (z.Lc + det)), nm = (int)Math.Round(rm * (z.Lc + det));
                int ic = 0, im = 0;
                double k = Math.Exp(-1.0 / (tau * Rate)), e = 1, eA = Math.Pow(k, A), inv = 1.0 / (1.0 - eA);
                double scale = L / (2 * Math.PI);
                for (int i = 0; i < total; i++)
                {
                    double I = i < A ? i1 + (i0 - i1) * (e - eA) * inv : i1; e *= k;
                    double ph = ic + I * sin[im] * scale;
                    int j = (int)ph % L; if (j < 0) j += L;
                    buf[i] += (float)(amp * sin[j]);
                    ic += nc; if (ic >= L) ic -= L; im += nm; if (im >= L) im -= L;
                }
            }
            foreach (var p in z.noises)
            {
                double amp = p[0], tau = p[1], lp = LpA(p[2]), hp = LpA(p[3]);
                double y1 = 0, y2 = 0, h1 = 0;
                for (int i = 0; i < A; i++)
                {
                    double t = i / (double)Rate, w = 1 - i / (double)A;
                    double x = rng.NextDouble() * 2 - 1;
                    y1 += lp * (x - y1); y2 += lp * (y1 - y2);
                    h1 += hp * (y2 - h1);
                    buf[i] += (float)((y2 - h1) * amp * 2.5 * Math.Exp(-t / tau) * w * w);
                }
            }
            float peak = 1e-6f;
            for (int i = 0; i < total; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
            float g = 1f / peak;
            for (int i = 0; i < total; i++) buf[i] *= g;
            buf[total] = buf[A];
            return new Sample { D = buf, Len = total, LoopStart = A, BaseHz = z.F0 };
        }

        static double LpA(double hz) { return 1 - Math.Exp(-2 * Math.PI * Math.Min(hz, Rate * 0.45) / Rate); }
        static double Gauss(double f, double c, double w) { double d = (f - c) / w; return Math.Exp(-d * d); }
        static double Roll(double f, double fc) { return 1.0 / (1.0 + (f / fc) * (f / fc)); }

        static int seedCounter = 100;
        static Instrument Def(string name, int[] zones, float a, float d, float s, float sd, float r, float gain,
            double attackSec, double loopSec, int minCycles, Action<ZB> build)
        {
            var ins = new Instrument { Name = name, Attack = a, Decay = d, Sustain = s, SusDecay = sd, Release = r, Gain = gain };
            ins.Zones = new Sample[zones.Length]; ins.ZoneHz = new double[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                ins.Zones[i] = RenderZone(zones[i], attackSec, loopSec, minCycles, seedCounter++, build);
                ins.ZoneHz[i] = ins.Zones[i].BaseHz;
            }
            Add(ins);
            return ins;
        }

        static void Add(Instrument ins) { ins.Id = List.Count; List.Add(ins); byName[ins.Name] = ins; }

        // ---------------------------------------------------------------- the bank
        static void Build()
        {
            int[] Z3 = { 36, 60, 84 }, Z3h = { 48, 72, 96 }, Zmid = { 60, 84 }, Zlow = { 36, 60 }, Zhi = { 72, 96 }, Z2 = { 48, 72 };

            // Keys
            Def("piano", Z3, 0.001f, 0.5f, 0.45f, 3.0f, 0.25f, 1.0f, 0.45, 0.02, 24, z =>
            {
                for (int h = 1; h <= 30; h++)
                {
                    double r = h * Math.Sqrt(1 + 0.00035 * h * h), f = r * z.F0; if (f > z.MaxHz) break;
                    double a = Math.Pow(h, -1.1) * (0.4 + 0.6 * Math.Abs(Math.Sin(Math.PI * h * 0.12)));
                    z.H(r, a, a * (h == 1 ? 0.6 : 0.4 * Math.Pow(h, -0.8)), 0.3 / Math.Pow(h, 0.6));
                    if (h <= 4) z.H(r, a * 0.25, a * 0.2, 0.3, 0.6); // second string, slightly out
                }
                z.Noise(0.12, 0.006, 2500, 150);
            });
            Def("epiano", Z2, 0.001f, 0.8f, 0.5f, 2.5f, 0.3f, 0.9f, 0.5, 0.02, 12, z =>
            {
                z.FM(1, 1, 0.8, 2.0, 0.25, 0.15);
                z.H(1, 0.5);
                z.H(11, 0.25, 0, 0.012);
                z.FM(1, 1, 0.3, 1.0, 0.2, 0.2, 0.7);
            });
            Def("organ", Z3h, 0.008f, 0.1f, 1f, 0, 0.08f, 0.7f, 0.05, 0.4, 60, z =>
            {
                double[] dr = { 1, 0.8, 0.5, 0.55, 0, 0.3, 0, 0.3 };
                for (int h = 1; h <= 8; h++) if (dr[h - 1] > 0 && h * z.F0 < z.MaxHz) { z.H(h, dr[h - 1] * 0.6); z.H(h, dr[h - 1] * 0.4, dr[h - 1] * 0.4, 1, 0.5); }
            });
            Def("musicbox", Zhi, 0.001f, 0.4f, 0.45f, 1.2f, 0.5f, 0.8f, 0.3, 0.02, 20, z =>
            {
                z.H(1, 1, 0.8, 0.3); z.H(2, 0.25, 0.05, 0.1); z.H(5.4, 0.3, 0, 0.03); z.H(9.1, 0.12, 0, 0.012);
                z.Noise(0.05, 0.002, 8000, 2000);
            });

            // Mallets & bells
            Def("marimba", Zmid, 0.001f, 0.15f, 0.4f, 0.45f, 0.12f, 1.0f, 0.25, 0.02, 20, z =>
            {
                z.H(1, 1, 0.7, 0.25); z.H(3.93, 0.45, 0, 0.03); z.H(9.3, 0.15, 0, 0.01);
                z.Noise(0.12, 0.004, 2500, 300);
            });
            Def("xylo", Zhi, 0.001f, 0.08f, 0.3f, 0.3f, 0.1f, 0.9f, 0.2, 0.02, 20, z =>
            {
                z.H(1, 1, 0.6, 0.15); z.H(3.0, 0.55, 0, 0.04); z.H(6.1, 0.25, 0, 0.015);
                z.Noise(0.2, 0.003, 6000, 800);
            });
            Def("vibes", Zmid, 0.001f, 0.5f, 0.6f, 2.0f, 0.6f, 0.9f, 0.3, 0.4, 32, z =>
            {
                double det = 5.0 / (z.F0 / z.Lc); // twin partial beating at ~5 Hz = motor tremolo
                z.H(1, 1, 0.75, 0.5); z.H(1, 0.3, 0.3, 1, det);
                z.H(4.0, 0.35, 0.05, 0.15); z.H(10, 0.12, 0, 0.03);
                z.Noise(0.05, 0.003, 5000, 1000);
            });
            Def("steeldrum", Zmid, 0.001f, 0.3f, 0.5f, 0.8f, 0.2f, 1.0f, 0.35, 0.02, 20, z =>
            {
                z.H(1, 1, 0.6, 0.3); z.H(2.0, 0.8, 0.25, 0.12); z.H(3.0, 0.45, 0.08, 0.08);
                z.H(4.0, 0.2, 0, 0.05); z.H(5.05, 0.1, 0, 0.03);
                z.Noise(0.08, 0.003, 4000, 500);
            });
            Def("glock", Zhi, 0.001f, 0.5f, 0.6f, 1.6f, 0.7f, 0.75f, 0.3, 0.02, 24, z =>
            {
                z.H(1, 1, 0.8, 0.4); z.H(2.76, 0.45, 0.05, 0.15); z.H(5.4, 0.3, 0, 0.06); z.H(8.93, 0.15, 0, 0.03);
                z.Noise(0.06, 0.002, 8000, 2000);
            });
            Def("bell", Zmid, 0.001f, 1.0f, 0.5f, 2.5f, 1.0f, 0.8f, 0.6, 0.05, 40, z =>
            {
                z.H(0.5, 0.35, 0.3, 1); z.H(1, 1, 0.7, 0.6); z.H(1.2, 0.4, 0.2, 0.4); z.H(1.5, 0.25, 0.1, 0.3);
                z.H(2.0, 0.4, 0.1, 0.3); z.H(2.5, 0.2, 0, 0.15); z.H(3.0, 0.15, 0, 0.1); z.H(4.2, 0.1, 0, 0.05);
            });
            Def("timpani", new[] { 36, 48 }, 0.001f, 1.2f, 0f, 0, 0.6f, 1.1f, 0.6, 0.02, 24, z =>
            {
                z.H(1, 1, 0.5, 0.5); z.H(1.5, 0.5, 0.1, 0.3); z.H(1.98, 0.35, 0.05, 0.25); z.H(2.44, 0.25, 0, 0.15); z.H(2.94, 0.12, 0, 0.1);
                z.Noise(0.45, 0.02, 700, 40);
            });

            // Strings, pads, voices
            Def("strings", Z3h, 0.15f, 0.5f, 0.9f, 0, 0.4f, 0.8f, 0.12, 0.5, 100, z =>
            {
                foreach (var det in new[] { -1.0, 0.0, 1.1 })
                    z.Harm((h, f) => Roll(f, 4000) / h, det, 0.6);
                z.Noise(0.03, 0.05, 3000, 800);
            });
            Def("pizz", Z2, 0.001f, 0.12f, 0.25f, 0.25f, 0.1f, 0.9f, 0.25, 0.02, 16, z =>
            {
                z.Harm((h, f) => Roll(f, 3000) / h, h => 0.3 / h, h => 0.08 / Math.Sqrt(h));
                z.Noise(0.1, 0.005, 2500, 300);
            });
            Def("pad", Z2, 0.4f, 1.0f, 0.85f, 0, 0.8f, 0.7f, 0.05, 0.5, 100, z =>
            {
                foreach (var det in new[] { -0.8, 0.8 })
                    z.Harm((h, f) => Roll(f, 1500) / h, det, 0.7);
                z.H(0.5, 0.3);
            });
            Def("choir", Z2, 0.25f, 0.5f, 0.9f, 0, 0.5f, 0.8f, 0.05, 0.5, 100, z =>
            {
                foreach (var det in new[] { -1.0, 0.0, 1.0 })
                    z.Harm((h, f) => (Gauss(f, 700, 180) + 0.6 * Gauss(f, 1150, 220) + 0.25 * Gauss(f, 2600, 350) + 0.04) / Math.Sqrt(h), det, 0.6);
            });

            // Winds & brass
            Def("brass", Z3h, 0.03f, 0.4f, 0.8f, 0, 0.12f, 0.8f, 0.2, 0.5, 60, z =>
            {
                foreach (var det in new[] { 0.0, 1.0 })
                    z.Harm((h, f) => Math.Pow(h, -0.85) * Roll(f, 6000), h => h == 1 ? 1.25 : 2.5 + 0.2 * h, h => 0.04 + 0.01 * h, det, 0.2);
            });
            Def("trumpet", Zmid, 0.02f, 0.3f, 0.85f, 0, 0.1f, 0.8f, 0.2, 0.02, 16, z =>
            {
                z.Harm((h, f) => Math.Pow(h, -0.75) * Roll(f, 7000), h => h == 1 ? 1.2 : 3 + 0.3 * h, h => 0.03 + 0.008 * h, 0, 0.25);
            });
            Def("flute", Zmid, 0.05f, 0.3f, 0.9f, 0, 0.12f, 0.9f, 0.15, 0.02, 16, z =>
            {
                z.H(1, 1); z.H(2, 0.22); z.H(3, 0.1); z.H(4, 0.04);
                z.Noise(0.25, 0.06, 5000, 1200);
            });
            Def("ocarina", Zmid, 0.03f, 0.2f, 0.95f, 0, 0.1f, 0.9f, 0.1, 0.02, 16, z =>
            {
                z.H(1, 1); z.H(2, 0.04); z.H(3, 0.12); z.H(5, 0.03);
                z.Noise(0.12, 0.04, 3000, 800);
            });
            Def("whistle", new[] { 72, 96 }, 0.015f, 0.2f, 0.95f, 0, 0.06f, 0.85f, 0.08, 0.02, 16, z =>
            {
                z.H(1, 1); z.H(2, 0.06); z.H(3, 0.025);
                z.Noise(0.08, 0.03, 6000, 2000);
            });
            Def("oboe", Zmid, 0.03f, 0.3f, 0.9f, 0, 0.08f, 0.8f, 0.08, 0.02, 16, z =>
            {
                z.Harm((h, f) => (0.15 + Gauss(f, 1100, 500) + 0.4 * Gauss(f, 2900, 600)) / Math.Pow(h, 0.3));
            });
            Def("clarinet", Z2, 0.03f, 0.3f, 0.9f, 0, 0.08f, 0.8f, 0.08, 0.02, 16, z =>
            {
                z.Harm((h, f) => (h % 2 == 1 ? 1.0 : 0.12) / h * Roll(f, 3000));
            });

            // Synths
            Def("lead", Zmid, 0.004f, 0.4f, 0.75f, 0, 0.1f, 0.7f, 0.05, 0.4, 60, z =>
            {
                foreach (var det in new[] { 0.0, 0.6 })
                    z.Harm((h, f) => Math.Abs(Math.Sin(Math.PI * h * 0.3)) / h * Roll(f, 7000), det, 0.6);
            });
            Def("sawlead", Zmid, 0.004f, 0.4f, 0.75f, 0, 0.1f, 0.65f, 0.05, 0.4, 60, z =>
            {
                foreach (var det in new[] { -0.5, 0.5 })
                    z.Harm((h, f) => Roll(f, 6000) / h, det, 0.6);
            });

            Def("snaplead", Zmid, 0.002f, 0.12f, 0.5f, 0.8f, 0.05f, 0.75f, 0.08, 0.4, 60, z =>
            {
                foreach (var det in new[] { 0.0, 0.7 })
                    z.Harm((h, f) => Math.Abs(Math.Sin(Math.PI * h * 0.25)) / h * Roll(f, 8000), h => 0.7, h => 0.05, det, 0.6);
            });

            // Basses & plucks
            Def("tuba", new[] { 36, 48 }, 0.02f, 0.25f, 0.75f, 0, 0.08f, 1.1f, 0.15, 0.02, 12, z =>
            {
                z.Harm((h, f) => Math.Pow(h, -1.2) * Roll(f, 1400), h => h == 1 ? 1.1 : 1.8, h => 0.04, 0, 0.3);
            });
            Def("bass", Zlow, 0.002f, 0.5f, 0.55f, 1.5f, 0.08f, 1.1f, 0.3, 0.02, 12, z =>
            {
                z.Harm((h, f) => Math.Pow(h, -1.3) * Roll(f, 2500), h => h == 1 ? 0.8 : 0.4 / Math.Sqrt(h), h => 0.15 / Math.Sqrt(h));
                z.Noise(0.06, 0.004, 1500, 100);
            });
            Def("slapbass", Zlow, 0.001f, 0.25f, 0.45f, 1.0f, 0.07f, 1.0f, 0.3, 0.02, 12, z =>
            {
                z.Harm((h, f) => Math.Pow(h, -0.9) * Roll(f, 5000), h => h == 1 ? 0.7 : 0.25 / h, h => 0.06 / Math.Pow(h, 0.3));
                z.Noise(0.3, 0.003, 4000, 800);
            });
            Def("synbass", Zlow, 0.001f, 0.3f, 0.7f, 0, 0.06f, 0.9f, 0.3, 0.02, 12, z =>
            {
                Func<double, double, double> lp = (f, fc) => 1.0 / Math.Sqrt(1 + Math.Pow(f / fc, 4)) + 1.2 * Gauss(f, fc, fc * 0.25);
                for (int h = 1; h <= 40 && h * z.F0 < z.MaxHz; h++)
                {
                    double f = h * z.F0, a = 1.0 / h;
                    z.H(h, a * lp(f, 3000), a * lp(f, 450), 0.07);
                }
            });
            Def("guitar", Z2, 0.001f, 0.6f, 0.35f, 1.2f, 0.15f, 1.0f, 0.4, 0.02, 12, z =>
            {
                z.Harm((h, f) => Math.Abs(Math.Sin(Math.PI * h * 0.18)) / h * Roll(f, 5000), h => 0.5 / Math.Sqrt(h), h => 0.4 / Math.Pow(h, 0.7));
                z.Noise(0.08, 0.003, 4000, 500);
            });
            Def("harp", Zmid, 0.001f, 0.8f, 0.4f, 1.8f, 0.4f, 1.0f, 0.4, 0.02, 12, z =>
            {
                z.Harm((h, f) => Math.Abs(Math.Sin(Math.PI * h * 0.3)) * Math.Pow(h, -1.4), h => 0.6 / Math.Sqrt(h), h => 0.5 / Math.Pow(h, 0.6));
                z.Noise(0.04, 0.003, 3000, 400);
            });
            Def("orchhit", Z2, 0.001f, 0.25f, 0f, 0, 0.2f, 1.0f, 0.45, 0.02, 16, z =>
            {
                foreach (var r in new[] { 1.0, 1.5, 2.0, 2.52, 3.0, 4.0 })
                    for (int h = 1; h <= 12; h++) { double f = r * h * z.F0; if (f > z.MaxHz) break; z.H(r * h, 0.8 / h / r, 0, 0.1 + 0.05 / h); }
                z.Noise(0.4, 0.03, 5000, 300);
            });

            // Raw waves (mainly for sound effects)
            Def("sine", Z3h, 0.002f, 0.1f, 1f, 0, 0.05f, 0.8f, 0.01, 0.01, 1, z => z.H(1, 1));
            Def("square", Z3h, 0.002f, 0.1f, 1f, 0, 0.05f, 0.55f, 0.01, 0.01, 1, z => z.Harm((h, f) => h % 2 == 1 ? 1.0 / h : 0));
            Def("pulse", Z3h, 0.002f, 0.1f, 1f, 0, 0.05f, 0.55f, 0.01, 0.01, 1, z => z.Harm((h, f) => Math.Sin(Math.PI * h * 0.25) / h));
            Def("saw", Z3h, 0.002f, 0.1f, 1f, 0, 0.05f, 0.5f, 0.01, 0.01, 1, z => z.Harm((h, f) => 1.0 / h));
            Def("tri", Z3h, 0.002f, 0.1f, 1f, 0, 0.05f, 0.8f, 0.01, 0.01, 1, z => z.Harm((h, f) => h % 2 == 1 ? ((h / 2) % 2 == 0 ? 1.0 : -1.0) / (h * h) : 0));

            // Noise: 1 s of white noise, looped. "Pitch" changes its colour: 440 Hz = full band.
            {
                var rng = new Random(77);
                int n = Rate;
                var d = new float[n + 1];
                for (int i = 0; i < n; i++) d[i] = (float)(rng.NextDouble() * 2 - 1);
                d[n] = d[0];
                Add(new Instrument { Name = "noise", Zones = new[] { new Sample { D = d, Len = n, LoopStart = 0, BaseHz = 440 } }, ZoneHz = new[] { 440.0 }, Attack = 0.001f, Decay = 0.1f, Sustain = 1, Release = 0.05f, Gain = 0.5f });
            }

            BuildKit();
            var kit = new Instrument { Name = "kit", Kit = true, Zones = KitSamples, ZoneHz = new double[KitSamples.Length], Attack = 0.0005f, Decay = 1, Sustain = 1, Release = 0.03f, Gain = 1f };
            Add(kit);
            KitId = kit.Id;
        }

        // ---------------------------------------------------------------- drum kit
        public const int KKick = 0, KSnare = 1, KHat = 2, KOpenHat = 3, KCrash = 4, KRide = 5, KTom = 6, KConga = 7, KBongo = 8,
            KClap = 9, KShaker = 10, KRim = 11, KCowbell = 12, KBoom = 13, KThud = 14, KWood = 15, KCount = 16;

        static void BuildKit()
        {
            var k = new Sample[KCount];
            var rng = new Random(4242);
            k[KKick] = Drum(0.38, rng, KickGen);
            k[KSnare] = Drum(0.3, rng, SnareGen);
            k[KHat] = Drum(0.08, rng, (t, st) => Metal(t, st) * Math.Exp(-t / 0.014));
            k[KOpenHat] = Drum(0.5, rng, (t, st) => Metal(t, st) * Math.Exp(-t / 0.13));
            k[KCrash] = Drum(1.9, rng, (t, st) => (Metal(t, st) * 0.7 + st.HpNoise(3500) * 0.6) * Math.Exp(-t / 0.55) * Math.Min(1, t / 0.002));
            k[KRide] = Drum(1.1, rng, (t, st) =>
                (0.4 * Math.Sin(2 * Math.PI * 3720 * t) + 0.3 * Math.Sin(2 * Math.PI * 5230 * t) + 0.2 * Math.Sin(2 * Math.PI * 2880 * t)) * Math.Exp(-t / 0.3)
                + Metal(t, st) * 0.35 * Math.Exp(-t / 0.4));
            k[KTom] = Drum(0.5, rng, (t, st) =>
            {
                st.Ph += 110 * (1 + 0.45 * Math.Exp(-t / 0.05)) / Rate;
                return Math.Sin(2 * Math.PI * st.Ph) * Math.Exp(-t / 0.16) + st.LpNoise(2000) * 0.4 * Math.Exp(-t / 0.01);
            });
            k[KConga] = Drum(0.32, rng, (t, st) =>
            {
                st.Ph += 215 * (1 + 0.18 * Math.Exp(-t / 0.012)) / Rate;
                return Math.Sin(2 * Math.PI * st.Ph) * Math.Exp(-t / 0.075) + 0.1 * Math.Sin(4 * Math.PI * st.Ph) * Math.Exp(-t / 0.03) + st.HpNoise(1500) * 0.35 * Math.Exp(-t / 0.006);
            });
            k[KBongo] = Drum(0.2, rng, (t, st) =>
            {
                st.Ph += 395 * (1 + 0.12 * Math.Exp(-t / 0.008)) / Rate;
                return Math.Sin(2 * Math.PI * st.Ph) * Math.Exp(-t / 0.045) + st.HpNoise(2500) * 0.3 * Math.Exp(-t / 0.004);
            });
            k[KClap] = Drum(0.32, rng, (t, st) =>
            {
                double e = 0;
                for (int b = 0; b < 3; b++) { double tb = t - b * 0.011; if (tb >= 0) e = Math.Max(e, Math.Exp(-tb / 0.004)); }
                if (t > 0.022) e = Math.Max(e, 0.7 * Math.Exp(-(t - 0.022) / 0.07));
                return st.BpNoise(900, 4500) * e * 1.6;
            });
            k[KShaker] = Drum(0.12, rng, (t, st) => st.HpNoise(5000) * Math.Min(1, t / 0.015) * Math.Exp(-t / 0.025));
            k[KRim] = Drum(0.09, rng, (t, st) =>
                (Math.Sin(2 * Math.PI * 1700 * t) * 0.6 + Math.Sin(2 * Math.PI * 480 * t) * 0.5) * Math.Exp(-t / 0.01) + st.HpNoise(3000) * 0.3 * Math.Exp(-t / 0.003));
            k[KWood] = Drum(0.12, rng, (t, st) =>
                (Math.Sin(2 * Math.PI * 880 * t) * 0.8 + Math.Sin(2 * Math.PI * 1390 * t) * 0.35) * Math.Exp(-t / 0.018) + st.HpNoise(2500) * 0.25 * Math.Exp(-t / 0.002));
            k[KCowbell] = Drum(0.4, rng, (t, st) =>
            {
                double sq = Math.Sign(Math.Sin(2 * Math.PI * 562 * t)) + Math.Sign(Math.Sin(2 * Math.PI * 845 * t));
                return st.Bp(sq, 500, 3000) * (0.7 * Math.Exp(-t / 0.02) + 0.3 * Math.Exp(-t / 0.12));
            });
            k[KBoom] = Drum(1.0, rng, (t, st) =>
            {
                st.Ph += (40 + 60 * Math.Exp(-t / 0.08)) / Rate;
                return (Math.Sin(2 * Math.PI * st.Ph) * 0.8 + st.LpNoise(450) * 1.2) * Math.Exp(-t / 0.28);
            });
            k[KThud] = Drum(0.4, rng, (t, st) =>
            {
                st.Ph += (40 + 45 * Math.Exp(-t / 0.04)) / Rate;
                return (Math.Sin(2 * Math.PI * st.Ph) + st.LpNoise(300) * 0.6) * Math.Exp(-t / 0.09);
            });
            KitSamples = k;

            // letter -> piece. Drums, hats and cymbals each ring on their own voice of the channel.
            Kit['k'] = new KitPiece(KKick, 1f, 1.0f, 0f, 0);
            Kit['s'] = new KitPiece(KSnare, 1f, 0.85f, 0.05f, 0);
            Kit['h'] = new KitPiece(KHat, 1f, 0.45f, 0.3f, 1);
            Kit['n'] = new KitPiece(KOpenHat, 1f, 0.45f, 0.3f, 1);
            Kit['c'] = new KitPiece(KCrash, 1f, 0.6f, -0.3f, 2);
            Kit['e'] = new KitPiece(KRide, 1f, 0.45f, 0.35f, 2);
            Kit['t'] = new KitPiece(KTom, 0.75f, 0.85f, -0.35f, 0);
            Kit['m'] = new KitPiece(KTom, 1.0f, 0.85f, 0f, 0);
            Kit['u'] = new KitPiece(KTom, 1.33f, 0.85f, 0.35f, 0);
            Kit['g'] = new KitPiece(KConga, 1f, 0.75f, -0.25f, 0);
            Kit['f'] = new KitPiece(KConga, 1.4f, 0.7f, -0.15f, 0);
            Kit['z'] = new KitPiece(KBongo, 1f, 0.7f, 0.25f, 0);
            Kit['b'] = new KitPiece(KBongo, 0.78f, 0.7f, 0.15f, 0);
            Kit['y'] = new KitPiece(KClap, 1f, 0.7f, -0.05f, 0);
            Kit['j'] = new KitPiece(KShaker, 1f, 0.4f, 0.4f, 1);
            Kit['d'] = new KitPiece(KRim, 1f, 0.6f, -0.1f, 0);
            Kit['a'] = new KitPiece(KCowbell, 1f, 0.45f, 0.2f, 1);
            Kit['w'] = new KitPiece(KBoom, 1f, 1.0f, 0f, 0);
            Kit['x'] = new KitPiece(KThud, 1f, 1.0f, 0f, 0);
            Kit['i'] = new KitPiece(KWood, 1f, 0.55f, -0.2f, 0);
        }

        sealed class DS
        {
            public Random R; public double Ph, lp1, lp2, hp1, bpa, bpb, mph0, mph1, mph2, mph3, mph4, mph5;
            double N() { return R.NextDouble() * 2 - 1; }
            public double LpNoise(double fc) { double a = LpA(fc); lp1 += a * (N() - lp1); lp2 += a * (lp1 - lp2); return lp2 * 2.2; }
            public double HpNoise(double fc) { double x = N(); double a = LpA(fc); hp1 += a * (x - hp1); return x - hp1; }
            public double BpNoise(double lo, double hi) { return Bp(N(), lo, hi) * 1.8; }
            public double Bp(double x, double lo, double hi) { bpa += LpA(lo) * (x - bpa); double h = x - bpa; bpb += LpA(hi) * (h - bpb); return bpb; }
        }

        static readonly double[] MetalHz = { 205.3, 304.4, 369.6, 522.7, 540.0, 800.0 };
        static double Metal(double t, DS st)
        {
            double s = 0;
            for (int i = 0; i < MetalHz.Length; i++) s += Math.Sign(Math.Sin(2 * Math.PI * MetalHz[i] * 2.6 * t + i));
            double x = s / 6 + (st.R.NextDouble() * 2 - 1) * 0.5;
            // crude high-pass so the cluster sounds like a cymbal
            double a = LpA(6500); st.mph0 += a * (x - st.mph0);
            return (x - st.mph0) * 1.2;
        }

        static double KickGen(double t, DS st)
        {
            st.Ph += (46 + 120 * Math.Exp(-t / 0.028)) / Rate;
            double body = Math.Sin(2 * Math.PI * st.Ph) * Math.Exp(-t / 0.13);
            double click = st.HpNoise(1500) * 0.35 * Math.Exp(-t / 0.003);
            return Math.Tanh((body + click) * 1.6);
        }

        static double SnareGen(double t, DS st)
        {
            double tone = (Math.Sin(2 * Math.PI * 185 * t) * 0.6 + Math.Sin(2 * Math.PI * 330 * t) * 0.35) * Math.Exp(-t / 0.035);
            double nz = st.BpNoise(1200, 9000) * Math.Exp(-t / 0.075);
            return tone + nz * 0.9;
        }

        static Sample Drum(double sec, Random rng, Func<double, DS, double> gen)
        {
            int n = (int)(sec * Rate);
            var d = new float[n + 1];
            var st = new DS { R = rng };
            float peak = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double fade = i > n - 480 ? (n - i) / 480.0 : 1.0;
                d[i] = (float)(gen(t, st) * fade);
                peak = Math.Max(peak, Math.Abs(d[i]));
            }
            for (int i = 0; i < n; i++) d[i] /= peak;
            return new Sample { D = d, Len = n, LoopStart = -1, BaseHz = 0 };
        }
    }
}
