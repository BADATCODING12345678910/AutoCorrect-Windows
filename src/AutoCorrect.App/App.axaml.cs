using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AutoCorrect.App.Themes;
using AutoCorrect.App.ViewModels;
using AutoCorrect.App.Views;

namespace AutoCorrect.App;

public partial class App : Application
{
    private AppViewModel? _vm;
    private ReviewCardWindow? _review;
    private UnderlineOverlayWindow? _underline;
    private ToastWindow? _toast;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Default chrome before settings load (AppViewModel applies the saved theme next).
        ThemeService.Apply(AppThemeCatalog.DefaultThemeId);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Stay alive in the tray — no big permanent main window.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _vm = new AppViewModel();
            DataContext = _vm;
            // Tray exists now — refresh themed logo (theme was also applied during Initialize).
            ThemeService.Apply(_vm.SettingsStore.Current.General.ThemeId);

            _review = new ReviewCardWindow { DataContext = _vm };
            _vm.AttachReviewWindow(_review);

            _underline = new UnderlineOverlayWindow();
            _vm.AttachUnderlineOverlay(_underline);

            _toast = new ToastWindow();
            _vm.ToastRequested += msg =>
                Dispatcher.UIThread.Post(() => _toast?.ShowMessage(msg));
            _vm.ToastActionRequested += (msg, ms, label, action) =>
                Dispatcher.UIThread.Post(() => _toast?.ShowMessage(msg, ms, label, action));
            _vm.UpdateProgressRequested += (msg, fraction, detail) =>
                Dispatcher.UIThread.Post(() => _toast?.UpdateProgress(msg, fraction, detail));
            _vm.UpdateProgressFinished += () =>
                Dispatcher.UIThread.Post(() => _toast?.HideProgress());

            // Confirm tray launch + real hotkey registration result.
            Dispatcher.UIThread.Post(() =>
            {
                if (_vm is null || !_vm.ShowStartupToast)
                    return;
                var hotkeyStatus = _vm.ReportHotkeyStatus();
                var licenseLine = _vm.License.Refresh().StatusLine;
                _toast?.ShowMessage(
                    "AutoCorrect is running in the system tray.\n" +
                    "Right-click the tray icon → Settings… / Fix / License.\n" +
                    hotkeyStatus + "\n" + licenseLine +
                    "\nTip: type teh + space to test autocorrect.",
                    7000);
            });

            desktop.Exit += (_, _) =>
            {
                _vm?.Dispose();
                _vm = null;
                if (_review is not null)
                {
                    _review.AllowClose = true;
                    try { _review.Close(); } catch { /* ignore */ }
                }
                if (_underline is not null)
                {
                    _underline.AllowClose = true;
                    try { _underline.Close(); } catch { /* ignore */ }
                }
                try { _toast?.Close(); } catch { /* ignore */ }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
