namespace AutoCorrect.Core.Correction;

/// <summary>
/// Marks hunks that appear only in the writing-suggestion pass (not in correctness)
/// with Clarity labels like “Improve clarity” / “Make concise”.
/// </summary>
public static class WritingSuggestionAnnotator
{
    public static void MarkStyleOnlyHunks(
        IList<CorrectionHunk> finalHunks,
        IReadOnlyList<CorrectionHunk> correctnessHunks)
    {
        foreach (var h in finalHunks)
        {
            if (OverlapsCorrectness(h, correctnessHunks))
                continue;

            h.Category = SuggestionCategory.Clarity;
            h.CategoryLabel = PreferConciseLabel(h) ? "Make concise" : "Improve clarity";
            h.ShortMessage = h.CategoryLabel;
            h.RuleId = "writing_suggestion";
            h.Confidence = SuggestionConfidence.Low;
        }
    }

    private static bool PreferConciseLabel(CorrectionHunk h)
    {
        if (h.OldText.Length == 0)
            return false;
        return h.NewText.Length < h.OldText.Length * 0.85;
    }

    private static bool OverlapsCorrectness(CorrectionHunk h, IReadOnlyList<CorrectionHunk> correctness)
    {
        if (correctness.Count == 0)
            return false;

        var hEnd = h.StartPos + Math.Max(h.OldText.Length, 1);
        foreach (var c in correctness)
        {
            var cEnd = c.StartPos + Math.Max(c.OldText.Length, 1);
            var overlap = Math.Min(hEnd, cEnd) - Math.Max(h.StartPos, c.StartPos);
            if (overlap > 0)
                return true;

            // Same edit text even if offsets drifted slightly after polish.
            if (string.Equals(h.OldText, c.OldText, StringComparison.Ordinal) &&
                string.Equals(h.NewText, c.NewText, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
