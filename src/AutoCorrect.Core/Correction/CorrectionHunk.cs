namespace AutoCorrect.Core.Correction;

/// <summary>
/// One suggested edit. <see cref="StartPos"/> is 0-based index into the original string
/// (AHK used 1-based; empty OldText inserts must not search by needle).
/// </summary>
public sealed class CorrectionHunk
{
    public int StartPos { get; set; }
    public string OldText { get; init; } = "";
    public string NewText { get; init; } = "";
    public bool Selected { get; set; } = true;

    public SuggestionCategory Category { get; set; } = SuggestionCategory.Other;
    public string CategoryLabel { get; set; } = "Correct your writing";
    public string? ShortMessage { get; set; }
    public string? RuleId { get; set; }

    /// <summary>Heuristic Fix-card confidence (set by annotators).</summary>
    public SuggestionConfidence Confidence { get; set; } = SuggestionConfidence.Medium;

    public bool IsSpelling => Category == SuggestionCategory.Spelling;

    /// <summary>Word to persist when user chooses Add to dictionary (spelling).</summary>
    public string? DictionaryToken =>
        SuggestionCategoryLabels.DictionaryToken(OldText.Length > 0 ? OldText : NewText);

    public string Label
    {
        get
        {
            static string Clip(string s)
            {
                s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                return s.Length <= 42 ? s : s[..40] + "…";
            }

            var old = Clip(OldText);
            var neu = Clip(NewText);
            if (old.Length == 0)
                return "＋ " + neu;
            if (neu.Length == 0)
                return "✕ " + old;
            return old + "  →  " + neu;
        }
    }

    public CorrectionHunk Clone() => new()
    {
        StartPos = StartPos,
        OldText = OldText,
        NewText = NewText,
        Selected = Selected,
        Category = Category,
        CategoryLabel = CategoryLabel,
        ShortMessage = ShortMessage,
        RuleId = RuleId,
        Confidence = Confidence
    };

    /// <summary>Apply this single hunk to <paramref name="text"/> (ignores <see cref="Selected"/>).</summary>
    public string ApplyTo(string text) =>
        TryApply(text, out var result, out _, out _) ? result : text;

    /// <summary>
    /// Apply this hunk surgically. Returns false when the old text cannot be located
    /// (caller should skip). <paramref name="appliedAt"/> is the actual edit index used.
    /// </summary>
    public bool TryApply(string text, out string result, out int appliedAt, out int replacedLength)
    {
        result = text;
        appliedAt = StartPos;
        replacedLength = OldText.Length;

        if (StartPos < 0 || StartPos > text.Length)
            return false;

        if (OldText.Length == 0)
        {
            // Pure insert at StartPos (clamped).
            appliedAt = Math.Clamp(StartPos, 0, text.Length);
            replacedLength = 0;
            result = text[..appliedAt] + NewText + text[appliedAt..];
            return true;
        }

        var end = Math.Min(text.Length, StartPos + OldText.Length);
        int idx;
        if (end <= text.Length &&
            string.Equals(text[StartPos..end], OldText, StringComparison.Ordinal))
        {
            idx = StartPos;
        }
        else
        {
            // Stale offset — fall back to first exact occurrence after StartPos, else anywhere.
            idx = text.IndexOf(OldText, Math.Min(StartPos, text.Length), StringComparison.Ordinal);
            if (idx < 0)
                idx = text.IndexOf(OldText, StringComparison.Ordinal);
            if (idx < 0)
                return false;
        }

        appliedAt = idx;
        replacedLength = OldText.Length;
        result = text[..idx] + NewText + text[(idx + OldText.Length)..];
        return true;
    }
}
