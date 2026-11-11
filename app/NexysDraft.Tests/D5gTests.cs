using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft5g: TRIM and EXTEND (AutoCAD quick mode: every visible curve is a cutting edge / boundary). Geometry gate 1e-9 on the cut
/// points, undo / redo restore the drawing, and a trimmed drawing saved as DXF and DWG opens in DWG TrueView when it is installed.</summary>
public class D5gTests
{
    readonly ITestOutputHelper _o;
    public D5gTests(ITestOutputHelper o) { _o = o; }
    const double Tol = 1e-9;
    static bool Near(V3 p, double x, double y) => Math.Abs(p.X - x) < Tol && Math.Abs(p.Y - y) < Tol;
    static (Document d, Editor ed) New() { var d = Document.CreateNew(); return (d, new Editor(d)); }
    static string H(Change c) => c.Added[0].e.Handle;
    static string Snap(Document d) => string.Join("|", d.Entities.Select(e => e.Handle + ":" + e switch
    {
        Line l => $"L {l.P0} {l.P1}", Arc a => $"A {a.Center} {a.Radius:R} {a.StartDeg:R} {a.EndDeg:R}", Circle c => $"C {c.Center} {c.Radius:R}",
        LwPolyline p => "P " + p.Closed + " " + string.Join(";", p.Vertices.Select(v => $"{v.X:R},{v.Y:R},{v.Bulge:R}")), _ => e.DxfName
    }));

    [Fact]
    public void Trim_Line_MiddleAndEnds()
    {
        var (d, ed) = New();
        string h = H(ed.AddLine(0, 0, 100, 0, "0")); ed.AddLine(30, -10, 30, 10, "0"); ed.AddLine(70, -10, 70, 10, "0");
        string before = Snap(d);
        var c = ed.Trim(h, new V3(50, 0));   // between the two cutting lines → two lines
        var parts = d.Entities.OfType<Line>().Where(l => Math.Abs(l.P0.Y) < Tol && Math.Abs(l.P1.Y) < Tol).ToList();
        Assert.Equal(2, parts.Count); Assert.Single(c.Added); Assert.Single(c.Replaced);
        Assert.True(Near(parts[0].P0, 0, 0) && Near(parts[0].P1, 30, 0), $"{parts[0].P0} {parts[0].P1}");
        Assert.True(Near(parts[1].P0, 70, 0) && Near(parts[1].P1, 100, 0), $"{parts[1].P0} {parts[1].P1}");
        Assert.Equal(h, parts[0].Handle); Assert.NotEqual(h, parts[1].Handle);
        ed.Undo(); Assert.Equal(before, Snap(d)); ed.Redo(); Assert.Equal(4, d.Entities.Count);
        ed.Trim(h, new V3(10, 0));   // the left stub ends at one cutting edge only: nothing on its far side → unchanged? no: 0..30 has the cut at 30 as its end → no interior cut
        Assert.True(Near(d.Entities.OfType<Line>().First(l => l.Handle == h).P1, 30, 0));
        // an end part: cut at 30 only
        var (d2, ed2) = New(); string h2 = H(ed2.AddLine(0, 0, 100, 0, "0")); ed2.AddLine(30, -10, 30, 10, "0");
        ed2.Trim(h2, new V3(80, 0)); var l2 = (Line)d2.Find(h2)!;
        Assert.True(Near(l2.P0, 0, 0) && Near(l2.P1, 30, 0)); Assert.Equal(2, d2.Entities.Count);
    }

    [Fact]
    public void Trim_NoCrossing_ChangesNothing()
    {
        var (d, ed) = New(); string h = H(ed.AddLine(0, 0, 100, 0, "0")); ed.AddLine(0, 10, 100, 10, "0"); ed.AddLine(100, 0, 100, 50, "0");   // parallel, and one touching the end point only
        string before = Snap(d); var msgs = new List<string>();
        var c = ed.TrimExtend(new[] { (h, new V3(50, 0)) }, null, false, msgs);
        Assert.Equal(before, Snap(d)); Assert.Empty(c.Replaced); Assert.Single(msgs); Assert.False(ed.CanUndo && ed.UndoName == "Trim");
    }

