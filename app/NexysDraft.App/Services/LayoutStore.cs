using System.IO;
using System.Text.Json;
using System.Windows;

namespace NexysDraft.App.Services;

/// <summary>%LOCALAPPDATA%\NexysDraft\layout.json: window rect, theme, recent files. Automation runs (--selftest/--shot/--fresh) never read or write it.</summary>
public static class LayoutStore
{
    public sealed class Dto { public string Theme { get; set; } = "light"; public double W { get; set; } = 1440; public double H { get; set; } = 900; public double X { get; set; } = double.NaN; public double Y { get; set; } = double.NaN; public bool Maximized { get; set; } public List<string> Recent { get; set; } = new(); public string Version { get; set; } = ""; }
    static string FilePath => Path.Combine(AppInfo.LocalDir, "layout.json");
    static bool Skip => SelfTest.Enabled || Program.Fresh;

    public static void Load(MainWindow w)
    {
        if (Skip || !File.Exists(FilePath)) return;
        try
        {
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath)); if (d == null) return;
            w.Engine.Theme = d.Theme is "dark" ? "dark" : "light"; w.Engine.Recent.Clear(); w.Engine.Recent.AddRange(d.Recent.Where(File.Exists));
            if (!double.IsNaN(d.X) && !double.IsNaN(d.Y) && d.W >= 800 && d.H >= 500)
            {
                var va = SystemParameters.VirtualScreenWidth; var vh = SystemParameters.VirtualScreenHeight;
                if (d.X > -d.W + 100 && d.Y > -50 && d.X < va - 100 && d.Y < vh - 100) { w.Left = d.X; w.Top = d.Y; w.Width = d.W; w.Height = d.H; }
            }
            if (d.Maximized) w.WindowState = WindowState.Maximized;
            w.Engine.Touch();
        }
        catch (Exception ex) { Diag.Trace("layout load failed " + ex.Message); }
    }
    public static void Save(MainWindow w)
    {
        if (Skip) return;
        try
        {
            Directory.CreateDirectory(AppInfo.LocalDir);
            var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds;
            var d = new Dto { Theme = w.Engine.Theme, X = r.X, Y = r.Y, W = r.Width, H = r.Height, Maximized = w.WindowState == WindowState.Maximized, Recent = w.Engine.Recent.ToList(), Version = AppInfo.Version };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Diag.Trace("layout save failed " + ex.Message); }
    }
}
