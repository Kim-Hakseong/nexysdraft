using System.Diagnostics;
using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft5f: a DWG saved by NexysDraft aborted DWG TrueView ("INTERNAL ERROR: !dbspace.cpp@2194: eOk", colleague report 2026-11-10).
/// With DWG TrueView 2027 on the dev PC the saved files were opened by its console engine (accoreconsole.exe) and three faults were measured:
/// (1) *Model_Space not at handle 1F leaves LibreDWG's own *Model_Space behind as a second, ownerless record, and a duplicate handle when
/// another object holds 1F ("Duplicate handle(s) ignored. Drawing needs recovery"); (2) a DXF whose LAYER has no 390 is discarded
/// ("Error in LAYER Table. Did not receive PlotStyleName"); (3) spline-fit polylines lost every vertex ("Two vertices were added to a 2D pline
/// which had no vertices"). The gate checks the three rules on every save and, when TrueView is installed, opens every file with it.</summary>
public class D5fTests
{
    readonly ITestOutputHelper _o;
    public D5fTests(ITestOutputHelper o) { _o = o; }

    /// <summary>accoreconsole.exe of an installed DWG TrueView (dev PC check only — not part of the product).</summary>
    public static string? TrueViewConsole()
    {
        try
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk");
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root, "DWG TrueView*").OrderByDescending(x => x).Select(d => Path.Combine(d, "accoreconsole.exe")).FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    static readonly string[] Bad = { "Duplicate handle", "needs recovery", "no vertices", "discarded", "Error in ", "ERROR:", "orphaned", "INTERNAL ERROR" };

    /// <summary>Open one DWG/DXF with the TrueView console engine, switch to paper space and back, quit. Returns the complaints it printed ("" = clean), or null when TrueView is not installed.</summary>
    public static string? TrueViewOpen(string file)
    {
        var exe = TrueViewConsole(); if (exe == null) return null;
        string work = Path.Combine(Path.GetTempPath(), "NexysDraft", "tv", Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(work);
        string inp = Path.Combine(work, "in" + Path.GetExtension(file).ToLowerInvariant()), scr = Path.Combine(work, "q.scr");
        File.Copy(file, inp, true); File.WriteAllText(scr, "TILEMODE\n0\nTILEMODE\n1\nQUIT\nY\n", Encoding.ASCII);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.Unicode, WorkingDirectory = work };
        foreach (var a in new[] { "/i", inp, "/s", scr, "/l", "en-US" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!; var outTask = p.StandardOutput.ReadToEndAsync(); var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(240000)) { try { p.Kill(true); } catch { } return "timeout 240 s"; }
        string text = outTask.Result.Replace("\0", "");
        var hits = text.Split('\n').Select(l => l.Trim()).Where(l => Bad.Any(b => l.Contains(b, StringComparison.OrdinalIgnoreCase))).Distinct().Take(4).ToList();
        if (p.ExitCode != 0) hits.Add("exit " + p.ExitCode);
        if (!text.Contains("Regenerating model", StringComparison.OrdinalIgnoreCase)) hits.Add("drawing not loaded");
        try { Directory.Delete(work, true); } catch { }
        return string.Join("; ", hits);
    }

    /// <summary>The draft5f rules on a written DXF: *Model_Space record at 1F, no other object at 1F, every LAYER of a complete file (one with
    /// an ACDBPLACEHOLDER) carries a 390 that points at an object in the file, no POLYLINE without VERTEX.</summary>
    public static List<string> Rules(string path, bool complete)
    {
        var lines = File.ReadAllLines(path, Encoding.Latin1); var p = new List<(int c, string v)>();
        for (int i = 0; i + 1 < lines.Length; i += 2) if (int.TryParse(lines[i].Trim(), out int c)) p.Add((c, lines[i + 1].Trim()));
        var issues = new List<string>(); var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var at1F = new List<string>();
        string cur = "", recHandle = ""; int layers = 0, layers390 = 0; var layer390 = new List<string>(); bool layerHas = false, inLayer = false; string ms = "";
        for (int i = 0; i < p.Count; i++)
        {
            var (c, v) = p[i];
            if (c == 0)
            {
                if (inLayer) { layers++; if (layerHas) layers390++; }
                if (cur == "POLYLINE" && v != "VERTEX") issues.Add("POLYLINE without VERTEX");
                cur = v; recHandle = ""; inLayer = false; layerHas = false;
                // a LAYER record (not the TABLE head, whose name group is 2 LAYER)
                if (v == "LAYER") inLayer = true;
                continue;
            }
            if (c == 5 || (c == 105 && cur == "DIMSTYLE")) { handles.Add(v); recHandle = v; if (v.Equals("1F", StringComparison.OrdinalIgnoreCase)) at1F.Add(cur); }
            if (cur == "BLOCK_RECORD" && c == 2 && v.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase)) ms = recHandle;
            if (inLayer && c == 390) { layerHas = true; layer390.Add(v); }
        }
        if (!ms.Equals("1F", StringComparison.OrdinalIgnoreCase)) issues.Add($"*Model_Space record at {ms}, not 1F");
        if (at1F.Count != 1) issues.Add($"handle 1F used by {string.Join("+", at1F)}");
        if (complete)
        {
            if (layers390 != layers) issues.Add($"{layers - layers390} of {layers} LAYER without plot style (390)");
            if (layer390.Any(h => !handles.Contains(h))) issues.Add("LAYER 390 points at a missing object");
        }
        return issues.Distinct().ToList();
    }

