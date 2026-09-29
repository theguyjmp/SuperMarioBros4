using System;
using System.Collections.Generic;

namespace SMB4.Audio
{
    public enum SfxId
    {
        Jump, Stomp, Kick, Bump, Break, Coin, Sprout, PowerUp, OneUp, Fireball, Pipe, Pause, PMeter, Skid, Tail,
        Flutter, Note, PSwitch, Door, Tally, CardStop, MapStep, MapEnter, ItemUse, Thwomp, Cannon, BossHit, Vine,
        Swim, Poof, Explode, Hammer, BowserFire, MenuMove, MenuSelect, MenuBack, Splash, Magic, FireballHit,
        Ricochet, Chest, Error, Spring, Statue, Lava, Count
    }

    /// <summary>A sound effect program for one SFX slot: one VoiceCtl per 60 Hz frame.</summary>
    public sealed class SfxDef
    {
        public int Ch; public int Priority; public VoiceCtl[] Frames; public bool Loop;
    }

    /// <summary>SFX slots. Each has its own voice, so effects layer over the music instead of stealing it.</summary>
    public sealed class SfxPlayer
    {
        readonly SfxDef[] active = new SfxDef[Ch.Sfx];
        readonly int[] frame = new int[Ch.Sfx];

        public void Start(SfxDef d)
        {
            int c = d.Ch;
            if (active[c] == null || d.Priority >= active[c].Priority) { active[c] = d; frame[c] = 0; }
        }

        public void Stop(SfxDef d) { if (active[d.Ch] == d) active[d.Ch] = null; }
        public void StopAll() { for (int c = 0; c < Ch.Sfx; c++) active[c] = null; }
        public void Frame() { }

        public bool Output(int c, out VoiceCtl o)
        {
            var d = active[c];
            o = new VoiceCtl();
            if (d == null) return false;
            if (frame[c] >= d.Frames.Length)
            {
                if (d.Loop) frame[c] = 0;
                else { active[c] = null; return false; }
            }
            o = d.Frames[frame[c]];
            if (frame[c] == 0) o.Trigger = true;   // a (re)started effect always keys on
            frame[c]++;
            return true;
        }
    }

    /// <summary>All sound effects: short programs on the sample instruments (original sounds, 16-bit flavour).</summary>
    public static class SfxBank
    {
        public static readonly SfxDef[][] Defs = new SfxDef[(int)SfxId.Count][];

        static float Hz(double midi) { return (float)(440.0 * Math.Pow(2.0, (midi - 69) / 12.0)); }
        static int I(string name) { int id = Bank.Find(name); return id < 0 ? 0 : id; }

        static VoiceCtl F(int inst, float hz, float vol, float pan, bool trig, bool echo)
        {
            return new VoiceCtl { Inst = inst, Freq = hz, Vol = vol, Pan = pan, Trigger = trig, Gate = true, Echo = echo };
        }

        /// <summary>Keyed tone gliding exponentially hz0 -> hz1 over n frames, volume v0 -> v1, pan p0 -> p1.</summary>
        static IEnumerable<VoiceCtl> Tone(string inst, float hz0, float hz1, int n, float v0, float v1, float p0 = 0, float p1 = 0, bool echo = false)
        {
            int id = I(inst);
            for (int i = 0; i < n; i++)
            {
                double t = n <= 1 ? 1 : i / (double)(n - 1);
                yield return F(id, (float)(hz0 * Math.Pow(hz1 / hz0, t)), (float)(v0 + (v1 - v0) * t), (float)(p0 + (p1 - p0) * t), i == 0, echo);
            }
        }
        /// <summary>A run of re-triggered notes, each held for 'each' frames.</summary>
        static IEnumerable<VoiceCtl> Notes(string inst, int each, float v, bool echo, params int[] midi)
        {
            int id = I(inst);
            float pan = 0;
            foreach (int m in midi)
            {
                for (int i = 0; i < each; i++)
                    yield return m < 0 ? new VoiceCtl { Inst = id } : F(id, Hz(m), v, pan, i == 0, echo);
                pan = -pan + (pan <= 0 ? 0.15f : -0.15f);
            }
        }
        /// <summary>Noise with its colour swept hz0 -> hz1 (440 = full band) and volume v0 -> v1.</summary>
        static IEnumerable<VoiceCtl> Nz(float c0, float c1, int n, float v0, float v1, float p0 = 0, float p1 = 0, bool echo = false)
        {
            return Tone("noise", c0, c1, n, v0, v1, p0, p1, echo);
        }
        static IEnumerable<VoiceCtl> Hit(char piece, float vol, float pitch = 1f, int frames = 1, bool echo = false)
        {
            int id = Bank.KitId;
            for (int i = 0; i < frames; i++) yield return new VoiceCtl { Inst = id, Piece = piece, Vol = vol, Freq = 440 * pitch, Trigger = i == 0, Gate = true, Echo = echo };
        }
        static IEnumerable<VoiceCtl> Rest(int n) { for (int i = 0; i < n; i++) yield return new VoiceCtl(); }

