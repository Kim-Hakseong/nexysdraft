using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;

namespace NexysDraft.Tests;

/// <summary>Repository paths and the generated sample set (samples/gen): written by our DXF writer, converted to DWG by dxf2dwg.</summary>
public static class Samples
{
    public static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }
    }
    public static string GatesDir { get { var p = Path.Combine(Root, "work", "gates"); Directory.CreateDirectory(p); return p; } }
    public static string WorkDir(string sub) { var p = Path.Combine(Root, "work", sub); Directory.CreateDirectory(p); return p; }
    public static string UserDir => Path.Combine(Root, "samples", "user");
    public static string GenDir => Path.Combine(Root, "samples", "gen");

    static readonly object _lock = new();
    /// <summary>All sample DWGs (user + generated); generates the gen set when missing.</summary>
    public static List<string> AllDwg()
    {
        lock (_lock) EnsureGenerated();
        var list = new List<string>();
        if (Directory.Exists(UserDir)) list.AddRange(Directory.GetFiles(UserDir, "*.dwg").OrderBy(x => x));
        list.AddRange(Directory.GetFiles(GenDir, "*.dwg").OrderBy(x => x));
        return list;
    }
    /// <summary>Generated DXF-only samples (no DWG twin).</summary>
    public static List<string> GenDxfOnly() { lock (_lock) EnsureGenerated(); return Directory.GetFiles(GenDir, "*.dxf").Where(f => !File.Exists(Path.ChangeExtension(f, ".dwg"))).OrderBy(x => x).ToList(); }

    public static void EnsureGenerated()
    {
        Directory.CreateDirectory(GenDir);
        var gens = new (string name, Func<Document> make)[] { ("g01_basic", Basic), ("g02_korean", Korean), ("g03_blocks_dims", BlocksDims), ("g04_curves", Curves), ("g05_100k", Big100k), ("g06_paper", Paper) };
        foreach (var (name, make) in gens)
        {
            string dxf = Path.Combine(GenDir, name + ".dxf"), dwg = Path.Combine(GenDir, name + ".dwg");
            if (File.Exists(dwg) && File.Exists(dxf)) continue;
            var doc = make();
            DxfWriter.Write(doc, dxf, DxfWriter.Mode.R2000);
            if (name == "g05_100k") continue;   // 100k entities: dxf2dwg 0.14 does not finish in 5 min (docs/blockers.md) — DXF only (render/open gate)
            var r = LibreDwg.DxfToDwg(dxf, dwg, "r2000", 180000);
            if (!r.Ok) File.WriteAllText(Path.Combine(GenDir, name + ".dxf2dwg.err.txt"), r.Stderr);
        }
    }

    public static Document NewDoc()
    {
        var d = new Document { CodePage = "ANSI_949", Version = "AC1015" };
        d.Layers.Add(new Layer { Name = "0", Handle = d.NewHandle(), Dirty = true });
        d.Layers.Add(new Layer { Name = "WALL", Color = 1, Handle = d.NewHandle(), Dirty = true });
        d.Layers.Add(new Layer { Name = "TEXT", Color = 3, Handle = d.NewHandle(), Dirty = true });
        d.Layers.Add(new Layer { Name = "DIM", Color = 4, Handle = d.NewHandle(), Dirty = true });
        d.Linetypes.Add(new LineType { Name = "DASHED", Description = "Dashed __ __", Pattern = { 0.5, -0.25 }, Handle = d.NewHandle() });
        d.Styles.Add(new TextStyle { Name = "Standard", Font = "txt", Handle = d.NewHandle() });
        d.Styles.Add(new TextStyle { Name = "KOR", Font = "malgun.ttf", Handle = d.NewHandle() });
        d.Blocks["*Model_Space"] = new Block { Name = "*Model_Space", RecordHandle = d.NewHandle(), Handle = d.NewHandle(), EndHandle = d.NewHandle() };
        d.Blocks["*Paper_Space"] = new Block { Name = "*Paper_Space", RecordHandle = d.NewHandle(), Handle = d.NewHandle(), EndHandle = d.NewHandle() };
        d.BlockOrder.Add("*Model_Space"); d.BlockOrder.Add("*Paper_Space");
        return d;
    }
    static void Add(Document d, Entity e, string layer = "0") { e.Handle = d.NewHandle(); e.Layer = layer; e.Dirty = true; e.Owner = d.ModelSpaceHandle; d.Entities.Add(e); }

    public static Document Basic()
    {
        var d = NewDoc();
        Add(d, new Line { P0 = new V3(0, 0), P1 = new V3(100, 50) }, "WALL");
        Add(d, new Line { P0 = new V3(0, 50), P1 = new V3(100, 0), Linetype = "DASHED", Color = 2 });
        Add(d, new Circle { Center = new V3(50, 25), Radius = 20 }, "WALL");
        Add(d, new Arc { Center = new V3(150, 25), Radius = 15, StartDeg = 30, EndDeg = 250 });
        Add(d, new Ellipse { Center = new V3(200, 25), Major = new V3(30, 10), Ratio = 0.5, StartParam = 0, EndParam = Math.PI * 2 });
        Add(d, new Ellipse { Center = new V3(260, 25), Major = new V3(20, 0), Ratio = 0.4, StartParam = 0.5, EndParam = 4.0 });
        var pl = new LwPolyline { Closed = true }; pl.Vertices.Add(new PVertex { X = 0, Y = 100 }); pl.Vertices.Add(new PVertex { X = 50, Y = 100, Bulge = 0.5 }); pl.Vertices.Add(new PVertex { X = 50, Y = 150 }); pl.Vertices.Add(new PVertex { X = 0, Y = 150, Bulge = -0.3 }); Add(d, pl, "WALL");
        var hp = new Polyline { Flags = 0 }; hp.Vertices.Add(new PVertex { X = 100, Y = 100 }); hp.Vertices.Add(new PVertex { X = 150, Y = 120, Bulge = 0.2 }); hp.Vertices.Add(new PVertex { X = 200, Y = 100 }); Add(d, hp);
        Add(d, new Text { Pos = new V3(0, 200), Align = new V3(0, 200), Height = 5, Value = "LINE TEXT 123", Style = "Standard" }, "TEXT");
        Add(d, new Text { Pos = new V3(100, 200), Align = new V3(150, 200), Height = 5, Value = "CENTERED", HAlign = 1, VAlign = 2, Rotation = 15 }, "TEXT");
        Add(d, new MText { Pos = new V3(0, 250), Height = 4, Width = 80, Value = "First line\\PSecond {\\fArial|b1|i0|c0|p34;bold} end", Attach = 1 }, "TEXT");
        Add(d, new Solid { P0 = new V3(300, 0), P1 = new V3(320, 0), P2 = new V3(300, 20), P3 = new V3(320, 20) });
        Add(d, new Point { Pos = new V3(350, 10) });
        var h = new Hatch { Pattern = "SOLID", Solid = true, Color = 8 }; var loop = new HatchLoop { Type = 2 }; var ed = new HatchEdge { Type = 0, PolyClosed = true }; ed.Poly.Add(new PVertex { X = 300, Y = 100 }); ed.Poly.Add(new PVertex { X = 340, Y = 100 }); ed.Poly.Add(new PVertex { X = 340, Y = 130 }); ed.Poly.Add(new PVertex { X = 300, Y = 130 }); loop.Edges.Add(ed); h.Loops.Add(loop); Add(d, h);
        return d;
    }

    public static Document Korean()
    {
        var d = NewDoc();
        string[] words = { "고온고압 시험설비", "압력 전송기 PT-101", "밸브 (V-12) 열림", "제어반 배선도", "주의: 고전압", "온도 ±0.5℃", "직경 Ø25 mm", "한글 TEXT", "맑은 고딕 폴백", "㈜넥시스테크" };
        for (int i = 0; i < words.Length; i++) Add(d, new Text { Pos = new V3(0, i * 10), Align = new V3(0, i * 10), Height = 5, Value = words[i], Style = i % 2 == 0 ? "KOR" : "Standard" }, "TEXT");
        Add(d, new MText { Pos = new V3(200, 0), Height = 4, Width = 120, Value = "{\\fGulimChe|b1|i0|c129|p49;한글 MTEXT 첫 줄}\\P{\\f맑은 고딕|b0|i0|c129|p50;두 번째 줄 \\U+2460 \\U+00B1 0.5}\\P세 번째 {\\H1.5x;큰 글자}", Attach = 1 }, "TEXT");
        Add(d, new MText { Pos = new V3(200, 60), Height = 4, Width = 100, Value = "{\\fHeadline R|b0|i0|c129|p18;(주)넥시스테크}\\P분수 \\S1^2; 끝", Attach = 1 }, "TEXT");
        Add(d, new Text { Pos = new V3(0, 120), Align = new V3(0, 120), Height = 5, Value = "ESC \\U+D55C\\U+AE00 unicode", Style = "Standard" }, "TEXT");
        return d;
    }

    public static Document BlocksDims()
    {
        var d = NewDoc();
        var b = new Block { Name = "VALVE", Handle = d.NewHandle(), EndHandle = d.NewHandle(), RecordHandle = d.NewHandle() };
        void BA(Entity e) { e.Handle = d.NewHandle(); e.Layer = "0"; e.Dirty = true; e.Owner = b.RecordHandle; b.Entities.Add(e); }
        BA(new Line { P0 = new V3(-5, -5), P1 = new V3(5, 5), Color = 0 }); BA(new Line { P0 = new V3(-5, 5), P1 = new V3(5, -5), Color = 0 }); BA(new Circle { Center = new V3(0, 0), Radius = 7 }); BA(new Text { Pos = new V3(0, 9), Align = new V3(0, 9), Height = 2, Value = "V", HAlign = 1, VAlign = 0 });
        BA(new Text { Pos = new V3(0, -12), Align = new V3(0, -12), Height = 2.5, Value = "TAG", IsAttdef = true, AttribTag = "TAG", Prompt = "Tag" });
        d.Blocks["VALVE"] = b; d.BlockOrder.Add("VALVE");
        for (int i = 0; i < 12; i++)
        {
            var ins = new Insert { Block = "VALVE", Pos = new V3(i * 30, 0), SX = 1 + i % 3 * 0.5, SY = 1 + i % 3 * 0.5, Rotation = i * 15 };
            var at = new Text { Pos = new V3(i * 30, -12), Align = new V3(i * 30, -12), Height = 2.5, Value = $"V-{i + 1:D3}", IsAttrib = true, AttribTag = "TAG", Handle = d.NewHandle(), Dirty = true, Layer = "0" };
            ins.Attribs.Add(new Attrib { Tag = "TAG", Value = at.Value, Text = at, Handle = at.Handle });
            Add(d, ins, "WALL");
        }
        var ed = new Core.Edit.Editor(d);
        ed.AddLinearDimension(new V3(0, 30), new V3(100, 30), new V3(50, 50), 0, "DIM");
        ed.AddLinearDimension(new V3(0, 30), new V3(0, 130), new V3(-20, 80), 90, "DIM");
        ed.AddLinearDimension(new V3(100, 30), new V3(180, 90), new V3(150, 40), 0, "DIM");
        foreach (var e in d.Entities) e.Owner = d.ModelSpaceHandle;
        var h = new Hatch { Pattern = "ANSI31", Solid = false, PatternAngle = 45, PatternScale = 2, PatternType = 1 }; var loop = new HatchLoop { Type = 1 };
        loop.Edges.Add(new HatchEdge { Type = 1, P0 = new V3(200, 0), P1 = new V3(260, 0) }); loop.Edges.Add(new HatchEdge { Type = 2, Center = new V3(260, 20), Radius = 20, StartDeg = 270, EndDeg = 90, Ccw = true }); loop.Edges.Add(new HatchEdge { Type = 1, P0 = new V3(260, 40), P1 = new V3(200, 40) }); loop.Edges.Add(new HatchEdge { Type = 1, P0 = new V3(200, 40), P1 = new V3(200, 0) });
        h.Loops.Add(loop); h.Tail.Add(new Group(78, "1")); h.Tail.Add(new Group(53, "45.0")); h.Tail.Add(new Group(43, "0.0")); h.Tail.Add(new Group(44, "0.0")); h.Tail.Add(new Group(45, "-2.2450640303")); h.Tail.Add(new Group(46, "2.2450640303")); h.Tail.Add(new Group(79, "0")); Add(d, h);
        return d;
    }

    /// <summary>Model content plus a Layout1 sheet in paper space (frame, title block, Korean title) - the paper-space round-trip sample.</summary>
    public static Document Paper()
    {
        var d = Basic();
        string ps = d.PaperSpaceHandle;
        void P(Entity e) { e.Handle = d.NewHandle(); e.Layer = "0"; e.Dirty = true; e.PaperSpace = true; e.Owner = ps; d.Entities.Add(e); }
        foreach (var (x0, y0, x1, y1) in new[] { (0.0, 0.0, 420.0, 0.0), (420.0, 0.0, 420.0, 297.0), (420.0, 297.0, 0.0, 297.0), (0.0, 297.0, 0.0, 0.0), (300.0, 0.0, 300.0, 40.0), (300.0, 40.0, 420.0, 40.0) }) P(new Line { P0 = new V3(x0, y0), P1 = new V3(x1, y1) });
        P(new Text { Pos = new V3(305, 15), Align = new V3(305, 15), Height = 5, Value = "배치 제목 LAYOUT-1", Style = "KOR" });
        P(new Circle { Center = new V3(30, 30), Radius = 10 });
        return d;
    }

    public static Document Curves()
    {
        var d = NewDoc();
        var sp = new Spline { Degree = 3, Flags = 8 }; sp.Control.AddRange(new[] { new V3(0, 0), new V3(20, 40), new V3(60, -20), new V3(100, 30), new V3(140, 0) }); sp.Knots.AddRange(new double[] { 0, 0, 0, 0, 0.4, 0.7, 1, 1, 1, 1 }); Add(d, sp);
        for (int i = 0; i < 20; i++) { var pl = new LwPolyline { ConstWidth = 0.5 * (i % 4) }; for (int k = 0; k < 8; k++) pl.Vertices.Add(new PVertex { X = i * 20 + k * 2, Y = 100 + Math.Sin(k + i) * 10, Bulge = k % 3 == 0 ? 0.4 : 0, StartWidth = 0.5 * (i % 4), EndWidth = 0.5 * (i % 4) }); Add(d, pl, "WALL"); }
        for (int i = 0; i < 10; i++) Add(d, new Arc { Center = new V3(i * 25, 200), Radius = 10, StartDeg = i * 36, EndDeg = i * 36 + 200 });
        for (int i = 0; i < 10; i++) Add(d, new Ellipse { Center = new V3(i * 25, 250), Major = new V3(10 * Math.Cos(i), 10 * Math.Sin(i)), Ratio = 0.3 + 0.05 * i, StartParam = 0, EndParam = i % 2 == 0 ? Math.PI * 2 : 2.5 });
        var h = new Hatch { Pattern = "SOLID", Solid = true, Color = 5 }; var loop = new HatchLoop { Type = 1 }; loop.Edges.Add(new HatchEdge { Type = 3, Center = new V3(300, 200), Major = new V3(30, 0), Ratio = 0.5, StartDeg = 0, EndDeg = 360, Ccw = true }); h.Loops.Add(loop); Add(d, h);
        return d;
    }

    /// <summary>100,000 entities: lines, circles, polylines, text in a grid (render/open gate).</summary>
    public static Document Big100k()
    {
        var d = NewDoc(); var rng = new Random(7);
        int n = 0;
        for (int gy = 0; gy < 400 && n < 100000; gy++)
            for (int gx = 0; gx < 250 && n < 100000; gx++)
            {
                double x = gx * 40, y = gy * 40; int k = (gx + gy) % 4;
                Entity e = k switch
                {
                    0 => new Line { P0 = new V3(x, y), P1 = new V3(x + 30, y + 20 + rng.NextDouble() * 10) },
                    1 => new Circle { Center = new V3(x + 15, y + 15), Radius = 5 + rng.NextDouble() * 8 },
                    2 => MakePl(x, y, rng),
                    _ => new Text { Pos = new V3(x, y + 10), Align = new V3(x, y + 10), Height = 3, Value = $"T{gx}-{gy}" },
                };
                Add(d, e, k == 3 ? "TEXT" : k == 0 ? "WALL" : "0"); n++;
            }
        return d;
    }
    static LwPolyline MakePl(double x, double y, Random rng) { var p = new LwPolyline { Closed = true }; for (int i = 0; i < 6; i++) p.Vertices.Add(new PVertex { X = x + rng.NextDouble() * 30, Y = y + rng.NextDouble() * 30, Bulge = i == 2 ? 0.3 : 0 }); return p; }
}
