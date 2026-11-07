using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AutoCorrect.App.Themes;

/// <summary>
/// Applies named palettes by mutating shared brushes (StaticResource still updates)
/// and swapping <c>AcWindowBackgroundBrush</c> for solid vs gradient chrome.
/// </summary>
public static class ThemeService
{
    public static string CurrentThemeId { get; private set; } = AppThemeCatalog.DefaultThemeId;

    public static void Apply(string? themeId)
    {
        var theme = AppThemeCatalog.Get(themeId);
        CurrentThemeId = theme.Id;

        var app = Application.Current;
        if (app is null)
            return;

        app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        SetColorResource(app, "AcBlack", theme.Black);
        SetColorResource(app, "AcSurface", theme.Surface);
        SetColorResource(app, "AcSurfaceRaised", theme.SurfaceRaised);
        SetColorResource(app, "AcBorder", theme.Border);
        SetColorResource(app, "AcBorderSoft", theme.BorderSoft);
        SetColorResource(app, "AcPurple", theme.Accent);
        SetColorResource(app, "AcPurpleDeep", theme.AccentDeep);
        SetColorResource(app, "AcPurpleSoft", theme.AccentSoft);
        SetColorResource(app, "AcPurpleGlow", theme.AccentGlow);
        SetColorResource(app, "AcText", theme.Text);
        SetColorResource(app, "AcTextMuted", theme.TextMuted);
        SetColorResource(app, "AcStrike", theme.Strike);

        MutateOrSetBrush(app, "AcBlackBrush", theme.Black);
        MutateOrSetBrush(app, "AcSurfaceBrush", theme.Surface);
        MutateOrSetBrush(app, "AcSurfaceRaisedBrush", theme.SurfaceRaised);
        MutateOrSetBrush(app, "AcBorderBrush", theme.Border);
        MutateOrSetBrush(app, "AcBorderSoftBrush", theme.BorderSoft);
        MutateOrSetBrush(app, "AcPurpleBrush", theme.Accent);
        MutateOrSetBrush(app, "AcPurpleDeepBrush", theme.AccentDeep);
        MutateOrSetBrush(app, "AcPurpleSoftBrush", theme.AccentSoft);
        MutateOrSetBrush(app, "AcPurpleGlowBrush", theme.AccentGlow);
        MutateOrSetBrush(app, "AcAccentHoverSoftBrush", theme.AccentHoverSoft);
        MutateOrSetBrush(app, "AcAccentHoverStrongBrush", theme.AccentHoverStrong);
        MutateOrSetBrush(app, "AcTextBrush", theme.Text);
        MutateOrSetBrush(app, "AcTextMutedBrush", theme.TextMuted);
        MutateOrSetBrush(app, "AcStrikeBrush", theme.Strike);
        MutateOrSetBrush(app, "AcOnAccentBrush", theme.OnAccent);

        IBrush windowBg = theme.WindowGradientEnd is { } end
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(theme.Black, 0),
                    new GradientStop(end, 1),
                ]
            }
            : new SolidColorBrush(theme.Black);

        // App-level override so DynamicResource picks gradient vs solid.
        app.Resources["AcWindowBackgroundBrush"] = windowBg;

        ThemeLogoApplier.Apply(theme);
    }

    public static IBrush CreatePreviewBrush(AppThemePalette theme)
    {
        if (theme.WindowGradientEnd is { } end)
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(theme.Black, 0),
                    new GradientStop(theme.Accent, 0.55),
                    new GradientStop(end, 1),
                ]
            };
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(theme.Black, 0),
                new GradientStop(theme.SurfaceRaised, 0.55),
                new GradientStop(theme.Accent, 1),
            ]
        };
    }

    private static void SetColorResource(Application app, string key, Color color)
    {
        app.Resources[key] = color;
    }

    private static void MutateOrSetBrush(Application app, string key, Color color)
    {
        if (app.TryGetResource(key, app.ActualThemeVariant, out var existing) &&
            existing is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        app.Resources[key] = new SolidColorBrush(color);
    }
}
