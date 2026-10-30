using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Core.Page;

/// <summary>
/// Compact records of a document for the page (renderer, snapping, selection). One object per entity with short keys; arcs/circles/
/// ellipses stay parametric (the page tessellates by zoom), splines and hatch loops are tessellated here.
/// </summary>
public static class PageDoc
{
    public static Dictionary<string, object?> Snapshot(Document doc)
    {
        var fontOf = new Dictionary<string, (string font, bool shx)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.Styles) fontOf[s.Name] = (s.TrueTypeFamily ?? s.Font, s.TrueTypeFamily == null && (s.Font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || !s.Font.Contains('.')));
        return new()
        {
            ["path"] = doc.Path, ["version"] = doc.Version, ["codepage"] = doc.CodePage,
            ["layers"] = doc.Layers.Select(l => new { name = l.Name, color = l.Color, tc = l.TrueColor, off = l.Off, frozen = l.Frozen, locked = l.Locked, lt = l.Linetype, lw = l.Lineweight }).ToList(),
            ["styles"] = doc.Styles.Select(s => new { name = s.Name, font = s.Font, ttf = s.TrueTypeFamily, big = s.BigFont, wf = s.WidthFactor }).ToList(),
            ["ltypes"] = doc.Linetypes.Select(l => new { name = l.Name, pattern = l.Pattern }).ToList(),
            ["blocks"] = doc.Blocks.Where(kv => !kv.Value.IsLayout).ToDictionary(kv => kv.Key, kv => (object)new { name = kv.Value.Name, bx = kv.Value.Base.X, by = kv.Value.Base.Y, ents = kv.Value.Entities.Select(e => Record(e, doc, fontOf)).Where(r => r != null).ToList() }),
            ["ents"] = doc.Entities.Where(e => !e.PaperSpace).Select(e => Record(e, doc, fontOf)).Where(r => r != null).ToList(),
            ["counts"] = doc.TypeCounts,
            ["unknown"] = doc.UnknownCount,
            ["paper"] = doc.Entities.Count(e => e.PaperSpace),
            ["warnings"] = doc.Warnings,
        };
    }

    public static Dictionary<string, object?>? Record(Entity e, Document doc, Dictionary<string, (string font, bool shx)> fontOf)
    {
        var r = new Dictionary<string, object?> { ["h"] = e.Handle, ["l"] = e.Layer, ["c"] = e.Color };
        if (e.TrueColor != null) r["tc"] = e.TrueColor; if (!e.Linetype.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase)) r["lt"] = e.Linetype; if (e.Invisible) r["inv"] = 1; if (e.Lineweight > 0) r["lw"] = e.Lineweight;
        switch (e)
        {
            case Line l: r["t"] = "L"; r["p"] = new[] { l.P0.X, l.P0.Y, l.P1.X, l.P1.Y }; break;
            case Circle c: r["t"] = "C"; r["p"] = new[] { c.Center.X, c.Center.Y, c.Radius }; break;
            case Arc a: r["t"] = "A"; r["p"] = new[] { a.Center.X, a.Center.Y, a.Radius, a.StartDeg, a.EndDeg }; break;
            case Ellipse el: r["t"] = "E"; r["p"] = new[] { el.Center.X, el.Center.Y, el.Major.X, el.Major.Y, el.Ratio, el.StartParam, el.EndParam }; break;
            case LwPolyline p: { r["t"] = "P"; var a = new double[p.Vertices.Count * 3]; for (int i = 0; i < p.Vertices.Count; i++) { a[i * 3] = p.Vertices[i].X; a[i * 3 + 1] = p.Vertices[i].Y; a[i * 3 + 2] = p.Vertices[i].Bulge; } r["p"] = a; if (p.Closed) r["cl"] = 1; double w = p.ConstWidth > 0 ? p.ConstWidth : p.Vertices.Count > 0 ? Math.Max(p.Vertices[0].StartWidth, p.Vertices[0].EndWidth) : 0; if (w > 0) r["w"] = w; break; }
            case Polyline p: { if (p.Vertices.Count == 0) return null; r["t"] = "P"; var a = new double[p.Vertices.Count * 3]; for (int i = 0; i < p.Vertices.Count; i++) { a[i * 3] = p.Vertices[i].X; a[i * 3 + 1] = p.Vertices[i].Y; a[i * 3 + 2] = p.Vertices[i].Bulge; } r["p"] = a; if (p.Closed) r["cl"] = 1; if (p.StartWidth > 0) r["w"] = p.StartWidth; r["heavy"] = 1; break; }
            case Text t:
                {
                    if (t.IsAttdef && (t.AttribFlags & 1) != 0) return null;
                    r["t"] = "T"; r["p"] = new[] { t.Pos.X, t.Pos.Y, t.Align.X, t.Align.Y }; r["h"] = e.Handle; r["ht"] = t.Height; r["rot"] = t.Rotation; r["s"] = TextCodes.Plain(t.Value); r["ha"] = t.HAlign; r["va"] = t.VAlign; r["wf"] = t.WidthFactor; r["st"] = t.Style; if (t.Oblique != 0) r["ob"] = t.Oblique; if ((t.GenFlags & 2) != 0) r["bw"] = 1; if ((t.GenFlags & 4) != 0) r["ud"] = 1;
                    if (fontOf.TryGetValue(t.Style, out var f)) { r["font"] = f.font; if (f.shx) r["shx"] = 1; }
                    if (t.IsAttrib) r["attr"] = t.AttribTag; if (t.IsAttdef) { r["attr"] = t.AttribTag; r["attdef"] = 1; }
                    break;
                }
            case MText m:
                {
                    r["t"] = "M"; r["p"] = new[] { m.Pos.X, m.Pos.Y }; r["ht"] = m.Height; r["w"] = m.Width; r["rot"] = m.RotationDeg; r["at"] = m.Attach; r["ls"] = m.LineSpacing; r["raw"] = m.Value; r["st"] = m.Style;
                    if (fontOf.TryGetValue(m.Style, out var f)) { r["font"] = f.font; if (f.shx) r["shx"] = 1; }
                    r["lines"] = MTextParser.Parse(m.Value).Select(l => l.Runs.Select(run => new Dictionary<string, object?> { ["t"] = run.Text, ["f"] = run.Font, ["b"] = run.Bold ? 1 : 0, ["i"] = run.Italic ? 1 : 0, ["hf"] = run.HeightFactor, ["wf"] = run.WidthFactor, ["u"] = run.Underline ? 1 : 0, ["c"] = run.Color, ["abs"] = run.HeightFactorIsAbsolute ? 1 : 0 }).ToList()).ToList();
                    break;
                }
            case Insert ins:
                {
                    r["t"] = "I"; r["blk"] = ins.Block; r["p"] = new[] { ins.Pos.X, ins.Pos.Y, ins.SX, ins.SY, ins.Rotation }; if (ins.Cols > 1 || ins.Rows > 1) r["grid"] = new[] { ins.Cols, ins.Rows, (int)ins.ColSpacing, (int)ins.RowSpacing }; if (ins.Cols > 1 || ins.Rows > 1) r["gridf"] = new[] { ins.ColSpacing, ins.RowSpacing };
                    r["attrs"] = ins.Attribs.Where(a => !a.Invisible && a.Text.Value != "").Select(a => Record(a.Text, doc, fontOf)).ToList();
                    break;
                }
            case Hatch h:
                {
                    r["t"] = "H"; r["solid"] = h.Solid ? 1 : 0; r["pat"] = h.Pattern; r["ang"] = h.PatternAngle; r["sc"] = h.PatternScale;
                    r["loops"] = h.Loops.Select(l => Tess.HatchLoop(l).ToArray()).Where(a => a.Length >= 6).ToList();
                    break;
                }
            case Dimension d:
                {
                    r["t"] = "D"; r["blk"] = d.Block; r["p"] = new[] { d.DefPoint.X, d.DefPoint.Y, d.TextMid.X, d.TextMid.Y, d.P13.X, d.P13.Y, d.P14.X, d.P14.Y }; r["kind"] = d.Kind; r["rot"] = d.Rotation; r["s"] = d.Text;
                    if (d.HasMeasurement) r["meas"] = d.Measurement;
                    break;
                }
            case Solid s: r["t"] = "S"; r["p"] = new[] { s.P0.X, s.P0.Y, s.P1.X, s.P1.Y, s.P2.X, s.P2.Y, s.P3.X, s.P3.Y }; break;
            case Point pt: r["t"] = "PT"; r["p"] = new[] { pt.Pos.X, pt.Pos.Y }; break;
            case Spline sp: { var pts = Tess.Spline(sp); if (pts.Count < 4) return null; r["t"] = "P"; var a = new double[pts.Count / 2 * 3]; for (int i = 0; i < pts.Count / 2; i++) { a[i * 3] = pts[i * 2]; a[i * 3 + 1] = pts[i * 2 + 1]; } r["p"] = a; r["spline"] = 1; if (sp.Closed) r["cl"] = 1; r["ctrl"] = sp.Control.Count > 0 ? sp.Control.SelectMany(c => new[] { c.X, c.Y }).ToArray() : sp.Fit.SelectMany(c => new[] { c.X, c.Y }).ToArray(); break; }
            case Unknown u: r["t"] = "X"; r["name"] = u.Name; break;
            default: return null;
        }
        return r;
    }
}
