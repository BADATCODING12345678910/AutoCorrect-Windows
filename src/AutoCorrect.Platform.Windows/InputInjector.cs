using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Runs SendInput off the low-level keyboard hook thread.
/// Calling SendInput inside WH_KEYBOARD_LL is undocumented/unsafe and fails in Electron (Cursor, Slack, etc.).
/// </summary>
public static class InputInjector
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly Thread Worker;
    private static volatile bool _running = true;

    static InputInjector()
    {
        Worker = new Thread(Loop)
        {
            IsBackground = true,
            Name = "AutoCorrect.InputInjector",
            Priority = ThreadPriority.AboveNormal
        };
        Worker.SetApartmentState(ApartmentState.STA);
        Worker.Start();
    }

    public static void Enqueue(Action action)
    {
        Queue.Enqueue(action);
        Wake.Set();
    }

    /// <summary>
    /// After the end-character has been allowed through (or swallowed), replace the typed word.
    /// Invokes <paramref name="onCompleted"/> with success after inject (never on the LL hook thread).
    /// </summary>
    /// <param name="typedLength">Letters already in the field (not including end char if it already landed).</param>
    /// <param name="endCharAlreadyTyped">If true, also backspace the end character then retype it.</param>
    /// <param name="targetHwnd">Foreground HWND captured when the match fired (restore if toast stole focus).</param>
    public static void EnqueueReplaceWord(
        int typedLength,
        string replacement,
        char endChar,
        bool endCharAlreadyTyped,
        int delayMs = 2,
        IntPtr targetHwnd = default,
        Action<bool>? onCompleted = null)
    {
        Enqueue(() =>
        {
            var ok = false;
            try
            {
                if (delayMs > 0)
                    Thread.Sleep(delayMs);

                // Undo toast / other AC windows must not keep focus or SendInput hits the wrong app.
                EnsureTargetForeground(targetHwnd);

                var fg = GetForegroundWindow();
                uint fgThread = 0;
                var attached = false;
                var ourThread = GetCurrentThreadId();
                if (fg != IntPtr.Zero)
                {
                    fgThread = GetWindowThreadProcessId(fg, out _);
                    if (fgThread != 0 && fgThread != ourThread)
                        attached = AttachThreadInput(ourThread, fgThread, true);
                }

                try
                {
                    // Skip if focus landed on our own process (inject would "succeed" into nowhere useful).
                    if (IsOurProcessForeground())
                    {
                        ok = false;
                        return;
                    }

                    var backspaces = typedLength + (endCharAlreadyTyped ? 1 : 0);
                    ok = SendInputText.ReplaceWord(backspaces, replacement, endChar, paced: true);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(ourThread, fgThread, false);
                }
            }
            catch
            {
                ok = false;
            }
            finally
            {
                try { onCompleted?.Invoke(ok); }
                catch { /* never kill injector */ }
            }
        });
    }

    /// <summary>
    /// Delete the last N characters already typed (e.g. collapse an accidental second Space).
    /// Runs off the LL hook thread like <see cref="EnqueueReplaceWord"/>.
    /// </summary>
    public static void EnqueueBackspaces(int count, int delayMs = 2, IntPtr targetHwnd = default)
    {
        if (count <= 0)
            return;

        Enqueue(() =>
        {
            try
            {
                if (delayMs > 0)
                    Thread.Sleep(delayMs);

                EnsureTargetForeground(targetHwnd);

                var fg = GetForegroundWindow();
                uint fgThread = 0;
                var attached = false;
                var ourThread = GetCurrentThreadId();
                if (fg != IntPtr.Zero)
                {
                    fgThread = GetWindowThreadProcessId(fg, out _);
                    if (fgThread != 0 && fgThread != ourThread)
                        attached = AttachThreadInput(ourThread, fgThread, true);
                }

                try
                {
                    if (IsOurProcessForeground())
                        return;

                    SendInputText.ReplaceWord(count, "", '\0', paced: true);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(ourThread, fgThread, false);
                }
            }
            catch
            {
                // best-effort
            }
        });
    }

    private static void EnsureTargetForeground(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero || !IsWindow(targetHwnd))
            return;

        var fg = GetForegroundWindow();
        if (fg == targetHwnd)
            return;

        // Only steal back when our toast/card took focus (or no FG).
        if (fg != IntPtr.Zero && !IsOurProcessWindow(fg))
            return;

        try
        {
            // Allow SetForegroundWindow from background injector thread.
            var targetThread = GetWindowThreadProcessId(targetHwnd, out _);
            var ourThread = GetCurrentThreadId();
            var attached = false;
            if (targetThread != 0 && targetThread != ourThread)
                attached = AttachThreadInput(ourThread, targetThread, true);
            try
            {
                ShowWindow(targetHwnd, 9 /* SW_RESTORE */);
                SetForegroundWindow(targetHwnd);
                Thread.Sleep(2);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(ourThread, targetThread, false);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool IsOurProcessForeground()
    {
        var fg = GetForegroundWindow();
        return fg != IntPtr.Zero && IsOurProcessWindow(fg);
    }

    private static bool IsOurProcessWindow(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return false;
        if (pid == (uint)Environment.ProcessId)
            return true;

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            return name.Equals("AutoCorrect.App", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void Loop()
    {
        while (_running)
        {
            Wake.WaitOne(500);
            while (Queue.TryDequeue(out var action))
            {
                try { action(); }
                catch { /* never kill injector */ }
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
