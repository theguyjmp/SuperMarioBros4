using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SMB4.Engine
{
    /// <summary>
    /// Game data (levels, maps, art, music) is embedded in the exe. When a "data" folder exists next to the
    /// exe or in the project root, files there take priority, so content can be edited without rebuilding.
    /// </summary>
    public static class Data
    {
        static string diskRoot;
        static readonly Dictionary<string, string> embedded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool init;

        static void Init()
        {
            if (init) return;
            init = true;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var n in asm.GetManifestResourceNames()) embedded[n.Replace('\\', '/')] = n;
            try
            {
                string exeDir = Path.GetDirectoryName(asm.Location);
                foreach (var cand in new[] { Path.Combine(exeDir, "data"), Path.Combine(exeDir, "..", "data"), Path.Combine(exeDir, "..", "..", "data") })
                {
                    if (Directory.Exists(cand) && Environment.GetEnvironmentVariable("SMB4_EMBEDDED_ONLY") == null) { diskRoot = Path.GetFullPath(cand); break; }
                }
            }
            catch { }
        }

        public static bool UsingDisk { get { Init(); return diskRoot != null; } }

        public static string ReadText(string path)
        {
            Init();
            path = path.Replace('\\', '/');
            if (diskRoot != null)
            {
                string f = Path.Combine(diskRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(f)) return File.ReadAllText(f);
            }
            string res;
            if (embedded.TryGetValue(path, out res))
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
            return null;
        }

        /// <summary>All data paths under a folder prefix (e.g. "music/"), disk and embedded merged.</summary>
        public static List<string> List(string prefix)
        {
            Init();
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in embedded.Keys) if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) set.Add(k);
            if (diskRoot != null)
            {
                string d = Path.Combine(diskRoot, prefix.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(d))
                    foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        set.Add(f.Substring(diskRoot.Length + 1).Replace('\\', '/'));
            }
            return new List<string>(set);
        }
    }
}
