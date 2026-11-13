using System.Diagnostics;
using System.IO;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Dxf;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using NexysDraft.Core.Page;
using NexysDraft.Core.Pdf;

namespace NexysDraft.App.Services;

/// <summary>Application model behind the page: the open document, its editor (undo/redo), selection, current layer, theme, status. File I/O: DXF directly,
/// DWG through LibreDWG (dwg2dxf / dxf2dwg, separate processes, background with progress and cancel), PDF through the built-in writer.</summary>
public sealed class Engine
{
    public Document? Doc { get; private set; }
    public Editor? Editor { get; private set; }
    public string Theme { get; set; } = "light";
    /// <summary>Drawing-area background: auto (follows <see cref="Theme"/>) | light | dark (draft5 I2, remembered in layout.json).</summary>
    public string CanvasTheme { get; set; } = "auto";
    public string CurrentLayer { get; set; } = "0";
    /// <summary>nanoCAD font compatibility on save (default ON, remembered in layout.json).</summary>
    public bool FontCompat { get; set; } = true;
    public List<FontFix> LastFontFixes { get; } = new();
    /// <summary>DXF version on save: auto | r2000 | keep (save dialog choice, draft4 H1).</summary>
    public string DxfVersion { get; set; } = "auto";
    /// <summary>What the last save wrote and why (status line, result.json).</summary>
    public sealed class SaveInfo { public string Format { get; set; } = ""; public string Version { get; set; } = ""; public string Reason { get; set; } = ""; public List<string> DroppedImages { get; set; } = new(); }
    public SaveInfo LastSave { get; private set; } = new();
    public List<string> Selection { get; } = new();
    public List<string> Recent { get; } = new();
    public const int RecentMax = 10;
    public string StatusText { get; private set; } = ""; public string Led { get; private set; } = "idle";
    public string? SourceDwg { get; private set; }
    public double OpenMs { get; private set; } public double ConvertMs { get; private set; } public double SnapshotMs { get; set; }
    public List<string> OpenWarnings { get; } = new();
    public Dictionary<string, double> Perf { get; } = new();
    /// <summary>A DWG save (dxf2dwg) is running: elapsed / estimated seconds for the status line, cancellable.</summary>
    public bool Saving { get; private set; } public double SaveElapsed { get; private set; } public double SaveEstimate { get; private set; }
    CancellationTokenSource? _saveCts;
    public event Action? StateChanged, DocReplaced, StatusChanged, TitleChanged;
    public event Action<(List<object> upsert, List<string> remove, Dictionary<string, object> blocks, string name)>? Patched;

    public void SetStatus(string text, string led = "ready") { StatusText = text; Led = led; StatusChanged?.Invoke(); }
    public void Touch() => StateChanged?.Invoke();
    public void PerfNote(string key, double v) { Perf[key] = v; }

    public object State() => new
    {
        app = new { name = AppInfo.Name, versionText = AppInfo.VersionText, libredwg = LibreDwg.Available ? LibreDwg.Version : null },
        theme = Theme, canvasTheme = CanvasTheme, status = new { text = StatusText, led = Led, saving = Saving },
        fontCompat = FontCompat,
        file = Doc == null ? null : new { path = Doc.Path, name = Doc.Path == "" ? "untitled.dxf" : Path.GetFileName(Doc.Path), version = Doc.Version, codepage = Doc.CodePage, dirty = Doc.Dirty, sourceDwg = SourceDwg, openMs = OpenMs, convertMs = ConvertMs, entities = Doc.Entities.Count, blocks = Doc.Blocks.Count, unknown = Doc.UnknownCount, warnings = OpenWarnings, counts = Doc.TypeCounts, size = File.Exists(Doc.Path) ? new FileInfo(Doc.Path).Length : 0 },
        layers = Doc?.Layers.Select(l => new { name = l.Name, color = l.Color, tc = l.TrueColor, off = l.Off, frozen = l.Frozen, locked = l.Locked, lt = l.Linetype, count = 0 }).ToList(),
        ltypes = Doc?.Linetypes.Select(l => new { name = l.Name, pattern = l.Pattern }).ToList(), ltscale = Doc?.HeaderDouble("$LTSCALE", 1) ?? 1,
        currentLayer = CurrentLayer, selection = Selection,
        undo = new { can = Editor?.CanUndo ?? false, name = Editor?.UndoName, canRedo = Editor?.CanRedo ?? false, redoName = Editor?.RedoName },
        recent = Recent,
    };
    public object? PageSnapshot() => Doc == null ? null : PageDoc.Snapshot(Doc);

