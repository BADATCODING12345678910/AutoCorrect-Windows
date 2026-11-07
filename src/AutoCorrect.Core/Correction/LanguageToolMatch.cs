namespace AutoCorrect.Core.Correction;

public sealed record LanguageToolMatch(
    int Offset,
    int Length,
    string Replacement,
    string MatchedText,
    string? Message,
    string? ShortMessage,
    string? IssueType,
    string? CategoryId,
    string? CategoryName,
    string? RuleId)
{
    public SuggestionCategory Category =>
        SuggestionCategoryLabels.FromLanguageTool(IssueType, CategoryId, RuleId, ShortMessage);

    public string CategoryLabel =>
        SuggestionCategoryLabels.For(Category, PreferLabel());

    private string? PreferLabel()
    {
        if (!string.IsNullOrWhiteSpace(ShortMessage))
            return ShortMessage;
        if (!string.IsNullOrWhiteSpace(CategoryName) &&
            CategoryName!.Length <= 40)
            return CategoryName;
        return null;
    }
}

public sealed record LanguageToolResult(
    string CorrectedText,
    IReadOnlyList<LanguageToolMatch> Matches,
    string? DetectedLanguageCode = null);
