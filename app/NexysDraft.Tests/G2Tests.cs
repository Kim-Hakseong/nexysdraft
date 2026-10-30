using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft3 G2 gate: the DWG pre-check (R2000Check) predicts the ODA outcome of the F2/G0 gate for ≥ 6 of the 7 user drawings.</summary>
public class G2Tests
{
    readonly ITestOutputHelper _o;
    public G2Tests(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Gate_G2_PreCheck_Matches_Oda()
    {
        string odaFile = Path.Combine(Samples.GatesDir, Environment.GetEnvironmentVariable("NEXYSDRAFT_G2_ODA") ?? "f2_oda_dwg.txt");
        var lines = new List<string> { $"# G2: DWG pre-check prediction (dwgOk) vs ODA result of {Path.GetFileName(odaFile)} — gate ≥ 6/7 user drawings match. ! = blocking (predicted unreadable), * = DXF recommended by policy (dynamic block)" };
        if (!Directory.Exists(Samples.UserDir) || !File.Exists(odaFile)) { lines.Add("SKIP\tno user drawings or no ODA gate file"); File.WriteAllLines(Path.Combine(Samples.GatesDir, "g2_check.txt"), lines, new UTF8Encoding(true)); return; }
        var oda = File.ReadAllLines(odaFile).Select(l => l.Split('\t')).Where(p => p.Length > 1 && (p[0] == "PASS" || p[0] == "FAIL")).ToDictionary(p => p[1].Split(' ')[0], p => p[0] == "PASS");
        int n = 0, match = 0;
        foreach (var f in Directory.GetFiles(Samples.UserDir, "*.dwg").OrderBy(x => x))
        {
            string name = Path.GetFileName(f); if (!oda.TryGetValue(name, out bool odaOk)) continue;
            string dxf = Path.Combine(Samples.WorkDir("dxf"), Path.GetFileNameWithoutExtension(f) + ".dxf");
            if (!File.Exists(dxf) || File.GetLastWriteTimeUtc(dxf) < File.GetLastWriteTimeUtc(f)) { var r = LibreDwg.DwgToDxf(f, dxf); Assert.True(r.Ok, r.Stderr); }
            var ck = R2000Check.Run(DxfReader.Read(dxf));
            n++; bool same = ck.DwgOk == odaOk; if (same) match++;
            string items = string.Join("; ", ck.Items.Select(i => $"{i.Kind}{(i.Blocking ? "!" : i.RecommendDxf ? "*" : "")}×{i.Count}"));
            lines.Add($"{(same ? "MATCH" : "MISS")}\t{name}\tpredict {(ck.DwgOk ? "readable" : "unreadable")}\tODA {(odaOk ? "PASS" : "FAIL")}\tdefault button {ck.Recommended}\test {ck.EstimateSeconds:F1} s\t{items}");
        }
        bool ok = n == 0 || match >= Math.Min(6, n);
        lines.Insert(1, $"{(ok ? "PASS" : "FAIL")}\tmatch {match}/{n}\tplanB {R2000Check.PlanB}");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "g2_check.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
