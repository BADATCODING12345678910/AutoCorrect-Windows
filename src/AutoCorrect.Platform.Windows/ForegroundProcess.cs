using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoCorrect.Platform.Windows;

public static class ForegroundProcess
{
    private static IntPtr _cachedHwnd;
    private static string? _cachedName;
    private static long _cachedTick;

    public static string? GetProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;

            var now = Environment.TickCount64;
            if (hwnd == _cachedHwnd &&
                _cachedName is not null &&
                now - _cachedTick < 250)
                return _cachedName;

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return null;
            using var p = Process.GetProcessById((int)pid);
            _cachedHwnd = hwnd;
            _cachedName = p.ProcessName;
            _cachedTick = now;
            return _cachedName;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsOurApp()
    {
        var name = GetProcessName();
        return name is not null &&
               name.Equals("AutoCorrect.App", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Foreground HWND at call time (for restoring focus before SendInput).</summary>
    public static IntPtr GetForegroundWindowSafe()
    {
        try { return GetForegroundWindow(); }
        catch { return IntPtr.Zero; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
