using AutoCorrect.Core.Settings;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// High-confidence as-you-type matcher: curated + imported AHK + dictionary 1-edit.
/// Never speculative AI.
/// </summary>
public sealed class AutocorrectEngine
{
    private readonly object _gate = new();
    private AutocorrectOptions _options = new();
    private readonly CuratedStore _curated = new();
    private readonly ImportedHotstringStore _imported = new();
    private readonly DictionaryDistanceMatcher _dictionary = new();
    private readonly LearnedStore _learned = new();
    private readonly RemovedCorrectionsStore _removed = new();
    private string _wordlistFile = "unigram_freq_list_filtered_89k.csv";
    private string _activeLanguagePack = "en-US";

    public CuratedStore Curated => _curated;

    /// <summary>Language pack currently driving curated/wordlist (en-US = bundled defaults).</summary>
    public string ActiveLanguagePack
    {
        get { lock (_gate) return _activeLanguagePack; }
    }
    public ImportedHotstringStore Imported => _imported;
    public DictionaryDistanceMatcher Dictionary => _dictionary;
    public LearnedStore Learned => _learned;
    public RemovedCorrectionsStore Removed => _removed;

    public AutocorrectOptions Options
    {
        get { lock (_gate) return CloneOptions(_options); }
    }

    /// <summary>Cheap Enabled check for the typing hook (no options clone).</summary>
    public bool IsEnabled
    {
        get { lock (_gate) return _options.Enabled; }
    }

    public int CuratedCount
    {
        get { lock (_gate) return _curated.Count; }
    }

    public int ImportedCount
    {
        get { lock (_gate) return _imported.Count; }
    }

    public int DictionaryCount
    {
        get { lock (_gate) return _dictionary.Count; }
    }

    public int LearnedCount
    {
        get { lock (_gate) return _learned.Count; }
    }

    public static bool IsWordEndChar(char c) =>
        char.IsWhiteSpace(c) ||
        c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '"' or '\'' or
            '\n' or '\r' or '\t';

    public static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c is '\'' or '-' or '_';

    public void Configure(AutocorrectSettings settings)
    {
        lock (_gate)
        {
            _options = AutocorrectOptions.FromSettings(settings);
            _wordlistFile = string.IsNullOrWhiteSpace(settings.WordlistFile)
                ? "unigram_freq_list_filtered_89k.csv"
                : settings.WordlistFile.Trim();
            // Keep active pack if already set to a non-English download; otherwise bundled English.
            if (string.IsNullOrWhiteSpace(_activeLanguagePack) ||
                _activeLanguagePack.Equals("en-US", StringComparison.OrdinalIgnoreCase) ||
                _activeLanguagePack.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                _activeLanguagePack = "en-US";
                ReloadCurated_NoLock(settings.CuratedFile);
                ReloadImported_NoLock(settings.UseImportedLibrary, forceReimport: false);
                ReloadDictionary_NoLock(settings.UseDictionaryDistance);
            }

            ReloadLearned_NoLock(settings.UseLearned);
            ReloadRemoved_NoLock();
        }
    }

