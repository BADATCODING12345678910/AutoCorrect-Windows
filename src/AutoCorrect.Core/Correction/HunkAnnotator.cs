namespace AutoCorrect.Core.Correction;

/// <summary>Attach Grammarly-style category labels to word-diff hunks using LT matches + heuristics.</summary>
public static class HunkAnnotator
{
    public static void Annotate(
        IList<CorrectionHunk> hunks,
        IReadOnlyList<LanguageToolMatch>? ltMatches,
        PersonalDictionary? dictionary = null)
    {
        foreach (var hunk in hunks)
        {
            LanguageToolMatch? best = null;
            var bestOverlap = 0;
            if (ltMatches is { Count: > 0 })
            {
                var hunkEnd = hunk.StartPos + hunk.OldText.Length;
                foreach (var m in ltMatches)
                {
                    var mEnd = m.Offset + m.Length;
                    var overlap = Math.Min(hunkEnd, mEnd) - Math.Max(hunk.StartPos, m.Offset);
                    if (overlap <= 0)
                        continue;
                    if (overlap > bestOverlap)
                    {
                        bestOverlap = overlap;
                        best = m;
                    }
                }
            }

            if (best is not null)
            {
                hunk.Category = best.Category;
                hunk.CategoryLabel = RefineLabel(best, hunk);
                hunk.ShortMessage = best.ShortMessage ?? best.Message;
                hunk.RuleId = best.RuleId;
            }
            else
            {
                hunk.Category = SuggestionCategoryLabels.InferFromHunk(hunk.OldText, hunk.NewText);
                hunk.CategoryLabel = SuggestionCategoryLabels.For(
                    hunk.Category,
                    InferShortMessage(hunk));
            }

            // Case-only edits are capitalization even if LT tags them as typos.
            if (IsCapitalizationOnly(hunk.OldText, hunk.NewText))
            {
                hunk.Category = SuggestionCategory.Capitalization;
                hunk.CategoryLabel = "Fix capitalization";
            }

            hunk.Confidence = SuggestionConfidenceLabels.Infer(hunk);
        }

        if (dictionary is null)
            return;

        // Drop spelling suggestions for dictionary words (post-diff safety net).
        for (var i = hunks.Count - 1; i >= 0; i--)
        {
            var h = hunks[i];
            if (!h.IsSpelling)
                continue;
            var token = h.DictionaryToken;
            if (token is not null && dictionary.Contains(token))
                hunks.RemoveAt(i);
        }
    }

    private static string RefineLabel(LanguageToolMatch match, CorrectionHunk hunk)
    {
        var shortMsg = (match.ShortMessage ?? "").Trim().ToLowerInvariant();
        if (shortMsg.Contains("preposition", StringComparison.Ordinal))
            return "Change preposition";
        if (shortMsg.Contains("spelling", StringComparison.Ordinal) || match.Category == SuggestionCategory.Spelling)
            return "Correct your spelling";
        if (shortMsg.Contains("punctuation", StringComparison.Ordinal))
            return "Fix punctuation";
        if (IsPrepositionSwap(hunk.OldText, hunk.NewText))
            return "Change preposition";

        return match.CategoryLabel;
    }

    private static string? InferShortMessage(CorrectionHunk hunk)
    {
        if (IsPrepositionSwap(hunk.OldText, hunk.NewText))
            return "Change preposition";
        return null;
    }

    private static readonly HashSet<string> Prepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "in", "on", "at", "to", "for", "of", "with", "by", "from", "about",
        "into", "onto", "over", "under", "through", "between", "among", "during",
        "without", "within", "along", "across", "behind", "beyond", "upon", "toward", "towards"
    };

    private static bool IsPrepositionSwap(string oldText, string newText)
    {
        var o = WordDiff.Tokenize(oldText.Trim()).Where(t => t.Length > 0 && char.IsLetter(t[0])).ToList();
        var n = WordDiff.Tokenize(newText.Trim()).Where(t => t.Length > 0 && char.IsLetter(t[0])).ToList();
        if (o.Count != 1 || n.Count != 1)
            return false;
        return Prepositions.Contains(o[0]) && Prepositions.Contains(n[0]) &&
               !string.Equals(o[0], n[0], StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCapitalizationOnly(string a, string b) =>
        a.Length > 0 && b.Length > 0 &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(a, b, StringComparison.Ordinal);
}
