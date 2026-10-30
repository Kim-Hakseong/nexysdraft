using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Core.Dxf;

/// <summary>One text style whose font reference is rewritten on save.</summary>
public sealed record FontFix(string Style, string OldFont, string OldBigFont, string NewFamily, string NewFamilyKo, int Flags, int KoreanTexts);

/// <summary>
/// nanoCAD font compatibility (draft3 G1). nanoCAD 5 shows Korean as "?" when a style points at a font file it cannot find
/// ("Cannot find: GulimChe.ttf" — Windows ships gulim.ttc, not GulimChe.ttf) or at an .shx without a Korean big font
/// ("AllInOneUni/AllInOneBig.shx substituted as txt.shx"). The form that nanoCAD 5 renders correctly is the one u01's 'PDF Arial' style uses:
/// font file (group 3) and big font (group 4) empty, the Windows family in ACAD xdata (1000 = family, 1071 = pitch/family/charset).
/// Only styles that Korean text actually uses (TEXT / ATTRIB values, MTEXT runs without an inline \f font) and whose font is missing on this
/// PC are rewritten — styles with fonts present, and styles no Korean text uses, are left untouched (u01: 0 changes).
/// </summary>
public static class FontCompat
{
    /// <summary>Korean charset (129, HANGUL) in the high byte, variable pitch / swiss family in the low byte — the value u04's Gulim style carries.</summary>
    public const int KoreanFlags = 33074;

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

    static bool NonAscii(string s) { foreach (var c in s) if (c >= 0x80) return true; return false; }

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

    /// <summary>Family to use for a missing font: GulimChe/Gulim → 굴림, BatangChe/Batang → 바탕, DotumChe/Dotum → 돋움, anything else → 굴림.
    /// The xdata family is written with the Windows English face name (u01's 'PDF Arial' writes "Arial"), which GDI resolves to the same face.</summary>
    public static (string family, string ko) MapFamily(string oldFont, string styleName)
    {
        string n = (oldFont + " " + styleName).ToLowerInvariant();
        if (n.Contains("batang") || n.Contains("바탕")) return ("Batang", "바탕");
        if (n.Contains("dotum") || n.Contains("돋움")) return ("Dotum", "돋움");
        if (n.Contains("gungsuh") || n.Contains("궁서")) return ("Gungsuh", "궁서");
        return ("Gulim", "굴림");
    }

    public static List<FontFix> Plan(Document doc, IEnumerable<string>? dirs = null)
    {
        var use = KoreanUse(doc); var fixes = new List<FontFix>();
        foreach (var s in doc.Styles)
        {
            if (!use.TryGetValue(s.Name, out int n) || n == 0) continue;
            if (StyleOk(s, dirs)) continue;
            var (fam, ko) = MapFamily(s.Font, s.Name);
            fixes.Add(new FontFix(s.Name, s.Font, s.BigFont, fam, ko, KoreanFlags, n));
        }
        return fixes;
    }

    /// <summary>The style record as written after a fix (raw groups dropped, xdata family form).</summary>
    public static TextStyle Applied(TextStyle s, FontFix f) => new() { Name = s.Name, Font = "", BigFont = "", Height = s.Height, WidthFactor = s.WidthFactor, Oblique = s.Oblique, Flags = s.Flags, GenFlags = s.GenFlags, Handle = s.Handle, Raw = null, TrueTypeFamily = f.NewFamily, FontFlags = f.Flags };
}
