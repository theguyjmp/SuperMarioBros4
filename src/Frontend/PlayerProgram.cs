using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SMB4.Platform;

namespace SMB4.Frontend
{
    /// <summary>Process-wide services of the player (the frontend's equivalent of the old game's Host).</summary>
    public static class Player
    {
        public static GameConfig Config;
        public static PlayerSettings Settings;
        public static InputSystem Input;
        public static PlayerWindow Window;
        public static Emulator Emu;
        public static AudioOut Out;
        public static int RefreshHz = 60;
        public static string PacingInfo = "", GpuInfo = "";
        public static double MeasuredFps;
        static StreamWriter log;

        public static void OpenLog(string path)
        {
            try { log = new StreamWriter(path, false) { AutoFlush = true }; } catch { }
        }

        public static void Log(string s)
        {
            try { if (log != null) log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); } catch { }
        }

        public static void ApplyInputSettings()
        {
            Input.Bind = Settings.Bind;
            Input.Opt = Settings.Input;
            Input.Opt.Vibration = false;   // the SNES pad has no rumble
        }

        static readonly int[] LatencyMs = { 48, 64, 96 };

        /// <summary>Volume always; restart = reopen the device with the chosen buffer size.</summary>
        public static void ApplyAudioSettings(bool restart)
        {
            if (Emu == null) return;
            Emu.Audio.Volume = Settings.Volume / 100f;
            int ms = LatencyMs[Math.Max(0, Math.Min(2, Settings.AudioLatency))];
            Emu.Audio.TargetFrames = AudioOut.Rate * ms / 2 / 1000;
            if (!restart) return;
            if (Out != null) { Out.Dispose(); Out = null; }
            if (Cli.HasArg("--mute")) return;
            Emu.Audio.Reset();
            var o = new AudioOut();
            if (o.Start(Emu.Audio, AudioOut.Rate * ms / 2 / 1000 / 4, 4)) Out = o;
            else Log("audio: could not open the output device");
        }
    }

    /// <summary>Command-line helpers.</summary>
    public static class Cli
    {
        public static string[] Args = new string[0];

        public static bool HasArg(string a)
        {
            foreach (var s in Args) if (string.Equals(s, a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string ArgValue(string a)
        {
            for (int i = 0; i + 1 < Args.Length; i++) if (string.Equals(Args[i], a, StringComparison.OrdinalIgnoreCase)) return Args[i + 1];
            return null;
        }
    }

    /// <summary>Entry point of the player exe: runs the embedded SNES ROM through a libretro core.</summary>
    public static class PlayerProgram
    {
        public static string ExeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        public static GameConfig Config { get { return Player.Config; } }

        [STAThread]
        public static int Main(string[] args)
        {
            Cli.Args = args;
            Player.Config = GameConfig.Load();
            string saveDir = Cli.ArgValue("--savedir");
            if (string.IsNullOrEmpty(saveDir)) saveDir = Environment.GetEnvironmentVariable("PLAYER_SAVE_DIR");
            if (string.IsNullOrEmpty(saveDir)) saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Player.Config.AppFolder);
            if (Cli.HasArg("--selftest")) return SelfTest.Run(saveDir);
            try { Directory.CreateDirectory(saveDir); } catch { }
            PlayerSettings.Folder = saveDir;
            Player.OpenLog(Path.Combine(saveDir, "player.log"));

            SetDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash(e.Exception, saveDir);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception, saveDir);
            Native.timeBeginPeriod(1);
            try
            {
                Player.Settings = PlayerSettings.Load();
                Player.Input = new InputSystem();
                Player.ApplyInputSettings();
                var emu = new Emulator();
                emu.Log = Player.Log;
                Player.Emu = emu;
                string core = Emulator.FindCore(Player.Config, Cli.ArgValue("--core"));
                Player.Log("core path: " + core);
                emu.Start(Player.Config, Player.Settings, core, Cli.ArgValue("--rom"), saveDir);
                Player.Log("rom: " + emu.RomLabel + " (" + emu.RomSize + " bytes)");
                Player.ApplyAudioSettings(true);
                var win = new PlayerWindow();
                Player.Window = win;
                emu.Notify = s => win.Toast(s);
                win.Run();
            }
            catch (Exception ex) { Crash(ex, saveDir); return 1; }
            finally
            {
                if (Player.Input != null) Player.Input.Shutdown();
                if (Player.Out != null) Player.Out.Dispose();
                if (Player.Emu != null) { try { Player.Emu.FlushSram(true); } catch { } Player.Emu.Dispose(); }
                if (Player.Settings != null) Player.Settings.Save();
                Native.timeEndPeriod(1);
            }
            return 0;
        }

        static void SetDpiAwareness()
        {
            try { if (Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { Native.SetProcessDPIAware(); } catch { }
        }

        static bool crashed;
        static void Crash(Exception ex, string dir)
        {
            if (crashed) return;
            crashed = true;
            string path = Path.Combine(dir, "crash.txt");
            try { File.WriteAllText(path, DateTime.Now + "\r\n" + ex); } catch { }
            try { if (Player.Emu != null) Player.Emu.FlushSram(false); } catch { }
            string title = Player.Config != null ? Player.Config.Title : "SNES Player";
            bool friendly = ex is InvalidOperationException;
            try
            {
                MessageBox.Show(friendly ? ex.Message : title + " hit an unexpected error and has to close.\n\n" +
                    (ex == null ? "" : ex.GetType().Name + ": " + ex.Message) + "\n\nDetails were saved to:\n" + path,
                    title, MessageBoxButtons.OK, friendly ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
            }
            catch { }
            Environment.Exit(1);
        }
    }

    public static class Png
    {
        /// <summary>Writes an ARGB buffer (w x h) as a PNG of outW x outH (nearest neighbour).</summary>
        public static void Write(int[] argb, int w, int h, int outW, int outH, string path)
        {
            using (var bmp = new Bitmap(outW, outH, PixelFormat.Format32bppArgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, outW, outH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var row = new int[outW];
                for (int y = 0; y < outH; y++)
                {
                    int sy = y * h / outH;
                    for (int x = 0; x < outW; x++) row[x] = argb[sy * w + x * w / outW] | unchecked((int)0xFF000000);
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                }
                bmp.UnlockBits(data);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
