using AutoCorrect.Core;
using AutoCorrect.Core.Autocorrect;
using AutoCorrect.Core.Settings;
using AutoCorrect.Core.Snippets;
using AutoCorrect.Platform.Windows;

namespace AutoCorrect.App.Services;

/// <summary>
/// Feeds LL key events into <see cref="AutocorrectEngine"/> and injects replacements.
/// Also detects manual backspace-corrections for <see cref="AutoLearnService"/>,
/// and expands intentional snippets (;sig) on a separate path.
/// Injection is always deferred off the hook thread for mass app compatibility (Electron/Chromium).
/// </summary>
public sealed class TypingAutocorrectService
{
    private readonly AutocorrectEngine _engine;
    private readonly WordBuffer _buffer = new();
    private readonly WordBuffer _snippetBuffer = new();
    private readonly SnippetStore _snippets = new();
    private SnippetSettings _snippetSettings = new();
    private long _lastInjectTicks;
    private AutoLearnService? _autoLearn;
    private bool _inSnippet;
    /// <summary>Last word-end char (for collapsing accidental double Space/comma).</summary>
    private char _lastEndChar;

    /// <summary>When true, skip as-you-type inject (Pause hotkey).</summary>
    public bool SoftPaused { get; set; }

    /// <summary>When true, skip as-you-type inject during an active Win+H Voice Typing session.</summary>
    public bool DictationHold { get; set; }

    // Manual-correction detection: peak word before a backspace streak → final word.
    private string _peakWord = "";
    private bool _correcting;

    public TypingAutocorrectService(AutocorrectEngine engine)
    {
        _engine = engine;
    }

    public SnippetStore Snippets => _snippets;
    public int SnippetCount => _snippets.Count;

    public void SetAutoLearn(AutoLearnService? autoLearn) => _autoLearn = autoLearn;

    public void ConfigureSnippets(SnippetSettings settings)
    {
        _snippetSettings = settings ?? new SnippetSettings();
        if (!_snippetSettings.Enabled)
        {
            _snippets.Clear();
            ExitSnippetMode();
            return;
        }

        try
        {
            var path = DataPaths.FindSnippets()
                       ?? DataPaths.FindOrCreateSnippetsPath();
            // Prefer named file under Data if configured.
            if (!string.IsNullOrWhiteSpace(_snippetSettings.FileName) &&
                !string.Equals(System.IO.Path.GetFileName(path), _snippetSettings.FileName, StringComparison.OrdinalIgnoreCase))
            {
                var alt = DataPaths.FindDataFilePublic(_snippetSettings.FileName)
                          ?? DataPaths.FindOrCreateDataFilePublic(_snippetSettings.FileName);
                path = alt;
            }
            _snippets.LoadFromFile(path);
        }
        catch
        {
            _snippets.Clear();
        }
    }

    /// <summary>Raised after a correction is queued (hook thread; marshal before touching UI).</summary>
    public event Action<AutocorrectAppliedEventArgs>? Applied;

