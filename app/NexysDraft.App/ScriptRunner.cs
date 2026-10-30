using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using NexysDraft.App.Services;
using NexysDraft.Core.Edit;
using NexysDraft.Core.Model;
using NexysDraft.Core.Pdf;

namespace NexysDraft.App;

/// <summary>
/// <c>NexysDraft.exe --script job.txt [--out dir] [--json]</c>: line-oriented commands without a window. Each line: a command and its
/// arguments (coordinates as x,y). Results and errors (line number, reason) go to result.json in --out (default: the job file's folder) and,
/// with --json, to stdout. Exit code 0 when every line succeeded, 1 otherwise, 2 when the job file could not be read.
/// Commands: new | open PATH | save PATH(.dxf|.dwg) | pdf PATH | select all|none|window X0,Y0 X1,Y1|crossing …|handles H1 H2 … | layer new NAME [COLOR] |
/// layer current NAME | layer set NAME on|off|frozen|thawed|locked|unlocked | l X0,Y0 X1,Y1 [X2,Y2 …] | pl [c] X,Y … | c CX,CY R | dli X0,Y0 X1,Y1 DX,DY [angle] |
/// m DX,DY | co DX,DY | e | ro CX,CY DEG | mi X0,Y0 X1,Y1 [erase] | o H DIST SX,SY | s X0,Y0 X1,Y1 DX,DY | text H STRING | replace OLD NEW | undo | redo |
/// query entities|layers|extents|texts | echo TEXT. Lines starting with # are comments.
/// </summary>
public static class ScriptRunner
{
    public static int Run(string[] args)
    {
        string job = Arg(args, "--script"); string outDir = Arg(args, "--out", Path.GetDirectoryName(Path.GetFullPath(job)) ?? "."); bool json = args.Contains("--json");
        var result = new Dictionary<string, object?> { ["job"] = job, ["ok"] = false, ["lines"] = new List<object>(), ["errors"] = new List<object>() };
        var lines = (List<object>)result["lines"]!; var errors = (List<object>)result["errors"]!;
        string[] script;
        try { script = File.ReadAllLines(job, Encoding.UTF8); } catch (Exception ex) { errors.Add(new { line = 0, error = "cannot read job: " + ex.Message }); return Finish(2, result, outDir, json); }
        var e = new Engine(); var sel = new List<string>();
        bool ok = true;
        for (int i = 0; i < script.Length; i++)
        {
            string raw = script[i].Trim(); if (raw.Length == 0 || raw.StartsWith("#")) continue;
            var sw = System.Diagnostics.Stopwatch.StartNew(); object? res = null; string? err = null;
            try { res = Exec(e, sel, raw, outDir); }
            catch (Exception ex) { err = ex.Message; }
            if (err != null) { ok = false; errors.Add(new { line = i + 1, cmd = raw, error = err }); lines.Add(new { line = i + 1, cmd = raw, error = err, ms = sw.Elapsed.TotalMilliseconds }); if (json) Console.Error.WriteLine($"line {i + 1}: {raw} → {err}"); break; }
            lines.Add(new { line = i + 1, cmd = raw, result = res, ms = sw.Elapsed.TotalMilliseconds });
        }
        result["ok"] = ok; result["entities"] = e.Doc?.Entities.Count ?? 0; result["layers"] = e.Doc?.Layers.Count ?? 0; result["file"] = e.Doc?.Path;
        return Finish(ok ? 0 : 1, result, outDir, json);
    }
    static int Finish(int code, Dictionary<string, object?> result, string outDir, bool json)
    {
        string text = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        try { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "result.json"), text, new UTF8Encoding(false)); } catch { }
        if (json) { try { Console.OutputEncoding = Encoding.UTF8; } catch { } Console.WriteLine(text); }
        return code;
    }
    static string Arg(string[] a, string name, string dflt = "") { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : dflt; }

    static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase) { ["line"] = "l", ["pline"] = "pl", ["circle"] = "c", ["move"] = "m", ["copy"] = "co", ["cp"] = "co", ["erase"] = "e", ["del"] = "e", ["rotate"] = "ro", ["mirror"] = "mi", ["offset"] = "o", ["stretch"] = "s", ["dimlinear"] = "dli", ["dim"] = "dli", ["u"] = "undo", ["ed"] = "text", ["sel"] = "select" };

    static object? Exec(Engine e, List<string> sel, string raw, string outDir)
    {
        var parts = Split(raw); string cmd = parts[0].ToLowerInvariant(); if (Alias.TryGetValue(cmd, out var al)) cmd = al;
        var a = parts.Skip(1).ToList();
        Document Doc() => e.Doc ?? throw new InvalidOperationException("no drawing open (use open or new)");
        Editor Ed() => e.Editor ?? throw new InvalidOperationException("no drawing open");
        List<string> Sel() { if (sel.Count == 0) throw new InvalidOperationException("nothing selected (use select)"); return sel; }
        switch (cmd)
        {
            case "echo": return string.Join(" ", a);
            case "new": e.New(); return "new drawing";
            case "open": { string p = Path.GetFullPath(string.Join(" ", a)); if (!e.OpenAsync(p).GetAwaiter().GetResult()) throw new IOException(e.StatusText); sel.Clear(); return new { entities = e.Doc!.Entities.Count, layers = e.Doc.Layers.Count, version = e.Doc.Version, ms = e.OpenMs }; }
            case "save": { string p = Path.GetFullPath(string.Join(" ", a)); Doc(); if (!e.SaveAsync(p).GetAwaiter().GetResult()) throw new IOException(e.StatusText); return new { path = p, status = e.StatusText }; }
            case "pdf": { string p = Path.GetFullPath(string.Join(" ", a)); Doc(); var r = e.ExportPdfAsync(p).GetAwaiter().GetResult() ?? throw new IOException(e.StatusText); var lp = e.LastPdf!; return new { path = p, pages = lp.Pages, sheets = lp.Sheets.Count(s => s.Label != "rest"), detected = lp.SheetsDetected, texts = lp.Texts, ms = lp.Ms }; }
            case "select":
                {
                    var d = Doc(); string what = a.Count > 0 ? a[0].ToLowerInvariant() : "all";
                    if (what == "all") { sel.Clear(); sel.AddRange(d.Entities.Where(x => !x.PaperSpace).Select(x => x.Handle)); }
                    else if (what == "none") sel.Clear();
                    else if (what is "window" or "crossing") { var p0 = Pt(a[1]); var p1 = Pt(a[2]); sel.Clear(); sel.AddRange(SelectRect(d, p0, p1, what == "crossing")); }
                    else if (what == "handles") { sel.Clear(); sel.AddRange(a.Skip(1).Where(h => d.Find(h) != null)); }
                    else if (what == "layer") { sel.Clear(); sel.AddRange(d.Entities.Where(x => x.Layer.Equals(a[1], StringComparison.OrdinalIgnoreCase)).Select(x => x.Handle)); }
                    else if (what == "type") { sel.Clear(); sel.AddRange(d.Entities.Where(x => x.DxfName.Equals(a[1], StringComparison.OrdinalIgnoreCase)).Select(x => x.Handle)); }
                    else throw new ArgumentException("select all|none|window P0 P1|crossing P0 P1|handles H…|layer NAME|type TYPE");
                    return new { selected = sel.Count };
                }
            case "layer":
                {
                    string sub = a.Count > 0 ? a[0].ToLowerInvariant() : ""; var d = Doc();
                    if (sub == "new") { Ed().AddLayer(a[1], a.Count > 2 ? int.Parse(a[2]) : 7); return new { layer = a[1] }; }
                    if (sub == "current") { if (d.LayerOf(a[1]) == null) Ed().AddLayer(a[1]); e.CurrentLayer = a[1]; return new { current = a[1] }; }
                    if (sub == "set") { string st = a[2].ToLowerInvariant(); var c = Ed().SetLayerState(a[1], off: st == "off" ? true : st == "on" ? false : null, frozen: st == "frozen" ? true : st == "thawed" ? false : null, locked: st == "locked" ? true : st == "unlocked" ? false : null, color: int.TryParse(st, out var ci) ? ci : null) ?? throw new ArgumentException("unknown layer " + a[1]); return new { layer = a[1], state = st }; }
                    throw new ArgumentException("layer new NAME [COLOR] | layer current NAME | layer set NAME on|off|frozen|thawed|locked|unlocked|COLOR");
                }
            case "l": { if (a.Count < 2) throw new ArgumentException("l X0,Y0 X1,Y1 [X2,Y2 …]"); int n = 0; for (int i = 0; i + 1 < a.Count; i++) { var p = Pt(a[i]); var q = Pt(a[i + 1]); Ed().AddLine(p.x, p.y, q.x, q.y, e.CurrentLayer); n++; } return new { lines = n }; }
            case "pl": { bool closed = a.Count > 0 && a[0].Equals("c", StringComparison.OrdinalIgnoreCase); var pts = a.Where(x => x.Contains(',')).Select(Pt).Select(p => (p.x, p.y)).ToList(); if (pts.Count < 2) throw new ArgumentException("pl [c] X,Y X,Y …"); Ed().AddPolyline(pts, closed, e.CurrentLayer); return new { vertices = pts.Count, closed }; }
            case "c": { var p = Pt(a[0]); double r = D(a[1]); Ed().AddCircle(p.x, p.y, r, e.CurrentLayer); return new { center = a[0], r }; }
            case "dli": { var p0 = Pt(a[0]); var p1 = Pt(a[1]); var pl = Pt(a[2]); double ang = a.Count > 3 ? D(a[3]) : (Math.Abs(pl.y - (p0.y + p1.y) / 2) >= Math.Abs(pl.x - (p0.x + p1.x) / 2) ? 0 : 90); var c = Ed().AddLinearDimension(new V3(p0.x, p0.y), new V3(p1.x, p1.y), new V3(pl.x, pl.y), ang, e.CurrentLayer); var dim = e.Doc!.Entities.OfType<Dimension>().Last(); return new { measurement = dim.Measurement, angle = ang }; }
            case "m": { var d = Pt(a[0]); Ed().Move(Sel(), d.x, d.y); return new { moved = sel.Count }; }
            case "co": { var d = Pt(a[0]); var c = Ed().Copy(Sel(), d.x, d.y); return new { copied = c.Added.Count }; }
            case "e": { int n = Sel().Count; Ed().Delete(sel); sel.Clear(); return new { erased = n }; }
            case "ro": { var c = Pt(a[0]); Ed().Rotate(Sel(), c.x, c.y, D(a[1])); return new { rotated = sel.Count }; }
            case "mi": { var p0 = Pt(a[0]); var p1 = Pt(a[1]); bool erase = a.Count > 2 && a[2].StartsWith("e", StringComparison.OrdinalIgnoreCase); Ed().Mirror(Sel(), p0.x, p0.y, p1.x, p1.y, !erase); return new { mirrored = sel.Count, erase }; }
            case "o": { var side = Pt(a[2]); var c = Ed().Offset(a[0], D(a[1]), new V3(side.x, side.y)) ?? throw new ArgumentException("offset: entity not found or type not supported (line, circle, arc, lwpolyline)"); return new { offset = a[0] }; }
            case "s": { var p0 = Pt(a[0]); var p1 = Pt(a[1]); var dd = Pt(a[2]); Ed().Stretch(Sel(), p0.x, p0.y, p1.x, p1.y, dd.x, dd.y); return new { stretched = sel.Count }; }
            case "text": { string h = a[0]; string text = string.Join(" ", a.Skip(1)); var ent = Doc().Find(h) ?? throw new ArgumentException("no entity " + h); var c = ent is MText ? Ed().SetMTextPlain(h, text) : Ed().SetText(h, text); if (c == null) throw new ArgumentException(h + " is not TEXT/MTEXT"); return new { handle = h, text }; }
            case "replace":
                {
                    if (a.Count < 2) throw new ArgumentException("replace OLD NEW"); string oldS = a[0], newS = a[1]; var d = Doc(); int n = 0;
                    foreach (var t in d.Entities.OfType<Text>().ToList()) if (t.Value.Contains(oldS)) { Ed().SetText(t.Handle, t.Value.Replace(oldS, newS)); n++; }
                    foreach (var m in d.Entities.OfType<MText>().ToList()) if (m.Value.Contains(oldS)) { Ed().SetText(m.Handle, m.Value.Replace(oldS, newS)); n++; }
                    foreach (var ins in d.Entities.OfType<Insert>()) foreach (var at in ins.Attribs) if (at.Text.Value.Contains(oldS)) { at.Text.Value = at.Text.Value.Replace(oldS, newS); at.Value = at.Text.Value; at.Text.Dirty = true; at.Raw = null; ins.Dirty = true; d.Dirty = true; n++; }
                    return new { replaced = n };
                }
            case "undo": return new { undone = e.Undo() };
            case "redo": return new { redone = e.Redo() };
            case "query":
                {
                    var d = Doc(); string what = a.Count > 0 ? a[0].ToLowerInvariant() : "entities";
                    if (what == "entities") return new { total = d.Entities.Count, model = d.Entities.Count(x => !x.PaperSpace), byType = d.Entities.Where(x => !x.PaperSpace).GroupBy(x => x.DxfName).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count()), byLayer = d.Entities.Where(x => !x.PaperSpace).GroupBy(x => x.Layer).ToDictionary(g => g.Key, g => g.Count()), blocks = d.Blocks.Count, unknown = d.UnknownCount };
                    if (what == "layers") return d.Layers.Select(l => new { l.Name, l.Color, l.Off, l.Frozen, l.Locked, l.Linetype, count = d.Entities.Count(x => x.Layer.Equals(l.Name, StringComparison.OrdinalIgnoreCase)) }).ToList();
                    if (what == "extents") { var b = Core.Geometry.Bounds.Of(d); return b == null ? null : new { x0 = b.Value.x0, y0 = b.Value.y0, x1 = b.Value.x1, y1 = b.Value.y1, width = b.Value.x1 - b.Value.x0, height = b.Value.y1 - b.Value.y0 }; }
                    if (what == "texts") return d.Entities.Where(x => !x.PaperSpace).Select(x => x switch { Text t => new { handle = t.Handle, type = t.DxfName, text = t.Value, x = t.Pos.X, y = t.Pos.Y, height = t.Height }, MText m => new { handle = m.Handle, type = "MTEXT", text = Core.MTextFmt.MTextParser.Plain(m.Value), x = m.Pos.X, y = m.Pos.Y, height = m.Height }, _ => null }).Where(x => x != null).ToList();
                    if (what == "selection") return sel;
                    if (what == "blockbounds") { var blk = d.Blocks.TryGetValue(a[1], out var bb) ? bb : throw new ArgumentException("no block " + a[1]); return blk.Entities.Select(x => (e: x, b: Core.Geometry.Bounds.Of(x, d, 1))).Select(x => new { handle = x.e.Handle, type = x.e.DxfName, x0 = x.b?.x0, y0 = x.b?.y0, x1 = x.b?.x1, y1 = x.b?.y1, text = x.e is Text tt ? tt.Value : x.e is MText mm ? mm.Value[..Math.Min(40, mm.Value.Length)] : null, h = x.e is Text t2 ? t2.Height : x.e is MText m2 ? m2.Height : 0, w = x.e is MText m3 ? m3.Width : 0 }).ToList(); }
                    if (what == "bounds") return d.Entities.Where(x => !x.PaperSpace).Select(x => (e: x, b: Core.Geometry.Bounds.Of(x, d, 0))).Where(x => x.b != null).OrderByDescending(x => Math.Max(Math.Abs(x.b!.Value.x1), Math.Abs(x.b!.Value.x0)) + Math.Max(Math.Abs(x.b!.Value.y1), Math.Abs(x.b!.Value.y0))).Take(8).Select(x => new { handle = x.e.Handle, type = x.e.DxfName, x0 = x.b!.Value.x0, y0 = x.b!.Value.y0, x1 = x.b!.Value.x1, y1 = x.b!.Value.y1, layer = x.e.Layer, blk = x.e is Insert ii ? ii.Block : null }).ToList();
                    throw new ArgumentException("query entities|layers|extents|texts|selection");
                }
            default: throw new ArgumentException("unknown command " + cmd);
        }
    }
    static List<string> Split(string s)
    {
        var list = new List<string>(); var sb = new StringBuilder(); bool q = false;
        foreach (var c in s) { if (c == '"') { q = !q; continue; } if (char.IsWhiteSpace(c) && !q) { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); } } else sb.Append(c); }
        if (sb.Length > 0) list.Add(sb.ToString()); return list;
    }
    static (double x, double y) Pt(string s) { var p = s.Split(','); if (p.Length < 2) throw new ArgumentException("point expected as X,Y: " + s); return (D(p[0]), D(p[1])); }
    static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    static IEnumerable<string> SelectRect(Document d, (double x, double y) p0, (double x, double y) p1, bool crossing)
    {
        double x0 = Math.Min(p0.x, p1.x), x1 = Math.Max(p0.x, p1.x), y0 = Math.Min(p0.y, p1.y), y1 = Math.Max(p0.y, p1.y);
        foreach (var e in d.Entities.Where(e => !e.PaperSpace))
        {
            var b = Core.Geometry.Bounds.Of(e, d, 0); if (b == null) continue;
            bool inside = b.Value.x0 >= x0 && b.Value.x1 <= x1 && b.Value.y0 >= y0 && b.Value.y1 <= y1; bool touches = !(b.Value.x1 < x0 || b.Value.x0 > x1 || b.Value.y1 < y0 || b.Value.y0 > y1);
            if (inside || (crossing && touches)) yield return e.Handle;
        }
    }
}
