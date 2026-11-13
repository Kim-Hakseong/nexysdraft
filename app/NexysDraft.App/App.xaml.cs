using System.Windows;

namespace NexysDraft.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Diag.Trace($"app start args=[{string.Join(" ", e.Args)}] pid={Environment.ProcessId}");
        DispatcherUnhandledException += (_, ex) => { Diag.Trace("UNHANDLED " + ex.Exception); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Diag.Trace("UNHANDLED(domain) " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Diag.Trace("UNOBSERVED " + ex.Exception); ex.SetObserved(); };
        if (!SelfTest.Enabled && !Program.Fresh) Task.Run(FileAssoc.RegisterCurrentUser);   // no installer: a normal start lists this exe under "Open with" for .dwg / .dxf
        var win = new MainWindow();
        MainWindow = win;
        win.WhenReady.ContinueWith(_ => Diag.StartWatchdog(win.Dispatcher));   // stalls while WebView2 boots are not user-facing; count from the first page frame
        if (SelfTest.Enabled) win.WhenReady.ContinueWith(_ => win.Dispatcher.BeginInvoke(async () => await SelfTest.RunAsync(win)));
        else win.WhenReady.ContinueWith(_ => win.Dispatcher.BeginInvoke(() => { bool any = false; foreach (var a in e.Args) if (!a.StartsWith("--") && System.IO.File.Exists(a)) { win.Engine.Open(a); any = true; } if (!any && win.Engine.Doc == null) win.Engine.New(); }));   // nothing to open: start on an empty drawing, ready to draw
        win.Show();
    }
}
