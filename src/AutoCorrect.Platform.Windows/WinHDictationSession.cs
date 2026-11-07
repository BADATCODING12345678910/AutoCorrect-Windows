using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoCorrect.Platform.Abstractions;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Windows Voice Typing session tracker (port of DictationFixer ~#h + TextInputHost watch).
/// First Win+H starts a session; second Win+H or mic-bar close ends it and raises <see cref="SessionEnded"/>.
/// Never swallows Win+H except during the post-start arm window (accidental double-tap).
/// Stopping Voice Typing (both paths end our session so the next Win+H opens fresh):
/// - Win+H again → stop listening only (physical toggle; no send).
/// - Enter → stop listening + let Enter reach the app (send/commit). If the mic bar is still
///   up after slow STT, we also inject Win+H to dismiss it (Enter alone often leaves it open).
/// </summary>
public sealed class WinHDictationSession : IDictationSession
{
    private readonly GlobalHotkey _hotkey;
    private readonly System.Threading.Timer _watchTimer;
    private readonly Func<bool> _shouldSuppressDictationKey;
    private readonly object _gate = new();
    private bool _started;
    private bool _sessionActive;
    private bool _uiSeenOpen;
    private int _uiSeenTicks;
    private int _uiGoneTicks;
    private long _sessionStartedMs;
    private long _uiConfirmedMs;
    private nint _targetHwnd;
    private nint _targetCtrlHwnd;
    private int _caretX;
    private int _caretY;
    private bool _hasCaret;
    private int _autoDelayMs = 1500;
    private int _minArmMs = 2500;
    private int _uiGoneTicksRequired = 5;
    private int _dismissGeneration;

    public int AutoDelayMs
    {
        get { lock (_gate) return _autoDelayMs; }
        set { lock (_gate) _autoDelayMs = Math.Max(0, value); }
    }

    /// <summary>Ignore / swallow a second Win+H for this long after session start.</summary>
    public int MinArmMs
    {
        get { lock (_gate) return _minArmMs; }
        set { lock (_gate) _minArmMs = Math.Clamp(value, 500, 8000); }
    }

    public int UiGoneTicksRequired
    {
        get { lock (_gate) return _uiGoneTicksRequired; }
        set { lock (_gate) _uiGoneTicksRequired = Math.Clamp(value, 2, 20); }
    }

    public bool IsSupported => true;
    public bool IsSessionActive
    {
        get { lock (_gate) return _sessionActive; }
    }

    public event EventHandler<DictationSessionEndedEventArgs>? SessionEnded;
    public event EventHandler? SessionStarted;

    public WinHDictationSession(GlobalHotkey hotkey, int autoDelayMs = 1500)
    {
        _hotkey = hotkey;
        _autoDelayMs = Math.Max(0, autoDelayMs);
        _shouldSuppressDictationKey = ShouldSuppressDictationKey;
        _watchTimer = new System.Threading.Timer(_ => WatchTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// True while an active session is still in the arm window — GlobalHotkey should swallow
    /// Win+H so Windows does not toggle Voice Typing off from an accidental double-press.
    /// </summary>
    public bool ShouldSuppressDictationKey()
    {
        lock (_gate)
        {
            if (!_sessionActive)
                return false;
            return Environment.TickCount64 - _sessionStartedMs < _minArmMs;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
                return;
            _started = true;
            _hotkey.WinHPressed += OnWinH;
            _hotkey.EnterPressed += OnEnter;
            _hotkey.ShouldSuppressDictationKey = _shouldSuppressDictationKey;
            _watchTimer.Change(400, 400);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _dismissGeneration);
        lock (_gate)
        {
            if (!_started)
                return;
            _started = false;
            _hotkey.WinHPressed -= OnWinH;
            _hotkey.EnterPressed -= OnEnter;
            if (ReferenceEquals(_hotkey.ShouldSuppressDictationKey, _shouldSuppressDictationKey))
                _hotkey.ShouldSuppressDictationKey = null;
            _watchTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _sessionActive = false;
            _uiSeenOpen = false;
            _uiSeenTicks = 0;
            _uiGoneTicks = 0;
        }
    }

    /// <summary>
    /// Enter = stop Voice Typing + send (Enter still reaches the app).
    /// If the mic bar is still up after slow STT, inject Win+H to dismiss it.
    /// </summary>
    private void OnEnter()
    {
        bool dismissUi = false;
        lock (_gate)
        {
            if (!_started || !_sessionActive)
                return;

            var elapsed = Environment.TickCount64 - _sessionStartedMs;
            if (elapsed < _minArmMs)
                return;

            dismissUi = IsVoiceTypingUiVisible();
            EndSession_NoLock("enter");
        }

        if (dismissUi)
            ScheduleVoiceTypingDismiss();
    }

    /// <summary>
    /// Win+H while active (after arm) = stop Voice Typing only (no send).
    /// Physical key toggles Windows off; we end our session so the next Win+H starts clean.
    /// </summary>
    private void OnWinH()
    {
        lock (_gate)
        {
            if (!_started)
                return;
            if (_sessionActive)
            {
                var elapsed = Environment.TickCount64 - _sessionStartedMs;
                // Accidental double-tap: keep session; key may already be swallowed by hotkey.
                if (elapsed < _minArmMs)
                    return;

                // Physical Win+H toggles Voice Typing off — stop only, do not reopen.
                EndSession_NoLock("win_h");
                return;
            }

            BeginSession_NoLock();
        }
    }

    /// <summary>
    /// After Enter while the mic bar was still up, toggle Voice Typing off so it does not
    /// linger and steal the next Win+H. Runs off the LL-hook thread.
    /// </summary>
    private void ScheduleVoiceTypingDismiss()
    {
        var gen = Interlocked.Increment(ref _dismissGeneration);
        InputInjector.Enqueue(() =>
        {
            try
            {
                // Let Enter reach the app first, then dismiss the mic if it is still up.
                Thread.Sleep(180);
                if (gen != Volatile.Read(ref _dismissGeneration))
                    return;

                lock (_gate)
                {
                    if (!_started)
                        return;
                }

                if (!IsVoiceTypingUiVisible())
                    return;

                SendInputText.SendChordAsUser(
                    win: true, alt: false, ctrl: false, shift: false, vk: 0x48 /* H */);
            }
            catch
            {
                // never break injector
            }
        });
    }

    private void BeginSession_NoLock()
    {
        _sessionActive = true;
        _uiSeenOpen = false;
        _uiSeenTicks = 0;
        _uiGoneTicks = 0;
        _uiConfirmedMs = 0;
        _sessionStartedMs = Environment.TickCount64;
        _targetHwnd = GetForegroundWindow();
        _targetCtrlHwnd = GetFocusedControlHwnd(_targetHwnd);
        CaptureCaretPoint_NoLock();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { SessionStarted?.Invoke(this, EventArgs.Empty); }
            catch { /* ignore */ }
        });
    }

    private void EndSession_NoLock(string reason)
    {
        if (!_sessionActive)
            return;

        var elapsed = Environment.TickCount64 - _sessionStartedMs;
        var uiSeen = _uiSeenOpen;
        var hwnd = _targetHwnd;
        var ctrl = _targetCtrlHwnd;
        var cx = _hasCaret ? _caretX : (int?)null;
        var cy = _hasCaret ? _caretY : (int?)null;

        // For ui_closed, require a real listen window so a flickering shell UI cannot end us
        // before the user has said anything.
        if (string.Equals(reason, "ui_closed", StringComparison.OrdinalIgnoreCase))
        {
            var minUiClosed = Math.Max(_minArmMs, 4000);
            if (elapsed < minUiClosed)
                return;
            if (_uiConfirmedMs != 0 && Environment.TickCount64 - _uiConfirmedMs < 1500)
                return;
        }

        _sessionActive = false;
        _uiSeenOpen = false;
        _uiSeenTicks = 0;
        _uiGoneTicks = 0;
        _targetCtrlHwnd = 0;

        // Ignore accidental double-taps / instant close for win_h (session already guarded above).
        if (elapsed < 700)
            return;

        var postDelay = string.Equals(reason, "ui_closed", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(_autoDelayMs, 350)
            : _autoDelayMs;

        var args = new DictationSessionEndedEventArgs
        {
            TargetWindowHandle = hwnd,
            TargetControlHandle = ctrl,
            Reason = reason,
            AutoDelayMs = postDelay,
            VoiceUiWasSeen = uiSeen,
            ElapsedMs = elapsed,
            CaretScreenX = cx,
            CaretScreenY = cy
        };

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { SessionEnded?.Invoke(this, args); }
            catch { /* ignore */ }
        });
    }

    private void WatchTick()
    {
        lock (_gate)
        {
            if (!_started || !_sessionActive)
                return;

            var openNow = IsVoiceTypingUiVisible();
            if (openNow)
            {
                _uiSeenTicks++;
                _uiGoneTicks = 0;
                // Require two consecutive hits so emoji/clipboard flicker does not arm ui_closed.
                if (!_uiSeenOpen && _uiSeenTicks >= 2)
                {
                    _uiSeenOpen = true;
                    _uiConfirmedMs = Environment.TickCount64;
                }
                return;
            }

            _uiSeenTicks = 0;

            if (!_uiSeenOpen)
                return;

            // Overlay was confirmed, then disappeared — need several quiet ticks (~2s default).
            if (Environment.TickCount64 - _sessionStartedMs > Math.Max(1500, _minArmMs))
            {
                _uiGoneTicks++;
                if (_uiGoneTicks >= _uiGoneTicksRequired)
                    EndSession_NoLock("ui_closed");
            }
        }
    }

    private static nint GetFocusedControlHwnd(nint winHwnd)
    {
        try
        {
            if (winHwnd == 0)
                winHwnd = GetForegroundWindow();
            if (winHwnd == 0)
                return 0;

            var tid = GetWindowThreadProcessId(winHwnd, out _);
            var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(tid, ref info))
                return 0;
            return info.hwndFocus != 0 ? info.hwndFocus : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void CaptureCaretPoint_NoLock()
    {
        _hasCaret = false;
        _caretX = 0;
        _caretY = 0;

        try
        {
            var hwnd = _targetHwnd != 0 ? _targetHwnd : GetForegroundWindow();
            if (hwnd == 0)
                return;

            var tid = GetWindowThreadProcessId(hwnd, out _);
            var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(tid, ref info))
            {
                FallbackMouse_NoLock();
                return;
            }

            if (_targetCtrlHwnd == 0 && info.hwndFocus != 0)
                _targetCtrlHwnd = info.hwndFocus;

            if (info.hwndCaret != 0 &&
                (info.rcCaret.Right > info.rcCaret.Left || info.rcCaret.Bottom > info.rcCaret.Top))
            {
                var cx = info.rcCaret.Left + Math.Max(1, (info.rcCaret.Right - info.rcCaret.Left) / 2);
                var cy = info.rcCaret.Top + Math.Max(1, (info.rcCaret.Bottom - info.rcCaret.Top) / 2);
                var pt = new POINT { X = cx, Y = cy };
                if (ClientToScreen(info.hwndCaret, ref pt))
                {
                    _caretX = pt.X;
                    _caretY = pt.Y;
                    _hasCaret = true;
                    return;
                }
            }
        }
        catch
        {
            // ignore
        }

        FallbackMouse_NoLock();
    }

    private void FallbackMouse_NoLock()
    {
        if (GetCursorPos(out var pt))
        {
            _caretX = pt.X;
            _caretY = pt.Y;
            _hasCaret = pt.X != 0 || pt.Y != 0;
        }
    }

    /// <summary>Win11 Voice Typing mic bar lives in TextInputHost — small visible strip.</summary>
    public static bool IsVoiceTypingUiVisible()
    {
        try
        {
            var procs = Process.GetProcessesByName("TextInputHost");
            try
            {
                foreach (var proc in procs)
                {
                    foreach (var hwnd in EnumerateProcessWindows(proc.Id))
                    {
                        if (!IsWindowVisible(hwnd))
                            continue;
                        if (!GetWindowRect(hwnd, out var rc))
                            continue;
                        var w = rc.Right - rc.Left;
                        var h = rc.Bottom - rc.Top;
                        // Mic bar is a compact floating strip. Exclude huge shells / tiny pixels.
                        if (w is >= 120 and <= 900 && h is >= 48 and <= 280)
                            return true;
                    }
                }
            }
            finally
            {
                foreach (var p in procs)
                    p.Dispose();
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    private static IEnumerable<nint> EnumerateProcessWindows(int pid)
    {
        var list = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var wpid);
            if ((int)wpid == pid)
                list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public void Dispose() => Stop();

    private delegate bool EnumWindowsProc(nint hWnd, IntPtr lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
}
