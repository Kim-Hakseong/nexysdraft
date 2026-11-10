# open a drawing in the DWG TrueView window, report the windows it shows, close it. usage: gui.ps1 file [seconds]
param([string]$File, [int]$Seconds = 60)
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class W {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  // describe the children of the window whose title contains t; click the first child whose text contains click (BM_CLICK)
  public static string Kids(uint pid, string t, string click) {
    var sb = new StringBuilder();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p != pid || !IsWindowVisible(h)) return true; var s = new StringBuilder(512); GetWindowText(h, s, 512); if (!s.ToString().Contains(t)) return true;
      var c0 = new StringBuilder(128); GetClassName(h, c0, 128); sb.Append("[" + c0 + "] ");
      EnumChildWindows(h, (c, l2) => { var k = new StringBuilder(512); GetWindowText(c, k, 512); var cn = new StringBuilder(128); GetClassName(c, cn, 128); sb.Append(cn + ":'" + k + "' ");
        if (click != null && k.ToString().Contains(click)) { PostMessage(c, 0x00F5, IntPtr.Zero, IntPtr.Zero); sb.Append("<CLICKED> "); } return true; }, IntPtr.Zero);
      return true; }, IntPtr.Zero);
    return sb.ToString(); }
  public static List<string> Titles(uint pid) {
    var r = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { var s = new StringBuilder(512); GetWindowText(h, s, 512); if (s.Length > 0) { string t = s.ToString(); var kids = new List<string>();
      EnumChildWindows(h, (c, l2) => { var k = new StringBuilder(512); GetWindowText(c, k, 512); if (k.Length > 0 && kids.Count < 6) kids.Add(k.ToString()); return true; }, IntPtr.Zero);
      r.Add(t + (t.Contains("Error") || t.Contains("TrueView Error") ? " :: " + string.Join(" | ", kids) : "")); } } return true; }, IntPtr.Zero);
    return r; }
}
"@
$exe = "C:\Program Files\Autodesk\DWG TrueView 2027 - English\dwgviewr.exe"
$p = Start-Process -FilePath $exe -ArgumentList ('"' + (Resolve-Path $File).Path + '"') -PassThru
$seen = @{}; $foreign = $false; $shx = $false; $verdict = "NO ERROR WINDOW"; $t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
  Start-Sleep -Milliseconds 1500
  if ($p.HasExited) { $verdict = "EXITED code " + $p.ExitCode; break }
  foreach ($t in [W]::Titles([uint32]$p.Id)) { if (-not $seen.ContainsKey($t)) { $seen[$t] = 1; Write-Output ("  window: " + $t) }
    if ($t -match "Error") { $verdict = "ERROR WINDOW" }
    if ($t -match "Missing SHX" -and -not $shx) { $shx = $true; [void][W]::Kids([uint32]$p.Id, "Missing SHX", "Ignore the missing") }
    if ($t -match "Foreign DWG" -and -not $foreign) { $foreign = $true; Write-Output ("  foreign: " + [W]::Kids([uint32]$p.Id, "Foreign DWG", "Continue")) } }
  if ($verdict -eq "ERROR WINDOW") { break }
}
if (-not $p.HasExited) { Write-Output ("  at end: " + (([W]::Titles([uint32]$p.Id)) -join " ; ")) }
Write-Output ("verdict: " + $verdict + "  (" + [int]((Get-Date) - $t0).TotalSeconds + " s)")
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
