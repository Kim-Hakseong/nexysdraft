using System.Globalization;
using System.Text.RegularExpressions;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using NexysDraft.Core.Pdf;
using Xunit;

namespace NexysDraft.Tests;

/// <summary>draft5m: (1) monochrome PDF — no colour operator other than black; (2) a polyline with width (the thick frame of real drawings) keeps its
/// width through DXF and is printed at that width; a tapering one is filled.</summary>
public class D5mTests
{
    static Document Sample()
    {
        var d = Document.CreateNew(); var ed = new Editor(d);
        ed.AddLayer("RED", 1); ed.AddLayer("BLUE", 5);
        var frame = new LwPolyline { Closed = true, ConstWidth = 6 };
        foreach (var (x, y) in new[] { (0.0, 0.0), (400.0, 0.0), (400.0, 277.0), (0.0, 277.0) }) frame.Vertices.Add(new PVertex { X = x, Y = y, StartWidth = 6, EndWidth = 6 });
        ed.Add(frame, "0");
        var arrow = new LwPolyline(); arrow.Vertices.Add(new PVertex { X = 50, Y = 50, StartWidth = 0, EndWidth = 20 }); arrow.Vertices.Add(new PVertex { X = 90, Y = 50 });
        ed.Add(arrow, "BLUE");
        ed.AddLine(20, 20, 380, 20, "RED"); ed.AddCircle(200, 140, 40, "BLUE"); ed.AddText(30, 200, 8, 0, "COLOUR", "RED");
        return d;
    }
    static HashSet<string> Colours(string content) => Regex.Matches(content, @"([\d.]+ [\d.]+ [\d.]+) (?:RG|rg)\n").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Pdf_Monochrome_Has_Only_Black()
    {
        var d = Sample(); string dir = Samples.WorkDir("d5m");
        string colour = Path.Combine(dir, "colour.pdf"), mono = Path.Combine(dir, "mono.pdf");
        DrawingPdf.Export(d, colour); DrawingPdf.Export(d, mono, null, true);
        var c = Colours(G3Tests.Content(colour)); var m = Colours(G3Tests.Content(mono));
        Assert.Contains("1 0 0", c); Assert.Contains("0 0 1", c);
        Assert.Equal(new[] { "0 0 0" }, m.ToArray());
        // the whole output set follows the option
        var (_, files) = DrawingPdf.ExportSet(d, Path.Combine(dir, "set.pdf"), true, true, true, null, false, true);
        Assert.True(files.Count >= 2);
        foreach (var f in files) Assert.Equal(new[] { "0 0 0" }, Colours(G3Tests.Content(f.Path)).ToArray());
    }

    [Fact]
    public void Wide_Polyline_Is_Printed_At_Its_Width()
    {
        var d = Sample(); string dir = Samples.WorkDir("d5m"); string pdf = Path.Combine(dir, "wide.pdf");
        var res = DrawingPdf.Export(d, pdf); string c = G3Tests.Content(pdf);
        double mmPerUnit = DrawingPdf.MmPerUnit(d, res.Sheets[0]); double wantPt = 6 * mmPerUnit * 72 / 25.4;
        var widths = Regex.Matches(c, @"(?<=\n)([\d.]+) w\n").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        Assert.Contains(widths, w => Math.Abs(w - wantPt) < 0.01);                       // the frame: stroked 6 units wide
        Assert.Matches(@"0 0 1 rg\n[\d.]+ [\d.]+ m [\d.]+ [\d.]+ l [\d.]+ [\d.]+ l [\d.]+ [\d.]+ l", c);   // the tapering arrow: a filled quadrilateral in the layer colour
        // DXF round trip keeps the widths
        string dxf = Path.Combine(dir, "wide.dxf"); DxfWriter.Write(d, dxf, DxfWriter.Mode.R2000, FontCompat.Plan(d), false, true);
        var back = DxfReader.Read(dxf).Entities.OfType<LwPolyline>().ToList();
        Assert.Equal(6, back[0].ConstWidth, 9); Assert.All(back[0].Vertices, v => Assert.Equal(6, v.StartWidth, 9));
        Assert.Equal(20, back[1].Vertices[0].EndWidth, 9); Assert.Equal(0, back[1].Vertices[0].StartWidth, 9);
    }
}
