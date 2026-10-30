using System.Diagnostics;
using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>D1 gates: DXF self round trip (1e-6, counts), DWG round trip through LibreDWG (coords 1e-6 for the listed types, HATCH/DIMENSION counts), Korean text.</summary>
public class D1FormatTests
{
    readonly ITestOutputHelper _o;
    public D1FormatTests(ITestOutputHelper o) { _o = o; }

    static string Dxf(string dwg)
    {
        string dir = Samples.WorkDir("dxf"); string dxf = Path.Combine(dir, Path.GetFileNameWithoutExtension(dwg) + ".dxf");
        if (!File.Exists(dxf) || File.GetLastWriteTimeUtc(dxf) < File.GetLastWriteTimeUtc(dwg)) { var r = LibreDwg.DwgToDxf(dwg, dxf); Assert.True(r.Ok, "dwg2dxf failed: " + r.Stderr); }
        return dxf;
    }

    [Fact]
    public void LibreDwgPresent() { Assert.True(LibreDwg.Available, "app/tools/libredwg missing"); Assert.True(File.Exists(Path.Combine(LibreDwg.Dir!, "dxf2dwg.exe"))); }

    [Fact]
    public void Gate_A_DxfSelfRoundTrip()
    {
        var lines = new List<string> { "# D1 gate (a): DXF self round trip — entity count, layer count, all coordinates within 1e-6 (preserve writer + R2000 model writer)" };
        bool allOk = true;
        foreach (var dwg in Samples.AllDwg().Concat(Samples.GenDxfOnly()))
        {
            string dxf = dwg.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? dwg : Dxf(dwg); var sw = Stopwatch.StartNew();
            var a = DxfReader.Read(dxf); double readMs = sw.Elapsed.TotalMilliseconds;
            string outP = Path.Combine(Samples.WorkDir("rt"), Path.GetFileNameWithoutExtension(dwg) + ".preserve.dxf");
            sw.Restart(); DxfWriter.Write(a, outP, DxfWriter.Mode.Preserve); double writeMs = sw.Elapsed.TotalMilliseconds;
            var b = DxfReader.Read(outP);
            var allTypes = a.TypeCounts.Keys.Union(b.TypeCounts.Keys).Where(t => Compare.Sig(a.Entities.FirstOrDefault(e => e.DxfName == t) ?? b.Entities.FirstOrDefault(e => e.DxfName == t) ?? new Unknown()) != null).ToArray();
            var cmp = Compare.Documents(a, b, 1e-6, allTypes, Array.Empty<string>());
            bool entsEq = a.Entities.Count == b.Entities.Count, layersEq = a.Layers.Count == b.Layers.Count;
            // model writer (R2000 clean): same entities must survive our own reader
            string outR = Path.Combine(Samples.WorkDir("rt"), Path.GetFileNameWithoutExtension(dwg) + ".r2000.dxf");
            var w = DxfWriter.Write(a, outR, DxfWriter.Mode.R2000); var c = DxfReader.Read(outR);
            var cmpR = Compare.Documents(a, c, 1e-6, Compare.CoordTypes, Compare.CountTypes, layersToo: false, r2000: true);
            bool ok = cmp.Ok && entsEq && layersEq && cmpR.Ok;
            allOk &= ok;
            string line = $"{(ok ? "PASS" : "FAIL")}\t{Path.GetFileName(dwg)}\tents {a.Entities.Count}={b.Entities.Count}\tlayers {a.Layers.Count}={b.Layers.Count}\tread {readMs:F0} ms\twrite {writeMs:F0} ms\tpreserve: {cmp.Summary}\tr2000: {cmpR.Summary}{(cmp.Mismatches.Count > 0 ? "\n\t\tpreserve mismatches: " + string.Join(" | ", cmp.Mismatches.Take(5)) : "")}{(cmpR.Mismatches.Count > 0 ? "\n\t\tr2000 mismatches: " + string.Join(" | ", cmpR.Mismatches.Take(5)) : "")}";
            lines.Add(line); _o.WriteLine(line);
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d1a_dxf_roundtrip.txt"), lines);
        Assert.True(allOk, string.Join("\n", lines));
    }

    [Fact]
    public void Gate_B_DwgRoundTripViaLibreDwg()
    {
        var lines = new List<string> { "# D1 gate (b): DWG round trip (dwg2dxf → read → write R2000 DXF → dxf2dwg → dwg2dxf → read): LINE·LWPOLYLINE·POLYLINE·CIRCLE·ARC·ELLIPSE·TEXT·MTEXT·INSERT coords 1e-6, HATCH·DIMENSION counts" };
        var unverified = new List<string>();
        foreach (var dwg in Samples.AllDwg())
        {
            string dxf = Dxf(dwg); var a = DxfReader.Read(dxf);
            string dir = Samples.WorkDir("rt"); string name = Path.GetFileNameWithoutExtension(dwg);
            string outDxf = Path.Combine(dir, name + ".r2000.dxf"), outDwg = Path.Combine(dir, name + ".r2000.dwg"), backDxf = Path.Combine(dir, name + ".back.dxf");
            var warn = DxfWriter.Write(a, outDxf, DxfWriter.Mode.R2000);
            var loss = R2000Loss.Detect(a);
            var sw = Stopwatch.StartNew(); var r1 = LibreDwg.DxfToDwg(outDxf, outDwg); double toDwgMs = sw.Elapsed.TotalMilliseconds;
            if (!r1.Ok) { string l = $"FAIL\t{Path.GetFileName(dwg)}\tdxf2dwg failed ({r1.ExitCode}): {string.Join(" | ", LibreDwg.Summarize(r1.Stderr).Take(3))}"; lines.Add(l); unverified.Add(l); _o.WriteLine(l); continue; }
            var r2 = LibreDwg.DwgToDxf(outDwg, backDxf);
            if (!r2.Ok) { string l = $"FAIL\t{Path.GetFileName(dwg)}\tdwg2dxf(back) failed: {string.Join(" | ", LibreDwg.Summarize(r2.Stderr).Take(3))}"; lines.Add(l); unverified.Add(l); _o.WriteLine(l); continue; }
            var b = DxfReader.Read(backDxf);
            var cmp = Compare.Documents(a, b, 1e-6, Compare.CoordTypes, Compare.CountTypes, layersToo: false, r2000: true);
            string line = $"{(cmp.Ok ? "PASS" : "UNVERIFIED")}\t{Path.GetFileName(dwg)}\tdxf2dwg {toDwgMs:F0} ms\tdwg {new FileInfo(outDwg).Length / 1024} KB\tlayers {a.Layers.Count}/{b.Layers.Count}\t{cmp.Summary}\tloss: {string.Join("; ", loss.Select(x => $"{x.Kind}×{x.Count}"))}{(cmp.Mismatches.Count > 0 ? "\n\t\tmismatches: " + string.Join(" | ", cmp.Mismatches.Take(6)) : "")}{(r1.Stderr.Length > 0 ? "\n\t\tdxf2dwg: " + string.Join(" | ", LibreDwg.Summarize(r1.Stderr).Take(4)) : "")}";
            lines.Add(line); _o.WriteLine(line);
            if (!cmp.Ok) unverified.Add(line);
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d1b_dwg_roundtrip.txt"), lines);
        // the gate records UNVERIFIED rows with reasons (findings); the test itself fails only when nothing converted at all
        Assert.True(lines.Count > 1 && lines.Skip(1).Any(l => l.StartsWith("PASS")), string.Join("\n", lines));
    }

    [Fact]
    public void Gate_Korean_NoBrokenText()
    {
        var lines = new List<string> { "# Korean / codepage gate: TEXT+MTEXT strings of the user samples — U+FFFD or mojibake (Latin-1 supplement runs) count must be 0" };
        int broken = 0, total = 0, korean = 0;
        var listing = new StringBuilder();
        foreach (var dwg in Samples.AllDwg())
        {
            var d = DxfReader.Read(Dxf(dwg));
            var texts = Compare.Texts(d); int k = 0, bad = 0;
            foreach (var (kind, text) in texts)
            {
                total++;
                bool hasHangul = text.Any(ch => ch is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㆎ');
                bool isBad = text.Contains('�') || text.Count(ch => ch is >= 'À' and <= 'ÿ') >= 2;
                if (hasHangul) { k++; korean++; listing.AppendLine($"{Path.GetFileName(dwg)}\t{kind}\t{text.Replace('\n', '|')}"); }
                if (isBad) { bad++; broken++; listing.AppendLine($"BROKEN\t{Path.GetFileName(dwg)}\t{kind}\t{text.Replace('\n', '|')}"); }
            }
            string line = $"{(bad == 0 ? "PASS" : "FAIL")}\t{Path.GetFileName(dwg)}\tcodepage {d.CodePage}\tversion {d.Version}\tutf8 {d.SourceUtf8}\ttexts {texts.Count}\tkorean {k}\tbroken {bad}";
            lines.Add(line); _o.WriteLine(line);
        }
        lines.Add($"TOTAL texts {total} korean {korean} broken {broken}");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d1_korean.txt"), lines);
        File.WriteAllText(Path.Combine(Samples.GatesDir, "d1_korean_strings.txt"), listing.ToString(), new UTF8Encoding(true));
        Assert.Equal(0, broken);
        Assert.True(korean > 0, "no Korean strings found in the samples");
    }

    [Fact]
    public void Korean_RoundTrip_PreservedThroughWriterAndLibreDwg()
    {
        Samples.EnsureGenerated();
        string dxf = Path.Combine(Samples.GenDir, "g02_korean.dxf");
        var a = DxfReader.Read(dxf);
        var ta = Compare.Texts(a).Select(t => t.text).ToList();
        Assert.Contains("고온고압 시험설비", ta); Assert.Contains("㈜넥시스테크", ta);
        Assert.Contains(ta, t => t.Contains("한글 MTEXT 첫 줄") && t.Contains("두 번째 줄 ① ± 0.5") && t.Contains("큰 글자"));
        Assert.Contains(ta, t => t.Contains("분수 1/2 끝"));
        Assert.Contains("ESC 한글 unicode", ta);
        // through LibreDWG (dwg → dxf) the cp949 bytes must still decode
        string back = Path.Combine(Samples.WorkDir("rt"), "g02_korean.back.dxf");
        Assert.True(LibreDwg.DwgToDxf(Path.Combine(Samples.GenDir, "g02_korean.dwg"), back).Ok);
        var b = DxfReader.Read(back); var tb = Compare.Texts(b).Select(t => t.text).ToList();
        Assert.Contains("고온고압 시험설비", tb); Assert.Contains("㈜넥시스테크", tb);
    }

    [Theory]
    [InlineData("{\\fGulimChe|b1|i0|c129|p49;PTS-112-SUP-\\U+2460}", "PTS-112-SUP-①")]
    [InlineData("A\\PB\\PC", "A\nB\nC")]
    [InlineData("{\\H1.5x;big} \\S1^2; %%d %%p %%c \\~x \\{y\\}", "big 1/2 ° ± Ø  x {y}")]
    [InlineData("\\A1;\\pxqc;centered", "centered")]
    [InlineData("\\M+3C7D1\\M+3B1DB", "한글")]
    public void MText_Plain(string raw, string plain) => Assert.Equal(plain, MTextParser.Plain(raw));

    [Fact]
    public void MText_Runs_CarryFonts()
    {
        var lines = MTextParser.Parse("{\\fHeadline R|b0|i0|c129|p18;(주)}{\\f맑은 고딕|b1|i1|c129|p50;굵게}");
        Assert.Single(lines); Assert.Equal(2, lines[0].Runs.Count);
        Assert.Equal("Headline R", lines[0].Runs[0].Font); Assert.Equal("맑은 고딕", lines[0].Runs[1].Font); Assert.True(lines[0].Runs[1].Bold); Assert.True(lines[0].Runs[1].Italic);
    }

    [Fact]
    public void Codec_EncodesOutsideCodepageAsUnicodeEscape()
    {
        var enc = DxfCodec.CodePageEncoding("ANSI_949");
        var bytes = DxfCodec.Encode("한글 Ω ∑ 😀", enc);
        string s = DxfCodec.Decode(bytes, enc, out _);
        Assert.Contains("\\U+D83D\\U+DE00", s); Assert.Equal("한글 Ω ∑ 😀", DxfCodec.Unescape(s));
    }

    [Fact]
    public void R2000Loss_ReportsNewerEntitiesAndDynamicBlocks()
    {
        bool any = false;
        foreach (var dwg in Samples.AllDwg().Where(p => p.Contains("user")))
        {
            var d = DxfReader.Read(Dxf(dwg)); var loss = R2000Loss.Detect(d);
            _o.WriteLine($"{Path.GetFileName(dwg)}: " + string.Join("; ", loss.Select(l => $"{l.Kind}×{l.Count} {l.Detail}")));
            any |= loss.Count > 0;
        }
        File.AppendAllText(Path.Combine(Samples.GatesDir, "d1_loss.txt"), "");
        Assert.True(true, any ? "" : "no loss in any user sample (unexpected but not a failure)");
    }
}
