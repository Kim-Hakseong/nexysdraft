using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Core.Dxf;

/// <summary>One text style whose font reference is rewritten on save. <see cref="NewFile"/> / <see cref="NewFamily"/> are what is written
/// (group 3 / ACAD xdata 1000) in <see cref="Mode"/>; <see cref="NewFamilyKo"/> is the Korean face name for messages.</summary>
public sealed record FontFix(string Style, string OldFont, string OldBigFont, string NewFamily, string NewFamilyKo, int Flags, int KoreanTexts, string NewFile = "", FontCompatMode Mode = FontCompat.DefaultMode);

/// <summary>
/// How a rewritten style refers to its font (draft4 H0 font matrix, work/font_matrix — nanoCAD 5 shows u04's Korean as "???" with B):
/// A group 3 empty + xdata family "굴림" (written as \U+ escapes) · B group 3 empty + xdata "Gulim" (draft3 behaviour) ·
/// C group 3 "gulim.ttc" + xdata "Gulim" · D group 3 "malgun.ttf" + xdata "Malgun Gothic" · E group 3 "malgun.ttf", no xdata ·
/// F u01's 'PDF Arial' record (group 3 empty, xdata "Arial", 1071 = 336) · G = B with 1071 = 34 (ANSI charset; diagnostic).
/// </summary>
public enum FontCompatMode { A, B, C, D, E, F, G }

/// <summary>
/// nanoCAD font compatibility (draft3 G1, draft4 H0). nanoCAD 5 shows Korean as "?" when a style points at a font file it cannot find
/// ("Cannot find: GulimChe.ttf" — Windows ships gulim.ttc, not GulimChe.ttf) or at an .shx without a Korean big font.
/// Styles that Korean text actually uses (TEXT / ATTRIB values, MTEXT runs without an inline \f font) and whose font is missing on this PC
/// are rewritten, and so is every style whose font strings (group 3, 4, xdata family) are not ASCII: all font strings written are ASCII
/// (Korean family → English face name and file, <see cref="MapFamily"/>). Styles with fonts present that only ASCII text uses are left
/// untouched (u01: 0 changes). The form is chosen by one constant, <see cref="DefaultMode"/> (provisional C until the nanoCAD matrix check).
/// </summary>
public static class FontCompat
{
    /// <summary>The rewrite form. Change this one line after the nanoCAD 5 font matrix check (docs/font_matrix.md).</summary>
    public const FontCompatMode DefaultMode = FontCompatMode.C;
    /// <summary>Form used by <see cref="Plan"/> (tests and the font matrix set it; the app keeps the default).</summary>
    public static FontCompatMode Mode { get; set; } = DefaultMode;

    /// <summary>Korean charset (129, HANGUL) in the high byte, variable pitch / swiss family in the low byte — the value u04's Gulim style carries.</summary>
    public const int KoreanFlags = 33074;
    /// <summary>u01 'PDF Arial' 1071 value.</summary>
    public const int ArialFlags = 336;

    public static IReadOnlyList<string> FontDirs(IEnumerable<string>? extra = null)
    {
        var dirs = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"), Path.Combine(AppContext.BaseDirectory, "fonts") };
        if (extra != null) dirs.AddRange(extra);
        return dirs.Where(Directory.Exists).ToList();
    }