    /// <summary>
    /// Point curated (+ optional wordlist) at an on-demand language pack.
    /// Pass <c>en-US</c> / null to restore bundled English sources from <paramref name="settings"/>.
    /// </summary>
    public void ApplyLanguagePack(
        string? languageCode,
        AutocorrectSettings settings,
        string? curatedPackPath,
        string? wordlistPackPath)
    {
        lock (_gate)
        {
            _options = AutocorrectOptions.FromSettings(settings);
            var code = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode.Trim();
            var useBundledEnglish =
                code.Equals("en-US", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                code.Equals("auto", StringComparison.OrdinalIgnoreCase);

            if (useBundledEnglish || string.IsNullOrWhiteSpace(curatedPackPath) || !File.Exists(curatedPackPath))
            {
                _activeLanguagePack = "en-US";
                _wordlistFile = string.IsNullOrWhiteSpace(settings.WordlistFile)
                    ? "unigram_freq_list_filtered_89k.csv"
                    : settings.WordlistFile.Trim();
                ReloadCurated_NoLock(settings.CuratedFile);
                ReloadImported_NoLock(settings.UseImportedLibrary, forceReimport: false);
                ReloadDictionary_NoLock(settings.UseDictionaryDistance);
            }
            else
            {
                _activeLanguagePack = code;
                _curated.LoadFromFile(curatedPackPath);
                // AHK import is English-centric — leave off for other packs.
                _imported.Clear();
                if (!string.IsNullOrWhiteSpace(wordlistPackPath) && File.Exists(wordlistPackPath))
                {
                    try { _dictionary.LoadFromFile(wordlistPackPath); }
                    catch { _dictionary.Clear(); }
                }
                else
                {
                    _dictionary.Clear();
                }
            }

            ReloadLearned_NoLock(settings.UseLearned);
            ReloadRemoved_NoLock();
        }
    }

    public void Configure(AutocorrectOptions options, string? curatedFile = null, bool loadImported = false,
        bool loadDictionary = false, string? wordlistFile = null)
    {
        lock (_gate)
        {
            _options = CloneOptions(options);
            if (wordlistFile is not null)
                _wordlistFile = wordlistFile;
            if (curatedFile is not null)
                ReloadCurated_NoLock(curatedFile);
            if (loadImported || _options.UseImportedLibrary)
                ReloadImported_NoLock(enabled: true, forceReimport: false);
            else
                _imported.Clear();
            if (loadDictionary || _options.UseDictionaryDistance)
                ReloadDictionary_NoLock(enabled: true);
            else
                _dictionary.Clear();
        }
    }

    public void ReloadCurated(string? fileName = null)
    {
        lock (_gate)
            ReloadCurated_NoLock(fileName);
    }

    public int ReimportLibrary(bool force = true)
    {
        lock (_gate)
        {
            ReloadImported_NoLock(enabled: true, forceReimport: force);
            return _imported.Count;
        }
    }

    public AutocorrectMatch? TryMatch(string word, string? processName = null)
    {
        lock (_gate)
        {
            var resolved = _options.ResolveForProcess(processName);
            if (!resolved.AutocorrectEnabled)
                return null;

            word = word.Trim();
            if (word.Length == 0)
                return null;

            if (_options.ProtectZones && ProtectZones.IsProtectedToken(word))
                return null;

            // User permanently disabled this trigger in Word Manager / Never.
            if (_removed.Contains(word))
                return null;

            if (resolved.UseCurated && _curated.TryGet(word, out var curatedRepl))
            {
                if (!string.Equals(word, curatedRepl, StringComparison.OrdinalIgnoreCase))
                {
                    return new AutocorrectMatch
                    {
                        Trigger = word,
                        Replacement = curatedRepl,
                        Source = "curated"
                    };
                }
            }

            if (resolved.UseLearned && _learned.TryGet(word, out var learnedRepl))
            {
                if (!string.Equals(word, learnedRepl, StringComparison.OrdinalIgnoreCase))
                {
                    return new AutocorrectMatch
                    {
                        Trigger = word,
                        Replacement = learnedRepl,
                        Source = "learned"
                    };
                }
            }

            if (resolved.UseImportedLibrary && _imported.Loaded)
            {
                var balanced = !resolved.IsStrict;
                // Balanced: AHK "?" suffix (mid-word) with nullifiers + dictionary guard.
                // Strict: exact non-"?" only (no suffix).
                Func<string, bool>? known = _dictionary.Loaded
                    ? w => _dictionary.Contains(w)
                    : null;
                if (_imported.TryMatch(word, balanced, allowSuffix: balanced, known, out var importedRepl) &&
                    !string.Equals(word, importedRepl, StringComparison.OrdinalIgnoreCase))
                {
                    return new AutocorrectMatch
                    {
                        Trigger = word,
                        Replacement = importedRepl,
                        Source = "imported"
                    };
                }
            }

            if (resolved.UseDictionaryDistance && _dictionary.Loaded)
            {
                var dictRepl = _dictionary.TryUniqueOneEdit(word);
                if (dictRepl is not null &&
                    !string.Equals(word, dictRepl, StringComparison.OrdinalIgnoreCase))
                {
                    return new AutocorrectMatch
                    {
                        Trigger = word,
                        Replacement = dictRepl,
                        Source = "dictionary"
                    };
                }
            }

            return null;
        }
    }

    /// <summary>Whether Fix should run in the focused process (profiles can disable).</summary>
    public bool IsFixEnabledForProcess(string? processName)
    {
        lock (_gate)
            return _options.ResolveForProcess(processName).FixEnabled;
    }

    public bool IsProcessExcluded(string? processName)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;
            return _options.ExcludedProcesses.Contains(AppProfileStore.NormalizeProcess(processName));
        }
    }

    private void ReloadCurated_NoLock(string? fileName)
    {
        var path = DataPaths.FindAutocorrectCurated(fileName);
        if (path is null)
        {
            _curated.Clear();
            return;
        }
        _curated.LoadFromFile(path);
    }

    private void ReloadImported_NoLock(bool enabled, bool forceReimport)
    {
        if (!enabled)
        {
            _imported.Clear();
            return;
        }

        try
        {
            var file = AhkHotstringImporter.EnsureImported(forceReimport);
            var path = DataPaths.FindAutocorrectImported()
                       ?? DataPaths.FindOrCreateAutocorrectImportedPath();
            _imported.Load(file, path);
        }
        catch
        {
            _imported.Clear();
        }
    }

    private void ReloadDictionary_NoLock(bool enabled)
    {
        if (!enabled)
        {
            _dictionary.Clear();
            return;
        }

        try
        {
            var path = DataPaths.FindWordlist(_wordlistFile);
            if (path is null)
            {
                _dictionary.Clear();
                return;
            }
            _dictionary.LoadFromFile(path);
        }
        catch
        {
            _dictionary.Clear();
        }
    }

    private void ReloadLearned_NoLock(bool enabled)
    {
        if (!enabled)
        {
            _learned.Clear();
            return;
        }

        try
        {
            var path = DataPaths.FindAutoLearned() ?? DataPaths.FindOrCreateAutoLearnedPath();
            _learned.LoadFromFile(path);
        }
        catch
        {
            _learned.Clear();
        }
    }

    public void ReloadLearned()
    {
        lock (_gate)
            ReloadLearned_NoLock(enabled: true);
    }

    public void ReloadRemoved()
    {
        lock (_gate)
            ReloadRemoved_NoLock();
    }

    private void ReloadRemoved_NoLock()
    {
        try
        {
            var path = DataPaths.FindRemovedCorrections() ?? DataPaths.FindOrCreateRemovedCorrectionsPath();
            _removed.LoadFromFile(path);
        }
        catch
        {
            // keep previous
        }
    }

    /// <summary>Manually add/update a user pair (Word Manager). Clears any removal block.</summary>
    public bool AddCustomPair(string trigger, string replacement, out string error)
    {
        error = "";
        lock (_gate)
        {
            var t = trigger.Trim().ToLowerInvariant();
            var r = replacement.Trim().ToLowerInvariant();
            if (t.Length < 1 || r.Length < 1)
            {
                error = "Both typo and correction are required.";
                return false;
            }
            if (t == r)
            {
                error = "Typo and correction can’t be the same.";
                return false;
            }
            if (t.Length > 64 || r.Length > 64)
            {
                error = "Keep each side under 64 characters.";
                return false;
            }
            if (t.Any(char.IsWhiteSpace) || r.Any(char.IsWhiteSpace))
            {
                error = "Single words only (no spaces).";
                return false;
            }

            _removed.Remove(t);
            _learned.Upsert(t, r);
            return true;
        }
    }

    /// <summary>Block a trigger forever (Word Manager remove). Also drops learned pair if present.</summary>
    public void RemoveCorrection(string trigger, string? replacement, string source)
    {
        lock (_gate)
        {
            var t = trigger.Trim();
            if (t.Length == 0)
                return;
            if (string.IsNullOrWhiteSpace(replacement))
            {
                if (_learned.TryGet(t, out var lr))
                    replacement = lr;
                else if (_curated.TryGet(t, out var cr))
                    replacement = cr;
            }

            _removed.Add(t, replacement, source);
            _learned.Remove(t);
        }
    }

    /// <summary>Undo a Word Manager removal — trigger may autocorrect again.</summary>
    public RemovedCorrection? RestoreCorrection(string trigger)
    {
        lock (_gate)
        {
            var item = _removed.Remove(trigger);
            if (item is not null &&
                string.Equals(item.Source, "learned", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Replacement))
            {
                _learned.Upsert(item.Trigger, item.Replacement);
            }
            // Curated/imported come back automatically once unblocked.
            return item;
        }
    }

    /// <summary>Factory reset of user learning/overrides (not the shipped curated file).</summary>
    public void ResetUserWordData(bool clearLearned, bool clearRemoved)
    {
        lock (_gate)
        {
            if (clearLearned)
            {
                foreach (var (trig, _) in _learned.Snapshot().ToList())
                    _learned.Remove(trig);
            }

            if (clearRemoved)
                _removed.Clear();
        }
    }

    private static AutocorrectOptions CloneOptions(AutocorrectOptions o) => new()
    {
        Enabled = o.Enabled,
        Strictness = o.Strictness,
        UseCurated = o.UseCurated,
        UseImportedLibrary = o.UseImportedLibrary,
        UseDictionaryDistance = o.UseDictionaryDistance,
        UseLearned = o.UseLearned,
        ProtectZones = o.ProtectZones,
        ExcludedProcesses = new HashSet<string>(o.ExcludedProcesses, StringComparer.OrdinalIgnoreCase),
        AppProfiles = o.AppProfiles.Select(AutocorrectOptions.CloneProfile).ToList()
    };
}
