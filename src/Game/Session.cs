using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SMB4.Game
{
    public enum Item { None = 0, Mushroom, Flower, Leaf, Star, PWing, Tanooki, Frog, Hammer, Cloud }

    public sealed class PlayerProgress
    {
        public int Lives = 4, Score, Coins;
        public int NSpadeAt = 80000;       // score at which the next N-Spade panel appears
        public Form Form = Form.Small;
        public List<int> Cards = new List<int>();
        public List<Item> Items = new List<Item>();
        public bool GameOver;
    }

    /// <summary>One save slot: world progress plus both players' lives/score/items.</summary>
    public sealed class SaveData
    {
        public int Slot;
        public bool Exists;
        public int World = 1;
        public bool TwoPlayer;
        public int Turn;
        public HashSet<string> Cleared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // "w:node" keys
        public HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // opened locks, used toad houses...
        public int MapX = -1, MapY = -1;
        public PlayerProgress[] Players = { new PlayerProgress(), new PlayerProgress() };
        public int PlayFrames;
        public bool Beaten;
        public int HighestWorld = 1;

        static string PathFor(int slot) { return System.IO.Path.Combine(Settings.Folder, "save" + (slot + 1) + ".txt"); }

        public static SaveData Load(int slot)
        {
            var s = new SaveData { Slot = slot };
            try
            {
                string p = PathFor(slot);
                if (!File.Exists(p)) return s;
                foreach (var raw in File.ReadAllLines(p))
                {
                    int eq = raw.IndexOf('='); if (eq < 0) continue;
                    string k = raw.Substring(0, eq), v = raw.Substring(eq + 1);
                    switch (k)
                    {
                        case "world": s.World = int.Parse(v); break;
                        case "highest": s.HighestWorld = int.Parse(v); break;
                        case "two": s.TwoPlayer = v == "1"; break;
                        case "turn": s.Turn = int.Parse(v); break;
                        case "cleared": foreach (var c in v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) s.Cleared.Add(c); break;
                        case "flags": foreach (var c in v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) s.Flags.Add(c); break;
                        case "mapx": s.MapX = int.Parse(v); break;
                        case "mapy": s.MapY = int.Parse(v); break;
                        case "frames": s.PlayFrames = int.Parse(v); break;
                        case "beaten": s.Beaten = v == "1"; break;
                        default:
                            if (k.StartsWith("p0.") || k.StartsWith("p1."))
                            {
                                var pp = s.Players[k[1] - '0'];
                                string f = k.Substring(3);
                                if (f == "lives") pp.Lives = int.Parse(v);
                                else if (f == "score") pp.Score = int.Parse(v);
                                else if (f == "coins") pp.Coins = int.Parse(v);
                                else if (f == "nspade") pp.NSpadeAt = int.Parse(v);
                                else if (f == "form") pp.Form = (Form)int.Parse(v);
                                else if (f == "cards") { pp.Cards.Clear(); foreach (var c in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) pp.Cards.Add(int.Parse(c)); }
                                else if (f == "items") { pp.Items.Clear(); foreach (var c in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) pp.Items.Add((Item)int.Parse(c)); }
                                else if (f == "over") pp.GameOver = v == "1";
                            }
                            break;
                    }
                }
                s.Exists = true;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("world=" + World);
                sb.AppendLine("highest=" + HighestWorld);
                sb.AppendLine("two=" + (TwoPlayer ? 1 : 0));
                sb.AppendLine("turn=" + Turn);
                sb.AppendLine("cleared=" + string.Join(";", Cleared));
                sb.AppendLine("flags=" + string.Join(";", Flags));
                sb.AppendLine("mapx=" + MapX);
                sb.AppendLine("mapy=" + MapY);
                sb.AppendLine("frames=" + PlayFrames);
                sb.AppendLine("beaten=" + (Beaten ? 1 : 0));
                for (int i = 0; i < 2; i++)
                {
                    var p = Players[i];
                    sb.AppendLine("p" + i + ".lives=" + p.Lives);
                    sb.AppendLine("p" + i + ".score=" + p.Score);
                    sb.AppendLine("p" + i + ".coins=" + p.Coins);
                    sb.AppendLine("p" + i + ".nspade=" + p.NSpadeAt);
                    sb.AppendLine("p" + i + ".form=" + (int)p.Form);
                    sb.AppendLine("p" + i + ".cards=" + string.Join(",", p.Cards));
                    var items = new List<string>(); foreach (var it in p.Items) items.Add(((int)it).ToString());
                    sb.AppendLine("p" + i + ".items=" + string.Join(",", items));
                    sb.AppendLine("p" + i + ".over=" + (p.GameOver ? 1 : 0));
                }
                string path = PathFor(Slot), tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(path)) { File.Copy(path, path + ".bak", true); File.Delete(path); }
                File.Move(tmp, path);
                Exists = true;
            }
            catch { }
        }

        public static void Erase(int slot)
        {
            try { string p = PathFor(slot); if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    /// <summary>The running game: save data + the active player.</summary>
    public sealed class Session
    {
        public SaveData Save;
        public int PlayerIndex;
        public bool UsePWing, StartStar;
        public PlayerProgress Cur { get { return Save.Players[PlayerIndex]; } }
        public Form Form { get { return Cur.Form; } set { Cur.Form = value; } }
        public int Lives { get { return Cur.Lives; } }

        public Session(SaveData s) { Save = s; PlayerIndex = s.TwoPlayer ? s.Turn : 0; }

        public void AddScore(int pts) { Cur.Score = Math.Min(9999990, Cur.Score + pts); }

        public void AddLife()
        {
            if (Settings0.InfiniteLives) return;
            Cur.Lives = Math.Min(99, Cur.Lives + 1);
        }

        public bool AddCoin()
        {
            Cur.Coins++;
            if (Cur.Coins >= 100) { Cur.Coins -= 100; AddLife(); return true; }
            return false;
        }

        /// <summary>Adds a goal card; on the third card returns the bonus lives (1/2/3/5) and clears the set.</summary>
        public int AddCard(int card)
        {
            Cur.Cards.Add(card);
            if (Cur.Cards.Count < 3) return 0;
            int a = Cur.Cards[0], b = Cur.Cards[1], c = Cur.Cards[2];
            Cur.Cards.Clear();
            if (a == b && b == c) return a == 0 ? 2 : a == 1 ? 3 : 5;
            return 1;
        }

        public void LoseLife()
        {
            if (Settings0.InfiniteLives) return;
            Cur.Lives--;
        }

        static Settings Settings0 { get { return SMB4.Platform.Host.Settings ?? new Settings(); } }
    }
}
