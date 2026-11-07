using System.IO;

namespace FeatureDebugLab.Services;

/// <summary>
/// Decides which discovered apps are safe/useful for live text testing.
/// Blocks Control Panel, CPL applets, installers, and other non-document hosts.
/// </summary>
public static class LiveAppPolicy
{
    private static readonly HashSet<string> BlockExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "control.exe", "mmc.exe", "explorer.exe", "ApplicationFrameHost.exe",
        "SystemSettings.exe", "SystemSettingsAdminFlows.exe", "Taskmgr.exe",
        "calc.exe", "CalculatorApp.exe", "SnippingTool.exe", "ScreenClippingHost.exe",
        "mspaint.exe", "PaintStudio.View.exe", "Magnify.exe", "Narrator.exe",
        "javacpl.exe", "jusched.exe", "jp2launcher.exe",
        "WizLink.exe", "OneDriveSetup.exe", "OfficeClickToRun.exe",
        "AutoCorrect.App.exe", "FeatureDebugLab.exe",
        "Cursor.exe", // IDE under test machine — Fix must not eat the lab log / chat
        "Code.exe",
        "devenv.exe",
        "WindowsTerminal.exe", "wt.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "7zFM.exe", "7zG.exe", "WinRAR.exe",
        "MSACCESS.EXE", // Access forms ≠ free text pad; too many false edit hits
    };

    private static readonly string[] BlockNameOrPath =
    [
        "administrative tools", "control panel", "about java", "java control",
        "adobe offers", "online services", "uninstall", "setup", "installer",
        " Troubleshoot", "recovery", "memory diagnostic", "character map",
        "odbc", "windows security", "windows defender", "firewall",
        "device manager", "disk cleanup", "resource monitor", "performance monitor",
        "registry editor", "services", "task scheduler", "event viewer",
        "file explorer", "command prompt", "windows powershell",
        "visual studio", "cursor", "feature debug",
    ];

    /// <summary>Exe/name hints that are likely real typing surfaces.</summary>
    private static readonly string[] TextCapableHints =
    [
        "notepad", "wordpad", "write.exe", "winword", "word",
        "excel", "powerpnt", "powerpoint", "outlook", "onenote",
        "sticky", "wordpad", "notepad++", "sublime",
        "notepad2", "notepad3", "emeditor", "ultraedit", "textpad",
        "obsidian", "notion", "typora", "marktext", "zettlr",
        "evernote", "simplenote", "standard notes",
        "discord", "slack", "telegram", "whatsapp", "signal",
        "mail", "thunderbird", "outlook",
        "chrome", "msedge", "firefox", "brave", "opera",
        "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe",
        "onenote.exe", "notepad.exe", "stikynot.exe",
    ];

    public static IReadOnlyList<DiscoveredApp> Filter(
        IReadOnlyList<DiscoveredApp> apps,
        bool textCapableOnly,
        string? extraExcludeCsv)
    {
        var extra = ParseExtra(extraExcludeCsv);
        var list = new List<DiscoveredApp>();
        foreach (var app in apps)
        {
            if (!IsAllowed(app, textCapableOnly, extra))
                continue;
            list.Add(app);
        }
        return list;
    }

    public static bool IsAllowed(DiscoveredApp app, bool textCapableOnly, HashSet<string>? extraExclude = null)
    {
        var exe = Path.GetFileName(app.ExePath) ?? "";
        var blob = $"{app.Name} {app.ExePath}".ToLowerInvariant();

        if (BlockExe.Contains(exe))
            return false;
        if (extraExclude is not null && extraExclude.Contains(exe))
            return false;

        foreach (var bad in BlockNameOrPath)
        {
            if (blob.Contains(bad.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                return false;
        }

        // Never launch raw control.exe / rundll32 control applets.
        if (exe.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("control.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!textCapableOnly)
            return true;

        foreach (var hint in TextCapableHints)
        {
            if (blob.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static HashSet<string> ParseExtra(string? csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (csv ?? "").Split([',', ';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var n = part.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? part : part + ".exe";
            set.Add(n);
        }
        return set;
    }
}
