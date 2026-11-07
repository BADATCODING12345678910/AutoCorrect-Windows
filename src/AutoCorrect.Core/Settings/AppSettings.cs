namespace AutoCorrect.Core.Settings;

/// <summary>Native app settings (JSON). Mirrors the useful AHK [DictationFix] knobs.</summary>
public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public DictationSettings Dictation { get; set; } = new();
    public GrammarSettings Grammar { get; set; } = new();
    public AutocorrectSettings Autocorrect { get; set; } = new();
    public AutoLearnSettings AutoLearn { get; set; } = new();
    public SnippetSettings Snippets { get; set; } = new();
}

/// <summary>Intentional expansions (;sig → signature). Separate from typo autocorrect.</summary>
public sealed class SnippetSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Trigger prefix character. v1 supports <c>;</c> only.</summary>
    public string Prefix { get; set; } = ";";

    public string FileName { get; set; } = "Snippets.json";
}

/// <summary>Learn typo→fix from backspace corrections (native AutoLearner).</summary>
public sealed class AutoLearnSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>manual | confirm | silent</summary>
    public string LearnMode { get; set; } = "confirm";

    public int MinOccurrences { get; set; } = 3;
}

/// <summary>High-confidence as-you-type autocorrect (Phase A). Never speculative AI.</summary>
public sealed class AutocorrectSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>strict | balanced — Balanced includes more imported AHK shapes (recommended default).</summary>
    public string Strictness { get; set; } = "balanced";

    /// <summary>Shipped allowlist (<c>Data/AutocorrectCurated.txt</c>).</summary>
    public bool UseCurated { get; set; } = true;

    /// <summary>Imported AHK hotstring library (~5k typos). On by default for coverage.</summary>
    public bool UseImportedLibrary { get; set; } = true;

    /// <summary>Dictionary unique-1-edit (+ keyboard-adjacent disambiguation). On by default.</summary>
    public bool UseDictionaryDistance { get; set; } = true;

    /// <summary>Apply auto-learned pairs as-you-type (from AutoLearn). Default on.</summary>
    public bool UseLearned { get; set; } = true;

    /// <summary>Relative name under Data/ or absolute path.</summary>
    public string CuratedFile { get; set; } = "AutocorrectCurated.txt";

    /// <summary>Wordlist for unique-1-edit (csv <c>word,freq</c> or one word per line).</summary>
    public string WordlistFile { get; set; } = "unigram_freq_list_filtered_89k.csv";

    /// <summary>Comma-separated process names to skip (no extension required).</summary>
    public string ExcludedProcesses { get; set; } =
        "AutoCorrect.App,WindowsTerminal,wt,cmd,powershell,pwsh,Code,devenv,WindowsTerminal";

    /// <summary>
    /// Per-app overrides (autocorrect on/off, strictness, Fix on/off, source toggles).
    /// ExcludedProcesses still means “autocorrect off”; profiles can be more precise.
    /// </summary>
    public List<AppProfile> AppProfiles { get; set; } = [];

    /// <summary>Skip emails, URLs, @handles, #tags, paths, code-ish tokens.</summary>
    public bool ProtectZones { get; set; } = true;
}

public sealed class GeneralSettings
{
    /// <summary>Show startup tray toast (helps new users find Settings in the tray).</summary>
    public bool ShowStartupToast { get; set; } = true;

    /// <summary>Quietly check the update feed a few seconds after startup.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Override update feed URL. Empty = default Supabase public <c>latest.json</c>.
    /// </summary>
    public string UpdateFeedUrl { get; set; } = "";

    /// <summary>Fix chord, e.g. Win+Alt+F. Requires a modifier.</summary>
    public string FixHotkey { get; set; } = "Win+Alt+F";

    /// <summary>Undo chord, e.g. Win+Alt+Shift+F. Blank disables.</summary>
    public string UndoHotkey { get; set; } = "Win+Alt+Shift+F";

    /// <summary>
    /// Observed dictation toggle (default Win+H for Windows Voice Typing).
    /// Changing this away from Win+H means auto-session won’t follow Voice Typing unless you use that chord.
    /// </summary>
    public string DictationHotkey { get; set; } = "Win+H";

    /// <summary>Phase D — selection rewrite chord (highlight → NL instruction → replace).</summary>
    public string RewriteHotkey { get; set; } = "Win+Alt+R";

    /// <summary>Last freeform rewrite instruction (prompt UI remembers it).</summary>
    public string LastRewriteInstruction { get; set; } = "";

    /// <summary>Toggle as-you-type autocorrect pause (does not quit the app).</summary>
    public string PauseHotkey { get; set; } = "Win+Alt+P";

    /// <summary>Clean clipboard text (and optionally paste). Blank disables.</summary>
    public string ClipboardFixHotkey { get; set; } = "Win+Alt+C";

    /// <summary>Toggle Privacy mode (default duration / End). Blank disables.</summary>
    public string PrivacyHotkey { get; set; } = "Win+Alt+Shift+P";

