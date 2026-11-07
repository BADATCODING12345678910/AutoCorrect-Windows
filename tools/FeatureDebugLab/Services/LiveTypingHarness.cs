using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AutoCorrect.Core.Autocorrect;
using AutoCorrect.Core.Settings;
using AutoCorrect.Platform.Windows;
using FeatureDebugLab.Models;

namespace FeatureDebugLab.Services;

/// <summary>
/// Scans installed apps, opens each one, and drives every live AutoCorrect feature
/// (autocorrect, Undo, Pause, Fix card, Rewrite). Requires AutoCorrect.App.
/// </summary>
public sealed class LiveTypingHarness
{
    private readonly UiaTextBridge _bridge = new();

    public async Task<IReadOnlyList<TestResult>> RunAsync(
        LabConfig cfg,
        IProgress<string>? log,
        CancellationToken ct,
        IReadOnlyList<DiscoveredApp>? preScanned = null)
    {
        var results = new List<TestResult>();
        if (!cfg.LiveEnabled)
        {
            results.Add(new TestResult("Live", "Suite", TestStatus.Skip, "disabled in config"));
            return results;
        }

        if (!AppWindowProbe.IsAutoCorrectRunning())
        {
            if (cfg.EnsureTrayAppRunning)
            {
                log?.Report("AutoCorrect.App not running — launching local build…");
                var (ok, detail) = await TrayAppLauncher.EnsureRunningAsync(ct).ConfigureAwait(true);
                results.Add(ok
                    ? new TestResult("Live", "Launch tray app", TestStatus.Pass, detail)
                    : new TestResult("Live", "Launch tray app", TestStatus.Fail, detail));
                if (!ok)
                    return results;
                await Task.Delay(800, ct).ConfigureAwait(true);
            }
            else
            {
                results.Add(new TestResult(
                    "Live",
                    "AutoCorrect.App",
                    TestStatus.Fail,
                    "not running — enable Ensure tray app or start AutoCorrect.App (Debug build)"));
                return results;
            }
        }
        else
        {
            results.Add(new TestResult("Live", "AutoCorrect.App", TestStatus.Pass, "process found"));
        }

        var settings = new SettingsStore().Load();
        var words = ParseWords(cfg.LiveWords);
        var engine = new AutocorrectEngine();
        engine.Configure(new AutocorrectSettings());

        var scanned = preScanned ?? InstalledAppScanner.Scan(log);
        var apps = LiveAppPolicy.Filter(scanned, cfg.LiveTextCapableAppsOnly, cfg.ExtraExcludeExes);
        if (apps.Count == 0)
        {
            results.Add(new TestResult("Live", "Scan", TestStatus.Fail,
                "no text-capable apps after policy filter — disable “Text-capable apps only” or install Notepad/Word"));
            return results;
        }

        results.Add(new TestResult("Live", "Scan", TestStatus.Pass,
            $"{apps.Count} apps queued (from {scanned.Count} scanned; textCapableOnly={cfg.LiveTextCapableAppsOnly})"));
        log?.Report($"Live walk: {apps.Count} text-capable apps × all features…");

        DebugLabInjectGate.Open();
        log?.Report("Opened DebugLabInjectGate");
        try
        {
            for (var i = 0; i < apps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var app = apps[i];
                var label = $"{i + 1}/{apps.Count} {app.Name}";
                log?.Report($"── Live → {label} ({app.ExePath}) ──");

                Process? proc = null;
                try
                {
                    proc = await LaunchAsync(app.ExePath, "", ct).ConfigureAwait(true);
                    if (proc is null || proc.HasExited)
                    {
                        results.Add(new TestResult("Live", label + " launch", TestStatus.Skip, "failed to launch"));
                        continue;
                    }

                    await Task.Delay(700, ct).ConfigureAwait(true);
                    FocusMainWindow(proc);
                    await CountdownAsync(cfg, label, proc, log, ct).ConfigureAwait(true);

                    // UIA first — never SendInput into Control Panel / lists / the lab itself.
                    const string probeToken = "ACLAB_PROBE_7f3a";
                    if (!await EnsureTargetFocusAsync(proc, label, log, ct).ConfigureAwait(true))
                    {
                        results.Add(new TestResult("Live", label + " focus", TestStatus.Skip,
                            "could not keep foreground on target app"));
                        continue;
                    }

                    proc.Refresh();
                    var focusedOk = _bridge.TryFocusEditableInProcess(
                        proc.Id, probeToken, out var field, out var focusDetail);
                    if (!focusedOk)
                    {
                        results.Add(new TestResult("Live", label + " text-field", TestStatus.Skip,
                            focusDetail));
                        log?.Report($"{label}: skip — {focusDetail}");
                        continue;
                    }

                    await Task.Delay(150, ct).ConfigureAwait(true);
                    var probe = ReadFocusedNormalized();
                    var probeOk = probe.Contains(probeToken, StringComparison.Ordinal);
                    if (!probeOk)
                    {
                        // Type probe only while FG is still the target + UIA already chose an edit.
                        if (!await EnsureTargetFocusAsync(proc, label, log, ct).ConfigureAwait(true))
                        {
                            results.Add(new TestResult("Live", label + " text-field", TestStatus.Skip,
                                "lost focus before probe type"));
                            continue;
                        }
                        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);
                        SendInputText.TypeAsUser(probeToken);
                        await Task.Delay(350, ct).ConfigureAwait(true);
                        probe = ReadFocusedNormalized();
                        probeOk = probe.Contains(probeToken, StringComparison.Ordinal);
                    }

                    if (!probeOk)
                    {
                        results.Add(new TestResult("Live", label + " text-field", TestStatus.Skip,
                            $"no confirmed document body ({focusDetail})"));
                        log?.Report($"{label}: skip — probe not confirmed in text field");
                        continue;
                    }

                    results.Add(new TestResult("Live", label + " text-field", TestStatus.Pass,
                        $"{field?.ControlType} · {focusDetail}"));
                    log?.Report($"{label}: text field OK ({field?.ControlType})");

                    if (words.Count > 0)
                        results.Add(await RunAutocorrectAsync(label, proc, words, engine, cfg, log, ct)
                            .ConfigureAwait(true));

                    results.Add(await RunAutocorrectUndoAsync(label, proc, settings, cfg, log, ct)
                        .ConfigureAwait(true));
                    results.Add(await RunPauseAsync(label, proc, settings, cfg, log, ct)
                        .ConfigureAwait(true));
                    results.AddRange(await RunFixCardAsync(label, proc, settings, cfg, log, ct)
                        .ConfigureAwait(true));
                    results.Add(await RunRewritePromptAsync(label, proc, settings, cfg, log, ct)
                        .ConfigureAwait(true));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    results.Add(new TestResult("Live", label, TestStatus.Fail, ex.Message));
                }
                finally
                {
                    if (cfg.CloseAppsWhenDone && proc is { HasExited: false })
                    {
                        try
                        {
                            proc.CloseMainWindow();
                            if (!proc.WaitForExit(2000))
                                proc.Kill(entireProcessTree: true);
                        }
                        catch { /* ignore */ }
                    }

                    if (cfg.DelayBetweenAppsMs > 0 && i < apps.Count - 1)
                        await Task.Delay(cfg.DelayBetweenAppsMs, ct).ConfigureAwait(true);
                }
            }
        }
        finally
        {
            DebugLabInjectGate.Close();
            log?.Report("Closed DebugLabInjectGate");
        }