    [Fact]
    public void Trim_Circle_BecomesArc_OnThePickedSide()
    {
        var (d, ed) = New(); string h = H(ed.AddCircle(0, 0, 50, "0")); ed.AddLine(-100, 0, 100, 0, "0");
        ed.Trim(h, new V3(0, 50));   // remove the upper half
        var a = Assert.IsType<Arc>(d.Find(h)); Assert.Equal(180, a.StartDeg, 9); Assert.Equal(0, Arc.Norm(a.EndDeg), 9); Assert.Equal(50, a.Radius, 12);
        ed.Undo(); Assert.IsType<Circle>(d.Find(h));
        ed.Trim(h, new V3(0, -50)); a = Assert.IsType<Arc>(d.Find(h)); Assert.Equal(0, a.StartDeg, 9); Assert.Equal(180, a.EndDeg, 9);
        // one crossing point only (tangent line) → a closed object stays
        var (d2, ed2) = New(); string h2 = H(ed2.AddCircle(0, 0, 50, "0")); ed2.AddLine(-100, 50, 100, 50, "0"); ed2.Trim(h2, new V3(0, -50)); Assert.IsType<Circle>(d2.Find(h2));
    }

    [Fact]
    public void Trim_Arc_ByLineAndCircle()
    {
        var (d, ed) = New(); string h = H(ed.AddArc(0, 0, 50, 0, 180, "0")); ed.AddLine(0, 0, 0, 100, "0");
        ed.Trim(h, new V3(-35, 35)); var a = (Arc)d.Find(h)!; Assert.Equal(0, a.StartDeg, 9); Assert.Equal(90, a.EndDeg, 9);
        // arc ∩ circle: circle r 50 at (50,0) meets the arc's circle at 60°
        var (d2, ed2) = New(); string h2 = H(ed2.AddArc(0, 0, 50, 0, 180, "0")); ed2.AddCircle(50, 0, 50, "0");
        ed2.Trim(h2, new V3(50 * Math.Cos(0.3), 50 * Math.Sin(0.3))); var a2 = (Arc)d2.Find(h2)!; Assert.Equal(60, a2.StartDeg, 9); Assert.Equal(180, a2.EndDeg, 9);
    }

