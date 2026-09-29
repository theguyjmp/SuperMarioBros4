using System;
using System.Collections.Generic;
using SMB4.Platform;

namespace SMB4.Audio
{
    public static class Ch
    {
        /// <summary>Music channels (C1..C8 in MML). Each owns three voices so note releases overlap the next notes (kit: drums, hats, cymbals).</summary>
        public const int Count = 8;
        /// <summary>Sound-effect slots (each one voice). They never steal music voices.</summary>
        public const int Sfx = 6;
        public const int S0 = 0, S1 = 1, S2 = 2, S3 = 3, S4 = 4, S5 = 5;
    }

    /// <summary>What a channel / SFX slot asks of its voice this frame (60 Hz control rate).</summary>
    public struct VoiceCtl
    {
        public bool Trigger;   // key-on this frame
        public bool Gate;      // key held (false = release)
        public bool Echo;      // send to the echo unit
        public int Inst;       // Bank instrument id
        public int Piece;      // kit piece letter (kit instrument only)
        public float Freq;     // Hz
        public float Vol;      // linear amplitude 0..1
        public float Pan;      // -1 left .. +1 right
        public int Chord;      // 2 or 3: also sound Freq2 (and Freq3) on the channel's other voices
        public float Freq2, Freq3;
    }

    sealed class Voice
    {
        const int Rate = AudioOut.Rate;
        public float[] D; public int Len, LoopStart, LoopLen; public double BaseHz;
        public double Pos, Step, StepInc; public int RampLeft;
        public bool Active, Kit; public int Stage;
        public float Env, Sus, AtkInc, DCoef, SCoef, RCoef, FastR;
        public float GL, GR, TL, TR; public bool Echo;
        public float Declick, Last;

        static float Coef(float sec) { return sec <= 0 ? 0f : (float)Math.Exp(-1.0 / (sec * Rate)); }

        public void KeyOn(Instrument ins, Sample s, double step, float vol, float pan, bool echo)
        {
            Declick = Active ? Last : 0f;
            D = s.D; Len = s.Len; LoopStart = s.LoopStart; LoopLen = s.Len - Math.Max(0, s.LoopStart); BaseHz = s.BaseHz;
            Pos = 0; Step = step; RampLeft = 0; StepInc = 0;
            Active = true; Kit = ins.Kit; Stage = 0; Env = 0;
            AtkInc = 1f / Math.Max(1f, ins.Attack * Rate);
            Sus = ins.Sustain; DCoef = Coef(ins.Decay); SCoef = ins.SusDecay > 0 ? Coef(ins.SusDecay) : 1f; RCoef = Coef(ins.Release);
            SetGains(vol * ins.Gain, pan); GL = TL; GR = TR;
            Echo = echo;
        }

        public void KeyOff() { if (Active && Stage < 2) Stage = 2; }
        public void Kill() { if (Active) { Stage = 2; RCoef = Coef(0.012f); } }

        public void SetGains(float vol, float pan)
        {
            if (pan < -1) pan = -1; else if (pan > 1) pan = 1;
            double a = (pan + 1) * Math.PI / 4;
            TL = (float)(vol * Math.Cos(a) * 1.41421356); TR = (float)(vol * Math.Sin(a) * 1.41421356);
        }

        public void SetStep(double target, int frameSamples)
        {
            if (Kit || BaseHz <= 0) return;
            double d = target - Step;
            if (Math.Abs(d) < 1e-9) { RampLeft = 0; return; }
            StepInc = d / frameSamples; RampLeft = frameSamples;
        }

        public float Tick()
        {
            int ip = (int)Pos; float fr = (float)(Pos - ip);
            float a = D[ip];
            float x = a + (D[ip + 1] - a) * fr;
            Pos += Step;
            if (RampLeft > 0) { Step += StepInc; RampLeft--; }
            if (Pos >= Len)
            {
                if (LoopStart >= 0) { do Pos -= LoopLen; while (Pos >= Len); }
                else { Active = false; Pos = 0; }
            }
            switch (Stage)
            {
                case 0: Env += AtkInc; if (Env >= 1f) { Env = 1f; Stage = 1; } break;
                case 1:
                    Sus *= SCoef; Env = Sus + (Env - Sus) * DCoef;
                    if (Env < 0.0002f && Sus < 0.0002f) Active = false;
                    break;
                default: Env *= RCoef; if (Env < 0.0002f) Active = false; break;
            }
            float y = x * Env + Declick;
            Declick *= 0.994f;
            Last = y;
            return y;
        }
    }

    /// <summary>
    /// SPC700-flavoured sample synthesizer: 24 music voices (8 channels x 3 for overlapping releases) + 6 SFX voices,
    /// each playing a code-generated instrument sample with linear-interpolated pitch, ADSR, volume and stereo pan,
    /// feeding a stereo mix and a SNES-style echo (delay line + 8-tap FIR low-pass + feedback).
    /// Runs on the audio thread; the sequencers advance once every 800 samples (60 Hz at 48 kHz).
    /// </summary>
    public sealed class Synth : ISampleSource
    {
        const int Rate = AudioOut.Rate;
        const int SamplesPerFrame = Rate / 60;
        const int EchoSize = 16384, EchoMask = EchoSize - 1;

