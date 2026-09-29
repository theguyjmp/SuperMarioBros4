using System;
using SMB4.Platform;

namespace SMB4.Frontend
{
    /// <summary>
    /// Core audio → 48 kHz output. The main thread pushes each emulated frame's samples (at the core's rate); they are
    /// resampled (cubic Hermite) into a ring that the waveOut thread drains. Dynamic rate control nudges the resampling
    /// ratio by at most ±0.5 % to keep the ring near its target fill, which absorbs the 60.0988 vs 60.000 Hz difference,
    /// display-clock drift and DAC-clock drift without audible pitch change, crackle or growing latency.
    /// </summary>
    public sealed class AudioStream : ISampleSource
    {
        public const int OutRate = AudioOut.Rate;
        const double MaxSkew = 0.005;

        readonly object gate = new object();
        readonly short[] ring;            // interleaved stereo
        readonly int cap;                 // frames
        int rd, wr, fill;                 // frames
        bool starved = true;              // waiting to refill to target after an underrun / at start
        public int TargetFrames = 1536;   // ~32 ms
        public float Volume = 1f;

        // resampler state (main thread only)
        double pos;                       // fractional read position into hist+input
        readonly float[] hist = new float[6]; // last 3 input frames (L,R) carried between pushes
        float[] work = new float[16384];
        short[] outTmp = new short[16384];
        double avgFill = -1;

        // stats
        public double Ratio = 1;          // current skew factor applied (1 = nominal)
        public double NominalStep;        // input frames per output frame
        public long Underruns, PushedIn, PushedOut, Dropped, Pulled;
        public int LastFill { get { return fill; } }
        public int Peak;

        public AudioStream(int capacityFrames) { cap = capacityFrames; ring = new short[cap * 2]; }

        public void Reset()
        {
            lock (gate) { rd = wr = fill = 0; starved = true; }
            pos = 0; Array.Clear(hist, 0, hist.Length); avgFill = -1;
        }

        /// <summary>
        /// Adds <paramref name="frames"/> stereo frames produced at <paramref name="inRate"/> Hz. speed = emulated
        /// seconds per wall second (e.g. 60/60.0988 when the core is locked to a 60 Hz display).
        /// </summary>
        public void Push(short[] src, int frames, double inRate, double speed)
        {
            if (frames <= 0) return;
            PushedIn += frames;
            int f;
            lock (gate) f = fill;
            avgFill = avgFill < 0 ? f : avgFill * 0.95 + f * 0.05;
            // DRC: too full → consume input faster (fewer output frames), too empty → slower.
            double err = (avgFill - TargetFrames) / Math.Max(1.0, TargetFrames);
            double skew = 1 + Math.Max(-MaxSkew, Math.Min(MaxSkew, err * MaxSkew));
            Ratio = skew;
            NominalStep = inRate * speed / OutRate;
            double step = NominalStep * skew;

            // work = 3 history frames + new input (floats)
            int total = frames + 3;
            if (work.Length < total * 2) work = new float[total * 2 + 1024];
            Array.Copy(hist, work, 6);
            for (int i = 0; i < frames * 2; i++) work[6 + i] = src[i];

            // outputs need positions t with floor(t)-1 >= 0 and floor(t)+2 < total → t in [1, total-2)
            int maxOut = (int)((total - 3 - pos) / step) + 4;
            if (outTmp.Length < maxOut * 2) outTmp = new short[maxOut * 2 + 1024];
            int n = 0;
            double t = pos + 1;
            while (true)
            {
                int i = (int)t;
                if (i + 2 >= total) break;
                float x = (float)(t - i);
                for (int ch = 0; ch < 2; ch++)
                {
                    float y0 = work[(i - 1) * 2 + ch], y1 = work[i * 2 + ch], y2 = work[(i + 1) * 2 + ch], y3 = work[(i + 2) * 2 + ch];
                    // Catmull-Rom
                    float c1 = 0.5f * (y2 - y0);
                    float c2 = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
                    float c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
                    float v = ((c3 * x + c2) * x + c1) * x + y1;
                    int s = (int)(v * Volume);
                    if (s > 32767) s = 32767; else if (s < -32768) s = -32768;
                    outTmp[n * 2 + ch] = (short)s;
                    int a = s < 0 ? -s : s; if (a > Peak) Peak = a;
                }
                n++;
                t += step;
            }
            // carry: the last 3 input frames become history; position relative to them
            pos = t - 1 - (total - 3);
            Array.Copy(work, (total - 3) * 2, hist, 0, 6);
            PushedOut += n;
            Write(outTmp, n);
        }

        void Write(short[] src, int frames)
        {
            lock (gate)
            {
                int room = cap - fill;
                if (frames > room) { Dropped += frames - room; frames = room; }
                for (int i = 0; i < frames; i++)
                {
                    ring[wr * 2] = src[i * 2]; ring[wr * 2 + 1] = src[i * 2 + 1];
                    wr++; if (wr == cap) wr = 0;
                }
                fill += frames;
            }
        }

        /// <summary>Audio thread: fills count stereo frames. Plays silence while (re)buffering.</summary>
        public void Render(short[] buffer, int count)
        {
            lock (gate)
            {
                if (starved && fill >= TargetFrames) starved = false;
                int i = 0;
                if (!starved)
                {
                    int take = Math.Min(count, fill);
                    for (; i < take; i++)
                    {
                        buffer[i * 2] = ring[rd * 2]; buffer[i * 2 + 1] = ring[rd * 2 + 1];
                        rd++; if (rd == cap) rd = 0;
                    }
                    fill -= take;
                    Pulled += take;
                    if (take < count) { Underruns++; starved = true; }
                }
                for (; i < count; i++) { buffer[i * 2] = 0; buffer[i * 2 + 1] = 0; }
            }
        }
    }
}
