namespace AutoCorrect.Core.Correction;

/// <summary>
/// Maps Settings → Grammar Fix quality to the effective correction provider.
/// Perfect = LanguageTool then ChatGPT when licensed + key; otherwise free LT path.
/// </summary>
public static class FixQualityResolver
{
    public static bool IsPerfect(string? fixQuality) =>
        string.Equals(fixQuality, "perfect", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the provider/order to use for Fix. Does not apply Privacy (caller forces none).
    /// For Standard, returns the configured provider/order unchanged (caller may still downgrade ChatGPT when trial ended).
    /// </summary>
    public static (string Provider, string ProviderOrder, bool SuggestAddKeyToast, bool SuggestLicenseToast)
        Resolve(
            string? fixQuality,
            string configuredProvider,
            string configuredOrder,
            bool premiumOk,
            bool hasOpenAiKey)
    {
        if (!IsPerfect(fixQuality))
            return (configuredProvider, configuredOrder, false, false);

        if (premiumOk && hasOpenAiKey)
            return ("both", "languagetool_then_chatgpt", false, false);

        if (!hasOpenAiKey)
            return ("languagetool", configuredOrder, true, false);

        // Has key but no premium entitlement
        return ("languagetool", configuredOrder, false, true);
    }
}