        if (cfg.StrictLiveFailures)
        {
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Status != TestStatus.Warn)
                    continue;
                // Soft skips stay Warn; feature regressions that mean a broken product become Fail.
                if (r.Name.Contains("autocorrect", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains("pause", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains("fix-card", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains("fix-apply", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains("Launch tray", StringComparison.OrdinalIgnoreCase))
                {
                    results[i] = r with
                    {
                        Status = TestStatus.Fail,
                        FixHint = r.FixHint ?? LiveFixHint(r.Name)
                    };
                }
            }
        }

        return results;
    }

    private static string LiveFixHint(string name)
    {
        if (name.Contains("Launch tray", StringComparison.OrdinalIgnoreCase))
            return "tools/FeatureDebugLab/Services/TrayAppLauncher.cs — build AutoCorrect.App Debug and ensure path resolve.";
        if (name.Contains("autocorrect-undo", StringComparison.OrdinalIgnoreCase))
            return "TypingAutocorrectService undo stack + GlobalHotkey UndoHotkey; confirm DebugLabInjectGate is open.";
        if (name.Contains("autocorrect", StringComparison.OrdinalIgnoreCase))
            return "TypingAutocorrectService + DebugLabInjectGate + AutocorrectEngine; verify Debug tray build is running.";
        if (name.Contains("pause", StringComparison.OrdinalIgnoreCase))
            return "AppViewModel.ToggleSoftPause / SoftPaused; PauseHotkey in Settings.";
        if (name.Contains("fix", StringComparison.OrdinalIgnoreCase))
            return "AppViewModel.FixNowAsync + UiaTextBridge capture; LanguageTool reachability; review card title 'AutoCorrect Suggestion'.";
        if (name.Contains("rewrite", StringComparison.OrdinalIgnoreCase))
            return "RewritePromptWindow + license Premium AI gate; RewriteHotkey.";
        return "See LiveTypingHarness.cs for this live scenario.";
    }

    private async Task<bool> EnsureTargetFocusAsync(
        Process proc, string label, IProgress<string>? log, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            FocusMainWindow(proc);
            await Task.Delay(120, ct).ConfigureAwait(true);
            proc.Refresh();
            if (_bridge.IsForegroundProcess(proc.Id))
                return true;
        }
        log?.Report($"{label}: foreground is not target (refusing keys — prevents Fix on lab/Cursor)");
        return false;
    }

