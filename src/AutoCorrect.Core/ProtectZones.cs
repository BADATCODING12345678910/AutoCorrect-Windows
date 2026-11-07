using System.Text.RegularExpressions;

namespace AutoCorrect.Core;

/// <summary>
/// Skip autocorrect / Fix inside emails, URLs, handles, tags, paths, and code-ish tokens.
/// </summary>
public static partial class ProtectZones
{
    public static bool IsProtectedToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var t = token.Trim();
        if (t.Length == 0)
            return false;

        if (t.StartsWith('@') || t.StartsWith('#'))
            return true;

        if (t.Contains('@') && EmailRegex().IsMatch(t))
            return true;

        if (t.Contains("://", StringComparison.Ordinal) ||
            t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            UrlRegex().IsMatch(t))
            return true;

        if (t.Contains('\\') || (t.Length >= 3 && t[1] == ':' && (t[2] == '\\' || t[2] == '/')))
            return true;

        if (t.Contains('/') && t.Contains('.') && !t.Contains(' '))
            return true;

        if (CodeTokenRegex().IsMatch(t))
            return true;

        return false;
    }

    /// <summary>
    /// True when a span in <paramref name="fullText"/> overlaps a protected region
    /// (URLs, emails, @handles, #tags, paths).
    /// </summary>
    public static bool SpanIsProtected(string fullText, int start, int length)
    {
        if (string.IsNullOrEmpty(fullText) || length <= 0)
            return false;

        start = Math.Clamp(start, 0, fullText.Length);
        var end = Math.Clamp(start + length, start, fullText.Length);
        if (end <= start)
            return false;

        foreach (Match m in ProtectedSpanRegex().Matches(fullText))
        {
            if (m.Index < end && m.Index + m.Length > start)
                return true;
        }

        var token = fullText[start..end];
        return IsProtectedToken(token);
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(https?://|www\.)\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*([.][A-Za-z_][A-Za-z0-9_]*)+$|^[a-f0-9]{8,}$|[{}<>;=]", RegexOptions.CultureInvariant)]
    private static partial Regex CodeTokenRegex();

    [GeneratedRegex(
        @"https?://\S+|www\.\S+|[^\s@]+@[^\s@]+\.[^\s@]+|@[A-Za-z0-9_]{2,}|#[A-Za-z0-9_]{2,}|(?:[A-Za-z]:)?[\\/][^\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedSpanRegex();
}
