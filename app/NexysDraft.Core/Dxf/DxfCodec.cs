using System.Globalization;
using System.Text;

namespace NexysDraft.Core.Dxf;

/// <summary>
/// Byte ↔ string rules of ASCII DXF: strings are bytes in $DWGCODEPAGE up to R2004, UTF-8 from R2007 — and converters (LibreDWG)
/// emit codepage bytes even in AC1024 files, so each value is decoded on its own: ASCII as is, strict UTF-8 when it validates,
/// else the codepage. \U+XXXX and \M+nXXXX escapes are resolved by the text layer (<see cref="Unescape"/>).
/// </summary>
public static class DxfCodec
{
    static DxfCodec() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Encoding CodePageEncoding(string dwgCodePage)
    {
        string cp = (dwgCodePage ?? "").Trim().ToUpperInvariant();
        try
        {
            if (cp.StartsWith("ANSI_")) return Encoding.GetEncoding(int.Parse(cp[5..]));
            return cp switch
            {
                "" or "ASCII" or "ANSI" => Encoding.GetEncoding(1252),
                "UTF-8" or "UTF8" => new UTF8Encoding(false),
                "DOS437" => Encoding.GetEncoding(437), "DOS850" => Encoding.GetEncoding(850), "DOS949" => Encoding.GetEncoding(949), "DOS932" => Encoding.GetEncoding(932), "DOS936" => Encoding.GetEncoding(936), "DOS950" => Encoding.GetEncoding(950),
                "MAC-ROMAN" => Encoding.GetEncoding(10000), "ISO8859-1" => Encoding.GetEncoding(28591), "KSC5601" => Encoding.GetEncoding(949), "JOHAB" => Encoding.GetEncoding(1361), "GB2312" => Encoding.GetEncoding(936), "BIG5" => Encoding.GetEncoding(950), "SHIFTJIS" or "SJIS" => Encoding.GetEncoding(932),
                _ => Encoding.GetEncoding(1252),
            };
        }
        catch { return Encoding.GetEncoding(1252); }
    }

    /// <summary>Decode one value: ASCII → as is; valid UTF-8 → UTF-8; else the codepage. Reports which path was taken.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, Encoding codepage, out bool utf8)
    {
        utf8 = false;
        bool ascii = true; foreach (var b in bytes) if (b >= 0x80) { ascii = false; break; }
        if (ascii) return Encoding.ASCII.GetString(bytes);
        try { string s = StrictUtf8.GetString(bytes); utf8 = true; return s; } catch (DecoderFallbackException) { }
        return codepage.GetString(bytes);
    }

    /// <summary>Encode for writing: the file is pure ASCII — every non-ASCII character becomes \U+XXXX (astral characters as two surrogate
    /// escapes, AutoCAD's convention). nanoCAD shows "??" for raw code-page bytes in AC1015/AC1018/AC1024 DXF (user check, draft2) while
    /// every reader (AutoCAD, ODA, LibreDWG, our own) resolves \U+. The target encoding no longer matters for the bytes.</summary>
    public static byte[] Encode(string s, Encoding target)
    {
        bool ascii = true; foreach (var c in s) if (c >= 0x80) { ascii = false; break; }
        if (ascii) return Encoding.ASCII.GetBytes(s);
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s) { if (c < 0x80) sb.Append(c); else sb.Append("\\U+").Append(((int)c).ToString("X4")); }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Resolve \U+XXXX (unicode) and \M+nXXXX (MBCS: n = 1 cp932, 2 cp950, 3 cp949, 4 cp936, 5 cp1361) escapes.</summary>
    public static string Unescape(string s)
    {
        if (s.IndexOf("\\U+", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf("\\M+", StringComparison.OrdinalIgnoreCase) < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 2 < s.Length && (s[i + 1] == 'U' || s[i + 1] == 'u') && s[i + 2] == '+' && i + 6 < s.Length + 0 && TryHex(s, i + 3, 4, out int cp))
            { sb.Append((char)cp); i += 6; continue; }
            if (s[i] == '\\' && i + 2 < s.Length && (s[i + 1] == 'M' || s[i + 1] == 'm') && s[i + 2] == '+' && i + 7 < s.Length + 0 && char.IsDigit(s[i + 3]) && TryHex(s, i + 4, 4, out int mb))
            {
                int page = s[i + 3] switch { '1' => 932, '2' => 950, '3' => 949, '4' => 936, '5' => 1361, _ => 0 };
                if (page != 0) { try { sb.Append(Encoding.GetEncoding(page).GetString(new[] { (byte)(mb >> 8), (byte)(mb & 0xFF) })); i += 7; continue; } catch { } }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
    static bool TryHex(string s, int at, int n, out int v) { v = 0; if (at + n > s.Length) return false; return int.TryParse(s.AsSpan(at, n), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v); }

    public static string FormatDouble(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "0.0";
        string s = d.ToString("R", CultureInfo.InvariantCulture);
        if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
        return s;
    }
    public static double ParseDouble(string s) => double.TryParse(s.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    public static int ParseInt(string s) => int.TryParse(s.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : (int)ParseDouble(s);
    public static long ParseHandle(string s) => long.TryParse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : 0;
}
