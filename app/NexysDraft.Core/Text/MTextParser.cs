using System.Text;
using NexysDraft.Core.Dxf;

namespace NexysDraft.Core.MTextFmt;

/// <summary>One formatted run of an MTEXT line.</summary>
public sealed class MRun
{
    public string Text = ""; public string? Font; public bool Bold, Italic, Underline, Overline, Strike; public double HeightFactor = 1; public bool HeightFactorIsAbsolute; public double WidthFactor = 1; public int? Color; public bool Shx;
    public MRun Clone() => (MRun)MemberwiseClone();
}

public sealed class MLine { public List<MRun> Runs = new(); public int Align; public string Plain => string.Concat(Runs.Select(r => r.Text)); }

/// <summary>
/// MTEXT format-code parser: \P (paragraph), \~ (nbsp), \\ \{ \} (literals), {…} (scoped formatting), \fName|b1|i0|c129|p49; (TrueType),
/// \Fshx; , \H1.5x; \H2.5; (height), \W (width), \Q (oblique), \C (ACI colour), \c (true colour), \T (tracking), \L \l \O \o \K \k (lines),
/// \A (vertical alignment), \pxxx; (paragraph properties), \S…^…; (stacked fraction → "a/b"), \N (column break), \U+XXXX, \M+nXXXX,
/// %%d %%p %%c %%u %%o. Returns lines of runs; <see cref="Plain"/> is the text without codes.
/// </summary>
public static class MTextParser
{
    public static List<MLine> Parse(string raw)
    {
        var lines = new List<MLine>(); var cur = new MLine(); lines.Add(cur);
        var stack = new Stack<MRun>(); var st = new MRun(); var sb = new StringBuilder();
        void Flush() { if (sb.Length > 0) { var r = st.Clone(); r.Text = sb.ToString(); cur.Runs.Add(r); sb.Clear(); } }
        string s = raw ?? "";
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{') { Flush(); stack.Push(st.Clone()); continue; }
            if (c == '}') { Flush(); if (stack.Count > 0) st = stack.Pop(); continue; }
            if (c == '%' && i + 2 < s.Length && s[i + 1] == '%')
            {
                char k = char.ToLowerInvariant(s[i + 2]);
                if (k == 'd') { sb.Append('°'); i += 2; continue; }
                if (k == 'p') { sb.Append('±'); i += 2; continue; }
                if (k == 'c') { sb.Append('Ø'); i += 2; continue; }
                if (k == 'u') { Flush(); st.Underline = !st.Underline; i += 2; continue; }
                if (k == 'o') { Flush(); st.Overline = !st.Overline; i += 2; continue; }
                if (k == '%') { sb.Append('%'); i += 2; continue; }
                if (char.IsDigit(k) && i + 4 < s.Length && char.IsDigit(s[i + 3]) && char.IsDigit(s[i + 4])) { sb.Append((char)int.Parse(s.Substring(i + 2, 3))); i += 4; continue; }
            }
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[i + 1];
            switch (n)
            {
                case 'P': case 'N': Flush(); cur = new MLine { Align = cur.Align }; lines.Add(cur); i++; continue;
                case '~': sb.Append('\u00A0'); i++; continue;
                case '\\': sb.Append('\\'); i++; continue;
                case '{': sb.Append('{'); i++; continue;
                case '}': sb.Append('}'); i++; continue;
                case 'L': Flush(); st.Underline = true; i++; continue;
                case 'l': Flush(); st.Underline = false; i++; continue;
                case 'O': Flush(); st.Overline = true; i++; continue;
                case 'o': Flush(); st.Overline = false; i++; continue;
                case 'K': Flush(); st.Strike = true; i++; continue;
                case 'k': Flush(); st.Strike = false; i++; continue;
                case 'U': case 'u':
                    if (i + 6 < s.Length + 0 && s[i + 2] == '+') { string u = DxfCodec.Unescape(s.Substring(i, Math.Min(7, s.Length - i))); if (u.Length < 7) { sb.Append(u); i += 6; continue; } }
                    break;
                case 'M': case 'm':
                    if (i + 7 < s.Length + 0 && s[i + 2] == '+') { string u = DxfCodec.Unescape(s.Substring(i, Math.Min(8, s.Length - i))); if (u.Length < 8) { sb.Append(u); i += 7; continue; } }
                    break;
            }
            // codes with a ';'-terminated argument
            int end = s.IndexOf(';', i + 2);
            if (end < 0) { sb.Append(c); continue; }
            string arg = s.Substring(i + 2, end - i - 2);
            switch (n)
            {
                case 'f': case 'F':
                    {
                        Flush(); var parts = arg.Split('|'); st.Font = parts[0]; st.Shx = n == 'F';
                        foreach (var p in parts.Skip(1)) { if (p.StartsWith("b")) st.Bold = p.EndsWith("1"); else if (p.StartsWith("i")) st.Italic = p.EndsWith("1"); }
                        break;
                    }
                case 'H': { Flush(); double v = DxfCodec.ParseDouble(arg.TrimEnd('x', 'X')); if (arg.EndsWith("x", StringComparison.OrdinalIgnoreCase)) st.HeightFactor *= v; else st.HeightFactor = v; st.HeightFactor = st.HeightFactor <= 0 ? 1 : st.HeightFactor; st.HeightFactorIsAbsolute = !arg.EndsWith("x", StringComparison.OrdinalIgnoreCase); break; }
                case 'W': { Flush(); double v = DxfCodec.ParseDouble(arg.TrimEnd('x', 'X')); st.WidthFactor = v > 0 ? v : 1; break; }
                case 'C': { Flush(); st.Color = DxfCodec.ParseInt(arg); break; }
                case 'c': case 'T': case 'Q': case 'A': case 'p': break;
                case 'S':
                    {
                        // stacked: num^den, num/den, num#den → "num/den"
                        int k = arg.IndexOfAny(new[] { '^', '/', '#' });
                        if (k >= 0) sb.Append(arg[..k].TrimEnd()).Append('/').Append(arg[(k + 1)..].TrimStart()); else sb.Append(arg);
                        break;
                    }
                default: sb.Append(c); continue;   // unknown code: keep the backslash, continue normally
            }
            i = end;
        }
        Flush();
        return lines;
    }

    public static string Plain(string raw) => string.Join("\n", Parse(raw).Select(l => l.Plain));

    /// <summary>Escape literal text for an MTEXT value (user edits): backslashes, braces, newlines → \P.</summary>
    public static string EscapePlain(string plain) => plain.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r\n", "\n").Replace("\n", "\\P");
}

public static class MRunExt { }
