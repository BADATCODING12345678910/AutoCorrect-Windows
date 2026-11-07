using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AutoCorrect.App.Views;

public partial class ToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private CancellationTokenSource? _cts;
    private Action? _action;
    private bool _progressMode;

    public ToastWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        ActionButton.Click += (_, _) =>
        {
            var act = _action;
            _action = null;
            Hide();
            act?.Invoke();
        };
        Opened += (_, _) => ApplyNoActivateStyle();
    }

    public void ShowMessage(string message, int ms = 2200) =>
        ShowMessage(message, ms, actionLabel: null, action: null);

    public void ShowMessage(string message, int ms, string? actionLabel, Action? action)
    {
        _progressMode = false;
        ProgressMeter.IsVisible = false;
        DetailText.IsVisible = false;
        DetailText.Text = "";

        MessageText.Text = message;
        _action = action;
        var hasAction = !string.IsNullOrWhiteSpace(actionLabel) && action is not null;
        ActionButton.Content = actionLabel ?? "Undo";
        ActionButton.IsVisible = hasAction;
        IsHitTestVisible = hasAction;

        if (!IsVisible)
            Show();
        ApplyNoActivateStyle();
        Relayout();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = DismissAfterAsync(ms, token);
    }

    /// <summary>Sticky toast with a progress bar; stays up until <see cref="HideProgress"/> or a normal toast.</summary>
    public void ShowProgress(string message, double? fraction = null, string? detail = null)
    {
        _cts?.Cancel();
        _cts = null;
        _progressMode = true;
        _action = null;
        ActionButton.IsVisible = false;
        IsHitTestVisible = false;

        MessageText.Text = message;
        ApplyProgressVisual(fraction, detail);

        if (!IsVisible)
            Show();
        ApplyNoActivateStyle();
        Relayout();
    }

    public void UpdateProgress(string message, double? fraction = null, string? detail = null)
    {
        if (!_progressMode)
        {
            ShowProgress(message, fraction, detail);
            return;
        }

        MessageText.Text = message;
        ApplyProgressVisual(fraction, detail);
        Relayout();
    }

    public void HideProgress()
    {
        if (!_progressMode)
            return;
        _progressMode = false;
        ProgressMeter.IsVisible = false;
        DetailText.IsVisible = false;
        DetailText.Text = "";
        Hide();
    }

    private void ApplyProgressVisual(double? fraction, string? detail)
    {
        ProgressMeter.IsVisible = true;
        if (fraction is null)
        {
            ProgressMeter.IsIndeterminate = true;
        }
        else
        {
            ProgressMeter.IsIndeterminate = false;
            ProgressMeter.Value = Math.Clamp(fraction.Value, 0, 1);
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            DetailText.IsVisible = false;
            DetailText.Text = "";
        }
        else
        {
            DetailText.Text = detail;
            DetailText.IsVisible = true;
        }
    }

    private void Relayout()
    {
        UpdateLayout();
        Position = PlaceBottomRight();
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            Position = PlaceBottomRight();
            ApplyNoActivateStyle();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Keep typing focus in the host app — Undo toast must not steal keyboard focus.</summary>
    private void ApplyNoActivateStyle()
    {
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
                return;
            var style = GetWindowLongPtr(handle, GwlExStyle);
            var next = style | (IntPtr)(WsExNoActivate | WsExToolWindow);
            if (next != style)
                SetWindowLongPtr(handle, GwlExStyle, next);
        }
        catch
        {
            // platform handle may be unavailable briefly
        }
    }

    private PixelPoint PlaceBottomRight()
    {
        const int pad = 16;
        var cursor = GetCursorPixel();
        var screen = Screens.ScreenFromPoint(cursor) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var scale = screen?.Scaling ?? 1.0;

        var wDip = Bounds.Width > 1 ? Bounds.Width : (double.IsNaN(Width) ? 280 : Width);
        var hDip = Bounds.Height > 1 ? Bounds.Height : 80;
        var w = (int)Math.Ceiling(wDip * scale);
        var h = (int)Math.Ceiling(hDip * scale);

        var x = area.X + area.Width - w - pad;
        var y = area.Y + area.Height - h - pad;
        x = Math.Max(area.X + pad, x);
        y = Math.Max(area.Y + pad, y);
        return new PixelPoint(x, y);
    }

    private async Task DismissAfterAsync(int ms, CancellationToken token)
    {
        try
        {
            await Task.Delay(ms, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                _action = null;
                ActionButton.IsVisible = false;
                IsHitTestVisible = false;
                Hide();
            }
        }
        catch (TaskCanceledException)
        {
            // newer toast replaced this one
        }
    }

    private static PixelPoint GetCursorPixel()
    {
        GetCursorPos(out POINT p);
        return new PixelPoint(p.X, p.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