    [Fact]
    public void Trim_ClosedPolyline_OpensIt_AndBulgeSegmentsStayOnTheirCircle()
    {
        var (d, ed) = New(); string h = H(ed.AddPolyline(Shapes.Rectangle(0, 0, 100, 50), true, "0")); ed.AddLine(30, -10, 30, 60, "0"); ed.AddLine(70, -10, 70, 60, "0");
        ed.Trim(h, new V3(50, 0));   // the bottom edge between x = 30 and 70
        var p = (LwPolyline)d.Find(h)!; Assert.False(p.Closed);
        var pts = p.Vertices.Select(v => (v.X, v.Y)).ToList();
        Assert.Equal(new[] { (70.0, 0.0), (100.0, 0.0), (100.0, 50.0), (0.0, 50.0), (0.0, 0.0), (30.0, 0.0) }, pts.Select(q => (Math.Round(q.X, 9), Math.Round(q.Y, 9))).ToArray());
        ed.Trim(h, new V3(50, 50));  // now open: the top edge between 30 and 70 → two polylines
        var polys = d.Entities.OfType<LwPolyline>().ToList(); Assert.Equal(2, polys.Count);
        Assert.Equal(4, polys[0].Vertices.Count); Assert.Equal(4, polys[1].Vertices.Count);
        Assert.True(Math.Abs(polys[0].Vertices[^1].X - 70) < Tol && Math.Abs(polys[0].Vertices[^1].Y - 50) < Tol);
        Assert.True(Math.Abs(polys[1].Vertices[0].X - 30) < Tol && Math.Abs(polys[1].Vertices[0].Y - 50) < Tol);

        // a half-circle bulge segment (0,0) → (100,0), bulge 1 = counter-clockwise below the chord? centre (50,0), r 50; cut by x = 50 … and by y = -25
        var (d2, ed2) = New(); var pl = new LwPolyline(); pl.Vertices.Add(new PVertex { X = 0, Y = 0, Bulge = 1 }); pl.Vertices.Add(new PVertex { X = 100, Y = 0, Bulge = 0 }); pl.Vertices.Add(new PVertex { X = 100, Y = 80 });
        string h2 = H(ed2.Add(pl, "0")); ed2.AddLine(50, -100, 50, 100, "0");
        ed2.Trim(h2, new V3(15, -35));   // the first quarter of the half circle
        var q2 = (LwPolyline)d2.Find(h2)!; Assert.Equal(3, q2.Vertices.Count);
        Assert.True(Math.Abs(q2.Vertices[0].X - 50) < Tol && Math.Abs(q2.Vertices[0].Y + 50) < Tol, $"{q2.Vertices[0].X},{q2.Vertices[0].Y}");
        Assert.Equal(Math.Tan(Math.PI / 8), q2.Vertices[0].Bulge, 9);   // a quarter circle
        var pr = Prim.Bulged(q2.Vertices[0].X, q2.Vertices[0].Y, q2.Vertices[1].X, q2.Vertices[1].Y, q2.Vertices[0].Bulge);
        Assert.True(Math.Abs(pr.Cx - 50) < 1e-7 && Math.Abs(pr.Cy) < 1e-7 && Math.Abs(pr.R - 50) < 1e-7, $"{pr.Cx},{pr.Cy} r {pr.R}");
        // clockwise bulge
        var (d3, ed3) = New(); var pc = new LwPolyline(); pc.Vertices.Add(new PVertex { X = 0, Y = 0, Bulge = -1 }); pc.Vertices.Add(new PVertex { X = 100, Y = 0 });
        string h3 = H(ed3.Add(pc, "0")); ed3.AddLine(50, -100, 50, 100, "0"); ed3.Trim(h3, new V3(85, 35));
        var q3 = (LwPolyline)d3.Find(h3)!; Assert.Equal(2, q3.Vertices.Count);
        Assert.True(Math.Abs(q3.Vertices[1].X - 50) < Tol && Math.Abs(q3.Vertices[1].Y - 50) < Tol, $"{q3.Vertices[1].X},{q3.Vertices[1].Y}"); Assert.Equal(-Math.Tan(Math.PI / 8), q3.Vertices[0].Bulge, 9);
    }

