using System;
using System.Collections.Generic;
using System.IO;
using SMB4.Engine;
using SMB4.Platform;

namespace SMB4.Audio
{
    /// <summary>Game-facing audio API. Everything is posted to the audio thread.</summary>
    public static class Sound
    {
        static Synth synth;
        static AudioOut output;
        static readonly Dictionary<string, Song> songs = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<string> Errors = new List<string>();
        static string current = "";
        static bool enabled;

        public static void Init(bool withDevice)
        {
            synth = new Synth();
            LoadSongs();
            ApplyVolumes();
            enabled = true;
            if (withDevice)
            {
                output = new AudioOut();
                if (!output.Start(synth, 480, 5)) output = null;
            }
        }

        public static void LoadSongs()
        {
            songs.Clear();
            foreach (var path in Data.List("music/"))
            {
                if (!path.EndsWith(".mml", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                var text = Data.ReadText(path);
                if (text == null) continue;
                songs[name] = Mml.Parse(name, text, Errors);
            }
        }

        public static bool HasSong(string name) { return songs.ContainsKey(name); }
        public static Song GetSong(string name) { Song s; return songs.TryGetValue(name, out s) ? s : null; }
        public static IEnumerable<string> SongNames { get { return songs.Keys; } }

        public static void Shutdown()
        {
            if (output != null) { output.Dispose(); output = null; }
        }

        public static void ApplyVolumes()
        {
            if (synth == null) return;
            var s = Host.Settings;
            float m = s == null ? 0.7f : s.MusicVolume / 100f, x = s == null ? 0.9f : s.SfxVolume / 100f;
            synth.MusicGain = m * m;   // perceptual curve
            synth.SfxGain = x * x;
            synth.Authentic = s == null || s.AuthenticFilter;
        }

        /// <summary>Starts a song. Does nothing if that song is already playing (unless restart).</summary>
        public static void Music(string name, bool restart = false)
        {
            if (synth == null) return;
            if (!restart && string.Equals(current, name, StringComparison.OrdinalIgnoreCase) && !synth.Music.Finished) return;
            Song s;
            if (!songs.TryGetValue(name, out s)) { StopMusic(); return; }
            current = name;
            synth.Music.Finished = false;
            synth.Post(() => { synth.Music.Play(s); synth.Paused = false; });
        }

        public static string CurrentMusic { get { return current; } }
        public static bool MusicFinished { get { return synth == null || synth.Music.Finished; } }

        public static void StopMusic()
        {
            current = "";
            if (synth == null) return;
            synth.Music.Finished = true;
            synth.Post(() => synth.Music.Stop());
        }

        public static void FadeOutMusic(int frames)
        {
            if (synth == null) return;
            current = "";
            synth.Post(() => synth.Music.FadeOut(frames));
        }

        public static void SetMusicSpeed(double speed)
        {
            if (synth == null) return;
            synth.Post(() => synth.Music.Speed = speed);
        }

        public static void PauseMusic(bool paused)
        {
            if (synth == null) return;
            synth.Post(() => synth.Paused = paused);
        }

        public static void Sfx(SfxId id)
        {
            if (synth == null) return;
            var defs = SfxBank.Defs[(int)id];
            if (defs == null) return;
            synth.Post(() => { foreach (var d in defs) synth.Sfx.Start(d); });
        }

        public static void StopSfx(SfxId id)
        {
            if (synth == null) return;
            var defs = SfxBank.Defs[(int)id];
            if (defs == null) return;
            synth.Post(() => { foreach (var d in defs) synth.Sfx.Stop(d); });
        }

        public static void StopAllSfx()
        {
            if (synth == null) return;
            synth.Post(() => synth.Sfx.StopAll());
        }

        /// <summary>Offline render (tools): plays a song for N seconds into an interleaved stereo 16-bit PCM buffer.</summary>
        public static short[] RenderOffline(string song, double seconds)
        {
            var s = new Synth();
            bool sfxReel = string.Equals(song, "sfx", StringComparison.OrdinalIgnoreCase);
            Song sg = null;
            if (!sfxReel) { if (!songs.TryGetValue(song, out sg)) return new short[0]; s.Music.Play(sg); }
            int frames = (int)(seconds * AudioOut.Rate);
            var buf = new short[frames * 2];
            var tmp = new short[480 * 2];
            int reel = 0;
            for (int i = 0; i < frames; i += 480)
            {
                // "sfx" reel: every SFX in enum order, one every 0.8 s
                if (sfxReel && i % 38400 == 0 && reel < (int)SfxId.Count) { var defs = SfxBank.Defs[reel++]; if (defs != null) foreach (var d in defs) s.Sfx.Start(d); }
                s.Render(tmp, 480);
                Array.Copy(tmp, 0, buf, i * 2, Math.Min(480, frames - i) * 2);
            }
            return buf;
        }
    }
}
