using System.IO;
using System.Text;

namespace NexysDraft.App;

/// <summary>
/// <c>NexysDraft.exe --install-skill [claude|gemini|agy|all] [--uninstall]</c>: copies the bundled agent skills (skills\*\SKILL.md, the open
/// "Agent Skills" format) into the per-user skill folder of each coding agent, with the full path of this program written in, so a
/// plain-language request ("draw a 200 × 120 plate with four holes and save it as DWG") reaches the headless CLI (<c>--script</c>).
/// Claude Code: %USERPROFILE%\.claude\skills · Gemini CLI: %USERPROFILE%\.gemini\skills · Antigravity CLI (agy): %USERPROFILE%\.gemini\config\skills.
/// Nothing else is written; exit code 0 = at least one target installed.
/// </summary>
public static class SkillInstaller
{
    public const string Placeholder = "<!--NEXYSDRAFT_EXE-->";
    static readonly (string key, string label, string[] rel)[] Targets =
    {
        ("claude", "Claude Code", new[] { ".claude", "skills" }),
        ("gemini", "Gemini CLI", new[] { ".gemini", "skills" }),
        ("agy", "Antigravity CLI", new[] { ".gemini", "config", "skills" }),
    };

    /// <summary>The folder that holds nexysdraft\SKILL.md: next to the program (release zip) or up the tree (development build).</summary>
    public static string? FindSkills()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string s = Path.Combine(d.FullName, "skills"); if (File.Exists(Path.Combine(s, "nexysdraft", "SKILL.md"))) return s;
        }
        return null;
    }
    public static string ExeLine(string exe) => $"`{exe}` — run it as `& \"{exe}\" --script job.txt --out outdir --json | Out-String` (PowerShell: the pipe makes it wait) or `\"{exe}\" --script job.txt --out outdir --json` (cmd / bash).";

    /// <summary>Command line: prints to the console and, with <c>--log FILE</c>, writes the same lines to a file (a batch file cannot read the output of a window program through a pipe).</summary>
    public static int Run(string[] args)
    {
        var w = new StringWriter(); int rc = Run(args, w); string text = w.ToString();
        try { Console.Out.Write(text); } catch { }
        string? log = Arg(args, "--log"); if (log != null) try { File.WriteAllText(log, text, new UTF8Encoding(false)); } catch { }
        return rc;
    }
    /// <summary>The same from inside the app (the help dialog button): returns what was done as text lines.</summary>
    public static (bool ok, string text) RunToText(params string[] args) { var w = new StringWriter(); int rc = Run(new[] { "--install-skill" }.Concat(args).ToArray(), w); return (rc == 0, w.ToString().Trim()); }
    public static int Run(string[] args, TextWriter o)
    {
        bool un = args.Contains("--uninstall");
        int i = Array.IndexOf(args, "--install-skill"); string which = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1].ToLowerInvariant() : "all";
        string home = Arg(args, "--home") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NexysDraft.exe");
        string? src = FindSkills();
        if (src == null && !un) { o.WriteLine("skills folder not found next to " + exe + " (expected skills\\nexysdraft\\SKILL.md)"); return 2; }
        var chosen = Targets.Where(t => which == "all" || which == t.key).ToList();
        if (chosen.Count == 0) { o.WriteLine("usage: NexysDraft.exe --install-skill [claude|gemini|agy|all] [--uninstall]"); return 2; }
        int done = 0;
        foreach (var (key, label, rel) in chosen)
        {
            string root = Path.Combine(new[] { home }.Concat(rel).ToArray());
            try
            {
                if (un)
                {
                    foreach (var name in new[] { "nexysdraft", "cad-headless" }) { string dst = Path.Combine(root, name); if (File.Exists(Path.Combine(dst, "SKILL.md"))) { Directory.Delete(dst, true); o.WriteLine($"{label}: removed {dst}"); done++; } }
                    continue;
                }
                foreach (var dir in Directory.GetDirectories(src!))
                {
                    string file = Path.Combine(dir, "SKILL.md"); if (!File.Exists(file)) continue;
                    string dst = Path.Combine(root, Path.GetFileName(dir)); Directory.CreateDirectory(dst);
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    text = string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.Contains(Placeholder) ? ExeLine(exe) : l));   // the marked line becomes the real path
                    File.WriteAllText(Path.Combine(dst, "SKILL.md"), text, new UTF8Encoding(false));
                }
                o.WriteLine($"{label}: installed to {root}"); done++;
            }
            catch (Exception ex) { o.WriteLine($"{label}: failed — {ex.Message}"); }
        }
        if (!un && done > 0) o.WriteLine("Program: " + exe + "\nStart a new agent session and ask in plain language, e.g. \"draw a 200 x 120 plate with four 8 mm holes and save it as DWG and PDF\" (any language).");
        return done > 0 ? 0 : 1;
    }
    static string? Arg(string[] a, string name) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
}
