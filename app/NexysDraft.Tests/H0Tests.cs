using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NexysDraft.Tests;

/// <summary>draft4 H0: font matrix for the nanoCAD 5 check (one style per <see cref="FontCompatMode"/>), and the u04 style audit.</summary>
public class H0Tests
{
    readonly ITestOutputHelper _o;
    public H0Tests(ITestOutputHelper o) { _o = o; }

    static readonly Dictionary<FontCompatMode, string> What = new()
    {
        [FontCompatMode.A] = "3 empty + xdata 굴림 (\\U+ escapes)",
        [FontCompatMode.B] = "3 empty + xdata Gulim (draft3)",
        [FontCompatMode.C] = "3 gulim.ttc + xdata Gulim",
        [FontCompatMode.D] = "3 malgun.ttf + xdata Malgun Gothic",
        [FontCompatMode.E] = "3 malgun.ttf, no xdata",
        [FontCompatMode.F] = "u01 'PDF Arial' copy",
        [FontCompatMode.G] = "3 empty + xdata Gulim, 1071 = 34 (diagnostic)",
    };

    public static Document Matrix(TextStyle? pdfArial)
    {
        var d = new Document { CodePage = "ANSI_949", Version = "AC1015" };
        d.Layers.Add(new Layer { Name = "0", Handle = d.NewHandle(), Dirty = true });
        d.Styles.Add(new TextStyle { Name = "Standard", Font = "txt", Handle = d.NewHandle() });
        d.Blocks["*Model_Space"] = new Block { Name = "*Model_Space", RecordHandle = d.NewHandle(), Handle = d.NewHandle(), EndHandle = d.NewHandle() };
        d.Blocks["*Paper_Space"] = new Block { Name = "*Paper_Space", RecordHandle = d.NewHandle(), Handle = d.NewHandle(), EndHandle = d.NewHandle() };
        d.BlockOrder.Add("*Model_Space"); d.BlockOrder.Add("*Paper_Space");
        int row = 0;
        foreach (var mode in Enum.GetValues<FontCompatMode>())
        {
            string x = mode.ToString(), name = "FM_" + x;
            TextStyle st = mode == FontCompatMode.F && pdfArial != null
                ? new TextStyle { Name = name, Font = pdfArial.Font, BigFont = pdfArial.BigFont, TrueTypeFamily = pdfArial.TrueTypeFamily, FontFlags = pdfArial.FontFlags, WidthFactor = pdfArial.WidthFactor, Handle = d.NewHandle() }
                : FontCompat.MatrixStyle(name, mode, d.NewHandle());
            d.Styles.Add(st);
            double y = -row * 20; row++;
            Add(d, new Text { Pos = new V3(0, y), Align = new V3(0, y), Height = 10, Value = x + " 한글테스트 ABC", Style = name });
            Add(d, new MText { Pos = new V3(260, y + 10), Height = 10, Width = 300, Value = x + " MTEXT 한글 엠텍스트", Attach = 1, Style = name });
        }
        Add(d, new Text { Pos = new V3(0, -row * 20 - 10), Align = new V3(0, -row * 20 - 10), Height = 5, Value = "NexysDraft font matrix — which letters show Korean in nanoCAD 5? (docs/font_matrix.md)", Style = "Standard" });
        return d;
    }
    static void Add(Document d, Entity e) { e.Handle = d.NewHandle(); e.Layer = "0"; e.Dirty = true; e.Owner = d.ModelSpaceHandle; d.Entities.Add(e); }

