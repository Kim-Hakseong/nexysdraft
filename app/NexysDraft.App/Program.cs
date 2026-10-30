using System.Runtime.InteropServices;

namespace NexysDraft.App;

public static class Program
{
    /// <summary>--fresh: behave like a first start (ignore and do not write layout.json, separate WebView2 profile).</summary>
    public static bool Fresh { get; private set; }
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--script"))
        {
            // headless job runner (F4): no window, results in result.json / stdout
            try { AttachConsole(-1); } catch { }
            return ScriptRunner.Run(args);
        }
        SelfTest.Args = args;
        Fresh = args.Contains("--fresh");
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