    static HashSet<string>? _files;
    static HashSet<string> Files(IEnumerable<string> dirs)
    {
        if (_files != null) return _files;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs) { try { foreach (var f in Directory.EnumerateFiles(d)) set.Add(Path.GetFileName(f)); } catch { } }
        return _files = set;
    }

    /// <summary>Is a font reference (group 3 or 4) present on this PC? Names without extension are .shx (AutoCAD rule).</summary>
    public static bool Present(string font, IEnumerable<string>? dirs = null)
    {
        if (string.IsNullOrWhiteSpace(font)) return true;
        string f = Path.GetFileName(font.Trim());
        if (!Path.HasExtension(f)) f += ".shx";
        return Files(dirs ?? FontDirs()).Contains(f);
    }

    /// <summary>Is the style renderable with Korean on this PC: family form (empty file + xdata family), or an existing TrueType file,
    /// or an existing .shx with an existing big font.</summary>
    public static bool StyleOk(TextStyle s, IEnumerable<string>? dirs = null)
    {
        string font = s.Font?.Trim() ?? "";
        if (font.Length == 0) return s.TrueTypeFamily != null;
        bool shx = !Path.HasExtension(font) || font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase);
        if (!shx) return Present(font, dirs);
        return Present(font, dirs) && !string.IsNullOrWhiteSpace(s.BigFont) && Present(s.BigFont, dirs);
    }

    static bool NonAscii(string? s) { if (s == null) return false; foreach (var c in s) if (c >= 0x80) return true; return false; }
    /// <summary>Any of the style's font strings (group 3, 4, xdata family) is not ASCII.</summary>
    public static bool NonAsciiFont(TextStyle s) => NonAscii(s.Font) || NonAscii(s.BigFont) || NonAscii(s.TrueTypeFamily);

    /// <summary>Style name → number of Korean (non-ASCII) texts that render through the style font.</summary>
    public static Dictionary<string, int> KoreanUse(Document doc)
    {
        var use = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Add(string style) => use[style] = use.GetValueOrDefault(style) + 1;
        IEnumerable<Entity> All() { foreach (var e in doc.Entities) yield return e; foreach (var b in doc.Blocks.Values) foreach (var e in b.Entities) yield return e; }
        foreach (var e in All())
        {
            switch (e)
            {
                case Text t when NonAscii(t.Value): Add(t.Style); break;
                case MText m when NonAscii(m.Value):
                    if (MTextParser.Parse(m.Value).Any(l => l.Runs.Any(r => r.Font == null && NonAscii(r.Text)))) Add(m.Style);
                    break;
                case Insert ins: foreach (var a in ins.Attribs) if (NonAscii(a.Text.Value)) Add(a.Text.Style); break;
            }
        }
        return use;
    }

    /// <summary>Korean family → (English face name, font file, Korean name): 굴림/굴림체 → Gulim·gulim.ttc, 돋움/돋움체 → Dotum·gulim.ttc,
    /// 바탕/바탕체 → Batang·batang.ttc, 맑은 고딕 → Malgun Gothic·malgun.ttf, anything else → Gulim. The match looks at the old font file,
    /// the xdata family and the style name. When the file is not in the font folders, Malgun Gothic (every Windows 8+) is used instead.</summary>
    public static (string family, string file, string ko) MapFamily(string oldFont, string styleName, string? oldFamily = null, IEnumerable<string>? dirs = null)
    {
        string n = (oldFont + " " + oldFamily + " " + styleName).ToLowerInvariant().Replace(" ", "");
        (string, string, string) m =
            n.Contains("malgun") || n.Contains("맑은고딕") ? ("Malgun Gothic", "malgun.ttf", "맑은 고딕") :
            n.Contains("batang") || n.Contains("바탕") ? ("Batang", "batang.ttc", "바탕") :
            n.Contains("dotum") || n.Contains("돋움") ? ("Dotum", "gulim.ttc", "돋움") :
            ("Gulim", "gulim.ttc", "굴림");
        if (!Present(m.Item2, dirs) && Present("malgun.ttf", dirs)) m = ("Malgun Gothic", "malgun.ttf", "맑은 고딕");
        return m;
    }

    public static List<FontFix> Plan(Document doc, IEnumerable<string>? dirs = null) => Plan(doc, Mode, dirs);
    public static List<FontFix> Plan(Document doc, FontCompatMode mode, IEnumerable<string>? dirs = null)
    {
        var use = KoreanUse(doc); var fixes = new List<FontFix>();
        foreach (var s in doc.Styles)
        {
            int n = use.GetValueOrDefault(s.Name);
            bool missing = n > 0 && !StyleOk(s, dirs), nonAscii = NonAsciiFont(s);
            if (!missing && !nonAscii) continue;
            var (fam, file, ko) = MapFamily(s.Font, s.Name, s.TrueTypeFamily, dirs);
            fixes.Add(new FontFix(s.Name, s.Font, s.BigFont, fam, ko, KoreanFlags, n, file, mode));
        }
        return fixes;
    }

    /// <summary>The style record as written after a fix (raw groups dropped), in the fix's form.</summary>
    public static TextStyle Applied(TextStyle s, FontFix f)
    {
        var t = new TextStyle { Name = s.Name, Font = "", BigFont = "", Height = s.Height, WidthFactor = s.WidthFactor, Oblique = s.Oblique, Flags = s.Flags, GenFlags = s.GenFlags, Handle = s.Handle, Raw = null, TrueTypeFamily = f.NewFamily, FontFlags = f.Flags };
        switch (f.Mode)
        {
            case FontCompatMode.A: t.TrueTypeFamily = f.NewFamilyKo; break;
            case FontCompatMode.B: break;
            case FontCompatMode.C: t.Font = f.NewFile; break;
            case FontCompatMode.D: t.Font = "malgun.ttf"; t.TrueTypeFamily = "Malgun Gothic"; break;
            case FontCompatMode.E: t.Font = "malgun.ttf"; t.TrueTypeFamily = null; t.FontFlags = 0; break;
            case FontCompatMode.F: t.TrueTypeFamily = "Arial"; t.FontFlags = ArialFlags; break;
            case FontCompatMode.G: t.FontFlags = 34; break;
        }
        return t;
    }

    /// <summary>The style for a matrix row: <paramref name="name"/> rewritten in <paramref name="mode"/> as if it were a Gulim style.</summary>
    public static TextStyle MatrixStyle(string name, FontCompatMode mode, string handle)
    {
        var src = new TextStyle { Name = name, Font = "GulimChe.ttf", Handle = handle };
        return Applied(src, new FontFix(name, src.Font, "", "Gulim", "굴림", KoreanFlags, 1, "gulim.ttc", mode));
    }
}
