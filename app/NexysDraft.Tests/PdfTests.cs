using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;
using NexysDraft.Core.Pdf;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>F3 gate: PDF of every sample → pdftotext extraction ≥ 99 % of the TEXT/MTEXT strings, page count = sheet count, 1 MB drawing ≤ 5 s.</summary>
public class PdfTests
{
    readonly ITestOutputHelper _o;
    public PdfTests(ITestOutputHelper o) { _o = o; }

    public static string? PdfToText()
    {
        foreach (var c in new[] { @"C:\Program Files\Git\mingw64\bin\pdftotext.exe", @"C:\Program Files\Git\usr\bin\pdftotext.exe" }) if (File.Exists(c)) return c;
        try { var p = Process.Start(new ProcessStartInfo("where.exe", "pdftotext") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!; string s = p.StandardOutput.ReadLine() ?? ""; p.WaitForExit(); return File.Exists(s) ? s : null; } catch { return null; }
    }
    public static string Extract(string pdf)
    {
        var exe = PdfToText(); if (exe == null) return "";
        string txt = pdf + ".txt"; var p = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "-enc", "UTF-8", pdf, txt }, UseShellExecute = false, CreateNoWindow = true })!; p.WaitForExit(60000);
        return File.Exists(txt) ? File.ReadAllText(txt, Encoding.UTF8) : "";
    }
    static string Norm(string s) => Regex.Replace(s, @"\s+", "");

    /// <summary>Strings the PDF must contain: TEXT values and MTEXT plain lines of visible model-space entities (and block contents through inserts).</summary>
    public static List<string> ExpectedStrings(Document d)
    {
        var list = new List<string>();
        void Add(string s) { s = Norm(s); if (s.Length >= 2) list.Add(s); }
        foreach (var e in d.Entities.Where(e => !e.PaperSpace))
        {
            var l = d.LayerOf(e.Layer); if (l != null && (l.Off || l.Frozen)) continue;
            if (e is Text t && !(t.IsAttdef && (t.AttribFlags & 1) != 0)) Add(TextCodes.Plain(t.Value));
            else if (e is MText m) foreach (var line in MTextParser.Parse(m.Value)) Add(line.Plain);
            else if (e is Insert ins) foreach (var a in ins.Attribs) if (!a.Invisible) Add(TextCodes.Plain(a.Text.Value));
        }
        return list;
    }

    [Fact]
    public void Gate_F3_Pdf_TextExtraction()
    {
        var lines = new List<string> { "# F3 gate: PDF (own writer, Malgun Gothic subset) → pdftotext: strings found ≥ 99 %, pages = sheets (+rest), 1 MB drawing ≤ 5 s" };
        bool allOk = true; var pdftotext = PdfToText();
        var inputs = Samples.AllDwg().ToList(); string st = Path.Combine(Samples.Root, "work", "selftest", "full", "files", "selftest_saved.dxf"); if (File.Exists(st)) inputs.Add(st);
        foreach (var f in inputs)
        {
            string dxf = f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? f : Path.Combine(Samples.WorkDir("dxf"), Path.GetFileNameWithoutExtension(f) + ".dxf");
            if (!File.Exists(dxf)) { var r = Core.Dwg.LibreDwg.DwgToDxf(f, dxf); Assert.True(r.Ok); }
            var d = DxfReader.Read(dxf); string pdf = Path.Combine(Samples.WorkDir("pdf"), Path.GetFileNameWithoutExtension(f) + ".pdf");
            var sw = Stopwatch.StartNew(); var res = DrawingPdf.Export(d, pdf); double ms = sw.Elapsed.TotalMilliseconds;
            long size = new FileInfo(pdf).Length; long srcSize = new FileInfo(f).Length;
            string extracted = pdftotext != null ? Norm(Extract(pdf)) : "";
            var expected = ExpectedStrings(d); int found = expected.Count(s => extracted.Contains(s)); double rate = expected.Count == 0 ? 1 : (double)found / expected.Count;
            bool timeOk = srcSize < 1_100_000 ? ms <= 5000 : true; bool rateOk = pdftotext == null || rate >= 0.99;
            bool ok = rateOk && timeOk && res.Pages == res.Sheets.Count; allOk &= ok;
            var missing = expected.Where(s => !extracted.Contains(s)).Take(6).ToList();
            lines.Add($"{(ok ? "PASS" : "FAIL")}\t{Path.GetFileName(f)}\t{ms:F0} ms\tpages {res.Pages} (sheets {res.Sheets.Count}, detected {res.SheetsDetected})\tpdf {size / 1024} KB\ttexts {res.Texts}\textract {found}/{expected.Count} = {rate:P2}{(pdftotext == null ? " (pdftotext missing → SKIP)" : "")}{(missing.Count > 0 ? "\tmissing: " + string.Join(" | ", missing) : "")}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "f3_pdf.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(allOk, string.Join("\n", lines));
    }

    [Fact]
    public void Pdf_Korean_Searchable_Smoke()
    {
        Samples.EnsureGenerated();
        var d = DxfReader.Read(Path.Combine(Samples.GenDir, "g02_korean.dxf"));
        string pdf = Path.Combine(Samples.WorkDir("pdf"), "g02_korean.pdf"); var res = DrawingPdf.Export(d, pdf);
        Assert.Equal(1, res.Pages); Assert.True(new FileInfo(pdf).Length < 2_000_000, "subset font should keep the PDF small");
        if (PdfToText() == null) return;
        string t = Norm(Extract(pdf));
        Assert.Contains(Norm("고온고압 시험설비"), t); Assert.Contains(Norm("㈜넥시스테크"), t); Assert.Contains(Norm("한글 MTEXT 첫 줄"), t); Assert.Contains(Norm("ESC 한글 unicode"), t);
    }
}
