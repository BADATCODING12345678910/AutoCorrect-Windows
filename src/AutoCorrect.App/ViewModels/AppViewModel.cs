using System.Diagnostics;
using System.Windows.Automation;
using AutoCorrect.App.Services;
using AutoCorrect.App.Themes;
using AutoCorrect.App.Views;
using AutoCorrect.Core;
using AutoCorrect.Core.Autocorrect;
using AutoCorrect.Core.Correction;
using AutoCorrect.Core.Licensing;
using AutoCorrect.Core.LanguagePacks;
using AutoCorrect.Core.Net;
using AutoCorrect.Core.Privacy;
using AutoCorrect.Core.Settings;
using AutoCorrect.Core.Stats;
using AutoCorrect.Core.Updates;
using AutoCorrect.Platform.Abstractions;
using AutoCorrect.Platform.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

/// <summary>Headless controller: hotkey/tray → Grammarly-style one-suggestion review card.</summary>
public partial class AppViewModel : ViewModelBase, IDisposable
{
    private readonly CorrectionPipeline _pipeline = new();
    private readonly UiaTextBridge _textBridge = new();
    private readonly ExternalFocusTracker _focusTracker = new();
    private readonly GlobalHotkey _hotkey = new();
    private readonly WinHDictationSession _dictation;
    private readonly SettingsStore _settingsStore = new();
    private readonly AutocorrectEngine _autocorrect = new();
    private readonly AutoLearnService _autoLearn = new();
    private readonly LearnContributionStore _learnContribution = new();
    private readonly LicenseService _license = new();
    private readonly UpdateService _updates = new();
    private readonly PrivacyModeService _privacy = new();
    private readonly UsageStatsStore _stats = new();
    private readonly LanguagePackService _languagePacks = new();
    private int _packEnsureBusy;
    private DispatcherTimer? _privacyTimer;
    private bool _privacyFixToastShown;
    private bool _perfectFixNoKeyToastShown;
    private int _updateCheckBusy;
    private UsageStatsWindow? _statsWindow;
    private WhatsNewWindow? _whatsNewWindow;
    private CancellationTokenSource? _dictationFixCts;
    private readonly Stack<(string Find, string Replace)> _localRuleUndo = new();
    private readonly TypingAutocorrectService _typingAutocorrect;
    private AutoLearnPromptWindow? _autoLearnPrompt;
    private WordManagerWindow? _wordManagerWindow;
    private SnippetManagerWindow? _snippetManagerWindow;
    private int _autoLearnPromptBusy;
    private readonly List<CorrectionHunk> _queue = [];
    private readonly Stack<UndoEntry> _undoStack = new();
    private readonly DismissedSuggestionsStore _dismissed = new();
    private AppSettings _settings;
    private string _workingText = "";
    private AutomationElement? _targetElement;
    private int _targetProcessId;
    private Window? _reviewWindow;
    private UnderlineOverlayWindow? _underline;
    private SettingsWindow? _settingsWindow;
    private RewritePromptWindow? _rewriteWindow;
    private readonly ChatGptCorrectionClient _chatGpt = new();
    private int? _pendingCaretX;
    private int? _pendingCaretY;
    private nint _pendingWindowHwnd;
    private nint _pendingControlHwnd;

    private sealed record UndoEntry(
        string BeforeText,
        string AfterText,
        AutomationElement? Target,
        string Label,
        bool Keystroke = false);

    [ObservableProperty]
    private HunkItemViewModel? _current;

    [ObservableProperty]
    private string _header = "AutoCorrect";

    [ObservableProperty]
    private string _categoryLabel = "Review suggestions";

    [ObservableProperty]
    private string _categoryBadge = "SUGGESTION";

    [ObservableProperty]
    private string _whyText = "";

    [ObservableProperty]
    private string _whyTooltip = "";

    [ObservableProperty]
    private string _confidenceChip = "";

    [ObservableProperty]
    private bool _hasWhyText;

    [ObservableProperty]
    private bool _hasConfidenceChip;

    [ObservableProperty]
    private string _positionLabel = "";

    [ObservableProperty]
    private bool _hasMultiple;

    [ObservableProperty]
    private string _acceptAllLabel = "Accept all";

    [ObservableProperty]
    private bool _canGoPrev;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _canAddToDictionary;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyStatus = "";

    private int _index;

    public event Action<string>? ToastRequested;
    public event Action<string, int, string?, Action?>? ToastActionRequested;
    /// <summary>Sticky toast progress: message, fraction 0–1 or null indeterminate, optional detail.</summary>
    public event Action<string, double?, string?>? UpdateProgressRequested;
    public event Action? UpdateProgressFinished;

