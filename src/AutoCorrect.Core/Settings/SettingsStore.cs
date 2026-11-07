using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private AppSettings _current = new();
    private string? _path;

    public string Path => _path ??= ResolvePath();
    public AppSettings Current
    {
        get { lock (_gate) return Clone(_current); }
    }

    public event EventHandler? Changed;

    public AppSettings Load()
    {
        lock (_gate)
        {
            _path = ResolvePath();
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded is not null)
                        _current = Normalize(loaded);
                }
                else
                {
                    _current = new AppSettings();
                    Save_NoLock(_current);
                }
            }
            catch
            {
                _current = new AppSettings();
            }
            return Clone(_current);
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _current = Normalize(Clone(settings));
            Save_NoLock(_current);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save_NoLock(AppSettings settings)
    {
        _path ??= ResolvePath();
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
    }

    /// <summary>
    /// Always prefer <c>%LocalAppData%\AutoCorrect\native-settings.json</c> so reinstalls
    /// (and shipped <c>Data/</c> next to the exe) do not wipe or shadow user preferences.
    /// </summary>
    public static string ResolvePath()
    {
        var appData = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");
        Directory.CreateDirectory(appData);
        var preferred = System.IO.Path.Combine(appData, "native-settings.json");
        if (File.Exists(preferred))
            return preferred;

        // One-time migrate: older builds stored settings under the first Data/ folder found.
        foreach (var root in DataPaths.CandidateRoots())
        {
            var legacy = System.IO.Path.Combine(root, "Data", "native-settings.json");
            if (!File.Exists(legacy))
                continue;
            try
            {
                if (!System.IO.Path.GetFullPath(legacy).Equals(
                        System.IO.Path.GetFullPath(preferred), StringComparison.OrdinalIgnoreCase))
                    File.Copy(legacy, preferred, overwrite: false);
            }
            catch
            {
                /* keep preferred path even if copy fails */
            }
            break;
        }

        return preferred;
    }

    private static AppSettings Normalize(AppSettings s)
    {
        s.Dictation.TriggerMode = NormalizeList(s.Dictation.TriggerMode, "both", "hotkey", "auto", "both");
        s.Dictation.ConfirmMode = NormalizeList(s.Dictation.ConfirmMode, "preview", "confirm", "silent", "preview");
        s.Dictation.Scope = NormalizeList(s.Dictation.Scope, "all", "selection", "sentence", "paragraph", "all");
        s.Dictation.AutoDelayMs = Math.Clamp(s.Dictation.AutoDelayMs, 0, 5000);
        s.Dictation.UiClosedDelayMs = Math.Clamp(s.Dictation.UiClosedDelayMs, 0, 2000);
        s.Dictation.MinArmMs = Math.Clamp(s.Dictation.MinArmMs, 500, 8000);
        s.Dictation.UiGoneTicksRequired = Math.Clamp(s.Dictation.UiGoneTicksRequired, 2, 20);
        s.Dictation.MultiUtteranceQuietMs = Math.Clamp(s.Dictation.MultiUtteranceQuietMs, 0, 15000);
        s.Dictation.MinChars = Math.Clamp(s.Dictation.MinChars, 1, 200);
        s.Dictation.MaxChars = Math.Clamp(s.Dictation.MaxChars, 100, 50000);

        s.Grammar.Provider = NormalizeList(s.Grammar.Provider, "languagetool", "languagetool", "chatgpt", "both", "none");
        s.Grammar.ProviderOrder = NormalizeList(
            s.Grammar.ProviderOrder,
            "languagetool_then_chatgpt",
            "languagetool_then_chatgpt",
            "chatgpt_then_languagetool");
        s.Grammar.FixQuality = NormalizeList(s.Grammar.FixQuality, "perfect", "perfect", "standard");
        if (string.IsNullOrWhiteSpace(s.Grammar.Language))
            s.Grammar.Language = "en-US";
        s.Grammar.NearbyMergeMaxGapChars = Math.Clamp(s.Grammar.NearbyMergeMaxGapChars, 0, 8);
        if (string.IsNullOrWhiteSpace(s.Grammar.ChatGptInstruction) ||
            (s.Grammar.ChatGptInstruction.Contains("Do not add commentary. Return only the corrected text.", StringComparison.Ordinal) &&
             !s.Grammar.ChatGptInstruction.Contains("wrote \"in\"", StringComparison.Ordinal)))
        {
            // Migrate stale default instruction (pre and←in note) to the shared constant.
            s.Grammar.ChatGptInstruction = AutoCorrect.Core.Correction.CorrectionOptions.DefaultChatGptInstruction;
        }

        if (string.IsNullOrWhiteSpace(s.General.FixHotkey))
            s.General.FixHotkey = "Win+Alt+F";
        s.General.UndoHotkey ??= "Win+Alt+Shift+F";
        if (string.IsNullOrWhiteSpace(s.General.DictationHotkey))
            s.General.DictationHotkey = "Win+H";
        if (string.IsNullOrWhiteSpace(s.General.RewriteHotkey))
            s.General.RewriteHotkey = "Win+Alt+R";
        s.General.LastRewriteInstruction ??= "";
        if (string.IsNullOrWhiteSpace(s.General.PauseHotkey))
            s.General.PauseHotkey = "Win+Alt+P";
        s.General.ClipboardFixHotkey ??= "Win+Alt+C";
        s.General.PrivacyHotkey ??= "Win+Alt+Shift+P";
        if (string.IsNullOrWhiteSpace(s.General.ThemeId))
            s.General.ThemeId = "light-lavender";
        else
            s.General.ThemeId = s.General.ThemeId.Trim().ToLowerInvariant();

        s.Autocorrect ??= new AutocorrectSettings();
        s.Autocorrect.Strictness = NormalizeList(s.Autocorrect.Strictness, "balanced", "strict", "balanced");
        if (string.IsNullOrWhiteSpace(s.Autocorrect.CuratedFile))
            s.Autocorrect.CuratedFile = "AutocorrectCurated.txt";
        if (string.IsNullOrWhiteSpace(s.Autocorrect.WordlistFile))
            s.Autocorrect.WordlistFile = "unigram_freq_list_filtered_89k.csv";
        s.Autocorrect.ExcludedProcesses ??=
            "AutoCorrect.App,WindowsTerminal,wt,cmd,powershell,pwsh,Code,devenv";
        s.Autocorrect.AppProfiles ??= [];

        s.AutoLearn ??= new AutoLearnSettings();
        s.AutoLearn.LearnMode = NormalizeList(s.AutoLearn.LearnMode, "confirm", "manual", "confirm", "silent");
        s.AutoLearn.MinOccurrences = Math.Clamp(s.AutoLearn.MinOccurrences, 1, 20);

        s.Snippets ??= new SnippetSettings();
        if (string.IsNullOrWhiteSpace(s.Snippets.Prefix))
            s.Snippets.Prefix = ";";
        if (string.IsNullOrWhiteSpace(s.Snippets.FileName))
            s.Snippets.FileName = "Snippets.json";

        return s;
    }

    private static string NormalizeList(string? value, string fallback, params string[] allowed)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return allowed.Any(a => a == v) ? v : fallback;
    }

    private static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, JsonOptions), JsonOptions)
        ?? new AppSettings();
}
