using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FeatureDebugLab.Services;

/// <summary>Find visible AutoCorrect UI windows by process + title (review card, rewrite, settings, …).</summary>
public static class AppWindowProbe
{
    public static bool IsAutoCorrectRunning() =>
        Process.GetProcessesByName("AutoCorrect.App").Length > 0;

    public static bool TryFindVisibleWindow(string titleContains, out IntPtr hwnd, int timeoutMs = 4000)
    {
        hwnd = IntPtr.Zero;
        var deadline = Environment.TickCount64 + Math.Max(200, timeoutMs);
        while (Environment.TickCount64 < deadline)
        {
            hwnd = FindFirst(titleContains);
            if (hwnd != IntPtr.Zero)
                return true;
            Thread.Sleep(120);
        }
        return false;
    }

    public static bool IsWindowVisibleNow(string titleContains) =>
        FindFirst(titleContains) != IntPtr.Zero;

    private static IntPtr FindFirst(string titleContains)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h))
                return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == 0)
                return true;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!p.ProcessName.Equals("AutoCorrect.App", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                return true;
            }

            var sb = new StringBuilder(512);
            _ = GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
