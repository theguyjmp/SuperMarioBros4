using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using SMB4.Platform;

namespace SMB4.Frontend
{
    /// <summary>
    /// Identity of the game this player runs. Embedded at build time from src/Frontend/game.ini (build.ps1 -GameIni);
    /// a game.ini next to the exe overrides individual keys. Nothing else in the frontend is game-specific.
    /// </summary>
    public sealed class GameConfig
    {
        public string Title = "SNES Game";
        public string AppFolder = "SnesPlayer";
        public string RomFile = "game.sfc";
        public string SaveFile = "save.srm";
        public string Core = "bsnes_libretro.dll";
        public string ScreenshotFolder = "SNES Screenshots";
        public string ScreenshotPrefix = "shot";
        public readonly Dictionary<string, string> CoreOptions = new Dictionary<string, string>(StringComparer.Ordinal);

        public static GameConfig Load()
        {
            var c = new GameConfig();
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("game.ini"))
                    if (s != null) using (var r = new StreamReader(s)) c.Apply(r.ReadToEnd());
            }
            catch { }
            try
            {
                string local = Path.Combine(PlayerProgram.ExeDir, "game.ini");
                if (File.Exists(local)) c.Apply(File.ReadAllText(local));
            }
            catch { }
            return c;
        }

        void Apply(string text)
        {
            foreach (var kv in Ini.Parse(text))
            {
                string k = kv.Key, v = kv.Value;
                if (k.StartsWith("CoreOption.", StringComparison.OrdinalIgnoreCase)) { CoreOptions[k.Substring(11)] = v; continue; }
                switch (k.ToLowerInvariant())
                {
                    case "title": Title = v; break;
                    case "appfolder": AppFolder = v; break;
                    case "romfile": RomFile = v; break;
                    case "savefile": SaveFile = v; break;
                    case "core": Core = v; break;
                    case "screenshotfolder": ScreenshotFolder = v; break;
                    case "screenshotprefix": ScreenshotPrefix = v; break;
                }
            }
        }
    }

    static class Ini
    {
        public static List<KeyValuePair<string, string>> Parse(string text)
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) l.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            return l;
        }
    }

    /// <summary>User options of the player. Persisted as %APPDATA%\&lt;AppFolder&gt;\player-settings.ini.</summary>
    public sealed class PlayerSettings
    {
        // Video
        public bool Fullscreen = false;
        public int WindowScale = 0;          // 0 = auto
        public ScaleMode Scale = ScaleMode.Integer;
        public bool NtscAspect = false;      // 8:7 pixels
        public int Scanlines = 0;            // 0 = off, otherwise darkness percent
        public bool ShowFps = false;
        // Timing
        public int Pacing = 0;               // 0 Auto, 1 VSync lock, 2 Smooth VSync, 3 VRR timer (G-Sync/FreeSync)
        public bool LowLatency = true;       // glFinish after swap
        public bool RunAhead = true;         // 1 frame of run-ahead (needs core serialization)
        public bool PauseOnFocusLoss = true;
        // Audio
        public int Volume = 100;
        public int AudioLatency = 1;         // 0 low, 1 normal, 2 safe
        // Controls
        public Bindings Bind = Bindings.Defaults();
        public InputOptions Input = new InputOptions();
        public int PadPreset = 0;
        // Core options chosen by the user (key → value)
        public readonly Dictionary<string, string> Core = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string[] ActNames = { "Up", "Down", "Left", "Right", "B", "Y", "Start", "Select", "A", "X", "L", "R" };

        public static string Folder;         // set by PlayerProgram (per-game %APPDATA% folder)
        static string FilePath { get { return Path.Combine(Folder, "player-settings.ini"); } }

        public static PlayerSettings Load()
        {
            var s = new PlayerSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Ini.Parse(File.ReadAllText(FilePath))) kv[p.Key] = p.Value;
                s.Fullscreen = B(kv, "Fullscreen", s.Fullscreen);
                s.WindowScale = Clamp(I(kv, "WindowScale", s.WindowScale), 0, 8);
                s.Scale = (ScaleMode)Clamp(I(kv, "ScaleMode", (int)s.Scale), 0, 2);
                s.NtscAspect = B(kv, "NtscAspect", s.NtscAspect);
                s.Scanlines = Clamp(I(kv, "Scanlines", s.Scanlines), 0, 80);
                s.ShowFps = B(kv, "ShowFps", s.ShowFps);
                s.Pacing = Clamp(I(kv, "Pacing", s.Pacing), 0, 3);
                s.LowLatency = B(kv, "LowLatency", s.LowLatency);
                s.RunAhead = B(kv, "RunAhead", s.RunAhead);
                s.PauseOnFocusLoss = B(kv, "PauseOnFocusLoss", s.PauseOnFocusLoss);
                s.Volume = Clamp(I(kv, "Volume", s.Volume), 0, 100);
                s.AudioLatency = Clamp(I(kv, "AudioLatency", s.AudioLatency), 0, 2);
                s.PadPreset = Clamp(I(kv, "PadPreset", s.PadPreset), 0, 1);
                s.Input.Deadzone = Clamp(I(kv, "Deadzone", s.Input.Deadzone), 10, 70);
                s.Input.StickCone = Clamp(I(kv, "StickCone", s.Input.StickCone), 20, 67);
                s.Input.UseStick = B(kv, "UseStick", s.Input.UseStick);
                s.Input.Socd = Clamp(I(kv, "Socd", s.Input.Socd), 0, 1);
                s.Input.GenericPads = B(kv, "GenericPads", s.Input.GenericPads);
                s.Input.Vibration = false;
                for (int a = 0; a < Bindings.ActCount; a++)
                {
                    string n = ActNames[a];
                    int[] v;
                    if (TryList(kv, "Key." + n, out v)) s.Bind.Keys[a] = v;
                    if (TryList(kv, "Pad." + n, out v)) s.Bind.XBtn[a] = v;
                    if (TryList(kv, "Joy." + n, out v)) s.Bind.JoyBtn[a] = v;
                }
                foreach (var p in kv) if (p.Key.StartsWith("Core.", StringComparison.OrdinalIgnoreCase)) s.Core[p.Key.Substring(5)] = p.Value;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# " + PlayerProgram.Config.Title + " player settings");
                sb.AppendLine("[Video]");
                W(sb, "Fullscreen", Fullscreen); W(sb, "WindowScale", WindowScale); W(sb, "ScaleMode", (int)Scale);
                W(sb, "NtscAspect", NtscAspect); W(sb, "Scanlines", Scanlines); W(sb, "ShowFps", ShowFps);
                sb.AppendLine("[Timing]");
                W(sb, "Pacing", Pacing); W(sb, "LowLatency", LowLatency); W(sb, "RunAhead", RunAhead); W(sb, "PauseOnFocusLoss", PauseOnFocusLoss);
                sb.AppendLine("[Audio]");
                W(sb, "Volume", Volume); W(sb, "AudioLatency", AudioLatency);
                sb.AppendLine("[Controls]");
                W(sb, "PadPreset", PadPreset); W(sb, "Deadzone", Input.Deadzone); W(sb, "StickCone", Input.StickCone);
                W(sb, "UseStick", Input.UseStick); W(sb, "Socd", Input.Socd); W(sb, "GenericPads", Input.GenericPads);
                for (int a = 0; a < Bindings.ActCount; a++)
                {
                    string n = ActNames[a];
                    sb.AppendLine("Key." + n + "=" + string.Join(",", Bind.Keys[a]));
                    sb.AppendLine("Pad." + n + "=" + string.Join(",", Bind.XBtn[a]));
                    sb.AppendLine("Joy." + n + "=" + string.Join(",", Bind.JoyBtn[a]));
                }
                sb.AppendLine("[Core]");
                foreach (var p in Core) sb.AppendLine("Core." + p.Key + "=" + p.Value);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch { }
        }

        static void W(StringBuilder sb, string k, int v) { sb.AppendLine(k + "=" + v.ToString(CultureInfo.InvariantCulture)); }
        static void W(StringBuilder sb, string k, bool v) { sb.AppendLine(k + "=" + (v ? "1" : "0")); }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
        static int I(Dictionary<string, string> kv, string k, int d)
        {
            string s; int v;
            return kv.TryGetValue(k, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : d;
        }
        static bool B(Dictionary<string, string> kv, string k, bool d) { string s; return kv.TryGetValue(k, out s) ? s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) : d; }
        static bool TryList(Dictionary<string, string> kv, string k, out int[] v)
        {
            v = null; string s;
            if (!kv.TryGetValue(k, out s)) return false;
            var l = new List<int>();
            foreach (var p in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) { int x; if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) l.Add(x); }
            v = l.ToArray();
            return true;
        }
    }
}
