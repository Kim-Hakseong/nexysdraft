using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Core.Pdf;

/// <summary>
/// Document → PDF: one page per detected sheet (plus a "rest" page for content outside every sheet, and the whole drawing when no sheets
/// are detected), A3 landscape fitted. Vector geometry (lines, arcs, circles, ellipses, polylines, solids, hatch boundaries / solid fills,
/// dimension and block contents) and real text (TEXT / MTEXT lines through the embedded Malgun Gothic subset) so the PDF is searchable.
/// Print colours: ACI mapped onto white (7 → black, light colours darkened). Line width from lineweight (mm/100) with a 0.2 pt floor.
/// </summary>
public static class DrawingPdf
{
    public sealed record Result(string Path, int Pages, List<Sheet> Sheets, bool SheetsDetected, double Ms, int Texts, string FontPath);
    public const double A3W = 1190.55, A3H = 841.89, Margin = 18;

    public static Result Export(Document doc, string path, string? fontPath = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        fontPath ??= TrueTypeFont.FindSystemFont() ?? throw new FileNotFoundException("no TrueType font (malgun.ttf) found");
        var font = TrueTypeFont.Load(fontPath); var pdf = new PdfWriter(font);
        var sheets = SheetDetector.Detect(doc, out bool detected);
        var pageSheets = new List<Sheet>(sheets);
        var ents = doc.Entities.Where(e => !e.PaperSpace && e is not Unknown && Visible(doc, e)).ToList();
        if (detected)
        {
            // content outside every sheet → a rest page
            var rest = new List<Entity>();
            foreach (var e in ents) { var b = Bounds.Of(e, doc, 0); if (b == null) continue; double cx = (b.Value.x0 + b.Value.x1) / 2, cy = (b.Value.y0 + b.Value.y1) / 2; if (!sheets.Any(s => s.Contains(cx, cy))) rest.Add(e); }
            if (rest.Count > 0) { double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue; foreach (var e in rest) { var b = Bounds.Of(e, doc, 0)!.Value; x0 = Math.Min(x0, b.x0); y0 = Math.Min(y0, b.y0); x1 = Math.Max(x1, b.x1); y1 = Math.Max(y1, b.y1); } pageSheets.Add(new Sheet(x0, y0, x1, y1, "rest")); }
        }
        int texts = 0;
        foreach (var sheet in pageSheets)
        {
            var page = pdf.AddPage(A3W, A3H);
            double w = Math.Max(1e-9, sheet.W), h = Math.Max(1e-9, sheet.H); double scale = Math.Min((A3W - 2 * Margin) / w, (A3H - 2 * Margin) / h);
            double ox = (A3W - w * scale) / 2 - sheet.X0 * scale, oy = (A3H - h * scale) / 2 - sheet.Y0 * scale;
            var ctx = new Ctx(doc, page, scale, ox, oy, sheet);
            foreach (var e in ents)
            {
                var b = Bounds.Of(e, doc, 0); if (b == null) continue;
                if (b.Value.x1 < sheet.X0 || b.Value.x0 > sheet.X1 || b.Value.y1 < sheet.Y0 || b.Value.y0 > sheet.Y1) continue;
                texts += ctx.Draw(e, null, Xform.Identity, 0);
            }
        }
        var bytes = pdf.Build(Path.GetFileNameWithoutExtension(doc.Path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
        return new Result(path, pageSheets.Count, pageSheets, detected, sw.Elapsed.TotalMilliseconds, texts, fontPath);
    }

    /// <summary>One single-page PDF per detected sheet next to the main file: name__001.pdf … (the main file keeps all pages).</summary>
    public static int ExportPerSheet(Document doc, string mainPath, Result res)
    {
        int n = 0; string dir = Path.GetDirectoryName(Path.GetFullPath(mainPath))!, stem = Path.GetFileNameWithoutExtension(mainPath);
        foreach (var s in res.Sheets) { if (s.Label == "rest") continue; ExportSheet(doc, Path.Combine(dir, stem + "__" + s.Label + ".pdf"), s, res.FontPath); n++; }
        return n;
    }
    static void ExportSheet(Document doc, string path, Sheet sheet, string fontPath)
    {
        var font = TrueTypeFont.Load(fontPath); var pdf = new PdfWriter(font);
        var ents = doc.Entities.Where(e => !e.PaperSpace && e is not Unknown && Visible(doc, e)).ToList();
        var page = pdf.AddPage(A3W, A3H); double w = Math.Max(1e-9, sheet.W), h = Math.Max(1e-9, sheet.H); double scale = Math.Min((A3W - 2 * Margin) / w, (A3H - 2 * Margin) / h);
        var ctx = new Ctx(doc, page, scale, (A3W - w * scale) / 2 - sheet.X0 * scale, (A3H - h * scale) / 2 - sheet.Y0 * scale, sheet);
        foreach (var e in ents) { var b = Bounds.Of(e, doc, 0); if (b == null || b.Value.x1 < sheet.X0 || b.Value.x0 > sheet.X1 || b.Value.y1 < sheet.Y0 || b.Value.y0 > sheet.Y1) continue; ctx.Draw(e, null, Xform.Identity, 0); }
        File.WriteAllBytes(path, pdf.Build(Path.GetFileNameWithoutExtension(path)));
    }

    static bool Visible(Document doc, Entity e) { if (e.Invisible) return false; var l = doc.LayerOf(e.Layer); return !(l != null && (l.Off || l.Frozen)); }

    /// <summary>ACI → print RGB (white background): 7/0/256 → black; bright colours darkened so they stay readable on paper.</summary>
    public static int PrintColor(int aci)
    {
        aci = Math.Abs(aci);
        if (aci is 7 or 0 or 256 || aci > 255) return 0x000000;
        int c = Aci[aci]; int r = c >> 16, g = (c >> 8) & 255, b = c & 255; double lum = 0.299 * r + 0.587 * g + 0.114 * b;
        if (lum > 160) { double k = 110 / lum; r = (int)(r * k); g = (int)(g * k); b = (int)(b * k); }
        return (r << 16) | (g << 8) | b;
    }
    static readonly int[] Aci = BuildAci();
    static int[] BuildAci()
    {
        var a = new int[256]; int[] baseC = { 0x000000, 0xFF0000, 0xFFFF00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xFF00FF, 0xFFFFFF, 0x414141, 0x808080 };
        for (int i = 0; i < 10; i++) a[i] = baseC[i];
        int idx = 10; double[] vals = { 1, 0.65, 0.5, 0.3, 1, 0.65, 0.5, 0.3, 1, 0.65 };
        for (int hue = 0; hue < 360; hue += 15) for (int k = 0; k < 10; k += 2) foreach (var sat in new[] { 1.0, 0.5 }) { if (idx < 250) a[idx++] = Hsv(hue, sat, vals[k]); }
        a[250] = 0x333333; a[251] = 0x505050; a[252] = 0x696969; a[253] = 0x828282; a[254] = 0xBEBEBE; a[255] = 0xFFFFFF;
        return a;
    }
    static int Hsv(double hdeg, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(hdeg / 60 % 2 - 1)), m = v - c; double r, g, b;
        if (hdeg < 60) (r, g, b) = (c, x, 0); else if (hdeg < 120) (r, g, b) = (x, c, 0); else if (hdeg < 180) (r, g, b) = (0, c, x); else if (hdeg < 240) (r, g, b) = (0, x, c); else if (hdeg < 300) (r, g, b) = (x, 0, c); else (r, g, b) = (c, 0, x);
        return ((int)Math.Round((r + m) * 255) << 16) | ((int)Math.Round((g + m) * 255) << 8) | (int)Math.Round((b + m) * 255);
    }

    sealed class Ctx
    {
        readonly Document _doc; readonly PdfWriter.Page _p; readonly double _k, _ox, _oy; readonly Sheet _sheet;
        public Ctx(Document doc, PdfWriter.Page p, double k, double ox, double oy, Sheet sheet) { _doc = doc; _p = p; _k = k; _ox = ox; _oy = oy; _sheet = sheet; }
        (double x, double y) P(V3 v, Xform t) { var q = t.Apply(v); return (q.X * _k + _ox, q.Y * _k + _oy); }
        int ColorOf(Entity e, (int col, string layer)? ctx)
        {
            if (e.Color == 0) return ctx?.col ?? 7;
            if (e.Color == 256 || e.Color < 0) { string ln = ctx != null && e.Layer == "0" ? ctx.Value.layer : e.Layer; var l = _doc.LayerOf(ln); return l != null ? Math.Abs(l.Color) : 7; }
            return e.Color;
        }
        double LineWidthPt(Entity e)
        {
            int lw = e.Lineweight; if (lw == -1) { var l = _doc.LayerOf(e.Layer); lw = l?.Lineweight ?? -3; }
            return lw > 0 ? Math.Max(0.2, lw / 100.0 * 2.8346) : 0.25;
        }
        /// <summary>Draw one entity under a transform (block nesting); returns the number of text strings written.</summary>
        public int Draw(Entity e, (int col, string layer)? bctx, Xform t, int depth)
        {
            int texts = 0; int col = ColorOf(e, bctx); int rgb = PrintColor(col);
            _p.Stroke(rgb); _p.LineWidth(LineWidthPt(e) * (depth == 0 ? 1 : 1));
            double k = t.ScaleFactor;
            switch (e)
            {
                case Line l: { var a = P(l.P0, t); var b = P(l.P1, t); _p.Line(a.x, a.y, b.x, b.y); break; }
                case Circle c: Poly(Tess.Arc(c.Center.X, c.Center.Y, c.Radius, 0, 360, true, 2), t, true); break;
                case Arc a: Poly(Tess.Arc(a.Center.X, a.Center.Y, a.Radius, a.StartDeg, a.EndDeg, true, 2), t, false); break;
                case Ellipse el: Poly(Tess.EllipseArc(el.Center, el.Major, el.Ratio, el.StartParam, el.EndParam), t, false); break;
                case LwPolyline p: Poly(PolyPts(p.Vertices, p.Closed), t, false); break;
                case Polyline p: Poly(PolyPts(p.Vertices, p.Closed), t, false); break;
                case Spline sp: Poly(Tess.Spline(sp), t, false); break;
                case Solid s: { var pts = new[] { s.P0, s.P1, s.P3, s.P2 }.Select(v => P(v, t)).ToList(); _p.Fill(rgb); _p.FillPolygons(new[] { pts.SelectMany(q => new[] { q.x, q.y }).ToArray() }); break; }
                case Point pt: { var a = P(pt.Pos, t); _p.Line(a.x - 1, a.y, a.x + 1, a.y); _p.Line(a.x, a.y - 1, a.x, a.y + 1); break; }
                case Hatch h:
                    {
                        var loops = h.Loops.Select(l => Tess.HatchLoop(l)).Where(l => l.Count >= 6).Select(l => { var o = new double[l.Count]; for (int i = 0; i < l.Count; i += 2) { var q = t.Apply(new V3(l[i], l[i + 1])); o[i] = q.X * _k + _ox; o[i + 1] = q.Y * _k + _oy; } return o; }).ToList();
                        if (h.Solid) { _p.Fill(rgb); _p.FillPolygons(loops); } else foreach (var lp in loops) _p.Polyline(lp, true);
                        break;
                    }
                case Text tx: { if (tx.IsAttdef && (tx.AttribFlags & 1) != 0) break; DrawText(tx, t, rgb); texts++; break; }
                case MText m: texts += DrawMText(m, t, rgb); break;
                case Insert ins:
                    {
                        if (depth < 8 && _doc.Blocks.TryGetValue(ins.Block, out var blk))
                        {
                            for (int r = 0; r < Math.Max(1, ins.Rows); r++) for (int c = 0; c < Math.Max(1, ins.Cols); c++)
                                {
                                    double rot = ins.Rotation * Math.PI / 180, cr = Math.Cos(rot), sr = Math.Sin(rot); double ox = c * ins.ColSpacing, oy = r * ins.RowSpacing;
                                    var L = new Xform(ins.SX * cr, -ins.SY * sr, ins.SX * sr, ins.SY * cr, ins.Pos.X + ox * cr - oy * sr, ins.Pos.Y + ox * sr + oy * cr);
                                    L = new Xform(L.A, L.B, L.C, L.D, L.E - (blk.Base.X * L.A + blk.Base.Y * L.B), L.F - (blk.Base.X * L.C + blk.Base.Y * L.D));
                                    var W = Compose(t, L);
                                    foreach (var ce in blk.Entities) { if (ce.Invisible) continue; var cl = _doc.LayerOf(ce.Layer); if (cl != null && ce.Layer != "0" && (cl.Off || cl.Frozen)) continue; if (ce is Text ct && ct.IsAttdef) continue; texts += Draw(ce, (col, e.Layer), W, depth + 1); }
                                }
                        }
                        foreach (var a in ins.Attribs) if (!a.Invisible && a.Text.Value != "") { DrawText(a.Text, t, rgb); texts++; }
                        break;
                    }
                case Dimension d:
                    {
                        if (d.Block != "" && _doc.Blocks.TryGetValue(d.Block, out var blk) && depth < 8) { foreach (var ce in blk.Entities) texts += Draw(ce, (col, e.Layer), t, depth + 1); }
                        else { var a = P(d.P13, t); var b = P(d.DefPoint, t); var c2 = P(d.P14, t); _p.Line(a.x, a.y, b.x, b.y); _p.Line(c2.x, c2.y, b.x, b.y); var tm = P(d.TextMid, t); string s = d.Text is { Length: > 0 } && d.Text != "<>" ? d.Text : d.Measurement.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); _p.Fill(rgb); _p.Text(s, tm.x - _p.Measure(s) * 2.5 * _k / 2, tm.y, 2.5 * _k / _p.CapHeightEm, d.Rotation); texts++; }
                        break;
                    }
            }
            return texts;
        }
        static Xform Compose(Xform a, Xform b) => new(a.A * b.A + a.B * b.C, a.A * b.B + a.B * b.D, a.C * b.A + a.D * b.C, a.C * b.B + a.D * b.D, a.A * b.E + a.B * b.F + a.E, a.C * b.E + a.D * b.F + a.F);
        static List<double> PolyPts(List<PVertex> vs, bool closed)
        {
            var pts = new List<double>(); int n = vs.Count; if (n == 0) return pts;
            for (int i = 0; i < n; i++) { var v = vs[i]; if (i == 0) { pts.Add(v.X); pts.Add(v.Y); } bool last = i == n - 1; if (last && !closed) break; var nx = vs[(i + 1) % n]; Tess.Bulge(v.X, v.Y, nx.X, nx.Y, v.Bulge, pts); }
            return pts;
        }
        void Poly(List<double> pts, Xform t, bool closed)
        {
            if (pts.Count < 4) return; var o = new double[pts.Count];
            for (int i = 0; i < pts.Count; i += 2) { var q = t.Apply(new V3(pts[i], pts[i + 1])); o[i] = q.X * _k + _ox; o[i + 1] = q.Y * _k + _oy; }
            _p.Polyline(o, closed);
        }
        void DrawText(Text tx, Xform t, int rgb)
        {
            string s = TextCodes.Plain(tx.Value); if (string.IsNullOrWhiteSpace(s)) return;
            double h = tx.Height * t.ScaleFactor * _k; if (h <= 0.05) return; double size = h / _p.CapHeightEm;
            double rot = t.ApplyAngleDeg(tx.Rotation); double wf = tx.WidthFactor > 0 ? tx.WidthFactor : 1;
            var anchor = P(tx.Anchor, t); double wpt = _p.Measure(s) * size * wf;
            double dx = 0, dy = 0;
            if (tx.HAlign == 1 || tx.HAlign == 4) dx = -wpt / 2; else if (tx.HAlign == 2) dx = -wpt; else if (tx.HAlign == 3 || tx.HAlign == 5) { var ins = P(tx.Pos, t); double d = Math.Sqrt((anchor.x - ins.x) * (anchor.x - ins.x) + (anchor.y - ins.y) * (anchor.y - ins.y)); anchor = ins; wf = wpt > 0 ? d / (wpt / wf) : wf; }
            if (tx.VAlign == 2 || tx.HAlign == 4) dy = -h / 2; else if (tx.VAlign == 3) dy = -h; else if (tx.VAlign == 1) dy = 0;
            double r = rot * Math.PI / 180; double x = anchor.x + dx * Math.Cos(r) - dy * Math.Sin(r), y = anchor.y + dx * Math.Sin(r) + dy * Math.Cos(r);
            _p.Fill(rgb); _p.Text(s, x, y, size, rot, wf);
        }
        int DrawMText(MText m, Xform t, int rgb)
        {
            var lines = MTextParser.Parse(m.Value); double h = m.Height; if (h <= 0 && lines.Count > 0 && lines[0].Runs.Count > 0 && lines[0].Runs[0].HeightFactorIsAbsolute) h = lines[0].Runs[0].HeightFactor; if (h <= 0) h = 2.5;
            double hp = h * t.ScaleFactor * _k; if (hp <= 0.05) return 0; double size = hp / _p.CapHeightEm; double lh = hp * 1.667 * (m.LineSpacing > 0 ? m.LineSpacing : 1);
            double rot = t.ApplyAngleDeg(m.RotationDeg); double r = rot * Math.PI / 180; var pos = P(m.Pos, t);
            // wrap by width
            var outLines = new List<string>(); double wpt = m.Width >= h * 2 ? m.Width * t.ScaleFactor * _k : 0;   // dxf2dwg round trips leave rect_width = 1.0: narrower than two characters means "no wrapping"
            foreach (var l in lines)
            {
                string plain = l.Plain; if (wpt <= 0) { outLines.Add(plain); continue; }
                var words = plain.Split(' '); string cur = "";
                foreach (var wd in words) { string cand = cur.Length == 0 ? wd : cur + " " + wd; if (_p.Measure(cand) * size > wpt && cur.Length > 0) { outLines.Add(cur); cur = wd; } else cur = cand; }
                outLines.Add(cur);
            }
            int nl = outLines.Count; double boxH = nl * lh; double boxW = wpt > 0 ? wpt : outLines.Max(l => _p.Measure(l) * size);
            int colA = (m.Attach - 1) % 3, rowA = (m.Attach - 1) / 3; double left = colA == 0 ? 0 : colA == 1 ? -boxW / 2 : -boxW; double top = rowA == 0 ? 0 : rowA == 1 ? boxH / 2 : boxH;
            int n = 0;
            for (int i = 0; i < nl; i++)
            {
                string s = outLines[i]; if (string.IsNullOrWhiteSpace(s)) continue; double lw = _p.Measure(s) * size;
                double lx = left + (colA == 1 ? (boxW - lw) / 2 : colA == 2 ? boxW - lw : 0), ly = top - (i + 1) * lh + lh * 0.25;
                double x = pos.x + lx * Math.Cos(r) - ly * Math.Sin(r), y = pos.y + lx * Math.Sin(r) + ly * Math.Cos(r);
                _p.Fill(rgb); _p.Text(s, x, y, size, rot); n++;
            }
            return n;
        }
    }
}
