namespace AutoCorrect.Core.Correction;

public enum SuggestionCategory
{
    Spelling,
    Grammar,
    Punctuation,
    Clarity,
    Capitalization,
    LocalRule,
    Other
}

public static class SuggestionCategoryLabels
{
    public static string For(SuggestionCategory category, string? shortMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(shortMessage))
        {
            var s = shortMessage.Trim();
            // Prefer Grammarly-style short imperatives when LT gives a terse label.
            if (s.Length <= 48 && !s.Contains('\n', StringComparison.Ordinal))
                return EnsureSentenceCase(s);
        }

        return category switch
        {
            SuggestionCategory.Spelling => "Correct your spelling",
            SuggestionCategory.Grammar => "Correct your grammar",
            SuggestionCategory.Punctuation => "Fix punctuation",
            SuggestionCategory.Clarity => "Improve clarity",
            SuggestionCategory.Capitalization => "Fix capitalization",
            SuggestionCategory.LocalRule => "Improve wording",
            _ => "Correct your writing"
        };
    }

    /// <summary>Compact uppercase header used on the review card (matches marketing demo).</summary>
    public static string Badge(SuggestionCategory category) => category switch
    {
        SuggestionCategory.Spelling => "SPELLING",
        SuggestionCategory.Grammar => "GRAMMAR",
        SuggestionCategory.Punctuation => "PUNCTUATION",
        SuggestionCategory.Clarity => "CLARITY",
        SuggestionCategory.Capitalization => "CAPITALIZATION",
        SuggestionCategory.LocalRule => "WORDING",
        _ => "SUGGESTION"
    };

    public static SuggestionCategory FromLanguageTool(
        string? issueType,
        string? categoryId,
        string? ruleId,
        string? shortMessage)
    {
        var issue = (issueType ?? "").Trim().ToLowerInvariant();
        var cat = (categoryId ?? "").Trim().ToUpperInvariant();
        var rule = (ruleId ?? "").Trim().ToUpperInvariant();
        var shortMsg = (shortMessage ?? "").Trim().ToLowerInvariant();

        if (issue is "misspelling" or "typographical" ||
            cat is "TYPOS" or "CASING" ||
            rule.Contains("MORFOLOGIK", StringComparison.Ordinal) ||
            rule.Contains("HUNSPELL", StringComparison.Ordinal) ||
            shortMsg.Contains("spelling", StringComparison.Ordinal))
        {
            if (cat is "CASING" || shortMsg.Contains("capital", StringComparison.Ordinal))
                return SuggestionCategory.Capitalization;
            return SuggestionCategory.Spelling;
        }

        if (cat is "PUNCTUATION" || issue is "punctuation" ||
            shortMsg.Contains("punctuat", StringComparison.Ordinal) ||
            shortMsg.Contains("comma", StringComparison.Ordinal) ||
            shortMsg.Contains("apostrophe", StringComparison.Ordinal))
            return SuggestionCategory.Punctuation;

        if (cat is "STYLE" or "REDUNDANCY" or "PLAIN_ENGLISH" ||
            issue is "style" or "duplication" ||
            shortMsg.Contains("clarity", StringComparison.Ordinal) ||
            shortMsg.Contains("concise", StringComparison.Ordinal))
            return SuggestionCategory.Clarity;

        if (shortMsg.Contains("preposition", StringComparison.Ordinal))
            return SuggestionCategory.Grammar;

        if (cat is "GRAMMAR" or "CONFUSED_WORDS" || issue is "grammar")
            return SuggestionCategory.Grammar;

        return SuggestionCategory.Other;
    }

    public static SuggestionCategory InferFromHunk(string oldText, string newText)
    {
        var oldTrim = oldText.Trim();
        var newTrim = newText.Trim();

        if (LooksLikePunctuationOnly(oldTrim, newTrim))
            return SuggestionCategory.Punctuation;

        if (IsCapitalizationOnly(oldTrim, newTrim))
            return SuggestionCategory.Capitalization;

        var oldWords = WordDiff.Tokenize(oldTrim).Where(IsWord).ToList();
        var newWords = WordDiff.Tokenize(newTrim).Where(IsWord).ToList();

        if (oldWords.Count == 1 && newWords.Count == 1 &&
            !string.Equals(oldWords[0], newWords[0], StringComparison.OrdinalIgnoreCase))
            return SuggestionCategory.Spelling;

        if (oldWords.Count <= 2 && newWords.Count <= 2)
            return SuggestionCategory.Grammar;

        return SuggestionCategory.Other;
    }

    public static string? DictionaryToken(string oldText)
    {
        foreach (var t in WordDiff.Tokenize(oldText.Trim()))
        {
            if (IsWord(t))
                return t;
        }
        return string.IsNullOrWhiteSpace(oldText) ? null : oldText.Trim();
    }

    private static bool IsWord(string t) =>
        t.Length > 0 && (char.IsLetterOrDigit(t[0]) || t[0] == '\'');

    private static bool LooksLikePunctuationOnly(string oldTrim, string newTrim)
    {
        static string Letters(string s) =>
            new(s.Where(c => char.IsLetterOrDigit(c) || c == '\'').ToArray());

        return Letters(oldTrim).Equals(Letters(newTrim), StringComparison.Ordinal) &&
               oldTrim != newTrim;
    }

    private static bool IsCapitalizationOnly(string a, string b) =>
        a.Length > 0 && b.Length > 0 &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(a, b, StringComparison.Ordinal);

    private static string EnsureSentenceCase(string s)
    {
        if (s.Length == 0)
            return s;
        if (char.IsUpper(s[0]))
            return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }
}
