namespace NexysDraft.Core.MTextFmt;

/// <summary>AutoCAD control codes in TEXT / ATTRIB values: %%d degree, %%p plus-minus, %%c diameter, %%u / %%o underline/overline toggles (dropped), %%nnn char code, %%% percent.</summary>
public static class TextCodes
{
    public static string Plain(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf("%%", StringComparison.Ordinal) < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length && s[i + 1] == '%')
            {
                char k = char.ToLowerInvariant(s[i + 2]);
                if (k == 'd') { sb.Append('\u00B0'); i += 2; continue; }
                if (k == 'p') { sb.Append('\u00B1'); i += 2; continue; }
                if (k == 'c') { sb.Append('\u00D8'); i += 2; continue; }
                if (k == 'u' || k == 'o') { i += 2; continue; }
                if (k == '%') { sb.Append('%'); i += 2; continue; }
                if (char.IsDigit(k) && i + 4 < s.Length && char.IsDigit(s[i + 3]) && char.IsDigit(s[i + 4])) { sb.Append((char)int.Parse(s.Substring(i + 2, 3))); i += 4; continue; }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
