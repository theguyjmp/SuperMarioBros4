using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SMB4.Engine;

namespace SMB4.Platform
{
    public interface IGame
    {
        void Tick(PadState p1, PadState p2);
        void Render(Ppu ppu);
        void OnFocusChanged(bool focused);
        bool WantsQuit { get; }
    }

    /// <summary>Process-wide services shared by the platform layer and the game.</summary>
    public static class Host
    {
        public static Settings Settings;
        public static InputSystem Input;
        public static GameWindow Window;
        public static AudioOut Audio;
        public static int RefreshHz = 60;
        public static string PacingInfo = "";
        public static string GpuInfo = "";
        public static double MeasuredFps;
        public static long TickCount;

        public static void ApplyInputSettings()
        {
            Input.Bind = Settings.Bind;
            Input.Opt = Settings.Input;
        }

        public static void ApplyVideo()
        {
            if (Window != null) Window.ApplyVideoSettings();
        }

        public static void Quit()
        {
            if (Window != null) Window.RequestQuit();
        }

        public static string SaveScreenshot(int[] argb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Super Mario Bros 4");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "smb4_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                WritePng(argb, 256, 240, 3, path);
                return path;
            }
            catch { return null; }
        }

        /// <summary>Writes an ARGB buffer as a PNG, nearest-neighbour upscaled by 'scale'.</summary>
        public static void WritePng(int[] argb, int w, int h, int scale, string path)
        {
            using (var bmp = new Bitmap(w * scale, h * scale, PixelFormat.Format32bppArgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, w * scale, h * scale), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var row = new int[w * scale];
                for (int y = 0; y < h * scale; y++)
                {
                    int sy = y / scale;
                    for (int x = 0; x < w * scale; x++) row[x] = argb[sy * w + x / scale];
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                }
                bmp.UnlockBits(data);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
