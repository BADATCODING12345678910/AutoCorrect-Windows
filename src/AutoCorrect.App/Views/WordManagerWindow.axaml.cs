using Avalonia.Controls;
using AutoCorrect.App.ViewModels;

namespace AutoCorrect.App.Views;

public partial class WordManagerWindow : Window
{
    public WordManagerWindow()
    {
        InitializeComponent();
    }

    private void OnActiveSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WordManagerViewModel vm || sender is not ListBox list)
            return;
        vm.SetActiveSelection(list.SelectedItems?.OfType<WordPairRow>() ?? []);
    }

    private void OnRemovedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WordManagerViewModel vm || sender is not ListBox list)
            return;
        vm.SetRemovedSelection(list.SelectedItems?.OfType<WordPairRow>() ?? []);
    }
}