    // ------------------------------------------------------------------ files
    public void Open(string path) => _ = OpenAsync(path);
    /// <summary>Empty drawing (layer 0, Standard = malgun.ttf).</summary>
    public void New()
    {
        var doc = Document.CreateNew(); Doc = doc; Editor = new Editor(doc); Editor.Applied += OnApplied; SourceDwg = null; ConvertMs = 0; OpenMs = 0; OpenWarnings.Clear(); Selection.Clear(); CurrentLayer = "0";
        SetStatus("New drawing"); DocReplaced?.Invoke(); StateChanged?.Invoke(); TitleChanged?.Invoke();
    }
    public async Task<bool> OpenAsync(string path)
    {
        try
        {
            SetStatus("Opening " + Path.GetFileName(path) + " …", "busy");
            var sw = Stopwatch.StartNew(); string dxf = path; string? src = null; double convMs = 0; var warnings = new List<string>();
            if (path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                src = path; dxf = Path.Combine(Path.GetTempPath(), "NexysDraft", "open", Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N")[..6] + ".dxf");
                var r = await Task.Run(() => LibreDwg.DwgToDxf(path, dxf));
                convMs = r.Ms;
                if (!r.Ok) { SetStatus("DWG → DXF failed: " + string.Join(" | ", LibreDwg.Summarize(r.Stderr).Take(2)), "error"); return false; }
                warnings.AddRange(LibreDwg.Summarize(r.Stderr).Take(8));
            }
            var doc = await Task.Run(() => DxfReader.Read(dxf));
            doc.Path = src ?? path; doc.FromLibreDwg = src != null;
            Doc = doc; Editor = new Editor(doc); Editor.Applied += OnApplied;   // no forced GC here: a blocking gen2 on the server heap showed up as a >300 ms UI stall in --selftest
            SourceDwg = src; ConvertMs = convMs; OpenMs = sw.Elapsed.TotalMilliseconds; OpenWarnings.Clear(); OpenWarnings.AddRange(warnings);
            Selection.Clear(); CurrentLayer = doc.LayerOf(doc.HeaderString("$CLAYER", "0")) != null ? doc.HeaderString("$CLAYER", "0") : "0";
            AddRecent(doc.Path);
            Diag.Trace($"open {Path.GetFileName(path)}: {doc.Entities.Count} entities, {doc.Layers.Count} layers, {OpenMs:F0} ms (convert {convMs:F0} ms)");
            SetStatus($"Opened {Path.GetFileName(path)} — {doc.Entities.Count:N0} entities, {doc.Layers.Count} layers, {doc.Version}, {doc.CodePage} ({OpenMs:F0} ms{(src != null ? $", LibreDWG {convMs:F0} ms" : "")})");
            DocReplaced?.Invoke(); StateChanged?.Invoke(); TitleChanged?.Invoke();
            return true;
        }
        catch (Exception ex) { Diag.Trace("open failed " + ex); SetStatus("Open failed: " + ex.Message, "error"); return false; }
    }
    void AddRecent(string path) { if (string.IsNullOrEmpty(path)) return; Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)); Recent.Insert(0, path); while (Recent.Count > RecentMax) Recent.RemoveAt(Recent.Count - 1); }
    public void Close()
    {
        Doc = null; Editor = null; SourceDwg = null; Selection.Clear(); DocReplaced?.Invoke(); StateChanged?.Invoke(); TitleChanged?.Invoke(); SetStatus("Closed", "idle");
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect(2, GCCollectionMode.Optimized, false, true);
    }

    /// <summary>dxf2dwg time estimate from the R2000 DXF size (measured 2026-10-30 on the R2000 DXF: 0.53 MB 2.4 s, 2.38 MB 38–59 s, 2.80 MB 79–125 s, 4.13 MB 128–205 s → ≈ 1 + 12·MB²).</summary>
    public static double EstimateDwgSeconds(long dxfBytes) { double mb = dxfBytes / 1048576.0; return 1 + 12 * mb * mb; }

    /// <summary>Save as DXF (same version, verbatim for untouched parts, pure ASCII) or DWG (R2000 DXF → dxf2dwg in the background; status shows elapsed / estimate; cancellable).</summary>
    /// <param name="dxfVer">DXF only: auto (R2000 when the original is newer than AC1027, else the original version), r2000 or keep; null = <see cref="DxfVersion"/>.</param>
    /// <param name="dropImages">DWG only: leave the raster IMAGE entities out (the save dialog's "without IMAGE" choice, draft4 H2).</param>
    public async Task<bool> SaveAsync(string path, string? dxfVer = null, bool dropImages = false)
    {
        if (Doc == null) return false;
        if (Saving) { SetStatus("A save is already running", "busy"); return false; }
        try
        {
            SetStatus("Saving " + Path.GetFileName(path) + " …", "busy"); var sw = Stopwatch.StartNew();
            bool dwg = path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase);
            LastSave = new SaveInfo();
            if (dwg)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NexysDraft", "save", Path.GetFileNameWithoutExtension(path) + ".dxf"); Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                var loss = R2000Loss.Detect(Doc); var docRef = Doc;
                var fixes = FontCompat ? Core.Dxf.FontCompat.Plan(docRef) : new List<FontFix>(); LastFontFixes.Clear(); LastFontFixes.AddRange(fixes);
                LastSave = new SaveInfo { Format = "dwg", Version = "AC1015", Reason = "DWG is always R2000 (LibreDWG dxf2dwg)", DroppedImages = dropImages ? Doc.Entities.Where(e => e.DxfName == "IMAGE").Select(e => e.Handle).ToList() : new() };
                var w = await Task.Run(() => DxfWriter.Write(docRef, tmp, DxfWriter.Mode.R2000, fixes, dropImages));
                SaveEstimate = R2000Check.EstimateSeconds(new FileInfo(tmp).Length); Saving = true; _saveCts = new CancellationTokenSource(); var ct = _saveCts.Token;
                var ticker = Task.Run(async () => { while (!ct.IsCancellationRequested && Saving) { SaveElapsed = sw.Elapsed.TotalSeconds; SetStatus($"Saving DWG via LibreDWG dxf2dwg … {SaveElapsed:F0} s / ~{SaveEstimate:F0} s (cancel in the status bar)", "busy"); try { await Task.Delay(500, ct); } catch { } } });
                var r = await Task.Run(() => LibreDwg.DxfToDwg(tmp, path, "r2000", 3600000, ct));
                Saving = false; try { _saveCts.Cancel(); } catch { } await ticker;
                if (ct.IsCancellationRequested && !r.Ok) { SetStatus($"DWG save cancelled after {sw.Elapsed.TotalSeconds:F0} s", "idle"); StateChanged?.Invoke(); return false; }
                if (!r.Ok) { SetStatus("DXF → DWG failed: " + string.Join(" | ", LibreDwg.Summarize(r.Stderr).Take(2)), "error"); StateChanged?.Invoke(); return false; }
                Doc.Dirty = false; Doc.Path = path; SourceDwg = path;
                SetStatus($"Saved {Path.GetFileName(path)} (R2000 via LibreDWG, {(LastFontFixes.Count > 0 ? $"font compat {LastFontFixes.Count} style(s), " : "")}{r.Ms / 1000:F1} s{(SaveEstimate > 0 ? $", estimate was {SaveEstimate:F0} s" : "")}){(LastSave.DroppedImages.Count > 0 ? $" — {LastSave.DroppedImages.Count} IMAGE left out: " + string.Join(" ", LastSave.DroppedImages.Take(6)) : "")}{(loss.Count > 0 ? $" — {loss.Sum(l => l.Count)} item(s) not carried: " + string.Join(", ", loss.Take(3).Select(l => l.Kind)) : "")}{(w.Count > 0 ? "; " + w[0] : "")}");
            }
            else
            {
                var docRef = Doc; var fixes = FontCompat ? Core.Dxf.FontCompat.Plan(docRef) : new List<FontFix>(); LastFontFixes.Clear(); LastFontFixes.AddRange(fixes);
                var (mode, reason) = DxfSave.Choose(docRef, DxfSave.Parse(dxfVer ?? DxfVersion));
                LastSave = new SaveInfo { Format = "dxf", Version = mode == DxfWriter.Mode.R2000 ? "AC1015" : Doc.Version, Reason = reason };
                await Task.Run(() => DxfWriter.Write(docRef, path, mode, fixes, false, mode == DxfWriter.Mode.R2000));   // a DXF for AutoCAD gets the full OBJECTS skeleton (draft5d)
                Doc.Dirty = false; Doc.Path = path; SourceDwg = null;
                SetStatus($"Saved {Path.GetFileName(path)} ({LastSave.Version}: {reason}, ASCII, {sw.Elapsed.TotalMilliseconds:F0} ms){(LastFontFixes.Count > 0 ? $" — font compat: {LastFontFixes.Count} style(s) → " + string.Join(", ", LastFontFixes.Select(f => $"{f.Style}: {f.OldFont} → {f.NewFamily} {f.NewFile}")) : "")}");
            }
            AddRecent(path);
            StateChanged?.Invoke(); TitleChanged?.Invoke(); return true;
        }
        catch (Exception ex) { Saving = false; Diag.Trace("save failed " + ex); SetStatus("Save failed: " + ex.Message, "error"); return false; }
    }
    public void CancelSave() { try { _saveCts?.Cancel(); } catch { } }
    public List<LossWarning> LossWarnings() => Doc == null ? new() : R2000Loss.Detect(Doc);
    public R2000CheckResult? Check() => Doc == null ? null : R2000Check.Run(Doc, FontCompat);

    /// <summary>PDF through the built-in writer: one page per detected sheet (+ rest page), real text (Malgun Gothic subset). With sheets, also one file per sheet next to it.</summary>
    /// <summary>New drawings: set $DIMTXT/$DIMASZ/$DIMEXO/$DIMEXE/$DIMGAP/$TEXTSIZE so text prints at 2.5 mm on the chosen paper, and rebuild the
    /// linear dimensions with them (two passes: dimensions change the extents). Returns the text height in drawing units, or 0 when not applicable.</summary>
    public double AutoSizeText(double paperMm = 2.5)
    {
        if (Doc == null || !Doc.AutoTextSize || Editor == null) return 0;
        double txt = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            var sheet = Core.Pdf.SheetDetector.Detect(Doc, out _).First(); double mmPerUnit = Core.Pdf.DrawingPdf.MmPerUnit(Doc, sheet); if (mmPerUnit <= 0) return 0;
            txt = paperMm / mmPerUnit; var ic = System.Globalization.CultureInfo.InvariantCulture;
            Doc.Header["$DIMTXT"] = txt.ToString("R", ic); Doc.Header["$DIMASZ"] = txt.ToString("R", ic); Doc.Header["$DIMEXO"] = (txt * 0.25).ToString("R", ic); Doc.Header["$DIMEXE"] = (txt * 0.5).ToString("R", ic); Doc.Header["$DIMGAP"] = (txt * 0.25).ToString("R", ic); Doc.Header["$TEXTSIZE"] = txt.ToString("R", ic); Doc.Header["$DIMSCALE"] = "1.0";
            Editor.RegenerateDimensions();
        }
        DocReplaced?.Invoke();
        return txt;
    }

    /// <param name="merged">name.pdf with every sheet as a page</param><param name="sheets">name__001.pdf … one file per detected sheet</param><param name="overview">name__overview.pdf, the whole model space on one page</param>
    /// <param name="explicitParts">the caller named the parts (CLI flags): no single-sheet collapse</param>
    public async Task<string?> ExportPdfAsync(string pdfPath, bool merged = true, bool sheets = true, bool overview = true, bool explicitParts = false, bool mono = false)
    {
        if (Doc == null) return null;
        try
        {
            AutoSizeText();
            SetStatus("Exporting PDF …", "busy"); var docRef = Doc; var sw = Stopwatch.StartNew();
            bool collapse = !explicitParts && merged && (sheets || overview);
            var (res, files) = await Task.Run(() => DrawingPdf.ExportSet(docRef, pdfPath, merged, sheets, overview, null, collapse, mono));
            LastPdf = res; LastPdfFiles.Clear(); LastPdfFiles.AddRange(files);
            LastPdfNote = collapse && res != null && res.Pages == 1 ? "single sheet — merged only" : "";
            string list = string.Join(", ", files.Select(f => $"{Path.GetFileName(f.Path)} ({f.Pages} p, {f.Paper})"));
            SetStatus($"PDF: {files.Count} file(s), {files.Sum(f => f.Pages)} page(s){(LastPdfNote != "" ? " (" + LastPdfNote + ")" : "")}{(mono ? " · monochrome" : "")} — {list}{(res != null ? $" · {res.Texts} texts" : "")} · {sw.Elapsed.TotalMilliseconds:F0} ms");
            return files.Count > 0 ? files[0].Path : null;
        }
        catch (Exception ex) { Diag.Trace("pdf failed " + ex); SetStatus("PDF export failed: " + ex.Message, "error"); return null; }
    }
    public List<DrawingPdf.OutFile> LastPdfFiles { get; } = new();
    /// <summary>"single sheet — merged only" when the set collapsed to the merged file, else "".</summary>
    public string LastPdfNote { get; private set; } = "";
    public DrawingPdf.Result? LastPdf { get; private set; }

    // ------------------------------------------------------------------ editing
    public void SetSelection(IEnumerable<string> ids) { Selection.Clear(); Selection.AddRange(ids); StateChanged?.Invoke(); }
    public object? Edit(Func<Editor, Change?> f)
    {
        if (Editor == null) return null;
        var c = f(Editor);
        return c == null ? null : new { name = c.Name, touched = c.Touched.ToList() };
    }
    public object? Undo() { var c = Editor?.Undo(); if (c != null) SetStatus("Undo " + c.Name); return c?.Name; }
    public object? Redo() { var c = Editor?.Redo(); if (c != null) SetStatus("Redo " + c.Name); return c?.Name; }

    void OnApplied(Change c)
    {
        if (Doc == null) return;
        var fontOf = new Dictionary<string, (string font, bool shx)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Doc.Styles) fontOf[s.Name] = (s.TrueTypeFamily ?? s.Font, s.TrueTypeFamily == null);
        var touched = new HashSet<string>(c.Touched);
        var upsert = new List<object>(); var remove = new List<string>(); var blocks = new Dictionary<string, object>();
        foreach (var h in touched)
        {
            var e = Doc.Find(h);
            if (e == null) { remove.Add(h); continue; }
            var rec = PageDoc.Record(e, Doc, fontOf); if (rec != null) upsert.Add(rec); else remove.Add(h);
            if (e is Dimension d && d.Block != "" && Doc.Blocks.TryGetValue(d.Block, out var b)) blocks[d.Block] = new { name = b.Name, bx = b.Base.X, by = b.Base.Y, ents = b.Entities.Select(x => PageDoc.Record(x, Doc, fontOf)).Where(x => x != null).ToList() };
        }
        foreach (var (name, b) in c.BlocksAdded) if (Doc.Blocks.ContainsKey(name)) blocks[name] = new { name = b.Name, bx = b.Base.X, by = b.Base.Y, ents = b.Entities.Select(x => PageDoc.Record(x, Doc, fontOf)).Where(x => x != null).ToList() };
        Selection.RemoveAll(s => remove.Contains(s));
        Patched?.Invoke((upsert, remove, blocks, c.Name));
        StateChanged?.Invoke(); TitleChanged?.Invoke();
    }
}
