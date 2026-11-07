using AutoCorrect.Core.Stats;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoCorrect.App.ViewModels;

public partial class UsageStatsViewModel : ViewModelBase
{
    private readonly UsageStatsStore _store;

    public UsageStatsViewModel(UsageStatsStore store, bool collecting)
    {
        _store = store;
        Collecting = collecting;
        Refresh();
    }

    [ObservableProperty] private bool _collecting;
    [ObservableProperty] private string _weekSummary = "";
    [ObservableProperty] private string _allTimeSummary = "";
    [ObservableProperty] private string _statusMessage = "";

    [RelayCommand]
    private void Refresh()
    {
        var w = _store.GetWeek();
        var a = _store.GetAllTime();
        WeekSummary = FormatBlock("This week", w);
        AllTimeSummary = FormatBlock("All time", a);
        StatusMessage = Collecting
            ? "Collecting locally — nothing is uploaded."
            : "Stats collection is off (Settings → General). Counts below are frozen until you enable it.";
    }

    [RelayCommand]
    private void ResetWeek()
    {
        _store.ResetWeek();
        Refresh();
        StatusMessage = "This week’s counters cleared.";
    }

    [RelayCommand]
    private void ResetAll()
    {
        _store.ResetAll();
        Refresh();
        StatusMessage = "All local stats cleared.";
    }

    private static string FormatBlock(string title, UsageStatsSnapshot s) =>
        $"{title}\n" +
        $"  Autocorrect: {s.Autocorrect}\n" +
        $"  Fix accepts: {s.FixAccepted}\n" +
        $"  Accept-all batches: {s.FixAcceptAll}\n" +
        $"  Snippets: {s.Snippets}\n" +
        $"  Rewrites: {s.Rewrites}\n" +
        $"  Clipboard Fix: {s.ClipboardFixes}\n" +
        $"  Total: {s.Total}";
}
