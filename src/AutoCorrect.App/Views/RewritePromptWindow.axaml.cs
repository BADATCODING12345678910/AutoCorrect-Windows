using Avalonia.Controls;
using Avalonia.Input;

namespace AutoCorrect.App.Views;

public partial class RewritePromptWindow : Window
{
    public RewritePromptWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
