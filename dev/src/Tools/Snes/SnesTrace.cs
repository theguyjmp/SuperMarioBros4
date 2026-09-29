using System;
using System.IO;
using System.Text;
using SMB4.Audio;
using SMB4.Game;
using SMB4.Platform;

namespace SMB4.Tools
{
    /// <summary>
    /// `smb4tools trace LEVEL OUT.txt SCRIPT [FORM]` — runs the C# World with scripted input and writes one line per tick:
    /// "tick held x y xvel yvel state form" (x/y/vel in the game's 1/16 px units; held = C# Btn bits).
    /// SCRIPT uses the `shot` token syntax (R30 = hold right 30 ticks, BR60 = run right, J/A = jump, D/U/L, W30 = wait),
    /// but Pressed is derived continuously across tokens (a button held in two consecutive tokens is not re-pressed),
    /// exactly like a real pad — the SNES parity test (snes/test/parity.ps1) feeds the same held bits to the ROM.
    /// </summary>
    public static class SnesTrace
    {
        public static int Run(string[] args)
        {
            Host.Settings = new Settings();
            Host.Input = new InputSystem();
            Sound.Init(false);
            string id = args[1], outPath = args[2], script = args.Length > 3 ? args[3] : "W60";
            var def = LevelLoader.Load(id);
            if (def == null) { Console.WriteLine("no level " + id); return 1; }
            var s = new Session(new SaveData());
            if (args.Length > 4) { Form f; if (Enum.TryParse(args[4], true, out f)) s.Form = f; }
            var w = new World(def, s);
            var sb = new StringBuilder();
            int prev = 0;
            foreach (var tokRaw in script.Split(','))
            {
                string tok = tokRaw.Trim().ToUpperInvariant();
                if (tok.Length == 0) continue;
                int held = 0, n = 1, i = 0;
                while (i < tok.Length && !char.IsDigit(tok[i]))
                {
                    char c = tok[i++];
                    if (c == 'R') held |= Btn.Right; else if (c == 'L') held |= Btn.Left; else if (c == 'U') held |= Btn.Up; else if (c == 'D') held |= Btn.Down;
                    else if (c == 'J' || c == 'A') held |= Btn.A; else if (c == 'B') held |= Btn.B;
                }
                if (i < tok.Length) n = int.Parse(tok.Substring(i));
                for (int k = 0; k < n; k++)
                {
                    var pad = new PadState { Held = held, Pressed = held & ~prev, Released = prev & ~held };
                    prev = held;
                    w.Tick(pad);
                    var p = w.P;
                    sb.Append(w.Frame).Append(' ').Append(held).Append(' ').Append(p.X).Append(' ').Append(p.Y).Append(' ')
                      .Append(p.XVel).Append(' ').Append(p.YVel).Append(' ').Append((int)p.State).Append(' ').Append((int)p.Form).Append('\n');
                }
            }
            File.WriteAllText(outPath, sb.ToString());
            var el = new StringBuilder();
            foreach (var e in w.Ents) el.Append(e.GetType().Name).Append('@').Append(e.Px).Append(',').Append(e.Py).Append(' ');
            Console.WriteLine("  form=" + w.P.Form + " score=" + s.Cur.Score + " coins=" + s.Cur.Coins + " area=" + w.AreaIndex + " time=" + w.Time + " result=" + w.Result + " ents: " + el);
            Console.WriteLine("trace " + id + ": " + w.Frame + " ticks -> " + outPath + " (player " + w.P.Px + "," + w.P.Py + " state=" + w.P.State + ")");
            return 0;
        }
    }
}
