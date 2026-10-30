using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace NexysDraft.Core.Pdf;

/// <summary>Minimal PDF 1.4 writer: pages with Flate-compressed content streams, one embedded TrueType subset (Type0 / CIDFontType2,
/// Identity-H, ToUnicode CMap so text extraction and search work), vector paths. No dependencies.</summary>
public sealed class PdfWriter
{
    readonly List<byte[]> _objects = new();   // index + 1 = object number
    readonly List<(double w, double h, int contentObj)> _pages = new();
    readonly TrueTypeFont _font; readonly HashSet<int> _usedGids = new(); readonly Dictionary<int, int> _gidToUnicode = new();
    public string FontName { get; }
    public PdfWriter(TrueTypeFont font) { _font = font; FontName = "NX+" + new string(font.FamilyName.Where(char.IsLetterOrDigit).ToArray()); }

    public static string N(double v) => double.IsFinite(v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "0";

    /// <summary>Content stream of one page (points, origin bottom-left).</summary>
    public sealed class Page
    {
        internal readonly StringBuilder S = new(); readonly PdfWriter _w; public double Width, Height;
        string _stroke = "", _fill = "", _lw = ""; bool _text;
        internal Page(PdfWriter w, double width, double height) { _w = w; Width = width; Height = height; S.Append("0 J 0 j\n"); }
        public void Stroke(int rgb) { string c = $"{N((rgb >> 16 & 255) / 255.0)} {N((rgb >> 8 & 255) / 255.0)} {N((rgb & 255) / 255.0)} RG\n"; if (c != _stroke) { S.Append(c); _stroke = c; } }
        public void Fill(int rgb) { string c = $"{N((rgb >> 16 & 255) / 255.0)} {N((rgb >> 8 & 255) / 255.0)} {N((rgb & 255) / 255.0)} rg\n"; if (c != _fill) { S.Append(c); _fill = c; } }
        public void LineWidth(double pt) { string c = N(pt) + " w\n"; if (c != _lw) { S.Append(c); _lw = c; } }
        public void Line(double x0, double y0, double x1, double y1) { S.Append(N(x0)).Append(' ').Append(N(y0)).Append(" m ").Append(N(x1)).Append(' ').Append(N(y1)).Append(" l S\n"); }
        /// <summary>Polyline (xy pairs) stroked; closed when asked.</summary>
        public void Polyline(ReadOnlySpan<double> xy, bool closed)
        {
            if (xy.Length < 4) return;
            S.Append(N(xy[0])).Append(' ').Append(N(xy[1])).Append(" m");
            for (int i = 2; i + 1 < xy.Length; i += 2) S.Append(' ').Append(N(xy[i])).Append(' ').Append(N(xy[i + 1])).Append(" l");
            S.Append(closed ? " h S\n" : " S\n");
        }
        /// <summary>Filled polygon(s), even-odd (holes).</summary>
        public void FillPolygons(IEnumerable<double[]> loops)
        {
            bool any = false;
            foreach (var xy in loops)
            {
                if (xy.Length < 6) continue; any = true;
                S.Append(N(xy[0])).Append(' ').Append(N(xy[1])).Append(" m");
                for (int i = 2; i + 1 < xy.Length; i += 2) S.Append(' ').Append(N(xy[i])).Append(' ').Append(N(xy[i + 1])).Append(" l");
                S.Append(" h\n");
            }
            if (any) S.Append("f*\n");
        }
        /// <summary>Text with its baseline origin at (x,y), rotated by deg, font size in points, horizontal scaling (width factor).</summary>
        public void Text(string text, double x, double y, double size, double deg, double widthFactor = 1)
        {
            if (string.IsNullOrEmpty(text) || size <= 0) return;
            double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            var hex = new StringBuilder();
            foreach (var rune in text.EnumerateRunes())
            {
                int gid = _w._font.Gid(rune.Value); if (gid == 0 && rune.Value != ' ') gid = _w._font.Gid('?');
                _w._usedGids.Add(gid); if (!_w._gidToUnicode.ContainsKey(gid)) _w._gidToUnicode[gid] = rune.Value;
                hex.Append(gid.ToString("X4"));
            }
            S.Append("BT /F1 ").Append(N(size)).Append(" Tf ");
            if (Math.Abs(widthFactor - 1) > 1e-6) S.Append(N(widthFactor * 100)).Append(" Tz ");
            S.Append(N(c)).Append(' ').Append(N(s)).Append(' ').Append(N(-s)).Append(' ').Append(N(c)).Append(' ').Append(N(x)).Append(' ').Append(N(y)).Append(" Tm <").Append(hex).Append("> Tj");
            if (Math.Abs(widthFactor - 1) > 1e-6) S.Append(" 100 Tz");
            S.Append(" ET\n");
        }
        /// <summary>Advance of a string in text-space units (em = 1) for alignment.</summary>
        public double Measure(string text) { double w = 0; foreach (var rune in text.EnumerateRunes()) { int gid = _w._font.Gid(rune.Value); if (gid == 0 && rune.Value != ' ') gid = _w._font.Gid('?'); w += _w._font.WidthEm(gid); } return w; }
        public double CapHeightEm => _w._font.CapHeight / (double)_w._font.UnitsPerEm;
        public double AscentEm => _w._font.Ascender / (double)_w._font.UnitsPerEm;
        public double DescentEm => _w._font.Descender / (double)_w._font.UnitsPerEm;
    }

    public Page AddPage(double width, double height) { var p = new Page(this, width, height); _pending.Add(p); return p; }
    readonly List<Page> _pending = new();

    int Add(byte[] obj) { _objects.Add(obj); return _objects.Count; }
    int Add(string obj) => Add(Encoding.Latin1.GetBytes(obj));
    static byte[] Flate(byte[] data) { var ms = new MemoryStream(); using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data); return ms.ToArray(); }
    byte[] Stream(string dict, byte[] data) { var z = Flate(data); var ms = new MemoryStream(); ms.Write(Encoding.Latin1.GetBytes($"<< {dict} /Length {z.Length} /Filter /FlateDecode >>\nstream\n")); ms.Write(z); ms.Write(Encoding.Latin1.GetBytes("\nendstream")); return ms.ToArray(); }

