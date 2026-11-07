using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Correction;

/// <summary>
/// Cheap style signals from the current paragraph/selection — used to tell
/// accidental typos apart from intentional formatting.
/// </summary>
public sealed partial class TextStyleProfile
{
    public bool PreferDoubleSpaceAfterSentence { get; init; }
    public bool PreferSpaceBeforePunct { get; init; }
    public bool PreferSpaceAfterSentencePunct { get; init; } = true;
    public bool UsesEllipsis { get; init; }
    public bool UsesEmphaticRepeats { get; init; }

    public static TextStyleProfile Analyze(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new TextStyleProfile();

        var sentenceSingle = 0;
        var sentenceMulti = 0;
        foreach (Match m in SentenceGap().Matches(text))
        {
            if (m.Groups[1].Length >= 2)
                sentenceMulti++;
            else
                sentenceSingle++;
        }

        var spaceBeforePunct = SpaceBeforePunct().Matches(text).Count;
        var tightPunct = TightPunct().Matches(text).Count;

        var spacedAfter = SpacedAfterSentence().Matches(text).Count;
        var gluedAfter = GluedAfterSentence().Matches(text).Count;

        return new TextStyleProfile
        {
            PreferDoubleSpaceAfterSentence = sentenceMulti >= 2 && sentenceMulti > sentenceSingle,
            PreferSpaceBeforePunct = spaceBeforePunct >= 2 && spaceBeforePunct > tightPunct,
            PreferSpaceAfterSentencePunct = gluedAfter == 0 || spacedAfter >= gluedAfter,
            UsesEllipsis = text.Contains("...", StringComparison.Ordinal),
            UsesEmphaticRepeats = EmphaticBang().IsMatch(text) || EmphaticQuest().IsMatch(text)
        };
    }

    [GeneratedRegex("""[.!?]["']?( +)(?=\S)""")]
    private static partial Regex SentenceGap();

    // French-ish: space before : ; ! ?
    [GeneratedRegex(@"\S ([;:!?])")]
    private static partial Regex SpaceBeforePunct();

    [GeneratedRegex(@"\S([,.;:!?])")]
    private static partial Regex TightPunct();

    [GeneratedRegex("""[.!?]["']? [A-Za-z0-9"']""")]
    private static partial Regex SpacedAfterSentence();

    [GeneratedRegex(@"[.!?]([A-Za-z])")]
    private static partial Regex GluedAfterSentence();

    [GeneratedRegex(@"!!+")]
    private static partial Regex EmphaticBang();

    [GeneratedRegex(@"\?\?+")]
    private static partial Regex EmphaticQuest();
}
