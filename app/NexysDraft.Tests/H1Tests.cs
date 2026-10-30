using System.Text;
using System.Text.RegularExpressions;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft4 H1 (DXF version on save), H2 (DWG without IMAGE) and the u04 audit behind H0 (style use, ACAD xdata, carets).</summary>
public class H1Tests
{
    readonly ITestOutputHelper _o;
    public H1Tests(ITestOutputHelper o) { _o = o; }

    static string AcadVer(string dxf) { var m = Regex.Match(File.ReadAllText(dxf, Encoding.Latin1), @"\$ACADVER\s*\r?\n\s*1\s*\r?\n(\w+)"); return m.Success ? m.Groups[1].Value : "?"; }

    /// <summary>Model-space counts of what the writer carries (R2000: R2000 entity types with resolved blocks; Preserve: everything).</summary>
    static Dictionary<string, int> Carried(Document a, DxfWriter.Mode mode, bool dropImages = false)
    {
        bool written(Entity e) => mode == DxfWriter.Mode.Preserve || (R2000Loss.R2000Entities.Contains(e.DxfName) && !(e is Insert ins && (ins.Block.Length == 0 || !a.Blocks.ContainsKey(ins.Block))));
        return a.Entities.Where(e => !e.PaperSpace && e is not Unknown && written(e) && !(dropImages && e.DxfName == "IMAGE")).GroupBy(e => e.DxfName).ToDictionary(g => g.Key, g => g.Count());
    }
    static Dictionary<string, int> Model(Document b) => b.Entities.Where(e => !e.PaperSpace && e is not Unknown).GroupBy(e => e.DxfName).ToDictionary(g => g.Key, g => g.Count());