    public AppViewModel()
    {
        _settings = _settingsStore.Load();
        ThemeService.Apply(_settings.General.ThemeId);
        _typingAutocorrect = new TypingAutocorrectService(_autocorrect);
        _dictation = new WinHDictationSession(_hotkey, _settings.Dictation.AutoDelayMs);
        ApplyHotkeySettings(_settings);
        _autocorrect.Configure(_settings.Autocorrect);
        ConfigureAutoLearn(_settings);
        _typingAutocorrect.ConfigureSnippets(_settings.Snippets);
        _ = EnsureLanguagePackAsync(_settings.Grammar.Language, quiet: true);
        try { _learnContribution.Load(); } catch { /* optional */ }
        _hotkey.TypingHandler = _typingAutocorrect.HandleKey;
        _typingAutocorrect.Applied += OnTypingAutocorrectApplied;
        _autoLearn.PromotionReady += OnAutoLearnPromotionReady;
        RunUpdateBootstrap();
        ScheduleStartupUpdateCheck();
        try
        {
            var llm = PersonalApiKeyStore.TryLoad();
            NetworkWarmup.Start(llm?.Provider, llm?.BaseUrl);
        }
        catch
        {
            NetworkWarmup.Start();
        }
        _hotkey.PausePressed += () =>
            Dispatcher.UIThread.Post(ToggleSoftPause);
        _hotkey.FixPressed += () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (!HotkeyFixAllowed())
                {
                    Toast("Dictation Fix is disabled in Settings.");
                    return;
                }
                if (!IsBusy)
                    _ = FixNowAsync();
            });
        _hotkey.UndoPressed += () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsBusy)
                    _ = UndoLastAsync();
            });
        _hotkey.RewritePressed += () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsBusy)
                    _ = RewriteSelectionAsync();
            });
        _hotkey.ClipboardFixPressed += () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsBusy)
                    _ = ClipboardFixAsync();
            });
        _hotkey.PrivacyPressed += () =>
            Dispatcher.UIThread.Post(PrivacyToggleDefault);
        _dictation.SessionStarted += OnDictationSessionStarted;
        _dictation.SessionEnded += OnDictationSessionEnded;
        ApplyDictationWatchSettings();
        _focusTracker.Start();
        _focusTracker.Changed += OnExternalFocusChanged;
        try
        {
            _dismissed.LoadFromFile(DataPaths.FindOrCreateDismissedSuggestionsPath());
        }
        catch { /* optional */ }
        _privacy.Changed += OnPrivacyChanged;
        StartPrivacyTimer();
        try
        {
            if (_privacy.TryRestore())
                Toast(_privacy.RestoredToast());
        }
        catch { /* optional */ }
        try
        {
            _stats.Load(System.IO.Path.Combine(DataPaths.LocalAppDataDir(), "usage-stats.json"));
        }
        catch { /* optional */ }
        RefreshHotkeyTooltip();
        ScheduleWeeklyDigestCheck();
        _ = SyncLicenseInBackgroundAsync();
    }

    public UsageStatsStore Stats => _stats;
    public double ReviewCardScale =>
        _settings.General.ReviewCardScale is >= 1.0 and <= 1.5
            ? _settings.General.ReviewCardScale
            : 1.0;

    private void RecordStat(Action<UsageStatsStore> action)
    {
        if (!_settings.General.CollectUsageStats)
            return;
        try { action(_stats); }
        catch { /* never break UX */ }
    }

    private void ScheduleWeeklyDigestCheck()
    {
        if (!_settings.General.CollectUsageStats || !_settings.General.ShowWeeklyDigest)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(8000).ConfigureAwait(false);
                if (!_settings.General.CollectUsageStats || !_settings.General.ShowWeeklyDigest)
                    return;
                if (!_stats.ShouldShowWeeklyDigest())
                    return;
                var msg = _stats.FormatWeeklyDigest();
                Dispatcher.UIThread.Post(() =>
                {
                    ToastAction(msg, 12000, "Stats…", () => OpenUsageStats());
                    try { _stats.MarkDigestShown(); } catch { /* ignore */ }
                });
            }
            catch { /* ignore */ }
        });
    }

    public PrivacyModeService Privacy => _privacy;

    private void StartPrivacyTimer()
    {
        _privacyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _privacyTimer.Tick += (_, _) =>
        {
            if (_privacy.Tick() || _privacy.IsActive)
                RefreshHotkeyTooltip();
        };
        _privacyTimer.Start();
    }

    private void OnPrivacyChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_privacy.IsActive)
                _privacyFixToastShown = false;
            ThemeLogoApplier.SetPrivacyBadge(_privacy.IsActive);
            RefreshHotkeyTooltip();
        });
    }

    private async Task SyncLicenseInBackgroundAsync()
    {
        try
        {
            await _license.RefreshOnlineAsync().ConfigureAwait(false);
            Dispatcher.UIThread.Post(RefreshHotkeyTooltip);
        }
        catch { /* offline OK */ }
    }

    private void OnTypingAutocorrectApplied(AutocorrectAppliedEventArgs e)
    {
        // Hook thread — marshal undo bookkeeping to UI.
        Dispatcher.UIThread.Post(() =>
        {
            var isSnippet = string.Equals(e.Source, "snippet", StringComparison.OrdinalIgnoreCase);
            var label = isSnippet
                ? "snippet " + e.Trigger
                : "autocorrect " + e.Trigger + "→" + e.Replacement;
            PushUndo(e.Before, e.After, label, keystroke: true);
            if (isSnippet)
                RecordStat(s => s.RecordSnippet());
            else
                RecordStat(s => s.RecordAutocorrect());
            if (_settings.General.ShowAutocorrectUndoToast)
            {
                var msg = isSnippet
                    ? $"Expanded {e.Trigger}"
                    : $"Corrected {e.Trigger} → {e.Replacement}";
                ToastAction(
                    msg,
                    ms: 4500,
                    actionLabel: "Undo",
                    action: () => _ = UndoLastAsync());
            }
        });
    }

    public SettingsStore SettingsStore => _settingsStore;
    public AutocorrectEngine Autocorrect => _autocorrect;
    public LicenseService License => _license;
    public bool ShowStartupToast => _settings.General.ShowStartupToast;

    [ObservableProperty]
    private string _trayToolTip = "AutoCorrect";

    public string ReportHotkeyStatus()
    {
        _hotkey.WaitUntilReady(3000);
        var dictation = _settings.Dictation.Enabled &&
                        _settings.Dictation.AutoWatchVoiceTyping &&
                        _settings.Dictation.UseWinHHook
            ? $"dictation auto ({_settings.General.DictationHotkey})"
            : "dictation auto off";
        var ac = !_settings.Autocorrect.Enabled
            ? "autocorrect off"
            : _settings.Autocorrect.UseImportedLibrary
                ? $"autocorrect ON ({_autocorrect.CuratedCount} curated, {_autocorrect.ImportedCount} imported)"
                : $"autocorrect ON ({_autocorrect.CuratedCount} curated)";
        return _hotkey.StatusMessage + " · " + dictation + " · " + ac;
    }

    private void OnDictationSessionStarted(object? sender, EventArgs e)
    {
        _typingAutocorrect.DictationHold = true;
        // New utterance — cancel a pending auto-Fix from the previous chunk.
        CancelPendingDictationFix();
    }

    private void OnDictationSessionEnded(object? sender, DictationSessionEndedEventArgs e)
    {
        _typingAutocorrect.DictationHold = false;

        if (!_settings.Dictation.Enabled)
            return;
        var mode = _settings.Dictation.TriggerMode;
        if (mode is not ("auto" or "both"))
            return;

        // Ghost sessions (UI flicker / never armed) — don't toast or steal focus.
        if (!e.VoiceUiWasSeen && e.ElapsedMs < Math.Max(2000, _settings.Dictation.MinArmMs))
            return;

        // ui_closed already waited for the mic bar — use a short cap; win_h keeps full AutoDelayMs.
        var delay = Math.Max(0, e.AutoDelayMs > 0 ? e.AutoDelayMs : _settings.Dictation.AutoDelayMs);
        if (string.Equals(e.Reason, "ui_closed", StringComparison.OrdinalIgnoreCase))
            delay = Math.Min(delay, Math.Max(0, _settings.Dictation.UiClosedDelayMs));

        // Quiet window so multi-utterance (end → quickly start again) coalesces into one Fix.
        delay = Math.Max(delay, Math.Max(0, _settings.Dictation.MultiUtteranceQuietMs));

        _pendingCaretX = e.CaretScreenX;
        _pendingCaretY = e.CaretScreenY;
        _pendingWindowHwnd = e.TargetWindowHandle;
        _pendingControlHwnd = e.TargetControlHandle;

        CancelPendingDictationFix();
        var cts = new CancellationTokenSource();
        _dictationFixCts = cts;
        var token = cts.Token;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (IsBusy || (_reviewWindow is { IsVisible: true } && _queue.Count > 0))
                    return;

                Toast(delay >= 1500
                    ? "Dictation ended — waiting in case you continue…"
                    : "Dictation ended — fixing…");
                if (delay > 0)
                    await Task.Delay(delay, token).ConfigureAwait(true);

                if (token.IsCancellationRequested || _dictation.IsSessionActive)
                    return;

                if (IsBusy || (_reviewWindow is { IsVisible: true } && _queue.Count > 0))
                    return;

                if (_settings.Dictation.SkipAutoFixIfFocusMoved &&
                    FocusMovedAwayFromDictationTarget())
                {
                    _pendingWindowHwnd = 0;
                    _pendingControlHwnd = 0;
                    Toast("Focus moved — press " + _settings.General.FixHotkey + " to Fix.");
                    return;
                }

                if (!IsBusy)
                    await FixNowAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                /* next utterance started */
            }
            finally
            {
                if (ReferenceEquals(_dictationFixCts, cts))
                    _dictationFixCts = null;
                cts.Dispose();
            }
        });
    }

    private void CancelPendingDictationFix()
    {
        try
        {
            _dictationFixCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }
    }

    private bool FocusMovedAwayFromDictationTarget()
    {
        try
        {
            if (_pendingWindowHwnd == 0)
                return false;
            var fg = ForegroundProcess.GetForegroundWindowSafe();
            if (fg == IntPtr.Zero)
                return false;
            // Still on session window or its control — OK.
            if (fg == (IntPtr)_pendingWindowHwnd || fg == (IntPtr)_pendingControlHwnd)
                return false;
            // Same process as session window — OK (child HWND / Electron).
            if (TryGetWindowProcessId((IntPtr)_pendingWindowHwnd, out var sessionPid) &&
                TryGetWindowProcessId(fg, out var fgPid) &&
                sessionPid == fgPid &&
                sessionPid != 0)
                return false;
            // Voice Typing / shell hosts don't count as "moved to another app".
            var name = ForegroundProcess.GetProcessName() ?? "";
            if (name.Contains("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetWindowProcessId(IntPtr hwnd, out int pid)
    {
        pid = 0;
        if (hwnd == IntPtr.Zero)
            return false;
        try
        {
            _ = GetWindowThreadProcessId(hwnd, out pid);
            return pid != 0;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    private bool HotkeyFixAllowed()
    {
        if (!_settings.Dictation.Enabled)
            return false;
        var mode = _settings.Dictation.TriggerMode;
        return mode is "hotkey" or "both";
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        ThemeService.Apply(settings.General.ThemeId);
        _dictation.AutoDelayMs = settings.Dictation.AutoDelayMs;
        _dictation.MinArmMs = settings.Dictation.MinArmMs;
        _dictation.UiGoneTicksRequired = settings.Dictation.UiGoneTicksRequired;
        ApplyHotkeySettings(settings);
        ApplyDictationWatchSettings();
        _autocorrect.Configure(settings.Autocorrect);
        ConfigureAutoLearn(settings);
        _typingAutocorrect.ConfigureSnippets(settings.Snippets);
        ApplyReviewCardScale();
        RefreshHotkeyTooltip();
        var ac = settings.Autocorrect.Enabled
            ? $"Autocorrect on ({_autocorrect.CuratedCount} curated, {_autocorrect.LearnedCount} learned)"
            : "Autocorrect off";
        var snip = settings.Snippets.Enabled
            ? $", snippets {_typingAutocorrect.SnippetCount}"
            : "";
        Toast("Settings saved. " + ac + snip + ". Hotkeys: " + settings.General.FixHotkey);
        _ = EnsureLanguagePackAsync(settings.Grammar.Language, quiet: false);
    }

    /// <summary>
    /// Download (if needed) and activate the as-you-type pack for a grammar language.
    /// en-US is bundled; other locales fetch on demand.
    /// </summary>
    private async Task EnsureLanguagePackAsync(string? languageCode, bool quiet)
    {
        if (Interlocked.CompareExchange(ref _packEnsureBusy, 1, 0) != 0)
            return;

        try
        {
            var progress = new Progress<string>(msg =>
            {
                if (!quiet)
                    Dispatcher.UIThread.Post(() => Toast(msg));
            });

            var (ok, message, resolved) = await _languagePacks
                .EnsurePackAsync(languageCode, progress)
                .ConfigureAwait(true);

            ApplyInstalledPackToEngine(resolved);

            if (!quiet || !ok)
                Toast(message);
        }
        catch (Exception ex)
        {
            if (!quiet)
                Toast("Language pack: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _packEnsureBusy, 0);
        }
    }

    private void ApplyInstalledPackToEngine(string resolvedCode)
    {
        var curated = _languagePacks.FindCuratedPath(resolvedCode);
        var wordlist = _languagePacks.FindWordlistPath(resolvedCode);
        _autocorrect.ApplyLanguagePack(resolvedCode, _settings.Autocorrect, curated, wordlist);
    }

    private void ConfigureAutoLearn(AppSettings settings)
    {
        // Ensure wordlist is available for learn filters even when dictionary distance is off.
        if (!settings.Autocorrect.UseDictionaryDistance && _autocorrect.DictionaryCount == 0)
        {
            try
            {
                var path = DataPaths.FindWordlist(settings.Autocorrect.WordlistFile);
                if (path is not null)
                    _autocorrect.Dictionary.LoadFromFile(path);
            }
            catch
            {
                // optional filter aid
            }
        }

        _autoLearn.Configure(settings.AutoLearn, _autocorrect.Learned, _autocorrect.Dictionary);
        _typingAutocorrect.SetAutoLearn(settings.AutoLearn.Enabled ? _autoLearn : null);
    }

    private void OnAutoLearnPromotionReady(PendingPromotion promo)
    {
        Dispatcher.UIThread.Post(() => _ = HandleAutoLearnPromotionAsync(promo));
    }

    private async Task HandleAutoLearnPromotionAsync(PendingPromotion _)
    {
        // Drain queue one at a time.
        if (Interlocked.Exchange(ref _autoLearnPromptBusy, 1) == 1)
            return;

        try
        {
            while (true)
            {
                var current = _autoLearn.DequeuePending();
                if (current is null)
                    break;

                if (string.Equals(current.Mode, "silent", StringComparison.OrdinalIgnoreCase))
                {
                    if (_autoLearn.Accept(current))
                    {
                        _autocorrect.ReloadLearned();
                        try { _learnContribution.RecordAccepted(current.Trigger, current.Replacement); }
                        catch { /* ignore */ }
                        Toast($"Auto-learned {current.Trigger} → {current.Replacement}");
                    }
                    continue;
                }

                _autoLearnPrompt?.Close();
                _autoLearnPrompt = new AutoLearnPromptWindow();
                ThemeLogoApplier.ApplyToWindow(_autoLearnPrompt);
                _autoLearnPrompt.ShowPromotion(current);
                var choice = await _autoLearnPrompt.WaitAsync().ConfigureAwait(true);
                switch (choice)
                {
                    case AutoLearnPromptWindow.Choice.Accept:
                        if (_autoLearn.Accept(current))
                        {
                            _autocorrect.ReloadLearned();
                            try { _learnContribution.RecordAccepted(current.Trigger, current.Replacement); }
                            catch { /* ignore */ }
                            Toast($"Learned {current.Trigger} → {current.Replacement}");
                        }
                        break;
                    case AutoLearnPromptWindow.Choice.Never:
                        _autoLearn.Never(current);
                        Toast("Won't learn " + current.Trigger);
                        break;
                    default:
                        _autoLearn.Skip(current);
                        break;
                }

                _autoLearnPrompt = null;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _autoLearnPromptBusy, 0);
        }
    }

    [RelayCommand]
    private void UndoLastAutoLearn()
    {
        var undone = _autoLearn.UndoLast();
        if (undone is null)
        {
            Toast("No auto-learned pair to undo.");
            return;
        }

        _autocorrect.ReloadLearned();
        Toast($"Removed learned {undone.Value.Trigger} → {undone.Value.Replacement}");
    }

    [RelayCommand]
    private void OpenWordManager()
    {
        if (_wordManagerWindow is { IsVisible: true })
        {
            _wordManagerWindow.Activate();
            return;
        }

        _wordManagerWindow = new WordManagerWindow();
        ThemeLogoApplier.ApplyToWindow(_wordManagerWindow);
        var vm = new WordManagerViewModel(
            _autocorrect,
            _autoLearn,
            () => RefreshHotkeyTooltip(),
            _learnContribution,
            _wordManagerWindow);
        _wordManagerWindow.DataContext = vm;
        _wordManagerWindow.Closed += (_, _) => _wordManagerWindow = null;
        _wordManagerWindow.Show();
        _wordManagerWindow.Activate();
    }

    [RelayCommand]
    private void OpenSnippetManager()
    {
        if (_snippetManagerWindow is { IsVisible: true })
        {
            _snippetManagerWindow.Activate();
            return;
        }

        _snippetManagerWindow = new SnippetManagerWindow();
        ThemeLogoApplier.ApplyToWindow(_snippetManagerWindow);
        var path = DataPaths.FindSnippets() ?? DataPaths.FindOrCreateSnippetsPath();
        var vm = new SnippetManagerViewModel(path, () =>
        {
            _typingAutocorrect.ConfigureSnippets(_settings.Snippets);
            Toast($"Snippets reloaded ({_typingAutocorrect.SnippetCount} enabled).");
        });
        _snippetManagerWindow.DataContext = vm;
        _snippetManagerWindow.Closed += (_, _) => _snippetManagerWindow = null;
        _snippetManagerWindow.Show();
        _snippetManagerWindow.Activate();
    }

    private void ApplyHotkeySettings(AppSettings settings) =>
        _hotkey.SetChords(
            settings.General.FixHotkey,
            settings.General.UndoHotkey,
            settings.General.DictationHotkey,
            settings.General.RewriteHotkey,
            settings.General.PauseHotkey,
            settings.General.ClipboardFixHotkey,
            settings.General.PrivacyHotkey);

    private void RefreshHotkeyTooltip()
    {
        var lic = _license.Refresh();
        var pause = _typingAutocorrect.SoftPaused ? " · PAUSED" : "";
        var privacy = _privacy.StatusSuffix();
        TrayToolTip = "AutoCorrect — Fix " + _settings.General.FixHotkey + pause + privacy + " · " + lic.StatusLine;
    }

    [RelayCommand]
    private void Privacy15() => EnablePrivacy(15);

    [RelayCommand]
    private void Privacy30() => EnablePrivacy(30);

    [RelayCommand]
    private void Privacy60() => EnablePrivacy(60);

    [RelayCommand]
    private void Privacy120() => EnablePrivacy(120);

    [RelayCommand]
    private void PrivacyUntilOff() => EnablePrivacy(0);

    [RelayCommand]
    private void PrivacyEnd()
    {
        if (!_privacy.IsActive)
        {
            Toast("Privacy mode is already off.");
            return;
        }

        _privacy.Disable();
        Toast(_privacy.StatusToast());
    }

    [RelayCommand]
    private void PrivacyToggleDefault()
    {
        if (_privacy.IsActive)
            PrivacyEnd();
        else
            EnablePrivacy(_settings.General.PrivacyDefaultMinutes);
    }

    private void EnablePrivacy(int minutes)
    {
        _privacy.Enable(minutes);
        Toast(_privacy.StatusToast());
    }

    private void RunUpdateBootstrap()
    {
        try
        {
            var result = UpdateMigration.Run();
            if (!result.HealthOk)
                Dispatcher.UIThread.Post(() => Toast(result.HealthMessage));
            else if (result.JustUpdated)
            {
                var notes = string.IsNullOrWhiteSpace(result.PendingNotes)
                    ? $"What's new in {result.CurrentVersion}\nYou're on the latest build."
                    : result.PendingNotes!;
                Dispatcher.UIThread.Post(() =>
                    ToastAction(
                        notes.Replace("\r\n", "\n"),
                        12000,
                        "Details…",
                        () => OpenWhatsNew()));
            }
        }
        catch
        {
            /* never block startup */
        }
    }

    private void ToggleSoftPause()
    {
        _typingAutocorrect.SoftPaused = !_typingAutocorrect.SoftPaused;
        RefreshHotkeyTooltip();
        Toast(_typingAutocorrect.SoftPaused
            ? "Autocorrect paused (" + _settings.General.PauseHotkey + " to resume)."
            : "Autocorrect resumed.");
    }

    private bool EnsurePremiumAi(string featureLabel)
    {
        var info = _license.Refresh();
        if (info.AllowsPremiumAi)
            return true;
        Toast(featureLabel + " needs a license (trial ended). Tray → Settings → License. Core Fix + autocorrect still work.");
        return false;
    }

    private void ApplyDictationWatchSettings()
    {
        _dictation.AutoDelayMs = _settings.Dictation.AutoDelayMs;
        _dictation.MinArmMs = _settings.Dictation.MinArmMs;
        _dictation.UiGoneTicksRequired = _settings.Dictation.UiGoneTicksRequired;
        var watch = _settings.Dictation.Enabled &&
                    _settings.Dictation.AutoWatchVoiceTyping &&
                    _settings.Dictation.UseWinHHook &&
                    _settings.Dictation.TriggerMode is "auto" or "both";
        if (watch)
            _dictation.Start();
        else
        {
            _dictation.Stop();
            _typingAutocorrect.DictationHold = false;
        }
    }

    [RelayCommand]
    private void OpenSettings() => ShowSettingsWindow();

    [RelayCommand]
    private void OpenLicense() => ShowSettingsWindow();

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(
            _settingsStore,
            ApplySettings,
            () => OpenWordManager(),
            _license,
            _autocorrect,
            _learnContribution,
            privacy: _privacy,
            openSnippetManager: () => OpenSnippetManager(),
            openUsageStats: () => OpenUsageStats());
        _settingsWindow = new SettingsWindow { DataContext = vm };
        ThemeLogoApplier.ApplyToWindow(_settingsWindow);
        vm.AttachHostWindow(_settingsWindow);
        _settingsWindow.Closed += (_, _) =>
        {
            vm.Detach();
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Phase D — highlight text → NL instruction → replace selection via ChatGPT.</summary>
    [RelayCommand]
    private async Task RewriteSelectionAsync()
    {
        if (IsBusy)
            return;

        if (_privacy.IsActive)
        {
            Toast("Rewrite is blocked in Privacy mode (no cloud). Tray → Privacy → End privacy mode.");
            return;
        }

        if (!EnsurePremiumAi("Rewrite"))
            return;

        if (_rewriteWindow is { IsVisible: true })
        {
            _rewriteWindow.Activate();
            return;
        }

        // Capture while the target app still has focus (before our UI steals it).
        var preferred = _focusTracker.GetLastExternal();
        if (!SelectionClipboard.TryCaptureSelection(preferred, out var selected, out var previousClip) ||
            string.IsNullOrWhiteSpace(selected))
        {
            Toast("Select some text first, then " + _settings.General.RewriteHotkey + ".");
            SelectionClipboard.RestoreClipboard(previousClip);
            return;
        }

        var creds = PersonalApiKeyStore.TryLoad();
        if (creds?.ApiKey is null || PersonalApiKeyStore.LooksLikePlaceholder(creds.ApiKey))
        {
            SelectionClipboard.RestoreClipboard(previousClip);
            Toast("Add an AI API key in Settings → Grammar to rewrite.");
            return;
        }

        var target = preferred ?? AutomationElement.FocusedElement;
        string? fieldBefore = null;
        if (target is not null)
        {
            var read = _textBridge.TryReadElement(target);
            if (read.Success)
                fieldBefore = read.Text;
        }

        var model = !string.IsNullOrWhiteSpace(_settings.Grammar.OpenAiModel)
            ? _settings.Grammar.OpenAiModel.Trim()
            : (creds.Model ?? "gpt-4o-mini");

        var promptVm = new RewritePromptViewModel
        {
            SelectedPreview = selected.Length > 1200 ? selected[..1200] + "…" : selected,
            Instruction = _settings.General.LastRewriteInstruction ?? ""
        };

        TaskCompletionSource<string?>? pending = null;
        promptVm.CancelRequested += () => pending?.TrySetResult(null);
        promptVm.SubmitRequested += () =>
        {
            if (promptVm.IsBusy || string.IsNullOrWhiteSpace(promptVm.Instruction))
                return;
            pending?.TrySetResult(promptVm.Instruction.Trim());
        };

        _rewriteWindow = new RewritePromptWindow { DataContext = promptVm };
        ThemeLogoApplier.ApplyToWindow(_rewriteWindow);
        _rewriteWindow.Closed += (_, _) =>
        {
            pending?.TrySetResult(null);
            _rewriteWindow = null;
        };
        _rewriteWindow.Show();
        _rewriteWindow.Activate();

        IsBusy = true;
        try
        {
            while (_rewriteWindow is { IsVisible: true })
            {
                pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var instruction = await pending.Task.ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(instruction))
                {
                    SelectionClipboard.RestoreClipboard(previousClip);
                    _rewriteWindow?.Close();
                    return;
                }

                promptVm.IsBusy = true;
                promptVm.StatusMessage = "Rewriting with " + model + "…";

                var result = await _chatGpt
                    .RewriteDetailedAsync(selected, instruction, creds.ApiKey, model, creds.Provider, creds.BaseUrl)
                    .ConfigureAwait(true);

                promptVm.IsBusy = false;
                if (!result.Ok)
                {
                    promptVm.StatusMessage = result.Error ?? "Rewrite failed.";
                    Toast(promptVm.StatusMessage);
                    continue; // keep window open — user can retry or cancel
                }

                var rewritten = result.Text;
                if (string.IsNullOrWhiteSpace(rewritten))
                {
                    promptVm.StatusMessage = "OpenAI returned empty text.";
                    Toast(promptVm.StatusMessage);
                    continue;
                }

                _settings.General.LastRewriteInstruction = instruction;
                _settingsStore.Save(_settings);

                _rewriteWindow?.Close();
                _rewriteWindow = null;

                if (target is not null)
                {
                    try
                    {
                        target.SetFocus();
                        await Task.Delay(80).ConfigureAwait(true);
                    }
                    catch
                    {
                        // clipboard paste may still work if selection survived
                    }
                }

                if (!SelectionClipboard.TryPasteOverSelection(rewritten, previousClip))
                {
                    Toast("Couldn't paste the rewrite into the field.");
                    return;
                }

                _targetElement = target;
                if (fieldBefore is not null && target is not null)
                {
                    var afterRead = _textBridge.TryReadElement(target);
                    var fieldAfter = afterRead.Success ? afterRead.Text : null;
                    if (fieldAfter is not null)
                        PushUndo(fieldBefore, fieldAfter, "rewrite");
                }

                if (!string.Equals(rewritten, selected, StringComparison.Ordinal))
                    RecordStat(s => s.RecordRewrite());
                Toast(string.Equals(rewritten, selected, StringComparison.Ordinal)
                    ? "Rewrite finished (text unchanged)."
                    : "Rewrote selection.");
                return;
            }
        }
        catch (Exception ex)
        {
            SelectionClipboard.RestoreClipboard(previousClip);
            Toast("Rewrite failed: " + ex.Message);
            _rewriteWindow?.Close();
        }
        finally
        {
            IsBusy = false;
            promptVm.IsBusy = false;
        }
    }

    public string ActiveHotkeyChord =>
        _hotkey.IsRegistered ? _hotkey.ActiveChord : "(none — use tray menu)";

    public void AttachReviewWindow(Window window)
    {
        _reviewWindow = window;
        ThemeLogoApplier.ApplyToWindow(window);
        ApplyReviewCardScale(window);
    }

    private void ApplyReviewCardScale(Window? window = null)
    {
        window ??= _reviewWindow;
        if (window is not ReviewCardWindow card)
            return;
        card.ApplyAccessibilityScale(ReviewCardScale);
    }

    [RelayCommand]
    private void OpenUsageStats()
    {
        if (_statsWindow is { IsVisible: true })
        {
            _statsWindow.Activate();
            return;
        }

        _statsWindow = new UsageStatsWindow();
        ThemeLogoApplier.ApplyToWindow(_statsWindow);
        var vm = new UsageStatsViewModel(_stats, _settings.General.CollectUsageStats);
        _statsWindow.DataContext = vm;
        _statsWindow.Closed += (_, _) => _statsWindow = null;
        _statsWindow.Show();
        _statsWindow.Activate();
    }

    [RelayCommand]
    private void OpenWhatsNew()
    {
        if (_whatsNewWindow is { IsVisible: true })
        {
            _whatsNewWindow.Activate();
            return;
        }

        var (version, notes, previous) = UpdateMigration.GetLastWhatsNew();
        if (string.IsNullOrWhiteSpace(notes))
            notes = $"You're on {UpdateService.GetCurrentVersionDisplay()}.\nRelease notes appear here after an in-app update.";

        _whatsNewWindow = new WhatsNewWindow();
        ThemeLogoApplier.ApplyToWindow(_whatsNewWindow);
        var vm = new WhatsNewViewModel(version, notes, previous);
        vm.CloseRequested += () => _whatsNewWindow?.Close();
        _whatsNewWindow.DataContext = vm;
        _whatsNewWindow.Closed += (_, _) => _whatsNewWindow = null;
        _whatsNewWindow.Show();
        _whatsNewWindow.Activate();
    }

    public void AttachUnderlineOverlay(UnderlineOverlayWindow overlay) => _underline = overlay;

    [RelayCommand]
    public async Task ClipboardFixAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        BusyStatus = "Clipboard…";
        ShowUpdateProgress("Clipboard Fix…", 0.05, "Reading…");
        try
        {
            if (!SelectionClipboard.TryGetClipboardText(out var text) || string.IsNullOrWhiteSpace(text))
            {
                Toast("Clipboard has no text to fix.");
                return;
            }

            if (text.Length < _settings.Dictation.MinChars)
            {
                Toast($"Clipboard too short (min {_settings.Dictation.MinChars} chars).");
                return;
            }

            if (text.Length > _settings.Dictation.MaxChars)
                text = text[.._settings.Dictation.MaxChars];

            BusyStatus = "Fixing clipboard…";
            var options = BuildCorrectionOptions(out var blocked);
            if (blocked)
                return;

            var progress = CreateFixProgress("Clipboard Fix…");
            var result = await _pipeline.CorrectAsync(text, options, progress: progress).ConfigureAwait(true);
            FinishUpdateProgress();
            if (!result.HasChanges ||
                string.Equals(result.Suggestion, text, StringComparison.Ordinal))
            {
                Toast("Clipboard already looks good.");
                return;
            }

            if (!SelectionClipboard.TrySetClipboardText(result.Suggestion))
            {
                Toast("Couldn't write the cleaned text to the clipboard.");
                return;
            }

            if (_settings.General.ClipboardFixPasteAfter)
            {
                await Task.Delay(40).ConfigureAwait(true);
                SelectionClipboard.PasteClipboard();
            }

            RecordStat(s => s.RecordClipboardFix());
            var hunkCount = result.Hunks?.Count ?? 0;
            Toast(_settings.General.ClipboardFixPasteAfter
                ? $"Clipboard fixed ({hunkCount} change{(hunkCount == 1 ? "" : "s")}) · pasted"
                : $"Clipboard fixed ({hunkCount} change{(hunkCount == 1 ? "" : "s")})");
        }
        catch (Exception ex)
        {
            Toast("Clipboard Fix: " + ex.Message);
        }
        finally
        {
            FinishUpdateProgress();
            IsBusy = false;
            BusyStatus = "";
        }
    }

    /// <summary>Shared Fix / Clipboard Fix provider options. Returns true when caller should abort (toast already shown).</summary>
    private CorrectionOptions BuildCorrectionOptions(out bool abort)
    {
        abort = false;
        var g = _settings.Grammar;
        var creds = PersonalApiKeyStore.TryLoad();
        var model = !string.IsNullOrWhiteSpace(g.OpenAiModel)
            ? g.OpenAiModel.Trim()
            : (creds?.Model ?? "gpt-4o-mini");

        // Privacy: local rules / punct / polish only — no LanguageTool or ChatGPT.
        if (_privacy.IsActive)
        {
            if (!_privacyFixToastShown)
            {
                _privacyFixToastShown = true;
                Toast("Privacy mode — Fix uses local rules only (no LanguageTool / ChatGPT).");
            }

            return new CorrectionOptions
            {
                Provider = "none",
                ProviderOrder = g.ProviderOrder,
                Language = g.Language,
                UseLocalRules = g.UseLocalRules,
                UsePunctuation = g.UsePunctuation,
                UseLightPolish = g.UseLightPolish,
                LocalRulesPath = DataPaths.FindDictationLocalRules(g.LocalRulesFile),
                UseWritingSuggestions = false
            };
        }

        var premiumOk = _license.Refresh().AllowsPremiumAi;
        var hasKey = !string.IsNullOrWhiteSpace(creds?.ApiKey);
        var perfect = FixQualityResolver.IsPerfect(g.FixQuality);
        var (provider, providerOrder, suggestKeyToast, suggestLicenseToast) = FixQualityResolver.Resolve(
            g.FixQuality,
            g.Provider,
            g.ProviderOrder,
            premiumOk,
            hasKey);

        if (perfect)
        {
            if (suggestKeyToast && !_perfectFixNoKeyToastShown)
            {
                _perfectFixNoKeyToastShown = true;
                Toast("Perfect Fix needs an AI API key — using LanguageTool only. Add key in Settings → Grammar.");
            }
            else if (suggestLicenseToast)
            {
                Toast("Trial ended — Perfect Fix skipped AI; LanguageTool still runs. Unlock in Settings → License.");
            }
        }
        else if (!premiumOk && g.Provider is "chatgpt" or "both")
        {
            if (g.Provider == "chatgpt")
            {
                Toast("ChatGPT Fix needs a license (trial ended). LanguageTool-only: set Provider to languagetool, or activate in Settings → License.");
                abort = true;
                return new CorrectionOptions();
            }
            provider = "languagetool";
            Toast("Trial ended — ChatGPT skipped; LanguageTool Fix still runs. Unlock in Settings → License.");
        }

        var useWriting = g.UseWritingSuggestions && premiumOk;
        if (g.UseWritingSuggestions && !premiumOk)
            Toast("Writing suggestions need a license — running correctness only.");

        var options = new CorrectionOptions
        {
            Provider = provider,
            ProviderOrder = providerOrder,
            Language = g.Language,
            UseLocalRules = g.UseLocalRules,
            UsePunctuation = g.UsePunctuation,
            UseLightPolish = g.UseLightPolish,
            LocalRulesPath = DataPaths.FindDictationLocalRules(g.LocalRulesFile),
            OpenAiApiKey = creds?.ApiKey,
            OpenAiModel = model,
            LlmProvider = string.IsNullOrWhiteSpace(creds?.Provider) ? "openai" : creds.Provider,
            LlmBaseUrl = creds?.BaseUrl,
            ChatGptInstruction = string.IsNullOrWhiteSpace(g.ChatGptInstruction)
                ? CorrectionOptions.DefaultChatGptInstruction
                : g.ChatGptInstruction,
            UseWritingSuggestions = useWriting,
            WritingInstruction = string.IsNullOrWhiteSpace(g.WritingInstruction)
                ? CorrectionOptions.DefaultWritingInstruction
                : g.WritingInstruction
        };

        var wantsChatGpt = premiumOk && (perfect
            ? hasKey
            : g.Provider is "chatgpt" or "both");
        if (!perfect && wantsChatGpt && string.IsNullOrWhiteSpace(options.OpenAiApiKey))
        {
            if (g.Provider == "chatgpt")
            {
                Toast("AI Fix needs an API key in Settings → Grammar.");
                abort = true;
            }
            else
                Toast("Writing suggestions need an AI API key — running correctness only.");
        }
        else if (useWriting && string.IsNullOrWhiteSpace(options.OpenAiApiKey))
        {
            Toast("Writing suggestions need an AI API key — running correctness only.");
        }

        return options;
    }

    [RelayCommand]
    public async Task FixNowAsync()
    {
        if (IsBusy)
            return;

        if (!_settings.Dictation.Enabled)
        {
            Toast("Dictation Fix is disabled in Settings.");
            return;
        }

        IsBusy = true;
        BusyStatus = "Checking…";
        ShowUpdateProgress("Fixing…", 0.02, "Reading field…");
        // Hide first so ClearQueue doesn't leave Current=null on a visible card (binding spam).
        HideReview();
        try
        {
            // After Voice Typing: activate window, click saved caret, SetFocus control.
            var sessionWin = _pendingWindowHwnd;
            var sessionCtrl = _pendingControlHwnd;
            RestorePendingDictationTarget();

            var target = ResolveFixTarget(sessionCtrl, sessionWin);
            if (target is null)
            {
                Toast("Click a text field first, then Win+Alt+F.");
                return;
            }

            _targetElement = target;
            try { _targetProcessId = target.Current.ProcessId; }
            catch { _targetProcessId = 0; }
            try
            {
                var proc = ForegroundProcess.GetProcessName();
                if (!_autocorrect.IsFixEnabledForProcess(proc))
                {
                    Toast($"Fix is disabled for {proc} (Settings → Autocorrect → per-app profiles).");
                    return;
                }
            }
            catch { /* proceed */ }
            var read = _textBridge.TryReadElement(target);
            if (!read.Success || read.Text is null)
            {
                read = _textBridge.TryReadFocused();
                if (!read.Success || read.Text is null || IsOurProcess(read.Field?.ProcessName))
                {
                    Toast(read.Error ?? "Couldn't read that field.");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(read.Text) || IsOurProcess(read.Field?.ProcessName))
            {
                Toast("No text to fix in the target field.");
                return;
            }

            if (UiaTextBridge.LooksLikeUiDump(read.Text))
            {
                Toast("Captured UI/file tree instead of text. Click in the editor, then Fix again.");
                return;
            }

            var text = read.Text;
            if (text.Length < _settings.Dictation.MinChars)
            {
                Toast($"Too short to fix (min {_settings.Dictation.MinChars} chars).");
                return;
            }
            if (text.Length > _settings.Dictation.MaxChars)
                text = text[.._settings.Dictation.MaxChars];

            BusyStatus = "Fixing…";
            var g = _settings.Grammar;
            var options = BuildCorrectionOptions(out var blocked);
            if (blocked)
                return;

            var progress = CreateFixProgress("Fixing…");
            var result = await _pipeline.CorrectAsync(text, options, progress: progress).ConfigureAwait(true);
            FinishUpdateProgress();

            // On-demand pack: when Auto-detect (or LT) names a language, fetch that pack in the background.
            var detected = result.DetectedLanguageCode;
            if (!string.IsNullOrWhiteSpace(detected) &&
                (string.Equals(_settings.Grammar.Language, "auto", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(detected, _settings.Grammar.Language, StringComparison.OrdinalIgnoreCase)))
            {
                _ = EnsureLanguagePackAsync(detected, quiet: true);
            }

            if (!result.HasChanges)
            {
                HideReview();
                Toast("No changes suggested.");
                return;
            }

            _workingText = result.Original;
            _queue.Clear();
            var atomic = WordDiff.ExplodeToAtomicHunks(result.Hunks);
            var queueItems = g.MergeNearbyMistakes
                ? WordDiff.MergeNearbyHunks(atomic, result.Original, g.NearbyMergeMaxGapChars)
                : atomic;
            foreach (var h in queueItems)
            {
                if (IsNoOpHunk(h))
                    continue;
                if (_settings.Autocorrect.ProtectZones &&
                    ProtectZones.SpanIsProtected(result.Original, h.StartPos, Math.Max(h.OldText.Length, 1)))
                    continue;
                if (_settings.Autocorrect.ProtectZones && ProtectZones.IsProtectedToken(h.OldText))
                    continue;
                if (_dismissed.Contains(h))
                    continue;
                _queue.Add(h.Clone());
            }

            if (_queue.Count == 0)
            {
                HideReview();
                Toast("No changes suggested.");
                return;
            }

            // Silent: apply the full corrected text once (no card).
            if (string.Equals(_settings.Dictation.ConfirmMode, "silent", StringComparison.OrdinalIgnoreCase))
            {
                var write = WriteField(result.Suggestion);
                if (write.Success)
                {
                    PushUndo(result.Original, result.Suggestion, "silent fix");
                    RecordStat(s => s.RecordFixAccepted(Math.Max(1, _queue.Count)));
                }

                HideReview();
                Toast(write.Success ? "Applied (silent)." : (write.Error ?? "Couldn't apply."));
                return;
            }

            _index = 0;
            Header = $"{_queue.Count} suggestion{(_queue.Count == 1 ? "" : "s")} · {read.Field?.ProcessName}";
            RefreshCurrent();
            Toast($"{_queue.Count} suggestion{(_queue.Count == 1 ? "" : "s")}");
        }
        catch (Exception ex)
        {
            Toast("Error: " + ex.Message);
        }
        finally
        {
            FinishUpdateProgress();
            IsBusy = false;
            BusyStatus = "";
        }
    }

    /// <summary>Apply ONLY the current queue item, then advance to the next mistake.</summary>
    [RelayCommand]
    private async Task ApplyCurrentAsync()
    {
        if (Current is null || _queue.Count == 0 || IsBusy)
            return;

        IsBusy = true;
        try
        {
            // Use the displayed/focused hunk — never a wider span.
            var hunk = Current.Hunk;
            _queue[_index] = hunk;

            var before = _workingText;
            if (!hunk.TryApply(before, out var nextText, out var appliedAt, out var replacedLen))
            {
                Toast("Couldn't locate that mistake in the text.");
                DismissCurrent();
                return;
            }

            // Guard: applying one queue item must not rewrite the whole paragraph.
            if (LooksLikeWholeFieldRewrite(before, nextText, hunk))
            {
                Toast("Blocked whole-paragraph apply — skipping bad hunk.");
                DismissCurrent();
                return;
            }

            var write = WriteField(nextText);
            if (!write.Success)
            {
                Toast(write.Error ?? "Couldn't apply.");
                return;
            }

            var appliedLabel = CompactLabel(hunk);
            PushUndo(before, nextText, appliedLabel);
            RecordStat(s => s.RecordFixAccepted());
            MaybeLearnLocalRule(before, nextText);
            _workingText = nextText;
            // Adjust remaining using the actual edit site (may differ from StartPos on fallback).
            hunk.StartPos = appliedAt;
            RemoveAtIndexAdjusting(_index, hunk, replacedLen);

            if (_queue.Count == 0)
            {
                HideReview();
                Toast("Done — applied " + appliedLabel);
                return;
            }

            if (_index >= _queue.Count)
                _index = _queue.Count - 1;
            RefreshCurrent();
            Toast("Applied " + appliedLabel + " · " + (_index + 1) + " of " + _queue.Count + " left");
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Safe Accept-all: apply each remaining queue hunk surgically (same whole-field guard),
    /// one UIA write, one undo. Never dumps the full pipeline Suggestion string.
    /// </summary>
    [RelayCommand]
    private async Task ApplyAllSafeAsync()
    {
        if (_queue.Count == 0 || IsBusy)
            return;

        if (_queue.Count == 1)
        {
            await ApplyCurrentAsync().ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        BusyStatus = "Applying…";
        try
        {
            var before = _workingText;
            var text = before;
            var applied = 0;
            var skipped = 0;
            var remaining = new List<CorrectionHunk>();
            // Sort by StartPos so later-offset adjustments stay valid.
            var pending = _queue.Select(h => h.Clone()).OrderBy(h => h.StartPos).ToList();

            // Left-to-right: each safe hunk applied surgically; later StartPos adjusted by delta.
            for (var i = 0; i < pending.Count; i++)
            {
                var hunk = pending[i];
                if (!hunk.TryApply(text, out var next, out var at, out var replacedLen) ||
                    LooksLikeWholeFieldRewrite(text, next, hunk))
                {
                    skipped++;
                    remaining.Add(hunk);
                    continue;
                }

                var delta = hunk.NewText.Length - replacedLen;
                var appliedEnd = at + replacedLen;
                text = next;
                applied++;
                for (var j = i + 1; j < pending.Count; j++)
                {
                    if (pending[j].StartPos >= appliedEnd)
                        pending[j].StartPos += delta;
                }
            }

            if (applied == 0)
            {
                Toast(skipped > 0
                    ? "Accept all skipped — no safe hunks could be applied."
                    : "Nothing to apply.");
                return;
            }

            if (string.Equals(before, text, StringComparison.Ordinal))
            {
                Toast("Accept all made no text change.");
                return;
            }

            var write = WriteField(text);
            if (!write.Success)
            {
                Toast(write.Error ?? "Couldn't apply.");
                return;
            }

            PushUndo(before, text, $"accept all ({applied})");
            RecordStat(s => s.RecordFixAcceptAll(applied));
            MaybeLearnLocalRule(before, text);
            _workingText = text;

            if (remaining.Count == 0)
            {
                HideReview();
                Toast(skipped > 0
                    ? $"Applied {applied}, skipped {skipped}."
                    : $"Done — applied all {applied}.");
                return;
            }

            _queue.Clear();
            _queue.AddRange(remaining);
            _index = 0;
            RefreshCurrent();
            Toast($"Applied {applied}, {remaining.Count} left (skipped unsafe/missing).");
        }
        finally
        {
            IsBusy = false;
            BusyStatus = "";
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task UndoLastAsync()
    {
        if (IsBusy)
            return;

        if (_undoStack.Count == 0)
        {
            Toast("Nothing to undo.");
            return;
        }

        IsBusy = true;
        try
        {
            var entry = _undoStack.Pop();

            // Queue offsets are relative to post-apply text — drop the card on undo.
            HideReview();

            if (entry.Keystroke)
            {
                // As-you-type autocorrect: backspace After, retype Before at caret.
                try
                {
                    SendInputText.ReplaceWord(entry.AfterText.Length, entry.BeforeText, '\0');
                    Toast("Undid " + entry.Label);
                }
                catch (Exception ex)
                {
                    _undoStack.Push(entry);
                    Toast("Couldn't undo autocorrect: " + ex.Message);
                }
                return;
            }

            var target = entry.Target ?? _targetElement ?? _focusTracker.GetLastExternal();
            TextWriteResult write;
            if (target is not null)
            {
                _targetElement = target;
                // If the field still has the applied text, restore exactly; otherwise
                // try a surgical replace of After→Before inside the current contents.
                var read = _textBridge.TryReadElement(target);
                if (read.Success && read.Text is not null)
                {
                    string restore;
                    if (string.Equals(read.Text, entry.AfterText, StringComparison.Ordinal))
                        restore = entry.BeforeText;
                    else if (read.Text.Contains(entry.AfterText, StringComparison.Ordinal))
                        restore = ReplaceOnce(read.Text, entry.AfterText, entry.BeforeText);
                    else if (string.Equals(read.Text, entry.BeforeText, StringComparison.Ordinal))
                    {
                        Toast("Already undone.");
                        return;
                    }
                    else
                    {
                        // Field moved on — still attempt full before snapshot write.
                        restore = entry.BeforeText;
                    }

                    write = WriteField(restore);
                }
                else
                {
                    write = WriteField(entry.BeforeText);
                }
            }
            else
            {
                write = _textBridge.TryWriteFocused(entry.BeforeText);
            }

            if (!write.Success)
            {
                // Put it back so the user can retry.
                _undoStack.Push(entry);
                Toast(write.Error ?? "Couldn't undo.");
                return;
            }

            _workingText = entry.BeforeText;
            Toast("Undid " + entry.Label);
        }
        catch (Exception ex)
        {
            Toast("Undo error: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    private void PushUndo(string before, string after, string label, bool keystroke = false)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        _undoStack.Push(new UndoEntry(before, after, keystroke ? null : _targetElement, label, keystroke));
        // Bound memory — keep newest 20 applies.
        if (_undoStack.Count > 20)
        {
            var keep = _undoStack.Take(20).Reverse().ToList();
            _undoStack.Clear();
            foreach (var e in keep)
                _undoStack.Push(e);
        }
    }

    private static string ReplaceOnce(string haystack, string oldValue, string newValue)
    {
        var idx = haystack.IndexOf(oldValue, StringComparison.Ordinal);
        if (idx < 0)
            return haystack;
        return string.Concat(haystack.AsSpan(0, idx), newValue, haystack.AsSpan(idx + oldValue.Length));
    }

    [RelayCommand]
    private void DismissCurrent()
    {
        HideUnderline();
        if (_queue.Count == 0)
        {
            HideReview();
            return;
        }

        // Session + persist so Skip doesn't reappear on the next Fix.
        try { _dismissed.Remember(_queue[_index], persist: true); }
        catch { /* ignore */ }

        _queue.RemoveAt(_index);
        if (_queue.Count == 0)
        {
            HideReview();
            Toast("Done");
            return;
        }

        if (_index >= _queue.Count)
            _index = _queue.Count - 1;
        RefreshCurrent();
    }

    [RelayCommand]
    private void NeverSuggestCurrent()
    {
        if (Current is null)
            return;

        var hunk = Current.Hunk;
        try { _dismissed.Remember(hunk, persist: true); }
        catch { /* ignore */ }

        var token = Current.DictionaryToken ?? hunk.OldText.Trim();
        if (!string.IsNullOrWhiteSpace(token) && !token.Contains(' '))
        {
            _autocorrect.Removed.Add(token, hunk.NewText, "fix-never");
            Toast("Never suggest “" + token + "” again (Fix + Word Manager → Removed).");
        }
        else
        {
            Toast("Won’t suggest this change again.");
        }

        // Remove without a second Skip toast path — already persisted above.
        HideUnderline();
        if (_queue.Count == 0)
        {
            HideReview();
            return;
        }
        _queue.RemoveAt(_index);
        if (_queue.Count == 0)
        {
            HideReview();
            return;
        }
        if (_index >= _queue.Count)
            _index = _queue.Count - 1;
        RefreshCurrent();
    }

    [RelayCommand]
    private void AddToDictionary()
    {
        if (Current is null || !Current.CanAddToDictionary || Current.DictionaryToken is null)
            return;

        _pipeline.Dictionary.Add(Current.DictionaryToken);
        Toast("Added “" + Current.DictionaryToken + "” to dictionary");
        DismissCurrent();
    }

    [RelayCommand]
    private void TogglePause() => ToggleSoftPause();

    private void MaybeLearnLocalRule(string before, string after)
    {
        if (!_settings.General.LearnLocalRulesFromAccepts)
            return;
        try
        {
            var pairs = LocalRulesLearner.ExtractPairs(before, after);
            if (pairs.Count == 0)
                return;
            var path = DataPaths.FindDictationLocalRules(_settings.Grammar.LocalRulesFile)
                       ?? DataPaths.FindOrCreateDataFilePublic(
                           string.IsNullOrWhiteSpace(_settings.Grammar.LocalRulesFile)
                               ? "DictationLocalRules.txt"
                               : _settings.Grammar.LocalRulesFile);
            var n = LocalRulesLearner.AppendPairs(path, pairs);
            if (n <= 0)
                return;
            foreach (var p in pairs)
                _localRuleUndo.Push(p);
            Toast("Learned " + n + " local STT rule" + (n == 1 ? "" : "s") + ".");
        }
        catch
        {
            /* ignore */
        }
    }

    [RelayCommand]
    private void UndoLastLocalRule()
    {
        if (_localRuleUndo.Count == 0)
        {
            Toast("No learned STT rule to undo.");
            return;
        }

        var (find, replace) = _localRuleUndo.Pop();
        var path = DataPaths.FindDictationLocalRules(_settings.Grammar.LocalRulesFile);
        if (path is null || !LocalRulesLearner.RemoveLastPair(path, find, replace))
        {
            Toast("Couldn't undo STT rule.");
            return;
        }

        Toast("Removed local rule “" + find + "” → “" + replace + "”.");
    }

    [RelayCommand]
    private void CloseQueue()
    {
        HideReview();
    }

    [RelayCommand]
    private void PrevSuggestion()
    {
        if (_index <= 0)
            return;
        _index--;
        RefreshCurrent();
    }

    [RelayCommand]
    private void NextSuggestion()
    {
        if (_index >= _queue.Count - 1)
            return;
        _index++;
        RefreshCurrent();
    }

    [RelayCommand]
    private void ExitApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsViewModel.WebsiteUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Toast("Couldn't open website: " + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenAlternateWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsViewModel.AlternateWebsiteUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Toast("Couldn't open alternate website: " + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenSocialX()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsViewModel.SocialXUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Toast("Couldn't open X: " + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenSocialGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsViewModel.SocialGitHubUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Toast("Couldn't open GitHub: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates() => await CheckForUpdatesAsync(quiet: false);

    private void ScheduleStartupUpdateCheck()
    {
        if (!_settings.General.CheckForUpdatesOnStartup)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() => CheckForUpdatesAsync(quiet: true));
            }
            catch
            {
                /* ignore startup update failures */
            }
        });
    }

    private async Task CheckForUpdatesAsync(bool quiet)
    {
        if (Interlocked.CompareExchange(ref _updateCheckBusy, 1, 0) != 0)
        {
            if (!quiet)
                Toast("Update check already running…");
            return;
        }

        try
        {
            if (!quiet)
                Toast("Checking for updates…");

            var feed = string.IsNullOrWhiteSpace(_settings.General.UpdateFeedUrl)
                ? null
                : _settings.General.UpdateFeedUrl;
            var result = await _updates.CheckAsync(feed).ConfigureAwait(true);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    if (!quiet)
                        Toast(result.Message);
                    break;

                case UpdateCheckStatus.RequiresFullSetup:
                    // Prefer in-app recovery messaging; Setup only as last resort.
                    Toast(result.Message + " If Check for updates keeps failing, reinstall once from Releases.");
                    break;

                case UpdateCheckStatus.Error:
                    if (!quiet)
                        Toast(result.Message + " Retry from the tray — Setup is not required for normal updates.");
                    break;

                case UpdateCheckStatus.UpdateAvailable:
                {
                    if (result.Manifest is null)
                    {
                        Toast("Update feed missing package details.");
                        break;
                    }

                    var version = result.Manifest.Version;
                    var notes = result.Manifest.Notes?.Trim();
                    var title = string.IsNullOrWhiteSpace(notes)
                        ? $"Update {version} — downloading…"
                        : $"Update {version}: {notes}";
                    ShowUpdateProgress(title, null, "Connecting…");

                    var progress = new Progress<UpdateDownloadProgress>(p =>
                    {
                        var header = p.Phase switch
                        {
                            UpdateProgressPhase.Verifying => $"Update {version} — verifying…",
                            UpdateProgressPhase.Extracting => $"Update {version} — extracting…",
                            UpdateProgressPhase.Applying => $"Update {version} — preparing install…",
                            _ => title
                        };
                        var detail = p.Phase == UpdateProgressPhase.Downloading
                            ? p.Message
                            : (string.IsNullOrWhiteSpace(p.Message) ? null : p.Message);
                        var pct = p.Fraction is { } f
                            ? (int)Math.Round(f * 100) + "%"
                            : null;
                        if (p.Phase == UpdateProgressPhase.Downloading && pct is not null)
                            detail = (detail ?? "Downloading…") + " · " + pct;
                        ShowUpdateProgress(header, p.Fraction, detail);
                    });

                    var (ok, msg) = await _updates
                        .DownloadAndStageAsync(result.Manifest, progress: progress)
                        .ConfigureAwait(true);
                    FinishUpdateProgress();
                    if (ok)
                    {
                        Toast(msg);
                        await Task.Delay(800).ConfigureAwait(true);
                        ExitApp();
                    }
                    else
                    {
                        ToastAction(
                            msg + " Tap Retry to try again (no Setup needed).",
                            12000,
                            "Retry",
                            () => _ = CheckForUpdatesAsync(quiet: false));
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (!quiet)
                Toast("Update check failed: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckBusy, 0);
        }
    }

    private void RefreshCurrent()
    {
        if (_queue.Count == 0)
        {
            Current = null;
            CategoryLabel = "Review suggestions";
            CategoryBadge = "SUGGESTION";
            WhyText = "";
            WhyTooltip = "";
            ConfidenceChip = "";
            HasWhyText = false;
            HasConfidenceChip = false;
            PositionLabel = "";
            HasMultiple = false;
            AcceptAllLabel = "Accept all";
            CanGoPrev = false;
            CanGoNext = false;
            CanAddToDictionary = false;
            HideUnderline();
            return;
        }

        _index = Math.Clamp(_index, 0, _queue.Count - 1);
        // Keep merged nearby clusters intact — only tighten single-token leftovers.
        var queued = _queue[_index];
        var focused = CountWords(queued.OldText) <= 1 && CountWords(queued.NewText) <= 1
            ? WordDiff.TightenHunk(queued.Clone())
            : queued.Clone();
        if (focused.Confidence == default && queued.Confidence != default)
            focused.Confidence = queued.Confidence;
        else if (focused.Confidence == SuggestionConfidence.Medium &&
                 string.IsNullOrWhiteSpace(focused.RuleId) &&
                 focused.Category == SuggestionCategory.Other)
            focused.Confidence = SuggestionConfidenceLabels.Infer(focused);
        else
            focused.Confidence = SuggestionConfidenceLabels.Infer(focused);
        _queue[_index] = focused;
        var item = new HunkItemViewModel(focused, _workingText, tighten: false);
        Current = item;
        CategoryLabel = item.CategoryLabel;
        CategoryBadge = item.CategoryBadge;
        WhyText = item.WhyText;
        WhyTooltip = item.RuleTooltip;
        ConfidenceChip = item.ConfidenceChip;
        HasWhyText = item.HasWhyText;
        HasConfidenceChip = !string.IsNullOrWhiteSpace(item.ConfidenceChip);
        PositionLabel = $"{_index + 1} of {_queue.Count}";
        HasMultiple = _queue.Count > 1;
        AcceptAllLabel = _queue.Count > 1 ? $"Accept all ({_queue.Count})" : "Accept all";
        CanGoPrev = _index > 0;
        CanGoNext = _index < _queue.Count - 1;
        CanAddToDictionary = item.CanAddToDictionary;
        ShowOrRepositionReview(focused);
    }

    private static int CountWords(string s) =>
        WordDiff.Tokenize(s).Count(t => t.Length > 0 && (char.IsLetterOrDigit(t[0]) || t[0] == '\''));

    /// <summary>
    /// Grammarly-style: highlight the active mistake and park the Fix card under it.
    /// Falls back to cursor placement when UIA bounds are unavailable.
    /// </summary>
    private void ShowOrRepositionReview(CorrectionHunk hunk)
    {
        var hasBounds = false;
        var x = 0;
        var y = 0;
        var w = 0;
        var h = 0;

        if (_targetElement is not null)
        {
            var start = hunk.StartPos;
            var needle = hunk.OldText.Length > 0 ? hunk.OldText : null;
            var length = Math.Max(1, hunk.OldText.Length > 0 ? hunk.OldText.Length : Math.Max(1, hunk.NewText.Length));
            hasBounds = _textBridge.TryGetTextRangeScreenBounds(
                _targetElement, start, length, needle, _workingText,
                out x, out y, out w, out h);
        }

        if (hasBounds)
            _underline?.ShowAt(x, y, w, h);
        else
            HideUnderline();

        if (_reviewWindow is null)
            return;

        _reviewWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        var wasHidden = !_reviewWindow.IsVisible;
        if (wasHidden)
            _reviewWindow.Show();

        if (_reviewWindow is ReviewCardWindow card)
        {
            if (hasBounds)
                card.PlaceUnderMistake(x, y, w, h);
            else
                card.PlaceNear(GetCursorPos());
        }
        else if (hasBounds)
        {
            _reviewWindow.Position = new PixelPoint(Math.Max(8, x), Math.Max(8, y + h + 8));
        }
        else
        {
            var pos = GetCursorPos();
            _reviewWindow.Position = new PixelPoint(Math.Max(8, pos.X + 12), Math.Max(8, pos.Y + 16));
        }

        SetReviewQuiet(false);
        // Only steal focus when first opening — passive Prev/Next repositions stay quiet.
        if (wasHidden)
            _reviewWindow.Activate();
    }

    private void HideUnderline() => _underline?.HideOverlay();

    private void OnExternalFocusChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_reviewWindow is not { IsVisible: true } || _queue.Count == 0)
                return;

            try
            {
                var focused = System.Windows.Automation.AutomationElement.FocusedElement;
                if (focused is null)
                    return;
                var pid = focused.Current.ProcessId;
                // Our card / windows — keep loud.
                if (pid == Environment.ProcessId)
                {
                    SetReviewQuiet(false);
                    return;
                }
                // Still editing the Fix target app — keep loud.
                if (_targetProcessId != 0 && pid == _targetProcessId)
                {
                    SetReviewQuiet(false);
                    return;
                }
                SetReviewQuiet(true);
            }
            catch
            {
                /* ignore */
            }
        });
    }

    /// <summary>Called from the review card when the pointer enters (restore after dim).</summary>
    public void NotifyReviewPointerEntered() => SetReviewQuiet(false);

    private void SetReviewQuiet(bool quiet)
    {
        var opacity = quiet ? 0.35 : 1.0;
        if (_reviewWindow is not null)
            _reviewWindow.Opacity = opacity;
        if (_underline is not null)
            _underline.Opacity = quiet ? 0.25 : 1.0;
        if (quiet)
            HideUnderline();
        else if (_queue.Count > 0 && Current is not null)
        {
            // Re-show underline for active hunk without Activate.
            try
            {
                var hunk = Current.Hunk;
                if (_targetElement is not null &&
                    _textBridge.TryGetTextRangeScreenBounds(
                        _targetElement, hunk.StartPos,
                        Math.Max(1, hunk.OldText.Length > 0 ? hunk.OldText.Length : Math.Max(1, hunk.NewText.Length)),
                        hunk.OldText.Length > 0 ? hunk.OldText : null,
                        _workingText,
                        out var x, out var y, out var w, out var h))
                {
                    _underline?.ShowAt(x, y, w, h);
                    _underline!.Opacity = 1.0;
                }
            }
            catch { /* ignore */ }
        }
    }

    private void RemoveAtIndexAdjusting(int index, CorrectionHunk applied, int? replacedLength = null)
    {
        var oldLen = replacedLength ?? applied.OldText.Length;
        var delta = applied.NewText.Length - oldLen;
        var appliedEnd = applied.StartPos + oldLen;
        _queue.RemoveAt(index);
        foreach (var h in _queue)
        {
            if (h.StartPos >= appliedEnd)
                h.StartPos += delta;
        }
    }

    private static bool LooksLikeWholeFieldRewrite(string before, string after, CorrectionHunk hunk)
    {
        if (before.Length < 40)
            return false;
        var expectedMaxChange = Math.Max(hunk.OldText.Length, hunk.NewText.Length) + 8;
        var changed = ApproximateChangedChars(before, after);
        // If we rewrote most of the paragraph for a tiny hunk, something went wrong.
        return changed > Math.Max(expectedMaxChange * 3, before.Length / 3);
    }

    private static int ApproximateChangedChars(string a, string b)
    {
        var pre = 0;
        var max = Math.Min(a.Length, b.Length);
        while (pre < max && a[pre] == b[pre])
            pre++;
        var suf = 0;
        while (suf < max - pre && a[a.Length - 1 - suf] == b[b.Length - 1 - suf])
            suf++;
        return Math.Max(a.Length - pre - suf, b.Length - pre - suf);
    }

    private static string CompactLabel(CorrectionHunk h)
    {
        var o = string.Join(' ', (h.OldText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var n = string.Join(' ', (h.NewText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (o.Length == 0)
            return "insert “" + Clip(n, 24) + "”";
        if (n.Length == 0)
            return "remove “" + Clip(o, 24) + "”";
        return "“" + Clip(o, 20) + "”→“" + Clip(n, 20) + "”";
    }

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static readonly TextWriteOptions FastWrite = new()
    {
        AllowClipboardFallback = true,
        PreferLowLatency = true
    };

    private TextWriteResult WriteField(string text) =>
        _targetElement is not null
            ? _textBridge.TryWriteElement(_targetElement, text, FastWrite)
            : _textBridge.TryWriteFocused(text, FastWrite);

    private void HideReview()
    {
        HideUnderline();
        ClearQueue();
        if (_reviewWindow is { IsVisible: true })
            _reviewWindow.Hide();
    }

    private void ClearQueue()
    {
        _queue.Clear();
        _workingText = "";
        _index = 0;
        RefreshCurrent();
    }

    private void RestorePendingDictationTarget()
    {
        var win = _pendingWindowHwnd;
        var ctrl = _pendingControlHwnd;
        var cx = _pendingCaretX;
        var cy = _pendingCaretY;
        _pendingWindowHwnd = 0;
        _pendingControlHwnd = 0;
        _pendingCaretX = null;
        _pendingCaretY = null;

        if (win == 0 && ctrl == 0 && cx is null)
            return;

        FocusRestorer.Restore(win, ctrl, cx, cy);
    }

    private AutomationElement? ResolveFixTarget(nint sessionControlHwnd, nint sessionWindowHwnd)
    {
        // After restore, prefer real focused element (must be editable — not file tree).
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null &&
                !IsIgnoredFocusProcess(focused) &&
                LooksLikeEditableTarget(focused))
                return focused;
        }
        catch
        {
            // ignore
        }

        // Session control HWND only when it's a child (not the Electron top-level shell).
        if (sessionControlHwnd != 0 &&
            sessionControlHwnd != sessionWindowHwnd)
        {
            try
            {
                var fromCtrl = AutomationElement.FromHandle(sessionControlHwnd);
                if (fromCtrl is not null &&
                    !IsIgnoredFocusProcess(fromCtrl) &&
                    LooksLikeEditableTarget(fromCtrl))
                    return fromCtrl;
            }
            catch
            {
                // ignore invalid/stale handles
            }
        }

        // Never FromHandle(top-level Electron window) — Ctrl+A there grabs the file tree.
        var tracked = _focusTracker.GetLastExternal();
        if (tracked is not null && LooksLikeEditableTarget(tracked))
            return tracked;

        return tracked;
    }

    private static bool LooksLikeEditableTarget(AutomationElement el)
    {
        try
        {
            var type = el.Current.ControlType.ProgrammaticName ?? "";
            if (type.Contains("Tree", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("List", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("DataGrid", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("ToolBar", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Menu", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Tab", StringComparison.OrdinalIgnoreCase))
                return false;

            if (type.Contains("Edit", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("Document", StringComparison.OrdinalIgnoreCase))
                return true;

            // Pane/custom contenteditable often still has Text/Value patterns.
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out _) ||
                el.TryGetCurrentPattern(TextPattern.Pattern, out _))
                return true;

            // Electron render HWND: allow; clipboard path will validate text.
            var cls = el.Current.ClassName ?? "";
            if (cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase))
                return true;

            // Reject bare top-level Chrome_WidgetWin / Window with no text patterns.
            if (type.Contains("Window", StringComparison.OrdinalIgnoreCase))
                return false;
            if (cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                return false;

            return el.Current.IsKeyboardFocusable;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNoOpHunk(CorrectionHunk h) =>
        (string.IsNullOrEmpty(h.OldText) && string.IsNullOrEmpty(h.NewText)) ||
        string.Equals(h.OldText, h.NewText, StringComparison.Ordinal);

    private static bool IsIgnoredFocusProcess(AutomationElement el)
    {
        try
        {
            var pid = el.Current.ProcessId;
            var name = System.Diagnostics.Process.GetProcessById(pid).ProcessName;
            if (IsOurProcess(name))
                return true;
            return name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void Toast(string message) => ToastRequested?.Invoke(message);

    private void ToastAction(string message, int ms, string? actionLabel, Action? action) =>
        ToastActionRequested?.Invoke(message, ms, actionLabel, action);

    private void ShowUpdateProgress(string message, double? fraction, string? detail) =>
        UpdateProgressRequested?.Invoke(message, fraction, detail);

    private void FinishUpdateProgress() => UpdateProgressFinished?.Invoke();

    private IProgress<CorrectionStage> CreateFixProgress(string title) =>
        new Progress<CorrectionStage>(stage =>
        {
            BusyStatus = stage.Message;
            ShowUpdateProgress(title, stage.Fraction, stage.Message);
        });

    private static bool IsOurProcess(string? name) =>
        string.Equals(name, "AutoCorrect.App", StringComparison.OrdinalIgnoreCase);

    private static PixelPoint GetCursorPos()
    {
        GetCursorPos(out POINT p);
        return new PixelPoint(p.X, p.Y);
    }

    public void Dispose()
    {
        if (_privacyTimer is not null)
        {
            _privacyTimer.Stop();
            _privacyTimer = null;
        }

        CancelPendingDictationFix();
        _privacy.Changed -= OnPrivacyChanged;
        _dictation.Dispose();
        _hotkey.Dispose();
        _focusTracker.Changed -= OnExternalFocusChanged;
        _focusTracker.Dispose();
        _pipeline.Dispose();
        _chatGpt.Dispose();
        _updates.Dispose();
        _languagePacks.Dispose();
        if (_autoLearnPrompt is not null)
        {
            try { _autoLearnPrompt.Close(); } catch { /* ignore */ }
            _autoLearnPrompt = null;
        }
        if (_wordManagerWindow is not null)
        {
            try { _wordManagerWindow.Close(); } catch { /* ignore */ }
            _wordManagerWindow = null;
        }
        if (_snippetManagerWindow is not null)
        {
            try { _snippetManagerWindow.Close(); } catch { /* ignore */ }
            _snippetManagerWindow = null;
        }
        if (_statsWindow is not null)
        {
            try { _statsWindow.Close(); } catch { /* ignore */ }
            _statsWindow = null;
        }
        if (_rewriteWindow is not null)
        {
            try { _rewriteWindow.Close(); } catch { /* ignore */ }
            _rewriteWindow = null;
        }
        if (_settingsWindow is not null)
        {
            try { _settingsWindow.Close(); } catch { /* ignore */ }
            _settingsWindow = null;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
