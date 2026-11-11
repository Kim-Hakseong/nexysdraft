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
    /// <summary>Paper of one page: ISO name, size in mm (landscape when W &gt; H), why it was chosen, and the text cap heights on it.</summary>
    public sealed record PageInfo(string Label, string Paper, double WidthMm, double HeightMm, string Basis, int Texts, double MinTextMm, double P10TextMm, double MedianTextMm);
    public sealed record Result(string Path, int Pages, List<Sheet> Sheets, bool SheetsDetected, double Ms, int Texts, string FontPath)
    {
        public List<PageInfo> PageInfos { get; init; } = new();
    }
    public const double A3W = 1190.55, A3H = 841.89, MarginMm = 10, Margin = MarginMm * 72 / 25.4;
    public static readonly (string name, double w, double h)[] Iso = { ("A4", 297, 210), ("A3", 420, 297), ("A2", 594, 420), ("A1", 841, 594), ("A0", 1189, 841) };
    const double Pt = 72 / 25.4;

    /// <summary>Paper size of the Model layout's plot settings (LAYOUT "Model", groups 44/45 mm, 4 media name), when the drawing carries one.</summary>
    public static (double w, double h, string media)? ModelPlotPaper(Document doc)
    {
        var o = doc.ObjectsRaw; (double w, double h, string media)? found = null;
        for (int i = 0; i < o.Count; i++)
        {
            if (o[i].Code != 0 || o[i].Value != "LAYOUT") continue;
            double w = 0, h = 0; string media = "", name = ""; int j = i + 1;
            for (; j < o.Count && o[j].Code != 0; j++)
            {
                var g = o[j];
                if (g.Code == 44) w = Dxf.DxfCodec.ParseDouble(g.Value); else if (g.Code == 45) h = Dxf.DxfCodec.ParseDouble(g.Value); else if (g.Code == 4 && media == "") media = g.Value; else if (g.Code == 1) name = g.Value;
            }
            if (name.Equals("Model", StringComparison.OrdinalIgnoreCase) && w > 1 && h > 1) found = (w, h, media);
        }
        return found;
    }

    /// <summary>ISO A4–A0 for a sheet: the paper of the drawing's own plot settings (nearest ISO by area) when present, else A3 (the company default);
    /// orientation from the sheet's aspect ratio.</summary>
    public static (string name, double wMm, double hMm, string basis) ChoosePaper(Document doc, Sheet sheet)
    {
        bool land = sheet.W >= sheet.H; var plot = ModelPlotPaper(doc);
        (string name, double w, double h) pick; string basis;
        if (plot != null)
        {
            double area = plot.Value.w * plot.Value.h;
            pick = Iso.OrderBy(p => Math.Abs(Math.Log(p.w * p.h / area))).First();
            basis = $"Model plot settings: media '{plot.Value.media}' {plot.Value.w:F0}×{plot.Value.h:F0} mm → nearest ISO {pick.name}";
        }
        else { pick = Iso[1]; basis = "no plot settings → A3 default"; }
        basis += $", {(land ? "landscape" : "portrait")} (sheet {sheet.W:F0}×{sheet.H:F0}, ratio {Math.Max(sheet.W, sheet.H) / Math.Max(1e-9, Math.Min(sheet.W, sheet.H)):F3})";
        return land ? (pick.name, pick.w, pick.h, basis) : (pick.name, pick.h, pick.w, basis);
    }

    /// <summary>Scale (paper mm per drawing unit) for a sheet on its chosen paper with the 10 mm margin.</summary>
    public static double MmPerUnit(Document doc, Sheet sheet) { var p = ChoosePaper(doc, sheet); return Math.Min((p.wMm - 2 * MarginMm) / Math.Max(1e-9, sheet.W), (p.hMm - 2 * MarginMm) / Math.Max(1e-9, sheet.H)); }

    public static Result Export(Document doc, string path, string? fontPath = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        fontPath ??= TrueTypeFont.FindSystemFont() ?? throw new FileNotFoundException("no TrueType font (malgun.ttf) found");
        var font = TrueTypeFont.Load(fontPath); var pdf = new PdfWriter(font);
        var infos = new List<PageInfo>(); var ents = doc.Entities.Where(e => !e.PaperSpace && e is not Unknown && Visible(doc, e)).ToList();
        var pageSheets = PageSheets(doc, ents, out bool detected);
        int texts = 0;
        foreach (var sheet in pageSheets)
        {
            var (ctx, info) = RenderSheet(doc, pdf, sheet, ents);
            texts += info.Texts; infos.Add(info);
        }
        var bytes = pdf.Build(Path.GetFileNameWithoutExtension(doc.Path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
        return new Result(path, pageSheets.Count, pageSheets, detected, sw.Elapsed.TotalMilliseconds, texts, fontPath) { PageInfos = infos };
    }

    /// <summary>The pages of the merged PDF: the detected sheets (or the whole drawing as one sheet), plus a "rest" page for content outside every sheet.</summary>
    public static List<Sheet> PageSheets(Document doc, List<Entity> ents, out bool detected)
    {
        var sheets = SheetDetector.Detect(doc, out detected); var pageSheets = new List<Sheet>(sheets);
        if (detected)
        {
            var rest = new List<Entity>();
            foreach (var e in ents) { var b = Bounds.Of(e, doc, 0); if (b == null) continue; double cx = (b.Value.x0 + b.Value.x1) / 2, cy = (b.Value.y0 + b.Value.y1) / 2; if (!sheets.Any(s => s.Contains(cx, cy))) rest.Add(e); }
            if (rest.Count > 0) { double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue; foreach (var e in rest) { var b = Bounds.Of(e, doc, 0)!.Value; x0 = Math.Min(x0, b.x0); y0 = Math.Min(y0, b.y0); x1 = Math.Max(x1, b.x1); y1 = Math.Max(y1, b.y1); } pageSheets.Add(new Sheet(x0, y0, x1, y1, "rest")); }
        }
        return pageSheets;
    }

    static (Ctx ctx, PageInfo info) RenderSheet(Document doc, PdfWriter pdf, Sheet sheet, List<Entity> ents)
    {
        var paper = ChoosePaper(doc, sheet); double pw = paper.wMm * Pt, ph = paper.hMm * Pt;
        var page = pdf.AddPage(pw, ph);
        double w = Math.Max(1e-9, sheet.W), h = Math.Max(1e-9, sheet.H); double scale = Math.Min((pw - 2 * Margin) / w, (ph - 2 * Margin) / h);
        double ox = (pw - w * scale) / 2 - sheet.X0 * scale, oy = (ph - h * scale) / 2 - sheet.Y0 * scale;
        var ctx = new Ctx(doc, page, scale, ox, oy, sheet); int texts = 0;
        foreach (var e in ents)
        {
            var b = Bounds.Of(e, doc, 0); if (b == null) continue;
            if (b.Value.x1 < sheet.X0 || b.Value.x0 > sheet.X1 || b.Value.y1 < sheet.Y0 || b.Value.y0 > sheet.Y1) continue;
            texts += ctx.Draw(e, null, Xform.Identity, 0);
        }
        var hs = ctx.TextMm.OrderBy(x => x).ToList();
        return (ctx, new PageInfo(sheet.Label, paper.name, paper.wMm, paper.hMm, paper.basis, texts, hs.Count > 0 ? hs[0] : 0, hs.Count > 0 ? hs[hs.Count / 10] : 0, hs.Count > 0 ? hs[hs.Count / 2] : 0));
    }

    /// <summary>One single-page PDF per detected sheet next to the main file: name__001.pdf … (the main file keeps all pages).</summary>
    public static int ExportPerSheet(Document doc, string mainPath, Result res)
    {
        int n = 0; string dir = Path.GetDirectoryName(Path.GetFullPath(mainPath))!, stem = Path.GetFileNameWithoutExtension(mainPath);
        foreach (var s in res.Sheets) { if (s.Label == "rest") continue; ExportSheet(doc, Path.Combine(dir, stem + "__" + s.Label + ".pdf"), s, res.FontPath); n++; }
        return n;
    }
    /// <summary>One file of a PDF export set: kind = merged (all sheet pages) | sheet (one sheet) | overview (whole model space on one page).</summary>
    public sealed record OutFile(string Path, string Kind, int Pages, string Paper);

    /// <summary>The PDF output set (draft5 I3, docs/pdf-output.md): <c>name.pdf</c> = every sheet as a page (+ a "rest" page for content
    /// outside the sheets), <c>name__001.pdf …</c> = one file per page of name.pdf (<c>name__rest.pdf</c> for the rest page), <c>name__overview.pdf</c> = the whole model space on one page
    /// (paper chosen like a sheet). Each part can be switched off; with no sheet detected the merged file is the extents page.
    /// <paramref name="collapseSingle"/> (draft5b): when the merged PDF has a single page (one sheet and no rest, or no sheet detected) the per-page and
    /// overview files would repeat it, so only the merged file is written. Callers that name the parts explicitly (CLI flags) pass false.</summary>
    public static (Result? merged, List<OutFile> files) ExportSet(Document doc, string mainPath, bool merged = true, bool sheets = true, bool overview = true, string? fontPath = null, bool collapseSingle = false)
    {
        var files = new List<OutFile>(); Result? res = null;
        string dir = Path.GetDirectoryName(Path.GetFullPath(mainPath))!, stem = Path.GetFileNameWithoutExtension(mainPath);
        Directory.CreateDirectory(dir);
        fontPath ??= TrueTypeFont.FindSystemFont() ?? throw new FileNotFoundException("no TrueType font (malgun.ttf) found");
        if (merged) { res = Export(doc, mainPath, fontPath); files.Add(new OutFile(Path.GetFullPath(mainPath), "merged", res.Pages, string.Join("/", res.PageInfos.Select(p => p.Paper).Distinct()))); }
        if (collapseSingle && res != null && res.Pages == 1) { sheets = false; overview = false; }
        if (sheets)
        {
            // one file per page of the merged PDF: sheets name__001 …, the content outside the sheets name__rest, an undetected drawing name__001
            var list = res?.Sheets ?? PageSheets(doc, doc.Entities.Where(e => !e.PaperSpace && e is not Unknown && Visible(doc, e)).ToList(), out _);
            foreach (var s in list) { string label = s.Label is { Length: > 0 } l ? l : "001"; string p = Path.Combine(dir, stem + "__" + label + ".pdf"); ExportSheet(doc, p, s, fontPath); files.Add(new OutFile(p, "sheet", 1, ChoosePaper(doc, s).name)); }
        }
        if (overview)
        {
            var b = Bounds.Of(doc);
            if (b != null) { var s = new Sheet(b.Value.x0, b.Value.y0, b.Value.x1, b.Value.y1, "overview"); string p = Path.Combine(dir, stem + "__overview.pdf"); ExportSheet(doc, p, s, fontPath); files.Add(new OutFile(p, "overview", 1, ChoosePaper(doc, s).name)); }
        }
        return (res, files);
    }

    static void ExportSheet(Document doc, string path, Sheet sheet, string fontPath)
    {
        var font = TrueTypeFont.Load(fontPath); var pdf = new PdfWriter(font);
        var ents = doc.Entities.Where(e => !e.PaperSpace && e is not Unknown && Visible(doc, e)).ToList();
        RenderSheet(doc, pdf, sheet, ents);
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
            _p.Dash(e is Line or Circle or Arc or Ellipse or LwPolyline or Polyline or Spline ? DashOf(e, bctx, k) : null);
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
                        if (h.Solid) { _p.Fill(rgb); _p.FillPolygons(loops); }
                        else
                        {
                            var hl = Edit.HatchPattern.Lines(h);   // the pattern lines; a hatch without pattern data (or too dense) keeps its outline
                            if (hl.Count == 0) foreach (var lp in loops) _p.Polyline(lp, true);
                            else { _p.LineWidth(0.18); for (int i = 0; i + 3 < hl.Count; i += 4) { var a = P(new V3(hl[i], hl[i + 1]), t); var b = P(new V3(hl[i + 2], hl[i + 3]), t); _p.Line(a.x, a.y, b.x, b.y); } }
                        }
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
        /// <summary>The entity's linetype as a PDF dash array (points), or null for a continuous line. BYLAYER takes the layer's linetype (the insert's layer for layer 0 inside a block).</summary>
        double[]? DashOf(Entity e, (int col, string layer)? ctx, double k)
        {
            string name = e.Linetype;
            if (name.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase)) { string ln = ctx != null && e.Layer == "0" ? ctx.Value.layer : e.Layer; name = _doc.LayerOf(ln)?.Linetype ?? "Continuous"; }
            if (Edit.StdLinetypes.IsContinuous(name)) return null;
            var lt = _doc.Linetypes.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); if (lt == null || lt.Pattern.Count < 2) return null;
            double s = _doc.HeaderDouble("$LTSCALE", 1) * (e.LtScale > 0 ? e.LtScale : 1) * k * _k; if (!(s > 0)) return null;
            var arr = lt.Pattern.Select(v => Math.Max(0.35, Math.Abs(v) * s)).ToArray(); if (arr.Length % 2 == 1) arr = arr.Concat(arr).ToArray();
            return arr.Sum() < 1.2 ? null : arr;   // a pattern shorter than 1.2 pt would print as grey: keep it solid
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
            _p.Fill(rgb); _p.Text(s, x, y, size, rot, wf); TextMm.Add(h * 25.4 / 72);
        }
        /// <summary>One piece of an MTEXT line: text with its own cap height (pt) and width factor (from the run's \H and \W codes).</summary>
        sealed record Piece(string Text, double CapPt, double Wf) { public double Width(PdfWriter.Page p) => p.Measure(Text) * (CapPt / p.CapHeightEm) * Wf; }
        int DrawMText(MText m, Xform t, int rgb)
        {
            var lines = MTextParser.Parse(m.Value); double h = m.Height; if (h <= 0 && lines.Count > 0 && lines[0].Runs.Count > 0 && lines[0].Runs[0].HeightFactorIsAbsolute) h = lines[0].Runs[0].HeightFactor; if (h <= 0) h = 2.5;
            double hp = h * t.ScaleFactor * _k; if (hp <= 0.05) return 0;
            double rot = t.ApplyAngleDeg(m.RotationDeg); double r = rot * Math.PI / 180; var pos = P(m.Pos, t);
            double wpt = m.Width >= h * 2 ? m.Width * t.ScaleFactor * _k : 0;   // dxf2dwg round trips leave rect_width = 1.0: narrower than two characters means "no wrapping"
            // runs → pieces (per-run height: \H2.5; absolute world height, \H1.5x; factor of the base height; width factor \W), wrapped by words
            var outLines = new List<List<Piece>>();
            foreach (var l in lines)
            {
                var cur = new List<Piece>(); double curW = 0;
                foreach (var run in l.Runs)
                {
                    double cap = run.HeightFactorIsAbsolute ? run.HeightFactor * t.ScaleFactor * _k : hp * run.HeightFactor; double wf = (run.WidthFactor > 0 ? run.WidthFactor : 1) * (run.Tracking > 0 ? run.Tracking : 1);   // T tracking approximated as horizontal scale
                    var words = wpt > 0 ? System.Text.RegularExpressions.Regex.Split(run.Text, "(?<= )") : new[] { run.Text };
                    foreach (var wd in words)
                    {
                        if (wd.Length == 0) continue; var pc = new Piece(wd, cap, wf); double w = pc.Width(_p);
                        if (wpt > 0 && curW + w > wpt && curW > 0) { outLines.Add(cur); cur = new List<Piece>(); curW = 0; }
                        if (cur.Count > 0 && cur[^1].CapPt == cap && cur[^1].Wf == wf) cur[^1] = cur[^1] with { Text = cur[^1].Text + wd }; else cur.Add(pc);
                        curW += w;
                    }
                }
                outLines.Add(cur);
            }
            double spacing = 1.667 * (m.LineSpacing > 0 ? m.LineSpacing : 1);
            var heights = outLines.Select(l => (l.Count == 0 ? hp : l.Max(p => p.CapPt)) * spacing).ToList();
            double boxH = heights.Sum(); double boxW = wpt > 0 ? wpt : Math.Max(1e-6, outLines.Max(l => l.Sum(p => p.Width(_p))));
            int colA = (m.Attach - 1) % 3, rowA = (m.Attach - 1) / 3; double left = colA == 0 ? 0 : colA == 1 ? -boxW / 2 : -boxW; double top = rowA == 0 ? 0 : rowA == 1 ? boxH / 2 : boxH;
            int n = 0; double yCursor = top;
            for (int i = 0; i < outLines.Count; i++)
            {
                var l = outLines[i]; double lhI = heights[i]; yCursor -= lhI;
                if (l.Count == 0 || l.All(p => string.IsNullOrWhiteSpace(p.Text))) continue;
                double lw = l.Sum(p => p.Width(_p)); double lx = left + (colA == 1 ? (boxW - lw) / 2 : colA == 2 ? boxW - lw : 0), ly = yCursor + lhI * 0.25;
                foreach (var pc in l)
                {
                    double x = pos.x + lx * Math.Cos(r) - ly * Math.Sin(r), y = pos.y + lx * Math.Sin(r) + ly * Math.Cos(r);
                    if (!string.IsNullOrWhiteSpace(pc.Text)) { _p.Fill(rgb); _p.Text(pc.Text, x, y, pc.CapPt / _p.CapHeightEm, rot, pc.Wf); TextMm.Add(pc.CapPt * 25.4 / 72); }
                    lx += pc.Width(_p);
                }
                n++;
            }
            return n;
        }
        /// <summary>Cap heights of the texts written on this page, in mm on paper.</summary>
        public List<double> TextMm { get; } = new();
    }
}
