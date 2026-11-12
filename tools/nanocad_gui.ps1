# dev PC check (not part of the product): open a drawing in an installed nanoCAD window, list the windows it shows, close it.
# usage: nanocad_gui.ps1 file [seconds] [png]   — only the process started here is closed
param([string]$File, [int]$Seconds = 30, [string]$Png = "")
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class NW {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  public struct RECT { public int L, T, R, B; }
  public static IntPtr Main, Dlg;
  public static List<string> Titles(uint pid) {
    var r = new List<string>(); long best = 0;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p != pid || !IsWindowVisible(h)) return true;
      var s = new StringBuilder(512); GetWindowText(h, s, 512); var c = new StringBuilder(128); GetClassName(h, c, 128);
      RECT q; GetWindowRect(h, out q); long area = (long)(q.R - q.L) * (q.B - q.T); if (area > best) { best = area; Main = h; }
      var kids = new List<string>();
      if (c.ToString() == "#32770") Dlg = h; if (c.ToString() == "#32770") EnumChildWindows(h, (k, l2) => { var t = new StringBuilder(512); GetWindowText(k, t, 512); if (t.Length > 0 && kids.Count < 12) kids.Add(t.ToString()); return true; }, IntPtr.Zero);
      r.Add("[" + c + "] '" + s + "'" + (kids.Count > 0 ? " :: " + string.Join(" | ", kids) : "")); return true; }, IntPtr.Zero);
    return r; }
}
"@ -ReferencedAssemblies System.Drawing
Add-Type -AssemblyName System.Drawing
$exe = "C:\Program Files (x86)\Nanosoft AS\nanoCAD en 5.0\nCad.exe"
$p = Start-Process $exe -ArgumentList ('"' + $File + '"') -PassThru
$seen = New-Object System.Collections.Generic.HashSet[string]
$sw = [Diagnostics.Stopwatch]::StartNew()
function Shot { $r = New-Object NW+RECT; [void][NW]::GetWindowRect([NW]::Main, [ref]$r); $w = $r.R - $r.L; $h = $r.B - $r.T; if ($w -le 0 -or $h -le 0) { return $null }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc(); [void][NW]::PrintWindow([NW]::Main, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose(); return $bmp }
# the document is open once the client area is no longer the grey MDI background; then give the first regen a few seconds
$openAt = -1.0
while ($sw.Elapsed.TotalSeconds -lt $Seconds -and -not $p.HasExited) {
  foreach ($t in [NW]::Titles([uint32]$p.Id)) { if ($seen.Add($t)) { "  {0,5:N1}s {1}" -f $sw.Elapsed.TotalSeconds, $t } }
  if ($openAt -lt 0 -and $sw.Elapsed.TotalSeconds -gt 3 -and [NW]::Main -ne [IntPtr]::Zero) {
    $b = Shot; if ($b) { $c = $b.GetPixel([int]($b.Width / 2), 140); $d = $b.GetPixel(60, [int]($b.Height / 2)); $b.Dispose(); if (-not ($c.R -eq $d.R -and $c.R -gt 40 -and $c.R -eq $c.G)) { $openAt = $sw.Elapsed.TotalSeconds; "  {0,5:N1}s document shown" -f $openAt } }
  }
  if ($openAt -ge 0 -and $sw.Elapsed.TotalSeconds -gt $openAt + 5) { break }
  if ([NW]::Dlg -ne [IntPtr]::Zero -and $openAt -lt 0) { Start-Sleep 2; "  a dialog appeared before any document"; break }
  Start-Sleep -Milliseconds 700
}
if ($openAt -lt 0 -and -not $p.HasExited) { "  no document after $Seconds s" }
if ($Png -and [NW]::Dlg -ne [IntPtr]::Zero -and -not $p.HasExited) { $r = New-Object NW+RECT; [void][NW]::GetWindowRect([NW]::Dlg, [ref]$r); $w = $r.R - $r.L; $h = $r.B - $r.T; if ($w -gt 0 -and $h -gt 0) { $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc(); [void][NW]::PrintWindow([NW]::Dlg, $dc, 2); $g.ReleaseHdc($dc); $bmp.Save(($Png -replace '\.png$', '_dialog.png')); "  dialog captured ${w}x$h" } }
if ([NW]::Dlg -ne [IntPtr]::Zero -and $openAt -lt 0) { $Seconds = 0 }
if ($p.HasExited) { "process exited by itself, code $($p.ExitCode)" }
else {
  if ($Png -and [NW]::Main -ne [IntPtr]::Zero) {
    $r = New-Object NW+RECT; [void][NW]::GetWindowRect([NW]::Main, [ref]$r); $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -gt 0 -and $h -gt 0) { $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc(); [void][NW]::PrintWindow([NW]::Main, $dc, 2); $g.ReleaseHdc($dc); $bmp.Save($Png); "  capture $Png ${w}x$h" }
  }
  Stop-Process -Id $p.Id -Force; "closed"
}
