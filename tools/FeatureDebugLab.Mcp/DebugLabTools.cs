using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FeatureDebugLab.Mcp;

/// <summary>
/// MCP tools Cursor can call to run / read AutoCorrect Feature Debug Lab results.
/// </summary>
[McpServerToolType]
public static class DebugLabTools
{
    [McpServerTool(Name = "debuglab_run_offline"),
     Description(
         "Run AutoCorrect Feature Debug Lab offline bug hunt (no live UI typing). " +
         "Returns pass/fail summary and actionable Fix hints. Use this to find product regressions, then fix the hinted files.")]
    public static async Task<string> RunOffline(CancellationToken cancellationToken = default)
    {
        try
        {
            var (exit, stdout, stderr) = await LabRunner.RunOfflineAsync(cancellationToken)
                .ConfigureAwait(false);
            var status = LabRunner.SummarizeStatus();
            var failures = LabRunner.ReadFailures();
            return
                $"exit={exit} (0=clean, 1=warn, 2=fail)\n\n" +
                "=== STATUS ===\n" + status + "\n\n" +
                "=== FAILURES (agent-ready) ===\n" + failures + "\n\n" +
                "=== STDOUT ===\n" + Trim(stdout, 4000) + "\n\n" +
                (string.IsNullOrWhiteSpace(stderr) ? "" : "=== STDERR ===\n" + Trim(stderr, 2000));
        }
        catch (Exception ex)
        {
            return "debuglab_run_offline failed: " + ex.Message +
                   "\nLab project expected at: " + LabPaths.LabProject +
                   "\nSet env AC_DEBUG_LAB_TOOLS to …/AutoCorrectNative/tools if needed.";
        }
    }

    [McpServerTool(Name = "debuglab_get_status"),
     Description("Read the last Debug Lab report summary (pass/fail/warn + bug list with Fix hints). Does not re-run tests.")]
    public static string GetStatus() => LabRunner.SummarizeStatus();

    [McpServerTool(Name = "debuglab_get_failures"),
     Description(
         "Return last-failures.txt from the Debug Lab — pasteable bug list with Expected/Actual/Fix hints. " +
         "Call after debuglab_run_offline when HasBugs is true.")]
    public static string GetFailures() => LabRunner.ReadFailures();

    [McpServerTool(Name = "debuglab_get_report"),
     Description("Return last-report.md (full Markdown Debug Lab report).")]
    public static string GetReport() => LabRunner.ReadMarkdown();

    [McpServerTool(Name = "debuglab_get_report_json"),
     Description("Return last-report.json (structured Debug Lab report including Bugs array).")]
    public static string GetReportJson() => LabRunner.ReadJson();

    [McpServerTool(Name = "debuglab_paths"),
     Description("Show Debug Lab report directory, project path, and how Cursor is configured to connect.")]
    public static string Paths() =>
        "ReportDir: " + LabPaths.ReportDir + "\n" +
        "last-failures: " + LabPaths.LastFailures + "\n" +
        "last-report.md: " + LabPaths.LastMarkdown + "\n" +
        "last-report.json: " + LabPaths.LastJson + "\n" +
        "ToolsDir: " + LabPaths.ToolsDir + "\n" +
        "LabProject: " + LabPaths.LabProject + "\n" +
        "LabProjectExists: " + File.Exists(LabPaths.LabProject) + "\n" +
        "MCP: project .cursor/mcp.json → autocorrect-debug-lab";

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" :
        s.Length <= max ? s : s[..max] + "\n…(truncated)…";
}
