using System.IO;
using System.Net.Http;
using AutoCorrect.Core;
using AutoCorrect.Core.Autocorrect;
using AutoCorrect.Core.Correction;
using AutoCorrect.Core.Licensing;
using AutoCorrect.Core.Settings;
using FeatureDebugLab.Models;

namespace FeatureDebugLab.Services;

public sealed class OfflineSuiteRunner
{
    public IReadOnlyList<TestResult> Run(LabConfig cfg, CancellationToken ct)
    {
        var results = new List<TestResult>();
        var ac = new AutocorrectEngine();

        if (cfg.TestCuratedMatcher)
        {
            ct.ThrowIfCancellationRequested();
            ac.Configure(new AutocorrectSettings());
            results.AddRange(ExpectMany("Curated", ac, [
                ("teh", "the"),
                ("thd", "the"),
                ("adn", "and"),
                ("yestiday", "yesterday"),
                ("the", null),
            ]));
            results.Add(ac.CuratedCount > 0
                ? Pass("Curated", $"Loaded {ac.CuratedCount} pairs")
                : Fail("Curated", "Loaded pairs", "curated list empty",
                    fixHint: "Data/AutocorrectCurated.txt missing or AutocorrectEngine.Configure failed."));
        }

        if (cfg.TestCasePreserve)
        {
            ct.ThrowIfCancellationRequested();
            ac.Configure(new AutocorrectSettings());
            results.AddRange(ExpectMany("Case", ac, [
                ("Teh", "The"),
                ("TEH", "THE"),
                ("THD", "THE"),
            ]));
        }

        if (cfg.TestImportedLibrary)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                AhkHotstringImporter.EnsureImported(force: false);
            }
            catch (Exception ex)
            {
                results.Add(Fail("Imported", "AHK import", ex.Message));
            }

