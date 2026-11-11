using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft5h: what the command line lacked for a proper drawing — linetypes on layers / entities, diameter and radius dimensions
/// (and a text override for linear ones), hatches with pattern lines. Each is written to DXF and DWG, read back, and opened by DWG TrueView.</summary>
public class D5hTests
{
    readonly ITestOutputHelper _o;
    public D5hTests(ITestOutputHelper o) { _o = o; }
    static (Document d, Editor ed) New() { var d = Document.CreateNew(); return (d, new Editor(d)); }

    [Fact]
    public void Linetype_OnLayerAndEntity_SurvivesDxf()
    {
        var (d, ed) = New();
        ed.AddLayerWithLinetype("CEN", 1, "center2"); ed.AddLayerWithLinetype("HID", 6, "HIDDEN2");
        Assert.Equal("CENTER2", d.LayerOf("CEN")!.Linetype); Assert.Contains(d.Linetypes, l => l.Name == "CENTER2" && l.Pattern.Count == 4);
        string h = ed.AddLine(0, 0, 100, 0, "0").Added[0].e.Handle; ed.SetLinetype(new[] { h }, "PHANTOM"); Assert.Equal("PHANTOM", d.Find(h)!.Linetype);
        ed.Undo(); Assert.Equal("BYLAYER", d.Find(h)!.Linetype); ed.Redo();
        Assert.Throws<ArgumentException>(() => ed.EnsureLinetype("NOPE"));
        int n = d.Linetypes.Count; ed.EnsureLinetype("center2"); Assert.Equal(n, d.Linetypes.Count);   // no duplicate
        string f = Path.Combine(Samples.WorkDir("d5h"), "lt.dxf"); DxfWriter.Write(d, f, DxfWriter.Mode.R2000, FontCompat.Plan(d), false, true);
        var b = DxfReader.Read(f);
        Assert.Equal("CENTER2", b.LayerOf("CEN")!.Linetype); Assert.Equal("HIDDEN2", b.LayerOf("HID")!.Linetype); Assert.Equal("PHANTOM", b.Entities.OfType<Line>().Single().Linetype);
        var c2 = b.Linetypes.Single(l => l.Name == "CENTER2"); Assert.Equal(new[] { 19.05, -3.175, 3.175, -3.175 }, c2.Pattern);
    }