    /// <summary>STYLE records of a DXF file as raw lines (bytes shown as text, non-ASCII bytes as \xNN).</summary>
    public static Dictionary<string, string> StyleBlocks(string path)
    {
        var lines = File.ReadAllBytes(path).AsSpan().ToArray(); var text = Encoding.Latin1.GetString(lines).Replace("\r\n", "\n").Split('\n');
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i + 1 < text.Length; i += 2)
        {
            if (text[i].Trim() != "0" || text[i + 1].Trim() != "STYLE") continue;
            var sb = new List<string>(); string name = "";
            for (int j = i + 2; j + 1 < text.Length && text[j].Trim() != "0"; j += 2)
            {
                string c = text[j].Trim(), v = text[j + 1];
                if (c == "2") name = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(v)).Trim();   // ODA output is AC1032 (UTF-8)
                if (c is "2" or "3" or "4" or "70" or "1001" or "1000" or "1071") sb.Add(c + "=" + Show(v));
            }
            res[name] = string.Join(" · ", sb);
        }
        return res;
    }
    static string Show(string latin1)
    {
        var sb = new StringBuilder();
        foreach (var ch in latin1) { if (ch >= 0x80) sb.Append("\\x").Append(((int)ch).ToString("X2")); else sb.Append(ch); }
        return "\"" + sb.ToString().Trim() + "\"";
    }

    [Fact]
    public void Gate_H0_FontMatrix()
    {
        string dir = Samples.WorkDir("font_matrix");
        string u01 = Path.Combine(Samples.UserDir, "u01_pid_2504.dwg");
        TextStyle? pdfArial = null;
        if (File.Exists(u01)) { string dxf = Path.Combine(Samples.WorkDir("dxf"), "u01_pid_2504.dxf"); if (!File.Exists(dxf)) Assert.True(LibreDwg.DwgToDxf(u01, dxf).Ok); pdfArial = DxfReader.Read(dxf).Styles.FirstOrDefault(s => s.Name == "PDF Arial"); }
        var d = Matrix(pdfArial);
        string fdxf = Path.Combine(dir, "font_matrix.dxf"), fdwg = Path.Combine(dir, "font_matrix.dwg"), back = Path.Combine(dir, "font_matrix.dwg2dxf.dxf");
        DxfWriter.Write(d, fdxf, DxfWriter.Mode.R2000);
        var r = LibreDwg.DxfToDwg(fdxf, fdwg); Assert.True(r.Ok, r.Stderr);
        var r2 = LibreDwg.DwgToDxf(fdwg, back); Assert.True(r2.Ok, r2.Stderr);
        var ours = StyleBlocks(fdxf); var rb = StyleBlocks(back);
        var lines = new List<string> { "# H0 font matrix: STYLE records as written (R2000 DXF) and after dxf2dwg → dwg2dxf; ODA read of both files", $"# u01 'PDF Arial' source: {(pdfArial != null ? $"font \"{pdfArial.Font}\" family \"{pdfArial.TrueTypeFamily}\" 1071 {pdfArial.FontFlags}" : "u01 missing — F built from the constant")}" };
        bool ok = true;
        foreach (var mode in Enum.GetValues<FontCompatMode>())
        {
            string n = "FM_" + mode;
            lines.Add($"{mode}\t{What[mode]}\tDXF: {ours.GetValueOrDefault(n, "MISSING")}\tDWG→DXF: {rb.GetValueOrDefault(n, "MISSING")}");
            ok &= ours.ContainsKey(n) && rb.ContainsKey(n);
        }
        var bd = DxfReader.Read(back);
        int korean = bd.Entities.Count(e => (e is Text t && t.Value.Contains("한글테스트")) || (e is MText m && m.Value.Contains("한글")));
        lines.Add($"DWG readback (LibreDWG): {bd.Entities.Count} entities, Korean TEXT/MTEXT {korean} (expected {2 * Enum.GetValues<FontCompatMode>().Length})");
        ok &= korean == 2 * Enum.GetValues<FontCompatMode>().Length;
        if (OdaGateTests.Converter() != null)
            foreach (var f in new[] { fdxf, fdwg })
            {
                var (o, err, _) = OdaGateTests.Convert(f);
                int k = o == null ? -1 : DxfReader.Read(o).Entities.Count(e => (e is Text t && t.Value.Contains("한글테스트")) || (e is MText m && m.Value.Contains("한글")));
                var os = o == null ? new() : StyleBlocks(o);
                lines.Add($"ODA {Path.GetFileName(f)}: {(o == null ? "ERR" : "read")} err[{err}] Korean {k}; styles: {string.Join(" | ", Enum.GetValues<FontCompatMode>().Select(m => m + " " + os.GetValueOrDefault("FM_" + m, "MISSING")))}");
                ok &= o != null && string.IsNullOrEmpty(err) && k == 2 * Enum.GetValues<FontCompatMode>().Length && !os.Values.Any(v => v.Contains("_APP"));   // xdata must stay on the ACAD APPID
            }
        else lines.Add("ODA SKIP (config/dev.json)");
        File.WriteAllLines(Path.Combine(Samples.GatesDir, "h0_font_matrix.txt"), lines, new UTF8Encoding(true));
        foreach (var l in lines) _o.WriteLine(l);
        Assert.True(ok, string.Join("\n", lines));
    }
}