        static SfxDef D(int ch, int prio, params IEnumerable<VoiceCtl>[] parts)
        {
            var l = new List<VoiceCtl>();
            foreach (var p in parts) l.AddRange(p);
            return new SfxDef { Ch = ch, Priority = prio, Frames = l.ToArray() };
        }
        static void Set(SfxId id, params SfxDef[] defs) { Defs[(int)id] = defs; }

        static SfxBank()
        {
            Bank.EnsureBuilt();
            const int A = Ch.S0, B = Ch.S1, C = Ch.S2, Dd = Ch.S3, E = Ch.S4, G = Ch.S5;

            // --- player movement
            Set(SfxId.Jump, D(A, 3, Tone("pulse", 262, 740, 7, 0.45f, 0.4f), Tone("pulse", 740, 880, 5, 0.35f, 0.05f)),
                            D(E, 2, Tone("sine", 524, 1480, 7, 0.25f, 0.2f), Tone("sine", 1480, 1760, 4, 0.15f, 0f)));
            Set(SfxId.Stomp, D(A, 4, Tone("square", 560, 150, 8, 0.6f, 0.2f)),
                             D(C, 4, Hit('k', 1.0f, 1.25f)), D(Dd, 4, Hit('z', 0.8f, 0.8f)));
            Set(SfxId.Kick, D(C, 4, Hit('d', 1.0f, 1.1f)), D(B, 3, Tone("pulse", 1050, 520, 5, 0.4f, 0.1f)));
            Set(SfxId.Bump, D(B, 3, Hit('x', 1.0f, 1.35f)), D(C, 3, Hit('m', 0.9f, 1.4f)));
            Set(SfxId.Break, D(C, 5, Nz(600, 90, 22, 0.9f, 0f, -0.3f, 0.3f)),
                             D(B, 4, Hit('x', 1.0f, 1.1f)),
                             D(Dd, 4, Notes("xylo", 2, 0.6f, false, 67, 62, 58, 55, 50)));
            Set(SfxId.Skid, D(C, 1, Nz(160, 110, 9, 0.55f, 0.1f)));
            Set(SfxId.Tail, D(C, 3, Nz(90, 520, 7, 0.35f, 0.7f, -0.7f, 0.1f), Nz(520, 120, 9, 0.7f, 0f, 0.1f, 0.7f)),
                            D(E, 2, Tone("sine", 300, 900, 7, 0.12f, 0.2f, 0.6f, 0f), Tone("sine", 900, 350, 8, 0.2f, 0f, 0f, -0.6f)));
            Set(SfxId.Flutter, D(A, 2, Tone("tri", 230, 460, 6, 0.4f, 0.1f)));
            Set(SfxId.Swim, D(A, 2, Tone("sine", 320, 640, 5, 0.45f, 0.05f, 0, 0, true)));
            Set(SfxId.Spring, D(B, 4, Tone("tri", 170, 1000, 12, 0.6f, 0.1f, 0, 0, true)));
            Set(SfxId.Note, D(B, 3, Tone("tri", 250, 720, 5, 0.6f, 0.5f), Tone("tri", 720, 340, 8, 0.45f, 0.05f)),
                            D(E, 2, Notes("marimba", 6, 0.35f, true, 79)));

            // --- items & rewards (bright, with echo)
            // coin: a crisp grace note up a fourth into a round, ringing "ding" (owner: the old one was too loud and
            // piercing — it rang ~1 s at B6, right where the ear is most sensitive). Lower pitch, shorter ring, a warm
            // vibes body an octave down and a tiny stereo sparkle on the attack.
            Set(SfxId.Coin, D(B, 2, Notes("glock", 3, 0.36f, true, 86), Tone("glock", Hz(93), Hz(93), 16, 0.40f, 0.12f, 0, 0, true)),
                            D(E, 1, Rest(3), Tone("vibes", Hz(81), Hz(81), 14, 0.20f, 0.04f, 0, 0, true)),
                            D(Dd, 0, Rest(3), Tone("sine", Hz(105), Hz(105), 3, 0.07f, 0f, -0.5f, -0.5f), Tone("sine", Hz(110), Hz(110), 4, 0.05f, 0f, 0.5f, 0.5f, true)));
            Set(SfxId.Sprout, D(B, 4, Notes("vibes", 2, 0.45f, true, 60, 64, 67, 72, 62, 66, 69, 74, 64, 68, 71, 76, 65, 69, 72, 77, 67, 71, 74, 79)));
            Set(SfxId.PowerUp, D(A, 6, Notes("snaplead", 2, 0.5f, true, 55, 59, 62, 67, 57, 61, 64, 69, 59, 63, 66, 71, 60, 64, 67, 72, 62, 66, 69, 74, 64, 67, 71, 76)),
                               D(E, 5, Notes("glock", 8, 0.35f, true, 79, 81, 83, 84, 86, 88, 91)));
            Set(SfxId.OneUp, D(A, 8, Notes("steeldrum", 5, 0.6f, true, 79, 84, 88, 86, 91), Notes("steeldrum", 16, 0.6f, true, 96)),
                             D(E, 7, Notes("vibes", 5, 0.3f, true, 67, 72, 76, 74, 79), Notes("vibes", 16, 0.3f, true, 84)));  // original "1-UP!" figure (G C E D G - C)
            Set(SfxId.ItemUse, D(A, 6, Notes("vibes", 3, 0.55f, true, 72, 76, 79, 84, 79, 84, 88), Rest(12)));
            Set(SfxId.Chest, D(A, 7, Notes("harp", 3, 0.6f, true, 67, 72, 76, 79, 84), Notes("harp", 18, 0.6f, true, 88)),
                             D(E, 6, Rest(15), Notes("glock", 24, 0.4f, true, 96)));
            Set(SfxId.Magic, D(A, 5, Notes("glock", 1, 0.45f, true, 84, 88, 91, 86, 89, 93, 88, 91, 95, 89, 93, 96), Rest(20)));
            Set(SfxId.CardStop, D(A, 7, Notes("vibes", 3, 0.6f, true, 84, 88, 91, 96, 91, 96)), D(E, 6, Hit('a', 0.6f, 1.2f)));
            Set(SfxId.Tally, D(B, 5, Notes("musicbox", 2, 0.35f, false, 96)));
            var pm = D(B, 2, Notes("sine", 2, 0.22f, false, 96), Rest(2)); pm.Loop = true;
            Set(SfxId.PMeter, pm);

            // --- combat
            Set(SfxId.Fireball, D(A, 3, Tone("pulse", 1200, 330, 6, 0.4f, 0.1f)), D(G, 2, Nz(300, 150, 5, 0.25f, 0f)));
            Set(SfxId.FireballHit, D(C, 2, Nz(260, 120, 5, 0.45f, 0f)), D(G, 1, Hit('d', 0.5f, 1.4f)));
            Set(SfxId.Hammer, D(A, 3, Tone("pulse", 640, 400, 5, 0.35f, 0.1f)));
            Set(SfxId.Ricochet, D(B, 2, Tone("pulse", 1600, 900, 5, 0.35f, 0.05f, 0, 0, true)));
            Set(SfxId.BossHit, D(A, 7, Notes("orchhit", 3, 0.75f, true, 60, 55, 48)), D(C, 6, Nz(300, 80, 14, 0.7f, 0f)), D(Dd, 6, Hit('x', 1f, 0.9f)));
            Set(SfxId.Poof, D(C, 6, Nz(200, 380, 5, 0.3f, 0.6f), Nz(380, 90, 12, 0.6f, 0f)), D(A, 5, Tone("sine", 700, 200, 10, 0.35f, 0.05f, 0, 0, true)));
            Set(SfxId.Explode, D(Dd, 7, Hit('w', 1f, 0.85f)), D(C, 7, Nz(220, 50, 32, 0.9f, 0f, 0, 0, true)));
            Set(SfxId.Thwomp, D(Dd, 6, Hit('w', 1f, 1.0f)), D(C, 6, Nz(160, 50, 20, 0.8f, 0f)), D(G, 5, Hit('t', 0.8f, 0.6f)));
            Set(SfxId.Cannon, D(Dd, 5, Hit('x', 1f, 0.8f)), D(C, 5, Nz(260, 70, 16, 0.7f, 0f)));
            Set(SfxId.BowserFire, D(C, 6, Nz(80, 260, 12, 0.3f, 0.75f, -0.4f, 0f), Nz(260, 60, 34, 0.75f, 0f, 0f, 0.4f, true)));
            Set(SfxId.Statue, D(C, 5, Nz(700, 250, 14, 0.5f, 0f)), D(B, 4, Tone("square", 220, 110, 14, 0.3f, 0.0f)));
            Set(SfxId.Lava, D(C, 2, Nz(70, 40, 14, 0.45f, 0f)), D(G, 1, Tone("sine", 90, 200, 8, 0.3f, 0f)));
            Set(SfxId.Splash, D(C, 3, Nz(380, 120, 16, 0.55f, 0f, 0, 0, true)), D(G, 2, Tone("sine", 500, 1100, 6, 0.2f, 0.0f, 0, 0, true)));
            Set(SfxId.Vine, D(B, 4, Notes("harp", 2, 0.45f, true, 60, 67, 62, 69, 64, 71, 65, 72, 67, 74, 69, 76, 71, 77, 72, 79, 74, 81, 76, 83)));

            // --- world / flow
            Set(SfxId.Pipe, D(A, 7, Tone("square", 420, 150, 8, 0.45f, 0.3f, 0, 0, true), Tone("square", 420, 150, 8, 0.4f, 0.2f, 0, 0, true), Tone("square", 420, 150, 8, 0.3f, 0.02f, 0, 0, true)),
                            D(E, 6, Tone("sine", 840, 300, 24, 0.15f, 0.0f)));
            Set(SfxId.PSwitch, D(A, 7, Notes("timpani", 6, 0.9f, false, 43, 38)), D(C, 6, Nz(120, 60, 14, 0.5f, 0f)), D(E, 6, Notes("orchhit", 4, 0.4f, true, 55)));
            Set(SfxId.Door, D(C, 5, Hit('x', 0.9f, 1.3f)), D(B, 4, Tone("square", 150, 95, 8, 0.25f, 0.02f)), D(G, 4, Nz(200, 90, 10, 0.35f, 0f)));
            Set(SfxId.MapStep, D(B, 3, Notes("marimba", 3, 0.45f, false, 72, 79)));
            Set(SfxId.MapEnter, D(A, 6, Notes("harp", 3, 0.6f, true, 60, 67, 72, 79, 84), Rest(10)), D(E, 5, Rest(6), Notes("strings", 14, 0.3f, true, 72)));
            Set(SfxId.Pause, D(A, 9, Notes("epiano", 4, 0.6f, true, 84, 79, 84), Notes("epiano", 20, 0.6f, true, 91)));
            Set(SfxId.MenuMove, D(B, 1, Notes("marimba", 3, 0.4f, false, 84)));
            Set(SfxId.MenuSelect, D(A, 6, Notes("vibes", 4, 0.55f, true, 79, 91), Rest(6)));
            Set(SfxId.MenuBack, D(A, 6, Notes("vibes", 4, 0.5f, true, 79, 72), Rest(6)));
            Set(SfxId.Error, D(A, 5, Notes("square", 7, 0.35f, false, 45, -1, 45), Rest(6)));
        }
    }
}
