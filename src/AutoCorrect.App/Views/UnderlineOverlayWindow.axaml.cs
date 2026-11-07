using Avalonia;
using Avalonia.Controls;

namespace AutoCorrect.App.Views;

public partial class UnderlineOverlayWindow : Window
{
    public bool AllowClose { get; set; }

    public UnderlineOverlayWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    /// <param name="screenX">Physical screen pixels (from UIA).</param>
    /// <param name="screenY">Physical screen pixels — top of the text glyph box.</param>
    /// <param name="pixelWidth">Physical width of the span.</param>
    /// <param name="pixelHeight">Physical height of the span.</param>
    public void ShowAt(int screenX, int screenY, int pixelWidth, int pixelHeight)
    {
        // Avalonia Position = physical pixels; Width/Height = DIPs — convert via screen scaling.
        var origin = new PixelPoint(screenX, screenY);
        var screen = Screens.ScreenFromPoint(origin) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1.0;
        if (scale <= 0)
            scale = 1.0;

        Width = Math.Max(10, pixelWidth / scale);
        Height = Math.Max(12, pixelHeight / scale);
        Position = new PixelPoint(screenX, screenY);

        if (!IsVisible)
            Show();
        else
            InvalidateVisual();
    }

    public void HideOverlay()
    {
        if (IsVisible)
            Hide();
    }
}
