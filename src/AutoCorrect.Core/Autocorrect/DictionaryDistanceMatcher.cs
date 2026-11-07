namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Correct when the typed token is 1 edit from a dictionary word.
/// Prefer a unique neighbor; if several, keep only keyboard-adjacent single-letter
/// substitutions (fat-finger) — still require uniqueness after that filter.
/// </summary>
public sealed class DictionaryDistanceMatcher
{
    private readonly HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _freq = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;

    public string? Path => _path;
    public int Count => _words.Count;
    public bool Loaded { get; private set; }

    /// <summary>Min length 3 so thd→the style fat-fingers can fire.</summary>
    public int MinLength { get; set; } = 3;

    public int MaxLength { get; set; } = 24;

    /// <summary>Inserts explode combinatorially; skip them above this length.</summary>
    public int MaxInsertLength { get; set; } = 10;

    public void Clear()
    {
        _words.Clear();
        _freq.Clear();
        Loaded = false;
        _path = null;
    }

    public bool Contains(string word) =>
        Loaded && !string.IsNullOrWhiteSpace(word) && _words.Contains(word.Trim());

    public void LoadFromFile(string path)
    {
        Clear();
        _path = path;
        Loaded = true;
        if (!File.Exists(path))
            return;

        var ext = System.IO.Path.GetExtension(path);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            string word;
            long freq = 1;
            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var comma = line.IndexOf(',');
                word = (comma > 0 ? line[..comma] : line).Trim().Trim('"');
                if (comma > 0)
                {
                    var rest = line[(comma + 1)..].Trim().Trim('"');
                    _ = long.TryParse(rest, out freq);
                    if (freq < 1) freq = 1;
                }
            }
            else
            {
                var parts = line.Split('\t', 3);
                word = parts[0].Trim();
                if (parts.Length > 1)
                {
                    _ = long.TryParse(parts[1].Trim(), out freq);
                    if (freq < 1) freq = 1;
                }
            }

            if (word.Length < 2 || word.Length > MaxLength)
                continue;
            if (!word.All(char.IsLetter))
                continue;

            var key = word.ToLowerInvariant();
            _words.Add(key);
            if (!_freq.TryGetValue(key, out var prev) || freq > prev)
                _freq[key] = freq;
        }
    }

    /// <summary>
    /// Returns the chosen 1-edit dictionary neighbor, or null if none / still ambiguous.
    /// </summary>
    public string? TryUniqueOneEdit(string typed)
    {
        if (!Loaded || _words.Count == 0)
            return null;

        typed = typed.Trim();
        if (typed.Length < MinLength || typed.Length > MaxLength)
            return null;
        if (!typed.All(char.IsLetter))
            return null;

        var lower = typed.ToLowerInvariant();
        if (_words.Contains(lower))
            return null;

        var neighbors = new HashSet<string>(StringComparer.Ordinal);
        string? first = null;
        foreach (var candidate in EnumerateOneEdits(lower, allowInserts: lower.Length <= MaxInsertLength))
        {
            if (!_words.Contains(candidate))
                continue;
            if (!neighbors.Add(candidate))
                continue;
            if (neighbors.Count == 1)
                first = candidate;
            // Keep scanning — ambiguity needs the full neighbor set.
        }

        if (neighbors.Count == 0)
            return null;
        if (neighbors.Count == 1)
            return first;

        // Ambiguous — prefer unique keyboard-adjacent substitution (e.g. thd→the via d/e).
        var fatFinger = neighbors
            .Where(n => KeyboardProximity.IsSingleAdjacentSubstitution(lower, n))
            .ToList();

        if (fatFinger.Count == 1)
            return fatFinger[0];

        // Still ambiguous — pick highest unigram frequency when clearly dominant.
        var ranked = neighbors
            .Select(n => (Word: n, Freq: _freq.TryGetValue(n, out var f) ? f : 0L))
            .OrderByDescending(x => x.Freq)
            .ToList();
        if (ranked.Count >= 2 && ranked[0].Freq > 0 && ranked[0].Freq >= ranked[1].Freq * 3)
            return ranked[0].Word;

        if (fatFinger.Count > 1)
        {
            var ffRanked = fatFinger
                .Select(n => (Word: n, Freq: _freq.TryGetValue(n, out var f) ? f : 0L))
                .OrderByDescending(x => x.Freq)
                .ToList();
            if (ffRanked.Count >= 2 && ffRanked[0].Freq > 0 && ffRanked[0].Freq >= ffRanked[1].Freq * 2)
                return ffRanked[0].Word;
            if (ffRanked.Count == 1)
                return ffRanked[0].Word;
        }

        return null;
    }

    public static IEnumerable<string> EnumerateOneEdits(string word) =>
        EnumerateOneEdits(word, allowInserts: true);

    public static IEnumerable<string> EnumerateOneEdits(string word, bool allowInserts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { word };
        var chars = word.ToCharArray();
        var n = chars.Length;

        // Cheapest first: deletes, then adjacent swaps, then substitutions, then inserts.
        for (var i = 0; i < n; i++)
        {
            var s = word.Remove(i, 1);
            if (seen.Add(s))
                yield return s;
        }

        for (var i = 0; i < n - 1; i++)
        {
            Swap(chars, i, i + 1);
            var s = new string(chars);
            Swap(chars, i, i + 1);
            if (seen.Add(s))
                yield return s;
        }

        for (var i = 0; i < n; i++)
        {
            var orig = chars[i];
            for (var c = 'a'; c <= 'z'; c++)
            {
                if (c == orig)
                    continue;
                chars[i] = c;
                var s = new string(chars);
                if (seen.Add(s))
                    yield return s;
            }
            chars[i] = orig;
        }

        if (!allowInserts)
            yield break;

        for (var i = 0; i <= n; i++)
        {
            for (var c = 'a'; c <= 'z'; c++)
            {
                var s = word.Insert(i, c.ToString());
                if (seen.Add(s))
                    yield return s;
            }
        }
    }

    private static void Swap(char[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);
}