    public bool HandleKey(TypingKeyEvent e)
    {
        // Always ignore our own correction inject (prevents feedback loops).
        if (e.FromAutoCorrectInject)
            return false;

        // Normal path: ignore all injected keys. Feature Debug Lab opens a gate so
        // SendInput typings (LLKHF_INJECTED, no InjectMarker) can exercise the hook.
        if (e.Injected && !DebugLabInjectGate.IsOpen)
            return false;

        // Ignore briefly after our own inject (belt + suspenders with InjectMarker).
        if (Environment.TickCount64 - _lastInjectTicks < 80)
            return false;

        if (SoftPaused || DictationHold)
        {
            ResetLearnState();
            ExitSnippetMode();
            _buffer.Clear();
            _lastEndChar = '\0';
            return false;
        }

        var snippetsOn = _snippetSettings.Enabled && _snippets.Loaded && _snippets.Count > 0;
        // AutoLearn can still observe even if autocorrect inject is off.
        var learn = _autoLearn?.Settings.Enabled == true;
        var autocorrectOn = _engine.IsEnabled;
        if (!autocorrectOn && !learn && !snippetsOn)
        {
            ResetLearnState();
            ExitSnippetMode();
            _buffer.Clear();
            _lastEndChar = '\0';
            return false;
        }

        // Ctrl/Alt/Win chords clear the word (Shift alone is fine for capitals).
        if (e.Ctrl || e.Alt || e.Win)
        {
            ResetLearnState();
            ExitSnippetMode();
            _buffer.Clear();
            return false;
        }

        if (e.IsBackspace)
        {
            if (_inSnippet)
            {
                if (_snippetBuffer.Length > 0)
                    _snippetBuffer.Backspace();
                if (_snippetBuffer.Length == 0)
                    ExitSnippetMode();
                return false;
            }

            if (_buffer.Length > 0)
            {
                if (!_correcting)
                {
                    _peakWord = _buffer.Current;
                    _correcting = true;
                }
                _buffer.Backspace();
            }
            else
            {
                ResetLearnState();
            }
            return false;
        }

        if (e.IsEscape || e.IsDelete || e.IsNavigation)
        {
            ResetLearnState();
            ExitSnippetMode();
            _buffer.Clear();
            return false;
        }

        if (e.Character is null)
        {
            if (e.VkCode is >= 0x70 and <= 0x87) // F1-F24
            {
                ResetLearnState();
                ExitSnippetMode();
                _buffer.Clear();
            }
            return false;
        }

        var ch = e.Character.Value;

        // —— Snippet path: ;sig + Space/Tab/Enter ——
        if (snippetsOn)
        {
            if (_inSnippet)
                return HandleSnippetKey(ch);

            // Starting a snippet: bare ';' with empty word buffer.
            if (ch == ';' && _buffer.Length == 0)
            {
                _inSnippet = true;
                _snippetBuffer.Clear();
                _snippetBuffer.Append(';');
                ResetLearnState();
                return false;
            }
        }

        if (AutocorrectEngine.IsWordEndChar(ch))
            return OnWordEnd(ch, autocorrectOn);

        if (AutocorrectEngine.IsWordChar(ch))
        {
            _lastEndChar = '\0';
            if (_buffer.Length < 64)
            {
                _buffer.Append(ch);
                if (!_correcting)
                    _peakWord = _buffer.Current;
            }
            else
            {
                ResetLearnState();
                _buffer.Clear();
            }
            return false;
        }

        _lastEndChar = '\0';
        ResetLearnState();
        _buffer.Clear();
        return false;
    }

    private bool HandleSnippetKey(char ch)
    {
        // Fire on Space / Tab / Enter only (not the full typo punctuation set).
        if (ch is ' ' or '\t' or '\n' or '\r')
            return OnSnippetEnd(ch);

        // Allow alnum + underscore after ';'
        if (char.IsLetterOrDigit(ch) || ch == '_')
        {
            if (_snippetBuffer.Length < 33)
                _snippetBuffer.Append(ch);
            else
                ExitSnippetMode();
            return false;
        }

        // Any other character cancels snippet mode.
        ExitSnippetMode();
        return false;
    }

    private bool OnSnippetEnd(char endChar)
    {
        var trigger = _snippetBuffer.Take();
        ExitSnippetMode();
        if (!_snippets.TryGet(trigger, out var expansion) ||
            string.Equals(trigger, expansion, StringComparison.Ordinal))
            return false;

        return EnqueueReplace(trigger, expansion, endChar, source: "snippet");
    }

