using System.Text;
using System.Text.Json;
using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Core.Page;

/// <summary>Fast path of <see cref="PageDoc.Snapshot"/>: the same records written straight with Utf8JsonWriter (no boxing / dictionaries) — the
/// 100k-entity open gate spends most of its time in serialization otherwise.</summary>
public static class PageJson
{
    public static byte[] DocMessage(Document doc)
    {
        var fontOf = new Dictionary<string, (string font, bool shx)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.Styles) fontOf[s.Name] = (s.TrueTypeFamily ?? s.Font, s.TrueTypeFamily == null && (s.Font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || !s.Font.Contains('.')));
        var ms = new MemoryStream(1 << 20);
        using var w = new Utf8JsonWriter(ms, new JsonWriterOptions { SkipValidation = true });
        w.WriteStartObject(); w.WriteString("ev", "doc"); w.WritePropertyName("doc"); w.WriteStartObject();
        w.WriteString("path", doc.Path); w.WriteString("version", doc.Version); w.WriteString("codepage", doc.CodePage);
        w.WritePropertyName("layers"); w.WriteStartArray();
        foreach (var l in doc.Layers) { w.WriteStartObject(); w.WriteString("name", l.Name); w.WriteNumber("color", l.Color); if (l.TrueColor != null) w.WriteNumber("tc", l.TrueColor.Value); w.WriteBoolean("off", l.Off); w.WriteBoolean("frozen", l.Frozen); w.WriteBoolean("locked", l.Locked); w.WriteString("lt", l.Linetype); w.WriteNumber("lw", l.Lineweight); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WritePropertyName("styles"); w.WriteStartArray();
        foreach (var s in doc.Styles) { w.WriteStartObject(); w.WriteString("name", s.Name); w.WriteString("font", s.Font); if (s.TrueTypeFamily != null) w.WriteString("ttf", s.TrueTypeFamily); w.WriteString("big", s.BigFont); w.WriteNumber("wf", s.WidthFactor); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WritePropertyName("ltypes"); w.WriteStartArray();
        foreach (var l in doc.Linetypes) { w.WriteStartObject(); w.WriteString("name", l.Name); w.WritePropertyName("pattern"); w.WriteStartArray(); foreach (var p in l.Pattern) w.WriteNumberValue(p); w.WriteEndArray(); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WritePropertyName("blocks"); w.WriteStartObject();
        foreach (var kv in doc.Blocks) { if (kv.Value.IsLayout) continue; w.WritePropertyName(kv.Key); WriteBlock(w, kv.Value, doc, fontOf); }
        w.WriteEndObject();
        w.WritePropertyName("ents"); w.WriteStartArray();
        foreach (var e in doc.Entities) if (!e.PaperSpace) Record(w, e, doc, fontOf);
        w.WriteEndArray();
        w.WritePropertyName("counts"); w.WriteStartObject(); foreach (var kv in doc.TypeCounts) w.WriteNumber(kv.Key, kv.Value); w.WriteEndObject();
        w.WriteNumber("unknown", doc.UnknownCount); w.WriteNumber("paper", doc.Entities.Count(e => e.PaperSpace));
        w.WritePropertyName("warnings"); w.WriteStartArray(); foreach (var s in doc.Warnings) w.WriteStringValue(s); w.WriteEndArray();
        w.WriteEndObject(); w.WriteEndObject(); w.Flush();
        return ms.ToArray();
    }
    public static void WriteBlock(Utf8JsonWriter w, Block b, Document doc, Dictionary<string, (string font, bool shx)> fontOf)
    {
        w.WriteStartObject(); w.WriteString("name", b.Name); w.WriteNumber("bx", b.Base.X); w.WriteNumber("by", b.Base.Y); w.WritePropertyName("ents"); w.WriteStartArray();
        foreach (var e in b.Entities) Record(w, e, doc, fontOf);
        w.WriteEndArray(); w.WriteEndObject();
    }
    static void Num(Utf8JsonWriter w, double v) { if (double.IsFinite(v)) w.WriteNumberValue(v); else w.WriteNumberValue(0); }
    static void Arr(Utf8JsonWriter w, string name, params double[] v) { w.WritePropertyName(name); w.WriteStartArray(); foreach (var x in v) Num(w, x); w.WriteEndArray(); }

    /// <summary>"ws": start / end width of every polyline segment (sw0, ew0, sw1, ew1 …) — written only when a segment has a width.</summary>
    static void Widths(Utf8JsonWriter w, List<PVertex> vs)
    {
        if (!vs.Any(v => v.StartWidth > 0 || v.EndWidth > 0)) return;
        w.WritePropertyName("ws"); w.WriteStartArray(); foreach (var v in vs) { Num(w, v.StartWidth); Num(w, v.EndWidth); } w.WriteEndArray();
    }
    /// <summary>Same keys as <see cref="PageDoc.Record"/>.</summary>
    public static void Record(Utf8JsonWriter w, Entity e, Document doc, Dictionary<string, (string font, bool shx)> fontOf)
    {
        switch (e)
        {
            case Polyline p when p.Vertices.Count == 0: return;
            case Text t when t.IsAttdef && (t.AttribFlags & 1) != 0: return;
            case Unknown: break;
            case Spline sp when Tess.Spline(sp).Count < 4: return;
        }
        w.WriteStartObject(); w.WriteString("h", e.Handle); w.WriteString("l", e.Layer); w.WriteNumber("c", e.Color);
        if (e.TrueColor != null) w.WriteNumber("tc", e.TrueColor.Value); if (!e.Linetype.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase)) w.WriteString("lt", e.Linetype); if (e.Invisible) w.WriteNumber("inv", 1); if (e.Lineweight > 0) w.WriteNumber("lw", e.Lineweight);
        switch (e)
        {
            case Line l: w.WriteString("t", "L"); Arr(w, "p", l.P0.X, l.P0.Y, l.P1.X, l.P1.Y); break;
            case Circle c: w.WriteString("t", "C"); Arr(w, "p", c.Center.X, c.Center.Y, c.Radius); break;
            case Arc a: w.WriteString("t", "A"); Arr(w, "p", a.Center.X, a.Center.Y, a.Radius, a.StartDeg, a.EndDeg); break;
            case Ellipse el: w.WriteString("t", "E"); Arr(w, "p", el.Center.X, el.Center.Y, el.Major.X, el.Major.Y, el.Ratio, el.StartParam, el.EndParam); break;
            case LwPolyline p:
                w.WriteString("t", "P"); w.WritePropertyName("p"); w.WriteStartArray(); foreach (var v in p.Vertices) { Num(w, v.X); Num(w, v.Y); Num(w, v.Bulge); } w.WriteEndArray();
                if (p.Closed) w.WriteNumber("cl", 1); { double wd = p.ConstWidth > 0 ? p.ConstWidth : p.Vertices.Count > 0 ? Math.Max(p.Vertices[0].StartWidth, p.Vertices[0].EndWidth) : 0; if (wd > 0) w.WriteNumber("w", wd); } Widths(w, p.Vertices);
                break;
            case Polyline p:
                w.WriteString("t", "P"); w.WritePropertyName("p"); w.WriteStartArray(); foreach (var v in p.Vertices) { Num(w, v.X); Num(w, v.Y); Num(w, v.Bulge); } w.WriteEndArray();
                if (p.Closed) w.WriteNumber("cl", 1); if (p.StartWidth > 0) w.WriteNumber("w", p.StartWidth); w.WriteNumber("heavy", 1); if (!p.Is3D) Widths(w, p.Vertices);
                break;
            case Text t:
                w.WriteString("t", "T"); Arr(w, "p", t.Pos.X, t.Pos.Y, t.Align.X, t.Align.Y); w.WriteNumber("ht", t.Height); w.WriteNumber("rot", t.Rotation); w.WriteString("s", TextCodes.Plain(t.Value)); w.WriteNumber("ha", t.HAlign); w.WriteNumber("va", t.VAlign); w.WriteNumber("wf", t.WidthFactor); w.WriteString("st", t.Style);
                if (t.Oblique != 0) w.WriteNumber("ob", t.Oblique); if ((t.GenFlags & 2) != 0) w.WriteNumber("bw", 1); if ((t.GenFlags & 4) != 0) w.WriteNumber("ud", 1);
                if (fontOf.TryGetValue(t.Style, out var f)) { w.WriteString("font", f.font); if (f.shx) w.WriteNumber("shx", 1); }
                if (t.IsAttrib) w.WriteString("attr", t.AttribTag); if (t.IsAttdef) { w.WriteString("attr", t.AttribTag); w.WriteNumber("attdef", 1); }
                break;
            case MText m:
                w.WriteString("t", "M"); Arr(w, "p", m.Pos.X, m.Pos.Y); w.WriteNumber("ht", m.Height); w.WriteNumber("w", m.Width); w.WriteNumber("rot", m.RotationDeg); w.WriteNumber("at", m.Attach); w.WriteNumber("ls", m.LineSpacing); w.WriteString("raw", m.Value); w.WriteString("st", m.Style);
                if (fontOf.TryGetValue(m.Style, out var mf)) { w.WriteString("font", mf.font); if (mf.shx) w.WriteNumber("shx", 1); }
                w.WritePropertyName("lines"); w.WriteStartArray();
                foreach (var line in MTextParser.Parse(m.Value))
                {
                    w.WriteStartArray();
                    foreach (var run in line.Runs) { w.WriteStartObject(); w.WriteString("t", run.Text); if (run.Font != null) w.WriteString("f", run.Font); w.WriteNumber("b", run.Bold ? 1 : 0); w.WriteNumber("i", run.Italic ? 1 : 0); w.WriteNumber("hf", run.HeightFactor); w.WriteNumber("wf", run.WidthFactor); w.WriteNumber("u", run.Underline ? 1 : 0); if (run.Color != null) w.WriteNumber("c", run.Color.Value); w.WriteNumber("abs", run.HeightFactorIsAbsolute ? 1 : 0); w.WriteEndObject(); }
                    w.WriteEndArray();
                }
                w.WriteEndArray();
                break;
            case Insert ins:
                w.WriteString("t", "I"); w.WriteString("blk", ins.Block); Arr(w, "p", ins.Pos.X, ins.Pos.Y, ins.SX, ins.SY, ins.Rotation);
                if (ins.Cols > 1 || ins.Rows > 1) { w.WritePropertyName("grid"); w.WriteStartArray(); w.WriteNumberValue(ins.Cols); w.WriteNumberValue(ins.Rows); w.WriteNumberValue((int)ins.ColSpacing); w.WriteNumberValue((int)ins.RowSpacing); w.WriteEndArray(); Arr(w, "gridf", ins.ColSpacing, ins.RowSpacing); }
                w.WritePropertyName("attrs"); w.WriteStartArray(); foreach (var a in ins.Attribs) if (!a.Invisible && a.Text.Value != "") Record(w, a.Text, doc, fontOf); w.WriteEndArray();
                break;
            case Hatch h:
                w.WriteString("t", "H"); w.WriteNumber("solid", h.Solid ? 1 : 0); w.WriteString("pat", h.Pattern); w.WriteNumber("ang", h.PatternAngle); w.WriteNumber("sc", h.PatternScale);
                w.WritePropertyName("loops"); w.WriteStartArray(); foreach (var l in h.Loops) { var pts = Tess.HatchLoop(l); if (pts.Count < 6) continue; w.WriteStartArray(); foreach (var v in pts) Num(w, v); w.WriteEndArray(); } w.WriteEndArray();
                break;
            case Dimension d:
                w.WriteString("t", "D"); w.WriteString("blk", d.Block); Arr(w, "p", d.DefPoint.X, d.DefPoint.Y, d.TextMid.X, d.TextMid.Y, d.P13.X, d.P13.Y, d.P14.X, d.P14.Y); w.WriteNumber("kind", d.Kind); w.WriteNumber("rot", d.Rotation); w.WriteString("s", d.Text); if (d.HasMeasurement) w.WriteNumber("meas", d.Measurement);
                break;
            case Solid s: w.WriteString("t", "S"); Arr(w, "p", s.P0.X, s.P0.Y, s.P1.X, s.P1.Y, s.P2.X, s.P2.Y, s.P3.X, s.P3.Y); break;
            case Point pt: w.WriteString("t", "PT"); Arr(w, "p", pt.Pos.X, pt.Pos.Y); break;
            case Spline sp:
                {
                    var pts = Tess.Spline(sp); w.WriteString("t", "P"); w.WritePropertyName("p"); w.WriteStartArray(); for (int i = 0; i < pts.Count / 2; i++) { Num(w, pts[i * 2]); Num(w, pts[i * 2 + 1]); w.WriteNumberValue(0); } w.WriteEndArray(); w.WriteNumber("spline", 1); if (sp.Closed) w.WriteNumber("cl", 1);
                    w.WritePropertyName("ctrl"); w.WriteStartArray(); foreach (var c in sp.Control.Count > 0 ? sp.Control : sp.Fit) { Num(w, c.X); Num(w, c.Y); } w.WriteEndArray();
                    break;
                }
            case Unknown u: w.WriteString("t", "X"); w.WriteString("name", u.Name); break;
        }
        w.WriteEndObject();
    }
}
