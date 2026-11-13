using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NexysDraft.App;

/// <summary>
/// "Open with" registration for .dwg / .dxf, per user (HKCU\Software\Classes — no administrator rights, nothing outside the user's hive):
/// ProgIDs NexysDraft.dwg / NexysDraft.dxf, the Applications\NexysDraft.exe entry, OpenWithProgids on both extensions, and the
/// Capabilities + RegisteredApplications pair that lists the program under Settings → Default apps.
/// Windows 10 / 11 do not let a program make itself the default (the UserChoice key is protected): <see cref="OpenDefaultAppsSettings"/> opens
/// the Settings page of this program, where the user confirms with one click.
/// The program has no installer, so a normal start registers the running exe (and re-registers after the folder was moved).
/// </summary>
public static class FileAssoc
{
    public static readonly string[] Exts = { ".dwg", ".dxf" };
    const string AppKey = "NexysDraft", ExeKey = "NexysDraft.exe";
    static string ProgId(string ext) => "NexysDraft" + ext;   // NexysDraft.dwg, NexysDraft.dxf
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NexysDraft.exe");
    static string Command(string exe) => $"\"{exe}\" \"%1\"";

    /// <summary>Write the registration under <paramref name="root"/> (HKCU in the product; a scratch key in the self-test). Returns true when something changed.</summary>
    public static bool Register(RegistryKey root, string exe)
    {
        bool changed = false;
        void Set(string key, string? name, string value) { using var k = root.CreateSubKey(key); if (k.GetValue(name) as string != value) { k.SetValue(name, value); changed = true; } }
        string cmd = Command(exe);
        foreach (var ext in Exts)
        {
            string id = ProgId(ext), type = ext == ".dwg" ? "DWG drawing" : "DXF drawing";
            Set($@"Software\Classes\{id}", null, type);
            Set($@"Software\Classes\{id}", "FriendlyTypeName", type);
            Set($@"Software\Classes\{id}\DefaultIcon", null, $"\"{exe}\",0");
            Set($@"Software\Classes\{id}\shell\open\command", null, cmd);
            Set($@"Software\Classes\{ext}\OpenWithProgids", id, "");
            Set($@"Software\Classes\Applications\{ExeKey}\SupportedTypes", ext, "");
            Set($@"Software\{AppKey}\Capabilities\FileAssociations", ext, id);
        }
        Set($@"Software\Classes\Applications\{ExeKey}", "FriendlyAppName", AppInfo.Name);
        Set($@"Software\Classes\Applications\{ExeKey}\shell\open\command", null, cmd);
        Set($@"Software\{AppKey}\Capabilities", "ApplicationName", AppInfo.Name);
        Set($@"Software\{AppKey}\Capabilities", "ApplicationDescription", "2D CAD: open, edit and save DWG / DXF drawings");
        Set(@"Software\RegisteredApplications", AppKey, $@"Software\{AppKey}\Capabilities");
        return changed;
    }

    public static void Unregister(RegistryKey root)
    {
        foreach (var ext in Exts)
        {
            root.DeleteSubKeyTree($@"Software\Classes\{ProgId(ext)}", false);
            using var k = root.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true); k?.DeleteValue(ProgId(ext), false);
        }
        root.DeleteSubKeyTree($@"Software\Classes\Applications\{ExeKey}", false);
        root.DeleteSubKeyTree($@"Software\{AppKey}\Capabilities", false);
        using var ra = root.OpenSubKey(@"Software\RegisteredApplications", true); ra?.DeleteValue(AppKey, false);
    }

    /// <summary>Register the running exe for the current user and tell the shell. Never throws.</summary>
    public static bool RegisterCurrentUser()
    {
        try { bool ch = Register(Registry.CurrentUser, ExePath); if (ch) { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); Diag.Trace("file association registered: " + ExePath); } return true; }
        catch (Exception ex) { Diag.Trace("file association failed " + ex.Message); return false; }
    }
    public static void UnregisterCurrentUser()
    {
        try { Unregister(Registry.CurrentUser); using (var k = Registry.CurrentUser.OpenSubKey($@"Software\{AppKey}", true)) k?.DeleteValue("AskDefault", false); SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch (Exception ex) { Diag.Trace("file association removal failed " + ex.Message); }
    }

    /// <summary>The program Windows starts for a double click on a file of this extension ("" when none).</summary>
    public static string DefaultExe(string ext)
    {
        try { uint n = 1024; var sb = new StringBuilder((int)n); return AssocQueryString(0, 2 /* ASSOCSTR_EXECUTABLE */, ext, null, sb, ref n) == 0 ? sb.ToString() : ""; } catch { return ""; }
    }
    public static bool IsDefault(string ext) => string.Equals(Path.GetFullPath(DefaultExe(ext) is { Length: > 0 } p ? p : "x:\\none"), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>The user chose "do not ask again".</summary>
    public static bool NeverAsk
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey($@"Software\{AppKey}"); return k?.GetValue("AskDefault") is int i && i == 0; } catch { return false; } }
        set { try { using var k = Registry.CurrentUser.CreateSubKey($@"Software\{AppKey}"); k.SetValue("AskDefault", value ? 0 : 1, RegistryValueKind.DWord); } catch { } }
    }

    /// <summary>Settings → Default apps → NexysDraft (Windows 11), or the Default apps page (Windows 10).</summary>
    public static void OpenDefaultAppsSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + AppKey) { UseShellExecute = true }); }
        catch (Exception ex) { Diag.Trace("default apps page failed " + ex.Message); try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); } catch { } }
    }

    /// <summary>Command line: --register / --unregister (no window).</summary>
    public static int Run(string[] args)
    {
        if (args.Contains("--unregister")) { UnregisterCurrentUser(); Console.WriteLine("NexysDraft: file associations removed (.dwg, .dxf)."); return 0; }
        bool ok = RegisterCurrentUser();
        Console.WriteLine(ok ? "NexysDraft: listed under \"Open with\" for .dwg and .dxf → " + ExePath : "NexysDraft: registration failed.");
        foreach (var ext in Exts) Console.WriteLine($"  {ext}: default program = {(DefaultExe(ext) is { Length: > 0 } d ? d : "(none)")}");
        return ok ? 0 : 1;
    }

    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder buf, ref uint len);
}
