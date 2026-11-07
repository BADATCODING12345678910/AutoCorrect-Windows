namespace AutoCorrect.Core.Correction;

/// <summary>
/// Languages supported by the public LanguageTool <c>/v2/check</c> API (Fix pipeline).
/// Codes are LT <c>longCode</c> values. As-you-type autocorrect remains English-oriented.
/// </summary>
public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class LanguageCatalog
{
    /// <summary>Auto language detection (LT <c>language=auto</c>).</summary>
    public static LanguageOption AutoDetect { get; } = new("auto", "Auto-detect language");

    /// <summary>
    /// Full LT public catalog (fetched 2026-11-01 from api.languagetool.org/v2/languages),
    /// plus Auto-detect. Prefer country variants over bare codes when both exist.
    /// </summary>
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        AutoDetect,

        // —— English ——
        new("en-US", "English (United States)"),
        new("en-GB", "English (United Kingdom)"),
        new("en-CA", "English (Canada)"),
        new("en-AU", "English (Australia)"),
        new("en-NZ", "English (New Zealand)"),
        new("en-ZA", "English (South Africa)"),
        new("en", "English (generic)"),

        // —— Spanish ——
        new("es-ES", "Spanish (Spain)"),
        new("es-AR", "Spanish (Argentina / voseo)"),
        new("es", "Spanish (generic)"),

        // —— Portuguese ——
        new("pt-BR", "Portuguese (Brazil)"),
        new("pt-PT", "Portuguese (Portugal)"),
        new("pt-AO", "Portuguese (Angola)"),
        new("pt-MZ", "Portuguese (Mozambique)"),
        new("pt", "Portuguese (generic)"),

        // —— French ——
        new("fr-FR", "French (France)"),
        new("fr-CA", "French (Canada)"),
        new("fr-BE", "French (Belgium)"),
        new("fr-CH", "French (Switzerland)"),
        new("fr", "French (generic)"),

        // —— German ——
        new("de-DE", "German (Germany)"),
        new("de-AT", "German (Austria)"),
        new("de-CH", "German (Switzerland)"),
        new("de-LU", "German (Luxembourg)"),
        new("de-DE-x-simple-language", "German (Simple)"),
        new("de", "German (generic)"),

        // —— Dutch ——
        new("nl-NL", "Dutch (Netherlands)"),
        new("nl-BE", "Dutch (Belgium)"),
        new("nl", "Dutch (generic)"),

        // —— Italian / other Romance ——
        new("it-IT", "Italian"),
        new("ca-ES", "Catalan"),
        new("ca-ES-valencia", "Catalan (Valencian)"),
        new("ca-ES-balear", "Catalan (Balearic)"),
        new("gl-ES", "Galician"),
        new("ro-RO", "Romanian"),

        // —— Nordic / Baltic / Slavic ——
        new("da-DK", "Danish"),
        new("sv-SE", "Swedish"),
        new("nb", "Norwegian (Bokmål)"),
        new("pl-PL", "Polish"),
        new("ru-RU", "Russian"),
        new("uk-UA", "Ukrainian"),
        new("sk-SK", "Slovak"),
        new("sl-SI", "Slovenian"),
        new("be-BY", "Belarusian"),
        new("crh-UA", "Crimean Tatar"),

        // —— Celtic / others (Europe) ——
        new("el-GR", "Greek"),
        new("ga-IE", "Irish"),
        new("br-FR", "Breton"),
        new("ast-ES", "Asturian"),
        new("eo", "Esperanto"),

        // —— Middle East / South Asia / SE Asia / East Asia ——
        new("ar", "Arabic"),
        new("fa-IR", "Persian (Farsi)"),
        new("ta-IN", "Tamil"),
        new("km-KH", "Khmer"),
        new("tl-PH", "Tagalog"),
        new("ja-JP", "Japanese"),
        new("zh-CN", "Chinese (Simplified)"),
        // Public LT API currently exposes Simplified Chinese only (no separate zh-TW pack).
    ];

    public static LanguageOption FindOrCustom(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return FindOrCustom("en-US");

        var trimmed = code.Trim();
        foreach (var opt in All)
        {
            if (string.Equals(opt.Code, trimmed, StringComparison.OrdinalIgnoreCase))
                return opt;
        }

        // Unknown / future LT code — still usable (editable historically).
        return new LanguageOption(trimmed, trimmed + " (custom)");
    }

    public static bool IsKnown(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        All.Any(o => string.Equals(o.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}
