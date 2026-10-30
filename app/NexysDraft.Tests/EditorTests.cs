using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using Xunit;

namespace NexysDraft.Tests;

public class EditorTests
{
    static (Document d, Editor ed) Make()
    {
        var d = Samples.Basic(); return (d, new Editor(d));
    }
    static Line L(Document d) => d.Entities.OfType<Line>().First();

    [Fact]
    public void MoveUndoRedo()
    {
        var (d, ed) = Make(); var l = L(d); string h = l.Handle;
        ed.Move(new[] { h }, 10, 5);
        var m = (Line)d.Find(h)!; Assert.Equal(10, m.P0.X, 9); Assert.Equal(5, m.P0.Y, 9); Assert.Equal(110, m.P1.X, 9);
        ed.Undo(); Assert.Equal(0, ((Line)d.Find(h)!).P0.X, 9);
        ed.Redo(); Assert.Equal(10, ((Line)d.Find(h)!).P0.X, 9);
    }

    [Fact]
    public void CopyAddsNewHandles()
    {
        var (d, ed) = Make(); int n = d.Entities.Count; var ids = d.Entities.Take(3).Select(e => e.Handle).ToList();
        var c = ed.Copy(ids, 100, 0);
        Assert.Equal(n + 3, d.Entities.Count); Assert.Equal(3, c.Added.Count); Assert.All(c.Added, a => Assert.DoesNotContain(a.e.Handle, ids));
        ed.Undo(); Assert.Equal(n, d.Entities.Count);
    }

    [Fact]
    public void RotateArcKeepsSweep()
    {
        var (d, ed) = Make(); var a = d.Entities.OfType<Arc>().First();
        ed.Rotate(new[] { a.Handle }, a.Center.X, a.Center.Y, 90);
        var r = (Arc)d.Find(a.Handle)!; Assert.Equal(120, r.StartDeg, 9); Assert.Equal(340, r.EndDeg, 9);
    }

    [Fact]
    public void MirrorArcReversesAndTextStaysReadable()
    {
        var (d, ed) = Make(); var a = d.Entities.OfType<Arc>().First(); var t = d.Entities.OfType<Text>().First();
        ed.Mirror(new[] { a.Handle, t.Handle }, 0, 0, 0, 1, keepOriginal: true);
        var ma = d.Entities.OfType<Arc>().Last(); Assert.Equal(-a.Center.X, ma.Center.X, 9); Assert.Equal(Arc.Norm(180 - a.EndDeg), ma.StartDeg, 9); Assert.Equal(Arc.Norm(180 - a.StartDeg), ma.EndDeg, 9);
        var mt = d.Entities.OfType<Text>().Last(); Assert.Equal(0, mt.Rotation, 9); Assert.True(mt.Pos.X < 0);
    }

    [Fact]
    public void OffsetLineAndCircle()
    {
        var (d, ed) = Make(); var l = L(d); var c = d.Entities.OfType<Circle>().First();
        ed.Offset(l.Handle, 2, new V3(0, 100)); var nl = d.Entities.OfType<Line>().Last();
        double dist = Math.Abs((nl.P0.X - l.P0.X) * (l.P1.Y - l.P0.Y) - (nl.P0.Y - l.P0.Y) * (l.P1.X - l.P0.X)) / Math.Sqrt(Math.Pow(l.P1.X - l.P0.X, 2) + Math.Pow(l.P1.Y - l.P0.Y, 2));
        Assert.Equal(2, dist, 9);
        ed.Offset(c.Handle, 3, new V3(c.Center.X + 100, c.Center.Y)); Assert.Equal(c.Radius + 3, d.Entities.OfType<Circle>().Last().Radius, 9);
        ed.Offset(c.Handle, 3, c.Center); Assert.Equal(c.Radius - 3, d.Entities.OfType<Circle>().Last().Radius, 9);
    }

