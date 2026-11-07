namespace FeatureDebugLab.Models;

/// <summary>Persisted GUI configuration for Feature Debug Lab.</summary>
public sealed class LabConfig
{
    // —— Offline suites ——
    public bool TestCuratedMatcher { get; set; } = true;
    public bool TestCasePreserve { get; set; } = true;
    public bool TestImportedLibrary { get; set; } = true;
    public bool TestDictionaryDistance { get; set; } = true;
    public bool TestExcludedProcess { get; set; } = true;
    public bool TestProtectZones { get; set; } = true;
    public bool TestSettingsLoad { get; set; } = true;
    public bool TestLicenseStatus { get; set; } = true;
    public bool TestAutoLearnRoundTrip { get; set; } = true;
    public bool TestWritingAnnotator { get; set; } = true;
    public bool TestRewriteDefaults { get; set; } = true;
    public bool TestThemeCatalog { get; set; } = true;
    public bool TestUpdateFeedReachable { get; set; }
    public bool TestWordDiffTighten { get; set; } = true;
    public bool TestMergeNearbyHunks { get; set; } = true;
    public bool TestPersonalDictionary { get; set; } = true;
    public bool TestHotkeyChordParse { get; set; } = true;
    public bool TestPipelineLocalRules { get; set; } = true;
    public bool TestProductDefaults { get; set; } = true;
    public bool TestSnippetsStore { get; set; } = true;
    public bool TestSettingsNormalize { get; set; } = true;
    public bool TestDataPathsHealth { get; set; } = true;

    /// <summary>When true, Treat soft live Warns that block a feature as Fail (stricter bug hunt).</summary>
    public bool StrictLiveFailures { get; set; } = true;

    // —— Live: scan every installed app, exercise every feature ——
    public bool LiveEnabled { get; set; } = true;
    public bool EnsureTrayAppRunning { get; set; } = true;

    /// <summary>
    /// When true (default), only Notepad/Office/editors/browsers/chat — never Control Panel,
    /// CPL applets, IDEs under test, file managers, etc.
    /// When false, still applies hard safety blocks (control.exe, etc.) but walks many more apps.
    /// </summary>
    public bool LiveTextCapableAppsOnly { get; set; } = true;

    /// <summary>
    /// Optional extra comma-separated exe names to skip.
    /// Example: <c>steam.exe,epicgameslauncher.exe</c>
    /// </summary>
    public string ExtraExcludeExes { get; set; } = "";

    /// <summary>Comma/newline separated typo words (all used — no per-app cap).</summary>
    public string LiveWords { get; set; } = "teh,adn,yestiday,thd,occurence,abotu,recieve,seperate";

    /// <summary>Sample paragraph for Fix / Rewrite exercises.</summary>
    public string LiveFixSampleText { get; set; } =
        "i dont know teh answer and i all ready went yestiday";

    /// <summary>Delay before first keystroke in each app (sec). Keep 0 for speed.</summary>
    public int CountdownSeconds { get; set; } = 0;

    /// <summary>Pause between typed words (ms).</summary>
    public int DelayBetweenWordsMs { get; set; } = 500;

    /// <summary>Wait after a word for autocorrect to land (ms).</summary>
    public int SettleMs { get; set; } = 400;

    /// <summary>Extra pause between apps (ms).</summary>
    public int DelayBetweenAppsMs { get; set; } = 400;

    /// <summary>Close each launched app when its suite finishes.</summary>
    public bool CloseAppsWhenDone { get; set; } = true;

    public string Notes { get; set; } = "";

    // Back-compat for older JSON
    public bool LiveTypingEnabled
    {
        get => LiveEnabled;
        set => LiveEnabled = value;
    }
}
