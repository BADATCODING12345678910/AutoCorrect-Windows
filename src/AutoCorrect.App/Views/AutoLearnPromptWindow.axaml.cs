using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AutoCorrect.Core.Autocorrect;

namespace AutoCorrect.App.Views;

public partial class AutoLearnPromptWindow : Window
{
    public enum Choice
    {
        Accept,
        Skip,
        Never,
        Dismiss
    }

    private readonly TaskCompletionSource<Choice> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _timeout;

    public AutoLearnPromptWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _timeout?.Cancel();
            _tcs.TrySetResult(Choice.Dismiss);
        };
    }

    public void ShowPromotion(PendingPromotion promo, int timeoutSec = 12)
    {
        PairText.Text = $"{promo.Trigger}  →  {promo.Replacement}";
        DetailText.Text = $"Seen {promo.Occurrences}×. Accept adds it to as-you-type autocorrect.";
        PositionNearTray();
        if (!IsVisible)
            Show();
        Activate();

        _timeout?.Cancel();
        _timeout = new CancellationTokenSource();
        var token = _timeout.Token;
        _ = DismissAfterAsync(Math.Clamp(timeoutSec, 5, 60) * 1000, token);
    }

    public Task<Choice> WaitAsync() => _tcs.Task;

    private async Task DismissAfterAsync(int ms, CancellationToken token)
    {
        try
        {
            await Task.Delay(ms, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                _tcs.TrySetResult(Choice.Skip);
                Hide();
            }
        }
        catch (TaskCanceledException)
        {
            // replaced / closed
        }
    }

    private void PositionNearTray()
    {
        UpdateLayout();
        const int pad = 16;
        var screen = Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;
        var wDip = Bounds.Width > 1 ? Bounds.Width : Width;
        var hDip = Bounds.Height > 1 ? Bounds.Height : 200;
        var w = (int)Math.Ceiling(wDip * scale);
        var h = (int)Math.Ceiling(hDip * scale);
        var x = Math.Max(area.X + pad, area.X + area.Width - w - pad);
        var y = Math.Max(area.Y + pad, area.Y + area.Height - h - pad);
        Position = new PixelPoint(x, y);
    }

    private void Finish(Choice choice)
    {
        _timeout?.Cancel();
        _tcs.TrySetResult(choice);
        Hide();
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Finish(Choice.Accept);
    private void OnSkip(object? sender, RoutedEventArgs e) => Finish(Choice.Skip);
    private void OnNever(object? sender, RoutedEventArgs e) => Finish(Choice.Never);
}
