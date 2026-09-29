using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SMB4.Platform;

namespace SMB4
{
    /// <summary>All user options. Persisted as a small INI file in %APPDATA%\SuperMarioBros4.</summary>
    public sealed class Settings
    {
        // Video
        public bool Fullscreen = false;
        public int WindowScale = 0;          // 0 = auto
        public ScaleMode Scale = ScaleMode.Integer;
        public bool NtscAspect = false;
        public int Scanlines = 0;            // 0 = off, otherwise darkness percent
        public int Pacing = 0;               // 0 Auto, 1 VSync lock, 2 Smooth VSync, 3 VRR timer (G-Sync/FreeSync)
        public bool LowLatency = true;       // glFinish after swap
        public bool ShowFps = false;
        public bool ReduceFlashing = false;
        public bool ScreenShake = true;
        // Audio
        public int MusicVolume = 70;
        public int SfxVolume = 90;
        public bool AuthenticFilter = true;
        // Gameplay
        public bool ModernFeel = true;       // coyote time + jump buffer (see docs/01 §9)
        public bool PauseOnFocusLoss = true;
        public bool InfiniteLives = false;
        public bool ShowInputDisplay = false;
        // Controls
        public Bindings Bind = Bindings.Defaults();
        public InputOptions Input = new InputOptions();
        public int PadPreset = 0;

        public static string Folder
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("SMB4_SAVE_DIR");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperMarioBros4");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
        static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                s.Fullscreen = B(kv, "Fullscreen", s.Fullscreen);
                s.WindowScale = I(kv, "WindowScale", s.WindowScale);
                s.Scale = (ScaleMode)Clamp(I(kv, "ScaleMode", (int)s.Scale), 0, 2);
                s.NtscAspect = B(kv, "NtscAspect", s.NtscAspect);
                s.Scanlines = Clamp(I(kv, "Scanlines", s.Scanlines), 0, 80);
                s.Pacing = Clamp(I(kv, "Pacing", s.Pacing), 0, 3);
                s.LowLatency = B(kv, "LowLatency", s.LowLatency);
                s.ShowFps = B(kv, "ShowFps", s.ShowFps);
                s.ReduceFlashing = B(kv, "ReduceFlashing", s.ReduceFlashing);
                s.ScreenShake = B(kv, "ScreenShake", s.ScreenShake);
                s.MusicVolume = Clamp(I(kv, "MusicVolume", s.MusicVolume), 0, 100);
                s.SfxVolume = Clamp(I(kv, "SfxVolume", s.SfxVolume), 0, 100);
                s.AuthenticFilter = B(kv, "AuthenticFilter", s.AuthenticFilter);
                s.ModernFeel = B(kv, "ModernFeel", s.ModernFeel);
                s.PauseOnFocusLoss = B(kv, "PauseOnFocusLoss", s.PauseOnFocusLoss);
                s.InfiniteLives = B(kv, "InfiniteLives", s.InfiniteLives);
                s.ShowInputDisplay = B(kv, "ShowInputDisplay", s.ShowInputDisplay);
                s.PadPreset = Clamp(I(kv, "PadPreset", s.PadPreset), 0, 1);
                s.Input.Deadzone = Clamp(I(kv, "Deadzone", s.Input.Deadzone), 10, 70);
                s.Input.StickCone = Clamp(I(kv, "StickCone", s.Input.StickCone), 20, 67);
                s.Input.UseStick = B(kv, "UseStick", s.Input.UseStick);
                s.Input.Vibration = B(kv, "Vibration", s.Input.Vibration);
                s.Input.VibrationStrength = Clamp(I(kv, "VibrationStrength", s.Input.VibrationStrength), 10, 100);
                s.Input.Socd = Clamp(I(kv, "Socd", s.Input.Socd), 0, 1);
                s.Input.GenericPads = B(kv, "GenericPads", s.Input.GenericPads);
                for (int a = 0; a < Bindings.ActCount; a++)
                {
                    string n = ((Act)a).ToString();
                    int[] v;
                    if (TryList(kv, "Key." + n, out v)) s.Bind.Keys[a] = v;
                    if (TryList(kv, "Pad." + n, out v)) s.Bind.XBtn[a] = v;
                    if (TryList(kv, "Joy." + n, out v)) s.Bind.JoyBtn[a] = v;
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Super Mario Bros. 4 settings");
                sb.AppendLine("[Video]");
                W(sb, "Fullscreen", Fullscreen); W(sb, "WindowScale", WindowScale); W(sb, "ScaleMode", (int)Scale);
                W(sb, "NtscAspect", NtscAspect); W(sb, "Scanlines", Scanlines); W(sb, "Pacing", Pacing);
                W(sb, "LowLatency", LowLatency); W(sb, "ShowFps", ShowFps); W(sb, "ReduceFlashing", ReduceFlashing); W(sb, "ScreenShake", ScreenShake);
                sb.AppendLine("[Audio]");
                W(sb, "MusicVolume", MusicVolume); W(sb, "SfxVolume", SfxVolume); W(sb, "AuthenticFilter", AuthenticFilter);
                sb.AppendLine("[Gameplay]");
                W(sb, "ModernFeel", ModernFeel); W(sb, "PauseOnFocusLoss", PauseOnFocusLoss); W(sb, "InfiniteLives", InfiniteLives);
                W(sb, "ShowInputDisplay", ShowInputDisplay);
                sb.AppendLine("[Controls]");
                W(sb, "PadPreset", PadPreset); W(sb, "Deadzone", Input.Deadzone); W(sb, "StickCone", Input.StickCone);
                W(sb, "UseStick", Input.UseStick); W(sb, "Vibration", Input.Vibration); W(sb, "VibrationStrength", Input.VibrationStrength);
                W(sb, "Socd", Input.Socd); W(sb, "GenericPads", Input.GenericPads);
                for (int a = 0; a < Bindings.ActCount; a++)
                {
                    string n = ((Act)a).ToString();
                    sb.AppendLine("Key." + n + "=" + string.Join(",", Bind.Keys[a]));
                    sb.AppendLine("Pad." + n + "=" + string.Join(",", Bind.XBtn[a]));
                    sb.AppendLine("Joy." + n + "=" + string.Join(",", Bind.JoyBtn[a]));
                }
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
            var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var l = new List<int>();
            foreach (var p in parts) { int x; if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) l.Add(x); }
            v = l.ToArray();
            return true;
        }
    }
}
