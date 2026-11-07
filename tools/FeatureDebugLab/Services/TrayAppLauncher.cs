using System.Diagnostics;
using System.IO;

namespace FeatureDebugLab.Services;

public static class TrayAppLauncher
{
    /// <summary>Start AutoCorrect.App from the local Debug/Release build if it is not already running.</summary>
    public static async Task<(bool Ok, string Detail)> EnsureRunningAsync(CancellationToken ct)
    {
        if (AppWindowProbe.IsAutoCorrectRunning())
            return (true, "already running");

        var exe = FindAppExe();
        if (exe is null)
            return (false, "AutoCorrect.App.exe not found under AutoCorrectNative/src/AutoCorrect.App/bin — build the tray app first");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            };
            _ = Process.Start(psi);
            for (var i = 0; i < 40; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (AppWindowProbe.IsAutoCorrectRunning())
                    return (true, "launched " + exe);
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            return (false, "started process but AutoCorrect.App never appeared");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static string? FindAppExe()
    {
        // bin/Debug/net8.0-windows → FeatureDebugLab → tools → AutoCorrectNative
        var nativeRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var candidates = new[]
        {
            Path.Combine(nativeRoot, "src", "AutoCorrect.App", "bin", "Debug", "net8.0-windows", "AutoCorrect.App.exe"),
            Path.Combine(nativeRoot, "src", "AutoCorrect.App", "bin", "Release", "net8.0-windows", "AutoCorrect.App.exe"),
            Path.Combine(nativeRoot, "src", "AutoCorrect.App", "bin", "Release", "net8.0-windows", "win-x64", "AutoCorrect.App.exe"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        // Fallback: walk up from cwd
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            foreach (var rel in new[]
                     {
                         @"src\AutoCorrect.App\bin\Debug\net8.0-windows\AutoCorrect.App.exe",
                         @"src\AutoCorrect.App\bin\Release\net8.0-windows\AutoCorrect.App.exe",
                     })
            {
                var p = Path.Combine(dir.FullName, rel);
                if (File.Exists(p))
                    return p;
                p = Path.Combine(dir.FullName, "AutoCorrectNative", rel);
                if (File.Exists(p))
                    return p;
            }
        }

        return null;
    }
}
