using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FeatureDebugLab.Models;
using FeatureDebugLab.Services;

namespace FeatureDebugLab.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private List<DiscoveredApp> _discovered = [];
    private List<TestResult> _allResults = [];

    [ObservableProperty] private LabConfig _config = LabConfigStore.Load();
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = "Ready — Run Offline (fast bugs) or Full lab.";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _discoveredSummary = "Apps not scanned yet.";
    [ObservableProperty] private string _reportPathHint = BugReportWriter.ReportDir;
    [ObservableProperty] private string _failuresSummary = "No run yet.";
    [ObservableProperty] private int _passCount;
    [ObservableProperty] private int _failCount;
    [ObservableProperty] private int _skipCount;
    [ObservableProperty] private int _warnCount;
    [ObservableProperty] private bool _showFailuresOnly;

    public ObservableCollection<TestResult> Results { get; } = [];
    public ObservableCollection<TestResult> VisibleResults { get; } = [];
    public ObservableCollection<string> DiscoveredAppLines { get; } = [];

    public string ConfigPath => LabConfigStore.Path;

    public MainViewModel()
    {
        _ = RescanAppsAsync();
    }

    partial void OnShowFailuresOnlyChanged(bool value) => RebuildVisible();

    [RelayCommand]
    private void SaveConfig()
    {
        LabConfigStore.Save(Config);
        Status = "Config saved → " + ConfigPath;
        AppendLog("Saved " + ConfigPath);
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        Config = LabConfigStore.Load();
        Status = "Config reloaded.";
        OnPropertyChanged(nameof(Config));
    }

    [RelayCommand]
    private void SelectAllOffline()
    {
        Config.TestCuratedMatcher = true;
        Config.TestCasePreserve = true;
        Config.TestImportedLibrary = true;
        Config.TestDictionaryDistance = true;
        Config.TestExcludedProcess = true;
        Config.TestProtectZones = true;
        Config.TestSettingsLoad = true;
        Config.TestLicenseStatus = true;
        Config.TestAutoLearnRoundTrip = true;
        Config.TestWritingAnnotator = true;
        Config.TestRewriteDefaults = true;
        Config.TestThemeCatalog = true;
        Config.TestUpdateFeedReachable = true;
        Config.TestWordDiffTighten = true;
        Config.TestMergeNearbyHunks = true;
        Config.TestPersonalDictionary = true;
        Config.TestHotkeyChordParse = true;
        Config.TestPipelineLocalRules = true;
        Config.TestProductDefaults = true;
        Config.TestSnippetsStore = true;
        Config.TestSettingsNormalize = true;
        Config.TestDataPathsHealth = true;
        RefreshConfigUi();
    }

    [RelayCommand]
    private void ClearOffline()
    {
        Config.TestCuratedMatcher = false;
        Config.TestCasePreserve = false;
        Config.TestImportedLibrary = false;
        Config.TestDictionaryDistance = false;
        Config.TestExcludedProcess = false;
        Config.TestProtectZones = false;
        Config.TestSettingsLoad = false;
        Config.TestLicenseStatus = false;
        Config.TestAutoLearnRoundTrip = false;
        Config.TestWritingAnnotator = false;
        Config.TestRewriteDefaults = false;
        Config.TestThemeCatalog = false;
        Config.TestUpdateFeedReachable = false;
        Config.TestWordDiffTighten = false;
        Config.TestMergeNearbyHunks = false;
        Config.TestPersonalDictionary = false;
        Config.TestHotkeyChordParse = false;
        Config.TestPipelineLocalRules = false;
        Config.TestProductDefaults = false;
        Config.TestSnippetsStore = false;
        Config.TestSettingsNormalize = false;
        Config.TestDataPathsHealth = false;
        RefreshConfigUi();
    }

    [RelayCommand]
    private async Task RescanAppsAsync()
    {
        if (IsRunning)
            return;
        Status = "Scanning installed apps…";
        AppendLog("Scanning Start Menu + App Paths…");
        var progress = new Progress<string>(AppendLog);
        var scanned = await Task.Run(() => InstalledAppScanner.Scan(progress)).ConfigureAwait(true);
        _discovered = LiveAppPolicy.Filter(scanned, Config.LiveTextCapableAppsOnly, Config.ExtraExcludeExes).ToList();
        DiscoveredAppLines.Clear();
        foreach (var a in _discovered)
            DiscoveredAppLines.Add($"{a.Name}  —  {a.ExePath}  [{a.Source}]");
        DiscoveredSummary = Config.LiveTextCapableAppsOnly
            ? $"{_discovered.Count} text-capable apps (filtered from {scanned.Count}). Control Panel / IDEs / file managers blocked."
            : $"{_discovered.Count} apps after safety filter (from {scanned.Count}).";
        Status = DiscoveredSummary;
    }

    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
        Status = "Stopping…";
    }

    [RelayCommand]
    private Task RunOfflineAsync() => RunCoreAsync(live: false);

    [RelayCommand]
    private Task RunAsync() => RunCoreAsync(live: true);

    private async Task RunCoreAsync(bool live)
    {
        if (IsRunning)
            return;

        if (live)
        {
            Config.LiveEnabled = true;
            Config.EnsureTrayAppRunning = true;
        }
        else
        {
            Config.LiveEnabled = false;
        }

        LabConfigStore.Save(Config);

        IsRunning = true;
        Results.Clear();
        VisibleResults.Clear();
        _allResults = [];
        PassCount = FailCount = SkipCount = WarnCount = 0;
        LogText = "";
        FailuresSummary = "Running…";
        Status = live ? "Running full lab…" : "Running offline bug hunt…";
        AppendLog("=== Feature Debug Lab " + (live ? "FULL" : "OFFLINE") + " " + DateTime.Now.ToString("HH:mm:ss") + " ===");
        AppendLog("AutoCorrect.App running: " + LiveTypingHarness.IsAutoCorrectRunning());
        AppendLog("Reports → " + BugReportWriter.ReportDir);

        if (live && _discovered.Count == 0)
            await RescanAppsAsync().ConfigureAwait(true);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<string>(AppendLog);

        try
        {
            await Task.Run(() =>
            {
                var offline = new OfflineSuiteRunner().Run(Config, ct);
                foreach (var r in offline)
                    AddResult(r);
            }, ct).ConfigureAwait(true);

            if (live)
            {
                var liveResults = await new LiveTypingHarness()
                    .RunAsync(Config, progress, ct, _discovered)
                    .ConfigureAwait(true);
                foreach (var r in liveResults)
                    AddResult(r);
            }

            var report = BugReportWriter.Write(_allResults, live ? "full" : "offline");
            ReportPathHint = BugReportWriter.LastMarkdownPath;
            FailuresSummary = report.HasBugs
                ? $"{report.Fail} fail + {report.Warn} warn — open report or Copy failures for Cursor"
                : "No bugs detected (pass=" + report.Pass + ").";
            Status = $"Done — {PassCount} pass, {FailCount} fail, {WarnCount} warn, {SkipCount} skip";
            AppendLog(Status);
            AppendLog("Wrote " + BugReportWriter.LastJsonPath);
            AppendLog("Wrote " + BugReportWriter.LastMarkdownPath);
            AppendLog("Wrote " + BugReportWriter.LastFailuresPath);
            if (report.HasBugs)
            {
                AppendLog("--- ACTIONABLE ---");
                foreach (var b in report.Bugs)
                    AppendLog($"[{b.Status}] {b.Suite}/{b.Name} → {b.FixHint}");
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
            AppendLog("Cancelled by user.");
            BugReportWriter.Write(_allResults, live ? "full-cancelled" : "offline-cancelled", "cancelled");
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
            AppendLog("ERROR " + ex);
            AddResult(new TestResult("Lab", "Runner crashed", TestStatus.Fail, ex.ToString(),
                FixHint: "FeatureDebugLab crashed — see stack in log; often UIA/COM in LiveTypingHarness."));
            BugReportWriter.Write(_allResults, "error", ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts = null;
            RebuildVisible();
        }
    }

    [RelayCommand]
    private void OpenReportFolder()
    {
        BugReportWriter.OpenReportFolder();
        Status = "Opened " + BugReportWriter.ReportDir;
    }

    [RelayCommand]
    private void OpenLastReport()
    {
        var path = File.Exists(BugReportWriter.LastMarkdownPath)
            ? BugReportWriter.LastMarkdownPath
            : BugReportWriter.ReportDir;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyFailures()
    {
        var bugs = _allResults.Where(r => r.Status is TestStatus.Fail or TestStatus.Warn).ToList();
        var text = bugs.Count == 0
            ? "No failures or warnings from the last Debug Lab run."
            : BugReportWriter.ToFailuresText(bugs.Select(r => new LabBugItem
            {
                Suite = r.Suite,
                Name = r.Name,
                Status = r.StatusTag,
                Detail = r.Detail,
                Expected = r.Expected,
                Actual = r.Actual,
                FixHint = r.FixHint,
                Category = r.Category
            }).ToList());

        try
        {
            File.WriteAllText(BugReportWriter.LastFailuresPath, text, Encoding.UTF8);
            // Windows clip.exe — Avalonia clipboard APIs differ by version; file is canonical for agents.
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "clip",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p is not null)
                {
                    p.StandardInput.Write(text);
                    p.StandardInput.Close();
                    p.WaitForExit(2000);
                }
            }
            catch
            {
                /* file still written */
            }

            Status = bugs.Count == 0
                ? "No bugs — cleared failures file."
                : $"Copied {bugs.Count} issue(s) to clipboard + {BugReportWriter.LastFailuresPath}";
            AppendLog(Status);
            Process.Start(new ProcessStartInfo
            {
                FileName = BugReportWriter.LastFailuresPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Status = "Could not write failures: " + ex.Message;
            AppendLog(Status);
        }
    }

    private void RefreshConfigUi() => OnPropertyChanged(nameof(Config));

    private void AddResult(TestResult r)
    {
        _allResults.Add(r);
        Results.Add(r);
        switch (r.Status)
        {
            case TestStatus.Pass: PassCount++; break;
            case TestStatus.Fail: FailCount++; break;
            case TestStatus.Skip: SkipCount++; break;
            case TestStatus.Warn: WarnCount++; break;
        }

        AppendLog(r.ToAgentLine());
        if (!ShowFailuresOnly || r.Status is TestStatus.Fail or TestStatus.Warn)
            VisibleResults.Add(r);
    }

    private void RebuildVisible()
    {
        VisibleResults.Clear();
        foreach (var r in _allResults)
        {
            if (!ShowFailuresOnly || r.Status is TestStatus.Fail or TestStatus.Warn)
                VisibleResults.Add(r);
        }
    }

    private void AppendLog(string line)
    {
        var sb = new StringBuilder(LogText);
        if (sb.Length > 0)
            sb.AppendLine();
        sb.Append(line);
        LogText = sb.ToString();
    }

    /// <summary>Headless entry used by <c>--offline</c> CLI.</summary>
    public static int RunHeadlessOffline()
    {
        var cfg = LabConfigStore.Load();
        cfg.LiveEnabled = false;
        var results = new OfflineSuiteRunner().Run(cfg, CancellationToken.None);
        var report = BugReportWriter.Write(results, "offline-headless");
        Console.WriteLine($"Pass={report.Pass} Fail={report.Fail} Warn={report.Warn} Skip={report.Skip}");
        Console.WriteLine(BugReportWriter.LastFailuresPath);
        foreach (var b in report.Bugs)
            Console.WriteLine($"[{b.Status}] {b.Suite}/{b.Name} :: {b.FixHint}");
        return report.Fail > 0 ? 2 : report.Warn > 0 ? 1 : 0;
    }
}
