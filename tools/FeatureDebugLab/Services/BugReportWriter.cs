using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatureDebugLab.Services;

/// <summary>
/// Writes structured lab reports under %LocalAppData%\AutoCorrect\debug-lab\
/// so Cursor/agents can read failures and open the hinted source files.
/// </summary>
public static class BugReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ReportDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect", "debug-lab");

    public static string LastJsonPath => Path.Combine(ReportDir, "last-report.json");
    public static string LastMarkdownPath => Path.Combine(ReportDir, "last-report.md");
    public static string LastFailuresPath => Path.Combine(ReportDir, "last-failures.txt");

    public static LabReport Write(
        IReadOnlyList<TestResult> results,
        string mode,
        string? notes = null)
    {
        Directory.CreateDirectory(ReportDir);
        var pass = results.Count(r => r.Status == TestStatus.Pass);
        var fail = results.Count(r => r.Status == TestStatus.Fail);
        var warn = results.Count(r => r.Status == TestStatus.Warn);
        var skip = results.Count(r => r.Status == TestStatus.Skip);
        var bugs = results
            .Where(r => r.Status is TestStatus.Fail or TestStatus.Warn)
            .Select(r => new LabBugItem
            {
                Suite = r.Suite,
                Name = r.Name,
                Status = r.StatusTag,
                Detail = r.Detail,
                Expected = r.Expected,
                Actual = r.Actual,
                FixHint = r.FixHint,
                Category = r.Category ?? r.Suite
            })
            .ToList();

        var report = new LabReport
        {
            GeneratedUtc = DateTime.UtcNow.ToString("o"),
            Mode = mode,
            Notes = notes,
            Pass = pass,
            Fail = fail,
            Warn = warn,
            Skip = skip,
            HasBugs = bugs.Count > 0,
            Bugs = bugs,
            All = results.Select(r => new LabBugItem
            {
                Suite = r.Suite,
                Name = r.Name,
                Status = r.StatusTag,
                Detail = r.Detail,
                Expected = r.Expected,
                Actual = r.Actual,
                FixHint = r.FixHint,
                Category = r.Category ?? r.Suite
            }).ToList()
        };

        File.WriteAllText(LastJsonPath, JsonSerializer.Serialize(report, JsonOpts));
        File.WriteAllText(LastMarkdownPath, ToMarkdown(report), Encoding.UTF8);
        File.WriteAllText(LastFailuresPath, ToFailuresText(bugs), Encoding.UTF8);
        return report;
    }

    public static void OpenReportFolder()
    {
        Directory.CreateDirectory(ReportDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = ReportDir,
            UseShellExecute = true
        });
    }

    public static string ToFailuresText(IReadOnlyList<LabBugItem> bugs)
    {
        if (bugs.Count == 0)
            return "No failures or warnings.\n";

        var sb = new StringBuilder();
        sb.AppendLine($"# AutoCorrect Debug Lab — {bugs.Count} issue(s)");
        sb.AppendLine("# Paste this into Cursor: \"Fix these Debug Lab failures:\"");
        sb.AppendLine();
        var i = 1;
        foreach (var b in bugs)
        {
            sb.AppendLine($"{i}. [{b.Status}] {b.Suite} / {b.Name}");
            if (!string.IsNullOrWhiteSpace(b.Detail))
                sb.AppendLine($"   Detail: {b.Detail}");
            if (!string.IsNullOrWhiteSpace(b.Expected))
                sb.AppendLine($"   Expected: {b.Expected}");
            if (!string.IsNullOrWhiteSpace(b.Actual))
                sb.AppendLine($"   Actual: {b.Actual}");
            if (!string.IsNullOrWhiteSpace(b.FixHint))
                sb.AppendLine($"   Fix: {b.FixHint}");
            sb.AppendLine();
            i++;
        }
        return sb.ToString();
    }

    private static string ToMarkdown(LabReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AutoCorrect Feature Debug Lab report");
        sb.AppendLine();
        sb.AppendLine($"- Generated (UTC): `{report.GeneratedUtc}`");
        sb.AppendLine($"- Mode: **{report.Mode}**");
        sb.AppendLine($"- Pass **{report.Pass}** · Fail **{report.Fail}** · Warn **{report.Warn}** · Skip **{report.Skip}**");
        if (!string.IsNullOrWhiteSpace(report.Notes))
            sb.AppendLine($"- Notes: {report.Notes}");
        sb.AppendLine();
        if (report.Bugs.Count == 0)
        {
            sb.AppendLine("## No bugs detected");
            sb.AppendLine();
            sb.AppendLine("All exercised assertions passed (or were skipped safely).");
            return sb.ToString();
        }

        sb.AppendLine("## Actionable issues");
        sb.AppendLine();
        sb.AppendLine("Ask Cursor: *Fix these Debug Lab failures using the Fix hints.*");
        sb.AppendLine();
        var n = 1;
        foreach (var b in report.Bugs)
        {
            sb.AppendLine($"### {n}. [{b.Status}] {b.Suite} — {b.Name}");
            if (!string.IsNullOrWhiteSpace(b.Detail))
                sb.AppendLine($"- Detail: {b.Detail}");
            if (!string.IsNullOrWhiteSpace(b.Expected))
                sb.AppendLine($"- Expected: `{b.Expected}`");
            if (!string.IsNullOrWhiteSpace(b.Actual))
                sb.AppendLine($"- Actual: `{b.Actual}`");
            if (!string.IsNullOrWhiteSpace(b.FixHint))
                sb.AppendLine($"- **Fix hint:** {b.FixHint}");
            sb.AppendLine();
            n++;
        }
        return sb.ToString();
    }
}

public sealed class LabReport
{
    public string GeneratedUtc { get; set; } = "";
    public string Mode { get; set; } = "";
    public string? Notes { get; set; }
    public int Pass { get; set; }
    public int Fail { get; set; }
    public int Warn { get; set; }
    public int Skip { get; set; }
    public bool HasBugs { get; set; }
    public List<LabBugItem> Bugs { get; set; } = [];
    public List<LabBugItem> All { get; set; } = [];
}

public sealed class LabBugItem
{
    public string Suite { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Detail { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? FixHint { get; set; }
    public string? Category { get; set; }
}