    private async Task<TestResult> RunAutocorrectAsync(
        string appName,
        Process proc,
        IReadOnlyList<string> words,
        AutocorrectEngine engine,
        LabConfig cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
            return new TestResult("Live", appName + " autocorrect", TestStatus.Skip, "lost focus");
        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);
        await ClearFieldAsync(ct).ConfigureAwait(true);
        var expectedParts = new List<string>();
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();
            var match = engine.TryMatch(word);
            var expected = match?.CasedReplacement ?? word;
            expectedParts.Add(expected);
            log?.Report($"{appName}: autocorrect '{word} ' → '{expected}'");
            await TypeWordAsync(word, cfg, ct).ConfigureAwait(true);
        }

        await Task.Delay(400, ct).ConfigureAwait(true);
        var text = ReadFocusedNormalized();
        var expectedBlob = string.Join(" ", expectedParts);
        var pass = text.Contains(expectedBlob, StringComparison.Ordinal) ||
                   WordsPresentInOrder(text, expectedParts);
        return pass
            ? new TestResult("Live", appName + " autocorrect", TestStatus.Pass, $"got «{Truncate(text, 120)}»")
            : new TestResult("Live", appName + " autocorrect", TestStatus.Fail,
                "as-you-type did not land",
                Expected: Truncate(expectedBlob, 80),
                Actual: Truncate(text, 120),
                FixHint: LiveFixHint(appName + " autocorrect"),
                Category: "LiveAutocorrect");
    }

    private async Task<TestResult> RunAutocorrectUndoAsync(
        string appName,
        Process proc,
        AppSettings settings,
        LabConfig cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
            return new TestResult("Live", appName + " autocorrect-undo", TestStatus.Skip, "lost focus");
        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);
        await ClearFieldAsync(ct).ConfigureAwait(true);
        log?.Report($"{appName}: autocorrect undo");
        await TypeWordAsync("teh", cfg, ct).ConfigureAwait(true);
        var afterCorrect = ReadFocusedNormalized();
        if (!afterCorrect.Contains("the", StringComparison.OrdinalIgnoreCase))
        {
            return new TestResult("Live", appName + " autocorrect-undo", TestStatus.Fail,
                "teh did not become the before Undo",
                Expected: "…the…",
                Actual: Truncate(afterCorrect, 80),
                FixHint: LiveFixHint(appName + " autocorrect"),
                Category: "LiveAutocorrect");
        }

        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
            return new TestResult("Live", appName + " autocorrect-undo", TestStatus.Skip, "lost focus before Undo");
        if (!HotkeyChord.TrySend(settings.General.UndoHotkey))
            return new TestResult("Live", appName + " autocorrect-undo", TestStatus.Fail,
                "could not parse UndoHotkey " + settings.General.UndoHotkey,
                FixHint: "Settings General.UndoHotkey / HotkeyChord parse.");

        await Task.Delay(Math.Max(400, cfg.SettleMs), ct).ConfigureAwait(true);
        var afterUndo = ReadFocusedNormalized();
        var ok = afterUndo.Contains("teh", StringComparison.OrdinalIgnoreCase);
        return ok
            ? new TestResult("Live", appName + " autocorrect-undo", TestStatus.Pass, $"restored «{Truncate(afterUndo, 80)}»")
            : new TestResult("Live", appName + " autocorrect-undo", TestStatus.Fail,
                "Undo did not restore teh",
                Expected: "…teh…",
                Actual: Truncate(afterUndo, 80),
                FixHint: LiveFixHint(appName + " autocorrect-undo"),
                Category: "LiveUndo");
    }

    private async Task<TestResult> RunPauseAsync(
        string appName,
        Process proc,
        AppSettings settings,
        LabConfig cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
            return new TestResult("Live", appName + " pause", TestStatus.Skip, "lost focus");
        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);
        await ClearFieldAsync(ct).ConfigureAwait(true);
        log?.Report($"{appName}: Pause → teh → resume");
        if (!HotkeyChord.TrySend(settings.General.PauseHotkey))
            return new TestResult("Live", appName + " pause", TestStatus.Fail,
                "could not parse PauseHotkey " + settings.General.PauseHotkey);

        await Task.Delay(300, ct).ConfigureAwait(true);
        await TypeWordAsync("teh", cfg, ct).ConfigureAwait(true);
        var pausedText = ReadFocusedNormalized();
        var stayed = pausedText.Contains("teh", StringComparison.Ordinal);

        HotkeyChord.TrySend(settings.General.PauseHotkey);
        await Task.Delay(300, ct).ConfigureAwait(true);

        if (!stayed)
        {
            return new TestResult("Live", appName + " pause", TestStatus.Fail,
                "teh was still corrected while paused",
                Expected: "teh (uncorrected)",
                Actual: Truncate(pausedText, 80),
                FixHint: LiveFixHint(appName + " pause"),
                Category: "LivePause");
        }

        await ClearFieldAsync(ct).ConfigureAwait(true);
        await TypeWordAsync("teh", cfg, ct).ConfigureAwait(true);
        var resumed = ReadFocusedNormalized();
        var resumedOk = resumed.Contains("the", StringComparison.OrdinalIgnoreCase);
        return resumedOk
            ? new TestResult("Live", appName + " pause", TestStatus.Pass, "paused blocked teh; resume corrected")
            : new TestResult("Live", appName + " pause", TestStatus.Warn,
                "pause OK, but resume did not correct teh",
                Expected: "the",
                Actual: Truncate(resumed, 80),
                FixHint: LiveFixHint(appName + " pause"),
                Category: "LivePause");
    }

    private async Task<List<TestResult>> RunFixCardAsync(
        string appName,
        Process proc,
        AppSettings settings,
        LabConfig cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var list = new List<TestResult>();
        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
        {
            list.Add(new TestResult("Live", appName + " fix-card", TestStatus.Skip, "lost focus"));
            return list;
        }
        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);
        await ClearFieldAsync(ct).ConfigureAwait(true);
        var sample = string.IsNullOrWhiteSpace(cfg.LiveFixSampleText)
            ? "i dont know teh answer"
            : cfg.LiveFixSampleText.Trim();

        log?.Report($"{appName}: Fix — {settings.General.FixHotkey}");
        foreach (var ch in sample)
        {
            SendInputText.TypeAsUser(ch.ToString());
            await Task.Delay(18, ct).ConfigureAwait(true);
        }
        await Task.Delay(400, ct).ConfigureAwait(true);
        var before = ReadFocusedNormalized();

        // Critical: never Fire Fix into FeatureDebugLab / Cursor log text.
        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
        {
            list.Add(new TestResult("Live", appName + " fix-card", TestStatus.Skip,
                "refused Fix — focus left target (would hit lab/Cursor)"));
            return list;
        }
        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);

        if (!HotkeyChord.TrySend(settings.General.FixHotkey))
        {
            list.Add(new TestResult("Live", appName + " fix-card", TestStatus.Fail,
                "could not parse FixHotkey " + settings.General.FixHotkey));
            return list;
        }

        var cardShown = AppWindowProbe.TryFindVisibleWindow("AutoCorrect Suggestion", out _, timeoutMs: 12000);
        if (!cardShown)
        {
            list.Add(new TestResult("Live", appName + " fix-card", TestStatus.Warn,
                "review card not detected",
                Expected: "window title contains AutoCorrect Suggestion",
                Actual: "before=«" + Truncate(before, 60) + "»",
                FixHint: LiveFixHint(appName + " fix-card"),
                Category: "LiveFix"));
            return list;
        }

        list.Add(new TestResult("Live", appName + " fix-card open", TestStatus.Pass, "review window visible"));

        SendInputText.SendChordAsUser(false, false, false, false, 0x0D);
        await Task.Delay(900, ct).ConfigureAwait(true);

        if (AppWindowProbe.IsWindowVisibleNow("AutoCorrect Suggestion"))
        {
            SendInputText.SendChordAsUser(false, false, false, false, 0x1B);
            await Task.Delay(400, ct).ConfigureAwait(true);
        }

        var afterApply = ReadFocusedNormalized();
        var changed = !string.Equals(before, afterApply, StringComparison.Ordinal);
        list.Add(changed
            ? new TestResult("Live", appName + " fix-apply", TestStatus.Pass,
                $"«{Truncate(before, 40)}» → «{Truncate(afterApply, 40)}»")
            : new TestResult("Live", appName + " fix-apply", TestStatus.Warn,
                "card opened but field text unchanged after Enter",
                Expected: "field text changes after Accept",
                Actual: Truncate(afterApply, 80),
                FixHint: "ReviewCardWindow Accept → UiaTextBridge.TryWrite; check PreferLowLatency / Electron paste path.",
                Category: "LiveFix"));

        if (changed)
        {
            if (!HotkeyChord.TrySend(settings.General.UndoHotkey))
            {
                list.Add(new TestResult("Live", appName + " fix-undo", TestStatus.Fail, "bad UndoHotkey",
                    FixHint: "Settings General.UndoHotkey"));
            }
            else
            {
                await Task.Delay(700, ct).ConfigureAwait(true);
                var afterUndo = ReadFocusedNormalized();
                var restored = string.Equals(afterUndo, before, StringComparison.Ordinal) ||
                               Math.Abs(afterUndo.Length - before.Length) <= 2;
                list.Add(restored
                    ? new TestResult("Live", appName + " fix-undo", TestStatus.Pass, $"«{Truncate(afterUndo, 60)}»")
                    : new TestResult("Live", appName + " fix-undo", TestStatus.Warn,
                        "undo did not restore prior text",
                        Expected: Truncate(before, 60),
                        Actual: Truncate(afterUndo, 60),
                        FixHint: "AppViewModel UndoLastAsync / undo stack after Fix apply.",
                        Category: "LiveUndo"));
            }
        }

        return list;
    }

    private async Task<TestResult> RunRewritePromptAsync(
        string appName,
        Process proc,
        AppSettings settings,
        LabConfig cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
            return new TestResult("Live", appName + " rewrite", TestStatus.Skip, "lost focus");
        _bridge.TryFocusEditableInProcess(proc.Id, null, out _, out _);
        await ClearFieldAsync(ct).ConfigureAwait(true);
        log?.Report($"{appName}: Rewrite — {settings.General.RewriteHotkey}");
        SendInputText.TypeAsUser("Please make this sentence clearer for testing.");
        await Task.Delay(200, ct).ConfigureAwait(true);
        SendInputText.SendCtrlChordAsUser(0x41);
        await Task.Delay(150, ct).ConfigureAwait(true);

        if (!await EnsureTargetFocusAsync(proc, appName, log, ct).ConfigureAwait(true))
            return new TestResult("Live", appName + " rewrite", TestStatus.Skip,
                "refused Rewrite — focus left target");

        if (!HotkeyChord.TrySend(settings.General.RewriteHotkey))
            return new TestResult("Live", appName + " rewrite", TestStatus.Fail,
                "could not parse RewriteHotkey " + settings.General.RewriteHotkey);

        var shown = AppWindowProbe.TryFindVisibleWindow("Rewrite", out _, timeoutMs: 6000);
        if (shown)
        {
            SendInputText.SendChordAsUser(false, false, false, false, 0x1B);
            await Task.Delay(300, ct).ConfigureAwait(true);
            return new TestResult("Live", appName + " rewrite", TestStatus.Pass, "prompt opened (dismissed)");
        }

        return new TestResult("Live", appName + " rewrite", TestStatus.Warn,
            "Rewrite window not seen — license gate or focus issue",
            Expected: "window title contains Rewrite",
            Actual: "not visible within 6s",
            FixHint: LiveFixHint(appName + " rewrite"),
            Category: "LiveRewrite");
    }

    private async Task TypeWordAsync(string word, LabConfig cfg, CancellationToken ct)
    {
        foreach (var ch in word)
        {
            SendInputText.TypeAsUser(ch.ToString());
            await Task.Delay(25, ct).ConfigureAwait(true);
        }
        SendInputText.TypeAsUser(" ");
        await Task.Delay(Math.Max(80, cfg.SettleMs), ct).ConfigureAwait(true);
        await Task.Delay(Math.Max(0, cfg.DelayBetweenWordsMs), ct).ConfigureAwait(true);
    }

    private async Task ClearFieldAsync(CancellationToken ct)
    {
        SendInputText.SendCtrlChordAsUser(0x41);
        await Task.Delay(80, ct).ConfigureAwait(true);
        SendInputText.TypeAsUser("\b");
        await Task.Delay(120, ct).ConfigureAwait(true);
    }

    private async Task CountdownAsync(
        LabConfig cfg, string name, Process proc, IProgress<string>? log, CancellationToken ct)
    {
        for (var i = cfg.CountdownSeconds; i > 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            log?.Report($"{name}: starting in {i}…");
            await Task.Delay(1000, ct).ConfigureAwait(true);
            FocusMainWindow(proc);
        }
    }

    private string ReadFocusedNormalized()
    {
        var read = _bridge.TryReadFocused();
        var text = (read.Success ? read.Text : null) ?? "";
        var normalized = text.Replace("\r", " ").Replace("\n", " ");
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        return normalized.Trim();
    }

    public static bool IsAutoCorrectRunning() => AppWindowProbe.IsAutoCorrectRunning();

    private static List<string> ParseWords(string raw) =>
        (raw ?? "")
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 0)
            .ToList();

    private static async Task<Process?> LaunchAsync(string fileName, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = true
        };
        var p = Process.Start(psi);
        if (p is null)
            return null;
        for (var i = 0; i < 50; i++)
        {
            ct.ThrowIfCancellationRequested();
            p.Refresh();
            if (p.MainWindowHandle != IntPtr.Zero)
                return p;
            await Task.Delay(100, ct).ConfigureAwait(true);
        }
        return p;
    }

    private static void FocusMainWindow(Process proc)
    {
        try
        {
            proc.Refresh();
            if (proc.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(proc.MainWindowHandle, 9);
                SetForegroundWindow(proc.MainWindowHandle);
            }
        }
        catch { /* ignore */ }
    }

    private static bool WordsPresentInOrder(string haystack, IReadOnlyList<string> words)
    {
        var idx = 0;
        foreach (var w in words)
        {
            var found = haystack.IndexOf(w, idx, StringComparison.Ordinal);
            if (found < 0)
                return false;
            idx = found + w.Length;
        }
        return true;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
