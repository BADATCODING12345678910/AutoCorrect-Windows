using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Correction;

/// <summary>Heuristic punctuation for run-on dictation — port of DictationFixer.FixWithPunctuation.</summary>
public static partial class PunctuationFixer
{
    public static string Apply(string text)
    {
        var outText = text.Trim();
        if (outText.Length == 0)
            return text;

        outText = MultiSpace().Replace(outText, " ");

        outText = ButJoiner().Replace(outText, ", but ");
        outText = SoJoiner().Replace(outText, ", so ");
        outText = WhichJoiner().Replace(outText, ", which ");
        outText = AndJoiner().Replace(outText, ", and ");

        outText = LikeOhNo().Replace(outText, "like, \"$1");
        outText = OhNoThats().Replace(outText, "oh no, that's");
        outText = OhNoThatIs().Replace(outText, "oh no, that is");

        outText = CloseQuoteThere().Replace(outText, "$1.\" $2");
        outText = CloseQuoteThen().Replace(outText, "$1.\" $2");

        if (outText.Contains("like, \"", StringComparison.OrdinalIgnoreCase) &&
            !LikeQuoted().IsMatch(outText))
        {
            outText = CloseQuoteBeforeClause().Replace(outText, "$1.\"$2");
            if (!LikeQuoted().IsMatch(outText))
                outText = CloseQuoteAtEnd().Replace(outText, "$1.\"");
        }

        outText = SentenceBeforeThere().Replace(outText, "$1. $2");
        outText = NoYesComma().Replace(outText, "$1, that's");

        if (!EndsWithSentencePunct().IsMatch(outText))
            outText += ".";

        outText = DoubledComma().Replace(outText, ", ");
        outText = SpaceBeforePunct().Replace(outText, "$1");
        outText = DoubledSentencePunct().Replace(outText, "$1");
        outText = MultiSpace().Replace(outText, " ");
        return outText.Trim();
    }

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"(?<![,\s])\s+but\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ButJoiner();

    [GeneratedRegex(@"(?<![,\s])\s+so\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SoJoiner();

    [GeneratedRegex(@"(?<![,\s])\s+which\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WhichJoiner();

    [GeneratedRegex(@"(?<![,\s])\s+and\s+(?=the\s|I\s|then\s|also\s)", RegexOptions.IgnoreCase)]
    private static partial Regex AndJoiner();

    [GeneratedRegex(@"\blike\s+(oh\s+no)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LikeOhNo();

    [GeneratedRegex(@"\boh\s+no\s+that's\b", RegexOptions.IgnoreCase)]
    private static partial Regex OhNoThats();

    [GeneratedRegex(@"\boh\s+no\s+that is\b", RegexOptions.IgnoreCase)]
    private static partial Regex OhNoThatIs();

    [GeneratedRegex(@"(like,\s*""[^""]*?)\s+(there\s+(?:was|were|is|are)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex CloseQuoteThere();

    [GeneratedRegex(@"(like,\s*""[^""]*?)\s+(then\s+I\b)", RegexOptions.IgnoreCase)]
    private static partial Regex CloseQuoteThen();

    [GeneratedRegex(@"like,\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex LikeQuoted();

    [GeneratedRegex(@"(like,\s*""[^"",.!?]+)(\s*,\s*(?:but|so|and)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex CloseQuoteBeforeClause();

    [GeneratedRegex(@"(like,\s*""[^""]+?)([.!?]?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CloseQuoteAtEnd();

    [GeneratedRegex(@"([a-z0-9'])\s+(there\s+(?:was|were|is|are)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex SentenceBeforeThere();

    [GeneratedRegex(@"\b(no|yes|well|okay|ok)\s+that's\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoYesComma();

    [GeneratedRegex(@"[.!?]\""?\s*$")]
    private static partial Regex EndsWithSentencePunct();

    [GeneratedRegex(@",\s*,+")]
    private static partial Regex DoubledComma();

    [GeneratedRegex(@"\s+([,.!?])")]
    private static partial Regex SpaceBeforePunct();

    [GeneratedRegex(@"([.!?]){2,}")]
    private static partial Regex DoubledSentencePunct();
}
