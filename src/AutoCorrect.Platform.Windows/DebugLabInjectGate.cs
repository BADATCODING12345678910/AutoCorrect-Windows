using System.IO;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// When Feature Debug Lab is running live typing tests, accept SendInput keystrokes
/// that are <em>not</em> from AutoCorrect's own inject marker (so the LL hook can be tested).
/// </summary>
public static class DebugLabInjectGate
{
    public static string FlagPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect",
            "debug-lab-accept-injected.flag");

    public static bool IsOpen
    {
        get
        {
            try { return File.Exists(FlagPath); }
            catch { return false; }
        }
    }

    public static void Open()
    {
        var dir = Path.GetDirectoryName(FlagPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(FlagPath, DateTime.UtcNow.ToString("o"));
    }

    public static void Close()
    {
        try
        {
            if (File.Exists(FlagPath))
                File.Delete(FlagPath);
        }
        catch { /* ignore */ }
    }
}
