using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public partial class WhatsNewViewModel : ViewModelBase
{
    public WhatsNewViewModel(string version, string notes, string? previousVersion = null)
    {
        VersionLabel = string.IsNullOrWhiteSpace(version) ? "AutoCorrect" : version.Trim();
        Notes = string.IsNullOrWhiteSpace(notes)
            ? "No release notes saved yet. After you install an in-app update, notes appear here."
            : notes.Trim();
        Subtitle = string.IsNullOrWhiteSpace(previousVersion)
            ? "Recent in-app update"
            : $"Updated from {previousVersion.Trim()}";
    }

    [ObservableProperty] private string _versionLabel = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _notes = "";

    public event Action? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