        public readonly MusicPlayer Music = new MusicPlayer();
        public readonly SfxPlayer Sfx = new SfxPlayer();
        readonly Queue<Action> commands = new Queue<Action>();
        readonly object cmdLock = new object();

        readonly Voice[] mv = new Voice[Ch.Count * VPC];
        const int VPC = 3; // voices per music channel
        readonly int[] cur = new int[Ch.Count];
        readonly Voice[] sv = new Voice[Ch.Sfx];
        int frameCountdown;

        // mixing
        public volatile float MusicGain = 0.7f, SfxGain = 0.9f;
        public volatile bool Authentic = true;
        public volatile bool Paused;
        float pauseGain = 1f;
        float dcXL, dcYL, dcXR, dcYR, lpL, lpR;

        // echo
        readonly float[] echoL = new float[EchoSize], echoR = new float[EchoSize];
        readonly float[] firL = new float[8], firR = new float[8];
        static readonly float[] Fir = { 0.04f, 0.09f, 0.15f, 0.22f, 0.22f, 0.15f, 0.09f, 0.04f };
        int ePos, firPos;
        int echoDelay = 5 * 768; float echoFb = 0.4f, echoVol = 0.3f;

        const float Master = 0.44f;

        public Synth()
        {
            Bank.EnsureBuilt();
            for (int i = 0; i < mv.Length; i++) mv[i] = new Voice();
            for (int i = 0; i < sv.Length; i++) sv[i] = new Voice();
        }

        public void Post(Action a) { lock (cmdLock) commands.Enqueue(a); }

        /// <summary>Renders <paramref name="count"/> stereo frames (interleaved L,R) into buffer.</summary>
        public void Render(short[] buffer, int count)
        {
            lock (cmdLock) { while (commands.Count > 0) commands.Dequeue()(); }
            float mg = MusicGain, sg = SfxGain;
            bool auth = Authentic;
            float lpA = auth ? 0.76f : 1f;   // ~ 11 kHz one-pole: the soft SNES output (Gaussian interpolation) character
            const float dcR = 0.9974f;         // ~20 Hz DC blocker

            for (int i = 0; i < count; i++)
            {
                if (frameCountdown <= 0) { frameCountdown += SamplesPerFrame; StepFrame(); }
                frameCountdown--;

                pauseGain += ((Paused ? 0f : 1f) - pauseGain) * 0.003f;
                float mgp = mg * pauseGain;

                float dl = 0, dr = 0, el = 0, er = 0;
                for (int v = 0; v < mv.Length; v++)
                {
                    var vo = mv[v];
                    if (!vo.Active) continue;
                    float y = vo.Tick() * mgp;
                    vo.GL += (vo.TL - vo.GL) * 0.004f; vo.GR += (vo.TR - vo.GR) * 0.004f;
                    float l = y * vo.GL, r = y * vo.GR;
                    dl += l; dr += r;
                    if (vo.Echo) { el += l; er += r; }
                }
                for (int v = 0; v < sv.Length; v++)
                {
                    var vo = sv[v];
                    if (!vo.Active) continue;
                    float y = vo.Tick() * sg;
                    vo.GL += (vo.TL - vo.GL) * 0.004f; vo.GR += (vo.TR - vo.GR) * 0.004f;
                    float l = y * vo.GL, r = y * vo.GR;
                    dl += l; dr += r;
                    if (vo.Echo) { el += l; er += r; }
                }

                // echo: delayed signal -> FIR low-pass -> output + feedback
                firL[firPos] = echoL[(ePos - echoDelay) & EchoMask];
                firR[firPos] = echoR[(ePos - echoDelay - 360) & EchoMask];
                float fl = 0, fr = 0;
                for (int k = 0; k < 8; k++) { int j = (firPos - k) & 7; fl += Fir[k] * firL[j]; fr += Fir[k] * firR[j]; }
                firPos = (firPos + 1) & 7;
                echoL[ePos] = el + fl * echoFb;
                echoR[ePos] = er + fr * echoFb;
                ePos = (ePos + 1) & EchoMask;

                float L = (dl + fl * echoVol) * Master, R = (dr + fr * echoVol) * Master;
                // DC block + optional warm low-pass
                float hl = L - dcXL + dcR * dcYL; dcXL = L; dcYL = hl;
                float hr = R - dcXR + dcR * dcYR; dcXR = R; dcYR = hr;
                lpL += (hl - lpL) * lpA; lpR += (hr - lpR) * lpA;
                buffer[i * 2] = ToPcm(lpL);
                buffer[i * 2 + 1] = ToPcm(lpR);
            }
        }

