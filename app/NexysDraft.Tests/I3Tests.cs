using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Pdf;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft5 I3: PDF output set — name.pdf (every sheet), name__NNN.pdf (one per sheet), name__overview.pdf (whole model space).</summary>
public class I3Tests
{
    readonly ITestOutputHelper _o;
    public I3Tests(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Gate_I3_PdfOutputSet()
    {
        var lines = new List<string> { "# I3 + draft5b: PDF output set (app default = collapse a single-page drawing) — u05: merged 10 pages + 10 page files + overview = 12 files; u01 (single page): merged only = 1 file; u01 with the parts named (CLI flags): 3 files; every file extracts text (pdftotext)" };
        bool ok = true; bool p2t = PdfTests.PdfToText() != null;
        foreach (var (name, files, pages, collapse) in new[] { ("u05_hthp_pid", 12, 10, true), ("u01_pid_2504", 1, 1, true), ("u01_pid_2504", 3, 1, false) })
        {
            string dwg = Path.Combine(Samples.UserDir, name + ".dwg"); if (!File.Exists(dwg)) { lines.Add($"SKIP\t{name}"); continue; }
            var d = DxfReader.Read(OdaGateTests.Dxf(dwg));
            string dir = Path.Combine(Samples.WorkDir("i3"), name + (collapse ? "" : "_named")); if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
            var (res, outs) = DrawingPdf.ExportSet(d, Path.Combine(dir, name + ".pdf"), collapseSingle: collapse);
            var onDisk = Directory.GetFiles(dir, "*.pdf");
            int merged = outs.Count(f => f.Kind == "merged"), sheets = outs.Count(f => f.Kind == "sheet"), overview = outs.Count(f => f.Kind == "overview");
            var extract = outs.Select(f => (f, n: p2t ? PdfTests.Extract(f.Path).Count(c => !char.IsWhiteSpace(c)) : -1)).ToList();
            bool rowOk = onDisk.Length == files && outs.Count == files && res != null && res.Pages >= pages && merged == 1 && (files == 1 ? sheets == 0 && overview == 0 : sheets == pages && overview == 1) && extract.All(x => !p2t || x.n > 0);
            ok &= rowOk;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{name}{(collapse ? "" : " (named parts)")}\tfiles {onDisk.Length} (expected {files}): merged {merged} ({res?.Pages} pages), sheet {sheets}, overview {overview}");
            foreach (var (f, n) in extract) lines.Add($"\t{Path.GetFileName(f.Path)}\t{f.Kind}\t{f.Pages} p\t{f.Paper}\tpdftotext {(n < 0 ? "SKIP" : n + " chars")}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "i3_pdf_set.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
