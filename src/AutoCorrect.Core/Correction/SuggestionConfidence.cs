namespace AutoCorrect.Core.Correction;

/// <summary>Heuristic confidence for Fix-card display (not a model score).</summary>
public enum SuggestionConfidence
{
    High,
    Medium,
    Low
}

public static class SuggestionConfidenceLabels
{
    public static string Chip(SuggestionConfidence c) => c switch
    {
        SuggestionConfidence.High => "Likely typo",
        SuggestionConfidence.Medium => "Grammar",
        SuggestionConfidence.Low => "Style",
        _ => "Suggestion"
    };

    public static SuggestionConfidence Infer(CorrectionHunk hunk)
    {
        if (string.Equals(hunk.RuleId, "writing_suggestion", StringComparison.OrdinalIgnoreCase) ||
            hunk.Category == SuggestionCategory.Clarity)
            return SuggestionConfidence.Low;

        if (hunk.Category is SuggestionCategory.Spelling or SuggestionCategory.Capitalization)
            return SuggestionConfidence.High;

        if (!string.IsNullOrWhiteSpace(hunk.RuleId) &&
            hunk.Category is SuggestionCategory.Grammar or SuggestionCategory.Punctuation or SuggestionCategory.LocalRule)
            return SuggestionConfidence.Medium;

        if (hunk.Category is SuggestionCategory.Grammar or SuggestionCategory.Punctuation)
            return SuggestionConfidence.Medium;

        // Heuristic-only / other without LT rule id
        if (string.IsNullOrWhiteSpace(hunk.RuleId))
            return SuggestionConfidence.Low;

        return SuggestionConfidence.Medium;
    }
}