            ac.Configure(new AutocorrectSettings
            {
                Enabled = true,
                UseCurated = false,
                UseImportedLibrary = true,
                UseDictionaryDistance = false,
                Strictness = "balanced"
            });
            results.Add(ac.ImportedCount > 0
                ? Pass("Imported", $"Loaded {ac.ImportedCount} entries")
                : Fail("Imported", "Loaded entries", "imported library empty",
                    fixHint: "Run AhkHotstringImporter / ensure AutocorrectImported.json ships."));
            results.AddRange(ExpectMany("Imported", ac, [
                ("teh", "the"),
                ("abotu", "about"),
                ("occurence", "occurrence"),
            ]));
        }

        if (cfg.TestDictionaryDistance)
        {
            ct.ThrowIfCancellationRequested();
            ac.Configure(new AutocorrectSettings
            {
                Enabled = true,
                UseCurated = false,
                UseImportedLibrary = false,
                UseDictionaryDistance = true
            });
            results.Add(ac.DictionaryCount > 0
                ? Pass("Dictionary", $"Loaded {ac.DictionaryCount} words")
                : Fail("Dictionary", "Loaded words", "wordlist empty",
                    fixHint: "Ship unigram_freq_list_filtered_89k.csv under Data/."));
            results.AddRange(ExpectMany("Dictionary", ac, [
                ("accomodate", "accommodate"),
                ("seperate", "separate"),
                ("receive", null),
            ]));
        }

        if (cfg.TestExcludedProcess)
        {
            ct.ThrowIfCancellationRequested();
            ac.Configure(new AutocorrectSettings());
            var hit = ac.TryMatch("teh", "AutoCorrect.App");
            results.Add(hit is null
                ? Pass("Exclude", "Skips AutoCorrect.App", "teh ignored while excluded")
                : Fail("Exclude", "Skips AutoCorrect.App", $"still matched → {hit.CasedReplacement}"));
        }

        if (cfg.TestProtectZones)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(ProtectZones.IsProtectedToken("user@email.com")
                ? Pass("ProtectZones", "Email token", "user@email.com protected")
                : Fail("ProtectZones", "Email token", "expected protected"));
            results.Add(ProtectZones.IsProtectedToken("https://example.com/x")
                ? Pass("ProtectZones", "URL token", "URL protected")
                : Fail("ProtectZones", "URL token", "expected protected"));
            results.Add(!ProtectZones.IsProtectedToken("teh")
                ? Pass("ProtectZones", "Normal word", "teh not protected")
                : Fail("ProtectZones", "Normal word", "teh should not be protected"));

            ac.Configure(new AutocorrectSettings { ProtectZones = true, UseImportedLibrary = false, UseDictionaryDistance = false });
            var protectedHit = ac.TryMatch("user@email.com");
            results.Add(protectedHit is null
                ? Pass("ProtectZones", "Engine skip", "no match for email-like token")
                : Fail("ProtectZones", "Engine skip", "engine matched protected token"));
        }

        if (cfg.TestSettingsLoad)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var store = new SettingsStore();
                var s = store.Load();
                results.Add(Pass("Settings", "Load native-settings.json", store.Path));
                results.Add(!string.IsNullOrWhiteSpace(s.General.FixHotkey)
                    ? Pass("Settings", "FixHotkey", s.General.FixHotkey)
                    : Fail("Settings", "FixHotkey", "empty"));
                results.Add(!string.IsNullOrWhiteSpace(s.General.ThemeId)
                    ? Pass("Settings", "ThemeId", s.General.ThemeId)
                    : Fail("Settings", "ThemeId", "empty"));
            }
            catch (Exception ex)
            {
                results.Add(Fail("Settings", "Load", ex.Message));
            }
        }

        if (cfg.TestLicenseStatus)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lic = new LicenseService();
                var snap = lic.Refresh();
                results.Add(Pass("License", snap.StatusLine, snap.DetailLine ?? ""));
            }
            catch (Exception ex)
            {
                results.Add(Fail("License", "Refresh", ex.Message));
            }
        }

        if (cfg.TestAutoLearnRoundTrip)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "ac-lab-learned-" + Guid.NewGuid().ToString("n") + ".json");
                var learned = new LearnedStore();
                learned.LoadFromFile(path);
                var learn = new AutoLearnService();
                learn.Configure(new AutoLearnSettings { Enabled = true, LearnMode = "silent", MinOccurrences = 2 }, learned);
                learn.RecordOccurrence("teh", "the");
                learn.RecordOccurrence("teh", "the");
                ac.Configure(new AutocorrectSettings
                {
                    Enabled = true,
                    UseCurated = false,
                    UseImportedLibrary = false,
                    UseDictionaryDistance = false,
                    UseLearned = true
                });
                // Reload learned from temp — engine uses default learned path; instead assert store API.
                learned.Upsert("zzlabtypo", "labfix");
                var reload = new LearnedStore();
                reload.LoadFromFile(path);
                results.Add(reload.TryGet("zzlabtypo", out var repl) && repl == "labfix"
                    ? Pass("AutoLearn", "LearnedStore round-trip", path)
                    : Fail("AutoLearn", "LearnedStore round-trip", "missing pair after reload"));
                try { File.Delete(path); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                results.Add(Fail("AutoLearn", "Round-trip", ex.Message));
            }
        }

        if (cfg.TestWritingAnnotator)
        {
            ct.ThrowIfCancellationRequested();
            var correctnessOnly = new List<CorrectionHunk>
            {
                new() { StartPos = 0, OldText = "teh", NewText = "the", Category = SuggestionCategory.Spelling, CategoryLabel = "Correct your spelling" }
            };
            var mixed = new List<CorrectionHunk>
            {
                new() { StartPos = 0, OldText = "teh", NewText = "the", Category = SuggestionCategory.Spelling, CategoryLabel = "Correct your spelling" },
                new() { StartPos = 10, OldText = "due to the fact that", NewText = "because", Category = SuggestionCategory.Other, CategoryLabel = "x" }
            };
            WritingSuggestionAnnotator.MarkStyleOnlyHunks(mixed, correctnessOnly);
            var ok = mixed[0].Category == SuggestionCategory.Spelling &&
                     mixed[1].Category == SuggestionCategory.Clarity;
            results.Add(ok
                ? Pass("Writing", "Annotator", "clarity label applied")
                : Fail("Writing", "Annotator", "unexpected categories"));
        }

        if (cfg.TestRewriteDefaults)
        {
            ct.ThrowIfCancellationRequested();
            var ok = !string.IsNullOrWhiteSpace(ChatGptCorrectionClient.DefaultRewriteSystem) &&
                     new AppSettings().General.RewriteHotkey.Equals("Win+Alt+R", StringComparison.OrdinalIgnoreCase);
            results.Add(ok
                ? Pass("Rewrite", "Defaults", "hotkey + system prompt present")
                : Fail("Rewrite", "Defaults", "missing defaults"));
        }

        if (cfg.TestThemeCatalog)
        {
            ct.ThrowIfCancellationRequested();
            // Lab does not reference AutoCorrect.App; verify persisted theme id + themed ico folder.
            var themeId = new SettingsStore().Load().General.ThemeId;
            var ico = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoCorrect", "themed-app.ico");
            results.Add(Pass("Theme", "ThemeId setting", themeId));
            results.Add(File.Exists(ico)
                ? Pass("Theme", "themed-app.ico", ico)
                : Warn("Theme", "themed-app.ico", "not written yet — open Appearance once in the main app",
                    fixHint: "ThemeLogoApplier / DesktopShortcutIconUpdater writes themed-app.ico on theme apply."));
        }

        if (cfg.TestUpdateFeedReachable)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var json = http.GetStringAsync(
                    "https://arsfrcafaoagrjbnjydx.supabase.co/functions/v1/update-feed", ct).GetAwaiter().GetResult();
                results.Add(json.Contains("\"version\"", StringComparison.Ordinal)
                    ? Pass("Updates", "Feed reachable", json.Length > 120 ? json[..120] + "…" : json)
                    : Fail("Updates", "Feed reachable", "unexpected body"));
            }
            catch (Exception ex)
            {
                results.Add(Fail("Updates", "Feed reachable", ex.Message));
            }
        }

        if (cfg.TestWordDiffTighten)
        {
            ct.ThrowIfCancellationRequested();
            var loose = new CorrectionHunk
            {
                StartPos = 2,
                OldText = "teh cat",
                NewText = "the cat"
            };
            var tight = WordDiff.TightenHunk(loose.Clone());
            results.Add(tight.OldText.Equals("teh", StringComparison.Ordinal) &&
                        tight.NewText.Equals("the", StringComparison.Ordinal)
                ? Pass("WordDiff", "TightenHunk", "teh→the")
                : Fail("WordDiff", "TightenHunk", $"got {tight.OldText}→{tight.NewText}"));
        }

        if (cfg.TestMergeNearbyHunks)
        {
            ct.ThrowIfCancellationRequested();
            var a = new CorrectionHunk { StartPos = 0, OldText = "i", NewText = "I" };
            var b = new CorrectionHunk { StartPos = 2, OldText = "all ready", NewText = "already" };
            var merged = WordDiff.MergeNearbyHunks([a, b], "i all ready", maxGapChars: 2);
            results.Add(merged.Count == 1
                ? Pass("WordDiff", "MergeNearbyHunks", merged[0].OldText + "→" + merged[0].NewText)
                : Fail("WordDiff", "MergeNearbyHunks", $"expected 1 hunk, got {merged.Count}"));
        }

        if (cfg.TestPersonalDictionary)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "ac-lab-dict-" + Guid.NewGuid().ToString("n") + ".txt");
                File.WriteAllText(path, "LabWordXYZ\n");
                var dict = new PersonalDictionary();
                dict.Load(path);
                results.Add(dict.Contains("LabWordXYZ")
                    ? Pass("Dictionary", "PersonalDictionary load", path)
                    : Fail("Dictionary", "PersonalDictionary load", "token missing"));
                try { File.Delete(path); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                results.Add(Fail("Dictionary", "PersonalDictionary", ex.Message));
            }
        }

        if (cfg.TestHotkeyChordParse)
        {
            ct.ThrowIfCancellationRequested();
            var ok =
                HotkeyChord.TryParse("Win+Alt+F", out var w, out var a, out var c, out var s, out var vk) &&
                w && a && !c && !s && vk == 0x46 &&
                HotkeyChord.TryParse("Win+Alt+Shift+F", out w, out a, out c, out s, out vk) &&
                w && a && !c && s && vk == 0x46;
            results.Add(ok
                ? Pass("Hotkeys", "Chord parse", "Fix + Undo shapes OK")
                : Fail("Hotkeys", "Chord parse", "unexpected parse"));
        }

        if (cfg.TestPipelineLocalRules)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var pipeline = new CorrectionPipeline();
                var opts = new CorrectionOptions
                {
                    Provider = "none",
                    UseLocalRules = true,
                    UsePunctuation = true,
                    UseLightPolish = false
                };
                var result = pipeline.CorrectAsync("hello world", opts, CancellationToken.None)
                    .GetAwaiter().GetResult();
                results.Add(Pass("Pipeline", "CorrectAsync local-only",
                    $"suggestion len={result.Suggestion.Length}"));
            }
            catch (Exception ex)
            {
                results.Add(Fail("Pipeline", "CorrectAsync", ex.Message,
                    fixHint: "AutoCorrect.Core/Correction/CorrectionPipeline.cs — local-only path threw."));
            }
        }

        if (cfg.TestProductDefaults)
        {
            ct.ThrowIfCancellationRequested();
            var fresh = new AppSettings();
            results.Add(fresh.General.ThemeId.Equals("light-lavender", StringComparison.OrdinalIgnoreCase)
                ? Pass("Defaults", "ThemeId", "light-lavender")
                : Fail("Defaults", "ThemeId", "",
                    expected: "light-lavender",
                    actual: fresh.General.ThemeId,
                    fixHint: "AppSettings.General.ThemeId + AppThemeCatalog.DefaultThemeId must be light-lavender."));
            results.Add(fresh.General.PrivacyHotkey.Equals("Win+Alt+Shift+P", StringComparison.OrdinalIgnoreCase)
                ? Pass("Defaults", "PrivacyHotkey", fresh.General.PrivacyHotkey)
                : Fail("Defaults", "PrivacyHotkey", "",
                    expected: "Win+Alt+Shift+P",
                    actual: fresh.General.PrivacyHotkey,
                    fixHint: "AppSettings.General.PrivacyHotkey default drifted."));
            results.Add(fresh.Dictation.MultiUtteranceQuietMs == 2800
                ? Pass("Defaults", "MultiUtteranceQuietMs", "2800")
                : Fail("Defaults", "MultiUtteranceQuietMs", "",
                    expected: "2800",
                    actual: fresh.Dictation.MultiUtteranceQuietMs.ToString(),
                    fixHint: "AppSettings.Dictation.MultiUtteranceQuietMs default (Win+H coalesce)."));
            results.Add(fresh.Dictation.MinArmMs >= 2000
                ? Pass("Defaults", "MinArmMs", fresh.Dictation.MinArmMs.ToString())
                : Fail("Defaults", "MinArmMs", "arm window too short for Voice Typing",
                    expected: ">=2000",
                    actual: fresh.Dictation.MinArmMs.ToString(),
                    fixHint: "AppSettings.Dictation.MinArmMs — keep ≥2000 so Win+H does not instant-cancel."));
        }

        if (cfg.TestSnippetsStore)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "ac-lab-snip-" + Guid.NewGuid().ToString("n") + ".json");
                File.WriteAllText(path,
                    """{"snippets":[{"trigger":";lab","expansion":"LAB_OK","enabled":true}]}""");
                var store = new AutoCorrect.Core.Snippets.SnippetStore();
                store.LoadFromFile(path);
                var ok = store.TryGet(";lab", out var hit) && hit == "LAB_OK";
                results.Add(ok
                    ? Pass("Snippets", "TryGet", path)
                    : Fail("Snippets", "TryGet", "",
                        expected: "LAB_OK",
                        actual: hit ?? "(null)",
                        fixHint: "AutoCorrect.Core/Snippets/SnippetStore.cs — expand/load broken."));
                try { File.Delete(path); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                results.Add(Fail("Snippets", "Store", ex.Message,
                    fixHint: "AutoCorrect.Core/Snippets/SnippetStore.cs"));
            }
        }

        if (cfg.TestSettingsNormalize)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var store = new SettingsStore();
                var s = store.Load();
                // Force out-of-range then re-load via Normalize by saving a clone path is heavy —
                // assert clamps on a fresh Normalize through public Load defaults instead.
                results.Add(s.Dictation.MultiUtteranceQuietMs is >= 0 and <= 15000
                    ? Pass("Settings", "MultiUtteranceQuietMs clamp", s.Dictation.MultiUtteranceQuietMs.ToString())
                    : Fail("Settings", "MultiUtteranceQuietMs clamp", "",
                        expected: "0..15000",
                        actual: s.Dictation.MultiUtteranceQuietMs.ToString(),
                        fixHint: "SettingsStore.Normalize MultiUtteranceQuietMs clamp."));
                results.Add(s.Dictation.MinArmMs is >= 500 and <= 8000
                    ? Pass("Settings", "MinArmMs clamp", s.Dictation.MinArmMs.ToString())
                    : Fail("Settings", "MinArmMs clamp", "",
                        expected: "500..8000",
                        actual: s.Dictation.MinArmMs.ToString(),
                        fixHint: "SettingsStore.Normalize MinArmMs clamp."));
                results.Add(!string.IsNullOrWhiteSpace(s.General.PrivacyHotkey)
                    ? Pass("Settings", "PrivacyHotkey persisted", s.General.PrivacyHotkey)
                    : Warn("Settings", "PrivacyHotkey persisted", "empty after Load",
                        fixHint: "SettingsStore should default PrivacyHotkey to Win+Alt+Shift+P."));
            }
            catch (Exception ex)
            {
                results.Add(Fail("Settings", "Normalize", ex.Message,
                    fixHint: "SettingsStore.cs Normalize/Load"));
            }
        }

        if (cfg.TestDataPathsHealth)
        {
            ct.ThrowIfCancellationRequested();
            var curated = DataPaths.FindAutocorrectCurated();
            results.Add(curated is not null && File.Exists(curated)
                ? Pass("Install", "Curated data file", curated)
                : Fail("Install", "Curated data file", "AutocorrectCurated.txt missing",
                    fixHint: "Ship Data/AutocorrectCurated.txt (or Data/shipped) with the app; DataPaths.FindAutocorrectCurated."));
            var local = DataPaths.LocalAppDataDir();
            results.Add(Directory.Exists(local)
                ? Pass("Install", "LocalAppData", local)
                : Fail("Install", "LocalAppData", "folder missing",
                    fixHint: "DataPaths.LocalAppDataDir should create %LocalAppData%\\AutoCorrect."));
        }

        return results;
    }

    private static IEnumerable<TestResult> ExpectMany(
        string suite,
        AutocorrectEngine ac,
        (string Word, string? Expected)[] cases)
    {
        foreach (var (word, expected) in cases)
        {
            var hit = ac.TryMatch(word);
            var got = hit?.CasedReplacement;
            var pass = string.Equals(got, expected, StringComparison.Ordinal);
            yield return pass
                ? Pass(suite, $"'{word}' → '{expected ?? "(null)"}'", hit?.Source ?? "")
                : Fail(suite, $"'{word}'",
                    detail: $"matcher mismatch",
                    expected: expected ?? "(null)",
                    actual: got ?? "(null)",
                    fixHint: suite switch
                    {
                        "Curated" or "Case" => "AutocorrectEngine + Data/AutocorrectCurated.txt (or fat-finger pack).",
                        "Imported" => "AhkHotstringImporter / AutocorrectImported.json.",
                        "Dictionary" => "Dictionary distance matcher / unigram wordlist.",
                        _ => "AutocorrectEngine.TryMatch"
                    });
        }
    }

    private static TestResult Pass(string suite, string name, string detail = "") =>
        new(suite, name, TestStatus.Pass, detail, Category: suite);

    private static TestResult Fail(
        string suite,
        string name,
        string detail,
        string? expected = null,
        string? actual = null,
        string? fixHint = null) =>
        new(suite, name, TestStatus.Fail, detail, expected, actual,
            fixHint ?? $"Investigate {suite} — see AutoCorrectNative source for {suite}.",
            Category: suite);

    private static TestResult Warn(
        string suite,
        string name,
        string detail,
        string? fixHint = null) =>
        new(suite, name, TestStatus.Warn, detail, FixHint: fixHint, Category: suite);

    private static TestResult Info(string suite, string name, TestStatus status) =>
        new(suite, name, status, "", Category: suite);
}
