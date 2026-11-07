using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using AutoCorrect.Core.Autocorrect;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public sealed class WordPairRow
{
    public required string Trigger { get; init; }
    public required string Replacement { get; init; }
    public required string Source { get; init; }
    public string Display => $"{Trigger}  →  {Replacement}";
    public string Meta => Source;
}

public partial class WordManagerViewModel : ViewModelBase
{
    private readonly AutocorrectEngine _engine;
    private readonly AutoLearnService? _autoLearn;
    private readonly LearnContributionStore? _contribution;
    private readonly Window? _host;
    private readonly Action? _onChanged;
    private readonly List<WordPairRow> _activeAll = [];
    private readonly List<WordPairRow> _removedAll = [];

    public WordManagerViewModel(
        AutocorrectEngine engine,
        AutoLearnService? autoLearn = null,
        Action? onChanged = null,
        LearnContributionStore? contribution = null,
        Window? host = null)
    {
        _engine = engine;
        _autoLearn = autoLearn;
        _contribution = contribution;
        _host = host;
        _onChanged = onChanged;
        Refresh();
    }

    public ObservableCollection<WordPairRow> ActivePairs { get; } = [];
    public ObservableCollection<WordPairRow> RemovedPairs { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _newTrigger = "";
    [ObservableProperty] private string _newReplacement = "";
    [ObservableProperty] private WordPairRow? _selectedActive;
    [ObservableProperty] private WordPairRow? _selectedRemoved;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _selectedTab; // 0 active, 1 removed
    [ObservableProperty] private int _activeSelectionCount;
    [ObservableProperty] private int _removedSelectionCount;

    private readonly List<WordPairRow> _activeSelected = [];
    private readonly List<WordPairRow> _removedSelected = [];

    public string RemoveButtonLabel =>
        ActiveSelectionCount > 1 ? $"Never {ActiveSelectionCount} selected" : "Never selected";

    public string RestoreButtonLabel =>
        RemovedSelectionCount > 1 ? $"Restore {RemovedSelectionCount} selected" : "Restore selected";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnActiveSelectionCountChanged(int value) => OnPropertyChanged(nameof(RemoveButtonLabel));
    partial void OnRemovedSelectionCountChanged(int value) => OnPropertyChanged(nameof(RestoreButtonLabel));

    public void SetActiveSelection(IEnumerable<WordPairRow> rows)
    {
        _activeSelected.Clear();
        _activeSelected.AddRange(rows);
        ActiveSelectionCount = _activeSelected.Count;
        SelectedActive = _activeSelected.Count > 0 ? _activeSelected[^1] : null;
    }

    public void SetRemovedSelection(IEnumerable<WordPairRow> rows)
    {
        _removedSelected.Clear();
        _removedSelected.AddRange(rows);
        RemovedSelectionCount = _removedSelected.Count;
        SelectedRemoved = _removedSelected.Count > 0 ? _removedSelected[^1] : null;
    }

    [RelayCommand]
    private void AddPair()
    {
        if (!_engine.AddCustomPair(NewTrigger, NewReplacement, out var error))
        {
            StatusMessage = error;
            return;
        }

        var t = NewTrigger.Trim().ToLowerInvariant();
        var r = NewReplacement.Trim().ToLowerInvariant();
        try { _contribution?.RecordAccepted(t, r); } catch { /* optional */ }
        StatusMessage = $"Added “{t} → {r}” (custom). It will autocorrect as you type.";
        NewTrigger = "";
        NewReplacement = "";
        SelectedTab = 0;
        SearchText = t;
        Refresh();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private async Task ImportListAsync()
    {
        if (_host is null)
        {
            StatusMessage = "Import needs the Word Manager window.";
            return;
        }

        var files = await _host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import word list",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Word lists")
                {
                    Patterns = ["*.json", "*.txt", "*.tsv", "*.csv"]
                },
                FilePickerFileTypes.All
            ]
        }).ConfigureAwait(true);

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Couldn't read that file path.";
            return;
        }

        try
        {
            var result = WordListImporter.ImportInto(_engine, path, _contribution);
            StatusMessage = result.Summary +
                " Shared-list overlaps are tallied locally for future curated updates.";
            SelectedTab = 0;
            Refresh();
            _onChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = "Import failed: " + ex.Message;
        }
    }

    public void Refresh()
    {
        _activeAll.Clear();
        foreach (var kv in _engine.Curated.Snapshot())
        {
            if (_engine.Removed.Contains(kv.Key))
                continue;
            _activeAll.Add(new WordPairRow
            {
                Trigger = kv.Key,
                Replacement = kv.Value,
                Source = "curated"
            });
        }

        foreach (var (trig, repl) in _engine.Learned.Snapshot())
        {
            if (_engine.Removed.Contains(trig))
                continue;
            _activeAll.Add(new WordPairRow
            {
                Trigger = trig,
                Replacement = repl,
                Source = "custom/learned"
            });
        }

        if (_engine.Options.UseImportedLibrary)
        {
            foreach (var (trig, repl) in _engine.Imported.SnapshotExact())
            {
                if (_engine.Removed.Contains(trig))
                    continue;
                // Skip if already listed from curated/learned
                if (_activeAll.Any(p => p.Trigger.Equals(trig, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _activeAll.Add(new WordPairRow
                {
                    Trigger = trig,
                    Replacement = repl,
                    Source = "imported"
                });
            }
        }

        _removedAll.Clear();
        foreach (var r in _engine.Removed.Snapshot())
        {
            _removedAll.Add(new WordPairRow
            {
                Trigger = r.Trigger,
                Replacement = string.IsNullOrEmpty(r.Replacement) ? "(blocked)" : r.Replacement,
                Source = $"removed · {r.Source} · {r.RemovedAt}"
            });
        }

        if (_autoLearn is not null)
        {
            foreach (var key in _autoLearn.SnapshotNeverKeys())
            {
                string trigger;
                string replacement;
                var tab = key.IndexOf('\t');
                if (tab >= 0)
                {
                    trigger = key[..tab];
                    replacement = key[(tab + 1)..];
                }
                else
                {
                    trigger = key;
                    replacement = "(any)";
                }

                if (_removedAll.Any(p => p.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _removedAll.Add(new WordPairRow
                {
                    Trigger = trigger,
                    Replacement = replacement,
                    Source = "auto-learn never"
                });
            }
        }

        ApplyFilter();
        StatusMessage =
            $"{_activeAll.Count} active · {_removedAll.Count} never/removed · " +
            "search both lists (try learned: or never:) · multi-select for bulk Never";
    }

    private void ApplyFilter()
    {
        var raw = (SearchText ?? "").Trim();
        var mode = FilterMode.All;
        var q = raw;
        if (raw.StartsWith("learned:", StringComparison.OrdinalIgnoreCase))
        {
            mode = FilterMode.Learned;
            q = raw["learned:".Length..].Trim();
        }
        else if (raw.StartsWith("never:", StringComparison.OrdinalIgnoreCase) ||
                 raw.StartsWith("removed:", StringComparison.OrdinalIgnoreCase))
        {
            mode = FilterMode.Never;
            var colon = raw.IndexOf(':');
            q = colon >= 0 ? raw[(colon + 1)..].Trim() : "";
        }

        ActivePairs.Clear();
        RemovedPairs.Clear();

        if (mode != FilterMode.Never)
        {
            foreach (var row in _activeAll
                         .Where(r => mode != FilterMode.Learned ||
                                     r.Source.Contains("learned", StringComparison.OrdinalIgnoreCase) ||
                                     r.Source.Contains("custom", StringComparison.OrdinalIgnoreCase))
                         .Where(r => Matches(r, q))
                         .OrderBy(r => r.Trigger, StringComparer.OrdinalIgnoreCase))
                ActivePairs.Add(row);
        }

        if (mode != FilterMode.Learned)
        {
            foreach (var row in _removedAll
                         .Where(r => Matches(r, q))
                         .OrderBy(r => r.Trigger, StringComparer.OrdinalIgnoreCase))
                RemovedPairs.Add(row);
        }

        if (mode == FilterMode.Never)
            SelectedTab = 1;
        else if (mode == FilterMode.Learned)
            SelectedTab = 0;
    }

    private enum FilterMode
    {
        All,
        Learned,
        Never
    }

    private static bool Matches(WordPairRow row, string q)
    {
        if (q.Length == 0)
            return true;
        return row.Trigger.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               row.Replacement.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               row.Source.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        var rows = _activeSelected.Count > 0
            ? _activeSelected.ToList()
            : SelectedActive is null ? [] : [SelectedActive];
        if (rows.Count == 0)
        {
            StatusMessage = "Select one or more active pairs to Never.";
            return;
        }

        foreach (var row in rows)
        {
            _engine.RemoveCorrection(row.Trigger, row.Replacement, row.Source);
            _autoLearn?.Never(new PendingPromotion(row.Trigger, row.Replacement, 0, "manual"));
        }

        StatusMessage = rows.Count == 1
            ? $"Never “{rows[0].Trigger}” — won’t autocorrect. Restore anytime from Removed."
            : $"Never’d {rows.Count} pairs — won’t autocorrect. Restore anytime from Removed.";
        SetActiveSelection([]);
        Refresh();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void RestoreSelected()
    {
        var rows = _removedSelected.Count > 0
            ? _removedSelected.ToList()
            : SelectedRemoved is null ? [] : [SelectedRemoved];
        if (rows.Count == 0)
        {
            StatusMessage = "Select one or more removed pairs to restore.";
            return;
        }

        foreach (var row in rows)
            _engine.RestoreCorrection(row.Trigger);

        StatusMessage = rows.Count == 1
            ? $"Restored “{rows[0].Trigger}” — curated/imported can fire again. Re-learn if it was learned-only."
            : $"Restored {rows.Count} pairs — curated/imported can fire again.";
        SetRemovedSelection([]);
        Refresh();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private async Task ExportRemovedAsync()
    {
        if (_removedAll.Count == 0)
        {
            StatusMessage = "Nothing in Never/Removed to export.";
            return;
        }

        try
        {
            var defaultName = $"AutoCorrect-removed-{DateTime.Now:yyyyMMdd-HHmm}.tsv";
            string? path = null;
            if (_host?.StorageProvider is { } sp)
            {
                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Never / Removed list",
                    SuggestedFileName = defaultName,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("TSV") { Patterns = ["*.tsv", "*.txt"] }
                    ]
                }).ConfigureAwait(true);
                path = file?.TryGetLocalPath();
            }

            path ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                defaultName);

            var sb = new StringBuilder();
            sb.AppendLine("trigger\treplacement\tsource");
            foreach (var row in _removedAll.OrderBy(r => r.Trigger, StringComparer.OrdinalIgnoreCase))
                sb.Append(row.Trigger).Append('\t').Append(row.Replacement).Append('\t').AppendLine(row.Source);
            await File.WriteAllTextAsync(path, sb.ToString()).ConfigureAwait(true);
            StatusMessage = $"Exported {_removedAll.Count} Never/Removed rows to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void ResetLearned()
    {
        _engine.ResetUserWordData(clearLearned: true, clearRemoved: false);
        StatusMessage = "Cleared all auto-learned pairs. Curated defaults and removed-list kept.";
        Refresh();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void ResetRemoved()
    {
        _engine.ResetUserWordData(clearLearned: false, clearRemoved: true);
        StatusMessage = "Cleared removed-list — previously blocked words can autocorrect again.";
        Refresh();
        _onChanged?.Invoke();
    }

    [RelayCommand]
    private void ResetAllUserData()
    {
        _engine.ResetUserWordData(clearLearned: true, clearRemoved: true);
        StatusMessage = "Reset to defaults: learned cleared, removed-list cleared. Shipped curated list unchanged.";
        Refresh();
        _onChanged?.Invoke();
    }
}