    public byte[] Build(string title = "")
    {
        _objects.Clear();
        // reserve: 1 catalog, 2 pages, 3 font (Type0), 4 CIDFont, 5 descriptor, 6 fontfile, 7 tounicode
        for (int i = 0; i < 7; i++) _objects.Add(Array.Empty<byte>());
        var pageObjs = new List<int>();
        foreach (var p in _pending)
        {
            int content = Add(Stream("", Encoding.Latin1.GetBytes(p.S.ToString())));
            pageObjs.Add(Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(p.Width)} {N(p.Height)}] /Resources << /Font << /F1 3 0 R >> >> /Contents {content} 0 R >>"));
        }
        _objects[0] = Encoding.Latin1.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");
        _objects[1] = Encoding.Latin1.GetBytes($"<< /Type /Pages /Count {pageObjs.Count} /Kids [{string.Join(" ", pageObjs.Select(o => o + " 0 R"))}] >>");
        // font
        var gids = _usedGids.OrderBy(g => g).ToList();
        var subset = _font.Subset(gids);
        _objects[5] = Stream($"/Length1 {subset.Length}", subset);
        var bb = _font.BBox; double k = 1000.0 / _font.UnitsPerEm;
        _objects[4] = Encoding.Latin1.GetBytes($"<< /Type /FontDescriptor /FontName /{FontName} /Flags 4 /FontBBox [{N(bb.x0 * k)} {N(bb.y0 * k)} {N(bb.x1 * k)} {N(bb.y1 * k)}] /ItalicAngle 0 /Ascent {N(_font.Ascender * k)} /Descent {N(_font.Descender * k)} /CapHeight {N(_font.CapHeight * k)} /StemV 80 /FontFile2 6 0 R >>");
        var w = new StringBuilder(); foreach (var g in gids) w.Append(g).Append(" [").Append(_font.Width1000(g)).Append("] ");
        _objects[3] = Encoding.Latin1.GetBytes($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{FontName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 5 0 R /DW 1000 /W [{w}] /CIDToGIDMap /Identity >>");
        _objects[2] = Encoding.Latin1.GetBytes($"<< /Type /Font /Subtype /Type0 /BaseFont /{FontName} /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 7 0 R >>");
        var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var entries = _gidToUnicode.Where(kv => kv.Key != 0).OrderBy(kv => kv.Key).ToList();
        for (int i = 0; i < entries.Count; i += 100)
        {
            var chunk = entries.Skip(i).Take(100).ToList(); cmap.Append(chunk.Count).Append(" beginbfchar\n");
            foreach (var kv in chunk) { cmap.Append('<').Append(kv.Key.ToString("X4")).Append("> <"); foreach (var ch in char.ConvertFromUtf32(kv.Value)) cmap.Append(((int)ch).ToString("X4")); cmap.Append(">\n"); }
            cmap.Append("endbfchar\n");
        }
        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        _objects[6] = Stream("", Encoding.Latin1.GetBytes(cmap.ToString()));
        int info = Add($"<< /Producer (NexysDraft) /Title ({Escape(title)}) >>");
        // serialize
        var ms = new MemoryStream(); void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.4\n%âãÏÓ\n"); var offs = new List<long>();
        for (int i = 0; i < _objects.Count; i++) { offs.Add(ms.Length); W($"{i + 1} 0 obj\n"); ms.Write(_objects[i]); W("\nendobj\n"); }
        long xref = ms.Length; W($"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n"); foreach (var o in offs) W(o.ToString("D10") + " 00000 n \n");
        W($"trailer\n<< /Size {_objects.Count + 1} /Root 1 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
    static string Escape(string s) { var sb = new StringBuilder(); foreach (var c in s) { if (c == '(' || c == ')' || c == '\\') sb.Append('\\').Append(c); else if (c < 128) sb.Append(c); else sb.Append('?'); } return sb.ToString(); }
}