    [Fact]
    public void Extend_Line_Arc_Polyline()
    {
        var (d, ed) = New(); string h = H(ed.AddLine(0, 0, 50, 0, "0")); ed.AddLine(80, -10, 80, 10, "0"); ed.AddLine(120, -10, 120, 10, "0"); ed.AddLine(-40, -10, -40, 10, "0");
        ed.Extend(h, new V3(45, 0)); var l = (Line)d.Find(h)!; Assert.True(Near(l.P1, 80, 0) && Near(l.P0, 0, 0), $"{l.P0} {l.P1}");   // the nearest boundary, not the far one
        ed.Extend(h, new V3(78, 0)); l = (Line)d.Find(h)!; Assert.True(Near(l.P1, 120, 0));
        ed.Extend(h, new V3(2, 0)); l = (Line)d.Find(h)!; Assert.True(Near(l.P0, -40, 0));
        string before = Snap(d); var msgs = new List<string>(); ed.TrimExtend(new[] { (h, new V3(119, 0)) }, null, true, msgs); Assert.Equal(before, Snap(d)); Assert.Single(msgs);   // nothing further out
        ed.Undo(); ed.Undo(); ed.Undo(); l = (Line)d.Find(h)!; Assert.True(Near(l.P0, 0, 0) && Near(l.P1, 50, 0));
        // line to a circle: the near crossing
        var (d1, ed1) = New(); string h1 = H(ed1.AddLine(0, 0, 10, 0, "0")); ed1.AddCircle(100, 0, 30, "0"); ed1.Extend(h1, new V3(9, 0)); Assert.True(Near(((Line)d1.Find(h1)!).P1, 70, 0));
        // a boundary that the line would only reach on its extension does not count (no implied edge)
        var (d4, ed4) = New(); string h4 = H(ed4.AddLine(0, 0, 10, 0, "0")); ed4.AddLine(50, 5, 50, 20, "0"); ed4.Extend(h4, new V3(9, 0)); Assert.True(Near(((Line)d4.Find(h4)!).P1, 10, 0));
        // arc 0° → 90° extended at its end to the line x = -30 (angle acos(-0.6))
        var (d2, ed2) = New(); string h2 = H(ed2.AddArc(0, 0, 50, 0, 90, "0")); ed2.AddLine(-30, -100, -30, 100, "0");
        ed2.Extend(h2, new V3(5, 49.7)); var a = (Arc)d2.Find(h2)!; Assert.Equal(Math.Acos(-0.6) * 180 / Math.PI, a.EndDeg, 9); Assert.Equal(0, a.StartDeg, 9);
        ed2.Extend(h2, new V3(49.9, 3)); a = (Arc)d2.Find(h2)!; Assert.Equal(360 - Math.Acos(-0.6) * 180 / Math.PI, a.StartDeg, 9);
        // open polyline: last straight segment, then a first bulge segment
        var (d3, ed3) = New(); var pl = new LwPolyline(); pl.Vertices.Add(new PVertex { X = 0, Y = 50, Bulge = Math.Tan(Math.PI / 8) }); pl.Vertices.Add(new PVertex { X = 50, Y = 0 }); pl.Vertices.Add(new PVertex { X = 80, Y = 0 });
        // first segment: quarter circle, centre (50,50), from 180° to 270° counter-clockwise
        string h3 = H(ed3.Add(pl, "0")); ed3.AddLine(100, -10, 100, 10, "0"); ed3.AddLine(-10, 80, 110, 80, "0");
        ed3.Extend(h3, new V3(79, 0)); var p = (LwPolyline)d3.Find(h3)!; Assert.True(Math.Abs(p.Vertices[2].X - 100) < Tol && Math.Abs(p.Vertices[2].Y) < Tol);
        ed3.Extend(h3, new V3(1, 52)); p = (LwPolyline)d3.Find(h3)!;
        Assert.True(Math.Abs(p.Vertices[0].X - 10) < 1e-7 && Math.Abs(p.Vertices[0].Y - 80) < 1e-7, $"{p.Vertices[0].X},{p.Vertices[0].Y}");   // circle (50,50) r 50 meets y = 80 at x = 10
        var pr = Prim.Bulged(p.Vertices[0].X, p.Vertices[0].Y, p.Vertices[1].X, p.Vertices[1].Y, p.Vertices[0].Bulge);
        Assert.True(Math.Abs(pr.Cx - 50) < 1e-7 && Math.Abs(pr.Cy - 50) < 1e-7 && Math.Abs(pr.R - 50) < 1e-7, $"{pr.Cx},{pr.Cy} r {pr.R}");
    }

    [Fact]
    public void Trim_SeveralPicks_AreOneUndoStep_AndPreviewMatches()
    {
        var (d, ed) = New(); string h = H(ed.AddLine(0, 0, 100, 0, "0")); for (int x = 20; x <= 80; x += 20) ed.AddLine(x, -10, x, 10, "0");
        string before = Snap(d);
        var pv = ed.PreviewTrim(h, new V3(30, 0), null, false)!; Assert.True(pv.Ok);
        Assert.Equal(20, pv.Preview[0], 9); Assert.Equal(40, pv.Preview[^2], 9); Assert.Equal(before, Snap(d));   // preview does not edit
        ed.TrimExtend(new[] { (h, new V3(30, 0)), (h, new V3(70, 0)) }, null, false);   // the second pick lands on the piece split off by the first
        var parts = d.Entities.OfType<Line>().Where(l => Math.Abs(l.P0.Y) < Tol && Math.Abs(l.P1.Y) < Tol).OrderBy(l => l.P0.X).ToList();
        Assert.Equal(new[] { (0.0, 20.0), (40.0, 60.0), (80.0, 100.0) }, parts.Select(l => (Math.Round(l.P0.X, 9), Math.Round(l.P1.X, 9))).ToArray());
        ed.Undo(); Assert.Equal(before, Snap(d)); ed.Redo(); Assert.Equal(7, d.Entities.Count);
        // explicit cutting edges: only the line at x = 20 cuts
        var (d2, ed2) = New(); string h2 = H(ed2.AddLine(0, 0, 100, 0, "0")); string e20 = H(ed2.AddLine(20, -10, 20, 10, "0")); ed2.AddLine(60, -10, 60, 10, "0");
        ed2.Trim(h2, new V3(90, 0), new[] { e20 }); var l2 = (Line)d2.Find(h2)!; Assert.True(Near(l2.P0, 0, 0) && Near(l2.P1, 20, 0));
        // hidden layers do not cut
        var (d3, ed3) = New(); ed3.AddLayer("off"); string h3 = H(ed3.AddLine(0, 0, 100, 0, "0")); ed3.AddLine(50, -10, 50, 10, "off"); ed3.SetLayerState("off", off: true);
        string b3 = Snap(d3); ed3.Trim(h3, new V3(80, 0)); Assert.Equal(b3, Snap(d3));
    }

