using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace NexysDraft.App;

/// <summary>Trace log (work/hang/trace.log next to the repository, else %LOCALAPPDATA%) and a UI-stall watchdog (count + max stall for the stress gate).</summary>
public static class Diag
{
    public static string HangDir { get; } = FindHangDir();
    static readonly object _lock = new(); static readonly Stopwatch _clock = Stopwatch.StartNew(); static string? _traceFile;
    static string FindHangDir()
    {
        try
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent;
            string root = d != null ? Path.Combine(d.FullName, "work", "hang") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexysDraft", "hang");
            Directory.CreateDirectory(root); return root;
        }
        catch { return Path.GetTempPath(); }
    }
    public static void Trace(string msg)
    {
        try { lock (_lock) { _traceFile ??= Path.Combine(HangDir, "trace.log"); File.AppendAllText(_traceFile, $"{DateTime.Now:HH:mm:ss.fff} +{_clock.Elapsed.TotalMilliseconds,9:F1} T{Environment.CurrentManagedThreadId,-3} {msg}{Environment.NewLine}"); } } catch { }
    }
    public static IDisposable Span(string name) => new Scope(name);
    sealed class Scope : IDisposable { readonly string _n; readonly Stopwatch _sw = Stopwatch.StartNew(); public Scope(string n) { _n = n; Trace(_n + " start"); } public void Dispose() => Trace($"{_n} end ({_sw.Elapsed.TotalMilliseconds:F1} ms)"); }

    static Thread? _wd;
    public static int StallThresholdMs { get; set; } = 300;
    public static int StallCount { get; private set; }
    public static double MaxStallMs { get; private set; }
    public static void ResetStats() { StallCount = 0; MaxStallMs = 0; }
    public static void StartWatchdog(Dispatcher ui)
    {
        if (_wd != null) return;
        long lastPing = _clock.ElapsedMilliseconds; bool pending = false;
        _wd = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(50); if (ui.HasShutdownStarted) return;
                long now = _clock.ElapsedMilliseconds;
                if (!pending) { pending = true; lastPing = now; ui.BeginInvoke(DispatcherPriority.Normal, () => { pending = false; }); }
                else if (now - lastPing > StallThresholdMs)
                {
                    StallCount++; Trace($"WATCHDOG ui stalled >{StallThresholdMs} ms");
                    while (pending && !ui.HasShutdownStarted) Thread.Sleep(50);
                    double total = _clock.ElapsedMilliseconds - lastPing; MaxStallMs = Math.Max(MaxStallMs, total); Trace($"WATCHDOG ui resumed after {total:F0} ms");
                }
            }
        }) { IsBackground = true, Name = "ui-watchdog" };
        _wd.Start();
    }
}

public static class AppInfo
{
    public static string Version { get; } = ((System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly).GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    public static string VersionText => Version.Replace("-draft", " draft");
    public const string Name = "NexysDraft";
    public static string LocalDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
}
