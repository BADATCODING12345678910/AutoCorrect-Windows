using AutoCorrect.App.Themes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public partial class ThemeOptionViewModel : ObservableObject
{
    private readonly Action<string> _onSelect;

    public ThemeOptionViewModel(AppThemePalette palette, Action<string> onSelect, bool selected)
    {
        _onSelect = onSelect;
        Id = palette.Id;
        DisplayName = palette.DisplayName;
        Category = palette.CategoryLabel;
        IsDark = palette.IsDark;
        PreviewBrush = ThemeService.CreatePreviewBrush(palette);
        AccentBrush = new SolidColorBrush(palette.Accent);
        LabelBrush = new SolidColorBrush(palette.IsDark ? Colors.White : Color.Parse("#1A1A1A"));
        IsSelected = selected;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool IsDark { get; }
    public IBrush PreviewBrush { get; }
    public IBrush AccentBrush { get; }
    public IBrush LabelBrush { get; }

    [ObservableProperty] private bool _isSelected;

    public IBrush BorderBrush => IsSelected
        ? AccentBrush
        : new SolidColorBrush(Color.Parse(IsDark ? "#66FFFFFF" : "#33000000"));

    [RelayCommand]
    private void Select() => _onSelect(Id);

    public void RefreshSelection(string? selectedId)
    {
        var next = string.Equals(Id, selectedId, StringComparison.OrdinalIgnoreCase);
        if (next == IsSelected)
            return;
        IsSelected = next;
        OnPropertyChanged(nameof(BorderBrush));
    }
}
