using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AutoCorrect.App.Themes;

/// <summary>Pushes a themed logo into the tray icon, open windows, and an optional preview resource.</summary>
public static class ThemeLogoApplier
{
    public const string PreviewResourceKey = "AcThemedLogo";

    private static WindowIcon? _windowIcon;
    private static MemoryStream? _windowIconStream;
    private static Bitmap? _previewBitmap;
    private static Bitmap? _pendingDispose;
    private static int _desktopJob;
    private static bool _privacyBadge;
    private static AppThemePalette? _lastTheme;

    public static WindowIcon? CurrentIcon => _windowIcon;
    public static Bitmap? CurrentPreview => _previewBitmap;
    public static bool PrivacyBadge => _privacyBadge;

    public static event Action? PreviewChanged;

    public static void Apply(AppThemePalette theme)
    {
        _lastTheme = theme;
        if (Dispatcher.UIThread.CheckAccess())
            ApplyCore(theme);
        else
            Dispatcher.UIThread.Post(() => ApplyCore(theme));
    }

    /// <summary>Teal lock badge on tray/window icons while Privacy mode is active.</summary>
    public static void SetPrivacyBadge(bool on)
    {
        if (_privacyBadge == on)
            return;
        _privacyBadge = on;
        var theme = _lastTheme ?? AppThemeCatalog.Get(ThemeService.CurrentThemeId);
        Apply(theme);
    }

    public static void ApplyToWindow(Window window)
    {
        if (_windowIcon is not null)
            window.Icon = _windowIcon;
    }

    private static void ApplyCore(AppThemePalette theme)
    {
        var app = Application.Current;
        if (app is null)
            return;

        try
        {
            // Keep the stream alive — Avalonia WindowIcon may read lazily.
            var iconStream = ThemedLogoFactory.CreatePngStream(theme, maxEdge: 256, _privacyBadge);
            var newIcon = new WindowIcon(iconStream);
            var oldIconStream = _windowIconStream;
            _windowIconStream = iconStream;
            _windowIcon = newIcon;

            var previewStream = ThemedLogoFactory.CreatePngStream(theme, maxEdge: 128, privacyBadge: false);
            var preview = new Bitmap(previewStream);
            previewStream.Dispose();

            var oldPreview = _previewBitmap;
            _previewBitmap = preview;
            app.Resources[PreviewResourceKey] = preview;

            // Notify bindings BEFORE disposing the previous bitmap (Settings Image crash).
            try { PreviewChanged?.Invoke(); } catch { /* ignore */ }

            var trayIcons = TrayIcon.GetIcons(app);
            if (trayIcons is { Count: > 0 })
            {
                foreach (var tray in trayIcons)
                    tray.Icon = _windowIcon;
            }

            if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                    window.Icon = _windowIcon;
            }

            // Defer dispose until after the next layout pass.
            if (oldPreview is not null)
            {
                _pendingDispose = oldPreview;
                Dispatcher.UIThread.Post(() =>
                {
                    try { _pendingDispose?.Dispose(); } catch { /* ignore */ }
                    _pendingDispose = null;
                    try { oldIconStream?.Dispose(); } catch { /* ignore */ }
                }, DispatcherPriority.Background);
            }
            else
            {
                try { oldIconStream?.Dispose(); } catch { /* ignore */ }
            }

            // Heavy GDI + COM off the UI thread so theme clicks stay responsive / crash-free.
            var job = Interlocked.Increment(ref _desktopJob);
            var themeCopy = theme;
            _ = Task.Run(() =>
            {
                if (job != Volatile.Read(ref _desktopJob))
                    return;
                try { DesktopShortcutIconUpdater.Apply(themeCopy); }
                catch { /* cosmetic */ }
            });
        }
        catch
        {
            // Logo theming is cosmetic — never break theme apply.
        }
    }
}
