using Avalonia.Media;

namespace AutoCorrect.App.Themes;

public enum ThemeCategory
{
    Dark,
    Light,
    Gradient
}

/// <summary>Named UI palette. Accent brushes keep legacy AcPurple* keys for existing XAML.</summary>
public sealed class AppThemePalette
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ThemeCategory Category { get; init; }
    public required bool IsDark { get; init; }

    public required Color Black { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceRaised { get; init; }
    public required Color Border { get; init; }
    public required Color BorderSoft { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentDeep { get; init; }
    public required Color AccentSoft { get; init; }
    public required Color AccentGlow { get; init; }
    public required Color AccentHoverSoft { get; init; }
    public required Color AccentHoverStrong { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Strike { get; init; }
    public required Color OnAccent { get; init; }

    /// <summary>When set, window chrome uses a vertical gradient from <see cref="Black"/> to this color.</summary>
    public Color? WindowGradientEnd { get; init; }

    public string CategoryLabel => Category switch
    {
        ThemeCategory.Light => "Light",
        ThemeCategory.Gradient => "Gradient",
        _ => "Dark"
    };
}

public static class AppThemeCatalog
{
    public const string DefaultThemeId = "light-lavender";

    public static IReadOnlyList<AppThemePalette> All { get; } =
    [
        // —— Dark ——
        new AppThemePalette
        {
            Id = "midnight-purple",
            DisplayName = "Midnight Purple",
            Category = ThemeCategory.Dark,
            IsDark = true,
            Black = Color.Parse("#07070C"),
            Surface = Color.Parse("#0C0B12"),
            SurfaceRaised = Color.Parse("#17141F"),
            Border = Color.Parse("#473A66"),
            BorderSoft = Color.Parse("#2E2740"),
            Accent = Color.Parse("#A78BFA"),
            AccentDeep = Color.Parse("#7C5CBF"),
            AccentSoft = Color.Parse("#C4B5FD"),
            AccentGlow = Color.Parse("#33A78BFA"),
            AccentHoverSoft = Color.Parse("#1AA78BFA"),
            AccentHoverStrong = Color.Parse("#44A78BFA"),
            Text = Color.Parse("#F4F2FF"),
            TextMuted = Color.Parse("#A39BB8"),
            Strike = Color.Parse("#8A819E"),
            OnAccent = Color.Parse("#0E0E10"),
        },
        new AppThemePalette
        {
            Id = "charcoal",
            DisplayName = "Charcoal",
            Category = ThemeCategory.Dark,
            IsDark = true,
            Black = Color.Parse("#0A0A0B"),
            Surface = Color.Parse("#121214"),
            SurfaceRaised = Color.Parse("#1C1C1F"),
            Border = Color.Parse("#3A3A42"),
            BorderSoft = Color.Parse("#2A2A30"),
            Accent = Color.Parse("#94A3B8"),
            AccentDeep = Color.Parse("#64748B"),
            AccentSoft = Color.Parse("#CBD5E1"),
            AccentGlow = Color.Parse("#3394A3B8"),
            AccentHoverSoft = Color.Parse("#1A94A3B8"),
            AccentHoverStrong = Color.Parse("#4494A3B8"),
            Text = Color.Parse("#F1F5F9"),
            TextMuted = Color.Parse("#94A3B8"),
            Strike = Color.Parse("#64748B"),
            OnAccent = Color.Parse("#0A0A0B"),
        },
        new AppThemePalette
        {
            Id = "ocean-night",
            DisplayName = "Ocean Night",
            Category = ThemeCategory.Dark,
            IsDark = true,
            Black = Color.Parse("#050B12"),
            Surface = Color.Parse("#0A1622"),
            SurfaceRaised = Color.Parse("#122636"),
            Border = Color.Parse("#1E4A66"),
            BorderSoft = Color.Parse("#163448"),
            Accent = Color.Parse("#38BDF8"),
            AccentDeep = Color.Parse("#0284C7"),
            AccentSoft = Color.Parse("#7DD3FC"),
            AccentGlow = Color.Parse("#3338BDF8"),
            AccentHoverSoft = Color.Parse("#1A38BDF8"),
            AccentHoverStrong = Color.Parse("#4438BDF8"),
            Text = Color.Parse("#E8F4FC"),
            TextMuted = Color.Parse("#8BAFC4"),
            Strike = Color.Parse("#6B8FA3"),
            OnAccent = Color.Parse("#041018"),
        },
        new AppThemePalette
        {
            Id = "forest",
            DisplayName = "Forest",
            Category = ThemeCategory.Dark,
            IsDark = true,
            Black = Color.Parse("#070C09"),
            Surface = Color.Parse("#0C1510"),
            SurfaceRaised = Color.Parse("#15241B"),
            Border = Color.Parse("#2F5A40"),
            BorderSoft = Color.Parse("#1F3A2A"),
            Accent = Color.Parse("#4ADE80"),
            AccentDeep = Color.Parse("#16A34A"),
            AccentSoft = Color.Parse("#86EFAC"),
            AccentGlow = Color.Parse("#334ADE80"),
            AccentHoverSoft = Color.Parse("#1A4ADE80"),
            AccentHoverStrong = Color.Parse("#444ADE80"),
            Text = Color.Parse("#ECFDF3"),
            TextMuted = Color.Parse("#8FB9A0"),
            Strike = Color.Parse("#6B9080"),
            OnAccent = Color.Parse("#052010"),
        },
        new AppThemePalette
        {
            Id = "rose-ember",
            DisplayName = "Rose Ember",
            Category = ThemeCategory.Dark,
            IsDark = true,
            Black = Color.Parse("#0E080A"),
            Surface = Color.Parse("#161014"),
            SurfaceRaised = Color.Parse("#241820"),
            Border = Color.Parse("#6B3A4A"),
            BorderSoft = Color.Parse("#3E2530"),
            Accent = Color.Parse("#FB7185"),
            AccentDeep = Color.Parse("#E11D48"),
            AccentSoft = Color.Parse("#FDA4AF"),
            AccentGlow = Color.Parse("#33FB7185"),
            AccentHoverSoft = Color.Parse("#1AFB7185"),
            AccentHoverStrong = Color.Parse("#44FB7185"),
            Text = Color.Parse("#FFF1F3"),
            TextMuted = Color.Parse("#C4A0A8"),
            Strike = Color.Parse("#9A7A82"),
            OnAccent = Color.Parse("#1A080C"),
        },
        new AppThemePalette
        {
            Id = "cyber-neon",
            DisplayName = "Cyber Neon",
            Category = ThemeCategory.Dark,
            IsDark = true,
            Black = Color.Parse("#03050A"),
            Surface = Color.Parse("#080C14"),
            SurfaceRaised = Color.Parse("#101828"),
            Border = Color.Parse("#1E3A5F"),
            BorderSoft = Color.Parse("#152238"),
            Accent = Color.Parse("#22D3EE"),
            AccentDeep = Color.Parse("#A855F7"),
            AccentSoft = Color.Parse("#67E8F9"),
            AccentGlow = Color.Parse("#3322D3EE"),
            AccentHoverSoft = Color.Parse("#1A22D3EE"),
            AccentHoverStrong = Color.Parse("#4422D3EE"),
            Text = Color.Parse("#E0F7FF"),
            TextMuted = Color.Parse("#7AA8C0"),
            Strike = Color.Parse("#5A8098"),
            OnAccent = Color.Parse("#021018"),
        },

        // —— Light ——
        new AppThemePalette
        {
            Id = "light-clean",
            DisplayName = "Light Clean",
            Category = ThemeCategory.Light,
            IsDark = false,
            Black = Color.Parse("#F4F6FA"),
            Surface = Color.Parse("#FFFFFF"),
            SurfaceRaised = Color.Parse("#EEF1F6"),
            Border = Color.Parse("#C5CDD8"),
            BorderSoft = Color.Parse("#DDE2EA"),
            Accent = Color.Parse("#2563EB"),
            AccentDeep = Color.Parse("#1D4ED8"),
            AccentSoft = Color.Parse("#60A5FA"),
            AccentGlow = Color.Parse("#332563EB"),
            AccentHoverSoft = Color.Parse("#1A2563EB"),
            AccentHoverStrong = Color.Parse("#332563EB"),
            Text = Color.Parse("#0F172A"),
            TextMuted = Color.Parse("#64748B"),
            Strike = Color.Parse("#94A3B8"),
            OnAccent = Color.Parse("#FFFFFF"),
        },
        new AppThemePalette
        {
            Id = "light-lavender",
            DisplayName = "Light Lavender",
            Category = ThemeCategory.Light,
            IsDark = false,
            Black = Color.Parse("#F5F3FA"),
            Surface = Color.Parse("#FFFFFF"),
            SurfaceRaised = Color.Parse("#EDE9F6"),
            Border = Color.Parse("#C4B5E0"),
            BorderSoft = Color.Parse("#DDD6EC"),
            Accent = Color.Parse("#7C3AED"),
            AccentDeep = Color.Parse("#6D28D9"),
            AccentSoft = Color.Parse("#A78BFA"),
            AccentGlow = Color.Parse("#337C3AED"),
            AccentHoverSoft = Color.Parse("#1A7C3AED"),
            AccentHoverStrong = Color.Parse("#337C3AED"),
            Text = Color.Parse("#1E1433"),
            TextMuted = Color.Parse("#6B6280"),
            Strike = Color.Parse("#9B92B0"),
            OnAccent = Color.Parse("#FFFFFF"),
        },
        new AppThemePalette
        {
            Id = "light-paper",
            DisplayName = "Light Paper",
            Category = ThemeCategory.Light,
            IsDark = false,
            Black = Color.Parse("#F7F4EF"),
            Surface = Color.Parse("#FFFCF7"),
            SurfaceRaised = Color.Parse("#EFE9E0"),
            Border = Color.Parse("#D2C4B0"),
            BorderSoft = Color.Parse("#E4D9C8"),
            Accent = Color.Parse("#0D9488"),
            AccentDeep = Color.Parse("#0F766E"),
            AccentSoft = Color.Parse("#5EEAD4"),
            AccentGlow = Color.Parse("#330D9488"),
            AccentHoverSoft = Color.Parse("#1A0D9488"),
            AccentHoverStrong = Color.Parse("#330D9488"),
            Text = Color.Parse("#1C1917"),
            TextMuted = Color.Parse("#78716C"),
            Strike = Color.Parse("#A8A29E"),
            OnAccent = Color.Parse("#FFFFFF"),
        },

        // —— Gradients ——
        new AppThemePalette
        {
            Id = "aurora",
            DisplayName = "Aurora",
            Category = ThemeCategory.Gradient,
            IsDark = true,
            Black = Color.Parse("#061018"),
            Surface = Color.Parse("#0C1A24"),
            SurfaceRaised = Color.Parse("#142830"),
            Border = Color.Parse("#2A5560"),
            BorderSoft = Color.Parse("#1C3A44"),
            Accent = Color.Parse("#34D399"),
            AccentDeep = Color.Parse("#8B5CF6"),
            AccentSoft = Color.Parse("#6EE7B7"),
            AccentGlow = Color.Parse("#3334D399"),
            AccentHoverSoft = Color.Parse("#1A34D399"),
            AccentHoverStrong = Color.Parse("#4434D399"),
            Text = Color.Parse("#ECFDF8"),
            TextMuted = Color.Parse("#8FB8B0"),
            Strike = Color.Parse("#6B9088"),
            OnAccent = Color.Parse("#041210"),
            WindowGradientEnd = Color.Parse("#1A0A2E"),
        },
        new AppThemePalette
        {
            Id = "sunset",
            DisplayName = "Sunset",
            Category = ThemeCategory.Gradient,
            IsDark = true,
            Black = Color.Parse("#14080C"),
            Surface = Color.Parse("#1C0E12"),
            SurfaceRaised = Color.Parse("#2A161C"),
            Border = Color.Parse("#7A3E3A"),
            BorderSoft = Color.Parse("#4A2428"),
            Accent = Color.Parse("#FB923C"),
            AccentDeep = Color.Parse("#EA580C"),
            AccentSoft = Color.Parse("#FDBA74"),
            AccentGlow = Color.Parse("#33FB923C"),
            AccentHoverSoft = Color.Parse("#1AFB923C"),
            AccentHoverStrong = Color.Parse("#44FB923C"),
            Text = Color.Parse("#FFF7ED"),
            TextMuted = Color.Parse("#C4A090"),
            Strike = Color.Parse("#9A7868"),
            OnAccent = Color.Parse("#1A0A08"),
            WindowGradientEnd = Color.Parse("#2A1030"),
        },
        new AppThemePalette
        {
            Id = "twilight",
            DisplayName = "Twilight",
            Category = ThemeCategory.Gradient,
            IsDark = true,
            Black = Color.Parse("#0A0A18"),
            Surface = Color.Parse("#12122A"),
            SurfaceRaised = Color.Parse("#1C1C3A"),
            Border = Color.Parse("#4A3A80"),
            BorderSoft = Color.Parse("#2E2858"),
            Accent = Color.Parse("#818CF8"),
            AccentDeep = Color.Parse("#6366F1"),
            AccentSoft = Color.Parse("#A5B4FC"),
            AccentGlow = Color.Parse("#33818CF8"),
            AccentHoverSoft = Color.Parse("#1A818CF8"),
            AccentHoverStrong = Color.Parse("#44818CF8"),
            Text = Color.Parse("#EEF0FF"),
            TextMuted = Color.Parse("#9AA0C8"),
            Strike = Color.Parse("#7878A0"),
            OnAccent = Color.Parse("#0C0C1C"),
            WindowGradientEnd = Color.Parse("#1A0828"),
        },
        new AppThemePalette
        {
            Id = "daybreak",
            DisplayName = "Daybreak",
            Category = ThemeCategory.Gradient,
            IsDark = false,
            Black = Color.Parse("#EEF4FF"),
            Surface = Color.Parse("#FFFFFF"),
            SurfaceRaised = Color.Parse("#E4ECFA"),
            Border = Color.Parse("#B8C8E8"),
            BorderSoft = Color.Parse("#D0DCF0"),
            Accent = Color.Parse("#DB2777"),
            AccentDeep = Color.Parse("#BE185D"),
            AccentSoft = Color.Parse("#F472B6"),
            AccentGlow = Color.Parse("#33DB2777"),
            AccentHoverSoft = Color.Parse("#1ADB2777"),
            AccentHoverStrong = Color.Parse("#33DB2777"),
            Text = Color.Parse("#1E1B2E"),
            TextMuted = Color.Parse("#6B6580"),
            Strike = Color.Parse("#9B95A8"),
            OnAccent = Color.Parse("#FFFFFF"),
            WindowGradientEnd = Color.Parse("#FFE4F0"),
        },
    ];

    public static AppThemePalette Get(string? themeId)
    {
        var id = (themeId ?? "").Trim().ToLowerInvariant();
        return All.FirstOrDefault(t => t.Id == id) ?? All[0];
    }

    public static bool IsKnown(string? themeId) =>
        All.Any(t => t.Id.Equals(themeId?.Trim(), StringComparison.OrdinalIgnoreCase));
}
