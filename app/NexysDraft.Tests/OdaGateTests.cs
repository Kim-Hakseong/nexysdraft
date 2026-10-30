using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>Development-PC gates through ODA File Converter (config/dev.json, never part of the product): F1 our DXF must read in ODA with no
/// error and keep entity/layer counts and Korean strings; F2 our DWG (dxf2dwg) must convert DWG→DXF in ODA with no error, same counts, no
/// TEXT/MTEXT loss. Rows are SKIP when the converter is not configured.</summary>
public class OdaGateTests
{
    readonly ITestOutputHelper _o;
    public OdaGateTests(ITestOutputHelper o) { _o = o; }

    public static string? Converter()
    {
        try
        {
            string p = Path.Combine(Samples.Root, "config", "dev.json"); if (!File.Exists(p)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(p)); var exe = doc.RootElement.GetProperty("odaFileConverter").GetString();
            return exe != null && File.Exists(exe) ? exe : null;
        }
        catch { return null; }
    }

    /// <summary>Run ODA on one file (staged into its own folder, audit on). Returns the converted file or null with the .err text.</summary>
    public static (string? output, string err, double ms) Convert(string input, string to = "DXF", string ver = "ACAD2018")
    {
        var exe = Converter(); if (exe == null) return (null, "SKIP", 0);
        string work = Path.Combine(Path.GetTempPath(), "NexysDraft", "oda", Guid.NewGuid().ToString("N")[..8]); string inp = Path.Combine(work, "in"), outp = Path.Combine(work, "out");
        Directory.CreateDirectory(inp); Directory.CreateDirectory(outp);
        string ext = Path.GetExtension(input).ToLowerInvariant(); File.Copy(input, Path.Combine(inp, "in" + ext), true);
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { inp, outp, ver, to, "0", "1", "*" + ext }) psi.ArgumentList.Add(a);
        using (var p = Process.Start(psi)!) { if (!p.WaitForExit(600000)) { try { p.Kill(true); } catch { } return (null, "timeout", sw.Elapsed.TotalMilliseconds); } }
        string err = string.Join(" | ", Directory.GetFiles(outp, "*.err").Select(f => File.ReadAllText(f).Trim().Replace("\r\n", " | ").Replace("\n", " | ")));
        var outFile = Directory.GetFiles(outp, "*." + to.ToLowerInvariant()).FirstOrDefault();
        string? final = null;
        if (outFile != null) { final = Path.Combine(Samples.WorkDir("oda"), Path.GetFileNameWithoutExtension(input) + ".oda." + to.ToLowerInvariant()); File.Copy(outFile, final, true); }
        try { Directory.Delete(work, true); } catch { }
        return (final, err, sw.Elapsed.TotalMilliseconds);
    }

    internal static Dictionary<string, int> Counts(Document d) => Compare.Flatten(d, true).Where(e => e is not Unknown).GroupBy(e => e.DxfName).ToDictionary(g => g.Key, g => g.Count());
    internal static string CountsText(Dictionary<string, int> c) => string.Join(" ", c.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
    /// <summary>Korean strings normalized to letters/digits only (ODA trims MTEXT line whitespace and rewrites stacked-fraction codes).</summary>
    internal static List<string> Korean(Document d) => Compare.Texts(d).Select(t => new string(t.text.Where(ch => char.IsLetter(ch)).ToArray())).Where(t => t.Any(ch => ch is >= '가' and <= '힣')).OrderBy(t => t, StringComparer.Ordinal).ToList();
    /// <summary>ODA adds the Defpoints layer when a drawing has dimensions: not a difference.</summary>
    static int LayerCount(Document d) => d.Layers.Count(l => !l.Name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase));
    /// <summary>Layer names (minus Defpoints, case-insensitive) — the set must survive, a count may differ by what ODA adds.</summary>
    internal static bool LayersSame(Document a, Document b) { var sa = a.Layers.Select(l => l.Name.ToUpperInvariant()).Where(n => n != "DEFPOINTS").ToHashSet(); var sb = b.Layers.Select(l => l.Name.ToUpperInvariant()).Where(n => n != "DEFPOINTS").ToHashSet(); return sa.IsSubsetOf(sb); }

    /// <summary>Entity signature list (type + rounded key coordinates) to find what vanished.</summary>
    static HashSet<string> Sigs(Document d) => Compare.Flatten(d, true).Select(e => (e.DxfName, Compare.Sig(e))).Where(x => x.Item2 != null).Select(x => x.DxfName + ":" + string.Join(",", x.Item2!.Take(4).Select(v => Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToHashSet();

    static IEnumerable<string> Inputs()
    {
        foreach (var f in Samples.AllDwg()) yield return f;
        string st = Path.Combine(Samples.Root, "work", "selftest", "full", "files", "selftest_saved.dxf"); if (File.Exists(st)) yield return st;
    }

    [Fact]
    public void Gate_F1_OurDxf_ReadsInOda()
    {
        var lines = new List<string> { "# F1 gate: our DXF (Preserve writer, pure ASCII) → ODA DXF→DXF: ODA read errors 0, entity/layer counts equal, Korean strings identical" };
        bool skip = Converter() == null; bool allOk = true;
        foreach (var f in Inputs())
        {
            string dxf = f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? f : Dxf(f);
            var a = DxfReader.Read(dxf); string ours = Path.Combine(Samples.WorkDir("oda"), Path.GetFileNameWithoutExtension(f) + ".ours.dxf"); DxfWriter.Write(a, ours, DxfWriter.Mode.Preserve);
            bool ascii = File.ReadAllBytes(ours).All(b => b < 0x80);
            if (skip) { lines.Add($"SKIP\t{Path.GetFileName(f)}\tODA not configured\tascii {ascii}"); continue; }
            var (outp, err, ms) = Convert(ours);
            if (outp == null) { allOk = false; lines.Add($"FAIL\t{Path.GetFileName(f)}\tODA: {err}"); continue; }
            var b = DxfReader.Read(outp);
            var ca = Counts(a); var cb = Counts(b); var ka = Korean(a); var kb = Korean(b);
            bool countsOk = ca.All(kv => cb.GetValueOrDefault(kv.Key) == kv.Value), layersOk = LayersSame(a, b), koreanOk = ka.SequenceEqual(kb);
            bool ok = string.IsNullOrEmpty(err) && countsOk && layersOk && koreanOk && ascii; allOk &= ok;
            lines.Add($"{(ok ? "PASS" : "FAIL")}\t{Path.GetFileName(f)}\tODA {ms:F0} ms\terr [{err}]\tascii {ascii}\tlayers {a.Layers.Count}/{b.Layers.Count}\tkorean {ka.Count}/{kb.Count} same {koreanOk}\tcounts {(countsOk ? "same" : "DIFF")}: {CountsText(ca)} | {CountsText(cb)}");
            if (!koreanOk) lines.Add("\t\tkorean diff: " + string.Join(" | ", ka.Except(kb).Take(5)) + " ⇐⇒ " + string.Join(" | ", kb.Except(ka).Take(5)));
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "f1_oda_dxf.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(allOk, string.Join("\n", lines));
    }

    [Fact]
    public void Gate_F2_OurDwg_ConvertsInOda()
    {
        string label = Environment.GetEnvironmentVariable("NEXYSDRAFT_GATE_LABEL") ?? "";
        var lines = new List<string> { $"# F2/G0 gate{(label != "" ? " [" + label + "]" : "")}: our DWG (R2000 DXF → dxf2dwg {LibreDwg.Dir}) → ODA DWG→DXF: errors 0, entity/layer counts equal (minus what the R2000 writer drops on purpose), TEXT/MTEXT loss 0, Korean 100%, paper space kept; 300 s dxf2dwg limit" };
        bool skip = Converter() == null; int pass = 0, fail = 0, xfail = 0;
        foreach (var f in Inputs())
        {
            string name = Path.GetFileNameWithoutExtension(f);
            string dxf = f.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? f : Dxf(f);
            var a = DxfReader.Read(dxf);
            string tag = label != "" ? "." + label : "";
            string r2000 = Path.Combine(Samples.WorkDir("oda"), name + tag + ".r2000.dxf"), dwg = Path.Combine(Samples.WorkDir("oda"), name + tag + ".r2000.dwg");
            var warn = DxfWriter.Write(a, r2000, DxfWriter.Mode.R2000);
            // a failure the DWG pre-check predicts (G2: "unreadable" — e.g. u06 IMAGE) is expected; only unpredicted failures fail the gate
            var pre = R2000Check.Run(a, false); string Why() => pre.DwgOk ? "" : " [expected: pre-check " + string.Join(", ", pre.Items.Where(i => i.Blocking).Select(i => i.Kind)) + "]";
            void Fail() { if (pre.DwgOk) fail++; else xfail++; }
            var sw = Stopwatch.StartNew(); var r = LibreDwg.DxfToDwg(r2000, dwg, "r2000", 300000); double toDwg = sw.Elapsed.TotalMilliseconds;
            if (!r.Ok) { Fail(); lines.Add($"FAIL\t{Path.GetFileName(f)}{Why()}\tdxf2dwg {toDwg / 1000:F0} s: {string.Join(" | ", LibreDwg.Summarize(r.Stderr).Take(2))}"); continue; }
            if (skip) { lines.Add($"SKIP\t{Path.GetFileName(f)}\tODA not configured (dxf2dwg {toDwg:F0} ms ok)"); continue; }
            var (outp, err, ms) = Convert(dwg);
            if (outp == null) { Fail(); lines.Add($"FAIL\t{Path.GetFileName(f)}{Why()}\tdxf2dwg {toDwg:F0} ms\tODA: {err}"); continue; }
            var b = DxfReader.Read(outp);
            var ca = Counts(a); var cb = Counts(b); var ka = Korean(a); var kb = Korean(b);
            // dimension blocks are regenerated by ODA (its own *D block content) → compare model-space counts without block contents, plus DIMENSION/HATCH counts
            int ta = a.Entities.Count(e => e is Text t && !t.IsAttrib) + a.Entities.OfType<MText>().Count(), tb = b.Entities.Count(e => e is Text t && !t.IsAttrib) + b.Entities.OfType<MText>().Count();
            int dimA = a.Entities.OfType<Dimension>().Count(), dimB = b.Entities.OfType<Dimension>().Count(), hA = a.Entities.OfType<Hatch>().Count(), hB = b.Entities.OfType<Hatch>().Count();
            bool written(Entity e) => R2000Loss.R2000Entities.Contains(e.DxfName) && !(e is Insert ins && (ins.Block.Length == 0 || !a.Blocks.ContainsKey(ins.Block)));
            var msA = a.Entities.Where(e => !e.PaperSpace && e is not Unknown && written(e)).GroupBy(e => e.DxfName).ToDictionary(g => g.Key, g => g.Count());
            int paperA = G1Tests.Spaces(a).paper, paperB = G1Tests.Spaces(b).paper; int splA = a.Entities.Count(e => e is Spline), splB = b.Entities.Count(e => e is Spline); var msB = b.Entities.Where(e => !e.PaperSpace && e is not Unknown).GroupBy(e => e.DxfName).ToDictionary(g => g.Key, g => g.Count());
            bool countsOk = msA.All(kv => msB.GetValueOrDefault(kv.Key) == kv.Value), layersOk = LayersSame(a, b), textOk = ta == tb, koreanOk = ka.SequenceEqual(kb);
            bool emptyOut = b.Entities.Count == 0 && msA.Count > 0;
            bool ok = string.IsNullOrEmpty(err) && !emptyOut && countsOk && layersOk && textOk && koreanOk && paperA == paperB; if (ok) pass++; else Fail();
            lines.Add($"{(ok ? "PASS" : "FAIL")}\t{Path.GetFileName(f)}\tdxf2dwg {toDwg:F0} ms\tODA {ms:F0} ms\terr [{err}]{(emptyOut ? " EMPTY OUTPUT" : "")}\tlayers {a.Layers.Count}/{b.Layers.Count}\ttext+mtext {ta}/{tb}\tdim {dimA}/{dimB} hatch {hA}/{hB} spline {splA}/{splB} paper {paperA}/{paperB}\tkorean {ka.Count}/{kb.Count} same {koreanOk}\tmodel counts {(countsOk ? "same" : "DIFF")}: {CountsText(msA)} | {CountsText(msB)}{(warn.Count > 0 ? "\twriter: " + string.Join("; ", warn) : "")}");
            if (!countsOk || !textOk)
            {
                var sa = Sigs(a); var sb = Sigs(b); var missing = sa.Except(sb).Where(s => s.StartsWith("TEXT") || s.StartsWith("MTEXT") || s.StartsWith("LINE") || s.StartsWith("LWPOLYLINE") || s.StartsWith("HATCH")).Take(8).ToList();
                lines.Add("\t\tmissing after ODA: " + string.Join(" | ", missing));
                foreach (var m in missing.Where(x => x.StartsWith("TEXT")).Take(4)) { var parts = m.Split(':')[1].Split(','); var t = a.Entities.OfType<Text>().FirstOrDefault(t => Math.Abs(t.Pos.X - double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)) < 1e-3 && Math.Abs(t.Pos.Y - double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)) < 1e-3); if (t != null) lines.Add($"\t\t  TEXT {t.Handle} '{t.Value}' h={t.Height} style={t.Style} ha={t.HAlign} va={t.VAlign} rot={t.Rotation} wf={t.WidthFactor} ob={t.Oblique} gen={t.GenFlags} extra={t.Extra.Count}"); }
            }
            if (!koreanOk) lines.Add("\t\tkorean diff: " + string.Join(" | ", ka.Except(kb).Take(5)) + " ⇐⇒ " + string.Join(" | ", kb.Except(ka).Take(5)));
        }
        lines.Insert(1, $"SUMMARY\tpass {pass}\tunexpected fail {fail}\texpected fail {xfail} (pre-check said unreadable)");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, $"f2_oda_dwg{(label != "" ? "_" + label : "")}.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(fail == 0, string.Join("\n", lines));
    }

    internal static string Dxf(string dwg)
    {
        string dir = Samples.WorkDir("dxf"); string dxf = Path.Combine(dir, Path.GetFileNameWithoutExtension(dwg) + ".dxf");
        if (!File.Exists(dxf) || File.GetLastWriteTimeUtc(dxf) < File.GetLastWriteTimeUtc(dwg)) { var r = LibreDwg.DwgToDxf(dwg, dxf); Assert.True(r.Ok, "dwg2dxf failed: " + r.Stderr); }
        return dxf;
    }
}
