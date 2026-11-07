using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Configurable global chords via WH_KEYBOARD_LL.
/// Chord handlers are passthrough (CallNextHookEx) so OS features like Voice Typing still work.
/// Optional <see cref="TypingHandler"/> may swallow a single key when applying autocorrect.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int LlkhfInjected = 0x10;
    private const int VkMenu = 0x12;
    private const int VkControl = 0x11;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkShift = 0x10;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;

    private readonly Thread _thread;
    private readonly LowLevelKeyboardProc _proc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _chordGate = new();
    private volatile bool _running = true;
    private IntPtr _hook;
    private uint _threadId;
    private long _lastFixTicks;
    private long _lastUndoTicks;
    private long _lastDictationTicks;
    private long _lastRewriteTicks;
    private long _lastPauseTicks;
    private long _lastClipboardFixTicks;
    private long _lastPrivacyTicks;

    private HotkeyChord _fixChord = HotkeyChord.Parse("Win+Alt+F")!;
    private HotkeyChord? _undoChord = HotkeyChord.Parse("Win+Alt+Shift+F");
    private HotkeyChord _dictationChord = HotkeyChord.Parse("Win+H")!;
    private HotkeyChord? _rewriteChord = HotkeyChord.Parse("Win+Alt+R");
    private HotkeyChord? _pauseChord = HotkeyChord.Parse("Win+Alt+P");
    private HotkeyChord? _clipboardFixChord = HotkeyChord.Parse("Win+Alt+C");
    private HotkeyChord? _privacyChord = HotkeyChord.Parse("Win+Alt+Shift+P");

    public event Action? FixPressed;
    public event Action? UndoPressed;

    /// <summary>Observed dictation toggle chord (default Win+H) — passthrough unless suppressed.</summary>
    public event Action? WinHPressed;

    /// <summary>
    /// Bare Enter (no Win/Ctrl/Alt; Shift optional for soft-newline apps is excluded).
    /// Used to dismiss a lingering Voice Typing session after the user sends/commits.
    /// </summary>
    public event Action? EnterPressed;

    /// <summary>
    /// When true, the dictation chord is swallowed (not passed to Windows). Used to block an
    /// accidental second Win+H from closing Voice Typing during the arm window.
    /// </summary>
    public Func<bool>? ShouldSuppressDictationKey { get; set; }

    /// <summary>Phase D selection rewrite chord (default Win+Alt+R) — passthrough.</summary>
    public event Action? RewritePressed;

    /// <summary>Toggle soft-pause for as-you-type autocorrect.</summary>
    public event Action? PausePressed;

    /// <summary>Clean clipboard text (optional paste) — default Win+Alt+C.</summary>
    public event Action? ClipboardFixPressed;

    /// <summary>Toggle Privacy mode — default Win+Alt+Shift+P.</summary>
    public event Action? PrivacyPressed;

    /// <summary>
    /// Optional as-you-type observer. Invoked for non-chord key downs.
    /// Return true to swallow the key (used when autocorrect re-injects the end char).
    /// </summary>
    public TypingKeyHandler? TypingHandler { get; set; }

    public string ActiveChord
    {
        get { lock (_chordGate) return _fixChord.Display; }
    }

    public bool IsRegistered { get; private set; }
    public string StatusMessage { get; private set; } = "Starting…";

    public GlobalHotkey()
    {
        _proc = HookCallback;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "AutoCorrect.Hotkeys",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public bool WaitUntilReady(int timeoutMs = 3000) => _ready.Wait(timeoutMs);

    /// <summary>Update chords at runtime (from Settings). Invalid strings keep previous / defaults.</summary>
    public void SetChords(
        string? fixHotkey,
        string? undoHotkey,
        string? dictationHotkey,
        string? rewriteHotkey = null,
        string? pauseHotkey = null,
        string? clipboardFixHotkey = null,
        string? privacyHotkey = null)
    {
        lock (_chordGate)
        {
            _fixChord = HotkeyChord.Parse(fixHotkey) ?? HotkeyChord.Parse("Win+Alt+F")!;
            _undoChord = string.IsNullOrWhiteSpace(undoHotkey)
                ? null
                : HotkeyChord.Parse(undoHotkey);
            _dictationChord = HotkeyChord.Parse(dictationHotkey) ?? HotkeyChord.Parse("Win+H")!;
            _rewriteChord = string.IsNullOrWhiteSpace(rewriteHotkey)
                ? null
                : HotkeyChord.Parse(rewriteHotkey) ?? HotkeyChord.Parse("Win+Alt+R");
            _pauseChord = string.IsNullOrWhiteSpace(pauseHotkey)
                ? null
                : HotkeyChord.Parse(pauseHotkey) ?? HotkeyChord.Parse("Win+Alt+P");
            _clipboardFixChord = string.IsNullOrWhiteSpace(clipboardFixHotkey)
                ? null
                : HotkeyChord.Parse(clipboardFixHotkey) ?? HotkeyChord.Parse("Win+Alt+C");
            _privacyChord = string.IsNullOrWhiteSpace(privacyHotkey)
                ? null
                : HotkeyChord.Parse(privacyHotkey) ?? HotkeyChord.Parse("Win+Alt+Shift+P");
            RefreshStatus_NoLock();
        }
    }

    private void RefreshStatus_NoLock()
    {
        var undo = _undoChord is null ? "off" : _undoChord.Display;
        var rewrite = _rewriteChord is null ? "off" : _rewriteChord.Display;
        var pause = _pauseChord is null ? "off" : _pauseChord.Display;
        var clip = _clipboardFixChord is null ? "off" : _clipboardFixChord.Display;
        var privacy = _privacyChord is null ? "off" : _privacyChord.Display;
        StatusMessage =
            $"Fix: {_fixChord.Display} · Undo: {undo} · Dictation: {_dictationChord.Display} · Rewrite: {rewrite} · Pause: {pause} · Clipboard: {clip} · Privacy: {privacy}";
    }

    private void MessageLoop()
    {
        try
        {
            _threadId = GetCurrentThreadId();

            var hMod = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, hMod, 0);
            if (_hook == IntPtr.Zero)
            {
                using var proc = Process.GetCurrentProcess();
                hMod = GetModuleHandle(proc.MainModule?.ModuleName);
                _hook = SetWindowsHookEx(WhKeyboardLl, _proc, hMod, 0);
            }

            if (_hook == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                IsRegistered = false;
                StatusMessage = $"Keyboard hook failed (Win32 {err}). Use tray → Fix now.";
                _ready.Set();
                return;
            }

            IsRegistered = true;
            lock (_chordGate) RefreshStatus_NoLock();
            _ready.Set();

            while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            IsRegistered = false;
            StatusMessage = "Hotkey thread error: " + ex.Message;
            _ready.Set();
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                // Ignore pure modifier key downs.
                if (IsModifierVk(info.vkCode))
                    return CallNextHookEx(_hook, nCode, wParam, lParam);

                var win = IsWinDown();
                var ctrl = IsCtrlDown();
                var alt = IsAltDown();
                var shift = IsShiftDown();
                var now = Environment.TickCount64;
                var fromAcInject = info.dwExtraInfo == SendInputText.InjectMarker;
                var injected = (info.flags & LlkhfInjected) != 0 || fromAcInject;

                HotkeyChord fix;
                HotkeyChord? undo;
                HotkeyChord dictation;
                HotkeyChord? rewrite;
                HotkeyChord? pause;
                HotkeyChord? clipboardFix;
                HotkeyChord? privacy;
                lock (_chordGate)
                {
                    fix = _fixChord;
                    undo = _undoChord;
                    dictation = _dictationChord;
                    rewrite = _rewriteChord;
                    pause = _pauseChord;
                    clipboardFix = _clipboardFixChord;
                    privacy = _privacyChord;
                }

                var isChord = false;
                if (fix.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    if (now - _lastFixTicks > 400)
                    {
                        _lastFixTicks = now;
                        try { FixPressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (undo is not null && undo.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    if (now - _lastUndoTicks > 400)
                    {
                        _lastUndoTicks = now;
                        try { UndoPressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (rewrite is not null && rewrite.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    if (now - _lastRewriteTicks > 400)
                    {
                        _lastRewriteTicks = now;
                        try { RewritePressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (pause is not null && pause.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    if (now - _lastPauseTicks > 400)
                    {
                        _lastPauseTicks = now;
                        try { PausePressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (clipboardFix is not null && clipboardFix.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    if (now - _lastClipboardFixTicks > 400)
                    {
                        _lastClipboardFixTicks = now;
                        try { ClipboardFixPressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (privacy is not null && privacy.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    if (now - _lastPrivacyTicks > 400)
                    {
                        _lastPrivacyTicks = now;
                        try { PrivacyPressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (dictation.Matches(info.vkCode, win, ctrl, alt, shift))
                {
                    isChord = true;
                    // Swallow accidental re-toggle while Voice Typing session is still arming.
                    var suppress = false;
                    try { suppress = ShouldSuppressDictationKey?.Invoke() == true; }
                    catch { /* ignore */ }
                    if (suppress)
                        return (IntPtr)1;

                    // Short debounce only (key-repeat). Accidental re-toggle while arming is
                    // handled by ShouldSuppressDictationKey (swallows Win+H for MinArmMs).
                    if (now - _lastDictationTicks > 350)
                    {
                        _lastDictationTicks = now;
                        try { WinHPressed?.Invoke(); } catch { /* ignore */ }
                    }
                }
                else if (info.vkCode == 0x0D && !win && !ctrl && !alt && !shift && !fromAcInject)
                {
                    // Bare Enter — notify dictation tracker (may dismiss lingering Voice Typing).
                    // Do not mark isChord; Enter must still reach the focused app (chat send, etc.).
                    try { EnterPressed?.Invoke(); } catch { /* ignore */ }
                }

                if (!isChord)
                {
                    var handler = TypingHandler;
                    if (handler is not null)
                    {
                        var ev = new TypingKeyEvent
                        {
                            VkCode = info.vkCode,
                            ScanCode = info.scanCode,
                            Shift = shift,
                            Ctrl = ctrl,
                            Alt = alt,
                            Win = win,
                            Injected = injected,
                            FromAutoCorrectInject = fromAcInject,
                            Character = ResolveCharacter(info.vkCode, shift)
                                       ?? ResolveUnicodeScan(info.vkCode, info.scanCode)
                        };
                        try
                        {
                            if (handler(ev))
                                return (IntPtr)1; // swallow — autocorrect will re-inject
                        }
                        catch
                        {
                            // never break the hook chain
                        }
                    }
                }
            }
        }
        catch
        {
            // never break the hook chain
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// KEYEVENTF_UNICODE SendInput delivers vk=0 with the UTF-16 code unit in scanCode.
    /// </summary>
    private static char? ResolveUnicodeScan(uint vk, uint scan)
    {
        if (vk != 0 || scan is 0 or > 0xFFFF)
            return null;
        var ch = (char)scan;
        return !char.IsControl(ch) || ch is '\t' or '\n' or '\r' ? ch : null;
    }

    /// <summary>
    /// US-layout-ish mapping without ToUnicode (avoids dead-key state corruption in the LL hook).
    /// Good enough for English word buffers + word-end punctuation.
    /// </summary>
    private static char? ResolveCharacter(uint vk, bool shift)
    {
        // A–Z
        if (vk is >= 0x41 and <= 0x5A)
        {
            var capsOn = (GetKeyState(0x14) & 1) != 0;
            var upper = capsOn ^ shift;
            var letter = (char)('A' + (vk - 0x41));
            return upper ? letter : char.ToLowerInvariant(letter);
        }

        // Main digits
        if (vk is >= 0x30 and <= 0x39)
        {
            if (!shift)
                return (char)vk;
            return vk switch
            {
                0x30 => ')',
                0x31 => '!',
                0x32 => '@',
                0x33 => '#',
                0x34 => '$',
                0x35 => '%',
                0x36 => '^',
                0x37 => '&',
                0x38 => '*',
                0x39 => '(',
                _ => null
            };
        }

        // Numpad digits (no shift variants needed)
        if (vk is >= 0x60 and <= 0x69)
            return (char)('0' + (vk - 0x60));

        return vk switch
        {
            0x20 => ' ',
            0x09 => '\t',
            0x0D => '\n',
            0xBA => shift ? ':' : ';',   // OEM_1
            0xBB => shift ? '+' : '=',   // OEM_PLUS
            0xBC => shift ? '<' : ',',   // OEM_COMMA
            0xBD => shift ? '_' : '-',   // OEM_MINUS
            0xBE => shift ? '>' : '.',   // OEM_PERIOD
            0xBF => shift ? '?' : '/',   // OEM_2
            0xC0 => shift ? '~' : '`',   // OEM_3
            0xDB => shift ? '{' : '[',   // OEM_4
            0xDC => shift ? '|' : '\\',  // OEM_5
            0xDD => shift ? '}' : ']',   // OEM_6
            0xDE => shift ? '"' : '\'',  // OEM_7
            _ => null
        };
    }

    private static bool IsModifierVk(uint vk) =>
        vk is VkMenu or VkControl or VkLControl or VkRControl or VkLWin or VkRWin
            or VkShift or VkLShift or VkRShift;

    private static bool IsAltDown() =>
        (GetAsyncKeyState(VkMenu) & 0x8000) != 0;

    private static bool IsCtrlDown() =>
        (GetAsyncKeyState(VkControl) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkLControl) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkRControl) & 0x8000) != 0;

    private static bool IsWinDown() =>
        (GetAsyncKeyState(VkLWin) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkRWin) & 0x8000) != 0;

    private static bool IsShiftDown() =>
        (GetAsyncKeyState(VkShift) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkLShift) & 0x8000) != 0 ||
        (GetAsyncKeyState(VkRShift) & 0x8000) != 0;

    public void Dispose()
    {
        _running = false;
        if (_threadId != 0)
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _ready.Dispose();
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