    [Fact]
    public void Gate_H1_DefaultDxf_OpensInNanoCadVersion()
    {
        var lines = new List<string> { $"# H1: default DXF save (auto: R2000 when the original is newer than {DxfSave.NanoCadNewest}) — $ACADVER ≤ {DxfSave.NanoCadNewest}, ODA read errors 0, model counts / layers / text / Korean / paper space as carried" };
        if (!Directory.Exists(Samples.UserDir)) { lines.Add("SKIP\tno user drawings"); File.WriteAllLines(Path.Combine(Samples.GatesDir, "h1_dxf_version.txt"), lines); return; }
        bool oda = OdaGateTests.Converter() != null; int pass = 0, n = 0;
        foreach (var f in Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x))
        {
            n++;
            var a = DxfReader.Read(OdaGateTests.Dxf(f)); var (mode, reason) = DxfSave.Choose(a, DxfSave.Ver.Auto); var fixes = FontCompat.Plan(a);
            string outp = Path.Combine(Samples.WorkDir("h1"), Path.GetFileNameWithoutExtension(f) + ".nexys.dxf");
            var warn = DxfWriter.Write(a, outp, mode, fixes);
            string ver = AcadVer(outp); bool verOk = string.CompareOrdinal(ver, DxfSave.NanoCadNewest) <= 0;
            var a0 = DxfReader.Read(OdaGateTests.Dxf(f));   // fresh copy: the writer assigns handles
            string odaCol = "ODA SKIP"; bool rowOk = verOk;
            if (oda)
            {
                var (o, err, _) = OdaGateTests.Convert(outp);
                if (o == null) { rowOk = false; odaCol = "ODA ERR " + err; }
                else
                {
                    var b = DxfReader.Read(o); var ca = Carried(a0, mode); var cb = Model(b);
                    bool countsOk = ca.All(kv => cb.GetValueOrDefault(kv.Key) == kv.Value), layersOk = OdaGateTests.LayersSame(a0, b);
                    int ta = a0.Entities.Count(e => !e.PaperSpace && (e is Text t && !t.IsAttrib || e is MText)), tb = b.Entities.Count(e => !e.PaperSpace && (e is Text t && !t.IsAttrib || e is MText));
                    var ka = OdaGateTests.Korean(a0); var kb = OdaGateTests.Korean(b); bool koreanOk = ka.SequenceEqual(kb);
                    int pa = G1Tests.Spaces(a0).paper, pb = G1Tests.Spaces(b).paper;
                    rowOk &= string.IsNullOrEmpty(err) && countsOk && layersOk && ta == tb && koreanOk && pa == pb;
                    odaCol = $"ODA err [{err}]\tmodel counts {(countsOk ? "same" : "DIFF " + OdaGateTests.CountsText(ca) + " | " + OdaGateTests.CountsText(cb))}\tlayers {(layersOk ? "same" : "DIFF")} {a0.Layers.Count}/{b.Layers.Count}\ttext+mtext {ta}/{tb}\tkorean {ka.Count}/{kb.Count} same {koreanOk}\tpaper {pa}/{pb}";
                    if (!koreanOk) odaCol += "\tkorean diff: " + string.Join(" | ", ka.Except(kb).Take(4)) + " ⇐⇒ " + string.Join(" | ", kb.Except(ka).Take(4));
                }
            }
            if (rowOk) pass++;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{Path.GetFileName(f)}\tsource {a0.Version} → {ver} ({reason})\tfont fixes {fixes.Count}\t{odaCol}{(warn.Count > 0 ? "\twriter: " + string.Join("; ", warn.Take(2)) : "")}");
        }
        lines.Insert(1, $"SUMMARY\t{pass}/{n} pass");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "h1_dxf_version.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(pass == n, string.Join("\n", lines));
    }

    [Fact]
    public void Gate_H2_U06_DwgWithoutImage()
    {
        string u06 = Path.Combine(Samples.UserDir, "u06_panel.dwg"); if (!File.Exists(u06)) return;
        var a = DxfReader.Read(OdaGateTests.Dxf(u06)); var pre = R2000Check.Run(a, false);
        var lines = new List<string> { "# H2: u06 saved as DWG without its IMAGE entities — pre-check offers it (IMAGE is the only blocking reason), ODA errors 0, other model counts equal", $"pre-check: imageOnly {pre.ImageOnly} images {pre.ImageCount} blocking [{string.Join(", ", pre.Items.Where(i => i.Blocking).Select(i => i.Kind))}]" };
        string r2000 = Path.Combine(Samples.WorkDir("h2"), "u06_panel.noimage.dxf"), dwg = Path.Combine(Samples.WorkDir("h2"), "u06_panel.noimage.dwg");
        var images = a.Entities.Where(e => e.DxfName == "IMAGE").Select(e => e.Handle).ToList();
        DxfWriter.Write(a, r2000, DxfWriter.Mode.R2000, FontCompat.Plan(a), dropImages: true);
        var r = LibreDwg.DxfToDwg(r2000, dwg); bool ok = pre.ImageOnly && r.Ok;
        lines.Add($"dropped IMAGE: {string.Join(" ", images)}\tdxf2dwg {(r.Ok ? "ok" : "FAILED " + r.Stderr)} {r.Ms:F0} ms");
        if (r.Ok && OdaGateTests.Converter() != null)
        {
            var (o, err, _) = OdaGateTests.Convert(dwg);
            var a0 = DxfReader.Read(OdaGateTests.Dxf(u06));
            if (o == null) { ok = false; lines.Add("ODA ERR " + err); }
            else
            {
                var b = DxfReader.Read(o); var ca = Carried(a0, DxfWriter.Mode.R2000, true); var cb = Model(b);
                bool countsOk = ca.All(kv => cb.GetValueOrDefault(kv.Key) == kv.Value) && !cb.ContainsKey("IMAGE");
                var ka = OdaGateTests.Korean(a0); var kb = OdaGateTests.Korean(b);
                ok &= string.IsNullOrEmpty(err) && countsOk && ka.SequenceEqual(kb);
                lines.Add($"ODA err [{err}]\tmodel counts {(countsOk ? "same" : "DIFF")}: {OdaGateTests.CountsText(ca)} | {OdaGateTests.CountsText(cb)}\tkorean {ka.Count}/{kb.Count} same {ka.SequenceEqual(kb)}");
            }
        }
        lines.Insert(1, ok ? "PASS" : "FAIL");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "h2_u06_noimage.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }

    /// <summary>u04 audit (H0): which styles its TEXT use, MTEXT inline fonts, what the DWG carries in the rewritten STYLE records (xdata must stay on
    /// the ACAD APPID), and carets in SHX-coded Korean text surviving our DXF (ODA reads "^ X" as "^X").</summary>
    [Fact]
    public void Gate_H0_U04_StyleAudit()
    {
        string u04 = Path.Combine(Samples.UserDir, "u04_hthp_pfd.dwg"); if (!File.Exists(u04)) return;
        var a = DxfReader.Read(OdaGateTests.Dxf(u04)); var use = FontCompat.KoreanUse(a); var fixes = FontCompat.Plan(a);
        var lines = new List<string> { "# H0 u04 audit: TEXT per style (model space / all incl. blocks, Korean), MTEXT inline fonts, fixes, the DWG's STYLE records via ODA, carets" };
        var modelTexts = a.Entities.OfType<Text>().Where(t => !t.IsAttrib && !t.PaperSpace).ToList();
        IEnumerable<Entity> All() { foreach (var e in a.Entities) yield return e; foreach (var b in a.Blocks.Values) foreach (var e in b.Entities) yield return e; }
        var allTexts = All().OfType<Text>().Where(t => !t.IsAttrib).ToList();
        lines.Add($"TEXT model {modelTexts.Count}, all {allTexts.Count}; MTEXT model {a.Entities.OfType<MText>().Count()}");
        foreach (var g in allTexts.GroupBy(t => t.Style).OrderByDescending(g => g.Count()))
        {
            var st = a.Styles.FirstOrDefault(s => s.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase));
            int km = modelTexts.Count(t => t.Style == g.Key && t.Value.Any(c => c >= 0x80));
            lines.Add($"style {g.Key}\tmodel {modelTexts.Count(t => t.Style == g.Key)} (Korean {km})\tall {g.Count()} (Korean {g.Count(t => t.Value.Any(c => c >= 0x80))})\tfont \"{st?.Font}\" big \"{st?.BigFont}\" family \"{st?.TrueTypeFamily}\" ok {(st != null && FontCompat.StyleOk(st))}\tfix {(fixes.FirstOrDefault(x => x.Style == g.Key) is { } fx ? $"{fx.Mode} → \"{fx.NewFile}\" + {fx.NewFamily}" : "-")}");
        }
        var inline = All().OfType<MText>().SelectMany(m => Regex.Matches(m.Value, @"\\[fF]([^|;]*)").Select(x => x.Groups[1].Value)).GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}").ToList();
        lines.Add($"MTEXT inline fonts: {(inline.Count == 0 ? "none" : string.Join(", ", inline))}");
        bool ok = true;
        // the DWG
        string r2000 = Path.Combine(Samples.WorkDir("h0"), "u04.r2000.dxf"), dwg = Path.Combine(Samples.WorkDir("h0"), "u04.nexys.dwg");
        DxfWriter.Write(a, r2000, DxfWriter.Mode.R2000, fixes); var r = LibreDwg.DxfToDwg(r2000, dwg); ok &= r.Ok;
        string pres = Path.Combine(Samples.WorkDir("h0"), "u04.preserve.dxf"); DxfWriter.Write(DxfReader.Read(OdaGateTests.Dxf(u04)), pres, DxfWriter.Mode.Preserve, fixes);
        if (OdaGateTests.Converter() != null && r.Ok)
        {
            var (o, err, _) = OdaGateTests.Convert(dwg); var sb = o == null ? new() : H0Tests.StyleBlocks(o);
            foreach (var fx in fixes) { string v = sb.GetValueOrDefault(fx.Style, "MISSING"); ok &= v.Contains("1001=\"ACAD\"") && v.Contains($"3=\"{fx.NewFile}\""); lines.Add($"DWG via ODA: {fx.Style}\t{v}"); }
            int app = File.ReadAllLines(o!, Encoding.Latin1).Count(l => l.StartsWith("_APP"));
            lines.Add($"DWG via ODA: err [{err}]; xdata on unnamed applications (_APPn) {app} — non-ACAD xdata that dxf2dwg leaves unresolved");
            // carets: SHX-coded Korean in style SHS (nameless block, Preserve DXF only)
            var (o2, err2, _) = OdaGateTests.Convert(pres);
            var src = All().OfType<Text>().Where(t => t.Style == "SHS").Select(t => t.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var back = o2 == null ? new List<string>() : DxfReader.Read(o2).Blocks.Values.SelectMany(bl => bl.Entities).Concat(DxfReader.Read(o2).Entities).OfType<Text>().Where(t => t.Style == "SHS").Select(t => t.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();
            bool caretOk = src.Count > 0 && src.SequenceEqual(back);
            ok &= caretOk && string.IsNullOrEmpty(err2);
            lines.Add($"carets: SHS texts {src.Count} → ODA(our Preserve DXF) {back.Count}, identical {caretOk}, with '^' {src.Count(s => s.Contains('^'))}; err [{err2}]");
        }
        lines.Insert(1, ok ? "PASS" : "FAIL");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "h0_u04_styles.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
