using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;
using NexysDraft.Core.Pdf;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft3 G1 gates: model/paper space separation and preservation, nanoCAD font compatibility, u04 model extents.</summary>
public class G1Tests
{
    readonly ITestOutputHelper _o;
    public G1Tests(ITestOutputHelper o) { _o = o; }

    static string Dxf(string dwg)
    {
        string dxf = Path.Combine(Samples.WorkDir("dxf"), Path.GetFileNameWithoutExtension(dwg) + ".dxf");
        if (!File.Exists(dxf) || File.GetLastWriteTimeUtc(dxf) < File.GetLastWriteTimeUtc(dwg)) { var r = LibreDwg.DwgToDxf(dwg, dxf); Assert.True(r.Ok, r.Stderr); }
        return dxf;
    }

    /// <summary>Model entities (ENTITIES without 67) and paper entities (67=1, plus entities of extra *Paper_Space* layout blocks).</summary>
    public static (int model, int paper) Spaces(Document d)
    {
        int model = d.Entities.Count(e => !e.PaperSpace);
        int paper = d.Entities.Count(e => e.PaperSpace) + d.Blocks.Values.Where(b => b.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase) && !b.Name.Equals("*Paper_Space", StringComparison.OrdinalIgnoreCase)).Sum(b => b.Entities.Count);
        return (model, paper);
    }

    [Fact]
    public void Gate_G1_SpacesTable_And_PaperRoundTrip()
    {
        var lines = new List<string> { "# G1: model / paper space entity counts (NexysDraft reader of the LibreDWG DXF vs ODA reading the original DWG), and paper space after our DXF / DWG save" };
        bool ok = true; bool oda = OdaGateTests.Converter() != null;
        if (Directory.Exists(Samples.UserDir))
            foreach (var f in Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x))
            {
                var a = DxfReader.Read(Dxf(f)); var (m, p) = Spaces(a);
                string odaCol = "ODA SKIP"; bool rowOk = true;
                if (oda)
                {
                    var (o, err, _) = OdaGateTests.Convert(f);
                    if (o != null) { var b = DxfReader.Read(o); var (mb, pb) = Spaces(b); rowOk = m == mb && p == pb; odaCol = $"ODA model {mb} paper {pb}"; }
                    else { rowOk = false; odaCol = "ODA ERR " + err; }
                }
                ok &= rowOk;
                lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{Path.GetFileName(f)}\tmodel {m}\tpaper {p}\t{odaCol}");
            }
        Samples.EnsureGenerated();
        var inputs = new List<string> { Path.Combine(Samples.GenDir, "g06_paper.dxf") };
        string u05 = Path.Combine(Samples.UserDir, "u05_hthp_pid.dwg"); if (File.Exists(u05)) inputs.Add(Dxf(u05));
        foreach (var src in inputs)
        {
            var a = DxfReader.Read(src); int pa = Spaces(a).paper; string name = Path.GetFileNameWithoutExtension(src);
            string ours = Path.Combine(Samples.WorkDir("g1"), name + ".preserve.dxf"); DxfWriter.Write(a, ours, DxfWriter.Mode.Preserve);
            int pOurs = Spaces(DxfReader.Read(ours)).paper; string dxfOda = "SKIP", dwgOda = "-"; bool rowOk = pOurs == pa;
            if (oda) { var (o, err, _) = OdaGateTests.Convert(ours); int po = o != null ? Spaces(DxfReader.Read(o)).paper : -1; dxfOda = $"paper {po} err[{err}]"; rowOk &= po == pa; }
            if (name == "g06_paper")
            {
                string r2000 = Path.Combine(Samples.WorkDir("g1"), name + ".r2000.dxf"), dwg = Path.ChangeExtension(r2000, ".dwg");
                DxfWriter.Write(a, r2000, DxfWriter.Mode.R2000); var r = LibreDwg.DxfToDwg(r2000, dwg);
                if (!r.Ok) { rowOk = false; dwgOda = "dxf2dwg failed"; }
                else if (oda)
                {
                    var (o, err, _) = OdaGateTests.Convert(dwg); var back = o != null ? DxfReader.Read(o) : null; int po = back != null ? Spaces(back).paper : -1;
                    bool korean = back != null && back.Entities.Any(e => e.PaperSpace && e is Text t && t.Value.Contains("배치 제목"));
                    dwgOda = $"paper {po} err[{err}] korean-title {korean}"; rowOk &= po == pa && string.IsNullOrEmpty(err) && korean;
                }
            }
            ok &= rowOk;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{name}\tpaper {pa}\tour DXF {pOurs}\tODA(our DXF) {dxfOda}\tODA(our DWG) {dwgOda}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "g1_spaces.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }

    [Fact]
    public void Gate_G1_FontCompat()
    {
        var lines = new List<string> { "# G1 font compatibility: after save, styles used by Korean text reference fonts present on this PC (or the family form); u01 STYLE unchanged" };
        bool ok = true;
        if (!Directory.Exists(Samples.UserDir)) { lines.Add("SKIP\tno user drawings"); File.WriteAllLines(Path.Combine(Samples.GatesDir, "g1_fonts.txt"), lines); return; }
        foreach (var f in Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x))
        {
            var a = DxfReader.Read(Dxf(f)); var fixes = FontCompat.Plan(a);
            string outp = Path.Combine(Samples.WorkDir("g1"), Path.GetFileNameWithoutExtension(f) + ".fontfix.dxf"); DxfWriter.Write(a, outp, DxfWriter.Mode.Preserve, fixes);
            var b = DxfReader.Read(outp); var use = FontCompat.KoreanUse(b);
            var bad = b.Styles.Where(s => use.GetValueOrDefault(s.Name) > 0 && !FontCompat.StyleOk(s)).Select(s => $"{s.Name}:{s.Font}").ToList();
            int changed = a.Styles.Count(sa => { var sb = b.Styles.First(x => x.Name == sa.Name); return sa.Font != sb.Font || sa.BigFont != sb.BigFont || sa.TrueTypeFamily != sb.TrueTypeFamily; });
            bool rowOk = bad.Count == 0 && changed == fixes.Count && (!f.Contains("u01") || changed == 0);
            ok &= rowOk;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{Path.GetFileName(f)}\tstyles {a.Styles.Count}\tKorean-used {use.Count(kv => kv.Value > 0)}\tfixes {fixes.Count}: {string.Join(", ", fixes.Select(x => $"{x.Style} {x.OldFont}{(x.OldBigFont != "" ? "+" + x.OldBigFont : "")} → {x.NewFamilyKo}({x.NewFamily})"))}\tchanged {changed}\tmissing after {bad.Count} {string.Join(",", bad)}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "g1_fonts.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }

    [Fact]
    public void Gate_G1_U04_ModelExtentsAreTheSheets()
    {
        string u04 = Path.Combine(Samples.UserDir, "u04_hthp_pfd.dwg"); if (!File.Exists(u04)) return;
        var d = DxfReader.Read(Dxf(u04)); var b = Bounds.Of(d)!.Value; var sheets = SheetDetector.Detect(d, out bool det);
        double sx0 = sheets.Min(s => s.X0), sx1 = sheets.Max(s => s.X1), sy0 = sheets.Min(s => s.Y0), sy1 = sheets.Max(s => s.Y1);
        string line = $"u04 extents x {b.x0:F1}..{b.x1:F1} y {b.y0:F1}..{b.y1:F1} | sheets {sheets.Count} x {sx0:F1}..{sx1:F1} y {sy0:F1}..{sy1:F1} | width ratio {(b.x1 - b.x0) / (sx1 - sx0):F3} height ratio {(b.y1 - b.y0) / (sy1 - sy0):F3}";
        File.WriteAllText(Path.Combine(Samples.GatesDir, "g1_u04_extents.txt"), line); _o.WriteLine(line);
        Assert.True(det && sheets.Count == 2, line);
        Assert.True((b.x1 - b.x0) <= (sx1 - sx0) * 1.15 && (b.y1 - b.y0) <= (sy1 - sy0) * 1.15, line);
    }
}
