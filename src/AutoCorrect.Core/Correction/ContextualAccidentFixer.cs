using System.Text;
using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Correction;

/// <summary>
/// High-confidence accident cleanup driven by the surrounding paragraph's style.
/// Examples: accidental double spaces between words, doubled commas, space-before
/// comma, glued "word.Next" — kept or fixed depending on whether the rest of the
/// text looks intentional.
/// </summary>
public static partial class ContextualAccidentFixer
{
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var style = TextStyleProfile.Analyze(text);
        var result = NormalizeSpaces(text, style);
        result = CollapseAccidentalDuplicateCommas(result);
        result = NormalizeSpaceBeforePunct(result, style);
        // ".Start" after sentence punct is almost always a missed space (not style-gated —
        // otherwise a lone accident would look like "intentional glue" and never heal).
        result = SplitGluedSentences(result);
        result = CollapseAccidentalDoublePeriods(result, style);
        return result;
    }

    /// <summary>Collapse mid-word double spaces; honor double-space-after-sentence style when clear.</summary>
    public static string NormalizeSpaces(string text, TextStyleProfile? style = null)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("  ", StringComparison.Ordinal) < 0)
            return text;

        style ??= TextStyleProfile.Analyze(text);
        var preserveSentenceDouble = style.PreferDoubleSpaceAfterSentence;
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != ' ')
            {
                sb.Append(text[i]);
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] == ' ')
                i++;
            var run = i - start;
            if (run == 1)
            {
                sb.Append(' ');
                continue;
            }

            if (preserveSentenceDouble && IsAfterSentenceEnd(sb))
                sb.Append("  ");
            else
                sb.Append(' ');
        }

        return sb.ToString();
    }

    private static string CollapseAccidentalDuplicateCommas(string text) =>
        // ",," / ", ," almost never intentional; ",," → ","
        DoubledComma().Replace(text, ",");

    private static string NormalizeSpaceBeforePunct(string text, TextStyleProfile style)
    {
        if (style.PreferSpaceBeforePunct)
            return text;

        // English default: no space before , . ! ? ; :
        return SpaceBeforeAsciiPunct().Replace(text, "$1");
    }

    private static string SplitGluedSentences(string text) =>
        // "end.Start" → "end. Start" (capital after sentence punct).
        GluedSentence().Replace(text, "$1 $2");

    private static string CollapseAccidentalDoublePeriods(string text, TextStyleProfile style)
    {
        // Always keep real ellipsis "...". Exactly ".." is almost always a slip
        // unless the paragraph already uses ellipsis heavily (then leave ".." alone too).
        if (style.UsesEllipsis)
            return text;

        return OrphanDoublePeriod().Replace(text, ".");
    }

    private static bool IsAfterSentenceEnd(StringBuilder sb)
    {
        var i = sb.Length - 1;
        while (i >= 0 && sb[i] is '"' or '\'' or ')' or ']')
            i--;
        return i >= 0 && sb[i] is '.' or '!' or '?';
    }

    [GeneratedRegex(@",\s*,+")]
    private static partial Regex DoubledComma();

    [GeneratedRegex(@" +([,.;:!?])")]
    private static partial Regex SpaceBeforeAsciiPunct();

    [GeneratedRegex(@"([.!?])([A-Z])")]
    private static partial Regex GluedSentence();

    // "word.. word" but not "word... word"
    [GeneratedRegex(@"(?<!\.)\.\.(?!\.)")]
    private static partial Regex OrphanDoublePeriod();
}