    /// <summary>After cleaning the clipboard, also Ctrl+V into the focused field.</summary>
    public bool ClipboardFixPasteAfter { get; set; } = true;

    /// <summary>Launch AutoCorrect when Windows signs in.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Learn short STT phrase rules when a Fix suggestion is applied.</summary>
    public bool LearnLocalRulesFromAccepts { get; set; } = true;

    /// <summary>
    /// UI theme id (see app Theme catalog). Default <c>light-lavender</c>.
    /// </summary>
    public string ThemeId { get; set; } = "light-lavender";

    /// <summary>After as-you-type autocorrect, show a brief Undo toast.</summary>
    public bool ShowAutocorrectUndoToast { get; set; } = true;

    /// <summary>
    /// Default Privacy mode duration in minutes (15 / 30 / 60 / 120).
    /// 0 = until turned off. Runtime session state is not persisted across restarts.
    /// </summary>
    public int PrivacyDefaultMinutes { get; set; } = 30;

    /// <summary>Opt-in: count local corrections (never uploaded).</summary>
    public bool CollectUsageStats { get; set; }

    /// <summary>When stats are on, show a weekly tray digest toast.</summary>
    public bool ShowWeeklyDigest { get; set; } = true;

    /// <summary>Fix card scale: 1.0 / 1.15 / 1.3 (accessibility).</summary>
    public double ReviewCardScale { get; set; } = 1.0;
}

/// <summary>When / how dictation Fix runs — port of AHK DictationFix trigger + capture.</summary>
public sealed class DictationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>hotkey | auto | both</summary>
    public string TriggerMode { get; set; } = "both";

    /// <summary>confirm | silent | preview — native card is always a review queue; silent auto-applies all.</summary>
    public string ConfirmMode { get; set; } = "preview";

    public bool AutoWatchVoiceTyping { get; set; } = true;
    public bool UseWinHHook { get; set; } = true;
    public int AutoDelayMs { get; set; } = 1200;

    /// <summary>When Voice Typing UI closes, cap the post-session wait (ms) before auto-Fix.</summary>
    public int UiClosedDelayMs { get; set; } = 350;

    /// <summary>
    /// Ignore a second Win+H (and swallow it so Windows does not close Voice Typing)
    /// for this many ms after session start. Stops accidental instant cancel.
    /// </summary>
    public int MinArmMs { get; set; } = 2500;

    /// <summary>Mic-bar must stay gone this many watch ticks (~400ms each) before ui_closed.</summary>
    public int UiGoneTicksRequired { get; set; } = 5;

    /// <summary>
    /// Wait this long after a Voice Typing session ends before auto-Fix, so a quick restart
    /// (multi-utterance) can cancel the pending Fix. 0 = no extra quiet window.
    /// </summary>
    public int MultiUtteranceQuietMs { get; set; } = 2800;

    /// <summary>Skip auto-Fix after dictation if focus left the dictation target app.</summary>
    public bool SkipAutoFixIfFocusMoved { get; set; } = true;

    /// <summary>Low enough for short Slack/chat messages.</summary>
    public int MinChars { get; set; } = 4;
    public int MaxChars { get; set; } = 10000;

    /// <summary>selection | sentence | paragraph | all — native currently captures all; kept for parity/UI.</summary>
    public string Scope { get; set; } = "all";

    public bool PreferSelection { get; set; } = true;
}

/// <summary>Correction pipeline / grammar provider — port of AHK DictationFix provider + polish flags.</summary>
public sealed class GrammarSettings
{
    /// <summary>languagetool | chatgpt | both | none</summary>
    public string Provider { get; set; } = "languagetool";

    /// <summary>languagetool_then_chatgpt | chatgpt_then_languagetool</summary>
    public string ProviderOrder { get; set; } = "languagetool_then_chatgpt";

    /// <summary>
    /// standard = honor Provider combo; perfect = LanguageTool then ChatGPT when a key is available.
    /// </summary>
    public string FixQuality { get; set; } = "perfect";

    public string Language { get; set; } = "en-US";
    public bool UseLocalRules { get; set; } = true;
    public bool UsePunctuation { get; set; } = true;
    public bool UseLightPolish { get; set; } = true;

    /// <summary>Relative name under Data/ or absolute path.</summary>
    public string LocalRulesFile { get; set; } = "DictationLocalRules.txt";

    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    public string ChatGptInstruction { get; set; } =
        AutoCorrect.Core.Correction.CorrectionOptions.DefaultChatGptInstruction;

    /// <summary>Phase B — optional clarity/conciseness pass after correctness. Default off.</summary>
    public bool UseWritingSuggestions { get; set; }

    public string WritingInstruction { get; set; } =
        "Improve clarity and conciseness while preserving the author's meaning and tone. Tighten wordy phrases. Do not add new ideas or commentary. Return only the revised text.";

    /// <summary>Merge nearby mistakes into one card (Phase 1.5).</summary>
    public bool MergeNearbyMistakes { get; set; } = true;

    public int NearbyMergeMaxGapChars { get; set; } = 2;
}
