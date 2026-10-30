using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using NexysDraft.App.Services;

namespace NexysDraft.App;

/// <summary>One WPF window hosting the WebView2 page. Dialogs and the window stay native; everything else is the page talking to <see cref="Engine"/> through <see cref="Bridge"/>.</summary>
public partial class MainWindow : Window
{
    public Engine Engine { get; } = new();
    public Bridge? Bridge { get; private set; }
    readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WhenReady => _ready.Task;
    public string RuntimeVersion { get; private set; } = "";

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.Name} {AppInfo.VersionText}";
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/brand/AppIcon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); } catch { }
        Loaded += async (_, _) => await InitAsync();
        Closing += (_, e) =>
        {
            if (Engine.Doc?.Dirty == true && !SelfTest.Enabled)
            {
                var r = MessageBox.Show(this, "저장하지 않은 변경이 있습니다. 저장할까요?", AppInfo.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (r == MessageBoxResult.Yes) { e.Cancel = true; _ = SaveWithDialog(false).ContinueWith(t => { if (t.Result) Dispatcher.BeginInvoke(Close); }); return; }
            }
            LayoutStore.Save(this);
        };
        Engine.TitleChanged += () => Dispatcher.BeginInvoke(() => Title = $"{(Engine.Doc != null ? Path.GetFileName(Engine.Doc.Path) + (Engine.Doc.Dirty ? " *" : "") + " — " : "")}{AppInfo.Name} {AppInfo.VersionText}");
    }

    async Task InitAsync()
    {
        try
        {
            string? rt = WebShell.ProbeRuntime(); string? fixedDir = null;
            if (rt == null) { string f = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime"); if (Directory.Exists(f)) fixedDir = f; else throw new InvalidOperationException("Microsoft Edge WebView2 Runtime is not installed"); }
            var env = await WebShell.CreateEnvironmentAsync(fixedDir, WebShell.UserDataFolder(SelfTest.Enabled ? "WebView2-auto" : Program.Fresh ? "WebView2-fresh" : "WebView2"));
            RuntimeVersion = env.BrowserVersionString;
            Web.DefaultBackgroundColor = System.Drawing.ColorTranslator.FromHtml("#ECEAE5");
            await Web.EnsureCoreWebView2Async(env);
            var core = Web.CoreWebView2;
            if (!Directory.Exists(WebShell.WwwRoot)) { Boot.Text = "wwwroot missing — run `npm run build` in app/web and rebuild"; Diag.Trace("wwwroot missing: " + WebShell.WwwRoot); }
            WebShell.ConfigureCore(core);
            Bridge = new Bridge(core, Engine, Dispatcher, HostCommand);
            Bridge.Ready += () => Dispatcher.BeginInvoke(() => { LayoutStore.Load(this); Bridge.PushState(); _ready.TrySetResult(); });
            core.Navigate(WebShell.Origin + "index.html");
            Diag.Trace("webview2 " + env.BrowserVersionString + " navigating");
        }
        catch (Exception ex)
        {
            Diag.Trace("webview init failed " + ex);
            Boot.Text = "WebView2 runtime not available: " + ex.Message + "\nInstall the Evergreen WebView2 Runtime (https://developer.microsoft.com/microsoft-edge/webview2/) or place a Fixed Version runtime in the WebView2Runtime folder next to NexysDraft.exe, then start again.";
            _ready.TrySetException(ex);
        }
    }

    public const string OpenFilter = "Drawings|*.dwg;*.dxf|DWG|*.dwg|DXF|*.dxf|All|*.*";

    /// <summary>Host-only commands (native dialogs, clipboard, URLs).</summary>
    public async Task<object?> HostCommand(string cmd, JsonElement args)
    {
        switch (cmd)
        {
            case "openDialog":
                {
                    var dlg = new OpenFileDialog { Filter = OpenFilter };
                    if (dlg.ShowDialog(this) == true) await Engine.OpenAsync(dlg.FileName);
                    return null;
                }
            case "saveDialog": if (args.TryGetProperty("fontCompat", out var fc) && (fc.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)) Engine.FontCompat = fc.GetBoolean(); if (Bridge.S(args, "dxfVer") is { Length: > 0 } dv) Engine.DxfVersion = dv; return await SaveWithDialog(Bridge.B(args, "dwg"), Bridge.B(args, "noImage", false));
            case "save": { if (Engine.Doc == null) return false; if (Engine.Doc.Path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) || Engine.Doc.Path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) return await Engine.SaveAsync(Engine.Doc.Path); return await SaveWithDialog(false); }
            case "exportPdfDialog":
                {
                    if (Engine.Doc == null) return null;
                    var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Path.GetFileNameWithoutExtension(Engine.Doc.Path) + ".pdf" };
                    if (dlg.ShowDialog(this) != true) return null;
                    return await Engine.ExportPdfAsync(dlg.FileName);
                }
            case "capture": { string path = args.GetProperty("path").GetString()!; await CaptureAsync(path); return path; }
            case "openUrl": { var u = Bridge.S(args, "url"); if (u.StartsWith("http")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u) { UseShellExecute = true }); return null; }
            case "openFolder": { var p = Bridge.S(args, "path"); if (Directory.Exists(p)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", p) { UseShellExecute = true }); return null; }
            case "quit": Close(); return null;
        }
        throw new NotSupportedException("unknown command " + cmd);
    }

    /// <summary>Native save dialog, then a background save (no UI wait: DWG saves run minutes for large drawings).</summary>
    async Task<bool> SaveWithDialog(bool dwg, bool noImage = false)
    {
        if (Engine.Doc == null) return false;
        var dlg = new SaveFileDialog { Filter = dwg ? "DWG (R2000, LibreDWG)|*.dwg|DXF|*.dxf" : "DXF|*.dxf|DWG (R2000, LibreDWG)|*.dwg", FileName = Path.GetFileNameWithoutExtension(Engine.Doc.Path == "" ? "untitled" : Engine.Doc.Path) + (dwg ? ".dwg" : ".dxf") };
        if (dlg.ShowDialog(this) != true) return false;
        return await Engine.SaveAsync(dlg.FileName, null, noImage && dlg.FileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase));
    }

    public async Task CaptureAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        await Web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
    }
}