        static short ToPcm(float x)
        {
            float a = x < 0 ? -x : x;
            if (a > 0.8f) { a = 0.8f + 0.2f * (float)Math.Tanh((a - 0.8f) / 0.2f); x = x < 0 ? -a : a; }
            if (float.IsNaN(x)) x = 0;
            return (short)(x * 32000f);
        }

        void StepFrame()
        {
            if (!Paused) Music.Frame();
            Sfx.Frame();

            // echo settings follow the current song
            echoDelay = Math.Max(1, Math.Min(15, Music.EchoDelay)) * 768; // 16 ms units
            echoFb = Music.EchoFb; echoVol = Music.EchoVol;

            if (!Paused)
                for (int c = 0; c < Ch.Count; c++)
                {
                    ApplyMusic(c, ref Music.Out[c]);
                    Music.Out[c].Trigger = false;
                }
            for (int s = 0; s < Ch.Sfx; s++)
            {
                VoiceCtl o;
                bool act = Sfx.Output(s, out o);
                var v = sv[s];
                if (!act) { v.KeyOff(); continue; }
                Apply(v, ref o, true);
            }
        }

        void ApplyMusic(int c, ref VoiceCtl o)
        {
            var ins = Bank.Get(o.Inst);
            if (o.Trigger && ins != null)
            {
                if (ins.Kit)
                {
                    KitPiece p;
                    if (!Bank.Kit.TryGetValue((char)o.Piece, out p)) return;
                    int slot = p.Group;
                    cur[c] = slot; chordN[c] = 1;
                    mv[c * VPC + slot].KeyOn(ins, Bank.KitSamples[p.Sample], p.Pitch, o.Vol * p.Vol, o.Pan + p.Pan, o.Echo);
                }
                else
                {
                    for (int k = 0; k < VPC; k++) if (!mv[c * VPC + k].Kit) mv[c * VPC + k].KeyOff();
                    if (o.Chord > 1)
                    {
                        // chord: one voice per note; spread them slightly across the stereo field
                        chordN[c] = Math.Min(VPC, o.Chord); cur[c] = 0;
                        for (int k = 0; k < chordN[c]; k++)
                        {
                            float f = k == 0 ? o.Freq : k == 1 ? o.Freq2 : o.Freq3;
                            var s = ins.Pick(f);
                            mv[c * VPC + k].KeyOn(ins, s, f / s.BaseHz, o.Vol * 0.8f, o.Pan + (k - 1) * 0.15f, o.Echo);
                        }
                    }
                    else
                    {
                        chordN[c] = 1;
                        cur[c] = (cur[c] + 1) % VPC;
                        var s = ins.Pick(o.Freq);
                        mv[c * VPC + cur[c]].KeyOn(ins, s, o.Freq / s.BaseHz, o.Vol, o.Pan, o.Echo);
                    }
                }
                return;
            }
            int n = chordN[c];
            for (int k = 0; k < n; k++)
            {
                var v = n > 1 ? mv[c * VPC + k] : mv[c * VPC + cur[c]];
                if (!v.Active || v.Kit) continue;
                if (!o.Gate) { v.KeyOff(); continue; }
                float f = k == 0 ? o.Freq : k == 1 ? o.Freq2 : o.Freq3;
                if (ins != null && v.BaseHz > 0 && f > 0) v.SetStep(f / v.BaseHz, SamplesPerFrame);
                if (ins != null) v.SetGains(o.Vol * ins.Gain * (n > 1 ? 0.8f : 1f), o.Pan + (n > 1 ? (k - 1) * 0.15f : 0f));
                v.Echo = o.Echo;
            }
        }
        readonly int[] chordN = new int[Ch.Count];

        void Apply(Voice v, ref VoiceCtl o, bool sfx)
        {
            var ins = Bank.Get(o.Inst);
            if (ins == null) { v.KeyOff(); return; }
            if (o.Trigger)
            {
                Sample s; double step;
                if (ins.Kit)
                {
                    KitPiece p;
                    if (!Bank.Kit.TryGetValue((char)o.Piece, out p)) return;
                    s = Bank.KitSamples[p.Sample]; step = p.Pitch * (o.Freq > 0 ? o.Freq / 440.0 : 1.0);
                    v.KeyOn(ins, s, step, o.Vol * p.Vol, o.Pan + p.Pan, o.Echo);
                }
                else
                {
                    s = ins.Pick(o.Freq); step = o.Freq / s.BaseHz;
                    v.KeyOn(ins, s, step, o.Vol, o.Pan, o.Echo);
                }
                return;
            }
            if (!v.Active) return;
            if (!o.Gate) { v.KeyOff(); return; }
            if (!v.Kit && v.BaseHz > 0) v.SetStep(o.Freq / v.BaseHz, SamplesPerFrame);
            v.SetGains(o.Vol * ins.Gain, o.Pan);
            v.Echo = o.Echo;
        }
    }
}