    private bool OnWordEnd(char endChar, bool autocorrectEnabled)
    {
        var finalWord = _buffer.Take();
        var peak = _peakWord;
        var wasCorrecting = _correcting;
        ResetLearnState();

        if (wasCorrecting &&
            learnEnabled() &&
            !string.IsNullOrWhiteSpace(peak) &&
            !string.IsNullOrWhiteSpace(finalWord) &&
            !string.Equals(peak, finalWord, StringComparison.OrdinalIgnoreCase))
        {
            try { _autoLearn?.RecordOccurrence(peak, finalWord); }
            catch { /* never break typing */ }
        }

        // Accidental repeat terminator between words: "word  " / "word,," → eat the extra.
        // Live path can't see the whole paragraph; Space/comma doubles are near-always slips
        // mid-typing. Sentence ".." / "!!" left alone (ellipsis / emphasis).
        if (autocorrectEnabled &&
            finalWord.Length == 0 &&
            IsLiveCollapsibleRepeat(endChar) &&
            endChar == _lastEndChar)
        {
            EnqueueCollapseExtraChar();
            // Keep _lastEndChar so a third Space also collapses.
            return false;
        }

        _lastEndChar = endChar;

        if (!autocorrectEnabled || finalWord.Length == 0)
            return false;

        return TryCorrectAndInject(finalWord, endChar);
    }

    private static bool IsLiveCollapsibleRepeat(char ch) => ch is ' ' or ',';

    private void EnqueueCollapseExtraChar()
    {
        IntPtr targetHwnd;
        try { targetHwnd = ForegroundProcess.GetForegroundWindowSafe(); }
        catch { targetHwnd = IntPtr.Zero; }

        _lastInjectTicks = Environment.TickCount64;
        InputInjector.EnqueueBackspaces(1, delayMs: 5, targetHwnd: targetHwnd);
    }

    private bool learnEnabled() => _autoLearn?.Settings.Enabled == true;

    private void ResetLearnState()
    {
        _peakWord = "";
        _correcting = false;
    }

    private void ExitSnippetMode()
    {
        _inSnippet = false;
        _snippetBuffer.Clear();
    }

    private bool TryCorrectAndInject(string word, char endChar)
    {
        string? process = null;
        try { process = ForegroundProcess.GetProcessName(); }
        catch { /* ignore */ }

        var match = _engine.TryMatch(word, process);
        if (match is null)
            return false;

        var replacement = match.CasedReplacement;
        if (string.Equals(word, replacement, StringComparison.Ordinal))
            return false;

        return EnqueueReplace(word, replacement, endChar, match.Source);
    }

    private bool EnqueueReplace(string trigger, string replacement, char endChar, string source)
    {
        // Never SendInput inside the LL hook — defer so Cursor/Chrome/Edge/Slack/etc. accept it.
        IntPtr targetHwnd;
        try { targetHwnd = ForegroundProcess.GetForegroundWindowSafe(); }
        catch { targetHwnd = IntPtr.Zero; }

        _lastInjectTicks = Environment.TickCount64;
        var trig = trigger;
        var repl = replacement;
        var end = endChar;
        InputInjector.EnqueueReplaceWord(
            typedLength: trigger.Length,
            replacement: replacement,
            endChar: endChar,
            endCharAlreadyTyped: true,
            // Keep tiny settle delay for Electron; large sleeps blow the <100ms budget.
            delayMs: 5,
            targetHwnd: targetHwnd,
            onCompleted: ok =>
            {
                if (!ok)
                    return;
                _lastInjectTicks = Environment.TickCount64;
                try
                {
                    Applied?.Invoke(new AutocorrectAppliedEventArgs(trig, repl, end, source));
                }
                catch { /* never break injector */ }
            });

        return false; // do not swallow — end char lands, then deferred replace
    }
}

public sealed class AutocorrectAppliedEventArgs(
    string trigger,
    string replacement,
    char endChar,
    string source) : EventArgs
{
    public string Trigger { get; } = trigger;
    public string Replacement { get; } = replacement;
    public char EndChar { get; } = endChar;
    public string Source { get; } = source;
    public string Before => Trigger + EndChar;
    public string After => Replacement + EndChar;
}
