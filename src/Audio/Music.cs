using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SMB4.Audio
{
    public enum EvT : byte { Note, Rest, Tie, Vol, Inst, Gate, Vib, Arp, LoopStart, LoopEnd, LoopPoint, Tempo, Transpose, Drum, Pan, Echo, Porta, Detune }

    public struct Ev
    {
        public EvT T; public int A; public int Dur; public float F; public int B; // B: chord notes 2 and 3 packed as (n2+1) | (n3+1) << 8
        public Ev(EvT t, int a, int dur) { T = t; A = a; Dur = dur; F = 0; B = 0; }
    }

    public sealed class Song
    {
        public string Name = "", Title = "";
        public int Tempo = 120;
        public bool Loops = true;
        public int EchoDelay = 5;          // 16 ms units (1..15)
        public float EchoFb = 0.40f;        // feedback -1..1
        public float EchoVol = 0.30f;       // return level 0..1
        public int Swing = 50;              // % of a beat given to the first 8th (50 = straight, 60-67 = shuffle)
        public Ev[][] Tracks = new Ev[Ch.Count][];
    }

    /// <summary>
    /// 16-bit MML dialect (full reference: data/music/README.md)
    ///   header lines: "tempo 150", "title Foo", "loop 0", "echo DELAY FEEDBACK VOLUME" (16 ms units, %, %), "swing 60"
    ///   channel lines: "C1: ..." .. "C8: ..." (repeat a prefix to continue that channel)
    ///   notes c d e f g a b [+ # -] [len] [.] ; r rest ; ^len tie ; o4 &gt; &lt; ; l8 ;
    ///   @name instrument (@kit = drum kit: letters become drum pieces) ; v0-15 volume ; p-10..10 pan ;
    ///   q1-8 gate ; V0-3 vibrato ; P0-9 portamento ; E0/E1 echo send ; A0-6 arpeggio ; K-24..24 transpose ;
    ///   D-50..50 detune (cents) ; [ ... ]n repeat ; L loop point ; T150 tempo
    /// Whole note = 192 ticks.
    /// </summary>
    public static class Mml
    {
        public const int Whole = 192;

        public static Song Parse(string name, string text, List<string> errors)
        {
            Bank.EnsureBuilt();
            var song = new Song { Name = name };
            var chan = new StringBuilder[Ch.Count];
            for (int i = 0; i < Ch.Count; i++) chan[i] = new StringBuilder();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw;
                // '#' starts a comment only at the start of a line or after whitespace ("c#" is a sharp)
                for (int h = 0; h < line.Length; h++)
                    if (line[h] == '#' && (h == 0 || char.IsWhiteSpace(line[h - 1]))) { line = line.Substring(0, h); break; }
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("tempo", StringComparison.OrdinalIgnoreCase)) { int t; if (int.TryParse(line.Substring(5).Trim(), out t)) song.Tempo = t; continue; }
                if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase)) { song.Title = line.Substring(5).Trim(); continue; }
                if (line.StartsWith("loop", StringComparison.OrdinalIgnoreCase)) { song.Loops = line.Substring(4).Trim() != "0"; continue; }
                if (line.StartsWith("swing", StringComparison.OrdinalIgnoreCase)) { int sw; if (int.TryParse(line.Substring(5).Trim(), out sw)) song.Swing = Math.Max(50, Math.Min(75, sw)); continue; }
                if (line.StartsWith("echo", StringComparison.OrdinalIgnoreCase))
                {
                    var p = line.Substring(4).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    int d, f, v;
                    if (p.Length == 3 && int.TryParse(p[0], out d) && int.TryParse(p[1], out f) && int.TryParse(p[2], out v))
                    {
                        song.EchoDelay = Math.Max(1, Math.Min(15, d));
                        song.EchoFb = Math.Max(-95, Math.Min(95, f)) / 100f;
                        song.EchoVol = Math.Max(0, Math.Min(100, v)) / 100f;
                    }
                    else errors.Add(name + ": bad echo line (want: echo DELAY FEEDBACK VOLUME): " + raw.Trim());
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon == 2 && (line[0] == 'C' || line[0] == 'c') && line[1] >= '1' && line[1] <= '8')
                {
                    chan[line[1] - '1'].Append(' ').Append(line.Substring(3));
                    continue;
                }
                errors.Add(name + ": unrecognised line: " + raw.Trim());
            }
            for (int c = 0; c < Ch.Count; c++) song.Tracks[c] = ParseTrack(name, c, chan[c].ToString(), errors);
            return song;
        }

        static readonly int[] Semis = { 9, 11, 0, 2, 4, 5, 7 }; // a b c d e f g

        static Ev[] ParseTrack(string name, int ch, string s, List<string> errors)
        {
            var evs = new List<Ev>();
            int octave = 4, defLen = Whole / 4, i = 0;
            bool drums = false;
            Func<int> num = () =>
            {
                int st = i; bool neg = false;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) { neg = s[i] == '-'; i++; }
                int v = 0; bool any = false;
                while (i < s.Length && char.IsDigit(s[i])) { v = v * 10 + (s[i] - '0'); i++; any = true; }
                if (!any) { i = st; return int.MinValue; }
                return neg ? -v : v;
            };
            Func<int> length = () =>
            {
                int n = num();
                int d = n == int.MinValue || n <= 0 ? defLen : Whole / n;
                int add = d;
                while (i < s.Length && s[i] == '.') { add /= 2; d += add; i++; }
                return d;
            };
            try
            {
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (char.IsWhiteSpace(c) || c == '|') continue;
                    if (drums && c >= 'a' && c <= 'z' && c != 'r' && c != 'l' && c != 'v' && c != 'q' && c != 'o' && c != 'p')
                    {
                        if (!Bank.Kit.ContainsKey(c)) { errors.Add(name + ": unknown drum '" + c + "' on " + ChName(ch)); continue; }
                        evs.Add(new Ev(EvT.Drum, c, length()));
                        continue;
                    }
                    if (!drums && c >= 'a' && c <= 'g')
                    {
                        int semi = Semis[c - 'a'];
                        if (i < s.Length && (s[i] == '+' || s[i] == '#')) { semi++; i++; }
                        else if (i < s.Length && s[i] == '-') { semi--; i++; }
                        evs.Add(new Ev(EvT.Note, 12 * (octave + 1) + semi, length()));
                        continue;
                    }
                    if (!drums && c == '{')
                    {
                        // chord: {c e g} (up to 3 notes, octave changes inside are local)
                        int oc = octave, cnt = 0; var ns = new int[3];
                        while (i < s.Length && s[i] != '}')
                        {
                            char k = s[i++];
                            if (char.IsWhiteSpace(k)) continue;
                            if (k == '<') { oc--; continue; }
                            if (k == '>') { oc++; continue; }
                            if (k == 'o') { int v = num(); if (v != int.MinValue) oc = v; continue; }
                            if (k >= 'a' && k <= 'g')
                            {
                                int semi = Semis[k - 'a'];
                                if (i < s.Length && (s[i] == '+' || s[i] == '#')) { semi++; i++; }
                                else if (i < s.Length && s[i] == '-') { semi--; i++; }
                                if (cnt < 3) ns[cnt] = 12 * (oc + 1) + semi;
                                cnt++; continue;
                            }
                            errors.Add(name + ": unexpected '" + k + "' inside chord on " + ChName(ch)); 
                        }
                        i++;
                        if (cnt > 3) errors.Add(name + ": chord with more than 3 notes on " + ChName(ch));
                        if (cnt == 0) { errors.Add(name + ": empty chord on " + ChName(ch)); continue; }
                        var ce = new Ev(EvT.Note, ns[0], length());
                        if (cnt > 1) ce.B = (ns[1] + 1) | (cnt > 2 ? (ns[2] + 1) << 8 : 0);
                        evs.Add(ce);
                        continue;
                    }
                    switch (c)
                    {
                        case 'r': evs.Add(new Ev(EvT.Rest, 0, length())); break;
                        case '^': evs.Add(new Ev(EvT.Tie, 0, length())); break;
                        case 'o': { int v = num(); if (v != int.MinValue) octave = v; break; }
                        case '>': octave++; break;
                        case '<': octave--; break;
                        case 'l': { int v = num(); if (v > 0) { defLen = Whole / v; int add = defLen; while (i < s.Length && s[i] == '.') { add /= 2; defLen += add; i++; } } break; }
                        case 'v': evs.Add(new Ev(EvT.Vol, Clamp(num(), 0, 15), 0)); break;
                        case 'p': evs.Add(new Ev(EvT.Pan, Clamp0(num(), -10, 10), 0)); break;
                        case '@':
                            {
                                int st = i;
                                while (i < s.Length && char.IsLetterOrDigit(s[i])) i++;
                                string id = s.Substring(st, i - st);
                                int n;
                                int inst = int.TryParse(id, out n) ? (Bank.Get(n) != null ? n : -1) : Bank.Find(id);
                                if (inst < 0) { errors.Add(name + ": unknown instrument '@" + id + "' on " + ChName(ch)); break; }
                                drums = inst == Bank.KitId;
                                evs.Add(new Ev(EvT.Inst, inst, 0));
                                break;
                            }
                        case 'q': evs.Add(new Ev(EvT.Gate, Clamp(num(), 1, 8), 0)); break;
                        case 'E': evs.Add(new Ev(EvT.Echo, Clamp(num(), 0, 1), 0)); break;
                        case 'V': evs.Add(new Ev(EvT.Vib, Clamp(num(), 0, 3), 0)); break;
                        case 'P': evs.Add(new Ev(EvT.Porta, Clamp(num(), 0, 9), 0)); break;
                        case 'D': evs.Add(new Ev(EvT.Detune, Clamp0(num(), -50, 50), 0)); break;
                        case 'A': evs.Add(new Ev(EvT.Arp, Clamp(num(), 0, ArpTable.Arps.Length - 1), 0)); break;
                        case 'K': evs.Add(new Ev(EvT.Transpose, Clamp0(num(), -24, 24), 0)); break;
                        case 'T': evs.Add(new Ev(EvT.Tempo, Clamp(num(), 30, 300), 0)); break;
                        case '[': evs.Add(new Ev(EvT.LoopStart, 0, 0)); break;
                        case ']': { int n = num(); evs.Add(new Ev(EvT.LoopEnd, n == int.MinValue ? 2 : Math.Max(1, n), 0)); break; }
                        case 'L': evs.Add(new Ev(EvT.LoopPoint, 0, 0)); break;
                        default: errors.Add(name + ": unexpected '" + c + "' in " + ChName(ch) + " at " + (i - 1)); break;
                    }
                }
            }
            catch (Exception ex) { errors.Add(name + ": " + ChName(ch) + ": " + ex.Message); }
            return evs.ToArray();
        }

        static int Clamp(int v, int lo, int hi) { if (v == int.MinValue) return lo; return v < lo ? lo : v > hi ? hi : v; }
        static int Clamp0(int v, int lo, int hi) { if (v == int.MinValue) return 0; return v < lo ? lo : v > hi ? hi : v; }
        public static string ChName(int c) { return "C" + (c + 1); }

        /// <summary>Total ticks from the loop point (or start) to the end — used to check tracks stay in sync.</summary>
        public static int LoopLength(Ev[] evs, out int introTicks)
        {
            introTicks = 0;
            int total = 0, loopAt = -1;
            var stack = new Stack<int[]>(); // [startIndex, count]
            int iters = 0;
            for (int i = 0; i < evs.Length && iters < 400000; i++, iters++)
            {
                var e = evs[i];
                switch (e.T)
                {
                    case EvT.Note: case EvT.Rest: case EvT.Tie: case EvT.Drum: total += e.Dur; break;
                    case EvT.LoopPoint: loopAt = total; break;
                    case EvT.LoopStart: stack.Push(new[] { i, 0 }); break;
                    case EvT.LoopEnd:
                        if (stack.Count > 0) { var top = stack.Peek(); top[1]++; if (top[1] < e.A) i = top[0]; else stack.Pop(); }
                        break;
                }
            }
            introTicks = loopAt < 0 ? 0 : loopAt;
            return total - introTicks;
        }
    }

    /// <summary>Plays one Song on the 60 Hz frame clock (audio thread only).</summary>
    public sealed class MusicPlayer
    {
        sealed class Track
        {
            public Ev[] Evs; public int Pos; public double Next; public int LoopPoint = -1; public bool Done;
            public int Vol = 12, Inst, Gate = 7, Vib, Arp, Transpose, Pan, Porta, Detune; public bool Echo;
            public int Note = -1; public int NoteFrame; public double GateEnd; public bool KeyOn;
            public float Glide; public bool Trig; public int Piece, Chord;
            public readonly int[] LoopStart = new int[8], LoopCount = new int[8]; public int Depth;
        }

        public readonly VoiceCtl[] Out = new VoiceCtl[Ch.Count];
        readonly Track[] tr = new Track[Ch.Count];
        Song song;
        double tick, perFrame;
        public double Speed = 1.0;
        public volatile bool Finished = true;
        public volatile string Current = "";
        int fadeFrames, fadeLeft;
        public int EchoDelay = 5; public float EchoFb = 0.4f, EchoVol = 0.3f;

        static readonly int[][] Arps = ArpTable.Arps;
        static readonly float[] PortaRate = { 1f, 0.06f, 0.09f, 0.13f, 0.18f, 0.25f, 0.33f, 0.45f, 0.6f, 0.8f };
        static readonly float[] VolCurve = BuildVol();
        static float[] BuildVol() { var v = new float[16]; for (int i = 0; i < 16; i++) v[i] = (float)Math.Pow(i / 15.0, 1.7); return v; }

        public void Play(Song s)
        {
            song = s;
            tick = 0;
            perFrame = s.Tempo * Mml.Whole / 14400.0;
            Speed = 1.0;
            fadeFrames = 0;
            EchoDelay = s.EchoDelay; EchoFb = s.EchoFb; EchoVol = s.EchoVol;
            swing = s.Swing / 100.0;
            int piano = Math.Max(0, Bank.Find("piano"));
            for (int c = 0; c < Ch.Count; c++)
            {
                tr[c] = new Track { Evs = s.Tracks[c] ?? new Ev[0], Inst = piano };
                if (tr[c].Evs.Length == 0) tr[c].Done = true;
                Out[c] = new VoiceCtl();
            }
            Finished = false;
            Current = s.Name;
        }

        public void Stop()
        {
            song = null; Finished = true; Current = "";
            for (int c = 0; c < Ch.Count; c++) Out[c] = new VoiceCtl();
        }

        /// <summary>Swing: maps straight ticks to played ticks, stretching the first 8th of every beat.</summary>
        double Warp(double x)
        {
            if (swing <= 0.5) return x;
            double q = Math.Floor(x / 48) * 48, p = x - q;
            return q + (p < 24 ? p * 2 * swing : 48 * swing + (p - 24) * 2 * (1 - swing));
        }
        double swing = 0.5;

        public void FadeOut(int frames) { fadeFrames = Math.Max(1, frames); fadeLeft = fadeFrames; }

        public void Frame()
        {
            if (song == null) return;
            tick += perFrame * Speed;
            bool allDone = true;
            for (int c = 0; c < Ch.Count; c++)
            {
                var t = tr[c];
                t.Trig = false;
                int guard = 0;
                while (!t.Done && tick >= Warp(t.Next) && guard++ < 512) Step(t);
                if (!t.Done) allDone = false;
                Output(c, t);
            }
            if (fadeFrames > 0)
            {
                fadeLeft--;
                float g = Math.Max(0f, fadeLeft / (float)fadeFrames);
                g *= g;
                for (int c = 0; c < Ch.Count; c++) Out[c].Vol *= g;
                if (fadeLeft <= 0) { Stop(); return; }
            }
            if (allDone) Stop();
        }

        void Step(Track t)
        {
            if (t.Pos >= t.Evs.Length)
            {
                if (song.Loops) { t.Pos = t.LoopPoint >= 0 ? t.LoopPoint : 0; t.Depth = 0; if (t.Pos >= t.Evs.Length) t.Done = true; return; }
                t.Done = true; t.KeyOn = false; return;
            }
            var e = t.Evs[t.Pos++];
            switch (e.T)
            {
                case EvT.Note:
                    {
                        int n = e.A + t.Transpose;
                        bool legato = t.Porta > 0 && t.KeyOn && t.Note >= 0 && t.Next <= t.GateEnd;
                        t.Note = n; t.KeyOn = true; t.Chord = e.B;
                        if (!legato) { t.Trig = true; t.NoteFrame = 0; t.Glide = n; }
                        t.GateEnd = t.Next + (t.Gate >= 8 || NextIsTie(t) ? e.Dur : Math.Max(1, e.Dur * t.Gate / 8));
                        t.Next += e.Dur; break;
                    }
                case EvT.Rest: t.KeyOn = false; t.Next += e.Dur; break;
                case EvT.Tie: t.GateEnd = t.Next + (t.Gate >= 8 || NextIsTie(t) ? e.Dur : Math.Max(1, e.Dur * t.Gate / 8)); t.Next += e.Dur; break;
                case EvT.Drum:
                    t.Piece = e.A; t.Trig = true; t.KeyOn = true; t.NoteFrame = 0;
                    t.GateEnd = t.Next + e.Dur;
                    t.Next += e.Dur; break;
                case EvT.Vol: t.Vol = e.A; break;
                case EvT.Inst: t.Inst = e.A; break;
                case EvT.Gate: t.Gate = e.A; break;
                case EvT.Vib: t.Vib = e.A; break;
                case EvT.Arp: t.Arp = e.A; break;
                case EvT.Pan: t.Pan = e.A; break;
                case EvT.Echo: t.Echo = e.A != 0; break;
                case EvT.Porta: t.Porta = e.A; break;
                case EvT.Detune: t.Detune = e.A; break;
                case EvT.Transpose: t.Transpose = e.A; break;
                case EvT.Tempo: perFrame = e.A * Mml.Whole / 14400.0; break;
                case EvT.LoopPoint: t.LoopPoint = t.Pos; break;
                case EvT.LoopStart: if (t.Depth < 8) { t.LoopStart[t.Depth] = t.Pos; t.LoopCount[t.Depth] = 0; t.Depth++; } break;
                case EvT.LoopEnd:
                    if (t.Depth > 0)
                    {
                        int d = t.Depth - 1;
                        t.LoopCount[d]++;
                        if (t.LoopCount[d] < e.A) t.Pos = t.LoopStart[d]; else t.Depth--;
                    }
                    break;
            }
        }

        /// <summary>True if the next time-consuming event (skipping volume/instrument/etc.) is a tie.</summary>
        static bool NextIsTie(Track t)
        {
            for (int i = t.Pos; i < t.Evs.Length; i++)
            {
                var k = t.Evs[i].T;
                if (k == EvT.Tie) return true;
                if (k == EvT.Note || k == EvT.Rest || k == EvT.Drum || k == EvT.LoopEnd || k == EvT.LoopStart || k == EvT.LoopPoint) return false;
            }
            return false;
        }

        static float MidiToHz(float m) { return (float)(440.0 * Math.Pow(2.0, (m - 69.0) / 12.0)); }

        void Output(int c, Track t)
        {
            var o = new VoiceCtl();
            o.Inst = t.Inst; o.Pan = t.Pan / 10f; o.Echo = t.Echo; o.Vol = VolCurve[t.Vol]; o.Piece = t.Piece;
            o.Trigger = t.Trig;
            var ins = Bank.Get(t.Inst);
            if (ins != null && ins.Kit) { o.Gate = true; Out[c] = o; return; }
            o.Gate = t.KeyOn && tick < Warp(t.GateEnd) && t.Note >= 0;
            if (t.Note < 0) { o.Trigger = false; Out[c] = o; return; }
            if (t.Porta > 0) t.Glide += (t.Note - t.Glide) * PortaRate[t.Porta]; else t.Glide = t.Note;
            float note = t.Glide + t.Detune / 100f;
            if (t.Arp > 0) { var a = Arps[t.Arp]; note += a[(t.NoteFrame / 2) % a.Length]; }
            if (t.Vib > 0)
            {
                int delay = t.Vib == 1 ? 20 : t.Vib == 2 ? 12 : 6;
                float depth = t.Vib == 1 ? 0.15f : t.Vib == 2 ? 0.25f : 0.45f;
                int f = t.NoteFrame - delay;
                if (f > 0) note += depth * Math.Min(1f, f / 12f) * (float)Math.Sin(f * 2 * Math.PI * 5.5 / 60.0);
            }
            o.Freq = MidiToHz(note);
            if (t.Chord != 0)
            {
                int n2 = (t.Chord & 255) - 1, n3 = ((t.Chord >> 8) & 255) - 1;
                o.Chord = n3 >= 0 ? 3 : 2;
                o.Freq2 = MidiToHz(note + n2 - (t.Note - t.Transpose));
                if (n3 >= 0) o.Freq3 = MidiToHz(note + n3 - (t.Note - t.Transpose));
            }
            t.NoteFrame++;
            Out[c] = o;
        }
    }

    public static class ArpTable
    {
        public static readonly int[][] Arps =
        {
            new[] { 0 },
            new[] { 0, 4, 7 },     // A1 major
            new[] { 0, 3, 7 },     // A2 minor
            new[] { 0, 12 },       // A3 octave
            new[] { 0, 7 },        // A4 fifth
            new[] { 0, 5, 9 },     // A5 sus/6th
            new[] { 0, 4, 7, 12 }, // A6 major + octave
        };
    }
}
