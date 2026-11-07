using System.IO;
using Microsoft.Win32;

namespace AutoCorrect.Platform.Windows;

/// <summary>Register/unregister AutoCorrect in HKCU Run for Start with Windows.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoCorrect";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled, string? exePath = null)
    {
        exePath ??= Path.Combine(AppContext.BaseDirectory, "AutoCorrect.App.exe");
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (key is null)
            throw new InvalidOperationException("Couldn't open HKCU Run key.");

        if (enabled)
            key.SetValue(ValueName, "\"" + exePath + "\"");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }
}
