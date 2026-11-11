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
            _lang0 = await SetLang(w, "en");   // the checks read English prompts and labels; the user's language is put back at the end
            if (Args[0] == "--shot") await ShotAsync(w); else if (Args[0] == "--perf") await PerfAsync(w); else if (Args[0] == "--stress") await StressAsync(w); else if (Args[0] == "--skilltest") await SkillTestAsync(w); else await FullAsync(w);
        }
        catch (Exception ex) { L("FATAL " + ex); GB("selftest_completed", false, "FATAL " + ex.GetType().Name + ": " + ex.Message); }   // a crashed run must not exit 0 (draft5d blockers #27)
        finally
        {
            try { if (_lang0 != "?") await SetLang(w, _lang0); } catch { }
            File.WriteAllLines(Path.Combine(OutDir, "selftest.log"), Log); if (Gate.Count > 0) File.WriteAllLines(Path.Combine(OutDir, "gates.txt"), Gate);
            await Task.Delay(200); Application.Current.Shutdown(Gate.Any(g => g.EndsWith("FAIL") || g.Contains("\tFAIL\t")) ? 1 : 0);
        }
    }
    static string? _lang0 = "?";
    /// <summary>Set the UI language (null = no stored choice → the Korean default) and reload the page; returns the stored value before.</summary>
    static async Task<string?> SetLang(MainWindow w, string? lang)
    {
        var before = await JS(w, "let v = null; try { v = localStorage.getItem('nx.lang'); } catch (e) { } return { v, cur: nx.test.lang() };");
        string? stored = before.GetProperty("v").ValueKind == JsonValueKind.String ? before.GetProperty("v").GetString() : null;
        if (stored == lang && before.GetProperty("cur").GetString() == (lang ?? "ko")) return stored;
        string set = lang == null ? "localStorage.removeItem('nx.lang')" : $"localStorage.setItem('nx.lang', '{lang}')";
        await w.Bridge!.Eval("(() => { try { " + set + "; } catch (e) { } window.__nxOld = 1; location.reload(); })()");
        for (int i = 0; i < 150; i++) { await Task.Delay(100); string r = await w.Bridge!.Eval("(window.nx && window.nx.test && !window.__nxOld && window.nxState) ? document.documentElement.lang : ''"); if (r.Trim('"') != "") break; }
        await Idle(w, 300); await JS(w, "await nx.test.settle(200); return true;");
        return stored;
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
        await ShapesAsync(w);
        await DraftAidsAsync(w);
        await DraftingAsync(w);
        await LangPanelsAsync(w);
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
        var def = await JS(w, "return nx.test.modes();");
        GB("aid_defaults_polar_otrack_on", def.GetProperty("polar").GetBoolean() && def.GetProperty("otrack").GetBoolean() && def.GetProperty("osnap").GetBoolean() && !def.GetProperty("ortho").GetBoolean(), $"at start: polar {def.GetProperty("polar").GetBoolean()}, otrack {def.GetProperty("otrack").GetBoolean()}, osnap {def.GetProperty("osnap").GetBoolean()}, ortho {def.GetProperty("ortho").GetBoolean()}");
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
    // ================================================================== draft5e: basic shapes through the real command line (typed points) and the toolbar
    static async Task ShapesAsync(MainWindow w)
    {
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(150); nx.test.closeModal(); return true;");
        var d = e.Doc!;
        static double Hy(double x, double y) => Math.Sqrt(x * x + y * y);
        // RECTANG: first corner typed, other corner relative
        await JS(w, "await nx.test.cmd('rec'); await nx.test.cmd('0,0'); await nx.test.cmd('@100,50'); return true;"); await Task.Delay(150);
        var rect = d.Entities.OfType<Core.Model.LwPolyline>().LastOrDefault();
        bool okRect = rect != null && rect.Closed && rect.Vertices.Count == 4 && rect.Vertices.Any(v => Math.Abs(v.X - 100) < 1e-9 && Math.Abs(v.Y - 50) < 1e-9) && rect.Vertices.Any(v => Math.Abs(v.X) < 1e-9 && Math.Abs(v.Y) < 1e-9);
        GB("shape_rect", okRect, rect == null ? "no polyline" : "vertices " + string.Join(" ", rect.Vertices.Select(v => $"({v.X:G6},{v.Y:G6})")) + $" closed {rect.Closed}");
        // ARC: start, point on arc, end
        await JS(w, "await nx.test.cmd('a'); await nx.test.cmd('150,0'); await nx.test.cmd('175,25'); await nx.test.cmd('200,0'); return true;"); await Task.Delay(150);
        var arc = d.Entities.OfType<Core.Model.Arc>().LastOrDefault();
        GB("shape_arc_3point", arc != null && Math.Abs(arc.Center.X - 175) < 1e-9 && Math.Abs(arc.Center.Y) < 1e-9 && Math.Abs(arc.Radius - 25) < 1e-9 && Math.Abs(arc.StartDeg) < 1e-9 && Math.Abs(arc.EndDeg - 180) < 1e-9, arc == null ? "no arc" : $"centre ({arc.Center.X:G6},{arc.Center.Y:G6}) r {arc.Radius:G6} {arc.StartDeg:G6}° → {arc.EndDeg:G6}°");
        // ELLIPSE: axis ends, other half axis as a value
        await JS(w, "await nx.test.cmd('el'); await nx.test.cmd('0,100'); await nx.test.cmd('80,100'); await nx.test.cmd('20'); return true;"); await Task.Delay(150);
        var el = d.Entities.OfType<Core.Model.Ellipse>().LastOrDefault();
        GB("shape_ellipse", el != null && Math.Abs(el.Center.X - 40) < 1e-9 && Math.Abs(el.Center.Y - 100) < 1e-9 && Math.Abs(el.Ratio - 0.5) < 1e-9 && Math.Abs(Hy(el.Major.X, el.Major.Y) - 40) < 1e-9, el == null ? "no ellipse" : $"centre ({el.Center.X:G6},{el.Center.Y:G6}) major ({el.Major.X:G6},{el.Major.Y:G6}) ratio {el.Ratio:G6}");
        // POLYGON: 6 sides, centre, radius typed (inscribed, bottom edge horizontal)
        await JS(w, "await nx.test.cmd('pol'); await nx.test.cmd('6'); await nx.test.cmd('150,100'); await nx.test.cmd('25'); return true;"); await Task.Delay(150);
        var pol = d.Entities.OfType<Core.Model.LwPolyline>().LastOrDefault();
        bool okPol = pol != null && pol != rect && pol.Closed && pol.Vertices.Count == 6 && pol.Vertices.All(v => Math.Abs(Hy(v.X - 150, v.Y - 100) - 25) < 1e-6);
        double minY = pol?.Vertices.Min(v => v.Y) ?? 0; int bottom = pol?.Vertices.Count(v => Math.Abs(v.Y - minY) < 1e-6) ?? 0;
        GB("shape_polygon", okPol && bottom == 2, pol == null ? "no polygon" : $"{pol.Vertices.Count} vertices on r 25 {okPol}, bottom edge horizontal {bottom == 2}");
        // POINT
        await JS(w, "await nx.test.cmd('po'); await nx.test.cmd('250,0'); return true;"); await Task.Delay(150);
        var pt = d.Entities.OfType<Core.Model.Point>().LastOrDefault();
        GB("shape_point", pt != null && Math.Abs(pt.Pos.X - 250) < 1e-9 && Math.Abs(pt.Pos.Y) < 1e-9, pt == null ? "no point" : $"({pt.Pos.X:G6},{pt.Pos.Y:G6})");
        // TEXT (DT): start point, height 5, default rotation (Enter), two lines with spaces and Korean, a Space keypress inside the text must not submit
        var sp = await JS(w, "await nx.test.cmd('dt'); await nx.test.cmd('0,-30'); await nx.test.cmd('5'); await nx.test.cmd(''); const i = document.querySelector('.cmdbar input'); i.value = '한글'; i.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })); const kept = i.value; const tool = nx.test.view.tool; i.value = ''; return { kept, tool, focus: document.activeElement === i };");
        await JS(w, "await nx.test.cmd('한글 텍스트 TEXT 1'); await nx.test.cmd('둘째 줄'); await nx.test.cmd(''); return true;"); await Task.Delay(200);
        var tx = d.Entities.OfType<Core.Model.Text>().Where(t => !t.IsAttrib).ToList();
        bool okTx = tx.Count == 2 && tx[0].Value == "한글 텍스트 TEXT 1" && tx[1].Value == "둘째 줄" && Math.Abs(tx[0].Height - 5) < 1e-9 && Math.Abs(tx[0].Pos.Y + 30) < 1e-9 && tx[1].Pos.Y < tx[0].Pos.Y - 5 && Math.Abs(tx[1].Pos.X) < 1e-9;
        bool spaceKept = sp.GetProperty("kept").GetString() == "한글" && sp.GetProperty("tool").GetString() == "dtext" && sp.GetProperty("focus").GetBoolean();
        GB("shape_text_dt", okTx && spaceKept, $"{tx.Count} TEXT: " + string.Join(" | ", tx.Select(t => $"'{t.Value}' h {t.Height:G4} at ({t.Pos.X:G6},{t.Pos.Y:G6})")) + $"; Space during text entry kept the input {spaceKept} ({sp.GetRawText()})");
        // MTEXT (T): two corners (width 80), two lines
        await JS(w, "await nx.test.cmd('t'); await nx.test.cmd('100,-30'); await nx.test.cmd('180,-60'); await nx.test.cmd('첫 줄'); await nx.test.cmd('둘째 줄 MTEXT'); await nx.test.cmd(''); return true;"); await Task.Delay(200);
        var mt = d.Entities.OfType<Core.Model.MText>().LastOrDefault();
        string nl = ((char)92).ToString() + "P";
        GB("shape_mtext", mt != null && Math.Abs(mt.Width - 80) < 1e-9 && mt.Value == "첫 줄" + nl + "둘째 줄 MTEXT" && Math.Abs(mt.Pos.X - 100) < 1e-9 && Math.Abs(mt.Pos.Y + 30) < 1e-9, mt == null ? "no mtext" : $"'{mt.Value}' width {mt.Width:G6} at ({mt.Pos.X:G6},{mt.Pos.Y:G6})");
        // toolbar: one button per shape, and a click starts the command
        var tb = await JS(w, "const ids = ['rect', 'arc', 'ellipse', 'polygon', 'point', 'dtext']; const found = ids.filter(n => document.querySelector(`.toolbar [data-tool=${n}]`)); document.querySelector('.toolbar [data-tool=rect]').dispatchEvent(new MouseEvent('click', { bubbles: true })); await nx.test.settle(30); const tool = nx.test.view.tool; await nx.test.key('Escape'); const tbEl = document.querySelector('.toolbar'); return { found: found.length, tool, overflow: tbEl.scrollWidth > tbEl.clientWidth + 1 };");
        GB("shape_toolbar_buttons", tb.GetProperty("found").GetInt32() == 6 && tb.GetProperty("tool").GetString() == "rect" && !tb.GetProperty("overflow").GetBoolean(), tb.GetRawText());
        // undo removes the last shape (MTEXT)
        int before = d.Entities.Count; e.Undo(); await Task.Delay(100);
        GB("shape_undo", d.Entities.Count == before - 1 && !d.Entities.OfType<Core.Model.MText>().Any(), $"entities {before} → {d.Entities.Count}"); e.Redo();
        await JS(w, "await nx.test.zoomExtents(); return true;"); await Shot(w, "11_basic_shapes");
    }

    // ================================================================== draft5g: TRIM / EXTEND, click-click window and crossing boxes, polar and object snap tracking — real pointer events
    static async Task DraftAidsAsync(MainWindow w)
    {
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(150); nx.test.closeModal(); return true;");
        var d = e.Doc!; const double T9 = 1e-9;
        List<Core.Model.Line> Row(double y) => d.Entities.OfType<Core.Model.Line>().Where(l => l.P0.X < 150 && Math.Abs(l.P0.Y - y) < T9 && Math.Abs(l.P1.Y - y) < T9).OrderBy(l => Math.Min(l.P0.X, l.P1.X)).ToList();
        string Spans(List<Core.Model.Line> r) => string.Join(" ", r.Select(l => $"{Math.Min(l.P0.X, l.P1.X):G9}..{Math.Max(l.P0.X, l.P1.X):G9}"));
        // geometry: three horizontals cut by two verticals, a short line to extend, a tracking reference line
        await JS(w, "for (const c of ['l 0,0 100,0', 'l 0,100 100,100', 'l 0,110 100,110', 'l 30,-20 30,130', 'l 70,-20 70,130', 'l 0,50 20,50', 'l 200,100 260,100']) { const [n, ...pts] = c.split(' '); await nx.test.cmd(n); for (const q of pts) await nx.test.cmd(q); await nx.test.key('Escape'); } await nx.test.zoomExtents(); return true;"); await Task.Delay(200);
        int n0 = d.Entities.Count;

        // (1) selection box by click – move – click: left → right = window (fully inside), right → left = crossing (touching)
        var b1 = await JS(w, "await nx.test.key('Escape'); await nx.test.clickWorld(120, -40); const open = nx.test.box().boxClick; await nx.test.moveWorld(180, 40); await nx.test.clickWorld(-20, 140); const cross = nx.test.box(); await nx.test.key('Escape'); await nx.test.clickWorld(-20, -40); await nx.test.moveWorld(50, 50); await nx.test.clickWorld(120, 140); const win = nx.test.box(); await nx.test.key('Escape'); await nx.test.clickWorld(45, 104); await nx.test.clickWorld(55, 106); const winSmall = nx.test.box().selection.length; await nx.test.key('Escape'); await nx.test.clickWorld(55, 106); await nx.test.clickWorld(45, 104); const crossSmall = nx.test.box().selection.length; await nx.test.key('Escape'); await nx.test.clickWorld(45, 104); await nx.test.key('Escape'); const esc = nx.test.box().boxClick; return { open, cross: cross.selection.length, crossOpen: cross.boxClick, win: win.selection.length, winSmall, crossSmall, esc };");
        // window (-20,-40)…(120,140) holds the six lines left of x = 120; crossing (120,-40)…(-20,140) the same six; the small box between the two horizontals at y 100 / 110 touches nothing inside (window 0) … and nothing crossing either, so use y 104…106 → 0 and 0? the verticals are at x 30 / 70 — the box x 45…55 crosses none
        bool okBox = b1.GetProperty("open").GetBoolean() && !b1.GetProperty("crossOpen").GetBoolean() && b1.GetProperty("cross").GetInt32() == 6 && b1.GetProperty("win").GetInt32() == 6 && !b1.GetProperty("esc").GetBoolean();
        GB("aid_box_click_click", okBox, b1.GetRawText());
        // window vs crossing on the same box: x 25…75, y 95…115 — only parts of the lines are inside → window 0, crossing 4 (two horizontals, two verticals)
        var b2 = await JS(w, "await nx.test.key('Escape'); await nx.test.clickWorld(25, 95); await nx.test.clickWorld(75, 115); const win = nx.test.box().selection.length; await nx.test.key('Escape'); await nx.test.clickWorld(75, 115); await nx.test.clickWorld(25, 95); const cross = nx.test.box().selection.length; await nx.test.key('Escape'); const dragWin = (await nx.test.selectWindow(25, 95, 75, 115)).length; const dragCross = (await nx.test.selectWindow(75, 115, 25, 95)).length; await nx.test.key('Escape'); return { win, cross, dragWin, dragCross };");
        GB("aid_window_inside_crossing_touching", b2.GetProperty("win").GetInt32() == 0 && b2.GetProperty("cross").GetInt32() == 4 && b2.GetProperty("dragWin").GetInt32() == 0 && b2.GetProperty("dragCross").GetInt32() == 4, b2.GetRawText() + " (expected window 0, crossing 4, by click-click and by drag)");

        // (2) TRIM: hover preview, click the middle part, one undo step
        var pv = await JS(w, "await nx.test.cmd('tr'); await nx.test.moveWorld(50, 0, { wait: 250 }); const m = await nx.test.moveWorld(50, 0, { wait: 150 }); return m.preview;");
        bool okPv = pv.ValueKind == JsonValueKind.Object && Math.Abs(pv.GetProperty("pts")[0].GetDouble() - 30) < T9 && Math.Abs(pv.GetProperty("pts")[pv.GetProperty("pts").GetArrayLength() - 2].GetDouble() - 70) < T9;
        await JS(w, "await nx.test.clickWorld(50, 0); return true;"); await Task.Delay(200);
        var r0 = Row(0);
        GB("aid_trim_click", okPv && r0.Count == 2 && Spans(r0) == "0..30 70..100" && d.Entities.Count == n0 + 1, $"y=0 row: {Spans(r0)} (expected 0..30 70..100), preview 30→70 {okPv} {pv.GetRawText()[..Math.Min(160, pv.GetRawText().Length)]}, entities {n0} → {d.Entities.Count}");
        // fence drag across the two upper horizontals: both lose their middle part in one undo step
        await JS(w, "await nx.test.dragWorld(50, 96, 50, 114); return true;"); await Task.Delay(250);
        var r100 = Row(100); var r110 = Row(110); int nAfter = d.Entities.Count;
        e.Undo(); await Task.Delay(150); var u100 = Row(100); var u110 = Row(110); e.Redo(); await Task.Delay(150);
        GB("aid_trim_fence_one_undo", Spans(r100) == "0..30 70..100" && Spans(r110) == "0..30 70..100" && Spans(u100) == "0..100" && Spans(u110) == "0..100", $"y=100: {Spans(r100)}; y=110: {Spans(r110)}; after one undo: {Spans(u100)} / {Spans(u110)}; entities {nAfter}");
        // Shift+click in TRIM extends: the short line 0,50 → 20,50 reaches the vertical at x = 30; EXTEND again reaches x = 70; Shift+click in EXTEND trims it back
        await JS(w, "await nx.test.clickWorld(18, 50, { shift: true }); return true;"); await Task.Delay(200); string s1 = Spans(Row(50));
        await JS(w, "await nx.test.key('Escape'); await nx.test.cmd('ex'); await nx.test.clickWorld(28, 50); return true;"); await Task.Delay(200); string s2 = Spans(Row(50));
        await JS(w, "await nx.test.clickWorld(50, 50, { shift: true }); await nx.test.key('Escape'); return nx.test.view.tool;"); await Task.Delay(200); string s3 = Spans(Row(50));
        GB("aid_extend_and_shift", s1 == "0..30" && s2 == "0..70" && s3 == "0..30", $"Shift+click in TRIM → {s1} (0..30); EXTEND → {s2} (0..70); Shift+click in EXTEND → {s3} (0..30)");

        // (3) polar tracking (on by default, 90°): the cursor 1.2 units off the horizontal picks a point exactly on it; far from any polar angle it is free
        var m0 = await JS(w, "await nx.test.cmd('polar on'); return nx.test.modes();");   // F8 in the keys section switched polar off (ortho and polar exclude each other); the default is checked there
        var pt = await JS(w, "await nx.test.cmd('l'); await nx.test.cmd('120,-10'); const a = await nx.test.moveWorld(180, -8.8); const free = await nx.test.moveWorld(160, 20); await nx.test.moveWorld(180, -8.8); await nx.test.clickWorld(180, -8.8); await nx.test.key('Escape'); return { on: a.pt, kind: a.trk && a.trk.guides[0].kind, label: a.trk && a.trk.label, free: free.pt, freeTrk: !!free.trk };"); await Task.Delay(200);
        var lp = d.Entities.OfType<Core.Model.Line>().LastOrDefault();
        bool okPolar = m0.GetProperty("polar").GetBoolean() && m0.GetProperty("otrack").GetBoolean() && pt.GetProperty("kind").GetString() == "polar" && !pt.GetProperty("freeTrk").GetBoolean() && lp != null && Math.Abs(lp.P0.X - 120) < T9 && Math.Abs(lp.P0.Y + 10) < T9 && Math.Abs(lp.P1.Y + 10) < T9 && Math.Abs(lp.P1.X - 180) < 1e-6;
        GB("aid_polar_tracking_default_on", okPolar, $"POLAR {m0.GetProperty("polar").GetBoolean()}, OTRACK {m0.GetProperty("otrack").GetBoolean()}; hover → {pt.GetRawText()}; line → ({lp?.P1.X:G9},{lp?.P1.Y:G9}) expected (180,-10)");

        // (4) object snap tracking: rest on the end point (200,100) for 500 ms → acquired; move up beside x = 200 → the point is on the vertical path;
        //     second point: rest on (260,100), then near (260,160): polar ray from the first point × vertical path through (260,100) = (260,160) exactly
        var t1 = await JS(w, "await nx.test.cmd('l'); const h = await nx.test.moveWorld(200, 100, { wait: 520 }); const acquired = h.trackPts.length; const m = await nx.test.moveWorld(201.1, 160); const first = m.pt; const g1 = m.trk && m.trk.guides.map(g => g.kind + g.deg).join('+'); await nx.test.clickWorld(201.1, 160); const cleared = nx.test.view.trackPts.length; await nx.test.moveWorld(260, 100, { wait: 520 }); const m2 = await nx.test.moveWorld(259.2, 161.3); const g2 = m2.trk && m2.trk.guides.map(g => g.kind + g.deg).join('+'); const label = m2.trk && m2.trk.label; await nx.test.clickWorld(259.2, 161.3); await nx.test.key('Escape'); return { acquired, first, g1, cleared, second: m2.pt, g2, label };"); await Task.Delay(250);
        var lt = d.Entities.OfType<Core.Model.Line>().LastOrDefault();
        bool okTrack = t1.GetProperty("acquired").GetInt32() == 1 && t1.GetProperty("g1").GetString() == "track90" && t1.GetProperty("cleared").GetInt32() == 0 && t1.GetProperty("g2").GetString() == "polar0+track90" && lt != null && Math.Abs(lt.P0.X - 200) < T9 && Math.Abs(lt.P1.X - 260) < T9 && Math.Abs(lt.P1.Y - lt.P0.Y) < T9 && Math.Abs(lt.P0.Y - 160) < 1e-6;
        GB("aid_object_snap_tracking", okTrack, $"{t1.GetRawText()}; line ({lt?.P0.X:G9},{lt?.P0.Y:G9}) → ({lt?.P1.X:G9},{lt?.P1.Y:G9}) expected (200,160) → (260,160)");
        // a capture with the tracking paths showing, then F11 / F10 off: no path
        await JS(w, "await nx.test.cmd('l'); await nx.test.cmd('120,40'); await nx.test.moveWorld(260, 100, { wait: 520 }); await nx.test.moveWorld(259.2, 41.2); return true;"); await Shot(w, "12_tracking_paths");
        var off = await JS(w, "await nx.test.key('F11'); const a = nx.test.modes(); const m = await nx.test.moveWorld(259.2, 60.7); await nx.test.cmd('polar off'); const p = await nx.test.moveWorld(200, 41.2); const b = nx.test.modes(); await nx.test.key('F11'); await nx.test.cmd('polar on'); await nx.test.key('Escape'); const c = nx.test.modes(); return { otrackOff: !a.otrack, pill: a.pills.includes('otrack:false'), noTrack: !m.trk, polarOff: !b.polar, noPolar: !p.trk, restored: c.polar && c.otrack };");
        GB("aid_f11_f10_toggle", new[] { "otrackOff", "pill", "noTrack", "polarOff", "noPolar", "restored" }.All(k => off.GetProperty(k).GetBoolean()), off.GetRawText());
        // toolbar: TRIM and EXTEND buttons, no overflow
        var tb = await JS(w, "const found = ['trim', 'extend'].filter(n => document.querySelector(`.toolbar [data-tool=${n}]`)); document.querySelector('.toolbar [data-tool=trim]').dispatchEvent(new MouseEvent('click', { bubbles: true })); await nx.test.settle(30); const tool = nx.test.view.tool; await nx.test.key('Escape'); const el = document.querySelector('.toolbar'); return { found: found.length, tool, overflow: el.scrollWidth > el.clientWidth + 1 };");
        GB("aid_toolbar_trim_extend", tb.GetProperty("found").GetInt32() == 2 && tb.GetProperty("tool").GetString() == "trim" && !tb.GetProperty("overflow").GetBoolean(), tb.GetRawText());
        await JS(w, "await nx.test.cmd('tr'); await nx.test.moveWorld(30, 75, { wait: 250 }); await nx.test.moveWorld(30, 75, { wait: 100 }); return true;"); await Shot(w, "13_trim_preview"); await JS(w, "await nx.test.key('Escape'); return true;");
    }

    // ================================================================== draft5g: UI language (Korean default / English) and hiding the side panels
    static async Task LangPanelsAsync(MainWindow w)
    {
        static string S(JsonElement j, string k) => j.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        // no stored choice → Korean
        await SetLang(w, null);
        var ko = await JS(w, "await nx.test.cmd('l'); const u = nx.test.ui(); await nx.test.key('Escape'); await nx.test.key('F1'); const help = document.querySelector('.help h3')?.textContent ?? ''; nx.test.closeModal(); const btn = document.querySelector('.hdr [data-a=lang] span')?.textContent ?? ''; return { ...u, help, btn };");
        bool okKo = S(ko, "lang") == "ko" && S(ko, "open") == "열기" && S(ko, "line").StartsWith("선") && S(ko, "file") == "파일" && S(ko, "props") == "속성" && S(ko, "prompt").Contains("첫 번째 점 지정") && S(ko, "help").Contains("사용법") && S(ko, "btn") == "한국어" && ko.GetProperty("toolbarH").GetInt32() <= 48;
        GB("lang_default_korean", okKo, $"no stored language → lang {S(ko, "lang")}, OPEN '{S(ko, "open")}', line tool '{S(ko, "line")}', cards '{S(ko, "file")}' / '{S(ko, "props")}', LINE prompt '{S(ko, "prompt")}', help '{S(ko, "help")}', button '{S(ko, "btn")}', toolbar one row ({ko.GetProperty("toolbarH").GetInt32()} px)");
        // the header button switches to English (page reload), the drawing stays
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(150); nx.test.closeModal(); await nx.test.cmd('l'); await nx.test.cmd('0,0'); await nx.test.cmd('100,50'); await nx.test.key('Escape'); return true;"); await Task.Delay(150);
        int ents = e.Doc!.Entities.Count;
        await w.Bridge!.Eval("window.__nxOld = 1; document.querySelector('.hdr [data-a=lang]').dispatchEvent(new MouseEvent('click', { bubbles: true }));");
        for (int i = 0; i < 150; i++) { await Task.Delay(100); string r = await w.Bridge!.Eval("(window.nx && window.nx.test && !window.__nxOld && window.nxState) ? document.documentElement.lang : ''"); if (r.Trim('"') != "") break; }
        await Idle(w, 300);
        var en = await JS(w, "await nx.test.settle(300); await nx.test.cmd('l'); const u = nx.test.ui(); await nx.test.key('Escape'); await nx.test.key('F1'); const help = document.querySelector('.help h3')?.textContent ?? ''; nx.test.closeModal(); let stored = null; try { stored = localStorage.getItem('nx.lang'); } catch (e) { } return { ...u, help, stored, ents: nx.test.stats().ents };");
        bool okEn = S(en, "lang") == "en" && S(en, "open") == "OPEN" && S(en, "line") == "Line (L)" && S(en, "file") == "FILE" && S(en, "props") == "PROPERTIES" && S(en, "prompt").Contains("Specify first point") && S(en, "help").Contains("how to use") && S(en, "stored") == "en" && en.GetProperty("toolbarH").GetInt32() <= 48 && en.GetProperty("ents").GetInt32() == ents && e.Doc!.Entities.Count == ents;
        GB("lang_switch_english_keeps_drawing", okEn, $"button click → lang {S(en, "lang")} (stored '{S(en, "stored")}'), OPEN '{S(en, "open")}', line tool '{S(en, "line")}', cards '{S(en, "file")}' / '{S(en, "props")}', LINE prompt '{S(en, "prompt")}', help '{S(en, "help")}', entities on the page {en.GetProperty("ents").GetInt32()} = engine {ents}");

        // help dialog: the skill install button is there (not clicked here — it writes into the user's agent folders; the installer itself is checked by the zip smoke with --home)
        var hb = await JS(w, "await nx.test.key('F1'); const b = document.querySelector('.help [data-b=skill]'); const t = b ? b.textContent : ''; nx.test.closeModal(); return t;");
        GB("help_skill_install_button", (hb.GetString() ?? "").Contains("SKILL"), $"help dialog button '{hb.GetString()}'");
        // side panels: toolbar buttons and Ctrl+1 / Ctrl+2 / Ctrl+0; the drawing takes the freed width
        var p0 = await JS(w, "localStorage.removeItem('nx.panel.left'); localStorage.removeItem('nx.panel.right'); return nx.test.ui();");
        var p1 = await JS(w, "document.querySelector('.toolbar [data-a=pright]').dispatchEvent(new MouseEvent('click', { bubbles: true })); await nx.test.settle(80); return nx.test.ui();");
        var p2 = await JS(w, "await nx.test.key('2', { ctrl: true }); await nx.test.settle(80); return { ...nx.test.ui(), view: nx.test.view.view.w };");
        var p3 = await JS(w, "await nx.test.key('0', { ctrl: true }); await nx.test.settle(80); const a = nx.test.ui(); await nx.test.key('1', { ctrl: true }); await nx.test.settle(80); const b = nx.test.ui(); await nx.test.key('1', { ctrl: true }); await nx.test.settle(80); return { a, b, c: nx.test.ui() };");
        int W(JsonElement j, string k) => j.GetProperty(k).GetInt32();
        bool okP = W(p0, "left") == 260 && W(p0, "right") == 300 && W(p1, "right") == 0 && W(p1, "left") == 260 && W(p1, "canvas") == W(p0, "canvas") + 312 && W(p2, "left") == 0 && W(p2, "canvas") == W(p0, "canvas") + 312 + 272 && Math.Abs(p2.GetProperty("view").GetDouble() - W(p2, "canvas")) <= 1
            && W(p3.GetProperty("a"), "left") == 260 && W(p3.GetProperty("a"), "right") == 300 && W(p3.GetProperty("b"), "right") == 0 && W(p3.GetProperty("c"), "right") == 300 && W(p3.GetProperty("c"), "canvas") == W(p0, "canvas");
        GB("panels_hide_show", okP, $"both {W(p0, "left")}/{W(p0, "right")} canvas {W(p0, "canvas")} → right hidden {W(p1, "left")}/{W(p1, "right")} canvas {W(p1, "canvas")} → Ctrl+2 left hidden too {W(p2, "left")}/{W(p2, "right")} canvas {W(p2, "canvas")} (view {p2.GetProperty("view").GetDouble():F0}) → Ctrl+0 both back {W(p3.GetProperty("a"), "left")}/{W(p3.GetProperty("a"), "right")} → Ctrl+1 {W(p3.GetProperty("b"), "right")} → Ctrl+1 {W(p3.GetProperty("c"), "right")} canvas {W(p3.GetProperty("c"), "canvas")}");
        // a narrow window: the toolbar wraps onto a second row instead of cutting tools off; with the panels hidden everything is on one row again
        double w0 = w.Width; w.Width = Math.Max(900, w0 - 520); await Idle(w, 300);
        var n1 = await JS(w, "await nx.test.settle(150); return nx.test.ui();");
        var n2 = await JS(w, "await nx.test.key('0', { ctrl: true }); await nx.test.settle(150); const u = nx.test.ui(); await nx.test.key('0', { ctrl: true }); await nx.test.settle(80); return u;");
        w.Width = w0; await Idle(w, 300); await JS(w, "await nx.test.settle(150); return true;");
        bool okN = !n1.GetProperty("toolbarOverflow").GetBoolean() && W(n1, "toolsVisible") == W(n1, "tools") && W(n1, "tools") == 20 && W(n2, "toolsVisible") == 20 && W(n2, "toolbarH") <= W(n1, "toolbarH") && W(n2, "left") == 0 && W(n2, "right") == 0;
        GB("toolbar_narrow_window_all_tools_visible", okN, $"window {w0:F0} → {Math.Max(900, w0 - 520):F0} px: canvas {W(n1, "canvas")}, tools visible {W(n1, "toolsVisible")}/{W(n1, "tools")}, toolbar height {W(n1, "toolbarH")} px, overflow {n1.GetProperty("toolbarOverflow").GetBoolean()}; panels hidden: canvas {W(n2, "canvas")}, tools {W(n2, "toolsVisible")}/20, toolbar height {W(n2, "toolbarH")} px");
        await JS(w, "await nx.test.key('0', { ctrl: true }); await nx.test.settle(120); await nx.test.zoomExtents(); return true;"); await Shot(w, "14_panels_hidden_english");
        await JS(w, "await nx.test.key('0', { ctrl: true }); await nx.test.settle(120); return true;");
        await SetLang(w, "ko"); await JS(w, "await nx.test.cmd('tr'); await nx.test.settle(80); return true;"); await Shot(w, "15_korean_ui"); await JS(w, "await nx.test.key('Escape'); return true;");
        await SetLang(w, "en");
    }

    // ================================================================== draft5h: linetypes drawn as dashes, pattern hatches, diameter / radius dimensions — on the screen
    static async Task DraftingAsync(MainWindow w)
    {
        var e = w.Engine; e.New(); await JS(w, "await nx.test.settle(150); nx.test.closeModal(); return true;");
        var d = e.Doc!; var ed = e.Editor!;
        ed.AddLayerWithLinetype("CEN", 1, "CENTER2"); ed.AddLayerWithLinetype("HID", 6, "HIDDEN2"); ed.AddLayer("DIM", 4); ed.AddLayer("HAT", 8);
        ed.AddPolyline(Core.Edit.Shapes.Rectangle(0, 0, 120, 60), true, "0"); string hc = ed.AddCircle(40, 30, 15, "0").Added[0].e.Handle; ed.AddCircle(90, 30, 6, "0");
        string hl = ed.AddLine(-10, 30, 130, 30, "CEN").Added[0].e.Handle; string hh = ed.AddLine(0, 45, 120, 45, "HID").Added[0].e.Handle; string hs = ed.AddLine(0, 15, 120, 15, "0").Added[0].e.Handle;
        var sq = new List<Core.Model.PVertex> { new() { X = 0, Y = 0 }, new() { X = 120, Y = 0 }, new() { X = 120, Y = 60 }, new() { X = 0, Y = 60 } };
        string hat = ed.AddHatch("ANSI31", 1, 0, new[] { sq, Core.Edit.Editor.CircleLoop(40, 30, 15), Core.Edit.Editor.CircleLoop(90, 30, 6) }, "HAT").Added[0].e.Handle;
        ed.AddRadialDimension(40, 30, 15, 30, true, "DIM"); ed.AddRadialDimension(90, 30, 6, 60, false, "DIM");
        ed.AddLinearDimension(new Core.Model.V3(120, 0), new Core.Model.V3(120, 60), new Core.Model.V3(135, 30), 90, "DIM", "Ø<>");
        e.Touch(); await Task.Delay(300);
        var r = await JS(w, $"await nx.test.settle(200); await nx.test.zoomExtents(); const doc = nx.test.doc(); const g = h => doc.geomOf(h); const n = h => {{ const p = g(h).parts[0]; return {{ segs: p.segs.length / 4, draw: p.draw ? p.draw.length / 4 : 0 }}; }}; return {{ centre: n('{hl}'), hidden: n('{hh}'), solid: n('{hs}'), circle: n('{hc}'), hatch: g('{hat}').parts.reduce((a, p) => a + p.segs.length / 4, 0), types: Array.from(doc.ltypes.keys()), hitGap: nx.test.hitAt(1.2, 45), texts: nx.test.texts().map(t => t.text) }};");
        int engineLines = Core.Edit.HatchPattern.Lines((Core.Model.Hatch)d.Find(hat)!).Count / 4;
        int cDraw = r.GetProperty("centre").GetProperty("draw").GetInt32(), hDraw = r.GetProperty("hidden").GetProperty("draw").GetInt32(), sDraw = r.GetProperty("solid").GetProperty("draw").GetInt32();
        // CENTER2 at LTSCALE 1 over 140 units: period 28.575 → 5 long + 5 short dashes (±1); HIDDEN2 over 120: period 4.7625 → 25 or 26 dashes; a continuous line has none
        bool okLt = cDraw >= 9 && cDraw <= 11 && hDraw >= 25 && hDraw <= 26 && sDraw == 0 && r.GetProperty("centre").GetProperty("segs").GetInt32() == 1 && r.GetProperty("hitGap").GetString() == hh;
        GB("draft_linetype_dashes_on_screen", okLt, $"CENTER2 line drawn as {cDraw} dashes (9…11), HIDDEN2 as {hDraw} (25…26), continuous {sDraw}; geometry stays 1 segment; a click in a gap of the hidden line still picks it ({r.GetProperty("hitGap").GetString()} = {hh}); page linetypes {string.Join(",", r.GetProperty("types").EnumerateArray().Select(x => x.GetString()))}");
        int pageLines = r.GetProperty("hatch").GetInt32();
        GB("draft_hatch_pattern_on_screen", pageLines == engineLines && engineLines > 40, $"ANSI31 with two islands: {pageLines} pattern lines on the page = {engineLines} from the engine");
        var tx = r.GetProperty("texts").EnumerateArray().Select(x => x.GetString()).ToList();
        GB("draft_diameter_radius_dims", tx.Contains("Ø30") && tx.Contains("R6") && tx.Contains("Ø60"), "dimension texts on the page: " + string.Join(" · ", tx));
        // LTSCALE 0.5 doubles the dashes
        d.Header["$LTSCALE"] = "0.5"; e.Touch(); await Task.Delay(250);
        var r2 = await JS(w, $"await nx.test.settle(200); const p = nx.test.doc().geomOf('{hl}').parts[0]; return p.draw ? p.draw.length / 4 : 0;");
        GB("draft_ltscale", r2.GetInt32() >= 2 * cDraw - 2 && r2.GetInt32() <= 2 * cDraw + 2, $"LTSCALE 1 → 0.5: {cDraw} → {r2.GetInt32()} dashes");
        await Shot(w, "16_linetypes_hatch_dims");
    }

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