    [Fact]
    public void StretchMovesOnlyGripsInWindow()
    {
        var (d, ed) = Make(); var l = L(d);   // (0,0)-(100,50)
        ed.Stretch(new[] { l.Handle }, 90, 40, 110, 60, 5, 5);
        var s = (Line)d.Find(l.Handle)!; Assert.Equal(0, s.P0.X, 9); Assert.Equal(105, s.P1.X, 9); Assert.Equal(55, s.P1.Y, 9);
    }

    [Fact]
    public void TextEditAndLayerOps()
    {
        var (d, ed) = Make(); var t = d.Entities.OfType<Text>().First();
        ed.SetText(t.Handle, "CHANGED"); Assert.Equal("CHANGED", ((Text)d.Find(t.Handle)!).Value);
        var m = d.Entities.OfType<MText>().First(); ed.SetMTextPlain(m.Handle, "plain\nline2"); Assert.Equal("plain\\Pline2", ((MText)d.Find(m.Handle)!).Value);
        ed.AddLayer("NEW", 5); Assert.NotNull(d.LayerOf("NEW"));
        ed.SetLayer(new[] { t.Handle }, "NEW"); Assert.Equal("NEW", d.Find(t.Handle)!.Layer);
        ed.SetLayerState("NEW", off: true, frozen: true); Assert.True(d.LayerOf("NEW")!.Off); Assert.True(d.LayerOf("NEW")!.Frozen);
        ed.Undo(); Assert.False(d.LayerOf("NEW")!.Off);
        ed.Undo(); Assert.Equal("TEXT", d.Find(t.Handle)!.Layer);
        ed.Undo(); Assert.Null(d.LayerOf("NEW"));
    }

    [Fact]
    public void LinearDimensionBuildsBlockAndSurvivesWriter()
    {
        var (d, ed) = Make();
        ed.AddLinearDimension(new V3(0, 0), new V3(100, 0), new V3(50, 20), 0, "DIM");
        var dim = d.Entities.OfType<Dimension>().Single(); Assert.True(dim.HasMeasurement); Assert.Equal(100, dim.Measurement, 9);
        var blk = d.Blocks[dim.Block]; Assert.Contains(blk.Entities, e => e is Text t && t.Value == "100"); Assert.Equal(3, blk.Entities.OfType<Line>().Count()); Assert.Equal(2, blk.Entities.OfType<Solid>().Count());
        string p = Path.Combine(Samples.WorkDir("rt"), "editor_dim.dxf"); DxfWriter.Write(d, p, DxfWriter.Mode.R2000);
        var back = DxfReader.Read(p); var bd = back.Entities.OfType<Dimension>().Single(); Assert.Equal(dim.Block, bd.Block); Assert.True(back.Blocks.ContainsKey(dim.Block));
        ed.Move(new[] { dim.Handle }, 0, 10); Assert.Equal(10, ((Dimension)d.Find(dim.Handle)!).P13.Y, 9); Assert.Contains(d.Blocks[dim.Block].Entities, e => e is Text t && Math.Abs(t.Pos.Y - (dim.TextMid.Y + 10)) < 1e-9);
    }

    [Fact]
    public void PreserveWriterKeepsRawForUntouchedAndRegeneratesDirty()
    {
        var (d, ed) = Make();
        string p1 = Path.Combine(Samples.WorkDir("rt"), "editor_a.dxf"); DxfWriter.Write(d, p1, DxfWriter.Mode.R2000);
        var r = DxfReader.Read(p1); var e2 = new Editor(r); var l = r.Entities.OfType<Line>().First();
        e2.Move(new[] { l.Handle }, 1, 1);
        string p2 = Path.Combine(Samples.WorkDir("rt"), "editor_b.dxf"); DxfWriter.Write(r, p2, DxfWriter.Mode.Preserve);
        var r2 = DxfReader.Read(p2); Assert.Equal(r.Entities.Count, r2.Entities.Count); Assert.Equal(1, ((Line)r2.Find(l.Handle)!).P0.X, 9);
        var cmp = Compare.Documents(r, r2, 1e-9, Compare.CoordTypes, Compare.CountTypes); Assert.True(cmp.Ok, string.Join("\n", cmp.Mismatches));
    }
}