    /// <summary>A drawing with trimmed and extended objects (circle → arc, split line, opened polyline) written as DXF and as DWG: re-read
    /// gives the same geometry, and DWG TrueView opens both without a complaint (skipped when it is not installed).</summary>
    [Fact]
    public void Gate_D5g_TrimmedDrawing_SavesAndOpens()
    {
        var (d, ed) = New();
        string hl = H(ed.AddLine(0, 0, 200, 0, "0")); ed.AddLine(60, -20, 60, 80, "0"); ed.AddLine(140, -20, 140, 80, "0");
        string hc = H(ed.AddCircle(100, 30, 25, "0")); string hr = H(ed.AddPolyline(Shapes.Rectangle(20, 40, 180, 70), true, "0")); string hs = H(ed.AddLine(100, -60, 100, -40, "0"));
        ed.TrimExtend(new[] { (hl, new V3(100, 0)), (hc, new V3(100, 55)), (hr, new V3(100, 40)) }, null, false);
        ed.Extend(hs, new V3(100, -41));
        Assert.IsType<Arc>(d.Find(hc)); Assert.False(((LwPolyline)d.Find(hr)!).Closed); Assert.True(Near(((Line)d.Find(hs)!).P1, 100, 0) || Near(((Line)d.Find(hs)!).P1, 100, 5), ((Line)d.Find(hs)!).P1.ToString());
        string dir = Samples.WorkDir("d5g"); string dxf = Path.Combine(dir, "trimmed.dxf"), tmp = Path.Combine(dir, "trimmed.fordwg.dxf"), dwg = Path.Combine(dir, "trimmed.dwg");
        var (mode, _) = DxfSave.Choose(d, DxfSave.Ver.Auto); DxfWriter.Write(d, dxf, mode, FontCompat.Plan(d), false, mode == DxfWriter.Mode.R2000);
        var back = DxfReader.Read(dxf); Assert.Equal(Snap(d).Split('|').Select(s => s[(s.IndexOf(':') + 1)..]).OrderBy(s => s), Snap(back).Split('|').Select(s => s[(s.IndexOf(':') + 1)..]).OrderBy(s => s));
        var lines = new List<string> { $"# draft5g trimmed drawing: {d.Entities.Count} entities → DXF ({mode}) / DWG" };
        string? td = D5fTests.TrueViewOpen(dxf), tw = null; string conv = "dxf2dwg SKIP";
        DxfWriter.Write(d, tmp, DxfWriter.Mode.R2000, FontCompat.Plan(d));
        if (LibreDwg.Available) { var r = LibreDwg.DxfToDwg(tmp, dwg); conv = r.Ok ? "dxf2dwg ok" : "dxf2dwg FAIL"; Assert.True(r.Ok); tw = D5fTests.TrueViewOpen(dwg); }
        string T(string? t) => t == null ? "TrueView SKIP" : t == "" ? "TrueView clean" : "TrueView: " + t;
        bool ok = string.IsNullOrEmpty(td) && string.IsNullOrEmpty(tw);
        lines.Add($"{(ok ? "PASS" : "FAIL")}\tDXF re-read identical, {T(td)}\tDWG {conv}, {T(tw)}");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d5g_trim.txt"), lines, new UTF8Encoding(true)); foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
