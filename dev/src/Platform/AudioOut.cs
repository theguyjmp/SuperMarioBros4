using System;
using System.Runtime.InteropServices;
using System.Threading;
using N = SMB4.Platform.Native;

namespace SMB4.Platform
{
    /// <summary>Fills <c>count</c> stereo frames (interleaved L,R shorts: buffer.Length >= count * 2).</summary>
    public interface ISampleSource { void Render(short[] buffer, int count); }

    /// <summary>
    /// Streams interleaved stereo 16-bit audio through waveOut on a dedicated high-priority thread.
    /// A ring of small buffers keeps latency around 40 ms without underruns.
    /// </summary>
    public sealed class AudioOut : IDisposable
    {
        public const int Rate = 48000;
        IntPtr hwo;
        IntPtr[] hdrs, datas;
        short[] temp;
        int bufSamples;
        Thread thread;
        volatile bool running;
        ISampleSource source;
        static readonly int HdrSize = Marshal.SizeOf(typeof(N.WAVEHDR));
        static readonly int FlagsOffset = (int)Marshal.OffsetOf(typeof(N.WAVEHDR), "dwFlags");
        public bool Ok { get; private set; }

        public bool Start(ISampleSource src, int samplesPerBuffer, int bufferCount)
        {
            source = src;
            bufSamples = samplesPerBuffer;
            var fmt = new N.WAVEFORMATEX();
            fmt.wFormatTag = 1; fmt.nChannels = 2; fmt.nSamplesPerSec = Rate; fmt.wBitsPerSample = 16;
            fmt.nBlockAlign = 4; fmt.nAvgBytesPerSec = Rate * 4; fmt.cbSize = 0;
            if (N.waveOutOpen(out hwo, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) { Ok = false; return false; }

            temp = new short[bufSamples * 2];
            hdrs = new IntPtr[bufferCount];
            datas = new IntPtr[bufferCount];
            for (int i = 0; i < bufferCount; i++)
            {
                datas[i] = Marshal.AllocHGlobal(bufSamples * 4);
                hdrs[i] = Marshal.AllocHGlobal(HdrSize);
                var h = new N.WAVEHDR();
                h.lpData = datas[i]; h.dwBufferLength = (uint)(bufSamples * 4);
                Marshal.StructureToPtr(h, hdrs[i], false);
                N.waveOutPrepareHeader(hwo, hdrs[i], HdrSize);
            }
            running = true;
            Ok = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.Name = "Audio";
            thread.Start();
            return true;
        }

        void Fill(int i)
        {
            source.Render(temp, bufSamples);
            Marshal.Copy(temp, 0, datas[i], bufSamples * 2);
            N.waveOutWrite(hwo, hdrs[i], HdrSize);
        }

        void Loop()
        {
            for (int i = 0; i < hdrs.Length; i++) Fill(i);
            while (running)
            {
                bool any = false;
                for (int i = 0; i < hdrs.Length; i++)
                {
                    int flags = Marshal.ReadInt32(hdrs[i], FlagsOffset);
                    if ((flags & (int)N.WHDR_DONE) != 0)
                    {
                        Marshal.WriteInt32(hdrs[i], FlagsOffset, flags & ~(int)N.WHDR_DONE);
                        Fill(i);
                        any = true;
                    }
                }
                if (!any) Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            if (!Ok) return;
            running = false;
            if (thread != null) thread.Join(500);
            N.waveOutReset(hwo);
            for (int i = 0; i < hdrs.Length; i++)
            {
                N.waveOutUnprepareHeader(hwo, hdrs[i], HdrSize);
                Marshal.FreeHGlobal(hdrs[i]);
                Marshal.FreeHGlobal(datas[i]);
            }
            N.waveOutClose(hwo);
            Ok = false;
        }
    }
}
