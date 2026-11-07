namespace FeatureDebugLab.Mcp;

internal static class LabPaths
{
    public static string ReportDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect", "debug-lab");

    public static string LastJson => Path.Combine(ReportDir, "last-report.json");
    public static string LastMarkdown => Path.Combine(ReportDir, "last-report.md");
    public static string LastFailures => Path.Combine(ReportDir, "last-failures.txt");

    /// <summary>Repo tools folder: …/AutoCorrectNative/tools</summary>
    public static string ToolsDir
    {
        get
        {
            // Prefer env override (set in mcp.json).
            var env = Environment.GetEnvironmentVariable("AC_DEBUG_LAB_TOOLS");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return env.TrimEnd('\\', '/');

            // bin/Debug/net8.0 → FeatureDebugLab.Mcp → tools
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(dir, "FeatureDebugLab"));
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "FeatureDebugLab.csproj")))
                    return Path.GetDirectoryName(candidate)!;

                var parent = Directory.GetParent(dir);
                if (parent is null)
                    break;
                dir = parent.FullName;
            }

            // Fallback: walk up from cwd
            dir = Directory.GetCurrentDirectory();
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "AutoCorrectNative", "tools");
                if (Directory.Exists(Path.Combine(candidate, "FeatureDebugLab")))
                    return candidate;
                var parent = Directory.GetParent(dir);
                if (parent is null)
                    break;
                dir = parent.FullName;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }

    public static string LabProject =>
        Path.Combine(ToolsDir, "FeatureDebugLab", "FeatureDebugLab.csproj");

    public static string RunCmd =>
        Path.Combine(ToolsDir, "run-feature-debug-lab.cmd");
}
