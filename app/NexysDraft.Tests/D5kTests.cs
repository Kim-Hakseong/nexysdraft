using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using Xunit;

namespace NexysDraft.Tests;

/// <summary>draft5k: (1) the model view written to a saved file. A view made from stale header extents showed the drawing as a dot in nanoCAD and
/// DWG TrueView ("the file does not open"). The view the file came with is kept (centre + target), unless it shows none of the geometry.
/// (2) grip edits: the end of a line is dragged shorter / longer, the middle moves it, a circle quadrant changes the radius.</summary>
public class D5kTests
{
    static (double cx, double cy, double h, double a, double tx, double ty) ViewOf(string dxf)
    {
        var d = DxfReader.Read(dxf); var rec = d.RawTables.First(t => t.Name.Equals("VPORT", StringComparison.OrdinalIgnoreCase)).Entries.First();
        double F(int c) => DxfCodec.ParseDouble(rec.First(g => g.Code == c).Value);
        return (F(12), F(22), F(40), F(41), F(17), F(27));
    }
    static Document WithView(string name, double cx, double cy, double h, double a, double tx, double ty, bool staleHeader = true)
    {
        var d = Document.CreateNew(); var ed = new Editor(d);
        ed.AddLine(1000, 500, 1400, 500, "0"); ed.AddLine(1000, 500, 1000, 700, "0"); ed.AddCircle(1200, 600, 50, "0");   // the drawing: 1000…1400 × 500…700
        if (staleHeader) { d.Header["$EXTMIN"] = "-90000 -50000 0"; d.Header["$EXTMAX"] = "90000 50000 0"; }                // what real files carry: extents of long-deleted objects
        var vp = new RawTable { Name = "VPORT" };
        vp.Entries.Add(new List<Group> { new(0, "VPORT"), new(2, "*Active"), new(70, "0"), new(12, cx.ToString("R")), new(22, cy.ToString("R")), new(16, "0.0"), new(26, "0.0"), new(36, "1.0"), new(17, tx.ToString("R")), new(27, ty.ToString("R")), new(37, "0.0"), new(40, h.ToString("R")), new(41, a.ToString("R")), new(51, "0.0") });
        d.RawTables.RemoveAll(t => t.Name.Equals("VPORT", StringComparison.OrdinalIgnoreCase)); d.RawTables.Add(vp);
        string f = Path.Combine(Samples.WorkDir("d5k"), name + ".dxf"); DxfWriter.Write(d, f, DxfWriter.Mode.R2000, FontCompat.Plan(d), false, true); d.Path = f; return d;
    }

    [Fact]
    public void SavedView_KeepsTheViewOfTheFile_CentrePlusTarget()
    {
        // centre given from a target point, as AutoCAD 2018 files have it (u04): −300,−200 from the target 1500,800 = 1200,600
        var d = WithView("kept", -300, -200, 300, 2.0, 1500, 800);
        var v = ViewOf(d.Path);
        Assert.Equal(1200, v.cx, 6); Assert.Equal(600, v.cy, 6); Assert.Equal(300, v.h, 6); Assert.Equal(2.0, v.a, 6); Assert.Equal(0, v.tx, 9); Assert.Equal(0, v.ty, 9);
    }

    [Fact]
    public void SavedView_ThatShowsNothing_IsReplacedByTheGeometry_NotByTheStaleHeader()
    {
        var d = WithView("off", -50000, -40000, 300, 2.0, 0, 0);   // looks at empty space
        var v = ViewOf(d.Path);
        Assert.InRange(v.cx, 1150, 1250); Assert.InRange(v.cy, 550, 650);
        Assert.InRange(v.h, 200, 400);   // frames the 400 × 200 drawing; from the header extents it would have been 110 000
    }

    [Fact]
    public void NewDrawing_ViewFramesWhatWasDrawn()
    {
        var d = Document.CreateNew(); var ed = new Editor(d); ed.AddLine(0, 0, 100, 100, "0"); ed.AddCircle(50, 50, 20, "0");
        string f = Path.Combine(Samples.WorkDir("d5k"), "new.dxf"); DxfWriter.Write(d, f, DxfWriter.Mode.R2000, FontCompat.Plan(d), false, true);
        var v = ViewOf(f); Assert.Equal(50, v.cx, 6); Assert.Equal(50, v.cy, 6); Assert.InRange(v.h, 100, 120);
    }

    [Fact]
    public void Grip_LineEndStretches_MiddleMoves_CircleQuadrantSetsRadius_ArcEndTurns()
    {
        var d = Document.CreateNew(); var ed = new Editor(d);
        string hl = ed.AddLine(0, 0, 100, 0, "0").Added[0].e.Handle; string hc = ed.AddCircle(50, 50, 10, "0").Added[0].e.Handle;
        Line L() => (Line)d.Find(hl)!; Circle C() => (Circle)d.Find(hc)!;
        Assert.NotNull(ed.MoveGrip(hl, new V3(100, 0), new V3(60, 0), 0.5)); Assert.Equal(0, L().P0.X, 9); Assert.Equal(60, L().P1.X, 9);                    // shorter
        Assert.NotNull(ed.MoveGrip(hl, new V3(0, 0), new V3(-20, 5), 0.5)); Assert.Equal(-20, L().P0.X, 9); Assert.Equal(5, L().P0.Y, 9); Assert.Equal(60, L().P1.X, 9);   // longer, other end
        ed.Undo(); Assert.Equal(0, L().P0.X, 9); Assert.Equal(60, L().P1.X, 9);
        Assert.NotNull(ed.MoveGrip(hl, new V3(30, 0), new V3(30, 10), 0.5)); Assert.Equal(10, L().P0.Y, 9); Assert.Equal(10, L().P1.Y, 9); Assert.Equal(60, L().P1.X - L().P0.X, 9);   // middle: moved, same length
        Assert.NotNull(ed.MoveGrip(hc, new V3(60, 50), new V3(75, 50), 0.5)); Assert.Equal(25, C().Radius, 9); Assert.Equal(50, C().Center.X, 9);
        Assert.NotNull(ed.MoveGrip(hc, new V3(50, 50), new V3(40, 40), 0.5)); Assert.Equal(40, C().Center.X, 9); Assert.Equal(25, C().Radius, 9);
        string hp = ed.AddPolyline(Shapes.Rectangle(0, 100, 50, 130), true, "0").Added[0].e.Handle;
        Assert.NotNull(ed.MoveGrip(hp, new V3(50, 130), new V3(70, 150), 0.5));
        var p = (LwPolyline)d.Find(hp)!; Assert.Contains(p.Vertices, v => Math.Abs(v.X - 70) < 1e-9 && Math.Abs(v.Y - 150) < 1e-9); Assert.Equal(4, p.Vertices.Count);
        Assert.Null(ed.MoveGrip("NOPE", new V3(0, 0), new V3(1, 1), 0.5));
    }
}