    [Fact]
    public void SwapHandles_ExchangesEveryHandleGroup_ButNotHandseed()
    {
        string dxf = "  9\r\n$HANDSEED\r\n  5\r\n1F\r\n  0\r\nLAYER\r\n  5\r\n1F\r\n330\r\n15\r\n  2\r\n1F\r\n  0\r\nLINE\r\n  5\r\n15\r\n330\r\n1f\r\n1005\r\n15\r\n 10\r\n15\r\n";
        string got = Encoding.Latin1.GetString(DxfWriter.SwapHandles(Encoding.Latin1.GetBytes(dxf), "15", "1F"));
        string want = "  9\r\n$HANDSEED\r\n  5\r\n1F\r\n  0\r\nLAYER\r\n  5\r\n15\r\n330\r\n1F\r\n  2\r\n1F\r\n  0\r\nLINE\r\n  5\r\n1F\r\n330\r\n15\r\n1005\r\n1F\r\n 10\r\n15\r\n";
        Assert.Equal(want, got);
    }

    [Fact]
    public void SplineFitPolyline_KeepsItsCurvePoints()
    {
        // POLYLINE 70=4 (spline-fit): vertex 70=16 is a frame control point (not on the curve), 70=8 a point made by spline fitting (on the curve)
        var sb = new StringBuilder("  0\r\nSECTION\r\n  2\r\nENTITIES\r\n  0\r\nPOLYLINE\r\n  5\r\nA0\r\n  8\r\n0\r\n 66\r\n1\r\n 70\r\n4\r\n 10\r\n0.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n");
        void V(string h, double x, double y, int f) => sb.Append($"  0\r\nVERTEX\r\n  5\r\n{h}\r\n  8\r\n0\r\n 10\r\n{x}\r\n 20\r\n{y}\r\n 30\r\n0.0\r\n 70\r\n{f}\r\n");
        V("A1", 0, 0, 16); V("A2", 5, 9, 16); V("A3", 10, 0, 16); V("A4", 0, 0, 8); V("A5", 5, 4, 8); V("A6", 10, 0, 8);
        sb.Append("  0\r\nSEQEND\r\n  5\r\nA7\r\n  8\r\n0\r\n  0\r\nENDSEC\r\n  0\r\nEOF\r\n");
        string src = Path.Combine(Samples.WorkDir("d5f"), "splinefit_src.dxf"); File.WriteAllText(src, sb.ToString(), Encoding.ASCII);
        var d = DxfReader.Read(src); var pl = Assert.Single(d.Entities.OfType<Polyline>());
        Assert.Equal(3, pl.Vertices.Count); Assert.Equal(4.0, pl.Vertices[1].Y, 9);
        string outp = Path.Combine(Samples.WorkDir("d5f"), "splinefit_out.dxf"); DxfWriter.Write(d, outp, DxfWriter.Mode.R2000, null, false, true);
        var b = Assert.Single(DxfReader.Read(outp).Entities.OfType<Polyline>());
        Assert.Equal(3, b.Vertices.Count); Assert.Equal(0, b.Flags & 6);   // written as plain vertices: the fit flags are cleared
        Assert.Empty(Rules(outp, true));
    }

