using System.Runtime.InteropServices;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Restore the pre-dictation field (AHK DictationFixer.FocusSessionTarget).
/// Electron/Chromium need a real caret click; SetFocus alone is not enough.
/// </summary>
public static class FocusRestorer
{
    public static bool Restore(nint windowHwnd, nint controlHwnd, int? caretScreenX, int? caretScreenY)
    {
        var activated = false;
        nint activateHwnd = 0;

        if (windowHwnd != 0 && IsWindow(windowHwnd))
            activateHwnd = windowHwnd;
        else if (controlHwnd != 0 && IsWindow(controlHwnd))
            activateHwnd = GetAncestor(controlHwnd, 2 /* GA_ROOT */);

        if (activateHwnd != 0)
        {
            try
            {
                // Only un-minimize when actually iconic. SW_RESTORE on a normal/maximized
                // Electron window can shrink or otherwise disturb the host (user saw minimize).
                if (IsIconic(activateHwnd))
                    ShowWindow(activateHwnd, 9 /* SW_RESTORE */);
                SetForegroundWindow(activateHwnd);
                Thread.Sleep(80);
                activated = true;
            }
            catch
            {
                // ignore
            }
        }

        var clicked = TryClickScreenPoint(caretScreenX, caretScreenY, activateHwnd);

        // Prefer focusing the child control — never SetFocus the top-level Electron HWND
        // (that can land focus on the file tree / chrome).
        if (controlHwnd != 0 &&
            IsWindow(controlHwnd) &&
            controlHwnd != windowHwnd &&
            controlHwnd != activateHwnd)
        {
            try
            {
                var myTid = GetCurrentThreadId();
                var targetTid = GetWindowThreadProcessId(controlHwnd, out _);
                var attached = false;
                if (targetTid != 0 && targetTid != myTid)
                {
                    AttachThreadInput(myTid, targetTid, true);
                    attached = true;
                }

                SetFocus(controlHwnd);
                if (attached)
                    AttachThreadInput(myTid, targetTid, false);
                Thread.Sleep(60);
            }
            catch
            {
                // ignore
            }
        }

        return clicked || activated;
    }

    public static bool TryClickScreenPoint(int? x, int? y, nint windowHwnd = 0)
    {
        if (x is not int cx || y is not int cy)
            return false;
        if (cx == 0 && cy == 0)
            return false;

        // Don't click the title-bar strip (minimize / maximize / close).
        if (windowHwnd != 0 && IsWindow(windowHwnd) && GetWindowRect(windowHwnd, out var rc))
        {
            if (cy >= rc.Top && cy <= rc.Top + 40 && cx >= rc.Left && cx <= rc.Right)
                return false;
        }

        try
        {
            GetCursorPos(out POINT old);
            SetCursorPos(cx, cy);
            mouse_event(0x0002 /* LEFTDOWN */, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004 /* LEFTUP */, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(90);
            SetCursorPos(old.X, old.Y);
            Thread.Sleep(50);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
