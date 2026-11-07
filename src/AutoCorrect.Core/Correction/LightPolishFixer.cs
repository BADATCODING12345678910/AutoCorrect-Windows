using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Correction;

/// <summary>Capitalization polish — port of DictationFixer.FixWithLightPolish.</summary>
public static partial class LightPolishFixer
{
    public static string Apply(string text)
    {
        var outText = LoneI().Replace(text, "I");

        var m = LeadingLower().Match(outText);
        if (m.Success)
        {
            outText = m.Groups[1].Value
                      + char.ToUpperInvariant(m.Groups[2].Value[0])
                      + outText[m.Length..];
        }

        outText = AfterSentence().Replace(outText, static match =>
            match.Groups[1].Value + match.Groups[2].Value + char.ToUpperInvariant(match.Groups[3].Value[0]));

        return outText;
    }

    [GeneratedRegex(@"(?<=^|[^A-Za-z])i(?=$|[^A-Za-z])")]
    private static partial Regex LoneI();

    [GeneratedRegex(@"^(\s*)([a-z])")]
    private static partial Regex LeadingLower();

    [GeneratedRegex(@"([.!?]""?)(\s+)([a-z])")]
    private static partial Regex AfterSentence();
}
