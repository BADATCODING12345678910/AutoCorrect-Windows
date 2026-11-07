namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Promote high local LearnContribution tallies into the user's curated list file.
/// </summary>
public static class CuratedPromotion
{
    public static int PromoteFromContributions(
        LearnContributionStore contributions,
        string curatedPath,
        int minCount = 3)
    {
        if (!File.Exists(curatedPath))
            File.WriteAllText(curatedPath, "# AutoCorrect curated pairs (tab-separated)\n");

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(curatedPath))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';'))
                continue;
            var tab = t.IndexOf('\t');
            if (tab > 0)
                existing.Add(t[..tab].Trim());
        }

        var added = 0;
        var lines = new List<string>();
        foreach (var (trigger, replacement, count) in contributions.Snapshot())
        {
            if (count < minCount)
                continue;
            if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(replacement))
                continue;
            if (existing.Contains(trigger))
                continue;
            existing.Add(trigger);
            lines.Add(trigger.Trim().ToLowerInvariant() + "\t" + replacement.Trim().ToLowerInvariant());
            added++;
        }

        if (lines.Count > 0)
            File.AppendAllLines(curatedPath, lines);

        return added;
    }
}
