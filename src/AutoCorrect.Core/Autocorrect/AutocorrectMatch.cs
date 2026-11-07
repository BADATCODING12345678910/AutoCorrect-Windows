namespace AutoCorrect.Core.Autocorrect;

/// <summary>A high-confidence typing correction from an enabled source.</summary>
public sealed class AutocorrectMatch
{
    public required string Trigger { get; init; }
    public required string Replacement { get; init; }

    /// <summary>curated | imported | dictionary</summary>
    public required string Source { get; init; }

    /// <summary>Replacement with first-letter case matching the typed trigger.</summary>
    public string CasedReplacement => PreserveCase(Trigger, Replacement);

    public static string PreserveCase(string typed, string replacement)
    {
        if (string.IsNullOrEmpty(typed) || string.IsNullOrEmpty(replacement))
            return replacement;

        if (typed.All(char.IsUpper) && typed.Length > 1)
            return replacement.ToUpperInvariant();

        if (char.IsUpper(typed[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }
}
