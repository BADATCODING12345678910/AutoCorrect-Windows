using AutoCorrect.Core.Correction;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoCorrect.App.ViewModels;

public partial class HunkItemViewModel : ObservableObject
{
    public HunkItemViewModel(CorrectionHunk hunk, string workingText, bool tighten = true)
    {
        // Nearby merged clusters must not be re-tightened into a single token.
        Hunk = tighten ? WordDiff.TightenHunk(hunk) : hunk;
        CategoryLabel = Hunk.CategoryLabel;
        CategoryBadge = SuggestionCategoryLabels.Badge(Hunk.Category);
        ConfidenceChip = SuggestionConfidenceLabels.Chip(Hunk.Confidence);
        WhyText = BuildWhyText(Hunk);
        RuleTooltip = string.IsNullOrWhiteSpace(Hunk.RuleId) ? WhyText : WhyText + " · " + Hunk.RuleId;
        IsSpelling = Hunk.IsSpelling;
        DictionaryToken = Hunk.DictionaryToken;
        HasOld = Hunk.OldText.Length > 0;
        HasNew = Hunk.NewText.Length > 0;

        OldDisplay = DisplayToken(Hunk.OldText);
        NewDisplay = HasNew ? DisplayToken(Hunk.NewText) : "(remove)";

        BuildContext(workingText, Hunk);
        ChangeSummary = BuildChangeSummary(Hunk);
        ShowOldStrike = HasOld && !string.Equals(Hunk.OldText, Hunk.NewText, StringComparison.Ordinal);
        ShowNewAccent = HasNew;
        ShowInsertMarker = !HasOld && HasNew;
    }

    public CorrectionHunk Hunk { get; }
    public string CategoryLabel { get; }
    /// <summary>Short uppercase badge for the card header, e.g. SPELLING.</summary>
    public string CategoryBadge { get; }
    /// <summary>Muted explanation under the badge.</summary>
    public string WhyText { get; }
    /// <summary>Heuristic chip: Likely typo / Grammar / Style.</summary>
    public string ConfidenceChip { get; }
    public string RuleTooltip { get; }
    public bool HasWhyText => !string.IsNullOrWhiteSpace(WhyText);
    public string OldDisplay { get; }
    public string NewDisplay { get; }
    public bool HasOld { get; }
    public bool HasNew { get; }
    public bool IsSpelling { get; }
    public string? DictionaryToken { get; }
    public bool CanAddToDictionary =>
        IsSpelling && !string.IsNullOrWhiteSpace(DictionaryToken);

    /// <summary>Words before the focused edit (muted).</summary>
    public string LeftContext { get; private set; } = "";

    /// <summary>Words after the focused edit (muted).</summary>
    public string RightContext { get; private set; } = "";

    public bool HasLeftContext => LeftContext.Length > 0;
    public bool HasRightContext => RightContext.Length > 0;
    public bool HasSentenceContext => HasLeftContext || HasRightContext;

    /// <summary>Always-visible focused change, e.g. “somethin” → “something”.</summary>
    public string ChangeSummary { get; }

    public bool ShowOldStrike { get; }
    public bool ShowNewAccent { get; }
    public bool ShowInsertMarker { get; }

    public string PrimarySuggestion => NewDisplay;

    private static string BuildWhyText(CorrectionHunk hunk)
    {
        var label = (hunk.CategoryLabel ?? "").Trim();
        var msg = (hunk.ShortMessage ?? "").Trim();
        if (msg.Length == 0)
            return label;
        if (label.Length == 0)
            return msg;
        if (string.Equals(label, msg, StringComparison.OrdinalIgnoreCase))
            return label;
        // Prefer the more specific LT short message when it differs.
        return msg.Length <= 72 ? msg : label;
    }

    private void BuildContext(string workingText, CorrectionHunk hunk)
    {
        if (string.IsNullOrEmpty(workingText))
            return;

        var start = Math.Clamp(hunk.StartPos, 0, workingText.Length);
        var end = Math.Clamp(start + hunk.OldText.Length, start, workingText.Length);

        if (hunk.OldText.Length > 0 &&
            (end > workingText.Length ||
             !string.Equals(workingText[start..end], hunk.OldText, StringComparison.Ordinal)))
        {
            var idx = workingText.IndexOf(hunk.OldText, Math.Max(0, start - 40), StringComparison.Ordinal);
            if (idx < 0)
                idx = workingText.IndexOf(hunk.OldText, StringComparison.Ordinal);
            if (idx >= 0)
            {
                start = idx;
                end = idx + hunk.OldText.Length;
            }
        }

        LeftContext = SliceWordsBefore(workingText, start, maxChars: 36);
        RightContext = SliceWordsAfter(workingText, end, maxChars: 36);
    }

    private static string BuildChangeSummary(CorrectionHunk hunk)
    {
        var oldPart = CollapseWs(hunk.OldText);
        var newPart = CollapseWs(hunk.NewText);

        if (oldPart.Length == 0 && newPart.Length > 0)
            return "Insert “" + Clip(newPart, 48) + "”";
        if (newPart.Length == 0 && oldPart.Length > 0)
            return "Remove “" + Clip(oldPart, 48) + "”";

        // Show ONLY the differing tokens — never truncate a long shared prefix.
        return "“" + Clip(oldPart, 48) + "” → “" + Clip(newPart, 48) + "”";
    }

    private static string DisplayToken(string s)
    {
        s = CollapseWs(s);
        // Keep focused tokens readable; don't clip so aggressively that diffs vanish.
        return Clip(s, 64);
    }

    private static string SliceWordsBefore(string text, int index, int maxChars)
    {
        if (index <= 0)
            return "";
        var start = Math.Max(0, index - maxChars - 16);
        var slice = text[start..index];
        if (start > 0)
        {
            var sp = slice.IndexOfAny([' ', '\t', '\r', '\n']);
            if (sp >= 0 && sp < slice.Length - 1)
                slice = slice[(sp + 1)..];
            else
                slice = "…" + slice;
        }
        slice = CollapseWs(slice);
        if (slice.Length > maxChars)
            slice = "…" + slice[^maxChars..];
        return slice;
    }

    private static string SliceWordsAfter(string text, int index, int maxChars)
    {
        if (index >= text.Length)
            return "";
        var end = Math.Min(text.Length, index + maxChars + 16);
        var slice = text[index..end];
        if (end < text.Length)
        {
            var sp = slice.LastIndexOfAny([' ', '\t', '\r', '\n']);
            if (sp > 0)
                slice = slice[..sp];
            else
                slice += "…";
        }
        slice = CollapseWs(slice);
        if (slice.Length > maxChars)
            slice = slice[..maxChars] + "…";
        return slice;
    }

    private static string CollapseWs(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
