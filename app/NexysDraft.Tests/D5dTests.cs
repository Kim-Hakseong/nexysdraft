using System.Globalization;
using System.Text;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft5d: files saved by NexysDraft would not open in AutoCAD (colleague report, 2026-11-02). No AutoCAD on the dev PC, so this gate checks
/// the structural rules AutoCAD enforces on DXF input that ODA and nanoCAD forgive: every 102 group closed and non-empty, no duplicate handle,
/// every handle below $HANDSEED, no pointer (330/340/350/360) to a handle that is not in the file, no empty table record name, root dictionary
/// first in OBJECTS, and every layer / linetype / text style / block / dimension style an entity names is defined.</summary>
public class D5dTests
{
    readonly ITestOutputHelper _o;
    public D5dTests(ITestOutputHelper o) { _o = o; }

    public sealed record Issue(string Kind, string Example);

    /// <summary>Structural problems of a DXF file (ASCII, as written by DxfWriter).</summary>
    public static List<Issue> Check(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.Latin1);
        var p = new List<(int c, string v)>();
        for (int i = 0; i + 1 < lines.Length; i += 2) if (int.TryParse(lines[i].Trim(), out int c)) p.Add((c, lines[i + 1].TrimEnd('\r')));
        var issues = new List<Issue>();
        string sec = "", cur = "", table = ""; long seed = -1; bool in102 = false; int n102 = 0; bool firstObject = true;
        var handles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var ptrs = new List<(string code, string h, string ent)>();
        var names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); var blocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new List<(string table, string name, string ent)>();
        HashSet<string> Names(string t) { if (!names.TryGetValue(t, out var s)) names[t] = s = new(StringComparer.OrdinalIgnoreCase); return s; }
        for (int i = 0; i < p.Count; i++)
        {
            var (c, v) = p[i];
            if (c == 0)
            {
                if (in102) issues.Add(new("unclosed 102 group", $"before {v} #{i}"));
                in102 = false; cur = v;
                if (v == "SECTION" && i + 1 < p.Count) { sec = p[i + 1].v; firstObject = true; }
                else if (v == "TABLE" && i + 1 < p.Count) table = p[i + 1].v;
                else if (v == "ENDTAB") table = "";
                else if (sec == "OBJECTS" && v != "ENDSEC") { if (firstObject && v != "DICTIONARY") issues.Add(new("first object is not the root dictionary", v)); firstObject = false; }
                continue;
            }
            if (c == 9 && v == "$HANDSEED" && i + 1 < p.Count) long.TryParse(p[i + 1].v.Trim(), NumberStyles.HexNumber, null, out seed);
            if (c == 102) { if (v.StartsWith("{")) { in102 = true; n102 = 0; } else { if (in102 && n102 == 0) issues.Add(new("empty 102 group", $"{cur} #{i}")); in102 = false; } continue; }
            if (in102) n102++;
            if (sec != "HEADER" && (c == 5 || (c == 105 && cur == "DIMSTYLE"))) handles[v.Trim()] = handles.GetValueOrDefault(v.Trim()) + 1;
            if (c is 330 or 340 or 350 or 360 && v.Trim() is { Length: > 0 } h && h != "0") ptrs.Add((c.ToString(), h, cur));
            if (sec == "TABLES" && table.Length > 0 && cur == table && c == 2) { if (v.Length == 0) issues.Add(new($"empty {table} name", $"#{i}")); Names(table).Add(v); }
            if (sec == "BLOCKS" && cur == "BLOCK" && c == 2) blocks.Add(v);
            if ((sec == "ENTITIES" || sec == "BLOCKS") && cur is not ("BLOCK" or "ENDBLK"))
            {
                if (c == 8) refs.Add(("LAYER", v, cur));
                else if (c == 6) refs.Add(("LTYPE", v, cur));
                else if (c == 7 && cur is "TEXT" or "MTEXT" or "ATTRIB" or "ATTDEF") refs.Add(("STYLE", v, cur));
                else if (c == 2 && cur == "INSERT") refs.Add(("BLOCK", v, cur));
                else if (c == 3 && cur == "DIMENSION") refs.Add(("DIMSTYLE", v, cur));
            }
        }
        foreach (var (h, n) in handles)
        {
            if (n > 1) issues.Add(new("duplicate handle", h));
            if (seed >= 0 && long.TryParse(h, NumberStyles.HexNumber, null, out long hv) && hv >= seed) issues.Add(new("handle >= $HANDSEED", h));
        }
        foreach (var (code, h, ent) in ptrs) if (!handles.ContainsKey(h)) issues.Add(new($"dangling {code} pointer", $"{ent} → {h}"));
        foreach (var (t, name, ent) in refs)
        {
            bool ok = t == "BLOCK" ? blocks.Contains(name) : t == "LTYPE" && (name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)) || Names(t).Contains(name);
            if (!ok) issues.Add(new($"undefined {t}", $"{ent} → {name}"));
        }
        foreach (var need in new[] { "*Model_Space", "*Paper_Space" }) { if (!blocks.Contains(need)) issues.Add(new("missing block", need)); if (!Names("BLOCK_RECORD").Contains(need)) issues.Add(new("missing block record", need)); }
        foreach (var b in blocks) if (!Names("BLOCK_RECORD").Contains(b)) issues.Add(new("BLOCK without BLOCK_RECORD", b));
        return issues;
    }

    static string Summary(List<Issue> issues) => issues.Count == 0 ? "0 issues" : $"{issues.Count} issue(s): " + string.Join("; ", issues.GroupBy(x => x.Kind).Select(g => $"{g.Key} ×{g.Count()} (e.g. {g.First().Example})"));

    [Fact]
    public void Gate_D5d_SavedDxf_AutoCadStructure()
    {
        var lines = new List<string> { "# draft5d: structure of the DXF files NexysDraft writes (app default DXF save of an opened DWG, and the R2000 DXF handed to dxf2dwg for a DWG save) — AutoCAD rules: closed non-empty 102 groups, unique handles < $HANDSEED, no dangling 330/340/350/360, no empty table names, root dictionary first, all named layers/linetypes/styles/blocks/dimstyles defined" };
        bool ok = true;
        var sources = new List<(string name, Func<Document> load)>();
        foreach (var f in Directory.Exists(Samples.UserDir) ? Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x) : Enumerable.Empty<string>())
            sources.Add((Path.GetFileName(f), () => { var d = DxfReader.Read(OdaGateTests.Dxf(f)); d.Path = f; d.FromLibreDwg = true; return d; }));
        foreach (var f in Directory.GetFiles(Samples.GenDir, "*.dxf").OrderBy(x => x)) sources.Add((Path.GetFileName(f), () => DxfReader.Read(f)));
        string dir = Samples.WorkDir("d5d");
        foreach (var (name, load) in sources)
        {
            var a = load(); var (mode, reason) = DxfSave.Choose(a, DxfSave.Ver.Auto);
            string auto = Path.Combine(dir, name + ".auto.dxf"); DxfWriter.Write(a, auto, mode, FontCompat.Plan(a), false, mode == DxfWriter.Mode.R2000);   // as Engine.SaveAsync (DXF)
            var r = load(); string r2000 = Path.Combine(dir, name + ".r2000.dxf"); var warn = DxfWriter.Write(r, r2000, DxfWriter.Mode.R2000, FontCompat.Plan(r));
            var ia = Check(auto); var ir = Check(r2000);
            string odaCol = "ODA SKIP"; bool odaOk = true;
            if (OdaGateTests.Converter() != null) { var (o, err, _) = OdaGateTests.Convert(auto); odaOk = o != null && string.IsNullOrEmpty(err); odaCol = odaOk ? "ODA ok" : "ODA ERR " + err; }
            bool rowOk = ia.Count == 0 && ir.Count == 0 && odaOk; ok &= rowOk;
            lines.Add($"{(rowOk ? "PASS" : "FAIL")}\t{name}\tauto ({reason}) {Summary(ia)}, {odaCol}\tR2000 for DWG {Summary(ir)}{(warn.Count > 0 ? "\twriter: " + string.Join("; ", warn) : "")}");
        }
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "d5d_autocad_structure.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }

    /// <summary>Negative control: the checker must flag the LibreDWG dwg2dxf output that the preserve path used to copy (u04: thousands of duplicate handles).</summary>
    [Fact]
    public void Checker_Flags_LibreDwgOutput()
    {
        string f = Path.Combine(Samples.UserDir, "u04_hthp_pfd.dwg"); if (!File.Exists(f)) return;
        var issues = Check(OdaGateTests.Dxf(f));
        _o.WriteLine(Summary(issues));
        Assert.Contains(issues, i => i.Kind == "duplicate handle");
    }
}