    [Fact]
    public void OldNexysDxf_WithoutPlotStyle_IsRegenerated()
    {
        // the generated samples were written before draft5f: AC1015, LAYER without 390 — keeping their structure would keep the fault
        var d = DxfReader.Read(Path.Combine(Samples.GenDir, "g01_basic.dxf"));
        Assert.True(DxfSave.NoPlotStyle(d));
        Assert.Equal(DxfWriter.Mode.R2000, DxfSave.Choose(d, DxfSave.Ver.Auto).mode);
    }

    [Fact]
    public void Gate_D5f_TrueView_OpensEverySave()
    {
        var lines = new List<string> { "# draft5f: every save is checked for the three faults DWG TrueView 2027 reported (model space at 1F / LAYER plot style / POLYLINE without VERTEX) and, when TrueView is installed, opened by its console engine (paper space switch included). Columns: DXF save (app default), DWG save (R2000 DXF → dxf2dwg)" };
        string tv = TrueViewConsole() ?? ""; lines.Add("# TrueView console: " + (tv == "" ? "not installed — TrueView columns SKIP (UNVERIFIED)" : tv));
        var sources = new List<(string name, Func<Document> load)>();
        foreach (var f in Directory.Exists(Samples.UserDir) ? Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x) : Enumerable.Empty<string>())
            sources.Add((Path.GetFileName(f), () => { var d = DxfReader.Read(OdaGateTests.Dxf(f)); d.Path = f; d.FromLibreDwg = true; return d; }));
        foreach (var f in Directory.GetFiles(Samples.GenDir, "*.dxf").Where(x => !Path.GetFileName(x).StartsWith("g05")).OrderBy(x => x)) /* g05 = 100k entities (speed sample) */ sources.Add((Path.GetFileName(f), () => DxfReader.Read(f)));
        sources.Add(("new drawing (basic shapes)", () =>
        {
            var d = Document.CreateNew(); var ed = new Editor(d);
            ed.AddPolyline(Shapes.Rectangle(0, 0, 100, 50), true, "0"); var a = Shapes.Arc3(150, 0, 175, 25, 200, 0)!.Value; ed.AddArc(a.cx, a.cy, a.r, a.a0, a.a1, "0");
            ed.AddText(0, -30, 5, 0, "한글 텍스트 TEXT 1", "0"); return d;
        }));
        string dir = Samples.WorkDir("d5f"); bool ok = true; int n = 0;
        foreach (var (name, load) in sources)
        {
            n++; string stem = Path.Combine(dir, $"s{n:00}");
            // DXF save as Engine.SaveAsync does it, after an edit
            var a = load(); new Editor(a).AddPolyline(Shapes.Rectangle(0, 0, 10, 5), true, "0");
            var (mode, _) = DxfSave.Choose(a, DxfSave.Ver.Auto); string dxf = stem + ".dxf";
            DxfWriter.Write(a, dxf, mode, FontCompat.Plan(a), false, mode == DxfWriter.Mode.R2000);
            var rd = mode == DxfWriter.Mode.R2000 ? Rules(dxf, true) : new List<string>(); rd.AddRange(D5dTests.Check(dxf).Select(i => i.Kind).Distinct());
            // DWG save
            var b = load(); new Editor(b).AddPolyline(Shapes.Rectangle(0, 0, 10, 5), true, "0");
            string tmp = stem + ".fordwg.dxf", dwg = stem + ".dwg"; DxfWriter.Write(b, tmp, DxfWriter.Mode.R2000, FontCompat.Plan(b));
            var rw = Rules(tmp, false); string conv = "dxf2dwg SKIP";
            string? td = null, tw = null;
            if (LibreDwg.Available) { var r = LibreDwg.DxfToDwg(tmp, dwg); conv = r.Ok ? "dxf2dwg ok" : "dxf2dwg FAIL"; if (!r.Ok) rw.Add("dxf2dwg failed"); else tw = TrueViewOpen(dwg); }
            td = TrueViewOpen(dxf);
            bool rowOk = rd.Count == 0 && rw.Count == 0 && string.IsNullOrEmpty(td) && string.IsNullOrEmpty(tw); ok &= rowOk;
            string T(string? t) => t == null ? "TrueView SKIP" : t == "" ? "TrueView clean" : "TrueView: " + t;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{name}\tDXF ({mode}) rules {(rd.Count == 0 ? "ok" : string.Join(", ", rd))}, {T(td)}\tDWG rules {(rw.Count == 0 ? "ok" : string.Join(", ", rw))}, {conv}, {T(tw)}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d5f_trueview.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
