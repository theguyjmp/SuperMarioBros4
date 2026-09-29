using System;
using System.IO;
using System.Windows.Forms;
using SMB4.Platform;

namespace SMB4
{
    /// <summary>LEGACY entry point of the retired C# game (reference only). It is compiled into smb4tools (the converter and
    /// reference tests use src/Game) but is no longer the shipped exe: that is the libretro player in src/Frontend.</summary>
    public static class Program
    {
        public static string[] Args = new string[0];

        [STAThread]
        public static int Main(string[] args)
        {
            Args = args;
            SetDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            Native.timeBeginPeriod(1);
            try
            {
                Host.Settings = Settings.Load();
                Host.Input = new InputSystem();
                Host.ApplyInputSettings();
                Audio.Sound.Init(!HasArg("--mute"));
                var game = new Game.GameMain();
                var win = new GameWindow(game);
                Host.Window = win;
                win.Run();
            }
            catch (Exception ex) { Crash(ex); return 1; }
            finally
            {
                if (Host.Input != null) Host.Input.Shutdown();
                Audio.Sound.Shutdown();
                if (Host.Settings != null) Host.Settings.Save();
                Native.timeEndPeriod(1);
            }
            return 0;
        }

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

        static void SetDpiAwareness()
        {
            try { if (Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { Native.SetProcessDPIAware(); } catch { }
        }

        static bool crashed;
        public static void Crash(Exception ex)
        {
            if (crashed) return;
            crashed = true;
            string path = Path.Combine(Settings.Folder, "crash.txt");
            try { File.WriteAllText(path, DateTime.Now + "\r\n" + ex); } catch { }
            try
            {
                MessageBox.Show("Super Mario Bros. 4 hit an unexpected error and has to close.\n\n" +
                    (ex == null ? "" : ex.GetType().Name + ": " + ex.Message) + "\n\nDetails were saved to:\n" + path,
                    "Super Mario Bros. 4", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            Environment.Exit(1);
        }
    }
}
