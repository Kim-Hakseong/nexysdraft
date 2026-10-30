using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using NexysDraft.App.Services;

namespace NexysDraft.App;

/// <summary>
/// WebView2 ⇄ Engine bridge. The page posts JSON commands <c>{id, cmd, …args}</c> and receives <c>{ev:'reply'|'state'|'doc'|'patch'|'status', …}</c>.
/// Host-only commands (dialogs) are delegated to the window. Test steps run inside the page (<c>window.nx.test.run</c>) and reply with <c>{cmd:'test', tid, result}</c>.
/// </summary>
public sealed class Bridge
{
    readonly CoreWebView2 _wv; readonly Engine _e; readonly Dispatcher _ui; readonly Func<string, JsonElement, Task<object?>> _host;
    readonly Dictionary<int, TaskCompletionSource<JsonElement>> _tests = new(); int _testId;
    public bool PageReady { get; private set; }
    public event Action? Ready;
    public static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    bool _stateScheduled; int _docGen;

    public Bridge(CoreWebView2 wv, Engine e, Dispatcher ui, Func<string, JsonElement, Task<object?>> host)
    {
        _wv = wv; _e = e; _ui = ui; _host = host;
        _wv.WebMessageReceived += OnMessage;
        _e.StateChanged += () => { if (_stateScheduled) return; _stateScheduled = true; _ui.BeginInvoke(DispatcherPriority.Background, () => { _stateScheduled = false; PushState(); }); };
        _e.DocReplaced += () =>
        {
            // serialize off the UI thread (a 100k-entity document is a 20 MB message), post on it
            var doc = _e.Doc; int gen = ++_docGen;
            Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string json = doc == null ? JsonSerializer.Serialize(new { ev = "doc", doc = (object?)null }, JsonOpts) : Encoding.UTF8.GetString(NexysDraft.Core.Page.PageJson.DocMessage(doc));
                double ms = sw.Elapsed.TotalMilliseconds;
                _ui.BeginInvoke(DispatcherPriority.Background, () => { if (gen != _docGen || !PageReady) return; _e.SnapshotMs = ms; try { _wv.PostWebMessageAsJson(json); } catch (Exception ex) { Diag.Trace("post doc failed " + ex.Message); } });
            });
        };
        _e.Patched += p => _ui.BeginInvoke(DispatcherPriority.Background, () => { if (PageReady) Post(new { ev = "patch", upsert = p.upsert, remove = p.remove, blocks = p.blocks, name = p.name }); });
        _e.StatusChanged += () => _ui.BeginInvoke(DispatcherPriority.Background, () => { if (PageReady) Post(new { ev = "status", text = _e.StatusText, led = _e.Led, saving = _e.Saving }); });
    }

    public void Post(object msg) { try { _wv.PostWebMessageAsJson(JsonSerializer.Serialize(msg, JsonOpts)); } catch (Exception ex) { Diag.Trace("post failed " + ex.Message); } }
    public void PushState() { if (PageReady) Post(new { ev = "state", state = _e.State() }); }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs a)
    {
        JsonDocument doc; try { doc = JsonDocument.Parse(a.WebMessageAsJson); } catch { return; }
        var root = doc.RootElement; if (root.ValueKind != JsonValueKind.Object) return;
        string cmd = S(root, "cmd"); int id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : -1;
        try
        {
            switch (cmd)
            {
                case "ready": PageReady = true; Ready?.Invoke(); PushState(); if (_e.Doc != null) Post(new { ev = "doc", doc = _e.PageSnapshot() }); return;
                case "files": foreach (var o in a.AdditionalObjects) if (o is CoreWebView2File f && !string.IsNullOrEmpty(f.Path)) await _e.OpenAsync(f.Path); Reply(id, true, null); return;
                case "test": { int tid = root.GetProperty("tid").GetInt32(); lock (_tests) { if (_tests.Remove(tid, out var tcs)) tcs.TrySetResult(root.TryGetProperty("result", out var r) ? r.Clone() : default); } return; }
                case "log": Diag.Trace("web: " + S(root, "text")); return;
            }
            object? result = Dispatch(cmd, root);
            if (result is Task<object?> task) result = await task;
            Reply(id, true, result);
        }
        catch (Exception ex) { Diag.Trace($"cmd {cmd} failed: {ex}"); _e.SetStatus("Error: " + ex.Message, "error"); Reply(id, false, ex.Message); }
    }
    void Reply(int id, bool ok, object? result) { if (id >= 0) Post(new { ev = "reply", id, ok, result }); }

    public static string S(JsonElement r, string k, string d = "") => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? d : d;
    public static int I(JsonElement r, string k, int d = 0) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
    public static double D(JsonElement r, string k, double d = 0) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
    public static bool B(JsonElement r, string k, bool d = false) => r.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : d;
    public static bool? BN(JsonElement r, string k) => r.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;
    public static int? IN(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    public static List<string> SL(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new();
    public static List<double> DL(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetDouble()).ToList() : new();

    object? Dispatch(string cmd, JsonElement r)
    {
        var e = _e;
        switch (cmd)
        {
            case "state": return e.State();
            case "doc": return e.PageSnapshot();
            case "open": return Task.Run(async () => { foreach (var p in SL(r, "paths")) await e.OpenAsync(p); return (object?)null; });
            case "close": e.Close(); return null;
            case "new": e.New(); return null;
            case "save.cancel": e.CancelSave(); return null;
            case "saveTo": return e.SaveAsync(S(r, "path")).ContinueWith(t => (object?)t.Result, TaskScheduler.FromCurrentSynchronizationContext());
            case "exportPdfTo": return e.ExportPdfAsync(S(r, "path")).ContinueWith(t => (object?)t.Result, TaskScheduler.FromCurrentSynchronizationContext());
            case "lossWarnings": return e.LossWarnings();
            case "check": { var ck = e.Check(); return ck == null ? null : new { dwgOk = ck.DwgOk, recommended = ck.Recommended, planB = ck.PlanB, estimateSeconds = ck.EstimateSeconds, items = ck.Items.Select(i => new { kind = i.Kind, count = i.Count, detail = i.Detail, blocking = i.Blocking, handles = i.Handles }).ToList(), fontFixes = ck.FontFixes.Select(f => new { style = f.Style, oldFont = f.OldFont, newFont = f.NewFamilyKo }).ToList() }; }
            case "setFontCompat": e.FontCompat = B(r, "on", true); e.Touch(); return null;
            case "setTheme": e.Theme = S(r, "theme", "light"); e.Touch(); return null;
            case "setLayer": e.CurrentLayer = S(r, "layer", "0"); e.Touch(); return null;
            case "select": e.SetSelection(SL(r, "ids")); return null;
            case "undo": return e.Undo();
            case "redo": return e.Redo();
            case "edit.move": return e.Edit(ed => ed.Move(SL(r, "ids"), D(r, "dx"), D(r, "dy")));
            case "edit.copy": return e.Edit(ed => ed.Copy(SL(r, "ids"), D(r, "dx"), D(r, "dy")));
            case "edit.delete": return e.Edit(ed => ed.Delete(SL(r, "ids")));
            case "edit.rotate": return e.Edit(ed => ed.Rotate(SL(r, "ids"), D(r, "cx"), D(r, "cy"), D(r, "deg")));
            case "edit.scale": return e.Edit(ed => ed.Scale(SL(r, "ids"), D(r, "cx"), D(r, "cy"), D(r, "k", 1)));
            case "edit.mirror": return e.Edit(ed => ed.Mirror(SL(r, "ids"), D(r, "x0"), D(r, "y0"), D(r, "x1"), D(r, "y1"), B(r, "keep", true)));
            case "edit.offset": return e.Edit(ed => ed.Offset(S(r, "id"), D(r, "dist"), new Core.Model.V3(D(r, "sx"), D(r, "sy"))));
            case "edit.stretch": return e.Edit(ed => ed.Stretch(SL(r, "ids"), D(r, "x0"), D(r, "y0"), D(r, "x1"), D(r, "y1"), D(r, "dx"), D(r, "dy")));
            case "edit.text": return e.Edit(ed => B(r, "plain") ? ed.SetMTextPlain(S(r, "id"), S(r, "text")) : ed.SetText(S(r, "id"), S(r, "text")));
            case "edit.layer": return e.Edit(ed => ed.SetLayer(SL(r, "ids"), S(r, "layer", "0")));
            case "draw.line": return e.Edit(ed => ed.AddLine(D(r, "x0"), D(r, "y0"), D(r, "x1"), D(r, "y1"), e.CurrentLayer));
            case "draw.circle": return e.Edit(ed => ed.AddCircle(D(r, "cx"), D(r, "cy"), D(r, "r"), e.CurrentLayer));
            case "draw.pline": { var p = DL(r, "pts"); var pts = new List<(double, double)>(); for (int i = 0; i + 1 < p.Count; i += 2) pts.Add((p[i], p[i + 1])); return e.Edit(ed => ed.AddPolyline(pts, B(r, "closed"), e.CurrentLayer)); }
            case "draw.dim": return e.Edit(ed => ed.AddLinearDimension(new Core.Model.V3(D(r, "x0"), D(r, "y0")), new Core.Model.V3(D(r, "x1"), D(r, "y1")), new Core.Model.V3(D(r, "dx"), D(r, "dy")), D(r, "angle"), e.CurrentLayer));
            case "layer.add": { var c = e.Edit(ed => ed.AddLayer(S(r, "name"), I(r, "color", 7))); if (B(r, "current")) { e.CurrentLayer = S(r, "name"); e.Touch(); } return c; }
            case "layer.set": return e.Edit(ed => ed.SetLayerState(S(r, "name"), BN(r, "off"), BN(r, "frozen"), BN(r, "locked"), IN(r, "color"), r.TryGetProperty("lt", out var lt) ? lt.GetString() : null));
            case "perf": e.PerfNote(S(r, "key"), D(r, "value")); return null;
            default: return _host(cmd, r);
        }
    }

    // ------------------------------------------------------------------ automation
    public Task<JsonElement> RunTest(string body, int timeoutMs = 60000)
    {
        int tid = Interlocked.Increment(ref _testId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_tests) _tests[tid] = tcs;
        _ = _wv.ExecuteScriptAsync($"window.nx.test.run({tid}, async () => {{ {body} }})");
        _ = Task.Delay(timeoutMs).ContinueWith(_ => { lock (_tests) _tests.Remove(tid); tcs.TrySetException(new TimeoutException("test step timed out: " + body[..Math.Min(80, body.Length)])); });
        return tcs.Task;
    }
    public Task<string> Eval(string js) => _wv.ExecuteScriptAsync(js);
}
