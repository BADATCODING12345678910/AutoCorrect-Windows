namespace AutoCorrect.Core.Correction;

/// <summary>
/// Back-compat wrapper — prefer <see cref="ContextualAccidentFixer"/>.
/// </summary>
public static class SpaceNormalizer
{
    public static string Apply(string text) => ContextualAccidentFixer.NormalizeSpaces(text);

    public static bool DetectsDoubleSpaceSentenceStyle(string text) =>
        TextStyleProfile.Analyze(text).PreferDoubleSpaceAfterSentence;
}
