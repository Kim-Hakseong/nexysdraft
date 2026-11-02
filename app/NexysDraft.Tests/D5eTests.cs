using System.Text;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft5e: basic shapes (RECTANG, 3-point ARC, ELLIPSE, POLYGON, POINT, TEXT, MTEXT) — geometry, DXF round trip in both writer modes,
/// AutoCAD structure rules (D5dTests.Check) and ODA read.</summary>
public class D5eTests
{
    readonly ITestOutputHelper _o;
    public D5eTests(ITestOutputHelper o) { _o = o; }
    const double Tol = 1e-9;
    static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);

    [Fact]
    public void Shapes_Geometry()
    {
        var r = Shapes.Rectangle(30, 20, 0, 0);
        Assert.Equal(new[] { (0.0, 0.0), (30.0, 0.0), (30.0, 20.0), (0.0, 20.0) }, r);
        // arc through (10,0) (0,10) (-10,0): centre 0,0 r 10, counter-clockwise 0 → 180
        var a = Shapes.Arc3(10, 0, 0, 10, -10, 0)!.Value;
        Assert.True(Math.Abs(a.cx) < Tol && Math.Abs(a.cy) < Tol && Math.Abs(a.r - 10) < Tol && Math.Abs(a.a0) < Tol && Math.Abs(a.a1 - 180) < Tol, $"{a}");
        // same points picked clockwise (start left, middle on top, end right) → still the upper half: 0 → 180
        var b = Shapes.Arc3(-10, 0, 0, 10, 10, 0)!.Value;
        Assert.True(Math.Abs(b.a0) < Tol && Math.Abs(b.a1 - 180) < Tol, $"{b}");
        // lower half through (0,-10)
        var c = Shapes.Arc3(10, 0, 0, -10, -10, 0)!.Value;
        Assert.True(Math.Abs(c.a0 - 180) < Tol && Math.Abs(c.a1) < Tol, $"{c}");
        Assert.Null(Shapes.Arc3(0, 0, 1, 1, 2, 2));
        // hexagon inscribed r 10: every vertex at 10 from the centre; circumscribed: edge midpoints at 10
        var hx = Shapes.Polygon(6, 5, 5, 10, true, 0);
        Assert.All(hx, p => Assert.True(Math.Abs(Hyp(p.x - 5, p.y - 5) - 10) < Tol));
        var sq = Shapes.Polygon(4, 0, 0, 10, false, -90);
        for (int i = 0; i < 4; i++) { var p = sq[i]; var q = sq[(i + 1) % 4]; Assert.True(Math.Abs(Hyp((p.x + q.x) / 2, (p.y + q.y) / 2) - 10) < Tol); }
        Assert.True(Math.Abs(sq[0].y - sq[3].y) < Tol || Math.Abs(sq[0].y - sq[1].y) < Tol, "circumscribed square with angle -90 has a horizontal bottom edge");
        // ellipse axis (0,0)-(40,0), other half 10 → centre 20,0, major 20,0, ratio 0.5; other half 30 > 20 → major turns vertical
        var e = Shapes.EllipseByAxis(0, 0, 40, 0, 10)!.Value;
        Assert.True(Math.Abs(e.cx - 20) < Tol && Math.Abs(e.mx - 20) < Tol && Math.Abs(e.my) < Tol && Math.Abs(e.ratio - 0.5) < Tol, $"{e}");
        var e2 = Shapes.EllipseByAxis(0, 0, 40, 0, 30)!.Value;
        Assert.True(Math.Abs(e2.mx) < Tol && Math.Abs(Math.Abs(e2.my) - 30) < Tol && Math.Abs(e2.ratio - 20.0 / 30) < Tol, $"{e2}");
    }

    [Fact]
    public void Gate_D5e_BasicShapes_RoundTrip()
    {
        var lines = new List<string> { "# draft5e: basic shapes drawn through the Editor, saved as DXF (app default for a new drawing) and as the R2000 DXF of a DWG save — read back, AutoCAD structure rules, ODA read" };
        Document Build()
        {
            var d = Document.CreateNew(); var ed = new Editor(d);
            ed.AddPolyline(Shapes.Rectangle(0, 0, 100, 50), true, "0");
            var a = Shapes.Arc3(150, 0, 175, 25, 200, 0)!.Value; ed.AddArc(a.cx, a.cy, a.r, a.a0, a.a1, "0");
            var el = Shapes.EllipseByAxis(0, 100, 80, 100, 20)!.Value; ed.AddEllipse(el.cx, el.cy, el.mx, el.my, el.ratio, "0");
            ed.AddPolyline(Shapes.Polygon(6, 150, 100, 25, true, -90 - 30), true, "0");
            ed.AddPoint(250, 0, "0");
            ed.AddText(0, -30, 5, 0, "한글 텍스트 TEXT 1", "0");
            ed.AddMText(100, -30, 3.5, 80, "첫 줄\\P둘째 줄 MTEXT", "0");
            return d;
        }
        bool ok = true;
        foreach (var (label, chosen) in new[] { ("dxf", (DxfWriter.Mode?)null), ("r2000", DxfWriter.Mode.R2000) })
        {
            var d = Build(); var mode = chosen ?? DxfSave.Choose(d, DxfSave.Ver.Auto).mode;   // "dxf" = what the app picks for a new drawing
            string path = Path.Combine(Samples.WorkDir("d5e"), $"shapes_{label}.dxf");
            DxfWriter.Write(d, path, mode, null, false, mode == DxfWriter.Mode.R2000);
            var b = DxfReader.Read(path);
            var rect = b.Entities.OfType<LwPolyline>().FirstOrDefault(p => p.Vertices.Count == 4);
            var hex = b.Entities.OfType<LwPolyline>().FirstOrDefault(p => p.Vertices.Count == 6);
            var arc = b.Entities.OfType<Arc>().FirstOrDefault(); var ell = b.Entities.OfType<Ellipse>().FirstOrDefault(); var pt = b.Entities.OfType<Point>().FirstOrDefault();
            var tx = b.Entities.OfType<Text>().FirstOrDefault(t => !t.IsAttrib); var mt = b.Entities.OfType<MText>().FirstOrDefault();
            bool geo = rect != null && rect.Closed && Math.Abs(rect.Vertices[2].X - 100) < 1e-9 && Math.Abs(rect.Vertices[2].Y - 50) < 1e-9
                && arc != null && Math.Abs(arc.Center.X - 175) < 1e-9 && Math.Abs(arc.Center.Y) < 1e-9 && Math.Abs(arc.Radius - 25) < 1e-9 && Math.Abs(arc.StartDeg) < 1e-9 && Math.Abs(arc.EndDeg - 180) < 1e-9
                && ell != null && Math.Abs(ell.Center.X - 40) < 1e-9 && Math.Abs(ell.Ratio - 0.5) < 1e-9
                && hex != null && hex.Closed && hex.Vertices.All(v => Math.Abs(Hyp(v.X - 150, v.Y - 100) - 25) < 1e-6)
                && pt != null && Math.Abs(pt.Pos.X - 250) < 1e-9
                && tx != null && tx.Value == "한글 텍스트 TEXT 1" && Math.Abs(tx.Height - 5) < 1e-9
                && mt != null && mt.Value.Contains("둘째 줄") && Math.Abs(mt.Width - 80) < 1e-9;
            var issues = D5dTests.Check(path);
            string oda = "ODA SKIP"; bool odaOk = true;
            if (OdaGateTests.Converter() != null) { var (o, err, _) = OdaGateTests.Convert(path); odaOk = o != null && string.IsNullOrEmpty(err); oda = odaOk ? "ODA ok" : "ODA ERR " + err; if (o != null) { var c = DxfReader.Read(o); oda += $" (ODA read back: {c.Entities.Count(x => !x.PaperSpace)} entities)"; odaOk &= c.Entities.Count(x => !x.PaperSpace) >= 7; } }
            bool rowOk = geo && issues.Count == 0 && odaOk; ok &= rowOk;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{label}\tgeometry {(geo ? "ok" : "DIFF")}\tstructure {(issues.Count == 0 ? "0 issues" : string.Join("; ", issues.Take(4).Select(i => i.Kind + " " + i.Example)))}\t{oda}\tentities {string.Join(" ", b.Entities.Where(x => !x.PaperSpace).GroupBy(x => x.DxfName).Select(g => g.Key + " " + g.Count()))}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d5e_basic_shapes.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
