using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AutoCorrect.App.ViewModels;

namespace AutoCorrect.App.Views;

public partial class ReviewCardWindow : Window
{
    public bool AllowClose { get; set; }

    private bool _dragging;
    private PixelPoint _dragCursorStart;
    private PixelPoint _dragWindowStart;
    private const double BaseWidth = 300;
    private const double BaseMinWidth = 280;
    private const double BaseMaxWidth = 340;

    public ReviewCardWindow()
    {
        InitializeComponent();
        PointerEntered += (_, _) =>
        {
            if (DataContext is AppViewModel vm)
                vm.NotifyReviewPointerEntered();
        };
        Closing += (_, e) =>
        {
            if (AllowClose)
                return;
            e.Cancel = true;
            if (DataContext is AppViewModel vm)
                vm.CloseQueueCommand.Execute(null);
            else
                Hide();
        };
    }

    /// <summary>Scale the Fix card for accessibility (1.0 / 1.15 / 1.3).</summary>
    public void ApplyAccessibilityScale(double scale)
    {
        if (scale is < 1.0 or > 1.5)
            scale = 1.0;

        // Keep logical width at base; ScaleTransform enlarges glyphs + hit targets.
        Width = BaseWidth;
        MinWidth = BaseMinWidth;
        MaxWidth = BaseMaxWidth;
        RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        RenderTransform = scale is > 0.99 and < 1.01
            ? null
            : new ScaleTransform(scale, scale);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AppViewModel vm)
            return;

        if (e.Key == Key.Escape)
        {
            vm.CloseQueueCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left || e.Key == Key.Up)
        {
            vm.PrevSuggestionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right || e.Key == Key.Down)
        {
            vm.NextSuggestionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (vm.HasMultiple)
                vm.ApplyAllSafeCommand.Execute(null);
            else
                vm.ApplyCurrentCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Space)
        {
            vm.ApplyCurrentCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            vm.NeverSuggestCurrentCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Use screen pixels — DIP-relative GetPosition is sluggish/wrong under DPI scaling.
        _dragging = true;
        _dragCursorStart = GetCursorPixel();
        _dragWindowStart = Position;
        e.Pointer.Capture(DragBar);
        e.Handled = true;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        var cursor = GetCursorPixel();
        var next = new PixelPoint(
            _dragWindowStart.X + (cursor.X - _dragCursorStart.X),
            _dragWindowStart.Y + (cursor.Y - _dragCursorStart.Y));
        Position = ClampToWorkingArea(next);
        e.Handled = true;
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnDragPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging = false;
    }

    public void PlaceNear(PixelPoint cursor)
    {
        UpdateLayout();
        var w = (int)(Bounds.Width > 1 ? Bounds.Width : Width);
        var h = (int)(Bounds.Height > 1 ? Bounds.Height : 160);
        Position = ClampToWorkingArea(new PixelPoint(cursor.X + 12, cursor.Y + 16), w, h);
    }

    /// <summary>
    /// Grammarly-style: park the card just under the mistake's screen rect
    /// (or above if it would clip the working area).
    /// </summary>
    public void PlaceUnderMistake(int screenX, int screenY, int pixelWidth, int pixelHeight)
    {
        UpdateLayout();
        var w = (int)(Bounds.Width > 1 ? Bounds.Width : Width);
        var h = (int)(Bounds.Height > 1 ? Bounds.Height : 160);
        if (w < 1) w = 320;
        if (h < 1) h = 160;

        const int gap = 8;
        var origin = new PixelPoint(screenX, screenY);
        var screen = Screens.ScreenFromPoint(origin) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        // Prefer below the glyph box; flip above when not enough room.
        var belowY = screenY + Math.Max(1, pixelHeight) + gap;
        var aboveY = screenY - h - gap;
        var desiredY = belowY + h <= area.Y + area.Height - gap
            ? belowY
            : Math.Max(area.Y + gap, aboveY);

        // Align to mistake start; nudge slightly so the card doesn't sit dead-center on the word.
        var desiredX = screenX;
        Position = ClampToWorkingArea(new PixelPoint(desiredX, desiredY), w, h);
    }

    private PixelPoint ClampToWorkingArea(PixelPoint desired, int? width = null, int? height = null)
    {
        var w = width ?? (int)(Bounds.Width > 1 ? Bounds.Width : Width);
        var h = height ?? (int)(Bounds.Height > 1 ? Bounds.Height : 160);
        var screen = Screens.ScreenFromPoint(desired) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        const int pad = 8;
        var x = Math.Min(Math.Max(desired.X, area.X + pad), area.X + area.Width - w - pad);
        var y = Math.Min(Math.Max(desired.Y, area.Y + pad), area.Y + area.Height - h - pad);
        return new PixelPoint(x, y);
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
}
