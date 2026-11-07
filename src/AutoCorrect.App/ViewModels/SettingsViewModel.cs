using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using AutoCorrect.App.Themes;
using AutoCorrect.Core;
using AutoCorrect.Core.Autocorrect;
using AutoCorrect.Core.Backup;
using AutoCorrect.Core.Correction;
using AutoCorrect.Core.Licensing;
using AutoCorrect.Core.Llm;
using AutoCorrect.Core.Privacy;
using AutoCorrect.Core.Settings;
using AutoCorrect.Core.Updates;
using AutoCorrect.Platform.Windows;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _store;
    private readonly Action<AppSettings> _onSaved;
    private readonly Action? _openWordManager;
    private readonly Action? _openSnippetManager;
    private readonly Action? _openUsageStats;
    private readonly PrivacyModeService? _privacy;
    private readonly LicenseService _license;

    /// <summary>PayPal NCP payment link (Personal Lifetime $59).</summary>
    public const string BuyUrl = "https://www.paypal.com/ncp/payment/PA3R3XQRAF8QG";

    /// <summary>Advertised primary site (Cloudflare Pages — school / Lightspeed-safe).</summary>
    public const string WebsiteUrl =
        "https://autocorrect-windows-site.pages.dev/";

    public const string SupportUrl =
        "https://autocorrect-windows-site.pages.dev/support";

    /// <summary>GitHub Pages mirror (fallback if Cloudflare is down).</summary>
    public const string AlternateWebsiteUrl =
        "https://badatcoding12345678910.github.io/AutoCorrect-Windows/";

    public const string AlternateSupportUrl =
        "https://badatcoding12345678910.github.io/AutoCorrect-Windows/support.html";

    /// <summary>Public X profile (Kyle Welch / AutoCorrect).</summary>
    public const string SocialXUrl = "https://x.com/phd_in_sleeping";

    /// <summary>Public GitHub profile (main contributor).</summary>
    public const string SocialGitHubUrl = "https://github.com/BADATCODING12345678910";

    public const string SubscribeBaseUrl =
        "https://arsfrcafaoagrjbnjydx.supabase.co/functions/v1/subscribe?tier=";

    private readonly AutocorrectEngine? _engine;
    private readonly LearnContributionStore? _contributions;
    private Window? _hostWindow;

    public SettingsViewModel(
        SettingsStore store,
        Action<AppSettings> onSaved,
        Action? openWordManager = null,
        LicenseService? license = null,
        AutocorrectEngine? engine = null,
        LearnContributionStore? contributions = null,
        Window? hostWindow = null,
        PrivacyModeService? privacy = null,
        Action? openSnippetManager = null,
        Action? openUsageStats = null)
    {
        _store = store;
        _onSaved = onSaved;
        _openWordManager = openWordManager;
        _openSnippetManager = openSnippetManager;
        _openUsageStats = openUsageStats;
        _privacy = privacy;
        _license = license ?? new LicenseService();
        _engine = engine;
        _contributions = contributions;
        _hostWindow = hostWindow;
        BuildThemeOptions();
        LoadFrom(store.Current);
        RefreshLicenseUi();
        RefreshPrivacyUi();
        if (_privacy is not null)
            _privacy.Changed += OnPrivacyServiceChanged;
        ThemeLogoApplier.PreviewChanged += RefreshThemedLogo;
    }

    private void OnPrivacyServiceChanged() => RefreshPrivacyUi();

    private void RefreshPrivacyUi()
    {
        if (_privacy is null)
        {
            PrivacyStatusText = "Privacy mode unavailable.";
            return;
        }

        if (!_privacy.IsActive)
        {
            PrivacyStatusText = "Off — Fix may use LanguageTool / ChatGPT per Grammar settings.";
            return;
        }

        if (_privacy.IsUntilManualOff)
        {
            PrivacyStatusText = "On until you end it — local rules / punct / polish only.";
            return;
        }

        var left = _privacy.Remaining;
        if (left is null)
        {
            PrivacyStatusText = "On — local-only.";
            return;
        }

        var mins = Math.Max(1, (int)Math.Ceiling(left.Value.TotalMinutes));
        PrivacyStatusText = $"On — ~{mins} min left (no LanguageTool / ChatGPT / Rewrite).";
    }

    private void BuildThemeOptions()
    {
        ThemeOptions.Clear();
        DarkThemes.Clear();
        LightThemes.Clear();
        GradientThemes.Clear();
        foreach (var palette in AppThemeCatalog.All)
        {
            var option = new ThemeOptionViewModel(palette, ApplyThemeSelection, selected: false);
            ThemeOptions.Add(option);
            switch (palette.Category)
            {
                case ThemeCategory.Light:
                    LightThemes.Add(option);
                    break;
                case ThemeCategory.Gradient:
                    GradientThemes.Add(option);
                    break;
                default:
                    DarkThemes.Add(option);
                    break;
            }
        }
    }

    private void ApplyThemeSelection(string themeId)
    {
        SelectedThemeId = themeId;
        ThemeService.Apply(themeId);
        foreach (var opt in ThemeOptions)
            opt.RefreshSelection(themeId);
        SelectedThemeLabel = AppThemeCatalog.Get(themeId).DisplayName;
        RefreshThemedLogo();
        StatusMessage = "Theme: " + SelectedThemeLabel + " (Save to keep). Desktop icon updates in the background — if Explorer lags, press F5 on the desktop.";
    }

    private void RefreshThemedLogo()
    {
        // Assign first so Image drops the previous bitmap before ThemeLogoApplier disposes it.
        ThemedLogo = ThemeLogoApplier.CurrentPreview;
    }

    public void AttachHostWindow(Window window) => _hostWindow = window;

    public void Detach()
    {
        if (_privacy is not null)
            _privacy.Changed -= OnPrivacyServiceChanged;
        ThemeLogoApplier.PreviewChanged -= RefreshThemedLogo;
    }

    [RelayCommand]
    private void OpenWordManager() => _openWordManager?.Invoke();

    [RelayCommand]
    private void OpenSnippetManager() => _openSnippetManager?.Invoke();

    [RelayCommand]
    private void OpenUsageStats() => _openUsageStats?.Invoke();

    [RelayCommand]
    private void StartPrivacyNow()
    {
        if (_privacy is null)
            return;
        _privacy.Enable(PrivacyDefaultMinutes);
        RefreshPrivacyUi();
        StatusMessage = _privacy.StatusToast();
    }

    [RelayCommand]
    private void EndPrivacyNow()
    {
        if (_privacy is null)
            return;
        _privacy.Disable();
        RefreshPrivacyUi();
        StatusMessage = _privacy.StatusToast();
    }

    public ObservableCollection<string> TriggerModes { get; } = ["hotkey", "auto", "both"];
    public ObservableCollection<string> ConfirmModes { get; } = ["preview", "confirm", "silent"];
    public ObservableCollection<string> Scopes { get; } = ["all", "selection", "sentence", "paragraph"];
    public ObservableCollection<string> Providers { get; } = ["languagetool", "chatgpt", "both", "none"];
    public ObservableCollection<string> ProviderOrders { get; } =
        ["languagetool_then_chatgpt", "chatgpt_then_languagetool"];
    public ObservableCollection<string> FixQualityModes { get; } = ["perfect", "standard"];
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new(LanguageCatalog.All);
    public ObservableCollection<string> StrictnessModes { get; } = ["strict", "balanced"];
    public ObservableCollection<string> LearnModes { get; } = ["manual", "confirm", "silent"];
    public ObservableCollection<string> ReviewCardScaleLabels { get; } =
        ["100% (default)", "115% (larger)", "130% (largest)"];

    public ObservableCollection<string> LlmProviderIds { get; } =
        new(LlmProviderCatalog.All.Select(p => p.Id));

    public ObservableCollection<string> OpenAiModelPresets { get; } = new(
        LlmProviderCatalog.ModelPresetsFor(LlmProviderCatalog.OpenAi));

    public ObservableCollection<string> FixHotkeyPresets { get; } =
        ["Win+Alt+F", "Ctrl+Alt+F", "Ctrl+Shift+F", "Win+Shift+F", "Ctrl+Alt+Shift+F"];

    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; } = [];
    public ObservableCollection<ThemeOptionViewModel> DarkThemes { get; } = [];
    public ObservableCollection<ThemeOptionViewModel> LightThemes { get; } = [];
    public ObservableCollection<ThemeOptionViewModel> GradientThemes { get; } = [];

    // —— General / hotkeys ——
    [ObservableProperty] private string _selectedThemeId = AppThemeCatalog.DefaultThemeId;
    [ObservableProperty] private string _selectedThemeLabel = "Light Lavender";
    [ObservableProperty] private Bitmap? _themedLogo;
    [ObservableProperty] private bool _showStartupToast = true;
    [ObservableProperty] private bool _showAutocorrectUndoToast = true;
    [ObservableProperty] private bool _checkForUpdatesOnStartup = true;
    [ObservableProperty] private string _appVersionDisplay = UpdateService.GetCurrentVersionDisplay();
    [ObservableProperty] private string _fixHotkey = "Win+Alt+F";
    [ObservableProperty] private string _undoHotkey = "Win+Alt+Shift+F";
    [ObservableProperty] private string _dictationHotkey = "Win+H";
    [ObservableProperty] private string _rewriteHotkey = "Win+Alt+R";
    [ObservableProperty] private string _pauseHotkey = "Win+Alt+P";
    [ObservableProperty] private string _clipboardFixHotkey = "Win+Alt+C";
    [ObservableProperty] private string _privacyHotkey = "Win+Alt+Shift+P";
    [ObservableProperty] private bool _clipboardFixPasteAfter = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _learnLocalRulesFromAccepts = true;
    [ObservableProperty] private int _privacyDefaultMinutes = 30;
    [ObservableProperty] private string _privacyStatusText = "";
    [ObservableProperty] private bool _collectUsageStats;
    [ObservableProperty] private bool _showWeeklyDigest = true;
    [ObservableProperty] private double _reviewCardScale = 1.0;
    [ObservableProperty] private string _reviewCardScaleLabel = "100% (default)";
    [ObservableProperty] private string _recordingTarget = ""; // "", "fix", "undo", "dictation", "rewrite", "pause", "clipboard", "privacy"
    [ObservableProperty] private string _hotkeyHint = "Click Record, then press a chord (needs Ctrl/Alt/Win/Shift + a key). Esc cancels.";

    public ObservableCollection<string> PrivacyDurationLabels { get; } =
        ["15 minutes", "30 minutes", "1 hour", "2 hours", "Until I turn it off"];

    [ObservableProperty] private string _privacyDurationLabel = "30 minutes";

    partial void OnPrivacyDurationLabelChanged(string value) =>
        PrivacyDefaultMinutes = PrivacyLabelToMinutes(value);

    partial void OnReviewCardScaleLabelChanged(string value) =>
        ReviewCardScale = ScaleLabelToValue(value);

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value is null)
            return;
        Language = value.Code;
    }

    private static double ScaleLabelToValue(string label) => label switch
    {
        "115% (larger)" => 1.15,
        "130% (largest)" => 1.3,
        _ => 1.0
    };

    private static string ScaleValueToLabel(double scale) => scale switch
    {
        >= 1.25 => "130% (largest)",
        >= 1.1 => "115% (larger)",
        _ => "100% (default)"
    };

    private static int PrivacyLabelToMinutes(string label) => label switch
    {
        "15 minutes" => 15,
        "1 hour" => 60,
        "2 hours" => 120,
        "Until I turn it off" => 0,
        _ => 30
    };

    private static string PrivacyMinutesToLabel(int minutes) => minutes switch
    {
        15 => "15 minutes",
        60 => "1 hour",
        120 => "2 hours",
        0 => "Until I turn it off",
        _ => "30 minutes"
    };

    // —— Dictation ——
    [ObservableProperty] private bool _dictationEnabled = true;
    [ObservableProperty] private string _triggerMode = "both";
    [ObservableProperty] private string _confirmMode = "preview";
    [ObservableProperty] private bool _autoWatchVoiceTyping = true;
    [ObservableProperty] private bool _useWinHHook = true;
    [ObservableProperty] private int _autoDelayMs = 1200;
    [ObservableProperty] private int _multiUtteranceQuietMs = 2800;
    [ObservableProperty] private int _minChars = 4;
    [ObservableProperty] private int _maxChars = 10000;
    [ObservableProperty] private string _scope = "all";
    [ObservableProperty] private bool _preferSelection = true;

    // —— Grammar / pipeline ——
    [ObservableProperty] private string _fixQuality = "perfect";
    [ObservableProperty] private string _provider = "languagetool";
    [ObservableProperty] private string _providerOrder = "languagetool_then_chatgpt";
    [ObservableProperty] private string _language = "en-US";
    [ObservableProperty] private LanguageOption _selectedLanguage = LanguageCatalog.FindOrCustom("en-US");
    [ObservableProperty] private bool _useLocalRules = true;
    [ObservableProperty] private bool _usePunctuation = true;
    [ObservableProperty] private bool _useLightPolish = true;
    [ObservableProperty] private string _localRulesFile = "DictationLocalRules.txt";
    [ObservableProperty] private string _llmProvider = LlmProviderCatalog.OpenAi;
    [ObservableProperty] private string _llmBaseUrl = "https://api.openai.com/v1";
    [ObservableProperty] private string _llmProviderHint = "";
    [ObservableProperty] private string _openAiModel = "gpt-4o-mini";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private bool _showOpenAiApiKey;
    [ObservableProperty] private string _chatGptInstruction = "";
    [ObservableProperty] private bool _mergeNearbyMistakes = true;
    [ObservableProperty] private int _nearbyMergeMaxGapChars = 2;
    [ObservableProperty] private bool _useWritingSuggestions;
    [ObservableProperty] private string _writingInstruction = "";

    // —— Autocorrect (Phase A) ——
    [ObservableProperty] private bool _autocorrectEnabled = true;
    [ObservableProperty] private string _autocorrectStrictness = "balanced";
    [ObservableProperty] private bool _useCurated = true;
    [ObservableProperty] private bool _useImportedLibrary = true;
    [ObservableProperty] private bool _useDictionaryDistance = true;
    [ObservableProperty] private bool _useLearned = true;
    [ObservableProperty] private string _curatedFile = "AutocorrectCurated.txt";
    [ObservableProperty] private string _wordlistFile = "unigram_freq_list_filtered_89k.csv";
    [ObservableProperty] private string _excludedProcesses =
        "AutoCorrect.App,WindowsTerminal,wt,cmd,powershell,pwsh,Code,devenv";
    [ObservableProperty] private string _appProfilesText = "";
    [ObservableProperty] private bool _protectZones = true;

    // —— Snippets ——
    [ObservableProperty] private bool _snippetsEnabled = true;
    [ObservableProperty] private string _snippetsFileName = "Snippets.json";
    [ObservableProperty] private string _snippetsPathHint = "";

    // —— AutoLearn ——
    [ObservableProperty] private bool _autoLearnEnabled = true;
    [ObservableProperty] private string _learnMode = "confirm";
    [ObservableProperty] private int _minOccurrences = 3;
    [ObservableProperty] private string _curatedPath = "";
    [ObservableProperty] private string _curatedCountHint = "";
    [ObservableProperty] private string _importedPath = "";
    [ObservableProperty] private string _importedCountHint = "";
    [ObservableProperty] private string _wordlistPath = "";
    [ObservableProperty] private string _wordlistCountHint = "";

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _settingsPath = "";
    [ObservableProperty] private string _apiKeyPath = "";

    // —— License (Phase C + online entitlements) ——
    [ObservableProperty] private string _licenseStatusLine = "";
    [ObservableProperty] private string _licenseDetailLine = "";
    [ObservableProperty] private string _licenseKeyInput = "";
    [ObservableProperty] private string _licenseFilePath = "";
    [ObservableProperty] private string _licenseEmail = "";

    public bool IsRecording => !string.IsNullOrEmpty(RecordingTarget);

    /// <summary>Avalonia PasswordChar — bullet when hidden, nul when revealed.</summary>
    public char OpenAiApiKeyPasswordChar => ShowOpenAiApiKey ? '\0' : '•';

    partial void OnShowOpenAiApiKeyChanged(bool value) =>
        OnPropertyChanged(nameof(OpenAiApiKeyPasswordChar));

    partial void OnLlmProviderChanged(string value)
    {
        var info = LlmProviderCatalog.Get(value);
        LlmProviderHint = info.Hint;
        // Only reset base URL when switching away from custom, or custom still empty.
        if (!string.Equals(value, LlmProviderCatalog.Custom, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(LlmBaseUrl))
            LlmBaseUrl = info.DefaultBaseUrl;
        RefreshModelPresets(value);
        if (string.IsNullOrWhiteSpace(OpenAiModel) ||
            !OpenAiModelPresets.Contains(OpenAiModel))
            OpenAiModel = info.DefaultModel;
    }

    private void RefreshModelPresets(string? providerId)
    {
        OpenAiModelPresets.Clear();
        foreach (var m in LlmProviderCatalog.ModelPresetsFor(providerId))
            OpenAiModelPresets.Add(m);
    }

    public void LoadFrom(AppSettings s)
    {
        SelectedThemeId = string.IsNullOrWhiteSpace(s.General.ThemeId)
            ? AppThemeCatalog.DefaultThemeId
            : s.General.ThemeId.Trim().ToLowerInvariant();
        SelectedThemeLabel = AppThemeCatalog.Get(SelectedThemeId).DisplayName;
        foreach (var opt in ThemeOptions)
            opt.RefreshSelection(SelectedThemeId);
        RefreshThemedLogo();

        ShowStartupToast = s.General.ShowStartupToast;
        ShowAutocorrectUndoToast = s.General.ShowAutocorrectUndoToast;
        CheckForUpdatesOnStartup = s.General.CheckForUpdatesOnStartup;
        AppVersionDisplay = UpdateService.GetCurrentVersionDisplay();
        FixHotkey = s.General.FixHotkey;
        UndoHotkey = s.General.UndoHotkey;
        DictationHotkey = s.General.DictationHotkey;
        RewriteHotkey = s.General.RewriteHotkey;
        PauseHotkey = s.General.PauseHotkey;
        ClipboardFixHotkey = s.General.ClipboardFixHotkey ?? "Win+Alt+C";
        PrivacyHotkey = s.General.PrivacyHotkey ?? "Win+Alt+Shift+P";
        ClipboardFixPasteAfter = s.General.ClipboardFixPasteAfter;
        StartWithWindows = s.General.StartWithWindows || StartupRegistration.IsEnabled();
        LearnLocalRulesFromAccepts = s.General.LearnLocalRulesFromAccepts;
        PrivacyDefaultMinutes = s.General.PrivacyDefaultMinutes is 15 or 30 or 60 or 120 or 0
            ? s.General.PrivacyDefaultMinutes
            : 30;
        PrivacyDurationLabel = PrivacyMinutesToLabel(PrivacyDefaultMinutes);
        CollectUsageStats = s.General.CollectUsageStats;
        ShowWeeklyDigest = s.General.ShowWeeklyDigest;
        ReviewCardScale = s.General.ReviewCardScale is >= 1.0 and <= 1.5
            ? s.General.ReviewCardScale
            : 1.0;
        ReviewCardScaleLabel = ScaleValueToLabel(ReviewCardScale);
        RefreshPrivacyUi();

        DictationEnabled = s.Dictation.Enabled;
        TriggerMode = s.Dictation.TriggerMode;
        ConfirmMode = s.Dictation.ConfirmMode;
        AutoWatchVoiceTyping = s.Dictation.AutoWatchVoiceTyping;
        UseWinHHook = s.Dictation.UseWinHHook;
        AutoDelayMs = s.Dictation.AutoDelayMs;
        MultiUtteranceQuietMs = s.Dictation.MultiUtteranceQuietMs;
        MinChars = s.Dictation.MinChars;
        MaxChars = s.Dictation.MaxChars;
        Scope = s.Dictation.Scope;
        PreferSelection = s.Dictation.PreferSelection;

        FixQuality = string.IsNullOrWhiteSpace(s.Grammar.FixQuality) ? "perfect" : s.Grammar.FixQuality;
        Provider = s.Grammar.Provider;
        ProviderOrder = s.Grammar.ProviderOrder;
        Language = string.IsNullOrWhiteSpace(s.Grammar.Language) ? "en-US" : s.Grammar.Language.Trim();
        SelectedLanguage = LanguageCatalog.FindOrCustom(Language);
        // Keep a custom option visible if user saved an unknown code.
        if (!LanguageOptions.Any(o =>
                string.Equals(o.Code, SelectedLanguage.Code, StringComparison.OrdinalIgnoreCase)))
            LanguageOptions.Insert(1, SelectedLanguage);
        UseLocalRules = s.Grammar.UseLocalRules;
        UsePunctuation = s.Grammar.UsePunctuation;
        UseLightPolish = s.Grammar.UseLightPolish;
        LocalRulesFile = s.Grammar.LocalRulesFile;
        OpenAiModel = s.Grammar.OpenAiModel;
        ChatGptInstruction = s.Grammar.ChatGptInstruction;
        MergeNearbyMistakes = s.Grammar.MergeNearbyMistakes;
        NearbyMergeMaxGapChars = s.Grammar.NearbyMergeMaxGapChars;
        UseWritingSuggestions = s.Grammar.UseWritingSuggestions;
        WritingInstruction = s.Grammar.WritingInstruction;

        AutocorrectEnabled = s.Autocorrect.Enabled;
        AutocorrectStrictness = s.Autocorrect.Strictness;
        UseCurated = s.Autocorrect.UseCurated;
        UseImportedLibrary = s.Autocorrect.UseImportedLibrary;
        UseDictionaryDistance = s.Autocorrect.UseDictionaryDistance;
        UseLearned = s.Autocorrect.UseLearned;
        CuratedFile = s.Autocorrect.CuratedFile;
        WordlistFile = s.Autocorrect.WordlistFile;
        ExcludedProcesses = s.Autocorrect.ExcludedProcesses;
        AppProfilesText = AppProfileStore.Format(s.Autocorrect.AppProfiles);
        ProtectZones = s.Autocorrect.ProtectZones;
        SnippetsEnabled = s.Snippets.Enabled;
        SnippetsFileName = s.Snippets.FileName;
        RefreshSnippetsHint();
        AutoLearnEnabled = s.AutoLearn.Enabled;
        LearnMode = s.AutoLearn.LearnMode;
        MinOccurrences = s.AutoLearn.MinOccurrences;
        RefreshCuratedHint();
        RefreshImportedHint();
        RefreshWordlistHint();

        var creds = PersonalApiKeyStore.LoadRaw();
        OpenAiApiKey = creds.ApiKey ?? "";
        LlmProvider = string.IsNullOrWhiteSpace(creds.Provider) ? LlmProviderCatalog.OpenAi : creds.Provider;
        var info = LlmProviderCatalog.Get(LlmProvider);
        LlmBaseUrl = string.IsNullOrWhiteSpace(creds.BaseUrl) ? info.DefaultBaseUrl : creds.BaseUrl!;
        LlmProviderHint = info.Hint;
        if (!string.IsNullOrWhiteSpace(creds.Model))
            OpenAiModel = creds.Model!;
        else if (string.IsNullOrWhiteSpace(OpenAiModel))
            OpenAiModel = info.DefaultModel;
        RefreshModelPresets(LlmProvider);
        ShowOpenAiApiKey = false;
        ApiKeyPath = DataPaths.FindOrCreatePersonalApiKeyPath();

        SettingsPath = _store.Path;
        RecordingTarget = "";
        StatusMessage = "";
        HotkeyHint = "Click Record, then press a chord (needs Ctrl/Alt/Win/Shift + a key). Esc cancels.";
    }

    public AppSettings ToSettings() => new()
    {
        General = new GeneralSettings
        {
            ShowStartupToast = ShowStartupToast,
            ShowAutocorrectUndoToast = ShowAutocorrectUndoToast,
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            UpdateFeedUrl = _store.Current.General.UpdateFeedUrl ?? "",
            FixHotkey = FixHotkey.Trim(),
            UndoHotkey = UndoHotkey.Trim(),
            DictationHotkey = DictationHotkey.Trim(),
            RewriteHotkey = RewriteHotkey.Trim(),
            PauseHotkey = PauseHotkey.Trim(),
            ClipboardFixHotkey = ClipboardFixHotkey.Trim(),
            PrivacyHotkey = PrivacyHotkey.Trim(),
            ClipboardFixPasteAfter = ClipboardFixPasteAfter,
            StartWithWindows = StartWithWindows,
            LearnLocalRulesFromAccepts = LearnLocalRulesFromAccepts,
            PrivacyDefaultMinutes = PrivacyDefaultMinutes is 15 or 30 or 60 or 120 or 0
                ? PrivacyDefaultMinutes
                : 30,
            CollectUsageStats = CollectUsageStats,
            ShowWeeklyDigest = ShowWeeklyDigest,
            ReviewCardScale = ReviewCardScale is >= 1.0 and <= 1.5 ? ReviewCardScale : 1.0,
            LastRewriteInstruction = _store.Current.General.LastRewriteInstruction ?? "",
            ThemeId = string.IsNullOrWhiteSpace(SelectedThemeId)
                ? AppThemeCatalog.DefaultThemeId
                : SelectedThemeId.Trim().ToLowerInvariant()
        },
        Dictation = new DictationSettings
        {
            Enabled = DictationEnabled,
            TriggerMode = TriggerMode,
            ConfirmMode = ConfirmMode,
            AutoWatchVoiceTyping = AutoWatchVoiceTyping,
            UseWinHHook = UseWinHHook,
            AutoDelayMs = AutoDelayMs,
            UiClosedDelayMs = _store.Current.Dictation.UiClosedDelayMs,
            MinArmMs = _store.Current.Dictation.MinArmMs,
            UiGoneTicksRequired = _store.Current.Dictation.UiGoneTicksRequired,
            MultiUtteranceQuietMs = MultiUtteranceQuietMs,
            SkipAutoFixIfFocusMoved = _store.Current.Dictation.SkipAutoFixIfFocusMoved,
            MinChars = MinChars,
            MaxChars = MaxChars,
            Scope = Scope,
            PreferSelection = PreferSelection
        },
        Grammar = new GrammarSettings
        {
            FixQuality = FixQuality,
            Provider = Provider,
            ProviderOrder = ProviderOrder,
            Language = (SelectedLanguage?.Code ?? Language).Trim(),
            UseLocalRules = UseLocalRules,
            UsePunctuation = UsePunctuation,
            UseLightPolish = UseLightPolish,
            LocalRulesFile = LocalRulesFile,
            OpenAiModel = OpenAiModel,
            ChatGptInstruction = ChatGptInstruction,
            UseWritingSuggestions = UseWritingSuggestions,
            WritingInstruction = WritingInstruction,
            MergeNearbyMistakes = MergeNearbyMistakes,
            NearbyMergeMaxGapChars = NearbyMergeMaxGapChars
        },
        Autocorrect = new AutocorrectSettings
        {
            Enabled = AutocorrectEnabled,
            Strictness = AutocorrectStrictness,
            UseCurated = UseCurated,
            UseImportedLibrary = UseImportedLibrary,
            UseDictionaryDistance = UseDictionaryDistance,
            UseLearned = UseLearned,
            CuratedFile = CuratedFile.Trim(),
            WordlistFile = WordlistFile.Trim(),
            ExcludedProcesses = ExcludedProcesses.Trim(),
            AppProfiles = AppProfileStore.Parse(AppProfilesText),
            ProtectZones = ProtectZones
        },
        AutoLearn = new AutoLearnSettings
        {
            Enabled = AutoLearnEnabled,
            LearnMode = LearnMode,
            MinOccurrences = MinOccurrences
        },
        Snippets = new SnippetSettings
        {
            Enabled = SnippetsEnabled,
            Prefix = ";",
            FileName = string.IsNullOrWhiteSpace(SnippetsFileName) ? "Snippets.json" : SnippetsFileName.Trim()
        }
    };

    private void RefreshSnippetsHint()
    {
        try
        {
            SnippetsPathHint = DataPaths.FindSnippets()
                               ?? DataPaths.FindOrCreateSnippetsPath();
        }
        catch
        {
            SnippetsPathHint = "(Snippets.json)";
        }
    }

    private void RefreshCuratedHint()
    {
        CuratedPath = DataPaths.FindAutocorrectCurated(CuratedFile)
                      ?? DataPaths.FindOrCreateAutocorrectCuratedPath(CuratedFile);
        try
        {
            if (System.IO.File.Exists(CuratedPath))
            {
                var n = 0;
                foreach (var line in System.IO.File.ReadLines(CuratedPath))
                {
                    if (CuratedStore.TryParseLine(line, out _, out _))
                        n++;
                }
                CuratedCountHint = $"{n} curated pairs loaded from file";
            }
            else
            {
                CuratedCountHint = "Curated file not found yet — will use path above when created";
            }
        }
        catch
        {
            CuratedCountHint = "Could not read curated file";
        }
    }

    private void RefreshImportedHint()
    {
        ImportedPath = DataPaths.FindAutocorrectImported()
                       ?? DataPaths.FindOrCreateAutocorrectImportedPath();
        var ahk = DataPaths.FindAhkHotstringsLibrary();
        try
        {
            if (System.IO.File.Exists(ImportedPath))
            {
                var json = System.IO.File.ReadAllText(ImportedPath);
                var entries = System.Text.RegularExpressions.Regex.Matches(
                    json, "\"trigger\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
                var nullifiers = 0;
                try
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<ImportedLibraryFile>(json);
                    if (parsed is not null)
                    {
                        entries = parsed.Entries.Count;
                        nullifiers = parsed.Nullifiers.Count;
                    }
                }
                catch { /* keep regex count */ }
                ImportedCountHint = ahk is null
                    ? $"{entries} entries · {nullifiers} nullifiers (AHK source not found)"
                    : $"{entries} entries · {nullifiers} nullifiers · AHK: {System.IO.Path.GetFileName(ahk)}";
            }
            else
            {
                ImportedCountHint = ahk is null
                    ? "No cache yet — AHK Core/AutoCorrectHotstrings.ahk not found"
                    : "No cache yet — enable library + Save, or Re-import";
            }
        }
        catch
        {
            ImportedCountHint = "Could not read imported cache";
        }
    }

    private void RefreshWordlistHint()
    {
        WordlistPath = DataPaths.FindWordlist(WordlistFile) ?? "(not found)";
        try
        {
            if (WordlistPath != "(not found)" && System.IO.File.Exists(WordlistPath))
            {
                var n = 0;
                foreach (var _ in System.IO.File.ReadLines(WordlistPath))
                    n++;
                WordlistCountHint = $"{n:N0} lines in wordlist (loads when Dictionary toggle is on)";
            }
            else
            {
                WordlistCountHint = "Wordlist not found under Data/";
            }
        }
        catch
        {
            WordlistCountHint = "Could not read wordlist";
        }
    }

    [RelayCommand]
    private void ReimportLibrary()
    {
        try
        {
            var ahk = DataPaths.FindAhkHotstringsLibrary();
            if (ahk is null)
            {
                StatusMessage = "AHK library not found (Core/AutoCorrectHotstrings.ahk).";
                return;
            }

            var file = AhkHotstringImporter.EnsureImported(force: true);
            RefreshImportedHint();
            StatusMessage =
                $"Imported {file.Entries.Count} hotstrings + {file.Nullifiers.Count} nullifiers from AHK. Enable the toggle + Save to use.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Import failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateHotkeys(out var error))
        {
            StatusMessage = error;
            return;
        }

        var settings = ToSettings();
        _store.Save(settings);
        try
        {
            ApiKeyPath = PersonalApiKeyStore.Save(OpenAiApiKey, OpenAiModel, LlmProvider, LlmBaseUrl);
        }
        catch (Exception ex)
        {
            StatusMessage = "Settings saved, but API key file failed: " + ex.Message;
            return;
        }

        try
        {
            StartupRegistration.SetEnabled(StartWithWindows);
        }
        catch (Exception ex)
        {
            StatusMessage = "Saved, but Start with Windows failed: " + ex.Message;
            _onSaved(settings);
            return;
        }

        _onSaved(settings);
        StatusMessage = PersonalApiKeyStore.LooksLikePlaceholder(OpenAiApiKey)
            ? "Saved. Add a real API key under Settings → Grammar to use Perfect Fix / AI."
            : "Saved (including API key).";
        RecordingTarget = "";
    }

    [RelayCommand]
    private void SelectTheme(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return;
        ApplyThemeSelection(themeId);
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        LoadFrom(new AppSettings());
        ThemeService.Apply(SelectedThemeId);
        StatusMessage = "Defaults loaded — click Save to keep.";
    }

    [RelayCommand]
    private void RecordFix() => BeginRecord("fix");

    [RelayCommand]
    private void RecordUndo() => BeginRecord("undo");

    [RelayCommand]
    private void RecordDictation() => BeginRecord("dictation");

    [RelayCommand]
    private void RecordRewrite() => BeginRecord("rewrite");

    [RelayCommand]
    private void RecordPause() => BeginRecord("pause");

    [RelayCommand]
    private void RecordClipboardFix() => BeginRecord("clipboard");

    [RelayCommand]
    private void RecordPrivacy() => BeginRecord("privacy");

    [RelayCommand]
    private void ClearUndo()
    {
        UndoHotkey = "";
        StatusMessage = "Undo hotkey cleared — click Save.";
    }

    [RelayCommand]
    private void ClearClipboardFix()
    {
        ClipboardFixHotkey = "";
        StatusMessage = "Clipboard Fix hotkey cleared — click Save.";
    }

    [RelayCommand]
    private void ClearPrivacy()
    {
        PrivacyHotkey = "";
        StatusMessage = "Privacy hotkey cleared — click Save.";
    }

    [RelayCommand]
    private void CancelRecord()
    {
        RecordingTarget = "";
        HotkeyHint = "Recording cancelled.";
        OnPropertyChanged(nameof(IsRecording));
    }

    private void BeginRecord(string target)
    {
        RecordingTarget = target;
        HotkeyHint = "Listening… press your chord now (Esc to cancel).";
        OnPropertyChanged(nameof(IsRecording));
        StatusMessage = "";
    }

    /// <summary>Called from SettingsWindow when capturing a key chord.</summary>
    public bool TryAcceptRecordedChord(bool win, bool ctrl, bool alt, bool shift, uint vk)
    {
        if (string.IsNullOrEmpty(RecordingTarget))
            return false;

        if (vk is 0x1B) // Esc
        {
            CancelRecord();
            return true;
        }

        if (!win && !ctrl && !alt && !shift)
        {
            HotkeyHint = "Add a modifier (Ctrl / Alt / Win / Shift) plus a key.";
            return true;
        }

        var display = HotkeyChord.Format(win, ctrl, alt, shift, vk);
        switch (RecordingTarget)
        {
            case "fix":
                FixHotkey = display;
                break;
            case "undo":
                UndoHotkey = display;
                break;
            case "dictation":
                DictationHotkey = display;
                break;
            case "rewrite":
                RewriteHotkey = display;
                break;
            case "pause":
                PauseHotkey = display;
                break;
            case "clipboard":
                ClipboardFixHotkey = display;
                break;
            case "privacy":
                PrivacyHotkey = display;
                break;
        }

        RecordingTarget = "";
        HotkeyHint = "Captured " + display + " — click Save to apply.";
        OnPropertyChanged(nameof(IsRecording));
        return true;
    }

    private bool ValidateHotkeys(out string error)
    {
        error = "";
        if (HotkeyChord.Parse(FixHotkey) is null)
        {
            error = "Fix hotkey invalid. Example: Win+Alt+F";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(UndoHotkey) && HotkeyChord.Parse(UndoHotkey) is null)
        {
            error = "Undo hotkey invalid (or clear it). Example: Win+Alt+Shift+F";
            return false;
        }
        if (HotkeyChord.Parse(DictationHotkey) is null)
        {
            error = "Dictation hotkey invalid. Example: Win+H (recommended for Voice Typing)";
            return false;
        }
        if (string.IsNullOrWhiteSpace(RewriteHotkey) || HotkeyChord.Parse(RewriteHotkey) is null)
        {
            error = "Rewrite hotkey invalid. Example: Win+Alt+R";
            return false;
        }
        if (string.IsNullOrWhiteSpace(PauseHotkey) || HotkeyChord.Parse(PauseHotkey) is null)
        {
            error = "Pause hotkey invalid. Example: Win+Alt+P";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(ClipboardFixHotkey) && HotkeyChord.Parse(ClipboardFixHotkey) is null)
        {
            error = "Clipboard Fix hotkey invalid (or clear it). Example: Win+Alt+C";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(PrivacyHotkey) && HotkeyChord.Parse(PrivacyHotkey) is null)
        {
            error = "Privacy hotkey invalid (or clear it). Example: Win+Alt+Shift+P";
            return false;
        }

        var fix = HotkeyChord.Parse(FixHotkey)!;
        var dict = HotkeyChord.Parse(DictationHotkey)!;
        var rewrite = HotkeyChord.Parse(RewriteHotkey)!;
        var pause = HotkeyChord.Parse(PauseHotkey)!;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            fix.Display, dict.Display, rewrite.Display, pause.Display
        };
        if (used.Count < 4)
        {
            error = "Fix, Dictation, Rewrite, and Pause hotkeys must all be different.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(UndoHotkey))
        {
            var undo = HotkeyChord.Parse(UndoHotkey)!;
            if (!used.Add(undo.Display))
            {
                error = "Undo hotkey must differ from Fix, Dictation, Rewrite, and Pause.";
                return false;
            }
        }
        if (!string.IsNullOrWhiteSpace(ClipboardFixHotkey))
        {
            var clip = HotkeyChord.Parse(ClipboardFixHotkey)!;
            if (!used.Add(clip.Display))
            {
                error = "Clipboard Fix hotkey must differ from the other chords.";
                return false;
            }
        }
        if (!string.IsNullOrWhiteSpace(PrivacyHotkey))
        {
            var privacy = HotkeyChord.Parse(PrivacyHotkey)!;
            if (!used.Add(privacy.Display))
            {
                error = "Privacy hotkey must differ from the other chords.";
                return false;
            }
        }
        return true;
    }

    public void RefreshLicenseUi()
    {
        var info = _license.Refresh();
        LicenseStatusLine = info.StatusLine;
        LicenseDetailLine = info.DetailLine;
        LicenseFilePath = DataPaths.FindOrCreateLicenseKeyPath();
        LicenseKeyInput = _license.ReadSavedKey() ?? "";
    }

    [RelayCommand]
    private void ActivateLicense()
    {
        var err = _license.Activate(LicenseKeyInput ?? "");
        if (err is not null)
        {
            StatusMessage = err;
            RefreshLicenseUi();
            return;
        }

        RefreshLicenseUi();
        StatusMessage = "License activated — " + LicenseStatusLine;
    }

    [RelayCommand]
    private async Task ActivateOnlineAsync()
    {
        StatusMessage = "Checking license server…";
        var (ok, msg) = await _license.ActivateOnlineAsync(LicenseEmail ?? "").ConfigureAwait(true);
        RefreshLicenseUi();
        StatusMessage = ok ? "Online license OK — " + msg : "Online activate failed: " + msg;
    }

    [RelayCommand]
    private async Task SyncLicenseAsync()
    {
        StatusMessage = "Syncing with license server…";
        await _license.RefreshOnlineAsync().ConfigureAwait(true);
        RefreshLicenseUi();
        StatusMessage = "Synced — " + LicenseStatusLine;
    }

    [RelayCommand]
    private void ClearLicense()
    {
        _license.ClearLicense();
        RefreshLicenseUi();
        StatusMessage = "License cleared. Trial/core rules apply.";
    }

    private bool _awaitingSubscribeReturn;
    private DateTime _subscribeOpenedUtc = DateTime.MinValue;

    [RelayCommand]
    private void OpenWebsite() =>
        OpenExternal(WebsiteUrl, "Opened AutoCorrect website.");

    [RelayCommand]
    private void OpenSupportPage() =>
        OpenExternal(SupportUrl, "Opened support page.");

    [RelayCommand]
    private void OpenAlternateWebsite() =>
        OpenExternal(AlternateWebsiteUrl, "Opened GitHub Pages mirror.");

    [RelayCommand]
    private void OpenSocialX() =>
        OpenExternal(SocialXUrl, "Opened X (@phd_in_sleeping).");

    [RelayCommand]
    private void OpenSocialGitHub() =>
        OpenExternal(SocialGitHubUrl, "Opened GitHub (BADATCODING12345678910).");

    [RelayCommand]
    private void OpenBuyPage() =>
        OpenExternal(BuyUrl, "Opened Lifetime checkout. After PayPal, return here — we'll sync automatically.", awaitReturn: true);

    [RelayCommand]
    private void OpenSubscribePersonal() =>
        OpenExternal(SubscribeBaseUrl + "personal", "Opened Personal yearly. After PayPal, return here — we'll sync automatically.", awaitReturn: true);

    [RelayCommand]
    private void OpenSubscribePro() =>
        OpenExternal(SubscribeBaseUrl + "pro", "Opened Pro yearly. After PayPal, return here — we'll sync automatically.", awaitReturn: true);

    [RelayCommand]
    private void OpenSubscribeTeam() =>
        OpenExternal(SubscribeBaseUrl + "team", "Opened Team monthly. After PayPal, return here — we'll sync automatically.", awaitReturn: true);

    private void OpenExternal(string url, string okMessage, bool awaitReturn = false)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            StatusMessage = okMessage;
            if (awaitReturn)
            {
                _awaitingSubscribeReturn = true;
                _subscribeOpenedUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Couldn't open browser: " + ex.Message;
        }
    }

    /// <summary>Called when Settings is re-activated after a Subscribe/checkout tab.</summary>
    public async Task OnWindowActivatedAsync()
    {
        if (!_awaitingSubscribeReturn)
            return;
        if ((DateTime.UtcNow - _subscribeOpenedUtc).TotalSeconds < 4)
            return;

        _awaitingSubscribeReturn = false;
        StatusMessage = "Checking license after checkout…";
        try
        {
            if (!string.IsNullOrWhiteSpace(LicenseEmail))
            {
                var (ok, msg) = await _license.ActivateOnlineAsync(LicenseEmail).ConfigureAwait(true);
                RefreshLicenseUi();
                StatusMessage = ok
                    ? "License synced after checkout — " + msg
                    : "Couldn't confirm yet — " + msg + " Try Activate online / Sync now.";
            }
            else
            {
                await _license.RefreshOnlineAsync().ConfigureAwait(true);
                RefreshLicenseUi();
                StatusMessage = "Synced after checkout — " + LicenseStatusLine +
                    (LicenseStatusLine.Contains("Trial", StringComparison.OrdinalIgnoreCase)
                        ? " Enter the email you used at PayPal, then Activate online."
                        : "");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Post-checkout sync failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"AutoCorrect-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip");
            if (_hostWindow?.StorageProvider is { } sp)
            {
                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export AutoCorrect backup",
                    SuggestedFileName = Path.GetFileName(path),
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }
                    ]
                }).ConfigureAwait(true);
                if (file is null)
                {
                    StatusMessage = "Export cancelled.";
                    return;
                }

                path = file.Path.LocalPath;
            }

            UserBackupService.ExportZip(path);
            StatusMessage = "Backup exported to " + path;
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        try
        {
            if (_hostWindow?.StorageProvider is not { } sp)
            {
                StatusMessage = "Open Settings from the tray to import a backup.";
                return;
            }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import AutoCorrect backup",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }
                ]
            }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                StatusMessage = "Import cancelled.";
                return;
            }

            var n = UserBackupService.ImportZip(files[0].Path.LocalPath);
            LoadFrom(_store.Load());
            _engine?.Configure(ToSettings().Autocorrect);
            StatusMessage = "Imported " + n + " file(s). Click Save if you want settings kept.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Import failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void PromoteLearnedToCurated()
    {
        try
        {
            var store = _contributions ?? new LearnContributionStore();
            store.Load();
            var curated = DataPaths.FindAutocorrectCurated(CuratedFile)
                          ?? DataPaths.FindOrCreateAutocorrectCuratedPath(CuratedFile);
            var n = CuratedPromotion.PromoteFromContributions(store, curated, minCount: 3);
            _engine?.ReloadCurated(CuratedFile);
            RefreshCuratedHint();
            StatusMessage = n == 0
                ? "No tallies ≥3 to promote yet."
                : $"Promoted {n} pair(s) into curated list.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Promote failed: " + ex.Message;
        }
    }
}
