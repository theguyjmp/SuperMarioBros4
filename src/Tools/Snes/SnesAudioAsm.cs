using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SMB4.Tools
{
    /// <summary>
    /// A small two-pass SPC700 assembler (ca65 has no SPC700 CPU). Used by the sound converter to build the
    /// sound driver from snes/spc/driver.asm. Syntax (Sony/"bass"-style operands):
    ///   labels `name:` and local `@name:` (scoped to the previous global label); `name = expr` constants
    ///   directives: .org expr | .db/.byte list | .dw/.word list | .fill count[,value] | .align n | .assert expr, "msg"
    ///   operands: #imm  A X Y SP YA PSW C  (X) (Y) (X)+  [d+X]  [d]+Y  [!a+X]  d  d+X  d+Y  !a  !a+X  !a+Y
    ///             d.bit (set1/clr1/bbs/bbc)  m.bit and /m.bit (mov1/and1/or1/eor1/not1)
    ///   An address operand is direct page when its value is known on pass 1 and &lt; $100 (define zero-page
    ///   variables before use); a `!` prefix forces absolute.
    ///   expressions: + - * / % &amp; | ^ &lt;&lt; &gt;&gt; ~, unary &lt; (low byte) &gt; (high byte), parentheses, $hex %bin decimal 'c'
    /// </summary>
    public sealed class SpcAsm
    {
        public readonly Dictionary<string, int> Symbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Errors = new List<string>();
        public readonly byte[] Mem = new byte[0x10000];
        public int Lo = 0x10000, Hi = 0;              // emitted range [Lo, Hi)
        readonly Dictionary<int, bool> dpDecision = new Dictionary<int, bool>();
        int pass, pc, lineNo; string file = "", lastGlobal = "";
        bool unresolved;

        static readonly Dictionary<string, int> Ops = BuildOps();

        public static SpcAsm Assemble(string path, Dictionary<string, int> predefined)
        {
            var a = new SpcAsm();
            var lines = File.ReadAllLines(path);
            a.file = Path.GetFileName(path);
            if (predefined != null) foreach (var kv in predefined) a.Symbols[kv.Key] = kv.Value;
            for (a.pass = 1; a.pass <= 2; a.pass++)
            {
                a.pc = 0; a.lastGlobal = "";
                for (int i = 0; i < lines.Length; i++)
                {
                    a.lineNo = i + 1;
                    try { a.Line(lines[i]); }
                    catch (AsmError e) { if (a.pass == 2 || e.Message.StartsWith("duplicate")) a.Errors.Add(a.file + "(" + a.lineNo + "): " + e.Message + "  | " + lines[i].Trim()); }
                }
                if (a.pass == 1 && a.Errors.Count > 0) break;
            }
            return a;
        }

        sealed class AsmError : Exception { public AsmError(string m) : base(m) { } }

        void Line(string raw)
        {
            string s = StripComment(raw).Trim();
            if (s.Length == 0) return;
            // label(s)
            while (true)
            {
                int c = LabelColon(s);
                if (c < 0) break;
                DefineLabel(s.Substring(0, c).Trim(), pc);
                s = s.Substring(c + 1).Trim();
                if (s.Length == 0) return;
            }
            // constant: name = expr
            int eq = s.IndexOf('=');
            if (eq > 0 && IsIdent(s.Substring(0, eq).Trim()))
            {
                string name = s.Substring(0, eq).Trim();
                unresolved = false;
                int v = Eval(s.Substring(eq + 1).Trim());
                if (!unresolved || pass == 2) Symbols[Scoped(name)] = v;
                return;
            }
            string mn, ops;
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            if (sp < 0) { mn = s; ops = ""; } else { mn = s.Substring(0, sp); ops = s.Substring(sp + 1).Trim(); }
            mn = mn.ToLowerInvariant();
            if (mn.StartsWith(".")) { Directive(mn, ops); return; }
            Instruction(mn, ops);
        }

        static string StripComment(string s)
        {
            bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '"' || (s[i] == '\'' && i + 2 < s.Length && s[i + 2] == '\'')) { if (s[i] == '\'') { i += 2; continue; } q = !q; }
                if (!q && s[i] == ';') return s.Substring(0, i);
            }
            return s;
        }

        static int LabelColon(string s)
        {
            int i = 0;
            if (i < s.Length && s[i] == '@') i++;
            if (i >= s.Length || !(char.IsLetter(s[i]) || s[i] == '_')) return -1;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            return i < s.Length && s[i] == ':' ? i : -1;
        }

        static bool IsIdent(string s)
        {
            if (s.Length == 0) return false;
            int i = s[0] == '@' ? 1 : 0;
            if (i >= s.Length || !(char.IsLetter(s[i]) || s[i] == '_')) return false;
            for (; i < s.Length; i++) if (!(char.IsLetterOrDigit(s[i]) || s[i] == '_')) return false;
            return true;
        }

        string Scoped(string name) { return name.StartsWith("@") ? lastGlobal + name : name; }

        void DefineLabel(string name, int v)
        {
            if (!name.StartsWith("@")) lastGlobal = name;
            string k = Scoped(name);
            if (pass == 1 && Symbols.ContainsKey(k)) throw new AsmError("duplicate label " + name);
            Symbols[k] = v;
        }

        void Emit(int b)
        {
            if (pc < 0 || pc > 0xFFFF) throw new AsmError("address out of range");
            if (pass == 2) { Mem[pc] = (byte)b; if (pc < Lo) Lo = pc; if (pc + 1 > Hi) Hi = pc + 1; }
            pc++;
        }

        void Directive(string d, string ops)
        {
            switch (d)
            {
                case ".org": pc = Eval(ops); if (unresolved) throw new AsmError(".org needs a known value"); return;
                case ".db": case ".byte":
                    foreach (var o in SplitOps(ops))
                    {
                        if (o.StartsWith("\"")) { foreach (char ch in o.Substring(1, o.Length - 2)) Emit(ch); continue; }
                        int v = Eval(o); if (pass == 2 && (v < -128 || v > 255)) throw new AsmError("byte out of range: " + v); Emit(v & 255);
                    }
                    return;
                case ".dw": case ".word":
                    foreach (var o in SplitOps(ops)) { int v = Eval(o); Emit(v & 255); Emit((v >> 8) & 255); }
                    return;
                case ".fill":
                    {
                        var p = SplitOps(ops); int n = Eval(p[0]); int v = p.Count > 1 ? Eval(p[1]) : 0;
                        for (int i = 0; i < n; i++) Emit(v); return;
                    }
                case ".align":
                    { int n = Eval(ops); while (pc % n != 0) Emit(0); return; }
                case ".assert":
                    {
                        var p = SplitOps(ops); int v = Eval(p[0]);
                        if (pass == 2 && v == 0) throw new AsmError("assert failed: " + (p.Count > 1 ? p[1] : p[0]));
                        return;
                    }
            }
            throw new AsmError("unknown directive " + d);
        }

        static List<string> SplitOps(string s)
        {
            var r = new List<string>(); int depth = 0; bool q = false; var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"') q = !q;
                if (!q && (c == '(' || c == '[')) depth++;
                if (!q && (c == ')' || c == ']')) depth--;
                if (!q && depth == 0 && c == ',') { r.Add(sb.ToString().Trim()); sb.Clear(); continue; }
                sb.Append(c);
            }
            if (sb.Length > 0 || r.Count > 0) r.Add(sb.ToString().Trim());
            return r;
        }

        // ------------------------------------------------------------------ operands
        struct Opnd { public string K; public int V; public int Bit; }

        Opnd ParseOp(string o, string mn)
        {
            var r = new Opnd();
            string l = o.ToLowerInvariant().Replace(" ", "");
            switch (l)
            {
                case "a": r.K = "A"; return r;
                case "x": r.K = "X"; return r;
                case "y": r.K = "Y"; return r;
                case "sp": r.K = "SP"; return r;
                case "ya": r.K = "YA"; return r;
                case "psw": r.K = "PSW"; return r;
                case "c": r.K = "C"; return r;
                case "(x)": r.K = "IX"; return r;
                case "(y)": r.K = "IY"; return r;
                case "(x)+": r.K = "IXP"; return r;
            }
            if (o.StartsWith("#")) { r.K = "IMM"; r.V = Eval(o.Substring(1)); return r; }
            if (l.StartsWith("[") && l.EndsWith("+x]"))
            {
                string inner = o.Substring(1, o.LastIndexOf('+') - 1).Trim();
                if (inner.StartsWith("!")) { r.K = "ABSXI"; r.V = Eval(inner.Substring(1)); return r; }
                r.K = mn == "jmp" ? "ABSXI" : "DPXI"; r.V = Eval(inner); return r;
            }
            if (l.StartsWith("[") && l.EndsWith("]+y")) { r.K = "DPIY"; r.V = Eval(o.Substring(1, o.LastIndexOf(']') - 1)); return r; }
            bool isBitOp = mn == "set1" || mn == "clr1" || mn == "bbs" || mn == "bbc" || mn == "mov1" || mn == "and1" || mn == "or1" || mn == "eor1" || mn == "not1";
            if (isBitOp && o.Length > 2 && o[o.Length - 2] == '.' && o[o.Length - 1] >= '0' && o[o.Length - 1] <= '7')
            {
                string e = o.Substring(0, o.Length - 2).Trim(); bool neg = false;
                if (e.StartsWith("/")) { neg = true; e = e.Substring(1).Trim(); }
                if (e.StartsWith("!")) e = e.Substring(1);
                r.V = Eval(e); r.Bit = o[o.Length - 1] - '0';
                if (mn == "set1" || mn == "clr1" || mn == "bbs" || mn == "bbc") { r.K = "DPBIT"; return r; }
                r.K = neg ? "NMEMBIT" : "MEMBIT"; return r;
            }
            bool forceAbs = false; string ex = o;
            string idx = "";
            if (l.EndsWith("+x")) { idx = "X"; ex = o.Substring(0, o.LastIndexOf('+')).Trim(); }
            else if (l.EndsWith("+y")) { idx = "Y"; ex = o.Substring(0, o.LastIndexOf('+')).Trim(); }
            if (ex.StartsWith("!")) { forceAbs = true; ex = ex.Substring(1); }
            unresolved = false;
            r.V = Eval(ex);
            bool isBranchTarget = IsBranch(mn);
            if (isBranchTarget) { r.K = "ADDR"; return r; }
            bool dp;
            int key = lineNo * 8 + opIndex;
            if (pass == 1) { dp = !forceAbs && !unresolved && r.V >= 0 && r.V < 0x100; dpDecision[key] = dp; }
            else dp = dpDecision.ContainsKey(key) && dpDecision[key];
            r.K = (dp ? "DP" : "ABS") + idx;
            return r;
        }
        int opIndex;

        static bool IsBranch(string mn)
        {
            switch (mn) { case "bra": case "beq": case "bne": case "bcs": case "bcc": case "bvs": case "bvc": case "bmi": case "bpl": case "jmp": case "call": return true; }
            return false;
        }

        void Instruction(string mn, string ops)
        {
            var list = ops.Length == 0 ? new List<string>() : SplitOps(ops);
            var od = new List<Opnd>();
            // multi-operand branch forms: bbs d.b,rel / bbc / cbne d,rel / dbnz d,rel
            bool relLast = mn == "bbs" || mn == "bbc" || mn == "cbne" || mn == "dbnz";
            for (int i = 0; i < list.Count; i++)
            {
                opIndex = i;
                if (relLast && i == list.Count - 1 && !(mn == "dbnz" && list.Count == 1)) { var rr = new Opnd { K = "REL", V = Eval(list[i]) }; od.Add(rr); continue; }
                od.Add(ParseOp(list[i], mn));
            }
            // single-target branches
            if (IsBranch(mn) && mn != "jmp" && mn != "call")
            {
                if (od.Count != 1) throw new AsmError("branch needs one target");
                Emit(Ops[mn + " REL"]); EmitRel(od[0].V); return;
            }
            if ((mn == "jmp" || mn == "call") && od.Count == 1 && od[0].K == "ADDR")
            {
                Emit(Ops[mn + " ABS"]); Emit(od[0].V & 255); Emit((od[0].V >> 8) & 255); return;
            }
            if (mn == "tcall") { int n = Eval(ops); Emit(0x01 | (n << 4)); return; }
            if (mn == "pcall") { int n = Eval(ops); Emit(0x4F); Emit(n & 255); return; }

            // try the table with DP forms, then with ABS fallbacks
            string key = Key(mn, od);
            int opc;
            if (!Ops.TryGetValue(key, out opc))
            {
                var od2 = new List<Opnd>();
                foreach (var o in od) { var c = o; if (c.K == "DP") c.K = "ABS"; else if (c.K == "DPX") c.K = "ABSX"; else if (c.K == "DPY") c.K = "ABSY"; od2.Add(c); }
                string key2 = Key(mn, od2);
                if (!Ops.TryGetValue(key2, out opc)) throw new AsmError("invalid instruction/addressing: " + key);
                od = od2; key = key2;
            }
            if (key.Contains("DPBIT")) { foreach (var o in od) if (o.K == "DPBIT") opc |= o.Bit << 5; }
            Emit(opc);
            bool reverse = key.EndsWith(" DP,IMM") || key.EndsWith(" DP,DP");
            var seq = new List<Opnd>(od);
            if (reverse) seq.Reverse();
            foreach (var o in seq) EmitOperand(o);
        }

        static string Key(string mn, List<Opnd> od)
        {
            var sb = new StringBuilder(mn);
            for (int i = 0; i < od.Count; i++) { sb.Append(i == 0 ? " " : ","); sb.Append(od[i].K); }
            return sb.ToString();
        }

        void EmitOperand(Opnd o)
        {
            switch (o.K)
            {
                case "IMM": if (pass == 2 && (o.V < -128 || o.V > 255)) throw new AsmError("immediate out of range: " + o.V); Emit(o.V & 255); break;
                case "DP": case "DPX": case "DPY": case "DPXI": case "DPIY": case "DPBIT":
                    if (pass == 2 && (o.V < 0 || o.V > 255)) throw new AsmError("direct page operand out of range: " + o.V);
                    Emit(o.V & 255); break;
                case "ABS": case "ABSX": case "ABSY": case "ABSXI": Emit(o.V & 255); Emit((o.V >> 8) & 255); break;
                case "MEMBIT": case "NMEMBIT":
                    { if (pass == 2 && o.V > 0x1FFF) throw new AsmError("mem.bit address > $1FFF"); int w = (o.V & 0x1FFF) | (o.Bit << 13); Emit(w & 255); Emit(w >> 8); break; }
                case "REL": EmitRel(o.V); break;
            }
        }

        void EmitRel(int target)
        {
            int d = target - (pc + 1);
            if (pass == 2 && (d < -128 || d > 127)) throw new AsmError("branch out of range (" + d + ")");
            Emit(d & 255);
        }

        // ------------------------------------------------------------------ expressions
        string ex; int ep;
        int Eval(string s)
        {
            ex = s; ep = 0; unresolved = false;
            int v = EOr();
            Skip();
            if (ep < ex.Length) throw new AsmError("bad expression: " + s);
            return v;
        }
        void Skip() { while (ep < ex.Length && char.IsWhiteSpace(ex[ep])) ep++; }
        bool Tk(string t) { Skip(); if (string.CompareOrdinal(ex, ep, t, 0, t.Length) == 0) { ep += t.Length; return true; } return false; }
        int EOr() { int v = EXor(); while (true) { if (Tk("|")) v |= EXor(); else return v; } }
        int EXor() { int v = EAnd(); while (true) { if (Tk("^")) v ^= EAnd(); else return v; } }
        int EAnd() { int v = EShift(); while (true) { if (Tk("&")) v &= EShift(); else return v; } }
        int EShift() { int v = EAdd(); while (true) { if (Tk("<<")) v <<= EAdd(); else if (Tk(">>")) v >>= EAdd(); else return v; } }
        int EAdd() { int v = EMul(); while (true) { if (Tk("+")) v += EMul(); else if (Tk("-")) v -= EMul(); else return v; } }
        int EMul()
        {
            int v = EUn();
            while (true)
            {
                if (Tk("*")) v *= EUn();
                else if (Tk("/")) { int d = EUn(); v = d == 0 ? 0 : v / d; }
                else return v;
            }
        }
        int EUn()
        {
            if (Tk("-")) return -EUn();
            if (Tk("~")) return ~EUn();
            if (Tk("<")) return EUn() & 255;
            if (Tk(">")) return (EUn() >> 8) & 255;
            return EAtom();
        }
        int EAtom()
        {
            Skip();
            if (ep >= ex.Length) throw new AsmError("missing operand in expression");
            char c = ex[ep];
            if (c == '(') { ep++; int v = EOr(); if (!Tk(")")) throw new AsmError("missing )"); return v; }
            if (c == '$') { ep++; int st = ep; while (ep < ex.Length && Uri.IsHexDigit(ex[ep])) ep++; return int.Parse(ex.Substring(st, ep - st), NumberStyles.HexNumber); }
            if (c == '%') { ep++; int v = 0; while (ep < ex.Length && (ex[ep] == '0' || ex[ep] == '1')) { v = v * 2 + (ex[ep] - '0'); ep++; } return v; }
            if (c == '\'' && ep + 2 < ex.Length && ex[ep + 2] == '\'') { int v = ex[ep + 1]; ep += 3; return v; }
            if (char.IsDigit(c)) { int st = ep; while (ep < ex.Length && char.IsDigit(ex[ep])) ep++; return int.Parse(ex.Substring(st, ep - st)); }
            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                int st = ep; ep++;
                while (ep < ex.Length && (char.IsLetterOrDigit(ex[ep]) || ex[ep] == '_')) ep++;
                string name = ex.Substring(st, ep - st);
                int v;
                if (Symbols.TryGetValue(Scoped(name), out v)) return v;
                if (pass == 2) throw new AsmError("undefined symbol " + name);
                unresolved = true; return 0x1234;
            }
            throw new AsmError("bad expression near '" + ex.Substring(ep) + "'");
        }

        // ------------------------------------------------------------------ opcode table
        static Dictionary<string, int> BuildOps()
        {
            var d = new Dictionary<string, int>();
            Action<string, int> A = (k, v) => d[k] = v;
            // ALU group: or/and/eor/cmp/adc/sbc share a layout (base = 00/20/40/60/80/A0)
            string[] alu = { "or", "and", "eor", "cmp", "adc", "sbc" };
            for (int i = 0; i < 6; i++)
            {
                int b = i * 0x20; string m = alu[i];
                A(m + " A,DP", b + 0x04); A(m + " A,DPX", b + 0x14); A(m + " A,ABS", b + 0x05); A(m + " A,ABSX", b + 0x15);
                A(m + " A,ABSY", b + 0x16); A(m + " A,IX", b + 0x06); A(m + " A,DPXI", b + 0x07); A(m + " A,DPIY", b + 0x17);
                A(m + " A,IMM", b + 0x08); A(m + " DP,IMM", b + 0x18); A(m + " DP,DP", b + 0x09); A(m + " IX,IY", b + 0x19);
            }
            A("cmp X,IMM", 0xC8); A("cmp X,DP", 0x3E); A("cmp X,ABS", 0x1E);
            A("cmp Y,IMM", 0xAD); A("cmp Y,DP", 0x7E); A("cmp Y,ABS", 0x5E);
            // mov
            A("mov A,IMM", 0xE8); A("mov A,IX", 0xE6); A("mov A,IXP", 0xBF); A("mov A,DP", 0xE4); A("mov A,DPX", 0xF4);
            A("mov A,ABS", 0xE5); A("mov A,ABSX", 0xF5); A("mov A,ABSY", 0xF6); A("mov A,DPXI", 0xE7); A("mov A,DPIY", 0xF7);
            A("mov X,IMM", 0xCD); A("mov X,DP", 0xF8); A("mov X,DPY", 0xF9); A("mov X,ABS", 0xE9);
            A("mov Y,IMM", 0x8D); A("mov Y,DP", 0xEB); A("mov Y,DPX", 0xFB); A("mov Y,ABS", 0xEC);
            A("mov IX,A", 0xC6); A("mov IXP,A", 0xAF); A("mov DP,A", 0xC4); A("mov DPX,A", 0xD4); A("mov ABS,A", 0xC5);
            A("mov ABSX,A", 0xD5); A("mov ABSY,A", 0xD6); A("mov DPXI,A", 0xC7); A("mov DPIY,A", 0xD7);
            A("mov DP,X", 0xD8); A("mov DPY,X", 0xD9); A("mov ABS,X", 0xC9);
            A("mov DP,Y", 0xCB); A("mov DPX,Y", 0xDB); A("mov ABS,Y", 0xCC);
            A("mov A,X", 0x7D); A("mov A,Y", 0xDD); A("mov X,A", 0x5D); A("mov Y,A", 0xFD); A("mov X,SP", 0x9D); A("mov SP,X", 0xBD);
            A("mov DP,DP", 0xFA); A("mov DP,IMM", 0x8F);
            // 16-bit
            A("movw YA,DP", 0xBA); A("movw DP,YA", 0xDA); A("incw DP", 0x3A); A("decw DP", 0x1A);
            A("addw YA,DP", 0x7A); A("subw YA,DP", 0x9A); A("cmpw YA,DP", 0x5A);
            // inc/dec/shifts
            A("inc A", 0xBC); A("inc X", 0x3D); A("inc Y", 0xFC); A("inc DP", 0xAB); A("inc DPX", 0xBB); A("inc ABS", 0xAC);
            A("dec A", 0x9C); A("dec X", 0x1D); A("dec Y", 0xDC); A("dec DP", 0x8B); A("dec DPX", 0x9B); A("dec ABS", 0x8C);
            A("asl A", 0x1C); A("asl DP", 0x0B); A("asl DPX", 0x1B); A("asl ABS", 0x0C);
            A("lsr A", 0x5C); A("lsr DP", 0x4B); A("lsr DPX", 0x5B); A("lsr ABS", 0x4C);
            A("rol A", 0x3C); A("rol DP", 0x2B); A("rol DPX", 0x3B); A("rol ABS", 0x2C);
            A("ror A", 0x7C); A("ror DP", 0x6B); A("ror DPX", 0x7B); A("ror ABS", 0x6C);
            A("xcn A", 0x9F); A("mul YA", 0xCF); A("div YA,X", 0x9E); A("daa A", 0xDF); A("das A", 0xBE);
            // branches / jumps
            A("bra REL", 0x2F); A("beq REL", 0xF0); A("bne REL", 0xD0); A("bcs REL", 0xB0); A("bcc REL", 0x90);
            A("bvs REL", 0x70); A("bvc REL", 0x50); A("bmi REL", 0x30); A("bpl REL", 0x10);
            A("bbs DPBIT,REL", 0x03); A("bbc DPBIT,REL", 0x13);
            A("cbne DP,REL", 0x2E); A("cbne DPX,REL", 0xDE); A("dbnz DP,REL", 0x6E); A("dbnz Y,REL", 0xFE);
            A("jmp ABS", 0x5F); A("jmp ABSXI", 0x1F); A("call ABS", 0x3F);
            A("brk", 0x0F); A("ret", 0x6F); A("reti", 0x7F);
            A("push A", 0x2D); A("push X", 0x4D); A("push Y", 0x6D); A("push PSW", 0x0D);
            A("pop A", 0xAE); A("pop X", 0xCE); A("pop Y", 0xEE); A("pop PSW", 0x8E);
            // bits
            A("set1 DPBIT", 0x02); A("clr1 DPBIT", 0x12); A("tset1 ABS", 0x0E); A("tclr1 ABS", 0x4E);
            A("and1 C,MEMBIT", 0x4A); A("and1 C,NMEMBIT", 0x6A); A("or1 C,MEMBIT", 0x0A); A("or1 C,NMEMBIT", 0x2A);
            A("eor1 C,MEMBIT", 0x8A); A("not1 MEMBIT", 0xEA); A("mov1 C,MEMBIT", 0xAA); A("mov1 MEMBIT,C", 0xCA);
            // flags / misc
            A("clrc", 0x60); A("setc", 0x80); A("notc", 0xED); A("clrv", 0xE0); A("clrp", 0x20); A("setp", 0x40);
            A("ei", 0xA0); A("di", 0xC0); A("nop", 0x00); A("sleep", 0xEF); A("stop", 0xFF);
            return d;
        }
    }
}
