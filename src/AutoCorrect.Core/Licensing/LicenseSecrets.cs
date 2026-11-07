namespace AutoCorrect.Core.Licensing;

/// <summary>
/// HMAC secret used to sign/verify license keys. Rotate before public launch
/// and keep the generator tool in sync (tools/LicenseKeyGen).
/// </summary>
internal static class LicenseSecrets
{
    // Product verification key — not a payment credential.
    internal const string HmacUtf8 =
        "AutoCorrect-Native-v2-license-hmac-2026-change-before-launch!";
}
