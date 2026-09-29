using System;
using System.IO;

namespace SMB4.Tools
{
    /// <summary>
    /// `smb4tools snes-export OUTDIR`: converts data/ (art, levels, maps, music) into SNES-ready binaries and ca65
    /// include files for the ROM build (snes/build.ps1). Each part lives in its own file (see snes/DESIGN.md §Ownership).
    /// </summary>
    public static partial class SnesExport
    {
        public static int Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            int fails = 0;
            fails += Step("levels+tiles", () => ExportLevels(outDir));
            fails += Step("sprites", () => ExportSprites(outDir));
            fails += Step("backgrounds", () => ExportBackgrounds(outDir));
            fails += Step("audio", () => ExportAudio(outDir));
            fails += Step("screens", () => ExportScreens(outDir));
            Console.WriteLine(fails == 0 ? "snes-export OK -> " + outDir : "snes-export: " + fails + " step(s) failed");
            return fails == 0 ? 0 : 1;
        }

        static int Step(string name, Func<int> f)
        {
            try { int r = f(); if (r != 0) Console.WriteLine("  " + name + ": FAILED (" + r + ")"); return r != 0 ? 1 : 0; }
            catch (Exception ex) { Console.WriteLine("  " + name + ": ERROR " + ex); return 1; }
        }

        /// <summary>SNES BGR555 from a color index.</summary>
        public static ushort Bgr555(int colorIndex)
        {
            int rgb = SMB4.Engine.NesPalette.RgbOf(colorIndex);
            int r = (rgb >> 19) & 31, g = (rgb >> 11) & 31, b = (rgb >> 3) & 31;
            return (ushort)(r | (g << 5) | (b << 10));
        }
    }
}
