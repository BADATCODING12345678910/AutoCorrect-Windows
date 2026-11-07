namespace AutoCorrect.Core.Correction;

/// <summary>
/// When the user Accepts a Fix, promote short phrase diffs into DictationLocalRules.txt.
/// </summary>
public static class LocalRulesLearner
{
    private const int MaxPhraseWords = 6;
    private const int MaxPhraseChars = 80;

    public static IReadOnlyList<(string Find, string Replace)> ExtractPairs(string before, string after)
    {
        if (string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after) ||
            string.Equals(before, after, StringComparison.Ordinal))
            return [];

        var pairs = new List<(string, string)>();
        var oldWords = SplitWords(before);
        var newWords = SplitWords(after);
        if (oldWords.Count == 0 || newWords.Count == 0)
            return pairs;

        // Shared prefix/suffix strip → one middle phrase pair when sizes are small.
        var i = 0;
        while (i < oldWords.Count && i < newWords.Count &&
               string.Equals(oldWords[i], newWords[i], StringComparison.Ordinal))
            i++;

        var jOld = oldWords.Count - 1;
        var jNew = newWords.Count - 1;
        while (jOld >= i && jNew >= i &&
               string.Equals(oldWords[jOld], newWords[jNew], StringComparison.Ordinal))
        {
            jOld--;
            jNew--;
        }

        if (jOld < i && jNew < i)
            return pairs;

        var findWords = oldWords.Skip(i).Take(jOld - i + 1).ToList();
        var replWords = newWords.Skip(i).Take(jNew - i + 1).ToList();
        if (findWords.Count == 0 || findWords.Count > MaxPhraseWords || replWords.Count > MaxPhraseWords)
            return pairs;

        var find = string.Join(' ', findWords);
        var repl = string.Join(' ', replWords);
        if (find.Length is 0 or > MaxPhraseChars || repl.Length > MaxPhraseChars)
            return pairs;
        if (string.Equals(find, repl, StringComparison.OrdinalIgnoreCase))
            return pairs;

        pairs.Add((find, repl));
        return pairs;
    }

    public static int AppendPairs(string rulesPath, IEnumerable<(string Find, string Replace)> pairs)
    {
        var added = 0;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(rulesPath))
        {
            foreach (var line in File.ReadLines(rulesPath))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';'))
                    continue;
                var tab = t.IndexOf('\t');
                if (tab > 0)
                    existing.Add(t[..tab].Trim());
            }
        }

        var lines = new List<string>();
        foreach (var (find, replace) in pairs)
        {
            var f = find.Trim();
            var r = replace.Trim();
            if (f.Length == 0 || existing.Contains(f))
                continue;
            existing.Add(f);
            lines.Add(f + "\t" + r);
            added++;
        }

        if (lines.Count == 0)
            return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(rulesPath)!);
        File.AppendAllLines(rulesPath, lines);
        return added;
    }

    public static bool RemoveLastPair(string rulesPath, string find, string replace)
    {
        if (!File.Exists(rulesPath))
            return false;

        var lines = File.ReadAllLines(rulesPath).ToList();
        var target = find.Trim() + "\t" + replace.Trim();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (string.Equals(lines[i].Trim(), target, StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                File.WriteAllLines(rulesPath, lines);
                return true;
            }
        }

        return false;
    }

    private static List<string> SplitWords(string text) =>
        text.Split((char[]?)null!, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