    [Fact]
    public void RadialDimensions_AndTextOverride()
    {
        var (d, ed) = New();
        ed.AddRadialDimension(100, 50, 30, 0, true, "0"); var dia = d.Entities.OfType<Dimension>().Last();
        Assert.Equal(3, dia.Kind); Assert.Equal(60, dia.Measurement, 9); Assert.Equal(70, dia.DefPoint.X, 9); Assert.Equal(130, dia.P15.X, 9); Assert.Equal(50, dia.P15.Y, 9);
        var blk = d.Blocks[dia.Block]; Assert.Equal("Ø60", blk.Entities.OfType<Text>().Single().Value); Assert.Equal(2, blk.Entities.OfType<Solid>().Count());
        ed.AddRadialDimension(0, 0, 10, 90, false, "0"); var rad = d.Entities.OfType<Dimension>().Last();
        Assert.Equal(4, rad.Kind); Assert.Equal(10, rad.Measurement, 9); Assert.Equal(0, rad.DefPoint.X, 9); Assert.Equal(10, rad.P15.Y, 9);
        Assert.Equal("R10", d.Blocks[rad.Block].Entities.OfType<Text>().Single().Value); Assert.Single(d.Blocks[rad.Block].Entities.OfType<Solid>());
        ed.AddLinearDimension(new V3(0, 0), new V3(0, 60), new V3(-20, 30), 90, "0", "Ø<> H7"); var lin = d.Entities.OfType<Dimension>().Last();
        Assert.Equal("Ø60 H7", d.Blocks[lin.Block].Entities.OfType<Text>().Single().Value);
        ed.AddRadialDimension(0, 0, 12.5, 30, true, "0", "4-Ø<> 드릴"); Assert.Equal("4-Ø25 드릴", d.Blocks[d.Entities.OfType<Dimension>().Last().Block].Entities.OfType<Text>().Single().Value);
        string f = Path.Combine(Samples.WorkDir("d5h"), "dim.dxf"); DxfWriter.Write(d, f, DxfWriter.Mode.R2000, FontCompat.Plan(d), false, true);
        var b = DxfReader.Read(f); var kinds = b.Entities.OfType<Dimension>().Select(x => x.Kind).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 0, 3, 3, 4 }, kinds); Assert.Equal(60, b.Entities.OfType<Dimension>().First(x => x.Kind == 3).Measurement, 9);
    }

    [Fact]
    public void Hatch_PatternLines_StayInsideTheBoundary_AndSkipIslands()
    {
        var (d, ed) = New();
        var sq = new List<PVertex> { new() { X = 0, Y = 0 }, new() { X = 100, Y = 0 }, new() { X = 100, Y = 100 }, new() { X = 0, Y = 100 } };
        ed.AddHatch("ANSI31", 1, 0, new[] { sq }, "0"); var h = d.Entities.OfType<Hatch>().Single();
        var ln = HatchPattern.Lines(h); int n = ln.Count / 4;
        // 45° lines 3.175 apart across a 100 square: the diagonal span is 100·√2 → 44 or 45 lines
        Assert.InRange(n, 43, 46);
        for (int i = 0; i < ln.Count; i += 4)
        {
            Assert.InRange(ln[i], -1e-9, 100 + 1e-9); Assert.InRange(ln[i + 1], -1e-9, 100 + 1e-9); Assert.InRange(ln[i + 2], -1e-9, 100 + 1e-9); Assert.InRange(ln[i + 3], -1e-9, 100 + 1e-9);
            Assert.Equal(1, (ln[i + 3] - ln[i + 1]) / (ln[i + 2] - ln[i]), 9);   // slope 1
        }
        // an island: no line passes through the circle (centre 50,50, r 20)
        ed.AddHatch("ANSI31", 1, 0, new[] { sq, Editor.CircleLoop(50, 50, 20) }, "0"); var h2 = d.Entities.OfType<Hatch>().Last(); var l2 = HatchPattern.Lines(h2);
        Assert.True(l2.Count / 4 > n);
        for (int i = 0; i < l2.Count; i += 4) { double mx = (l2[i] + l2[i + 2]) / 2, my = (l2[i + 1] + l2[i + 3]) / 2; Assert.True(Math.Sqrt((mx - 50) * (mx - 50) + (my - 50) * (my - 50)) > 19.5, $"midpoint ({mx},{my}) inside the island"); }
        // scale 2 halves the count; solid has no lines; unknown pattern is refused
        ed.AddHatch("ANSI31", 2, 0, new[] { sq }, "0"); Assert.InRange(HatchPattern.Lines(d.Entities.OfType<Hatch>().Last()).Count / 4, 21, 23);
        ed.AddHatch("SOLID", 1, 0, new[] { sq }, "0"); Assert.Empty(HatchPattern.Lines(d.Entities.OfType<Hatch>().Last())); Assert.True(d.Entities.OfType<Hatch>().Last().Solid);
        Assert.Throws<ArgumentException>(() => ed.AddHatch("BRICKS9", 1, 0, new[] { sq }, "0"));
        // DXF round trip keeps pattern, loops and the definition lines
        string f = Path.Combine(Samples.WorkDir("d5h"), "hatch.dxf"); DxfWriter.Write(d, f, DxfWriter.Mode.R2000, FontCompat.Plan(d), false, true);
        var b = DxfReader.Read(f); var hb = b.Entities.OfType<Hatch>().ToList();
        Assert.Equal(4, hb.Count); Assert.Equal("ANSI31", hb[0].Pattern); Assert.False(hb[0].Solid); Assert.Equal(2, hb[1].Loops.Count); Assert.True(hb[3].Solid);
        Assert.Equal(n, HatchPattern.Lines(hb[0]).Count / 4); Assert.Equal(l2.Count, HatchPattern.Lines(hb[1]).Count);
    }

    /// <summary>A drawing that uses all three (centre / hidden linetypes, Ø and R dimensions, pattern + solid hatches with an island): DXF and
    /// DWG saves open in DWG TrueView without a report (skipped when it is not installed), and the DWG read back keeps the objects.</summary>
    [Fact]
    public void Gate_D5h_LinetypesDimsHatch_SaveAndOpen()
    {
        var (d, ed) = New();
        ed.AddLayerWithLinetype("CEN", 1, "CENTER2"); ed.AddLayerWithLinetype("HID", 6, "HIDDEN2"); ed.AddLayer("DIM", 4); ed.AddLayer("HAT", 8);
        ed.AddPolyline(Shapes.Rectangle(0, 0, 120, 60), true, "0"); ed.AddCircle(40, 30, 15, "0"); ed.AddCircle(90, 30, 6, "0");
        ed.AddLine(-10, 30, 130, 30, "CEN"); ed.AddLine(0, 45, 120, 45, "HID");
        var sq = new List<PVertex> { new() { X = 0, Y = 0 }, new() { X = 120, Y = 0 }, new() { X = 120, Y = 60 }, new() { X = 0, Y = 60 } };
        ed.AddHatch("ANSI31", 1, 0, new[] { sq, Editor.CircleLoop(40, 30, 15), Editor.CircleLoop(90, 30, 6) }, "HAT");
        ed.AddHatch("SOLID", 1, 0, new[] { new List<PVertex> { new() { X = 150, Y = 0 }, new() { X = 170, Y = 0 }, new() { X = 170, Y = 20 }, new() { X = 150, Y = 20 } } }, "HAT");
        ed.AddHatch("ANSI37", 2, 15, new[] { Editor.CircleLoop(160, 45, 12) }, "HAT");
        ed.AddRadialDimension(40, 30, 15, 30, true, "DIM"); ed.AddRadialDimension(90, 30, 6, 60, false, "DIM");
        ed.AddLinearDimension(new V3(120, 0), new V3(120, 60), new V3(135, 30), 90, "DIM", "Ø<>");
        d.Header["$LTSCALE"] = "0.5";
        string dir = Samples.WorkDir("d5h"); string dxf = Path.Combine(dir, "all.dxf"), tmp = Path.Combine(dir, "all.fordwg.dxf"), dwg = Path.Combine(dir, "all.dwg");
        var (mode, _) = DxfSave.Choose(d, DxfSave.Ver.Auto); DxfWriter.Write(d, dxf, mode, FontCompat.Plan(d), false, mode == DxfWriter.Mode.R2000);
        var rules = D5fTests.Rules(dxf, true); rules.AddRange(D5dTests.Check(dxf).Select(i => i.Kind).Distinct());
        var lines = new List<string> { $"# draft5h: {d.Entities.Count} entities (linetypes CENTER2 / HIDDEN2, Ø and R dimensions, ANSI31 with two islands, SOLID, ANSI37) → DXF ({mode}) / DWG" };
        string? td = D5fTests.TrueViewOpen(dxf), tw = null; string conv = "dxf2dwg SKIP", back = "";
        DxfWriter.Write(d, tmp, DxfWriter.Mode.R2000, FontCompat.Plan(d));
        if (LibreDwg.Available)
        {
            var r = LibreDwg.DxfToDwg(tmp, dwg); conv = r.Ok ? "dxf2dwg ok" : "dxf2dwg FAIL"; Assert.True(r.Ok); tw = D5fTests.TrueViewOpen(dwg);
            string rt = Path.Combine(dir, "all.back.dxf"); var r2 = LibreDwg.DwgToDxf(dwg, rt); Assert.True(r2.Ok); var b = DxfReader.Read(rt);
            int hat = b.Entities.OfType<Hatch>().Count(), dim = b.Entities.OfType<Dimension>().Count(); string cen = b.LayerOf("CEN")?.Linetype ?? "?";
            back = $"DWG read back: HATCH {hat}, DIMENSION {dim} (kinds {string.Join("/", b.Entities.OfType<Dimension>().Select(x => x.Kind).OrderBy(x => x))}), layer CEN linetype {cen}";
            Assert.Equal(3, hat); Assert.Equal(3, dim); Assert.Equal("CENTER2", cen, ignoreCase: true);
        }
        string T(string? t) => t == null ? "TrueView SKIP" : t == "" ? "TrueView clean" : "TrueView: " + t;
        bool ok = rules.Count == 0 && string.IsNullOrEmpty(td) && string.IsNullOrEmpty(tw);
        lines.Add($"{(ok ? "PASS" : "FAIL")}\tDXF rules {(rules.Count == 0 ? "ok" : string.Join(", ", rules))}, {T(td)}\tDWG {conv}, {T(tw)}\t{back}");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d5h_draft.txt"), lines, new UTF8Encoding(true)); foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
