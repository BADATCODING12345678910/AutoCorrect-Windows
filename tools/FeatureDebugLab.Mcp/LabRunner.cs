using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FeatureDebugLab.Mcp;

internal static class LabRunner
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunOfflineAsync(
        CancellationToken ct)
    {
        Directory.CreateDirectory(LabPaths.ReportDir);

        var project = LabPaths.LabProject;
        if (!File.Exists(project))
            throw new FileNotFoundException(
                "FeatureDebugLab.csproj not found. Set AC_DEBUG_LAB_TOOLS to AutoCorrectNative/tools.",
                project);

        var psi = new ProcessStartInfo
        {
            FileName = ResolveDotnet(),
            Arguments = $"run --project \"{project}\" -c Debug -- --offline",
            WorkingDirectory = Path.GetDirectoryName(project)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Prefer machine-wide shared runtimes (Scoop DOTNET_ROOT often lacks them).
        psi.Environment["DOTNET_ROOT"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "1";

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FeatureDebugLab.");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    public static string ReadFailures() =>
        File.Exists(LabPaths.LastFailures)
            ? File.ReadAllText(LabPaths.LastFailures)
            : "No last-failures.txt yet. Call debuglab_run_offline first.";

    public static string ReadMarkdown() =>
        File.Exists(LabPaths.LastMarkdown)
            ? File.ReadAllText(LabPaths.LastMarkdown)
            : "No last-report.md yet. Call debuglab_run_offline first.";

    public static string ReadJson() =>
        File.Exists(LabPaths.LastJson)
            ? File.ReadAllText(LabPaths.LastJson)
            : "{}";

    public static string SummarizeStatus()
    {
        if (!File.Exists(LabPaths.LastJson))
            return "No report yet. Call debuglab_run_offline.";

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LabPaths.LastJson));
            var root = doc.RootElement;
            var pass = root.TryGetProperty("Pass", out var p) ? p.GetInt32() : 0;
            var fail = root.TryGetProperty("Fail", out var f) ? f.GetInt32() : 0;
            var warn = root.TryGetProperty("Warn", out var w) ? w.GetInt32() : 0;
            var skip = root.TryGetProperty("Skip", out var s) ? s.GetInt32() : 0;
            var mode = root.TryGetProperty("Mode", out var m) ? m.GetString() : "?";
            var when = root.TryGetProperty("GeneratedUtc", out var g) ? g.GetString() : "?";
            var hasBugs = root.TryGetProperty("HasBugs", out var hb) && hb.GetBoolean();
            var sb = new StringBuilder();
            sb.AppendLine($"Mode: {mode}");
            sb.AppendLine($"GeneratedUtc: {when}");
            sb.AppendLine($"Pass={pass} Fail={fail} Warn={warn} Skip={skip}");
            sb.AppendLine($"HasBugs: {hasBugs}");
            sb.AppendLine($"ReportDir: {LabPaths.ReportDir}");
            if (hasBugs && root.TryGetProperty("Bugs", out var bugs) && bugs.ValueKind == JsonValueKind.Array)
            {
                sb.AppendLine();
                sb.AppendLine("Bugs:");
                foreach (var b in bugs.EnumerateArray())
                {
                    var status = b.TryGetProperty("Status", out var st) ? st.GetString() : "?";
                    var suite = b.TryGetProperty("Suite", out var su) ? su.GetString() : "?";
                    var name = b.TryGetProperty("Name", out var n) ? n.GetString() : "?";
                    var hint = b.TryGetProperty("FixHint", out var h) ? h.GetString() : "";
                    sb.AppendLine($"- [{status}] {suite}/{name}");
                    if (!string.IsNullOrWhiteSpace(hint))
                        sb.AppendLine($"  Fix: {hint}");
                }
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "Failed to parse last-report.json: " + ex.Message;
        }
    }

    private static string ResolveDotnet()
    {
        var pf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "dotnet.exe");
        if (File.Exists(pf))
            return pf;
        return "dotnet";
    }
}
