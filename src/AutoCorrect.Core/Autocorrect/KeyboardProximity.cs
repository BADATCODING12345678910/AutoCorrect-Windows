namespace AutoCorrect.Core.Autocorrect;

/// <summary>QWERTY adjacency for fat-finger typo ranking.</summary>
public static class KeyboardProximity
{
    private static readonly Dictionary<char, string> Neighbors = Build();

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> are the same key or adjacent on QWERTY.</summary>
    public static bool AreAdjacent(char a, char b)
    {
        a = char.ToLowerInvariant(a);
        b = char.ToLowerInvariant(b);
        if (a == b)
            return true;
        if (!char.IsAsciiLetter(a) || !char.IsAsciiLetter(b))
            return false;
        return Neighbors.TryGetValue(a, out var n) && n.Contains(b);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is reachable from <paramref name="typed"/>
    /// by substituting exactly one letter for a keyboard-adjacent letter (same length).
    /// </summary>
    public static bool IsSingleAdjacentSubstitution(string typed, string candidate)
    {
        if (typed.Length != candidate.Length || typed.Length == 0)
            return false;

        var diffs = 0;
        var adjacentDiff = false;
        for (var i = 0; i < typed.Length; i++)
        {
            var t = char.ToLowerInvariant(typed[i]);
            var c = char.ToLowerInvariant(candidate[i]);
            if (t == c)
                continue;
            diffs++;
            if (diffs > 1)
                return false;
            adjacentDiff = AreAdjacent(t, c);
        }

        return diffs == 1 && adjacentDiff;
    }

    private static Dictionary<char, string> Build()
    {
        // Physical QWERTY neighbors (letters only).
        var rows = new[]
        {
            "qwertyuiop",
            "asdfghjkl",
            "zxcvbnm"
        };

        var map = new Dictionary<char, HashSet<char>>();
        void Touch(char a, char b)
        {
            if (!map.TryGetValue(a, out var set))
                map[a] = set = new HashSet<char>();
            set.Add(b);
        }

        for (var r = 0; r < rows.Length; r++)
        {
            var row = rows[r];
            for (var i = 0; i < row.Length; i++)
            {
                var ch = row[i];
                if (i > 0)
                    Touch(ch, row[i - 1]);
                if (i + 1 < row.Length)
                    Touch(ch, row[i + 1]);
                if (r > 0)
                {
                    var above = rows[r - 1];
                    var j = Math.Min(i, above.Length - 1);
                    Touch(ch, above[j]);
                    if (j + 1 < above.Length)
                        Touch(ch, above[j + 1]);
                    if (j > 0)
                        Touch(ch, above[j - 1]);
                }
                if (r + 1 < rows.Length)
                {
                    var below = rows[r + 1];
                    var j = Math.Min(i, below.Length - 1);
                    Touch(ch, below[j]);
                    if (j + 1 < below.Length)
                        Touch(ch, below[j + 1]);
                    if (j > 0)
                        Touch(ch, below[j - 1]);
                }
            }
        }

        // Explicit vertical pairs that matter for common typos (e/d, etc.).
        foreach (var (a, b) in new[] { ('e', 'd'), ('d', 'e'), ('w', 's'), ('s', 'w'), ('r', 'f'), ('f', 'r'),
                     ('t', 'g'), ('g', 't'), ('y', 'h'), ('h', 'y'), ('u', 'j'), ('j', 'u'),
                     ('i', 'k'), ('k', 'i'), ('o', 'l'), ('l', 'o') })
            Touch(a, b);

        return map.ToDictionary(kv => kv.Key, kv => string.Concat(kv.Value.OrderBy(c => c)));
    }
}
