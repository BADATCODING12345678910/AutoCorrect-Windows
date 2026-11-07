using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FeatureDebugLab.Services;

public sealed record DiscoveredApp(string Name, string ExePath, string Source);

/// <summary>
/// Discovers installed/launchable desktop apps from Start Menu shortcuts and App Paths.
/// </summary>
public static class InstalledAppScanner
{
    private static readonly HashSet<string> SkipNameFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "uninstall", "unins000", "setup", "installer", "update", "updater",
        "crashpad", "crash reporter", "helper", "notification_helper",
        "elevation_service", "chrmotp", "werfault", "dwm", "csrss",
        "autocorrect.app", "featuredebuglab", "dotnet", "mscorsvw",
        // File managers / shells — typing here renames files or navigates, not text editing.
        "file manager", "file explorer", "7-zip", "7zfm", "winrar", "peazip",
        "total commander", "double commander", "directory opus", "xyplorer",
        "freecommander", "far manager", "midnight commander",
    };

    private static readonly HashSet<string> SkipExactExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "dllhost.exe",
        "svchost.exe", "explorer.exe", "ApplicationFrameHost.exe", "SystemSettings.exe",
        "SearchHost.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe",
        "RuntimeBroker.exe", "sihost.exe", "taskhostw.exe", "TextInputHost.exe",
        "AutoCorrect.App.exe", "FeatureDebugLab.exe",
        "7zFM.exe", "7zG.exe", "7z.exe", "WinRAR.exe", "PeaZip.exe",
        "TOTALCMD64.EXE", "TOTALCMD.EXE", "DoubleCommander.exe",
    };

    public static IReadOnlyList<DiscoveredApp> Scan(IProgress<string>? log = null)
    {
        var byExe = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string exe, string source)
        {
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;
            exe = Path.GetFullPath(exe);
            var file = Path.GetFileName(exe);
            if (SkipExactExe.Contains(file))
                return;
            var lower = (name + " " + exe).ToLowerInvariant();
            foreach (var frag in SkipNameFragments)
            {
                if (lower.Contains(frag, StringComparison.Ordinal))
                    return;
            }
            // Prefer a nicer Start Menu name if we already have a registry stub.
            if (byExe.TryGetValue(exe, out var existing))
            {
                if (source.StartsWith("Start Menu", StringComparison.OrdinalIgnoreCase) &&
                    !existing.Source.StartsWith("Start Menu", StringComparison.OrdinalIgnoreCase))
                    byExe[exe] = new DiscoveredApp(name, exe, source);
                return;
            }
            byExe[exe] = new DiscoveredApp(
                string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(exe) : name.Trim(),
                exe,
                source);
        }

        // Always include Notepad as a known-good text host.
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        Add("Notepad", notepad, "System");

        log?.Report("Scanning Start Menu shortcuts…");
        foreach (var root in StartMenuRoots())
            ScanShortcuts(root, Add);

        log?.Report("Scanning Registry App Paths…");
        ScanAppPaths(Add);

        var list = byExe.Values
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        log?.Report($"Discovered {list.Count} launchable apps.");
        return list;
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (!string.IsNullOrWhiteSpace(programs))
            yield return programs;
        var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        if (!string.IsNullOrWhiteSpace(commonPrograms))
            yield return commonPrograms;
    }

    private static void ScanShortcuts(string root, Action<string, string, string> add)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        foreach (var lnk in files)
        {
            try
            {
                var target = ResolveShortcut(lnk);
                if (string.IsNullOrWhiteSpace(target))
                    continue;
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                var name = Path.GetFileNameWithoutExtension(lnk);
                add(name, target, "Start Menu");
            }
            catch
            {
                // skip bad shortcuts
            }
        }
    }

    private static void ScanAppPaths(Action<string, string, string> add)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (key is null)
                    continue;
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        var path = sub?.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(path))
                            continue;
                        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var name = Path.GetFileNameWithoutExtension(subName);
                        add(name, path, "App Paths");
                    }
                    catch
                    {
                        // skip
                    }
                }
            }
            catch
            {
                // skip hive
            }
        }
    }

    /// <summary>Resolve .lnk target via Shell COM (WScript.Shell).</summary>
    private static string? ResolveShortcut(string lnkPath)
    {
        // Prefer IWshRuntimeLibrary via late-bound COM — no extra NuGet.
        Type? type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null)
            return null;
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(type);
            if (shell is null)
                return null;
            shortcut = type.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [lnkPath]);
            if (shortcut is null)
                return null;
            var target = shortcut.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
            return string.IsNullOrWhiteSpace(target) ? null : target.Trim().Trim('"');
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
