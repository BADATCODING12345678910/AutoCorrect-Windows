namespace AutoCorrect.Core.Licensing;

public enum LicenseTier
{
    None = 0,
    Trial = 1,
    Personal = 2,
    Pro = 3,
    Team = 4,
    Lifetime = 5,
}

public enum LicenseAccess
{
    /// <summary>Full features (licensed or still in trial).</summary>
    Full,
    /// <summary>Trial ended, no key — core Fix/autocorrect only; AI gated.</summary>
    CoreOnly,
}

public sealed class LicenseInfo
{
    public required LicenseTier Tier { get; init; }
    public required LicenseAccess Access { get; init; }
    public string? Email { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public DateTime? TrialEndsUtc { get; init; }
    public int? TrialDaysLeft { get; init; }
    public string StatusLine { get; init; } = "";
    public string DetailLine { get; init; } = "";
    public bool IsLicensed =>
        Tier is LicenseTier.Personal or LicenseTier.Pro or LicenseTier.Team or LicenseTier.Lifetime;
    public bool AllowsPremiumAi => Access == LicenseAccess.Full;
}
