namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Runtime lookup over imported AHK hotstrings.
/// Exact whole-word always; Balanced also applies AHK <c>?</c> suffix (mid-word) rules
/// with nullifier + optional dictionary guards.
/// </summary>
public sealed class ImportedHotstringStore
{
    private readonly Dictionary<string, ImportedHotstringEntry> _insensitive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImportedHotstringEntry> _sensitive = new(StringComparer.Ordinal);
    private readonly List<ImportedHotstringEntry> _suffixInsensitive = [];
    private readonly List<ImportedHotstringEntry> _suffixSensitive = [];
    private readonly Dictionary<char, List<ImportedHotstringEntry>> _suffixInsensitiveByLast = new();
    private readonly Dictionary<char, List<ImportedHotstringEntry>> _suffixSensitiveByLast = new();
    private readonly HashSet<string> _nullifiersInsensitive = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nullifiersSensitive = new(StringComparer.Ordinal);
    private string? _path;

    public string? Path => _path;
    public int Count => _insensitive.Count + _sensitive.Count;
    public int NullifierCount => _nullifiersInsensitive.Count + _nullifiersSensitive.Count;
    public int SuffixRuleCount => _suffixInsensitive.Count + _suffixSensitive.Count;
    public bool Loaded { get; private set; }

    public void Clear()
    {
        _insensitive.Clear();
        _sensitive.Clear();
        _suffixInsensitive.Clear();
        _suffixSensitive.Clear();
        _suffixInsensitiveByLast.Clear();
        _suffixSensitiveByLast.Clear();
        _nullifiersInsensitive.Clear();
        _nullifiersSensitive.Clear();
        Loaded = false;
        _path = null;
    }

    public void Load(ImportedLibraryFile file, string? path = null)
    {
        Clear();
        _path = path;
        Loaded = true;

        foreach (var n in file.Nullifiers)
        {
            if (string.IsNullOrWhiteSpace(n.Trigger) || n.Trigger.Contains(' '))
                continue;
            if (n.CaseSensitive)
                _nullifiersSensitive.Add(n.Trigger);
            else
                _nullifiersInsensitive.Add(n.Trigger);
        }

        foreach (var e in file.Entries)
        {
            if (string.IsNullOrWhiteSpace(e.Trigger) || string.IsNullOrWhiteSpace(e.Replacement))
                continue;
            if (e.Trigger.Contains(' '))
                continue;

            if (e.CaseSensitive)
                _sensitive.TryAdd(e.Trigger, e);
            else
                _insensitive.TryAdd(e.Trigger, e);

            // AHK "?": may fire when preceded by other word characters (suffix of token).
            if (e.Question && e.Trigger.Length >= 2)
            {
                if (e.CaseSensitive)
                    _suffixSensitive.Add(e);
                else
                    _suffixInsensitive.Add(e);
            }
        }

        // Longest suffix first so "caly" wins over a hypothetical shorter overlap.
        _suffixInsensitive.Sort(static (a, b) => b.Trigger.Length.CompareTo(a.Trigger.Length));
        _suffixSensitive.Sort(static (a, b) => b.Trigger.Length.CompareTo(a.Trigger.Length));
        IndexSuffixByLast(_suffixInsensitive, _suffixInsensitiveByLast, caseSensitive: false);
        IndexSuffixByLast(_suffixSensitive, _suffixSensitiveByLast, caseSensitive: true);
    }

    private static void IndexSuffixByLast(
        List<ImportedHotstringEntry> rules,
        Dictionary<char, List<ImportedHotstringEntry>> index,
        bool caseSensitive)
    {
        foreach (var e in rules)
        {
            if (e.Trigger.Length == 0)
                continue;
            var last = e.Trigger[^1];
            if (!caseSensitive)
                last = char.ToLowerInvariant(last);
            if (!index.TryGetValue(last, out var list))
            {
                list = [];
                index[last] = list;
            }
            list.Add(e);
        }
    }

