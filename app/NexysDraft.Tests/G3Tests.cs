using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using NexysDraft.Core.Pdf;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft3 G3 gates: paper auto-selection, text heights on paper, extraction rates, u04 = 2 pages, colours and MTEXT runs in the content stream.</summary>
public class G3Tests
{
    readonly ITestOutputHelper _o;
    public G3Tests(ITestOutputHelper o) { _o = o; }

    /// <summary>All page content streams of a PDF written by PdfWriter, inflated.</summary>
    public static string Content(string pdf)
    {
        var b = File.ReadAllBytes(pdf); var sb = new StringBuilder(); string s = Encoding.Latin1.GetString(b);
        foreach (Match m in Regex.Matches(s, @"/Length (\d+) /Filter /FlateDecode >>\nstream\n"))
        {
            int start = m.Index + m.Length, len = int.Parse(m.Groups[1].Value);
            try { using var z = new ZLibStream(new MemoryStream(b, start, len), CompressionMode.Decompress); using var r = new StreamReader(z, Encoding.Latin1); sb.Append(r.ReadToEnd()).Append('\n'); } catch { }
        }
        return sb.ToString();
    }

    [Fact]
    public void Gate_G3_Paper_Pages_Extraction()
    {
        var lines = new List<string> { "# G3: paper auto (plot settings → nearest ISO, else A3; 10 mm margin), pages, pdftotext extraction, text cap heights on paper (mm)" };
        bool ok = true; var p2t = PdfTests.PdfToText() != null;
        if (Directory.Exists(Samples.UserDir))
            foreach (var f in Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x))
            {
                string dxf = Path.Combine(Samples.WorkDir("dxf"), Path.GetFileNameWithoutExtension(f) + ".dxf"); if (!File.Exists(dxf)) Assert.True(LibreDwg.DwgToDxf(f, dxf).Ok);
                var d = DxfReader.Read(dxf); string pdf = Path.Combine(Samples.WorkDir("g3"), Path.GetFileNameWithoutExtension(f) + ".pdf");
                var res = DrawingPdf.Export(d, pdf);
                string ext = p2t ? Regex.Replace(PdfTests.Extract(pdf), @"\s+", "") : "";
                var exp = PdfTests.ExpectedStrings(d); int found = exp.Count(x => ext.Contains(x)); double rate = exp.Count == 0 ? 1 : (double)found / exp.Count;
                string name = Path.GetFileName(f);
                var papers = res.PageInfos.Select(p => p.Paper).Distinct().ToList();
                bool paperOk = !(name.StartsWith("u01") || name.StartsWith("u05") || name.StartsWith("u07")) || papers.SequenceEqual(new[] { "A3" });
                bool pagesOk = !name.StartsWith("u04") || res.Pages == 2;
                bool rateOk = !p2t || rate >= (name.StartsWith("u06") ? 0.96 : 0.99);
                bool rowOk = paperOk && pagesOk && rateOk; ok &= rowOk;
                var pi = res.PageInfos[0];
                lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{name}\tpages {res.Pages}\tpaper {string.Join("/", res.PageInfos.Select(p => $"{p.Paper}{(p.WidthMm > p.HeightMm ? "L" : "P")}"))}\textract {found}/{exp.Count} = {rate:P2}\ttext mm p1: min {pi.MinTextMm:F2} p10 {pi.P10TextMm:F2} median {pi.MedianTextMm:F2}\t{res.Ms:F0} ms\tbasis: {pi.Basis}");
            }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "g3_pdf.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }

    [Fact]
    public void Pdf_ByLayerColours_And_MTextRuns()
    {
        var d = Document.CreateNew(); var ed = new Core.Edit.Editor(d);
        ed.AddLayer("PANEL", 1); ed.AddLayer("FILL", 5);
        ed.AddPolyline(new[] { (0.0, 0.0), (600.0, 0.0), (600.0, 400.0), (0.0, 400.0) }, true, "PANEL");
        ed.Add(new Polyline { Vertices = { new PVertex { X = 50, Y = 50 }, new PVertex { X = 100, Y = 80 }, new PVertex { X = 150, Y = 50 } } }, "PANEL");
        var h = new Hatch { Pattern = "SOLID", Solid = true }; var loop = new HatchLoop { Type = 2 }; var e = new HatchEdge { Type = 0, PolyClosed = true };
        foreach (var (x, y) in new[] { (200.0, 200.0), (260.0, 200.0), (260.0, 260.0) }) e.Poly.Add(new PVertex { X = x, Y = y });
        loop.Edges.Add(e); h.Loops.Add(loop); ed.Add(h, "FILL");
        ed.Add(new MText { Pos = new V3(10, 380), Height = 10, Width = 0, Value = "BASE{\\H2x;BIG}{\\W0.5;NARROW}", Attach = 1 }, "0");
        string pdf = Path.Combine(Samples.WorkDir("g3"), "colours.pdf"); var res = DrawingPdf.Export(d, pdf);
        string c = Content(pdf);
        Assert.Contains("1 0 0 RG", c);      // PANEL (ACI 1) for the LWPOLYLINE and the heavy POLYLINE stroke
        Assert.Contains("0 0 1 rg", c);      // FILL (ACI 5) for the solid hatch fill
        var sizes = Regex.Matches(c, @"/F1 ([\d.]+) Tf").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x).ToList();
        Assert.True(sizes.Count >= 2 && Math.Abs(sizes[^1] / sizes[0] - 2) < 0.05, "MTEXT \\H2x run must be twice the base size: " + string.Join(",", sizes));
        Assert.Contains("50 Tz", c);         // \W0.5 width factor
        File.WriteAllText(Path.Combine(Samples.GatesDir, "g3_colours.txt"), $"PASS colours: PANEL 1 0 0 RG, FILL 0 0 1 rg; MTEXT run sizes {string.Join(", ", sizes)} (ratio {sizes[^1] / sizes[0]:F2}), width factor 50 Tz; paper {res.PageInfos[0].Paper}");
    }
}
