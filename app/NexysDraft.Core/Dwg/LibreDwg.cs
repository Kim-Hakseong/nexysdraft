using System.Diagnostics;
using System.Text;

namespace NexysDraft.Core.Dwg;

/// <summary>
/// GNU LibreDWG 0.14 command-line tools, run as separate processes (GPLv3 — nothing is linked or copied):
/// dwg2dxf (reads R13–R2018 DWG, writes DXF of the same or a chosen version) and dxf2dwg (writes R2000, also r12/r14/r2004).
/// The tools take their arguments in the ANSI code page, so files are staged under ASCII names first (the dwg2spdf approach).
/// </summary>
public static class LibreDwg
{
    public sealed record Result(bool Ok, int ExitCode, string Stderr, double Ms, string Output);
    /// <summary>"0.14.8679 (33bfac15)" for a source build (COMMIT.txt next to the tools: commit, then git describe), else the release "0.14".</summary>
    public static string Version
    {
        get
        {
            try
            {
                var dir = Dir; var f = dir == null ? null : Path.Combine(dir, "COMMIT.txt");
                if (f != null && File.Exists(f)) { var l = File.ReadAllLines(f).Where(x => x.Trim().Length > 0).ToArray(); if (l.Length >= 2) return $"{l[1].Trim()} ({l[0].Trim()[..8]})"; }
            }
            catch (IOException) { }
            return "0.14";
        }
    }

    /// <summary>Folder with dwg2dxf.exe: next to the exe (tools/libredwg), or the repository's app/tools/libredwg, or NEXYSDRAFT_LIBREDWG.</summary>
    public static string? Dir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("NEXYSDRAFT_LIBREDWG");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "dwg2dxf.exe"))) return env;
            foreach (var c in new[] { Path.Combine(AppContext.BaseDirectory, "tools", "libredwg"), Path.Combine(AppContext.BaseDirectory, "libredwg") })
                if (File.Exists(Path.Combine(c, "dwg2dxf.exe"))) return c;
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                string p = Path.Combine(d.FullName, "app", "tools", "libredwg"); if (File.Exists(Path.Combine(p, "dwg2dxf.exe"))) return p;
                p = Path.Combine(d.FullName, "tools", "libredwg"); if (File.Exists(Path.Combine(p, "dwg2dxf.exe"))) return p;
                d = d.Parent;
            }
            return null;
        }
    }
    public static bool Available => Dir != null;

    public static Result DwgToDxf(string dwg, string dxf, string? asVersion = null, int timeoutMs = 120000) => Run("dwg2dxf.exe", dwg, ".dwg", dxf, ".dxf", asVersion, timeoutMs);
    public static Result DxfToDwg(string dxf, string dwg, string asVersion = "r2000", int timeoutMs = 300000, CancellationToken ct = default) => Run("dxf2dwg.exe", dxf, ".dxf", dwg, ".dwg", asVersion, timeoutMs, ct);

    static Result Run(string tool, string input, string inExt, string output, string outExt, string? asVersion, int timeoutMs, CancellationToken ct = default)
    {
        string? dir = Dir; if (dir == null) return new Result(false, -1, "LibreDWG tools not found (app/tools/libredwg)", 0, output);
        string stage = Path.Combine(Path.GetTempPath(), "NexysDraft", "libredwg", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stage);
        string sin = Path.Combine(stage, "in" + inExt), sout = Path.Combine(stage, "out" + outExt);
        var sw = Stopwatch.StartNew();
        try
        {
            File.Copy(input, sin, true);
            var psi = new ProcessStartInfo(Path.Combine(dir, tool)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = stage, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
            if (tool.StartsWith("dwg2dxf")) psi.ArgumentList.Add("-y");
            if (!string.IsNullOrEmpty(asVersion)) { psi.ArgumentList.Add("--as"); psi.ArgumentList.Add(asVersion); }
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(sout); psi.ArgumentList.Add(sin);
            using var p = Process.Start(psi)!;
            var err = new StringBuilder(); var outp = new StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data != null) outp.AppendLine(e.Data); }; p.ErrorDataReceived += (_, e) => { if (e.Data != null && err.Length < 200000) err.AppendLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return new Result(false, -2, "timeout after " + timeoutMs + " ms", sw.Elapsed.TotalMilliseconds, output); }
            p.WaitForExit();
            bool ok = File.Exists(sout) && new FileInfo(sout).Length > 0;
            if (ok) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.Copy(sout, output, true); }
            return new Result(ok, p.ExitCode, err.ToString() + outp.ToString(), sw.Elapsed.TotalMilliseconds, output);
        }
        catch (Exception ex) { return new Result(false, -3, ex.Message, sw.Elapsed.TotalMilliseconds, output); }
        finally { try { Directory.Delete(stage, true); } catch { } }
    }

    /// <summary>Distinct warning/error lines (first 20) for reports.</summary>
    public static List<string> Summarize(string stderr) => stderr.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().Take(20).ToList();
}
