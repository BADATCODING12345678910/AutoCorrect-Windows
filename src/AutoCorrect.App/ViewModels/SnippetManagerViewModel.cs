using System.Collections.ObjectModel;
using AutoCorrect.Core;
using AutoCorrect.Core.Snippets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public partial class SnippetRowViewModel : ObservableObject
{
    [ObservableProperty] private string _trigger = ";";
    [ObservableProperty] private string _expansion = "";
    [ObservableProperty] private bool _enabled = true;
}

public partial class SnippetManagerViewModel : ViewModelBase
{
    private readonly string _path;
    private readonly Action? _onSaved;

    public SnippetManagerViewModel(string? path = null, Action? onSaved = null)
    {
        _path = path
                ?? DataPaths.FindSnippets()
                ?? DataPaths.FindOrCreateSnippetsPath();
        _onSaved = onSaved;
        PathDisplay = _path;
        Reload();
    }

    public ObservableCollection<SnippetRowViewModel> Rows { get; } = [];

    [ObservableProperty] private SnippetRowViewModel? _selected;
    [ObservableProperty] private string _pathDisplay = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _filterText = "";

    partial void OnFilterTextChanged(string value) => ApplyFilterHighlight();

    public void Reload()
    {
        Rows.Clear();
        foreach (var e in SnippetStore.LoadEntries(_path))
        {
            Rows.Add(new SnippetRowViewModel
            {
                Trigger = e.Trigger,
                Expansion = e.Expansion,
                Enabled = e.Enabled
            });
        }

        StatusMessage = Rows.Count == 0
            ? "No snippets yet — Add one, then Save."
            : $"{Rows.Count} snippet(s). Type ;trigger then Space/Tab/Enter.";
        Selected = Rows.Count > 0 ? Rows[0] : null;
    }

    private void ApplyFilterHighlight()
    {
        // Filter is visual-only via Status; list stays editable. Keep simple for v1.
        if (string.IsNullOrWhiteSpace(FilterText))
            return;
        var q = FilterText.Trim();
        var hit = Rows.FirstOrDefault(r =>
            r.Trigger.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            r.Expansion.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
            Selected = hit;
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new SnippetRowViewModel { Trigger = ";new", Expansion = "", Enabled = true };
        Rows.Add(row);
        Selected = row;
        StatusMessage = "Fill trigger + expansion, then Save.";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Selected is null)
        {
            StatusMessage = "Select a snippet to remove.";
            return;
        }

        var idx = Rows.IndexOf(Selected);
        Rows.Remove(Selected);
        Selected = Rows.Count == 0
            ? null
            : Rows[Math.Clamp(idx, 0, Rows.Count - 1)];
        StatusMessage = "Removed from list — click Save to write Snippets.json.";
    }

    [RelayCommand]
    private void Save()
    {
        var entries = Rows.Select(r => new SnippetEntry
        {
            Trigger = r.Trigger,
            Expansion = r.Expansion,
            Enabled = r.Enabled
        });

        var store = new SnippetStore();
        if (!store.SaveEntries(_path, entries, out var error))
        {
            StatusMessage = error;
            return;
        }

        // Normalize triggers in the UI after save.
        Reload();
        StatusMessage = $"Saved {store.Count} enabled snippet(s) to Snippets.json.";
        _onSaved?.Invoke();
    }

    [RelayCommand]
    private void ReloadFromDisk() => Reload();
}
