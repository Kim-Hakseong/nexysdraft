using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Dwg;
using NexysDraft.App.Services;

namespace NexysDraft.App;

/// <summary>
/// Command-line automation on the page: --selftest (real user path: open → edit → save → reopen compare → DWG export → PDF), --shot (capture),
/// --perf (100k entity render gates), --stress. Steps run as real DOM events inside the page (window.nx.test.*) and are awaited through the bridge.
/// </summary>
public static class SelfTest
{
    public static string[] Args = Array.Empty<string>();
    public static bool Enabled => Args.Length > 0 && Args[0] is "--selftest" or "--shot" or "--perf" or "--stress" or "--skilltest";
    static readonly List<string> Log = new(); static readonly List<string> Gate = new(); public static string OutDir = "";
    static void L(string s) { lock (Log) Log.Add($"{DateTime.Now:HH:mm:ss.fff} {s}"); Diag.Trace("selftest " + s); }
    static void G(string k, double v, string unit, string? gate = null) { bool ok = gate == null || Check(v, gate); var s = $"{k}\t{v:F3} {unit}" + (gate != null ? $"\tgate {gate}\t{(ok ? "PASS" : "FAIL")}" : ""); Gate.Add(s); L(s); }
    static void GB(string k, bool ok, string detail) { var s = $"{k}\t{(ok ? "PASS" : "FAIL")}\t{detail}"; Gate.Add(s); L(s); }
    static bool Check(double v, string gate) => gate.StartsWith("<=") ? v <= double.Parse(gate[2..], System.Globalization.CultureInfo.InvariantCulture) : gate.StartsWith(">=") ? v >= double.Parse(gate[2..], System.Globalization.CultureInfo.InvariantCulture) : true;
    static string Arg(string name, string dflt = "") { int i = Array.IndexOf(Args, name); return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : dflt; }
    static string Root() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent; return d?.FullName ?? Directory.GetCurrentDirectory(); }
    static string J(string s) => JsonSerializer.Serialize(s);

    public static async Task RunAsync(MainWindow w)
    {
        try
        {
            OutDir = Arg("--out", Path.Combine(Root(), "work", "selftest")); Directory.CreateDirectory(OutDir);
            await Idle(w, 300);
            w.Width += 1440 - w.Web.ActualWidth; w.Height += 900 - w.Web.ActualHeight; await Idle(w, 300);
            L($"page size {w.Web.ActualWidth}x{w.Web.ActualHeight} dpr {VisualTreeHelper.GetDpi(w).DpiScaleX} webview2 {w.RuntimeVersion}");
            if (Args[0] == "--shot") await ShotAsync(w); else if (Args[0] == "--perf") await PerfAsync(w); else if (Args[0] == "--stress") await StressAsync(w); else if (Args[0] == "--skilltest") await SkillTestAsync(w); else await FullAsync(w);
        }
        catch (Exception ex) { L("FATAL " + ex); }
        finally
        {
            File.WriteAllLines(Path.Combine(OutDir, "selftest.log"), Log); if (Gate.Count > 0) File.WriteAllLines(Path.Combine(OutDir, "gates.txt"), Gate);
            await Task.Delay(200); Application.Current.Shutdown(Gate.Any(g => g.EndsWith("FAIL") || g.Contains("\tFAIL\t")) ? 1 : 0);
        }
    }
    static async Task Idle(MainWindow w, int ms = 50) { await w.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); await Task.Delay(ms); }
    static async Task<JsonElement> JS(MainWindow w, string body, int timeoutMs = 120000) => await w.Bridge!.RunTest(body, timeoutMs);
    static async Task Shot(MainWindow w, string name) { await JS(w, "await nx.test.settle(120); return true;"); await Idle(w, 150); await w.CaptureAsync(Path.Combine(OutDir, name + ".png")); L($"shot {name}.png"); }
    static double[] Arr(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetDouble()).ToArray() : Array.Empty<double>();
    static void ReportFrames(string name, double[] f, string gate = "<=16") { if (f.Length == 0) { L(name + ": no frames"); return; } var s = f.OrderBy(x => x).ToArray(); G(name + "_median_ms", s[s.Length / 2], "ms", gate); G(name + "_p95_ms", s[(int)(s.Length * 0.95)], "ms"); G(name + "_max_ms", s[^1], "ms"); }
    static async Task<bool> Open(MainWindow w, string path) { bool ok = await w.Engine.OpenAsync(path); await JS(w, "await nx.test.settle(200); return true;"); for (int i = 0; i < 50; i++) { var st = await JS(w, "return nx.test.stats();"); if (st.TryGetProperty("ents", out var n) && n.GetInt32() > 0) break; await Task.Delay(100); } return ok; }

    // ================================================================== --shot
    static async Task ShotAsync(MainWindow w)
    {
        w.Engine.Theme = Arg("--theme", "light"); w.Engine.Touch();
        string open = Arg("--open"); if (open != "") await Open(w, open);
        if (Arg("--zoom") != "") { var z = Arg("--zoom").Split(','); if (z.Length == 4) await JS(w, $"nx.test.view.zoomWindow([{z[0]},{z[1]}],[{z[2]},{z[3]}]); return true;"); }
        if (Arg("--select") == "all") await JS(w, "await nx.test.cmd('all'); return true;");
        if (Arg("--check") == "1") await JS(w, "return await nx.test.saveCheck(true);");   // leaves the DWG pre-check dialog open for the capture
        await Shot(w, Arg("--name", "shot"));
        if (Arg("--texts") == "1") { var t = await JS(w, "return nx.test.texts();"); File.WriteAllText(Path.Combine(OutDir, Arg("--name", "shot") + "_texts.json"), t.GetRawText()); }
    }

    // ================================================================== --perf (D2 gates)
    static async Task PerfAsync(MainWindow w)
    {
        string root = Root(); string big = Arg("--open", Path.Combine(root, "samples", "gen", "g05_100k.dxf"));
        var sw = Stopwatch.StartNew(); bool ok = await Open(w, big); double wall = sw.Elapsed.TotalMilliseconds;
        var st = await JS(w, "return nx.test.stats();");
        double eng = w.Engine.OpenMs, ser = w.Engine.SnapshotMs, page = w.Engine.Perf.GetValueOrDefault("page_load_ms"), first = w.Engine.Perf.GetValueOrDefault("page_first_draw_ms");
        G("open_100k_total_ms", eng + ser + page + first, "ms", "<=2000"); G("open_100k_parse_ms", eng, "ms"); G("open_100k_tokenize_ms", w.Engine.Doc?.TokenizeMs ?? 0, "ms"); G("open_100k_parse_only_ms", w.Engine.Doc?.ParseMs ?? 0, "ms"); G("open_100k_serialize_ms", ser, "ms"); G("open_100k_page_load_ms", page, "ms"); G("open_100k_first_draw_ms", first, "ms"); G("open_100k_wall_incl_settle_ms", wall, "ms");
        GB("open_100k_entities", st.GetProperty("ents").GetInt32() >= 100000, $"ents={st.GetProperty("ents").GetInt32()} segs={st.GetProperty("segs").GetDouble()} webgl={st.GetProperty("webgl")}");
        await JS(w, "await nx.test.zoomExtents(); return true;"); await Shot(w, "perf_100k_extents");
        ReportFrames("pan_100k_60", Arr(await JS(w, "return await nx.test.pan(60);")));
        ReportFrames("zoom_100k_60", Arr(await JS(w, "return await nx.test.zoomSteps(60);")));
        ReportFrames("wheel_100k_30", Arr(await JS(w, "return await nx.test.wheel(30);")));
        ReportFrames("dragpan_100k", Arr(await JS(w, "return await nx.test.dragPan(200);")));
        await JS(w, "nx.test.view.zoomWindow([1000,1000],[1400,1300]); await nx.test.settle(); return true;"); await Shot(w, "perf_100k_zoomed");
        ReportFrames("pan_100k_zoomed_60", Arr(await JS(w, "return await nx.test.pan(60);")));
        var st2 = await JS(w, "return nx.test.stats();"); L("zoomed stats " + st2.GetRawText());
        G("memory_ws_mb", Process.GetCurrentProcess().WorkingSet64 / 1048576.0, "MB");
    }

    // ================================================================== --selftest (D5 user path)
    static async Task FullAsync(MainWindow w)
    {
        string root = Root(); string sample = Arg("--open", Path.Combine(root, "samples", "gen", "g03_blocks_dims.dwg"));
        string userDir = Path.Combine(root, "samples", "user");   // user drawings are never in the repository: a fresh clone has none → that part is skipped
        string user = Arg("--user", Directory.Exists(userDir) ? Directory.GetFiles(userDir, "*.dwg").OrderBy(f => new FileInfo(f).Length).FirstOrDefault() ?? "" : "");
        GB("libredwg_available", LibreDwg.Available, LibreDwg.Dir ?? "missing");
        // 1) open a DWG (LibreDWG) — user sample with Korean text
        if (user != "")
        {
            var sw = Stopwatch.StartNew(); bool ok = await Open(w, user); G("open_user_dwg_ms", sw.Elapsed.TotalMilliseconds, "ms"); GB("open_user_dwg", ok && w.Engine.Doc != null, Path.GetFileName(user) + $" ents={w.Engine.Doc?.Entities.Count} convert={w.Engine.ConvertMs:F0}ms");
            await JS(w, "await nx.test.zoomExtents(); return true;"); await Shot(w, "01_user_dwg_extents");
            var texts = await JS(w, "return nx.test.texts();");
            int korean = 0, broken = 0; var list = new List<string>();
            foreach (var t in texts.EnumerateArray()) { string s = t.GetProperty("text").GetString() ?? ""; if (s.Any(ch => ch is >= '가' and <= '힣')) { korean++; list.Add(s + " [" + t.GetProperty("font").GetString() + "]"); } if (s.Contains('�') || s.Count(ch => ch is >= 'À' and <= 'ÿ') >= 2) broken++; }
            GB("korean_texts_rendered", korean > 0 && broken == 0, $"korean={korean} broken={broken}"); File.WriteAllLines(Path.Combine(OutDir, "korean_texts.txt"), list);
            // zoom onto the first Korean text and capture
            var doc = w.Engine.Doc!; var kt = doc.Entities.OfType<Core.Model.Text>().FirstOrDefault(t => t.Value.Any(ch => ch is >= '가' and <= '힣')) as Core.Model.Entity ?? doc.Entities.OfType<Core.Model.MText>().FirstOrDefault(m => m.Value.Any(ch => ch is >= '가' and <= '힣'));
            if (kt != null) { var b = Core.Geometry.Bounds.Of(kt, doc, 0); if (b != null) { double cx = (b.Value.x0 + b.Value.x1) / 2, cy = (b.Value.y0 + b.Value.y1) / 2, hw = Math.Max(b.Value.x1 - b.Value.x0, 1) * 3; await JS(w, $"nx.test.view.zoomWindow([{cx - hw},{cy - hw * 0.4}],[{cx + hw},{cy + hw * 0.4}]); await nx.test.settle(); return true;"); await Shot(w, "02_user_dwg_korean_zoom"); } }
            await JS(w, "await nx.test.cmd('all'); return true;"); await Shot(w, "03_user_dwg_selected_all"); await JS(w, "await nx.test.key('Escape'); return true;");
        }
        // 2) open the generated sample and run the user path: select → move → copy → delete → draw → dim → text edit → layer → undo/redo
        var sw2 = Stopwatch.StartNew(); bool ok2 = await Open(w, sample); G("open_gen_dwg_ms", sw2.Elapsed.TotalMilliseconds, "ms"); GB("open_gen_dwg", ok2, Path.GetFileName(sample));
        var e = w.Engine; var d = e.Doc!; int n0 = d.Entities.Count;
        await JS(w, "await nx.test.zoomExtents(); return true;"); await Shot(w, "04_gen_extents");
        // window selection of the first inserts (x 0..100, y -15..15)
        var sel = await JS(w, "return await nx.test.selectWindow(-15, -20, 100, 20);");
        int nsel = sel.GetArrayLength(); GB("window_select", nsel >= 3, $"selected {nsel}");
        await JS(w, "await nx.test.cmd('m'); await nx.test.clickWorld(0, 0); await nx.test.clickWorld(0, 200); return true;");
        await Task.Delay(200); var moved = d.Entities.OfType<Core.Model.Insert>().Where(i => i.Pos.Y > 190).Count(); GB("move_via_command", moved >= 3, $"inserts at y≈200: {moved}");
        await JS(w, "await nx.test.cmd('co'); await nx.test.clickWorld(0, 200); await nx.test.clickWorld(300, 200); await nx.test.key('Escape'); return true;");
        await Task.Delay(200); GB("copy_via_command", d.Entities.Count >= n0 + 3, $"entities {n0} → {d.Entities.Count}");
        // snapping: hover near an insert's insertion point → INS snap; near a line end → END
        var line = d.Entities.OfType<Core.Model.Line>().FirstOrDefault(); await JS(w, "await nx.test.cmd('l'); return true;");
        if (line != null) { var sn = await JS(w, $"return await nx.test.hoverWorld({line.P1.X + 0.3}, {line.P1.Y + 0.2});"); GB("snap_endpoint", sn.ValueKind == JsonValueKind.Object && sn.TryGetProperty("k", out var k) && k.GetInt32() == 1, sn.GetRawText()); }
        await JS(w, "await nx.test.key('Escape'); return true;");
        // draw: line, polyline, circle, dimension via command line + clicks
        await JS(w, "await nx.test.cmd('l'); await nx.test.clickWorld(400, 0); await nx.test.clickWorld(500, 50); await nx.test.key('Escape'); await nx.test.cmd('c'); await nx.test.clickWorld(450, 120); await nx.test.cmd('25'); await nx.test.cmd('pl'); await nx.test.clickWorld(400,150); await nx.test.clickWorld(450,180); await nx.test.clickWorld(500,150); await nx.test.cmd('c'); await nx.test.cmd('dli'); await nx.test.clickWorld(400, 0); await nx.test.clickWorld(500, 0); await nx.test.clickWorld(450, -30); return true;");
        await Task.Delay(300);
        GB("draw_line_circle_pline_dim", d.Entities.OfType<Core.Model.Line>().Any(l => Math.Abs(l.P0.X - 400) < 1e-6 && Math.Abs(l.P1.Y - 50) < 1e-6) && d.Entities.OfType<Core.Model.Circle>().Any(c => Math.Abs(c.Radius - 25) < 1e-6) && d.Entities.OfType<Core.Model.LwPolyline>().Any(p => p.Closed && p.Vertices.Count == 3) && d.Entities.OfType<Core.Model.Dimension>().Count() >= 4, $"lines={d.Entities.OfType<Core.Model.Line>().Count()} circles={d.Entities.OfType<Core.Model.Circle>().Count()} plines={d.Entities.OfType<Core.Model.LwPolyline>().Count()} dims={d.Entities.OfType<Core.Model.Dimension>().Count()}");
        var newDim = d.Entities.OfType<Core.Model.Dimension>().LastOrDefault(); GB("dim_measurement_100", newDim != null && Math.Abs(newDim.Measurement - 100) < 1e-6, $"meas={newDim?.Measurement}");
        await JS(w, "await nx.test.zoomExtents(); return true;"); await Shot(w, "05_gen_after_draw");
        // text edit by double-click: the attribute text "V-001" of the first insert moved to y≈200 → edit TEXT entity instead (the block's TEXT is inside the insert; use a model-space TEXT)
        var txt = d.Entities.OfType<Core.Model.Text>().FirstOrDefault(t => !t.IsAttrib);
        if (txt == null) { e.Edit(ed => ed.Add(new Core.Model.Text { Pos = new Core.Model.V3(400, 250), Align = new Core.Model.V3(400, 250), Height = 8, Value = "EDIT ME" }, "TEXT", "Text")); await Task.Delay(150); txt = d.Entities.OfType<Core.Model.Text>().Last(); }
        var anchor = txt.Anchor; double tx = anchor.X + txt.Height * 0.4 * Math.Max(1, txt.Value.Length) * 0.5, ty = anchor.Y + txt.Height * 0.4;
        await JS(w, $"nx.test.view.zoomWindow([{anchor.X - 50},{anchor.Y - 30}],[{anchor.X + 80},{anchor.Y + 40}]); await nx.test.settle(); return true;");
        bool editing = (await JS(w, $"return await nx.test.dblclick({tx}, {ty});")).GetBoolean();
        if (!editing) { editing = (await JS(w, $"await nx.test.pick({tx}, {ty}); nx.test.view.editText('{txt.Handle}'); await nx.test.settle(); return !!nx.test.view.textEditEl;")).GetBoolean(); }
        await JS(w, "return await nx.test.typeText('한글 수정 TEXT');"); await Task.Delay(200);
        GB("text_edit_dblclick", d.Find(txt.Handle) is Core.Model.Text t2 && t2.Value == "한글 수정 TEXT", $"editing={editing} value={(d.Find(txt.Handle) as Core.Model.Text)?.Value}");
        await Shot(w, "06_text_edited");
        // layers: new layer via rail, move selection to it, toggle off/on
        await JS(w, "await nx.test.setLayerRailName('NEW-LAYER'); return true;"); await Task.Delay(150);
        GB("layer_new_via_rail", d.LayerOf("NEW-LAYER") != null && e.CurrentLayer == "NEW-LAYER", $"current={e.CurrentLayer}");
        await JS(w, $"await nx.test.pick({tx}, {ty}); return true;"); e.Edit(ed => ed.SetLayer(new[] { txt.Handle }, "NEW-LAYER")); await Task.Delay(100);
        await JS(w, "nx.test.layerClick('NEW-LAYER', 'off'); await nx.test.settle(150); return true;"); await Task.Delay(150);
        bool off = d.LayerOf("NEW-LAYER")!.Off; var st = await JS(w, "await nx.test.settle(100); return nx.test.stats();");
        GB("layer_off_hides", off, $"off={off} stats={st.GetRawText()[..Math.Min(120, st.GetRawText().Length)]}");
        await JS(w, "nx.test.layerClick('NEW-LAYER', 'off'); await nx.test.settle(150); return true;"); await Task.Delay(100);
        // undo / redo chain
        int before = d.Entities.Count; e.Undo(); e.Undo(); await Task.Delay(100); int after = d.Entities.Count; e.Redo(); e.Redo(); await Task.Delay(100);
        GB("undo_redo", d.Entities.Count == before, $"before={before} afterUndo2={after} afterRedo2={d.Entities.Count}");
        // 3) save DXF → reopen → compare; save DWG → reopen; PDF
        string outDir = Path.Combine(OutDir, "files"); Directory.CreateDirectory(outDir);
        string dxfOut = Path.Combine(outDir, "selftest_saved.dxf"); bool saved = await e.SaveAsync(dxfOut); GB("save_dxf", saved && File.Exists(dxfOut), dxfOut);
        var snap = Snapshot(d);
        var back = DxfReader.Read(dxfOut); var snap2 = Snapshot(back);
        GB("reopen_dxf_equal", snap.SequenceEqual(snap2) && back.Layers.Count == d.Layers.Count, $"ents {snap.Count} vs {snap2.Count}, layers {d.Layers.Count} vs {back.Layers.Count}");
        string dwgOut = Path.Combine(outDir, "selftest_saved.dwg"); var sw3 = Stopwatch.StartNew(); bool savedDwg = await e.SaveAsync(dwgOut); G("save_dwg_ms", sw3.Elapsed.TotalMilliseconds, "ms"); GB("save_dwg_libredwg", savedDwg && File.Exists(dwgOut), dwgOut);
        if (savedDwg)
        {
            bool reopened = await Open(w, dwgOut); var rd = e.Doc!; var snap3 = Snapshot(rd);
            int matched = snap3.Intersect(snap).Count();
            GB("reopen_dwg_coords", reopened && matched >= snap.Count - 2, $"signatures matched {matched} of {snap.Count} (LibreDWG round trip)");
            await JS(w, "await nx.test.zoomExtents(); return true;"); await Shot(w, "07_reopened_dwg");
        }
        string pdf = Path.Combine(outDir, "selftest.pdf"); var sw4 = Stopwatch.StartNew(); var pdfRes = await e.ExportPdfAsync(pdf); G("pdf_ms", sw4.Elapsed.TotalMilliseconds, "ms");
        GB("pdf_export_builtin", pdfRes != null && File.Exists(pdf) && new FileInfo(pdf).Length > 1000, pdfRes ?? e.StatusText);
        // searchable: the edited Korean TEXT and the dimension value must come back from pdftotext
        var ptt = PdfToText(pdf);
        GB("pdf_text_searchable", ptt == null || (ptt.Contains("한글수정TEXT") && ptt.Contains("100")), ptt == null ? "pdftotext not found → SKIP" : $"extracted {ptt.Length} chars, 한글수정TEXT={ptt.Contains("한글수정TEXT")} 100={ptt.Contains("100")}");
        // G2 save dialog, path 1: a clean generated drawing → "DWG 저장 가능", DWG is the default button (unless plan B)
        await Open(w, sample);
        var ck1 = await JS(w, "const r = await nx.test.saveCheck(true); nx.test.closeModal(); return r;");
        string want1 = Core.Dwg.R2000Check.PlanB ? "dxf" : "dwg";
        GB("save_dialog_dwg_ok_path", ck1.ValueKind == JsonValueKind.Object && ck1.GetProperty("primary").GetString() == want1, ck1.GetRawText());
        // path 2: a drawing with a blocking reason (a raster IMAGE entity, which made u06's DWG unreadable in ODA) → "DXF 권장", DXF default, DWG marked beta
        e.Edit(ed => ed.Add(new Core.Model.Unknown { Name = "IMAGE", Layer = "0" }, "0", "Raster image"));
        var ck2 = await JS(w, "const r = await nx.test.saveCheck(true); nx.test.closeModal(); return r;");
        GB("save_dialog_dxf_recommended_path", ck2.ValueKind == JsonValueKind.Object && ck2.GetProperty("primary").GetString() == "dxf" && ck2.GetProperty("beta").GetBoolean() && ck2.GetProperty("rows").GetInt32() >= 1, ck2.GetRawText());
        e.Undo();
        // design gate: no accent borders
        var acc = await JS(w, "return nx.test.accentScan();"); G("accent_border_count", acc.GetDouble(), "", "<=0");
        await JS(w, "await nx.test.key('Escape'); return true;");
        e.Theme = "dark"; e.Touch(); await Shot(w, "08_dark");
        e.Theme = "light"; e.Touch();
        await KeysAsync(w);
        await RightClickAsync(w);
        await HostKeysAsync(w);
        await CanvasThemeAsync(w);
        G("memory_ws_mb", Process.GetCurrentProcess().WorkingSet64 / 1048576.0, "MB", "<=600");
        G("ui_stalls_300ms", Diag.StallCount, "", "<=0");
    }

    // ================================================================== draft5 I1: AutoCAD keys and input, driven by real key / pointer events on the page
    static async Task KeysAsync(MainWindow w)
    {
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(200); nx.test.view.zoomExtents(); nx.test.closeModal(); await nx.test.settle(); return true;");
        var d = e.Doc!;
        static double AngleErr(Core.Model.Line l) { double a = Math.Atan2(l.P1.Y - l.P0.Y, l.P1.X - l.P0.X) * 180 / Math.PI; double m = ((a % 90) + 90) % 90; return Math.Min(m, 90 - m); }
        // (a) Shift held while picking the second point → temporary ortho: 10 lines, 5 mostly horizontal and 5 mostly vertical
        var js = new System.Text.StringBuilder();
        for (int i = 0; i < 10; i++) { double x0 = -500 + i * 60, y0 = -200 + (i % 3) * 40; double x1 = i % 2 == 0 ? x0 + 45 : x0 + 9, y1 = i % 2 == 0 ? y0 + 7 : y0 + 50; js.Append($"await nx.test.cmd('l'); await nx.test.clickWorld({x0}, {y0}); await nx.test.clickWorld({x1}, {y1}, {{ shift: true }}); await nx.test.key('Escape'); "); }
        await JS(w, js + "return true;"); await Task.Delay(200);
        var la = d.Entities.OfType<Core.Model.Line>().ToList();
        double errA = la.Count == 0 ? double.NaN : la.Max(AngleErr);
        GB("keys_a_shift_ortho_10_lines", la.Count == 10 && errA <= 1e-9, $"lines {la.Count}, max angle error {errA:E2}° (0/90 ±1e-9)");
        // (b) F8 ortho on, same picks without Shift
        var m0 = await JS(w, "await nx.test.key('F8'); return nx.test.modes();");
        js.Clear(); for (int i = 0; i < 10; i++) { double x0 = -500 + i * 60, y0 = 150 + (i % 3) * 40; double x1 = i % 2 == 0 ? x0 + 45 : x0 + 9, y1 = i % 2 == 0 ? y0 - 7 : y0 + 50; js.Append($"await nx.test.cmd('l'); await nx.test.clickWorld({x0}, {y0}); await nx.test.clickWorld({x1}, {y1}); await nx.test.key('Escape'); "); }
        await JS(w, js + "return true;"); await Task.Delay(200);
        var m1 = await JS(w, "await nx.test.key('F8'); return nx.test.modes();");
        var lb = d.Entities.OfType<Core.Model.Line>().Skip(la.Count).ToList(); double errB = lb.Count == 0 ? double.NaN : lb.Max(AngleErr);
        bool pillOn = m0.GetProperty("pills").EnumerateArray().Any(p => p.GetString() == "ortho:true");
        GB("keys_b_f8_ortho_10_lines", lb.Count == 10 && errB <= 1e-9 && m0.GetProperty("ortho").GetBoolean() && !m1.GetProperty("ortho").GetBoolean() && pillOn, $"lines {lb.Count}, max angle error {errB:E2}°, F8 → ortho {m0.GetProperty("ortho").GetBoolean()} (pill {pillOn}) → F8 → {m1.GetProperty("ortho").GetBoolean()}");
        // (c) polar coordinate entry @100<45 from 0,0
        int nl = d.Entities.OfType<Core.Model.Line>().Count();
        await JS(w, "await nx.test.cmd('l'); await nx.test.cmd('0,0'); await nx.test.cmd('@100<45'); await nx.test.key('Escape'); return true;"); await Task.Delay(150);
        var lc = d.Entities.OfType<Core.Model.Line>().Skip(nl).FirstOrDefault();
        GB("keys_c_polar_entry_100_45", lc != null && Math.Abs(lc.P0.X) < 1e-9 && Math.Abs(lc.P0.Y) < 1e-9 && Math.Abs(lc.P1.X - 70.7107) < 1e-4 && Math.Abs(lc.P1.Y - 70.7107) < 1e-4, lc == null ? "no line" : $"({lc.P0.X},{lc.P0.Y}) → ({lc.P1.X:F6},{lc.P1.Y:F6})");
        // direct distance entry along the ortho direction (Shift ortho from (300,-250) toward +x, then "80")
        await JS(w, "await nx.test.cmd('l'); await nx.test.cmd('300,-250'); await nx.test.hoverWorld(360, -244); nx.test.view.shiftDown = true; await nx.test.cmd('80'); nx.test.view.shiftDown = false; await nx.test.key('Escape'); return true;"); await Task.Delay(150);
        var ld = d.Entities.OfType<Core.Model.Line>().LastOrDefault();
        GB("keys_direct_distance_ortho", ld != null && Math.Abs(ld.P1.X - 380) < 1e-9 && Math.Abs(ld.P1.Y + 250) < 1e-9, ld == null ? "no line" : $"→ ({ld.P1.X:F6},{ld.P1.Y:F6}) expected (380,-250)");
        // (d) Space repeats the last command: three circles
        int nc = d.Entities.OfType<Core.Model.Circle>().Count();
        await JS(w, "await nx.test.cmd('c'); await nx.test.clickWorld(150, -300); await nx.test.cmd('10'); await nx.test.key(' '); await nx.test.clickWorld(210, -300); await nx.test.cmd('10'); await nx.test.key(' '); await nx.test.clickWorld(270, -300); await nx.test.cmd('10'); return true;"); await Task.Delay(200);
        int nc2 = d.Entities.OfType<Core.Model.Circle>().Count();
        GB("keys_d_space_repeat_3_circles", nc2 - nc == 3, $"circles {nc} → {nc2}");
        // (e) Esc in the middle of LINE (one point) and of MOVE (base point picked) → no entity added or moved
        int n0 = d.Entities.Count; var before = Snapshot(d);
        await JS(w, "await nx.test.cmd('l'); await nx.test.clickWorld(-100, 300); await nx.test.key('Escape'); await nx.test.pick(150, -300); await nx.test.cmd('m'); await nx.test.clickWorld(150, -300); await nx.test.key('Escape'); await nx.test.key('Escape'); return true;"); await Task.Delay(200);
        var mE = await JS(w, "return nx.test.modes();");
        GB("keys_e_esc_cancels", d.Entities.Count == n0 && Snapshot(d).SequenceEqual(before) && mE.GetProperty("tool").GetString() == "select", $"entities {n0} → {d.Entities.Count}, unchanged {Snapshot(d).SequenceEqual(before)}, tool {mE.GetProperty("tool").GetString()}");
        // (f) wheel double-click = zoom extents
        var vf = await JS(w, "nx.test.view.zoomAt(200, 200, 6); await nx.test.settle(); return await nx.test.middleDouble();");
        var ve = await JS(w, "await nx.test.zoomExtents(); return { cx: nx.test.view.view.cx, cy: nx.test.view.view.cy, scale: nx.test.view.view.scale };");
        bool same = Math.Abs(vf.GetProperty("scale").GetDouble() - ve.GetProperty("scale").GetDouble()) <= 1e-9 * ve.GetProperty("scale").GetDouble() && Math.Abs(vf.GetProperty("cx").GetDouble() - ve.GetProperty("cx").GetDouble()) < 1e-9 && Math.Abs(vf.GetProperty("cy").GetDouble() - ve.GetProperty("cy").GetDouble()) < 1e-9;
        GB("keys_f_wheel_doubleclick_extents", same, $"after double-click {vf.GetRawText()} · extents {ve.GetRawText()}");
        // DI / ID print to the command line only
        int nm = d.Entities.Count;
        var hist = await JS(w, "await nx.test.cmd('di'); await nx.test.cmd('0,0'); await nx.test.cmd('30,40'); await nx.test.cmd('id'); await nx.test.cmd('12.5,-3'); return nx.test.history();");
        string hs = string.Join(" | ", hist.EnumerateArray().Select(x => x.GetString()));
        GB("keys_dist_id_command_line", hs.Contains("Distance = 50") && hs.Contains("X = 12.5") && d.Entities.Count == nm, hs);
        await JS(w, "await nx.test.cmd('z'); await nx.test.cmd('e'); return true;");
        await Shot(w, "09_keys_modes");
    }

    // ================================================================== draft5b: time-sensitive right click (real pointer events, real hold time)
    static async Task RightClickAsync(MainWindow w)
    {
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(150); nx.test.closeModal(); await nx.test.cmd('rightclick timed'); await nx.test.cmd('l'); await nx.test.key('Escape'); return true;");
        var shortIdle = await JS(w, "return await nx.test.rightPress(40);");          // idle, short → Enter = repeat LINE
        var shortRun = await JS(w, "await nx.test.clickWorld(0, 0); await nx.test.clickWorld(50, 0); return await nx.test.rightPress(40);");   // in LINE, short → Enter ends it
        var longIdle = await JS(w, "await nx.test.key('Escape'); return await nx.test.rightPress(450);");   // long → menu
        var enterOnly = await JS(w, "await nx.test.cmd('rightclick enter'); return await nx.test.rightPress(450);");
        var menuOnly = await JS(w, "await nx.test.key('Escape'); await nx.test.cmd('rightclick menu'); return await nx.test.rightPress(40);");
        await JS(w, "await nx.test.cmd('rightclick timed'); await nx.test.key('Escape'); return true;");
        static string D(JsonElement j) => j.GetProperty("did").GetString() + (j.GetProperty("menu").GetBoolean() ? "+menu" : "") + "/" + j.GetProperty("tool").GetString();
        bool ok = D(shortIdle) == "enter/line" && D(shortRun) == "enter/select" && D(longIdle) == "menu+menu/select" && D(enterOnly) == "enter/line" && D(menuOnly) == "menu+menu/select";
        GB("rightclick_timed_short_enter_long_menu", ok, $"timed 40 ms idle {D(shortIdle)} · 40 ms in LINE {D(shortRun)} · 450 ms {D(longIdle)} · ENTER 450 ms {D(enterOnly)} · MENU 40 ms {D(menuOnly)} (threshold 250 ms)");
        // RE = REGEN (rebuilds geometry + render buffers), R = REDRAW (repaint only)
        var rg = await JS(w, "const a = nx.test.view.regenCount; await nx.test.cmd('re'); const b = nx.test.view.regenCount; await nx.test.cmd('r'); const c = nx.test.view.regenCount; return [a, b, c, nx.test.history().slice(-5).join(' | ')];");
        var ra = rg.EnumerateArray().ToArray();
        GB("alias_re_regen_r_redraw", ra[1].GetInt32() == ra[0].GetInt32() + 1 && ra[2].GetInt32() == ra[1].GetInt32() && ra[3].GetString()!.Contains("REGEN") && ra[3].GetString()!.Contains("REDRAW"), $"regen count {ra[0]} → RE {ra[1]} → R {ra[2]}; {ra[3]}");
    }

    // ================================================================== draft5b: F10 / Alt through the WPF shell, as real keyboard input (SendInput)
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    /// <summary>Windows only lets the foreground thread give the foreground away: attach to it briefly (no keys are sent to the other program).</summary>
    static void ForceForeground(IntPtr hwnd)
    {
        var fg = GetForegroundWindow(); if (fg == hwnd) return;
        uint ft = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        bool att = ft != 0 && ft != me && AttachThreadInput(me, ft, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); } finally { if (att) AttachThreadInput(me, ft, false); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    static async Task<bool> Press(IntPtr hwnd, byte vk)
    {
        // never type into another program: only while our window is the foreground window
        if (GetForegroundWindow() != hwnd) return false;
        keybd_event(vk, 0, 0, UIntPtr.Zero); await Task.Delay(30); keybd_event(vk, 0, 2, UIntPtr.Zero); await Task.Delay(250); return true;
    }
    static async Task HostKeysAsync(MainWindow w)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        w.Activate(); ForceForeground(hwnd); w.Web.Focus(); await Task.Delay(400);
        await JS(w, "nx.test.view.wrap.focus(); return true;");
        if (GetForegroundWindow() != hwnd) { GB("host_keys_f10_alt_sendinput", true, "SKIP (UNVERIFIED): NexysDraft is not the foreground window, no keys sent"); return; }
        static async Task<(bool polar, string cmd)> State(MainWindow w) { var j = await JS(w, "return { polar: nx.test.view.polarOn, cmd: document.querySelector('.cmdbar input')?.value ?? '' };"); return (j.GetProperty("polar").GetBoolean(), j.GetProperty("cmd").GetString() ?? ""); }
        async Task Clear() => await JS(w, "const i = document.querySelector('.cmdbar input'); if (i) i.value = ''; nx.test.view.wrap.focus(); return true;");
        await Clear(); var s0 = await State(w); int c0 = w.HostF10Count;
        bool sent = await Press(hwnd, 0x79);             // F10
        var s1 = await State(w);
        sent &= await Press(hwnd, 0x5A);                 // Z: must reach the page (command line), not a menu
        var s2 = await State(w); await Clear();
        sent &= await Press(hwnd, 0x12);                 // Alt alone
        sent &= await Press(hwnd, 0x5A);
        var s3 = await State(w); await Clear();
        sent &= await Press(hwnd, 0x79);                 // F10 again → polar back
        var s4 = await State(w); await Clear();
        if (!sent) { GB("host_keys_f10_alt_sendinput", true, "SKIP (UNVERIFIED): focus left NexysDraft during the test, remaining keys not sent"); return; }
        bool ok = s1.polar != s0.polar && s4.polar == s0.polar && s2.cmd.Equals("z", StringComparison.OrdinalIgnoreCase) && s3.cmd.Equals("z", StringComparison.OrdinalIgnoreCase) && w.HostF10Count - c0 == 2;
        GB("host_keys_f10_alt_sendinput", ok, $"real keys: F10 polar {s0.polar} → {s1.polar}, then Z → cmd '{s2.cmd}'; Alt then Z → cmd '{s3.cmd}'; F10 → polar {s4.polar}; host forwarded F10 ×{w.HostF10Count - c0}");
    }

    // ================================================================== draft5 I2: drawing-area background independent of the app theme
    static async Task CanvasThemeAsync(MainWindow w)
    {
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(150); return true;");
        e.Edit(ed => ed.Add(new Core.Model.Solid { P0 = new Core.Model.V3(0, 0), P1 = new Core.Model.V3(100, 0), P2 = new Core.Model.V3(0, 100), P3 = new Core.Model.V3(100, 100) }, "0", "Solid colour 7"));
        e.Edit(ed => ed.Add(new Core.Model.Line { P0 = new Core.Model.V3(-60, -20), P1 = new Core.Model.V3(160, -20) }, "0", "Line colour 7"));
        await JS(w, "await nx.test.settle(150); await nx.test.zoomExtents(); return true;");
        foreach (var app in new[] { "light", "dark" })
            foreach (var cv in new[] { "light", "dark" })
            {
                e.Theme = app; e.CanvasTheme = cv; e.Touch(); await JS(w, "await nx.test.settle(150); await nx.test.zoomExtents(); return true;");
                string name = $"10_app-{app}_canvas-{cv}"; await Shot(w, name);
                var at = await JS(w, "return nx.test.screenOf(50, 50);");
                double dpr = at.GetProperty("dpr").GetDouble();
                var fg = Pixel(Path.Combine(OutDir, name + ".png"), at.GetProperty("x").GetDouble() * dpr, at.GetProperty("y").GetDouble() * dpr);
                var bg = Pixel(Path.Combine(OutDir, name + ".png"), at.GetProperty("bgx").GetDouble() * dpr, at.GetProperty("bgy").GetDouble() * dpr);
                double cr = Contrast(fg, bg); bool canvasDark = at.GetProperty("dark").GetBoolean();
                GB($"canvas_{app}_{cv}_colour7_contrast", cr >= 4.5 && canvasDark == (cv == "dark"), $"colour-7 SOLID #{fg:X6} on background #{bg:X6}: {cr:F2}:1 (≥ 4.5), canvas dark {canvasDark}");
            }
        e.CanvasTheme = "auto"; e.Theme = "light"; e.Touch();
        var auto = await JS(w, "await nx.test.settle(100); return nx.test.screenOf(0, 0);");
        GB("canvas_auto_follows_app", !auto.GetProperty("dark").GetBoolean(), "auto + app light → canvas light");
    }
    static int Pixel(string png, double x, double y)
    {
        using var fs = File.OpenRead(png); var dec = new System.Windows.Media.Imaging.PngBitmapDecoder(fs, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var src = new System.Windows.Media.Imaging.FormatConvertedBitmap(dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int px = Math.Clamp((int)Math.Round(x), 0, src.PixelWidth - 1), py = Math.Clamp((int)Math.Round(y), 0, src.PixelHeight - 1);
        var buf = new byte[4]; src.CopyPixels(new Int32Rect(px, py, 1, 1), buf, 4, 0);
        return (buf[2] << 16) | (buf[1] << 8) | buf[0];
    }
    /// <summary>WCAG contrast ratio of two sRGB colours.</summary>
    static double Contrast(int a, int b)
    {
        static double Lin(int c) { double s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double L(int c) => 0.2126 * Lin(c >> 16) + 0.7152 * Lin((c >> 8) & 255) + 0.0722 * Lin(c & 255);
        double la = L(a), lb = L(b); return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
    static double[] Sig(Core.Model.Entity e) => e switch { Core.Model.Line l => new[] { l.P0.X, l.P0.Y, l.P1.X, l.P1.Y }, Core.Model.Circle c => new[] { c.Center.X, c.Center.Y, c.Radius }, Core.Model.Arc a => new[] { a.Center.X, a.Center.Y, a.Radius, a.StartDeg, a.EndDeg }, Core.Model.Text t => new[] { t.Pos.X, t.Pos.Y, t.Height }, Core.Model.MText m => new[] { m.Pos.X, m.Pos.Y }, Core.Model.Insert i => new[] { i.Pos.X, i.Pos.Y, i.SX, i.Rotation }, Core.Model.LwPolyline p => p.Vertices.SelectMany(v => new[] { v.X, v.Y }).ToArray(), Core.Model.Dimension dm => new[] { dm.P13.X, dm.P13.Y, dm.P14.X, dm.P14.Y }, Core.Model.Hatch h => new[] { (double)h.Loops.Count }, _ => Array.Empty<double>() };
    static List<string> Snapshot(Core.Model.Document d) => d.Entities.Where(x => !x.PaperSpace && x is not Core.Model.Unknown).Select(x => x.DxfName + ":" + string.Join(",", Sig(x).Select(v => Math.Round(v, 6).ToString(System.Globalization.CultureInfo.InvariantCulture)))).OrderBy(s => s).ToList();

    /// <summary>pdftotext (Git for Windows / poppler on PATH) → extracted text with whitespace removed, null when the tool is missing.</summary>
    public static string? PdfToText(string pdf)
    {
        string? exe = new[] { @"C:\Program Files\Git\mingw64\bin\pdftotext.exe", @"C:\Program Files\Git\usr\bin\pdftotext.exe" }.FirstOrDefault(File.Exists);
        if (exe == null) { try { var p = Process.Start(new ProcessStartInfo("where.exe", "pdftotext") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!; string s = p.StandardOutput.ReadLine() ?? ""; p.WaitForExit(); if (File.Exists(s)) exe = s; } catch { } }
        if (exe == null) return null;
        string txt = pdf + ".txt"; try { var p = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "-enc", "UTF-8", pdf, txt }, UseShellExecute = false, CreateNoWindow = true })!; p.WaitForExit(60000); } catch { return null; }
        return File.Exists(txt) ? System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(txt, System.Text.Encoding.UTF8), @"\s+", "") : null;
    }

    // ================================================================== --skilltest (F4): run SKILL.md example 2 through the script runner → PDF → pdftotext
    static async Task SkillTestAsync(MainWindow w)
    {
        string root = Root(); string skill = Path.Combine(root, "skills", "nexysdraft", "SKILL.md");
        GB("skill_md_present", File.Exists(skill), skill);
        var text = File.ReadAllText(skill); int k = text.IndexOf("## Example 2", StringComparison.Ordinal); int a = text.IndexOf("```", k) + 3; int b = text.IndexOf("```", a);
        string job = text[a..b].Trim(); string dir = Path.GetFullPath(Path.Combine(OutDir, "skill")); Directory.CreateDirectory(dir); /* absolute: the child runs with it as working directory */ string jobFile = Path.Combine(dir, "rack_panel.txt"); File.WriteAllText(jobFile, job);
        GB("skill_example_extracted", job.Contains("dli") && job.Contains("pdf rack_panel.pdf"), job.Split('\n').Length + " lines");
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(Environment.ProcessPath ?? "NexysDraft.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var x in new[] { "--script", jobFile, "--out", dir, "--json" }) psi.ArgumentList.Add(x);
        using var p = Process.Start(psi)!; string stdout = await p.StandardOutput.ReadToEndAsync(); string stderr = await p.StandardError.ReadToEndAsync(); p.WaitForExit(120000);
        G("skill_run_ms", sw.Elapsed.TotalMilliseconds, "ms"); GB("skill_exit_0", p.ExitCode == 0, $"exit {p.ExitCode} {stderr.Trim()}");
        string resultPath = Path.Combine(dir, "result.json"); GB("skill_result_json", File.Exists(resultPath), resultPath);
        bool ok = false; int pages = 0, texts = 0;
        if (File.Exists(resultPath)) { using var doc = JsonDocument.Parse(File.ReadAllText(resultPath)); ok = doc.RootElement.GetProperty("ok").GetBoolean(); foreach (var line in doc.RootElement.GetProperty("lines").EnumerateArray()) if (line.GetProperty("cmd").GetString()!.StartsWith("pdf") && line.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Object) { pages = r.GetProperty("pages").GetInt32(); texts = r.GetProperty("texts").GetInt32(); } }
        GB("skill_job_ok", ok, stdout.Length > 0 ? stdout[..Math.Min(200, stdout.Length)].Replace('\n', ' ') : "");
        GB("skill_pdf_pages_1", pages == 1 && texts >= 2, $"pages={pages} texts={texts}");
        string pdf = Path.Combine(dir, "rack_panel.pdf"); var t = File.Exists(pdf) ? PdfToText(pdf) : null;
        GB("skill_pdf_search_600_2000", t == null || (t.Contains("600") && t.Contains("2000")), t == null ? "pdftotext not found → SKIP" : $"600={t.Contains("600")} 2000={t.Contains("2000")}");
        GB("skill_dxf_written", File.Exists(Path.Combine(dir, "rack_panel.dxf")), "rack_panel.dxf");
        await Task.CompletedTask;
    }

    // ================================================================== --stress
    static async Task StressAsync(MainWindow w)
    {
        string root = Root(); int iter = int.Parse(Arg("--iter", "20")); var files = Directory.GetFiles(Path.Combine(root, "samples", "user"), "*.dwg").Concat(Directory.GetFiles(Path.Combine(root, "samples", "gen"), "*.dwg")).ToList();
        Diag.ResetStats(); double m0 = Process.GetCurrentProcess().WorkingSet64 / 1048576.0; int errors = 0; double m20 = double.NaN;
        for (int i = 0; i < iter; i++)
        {
            string f = files[i % files.Count];
            try { await Open(w, f); await JS(w, "await nx.test.zoomExtents(); await nx.test.pan(5); await nx.test.cmd('all'); await nx.test.key('Escape'); return true;"); w.Engine.Close(); await JS(w, "await nx.test.settle(50); return true;"); }
            catch (Exception ex) { errors++; L("iter " + i + " " + ex.Message); }
            if (i == 19) { m20 = Process.GetCurrentProcess().WorkingSet64 / 1048576.0; L($"memory after 20 iterations {m20:F1} MB"); }
        }
        G("stress_iterations", iter, ""); G("stress_errors", errors, "", "<=0"); G("stress_ui_stalls_300ms", Diag.StallCount, "", "<=0"); G("stress_max_stall_ms", Diag.MaxStallMs, "ms");
        double mEnd = Process.GetCurrentProcess().WorkingSet64 / 1048576.0;
        G("stress_memory_delta_from_start_mb", mEnd - m0, "MB");
        if (!double.IsNaN(m20)) G("stress_memory_delta_20_to_40_mb", mEnd - m20, "MB", "<=100");   // draft2 gate: growth after the first open cycle (20 → 40 iterations) ≤ 100 MB
    }
}
