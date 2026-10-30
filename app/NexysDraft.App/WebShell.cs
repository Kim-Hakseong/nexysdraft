using System.IO;
using Microsoft.Web.WebView2.Core;

namespace NexysDraft.App;

/// <summary>WebView2 environment and page settings (page served from wwwroot next to the exe under the virtual host app.nexysdraft).</summary>
public static class WebShell
{
    public static string WwwRoot => Path.Combine(AppContext.BaseDirectory, "wwwroot");
    public const string Origin = "https://app.nexysdraft/";
    public static string? ProbeRuntime() { try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch (Exception ex) { Diag.Trace("webview2 runtime probe: " + ex.Message); return null; } }
    public static string UserDataFolder(string kind) => Path.Combine(AppInfo.LocalDir, kind);
    public static Task<CoreWebView2Environment> CreateEnvironmentAsync(string? fixedRuntimeFolder, string userDataFolder)
    {
        var opts = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection --enable-unsafe-webgpu --ignore-gpu-blocklist" };
        return CoreWebView2Environment.CreateAsync(fixedRuntimeFolder, userDataFolder, opts);
    }
    public static void ConfigureCore(CoreWebView2 core)
    {
        core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreBrowserAcceleratorKeysEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false; core.Settings.IsPinchZoomEnabled = false;
        core.Settings.AreDevToolsEnabled = true; core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
        core.SetVirtualHostNameToFolderMapping("app.nexysdraft", WwwRoot, CoreWebView2HostResourceAccessKind.Allow);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.ProcessFailed += (_, e) => Diag.Trace($"webview process failed: {e.ProcessFailedKind} {e.Reason}");
    }
}