    /// <summary>
    /// Exact whole-word match. When <paramref name="balanced"/> is false (Strict),
    /// skip entries whose AHK options include <c>?</c>.
    /// </summary>
    public bool TryMatch(string word, bool balanced, out string replacement) =>
        TryMatch(word, balanced, allowSuffix: balanced, isKnownWord: null, out replacement);

    /// <summary>
    /// Exact match, then (when allowed) longest <c>?</c> suffix replacement.
    /// <paramref name="isKnownWord"/> — when true for the typed token, skip suffix fixes
    /// (real dictionary words like "design" / "scaly" must not be mangled).
    /// </summary>
    public bool TryMatch(
        string word,
        bool balanced,
        bool allowSuffix,
        Func<string, bool>? isKnownWord,
        out string replacement)
    {
        replacement = "";
        if (string.IsNullOrEmpty(word))
            return false;

        if (IsNullified(word))
            return false;

        if (_sensitive.TryGetValue(word, out var sens) &&
            AllowExact(sens, balanced) &&
            !string.Equals(word, sens.Replacement, StringComparison.Ordinal))
        {
            replacement = sens.Replacement;
            return true;
        }

        if (_insensitive.TryGetValue(word, out var ins) &&
            AllowExact(ins, balanced) &&
            !string.Equals(word, ins.Replacement, StringComparison.OrdinalIgnoreCase))
        {
            replacement = ins.Replacement;
            return true;
        }

        if (!allowSuffix || !balanced)
            return false;

        // Real words stay put — nullifiers cover AHK’s list; dictionary covers the rest.
        if (isKnownWord?.Invoke(word) == true)
            return false;

        if (TrySuffixIndexed(_suffixSensitiveByLast, word, caseSensitive: true, out replacement))
            return true;
        if (TrySuffixIndexed(_suffixInsensitiveByLast, word, caseSensitive: false, out replacement))
            return true;

        return false;
    }

    public bool IsNullified(string word)
    {
        if (string.IsNullOrEmpty(word))
            return false;
        return _nullifiersSensitive.Contains(word) || _nullifiersInsensitive.Contains(word);
    }

    private static bool TrySuffixIndexed(
        Dictionary<char, List<ImportedHotstringEntry>> index,
        string word,
        bool caseSensitive,
        out string replacement)
    {
        replacement = "";
        if (word.Length == 0)
            return false;
        var last = caseSensitive ? word[^1] : char.ToLowerInvariant(word[^1]);
        if (!index.TryGetValue(last, out var rules))
            return false;

        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var e in rules)
        {
            var trig = e.Trigger;
            if (word.Length <= trig.Length)
                continue;
            if (!word.EndsWith(trig, cmp))
                continue;
            if (string.Equals(trig, e.Replacement, cmp))
                continue;

            var prefixLen = word.Length - trig.Length;
            var prefix = word[..prefixLen];
            var fixedTail = ApplySuffixCase(word[prefixLen..], trig, e.Replacement);
            var full = prefix + fixedTail;
            if (string.Equals(word, full, StringComparison.Ordinal))
                continue;

            replacement = full;
            return true;
        }

        return false;
    }

    /// <summary>Match replacement case to the typed suffix span.</summary>
    private static string ApplySuffixCase(string typedSuffix, string trigger, string replacement)
    {
        _ = trigger;
        return AutocorrectMatch.PreserveCase(typedSuffix, replacement);
    }

    private static bool AllowExact(ImportedHotstringEntry e, bool balanced) =>
        balanced || !e.Question;

    public IReadOnlyList<(string Trigger, string Replacement)> SnapshotExact()
    {
        var list = new List<(string, string)>(_insensitive.Count + _sensitive.Count);
        foreach (var kv in _insensitive)
            list.Add((kv.Key, kv.Value.Replacement));
        foreach (var kv in _sensitive)
            list.Add((kv.Key, kv.Value.Replacement));
        return list;
    }
}
