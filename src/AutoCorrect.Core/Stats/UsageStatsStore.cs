using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Stats;

public sealed class UsageStatsSnapshot
{
    public long Autocorrect { get; set; }
    public long FixAccepted { get; set; }
    public long FixAcceptAll { get; set; }
    public long Snippets { get; set; }
    public long Rewrites { get; set; }
    public long ClipboardFixes { get; set; }

    public long Total =>
        Autocorrect + FixAccepted + FixAcceptAll + Snippets + Rewrites + ClipboardFixes;
}

public sealed class UsageStatsFile
{
    public UsageStatsSnapshot AllTime { get; set; } = new();
    public UsageStatsSnapshot Week { get; set; } = new();
    public DateTimeOffset WeekStartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastDigestShownUtc { get; set; }
}

/// <summary>Local-only usage counters (opt-in). Never uploaded.</summary>
public sealed class UsageStatsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private UsageStatsFile _file = new();
    private string? _path;
    private bool _loaded;

    public string? Path => _path;

    public void Load(string path)
    {
        lock (_gate)
        {
            _path = path;
            _loaded = true;
            try
            {
                if (File.Exists(path))
                {
                    _file = JsonSerializer.Deserialize<UsageStatsFile>(File.ReadAllText(path), JsonOptions)
                            ?? new UsageStatsFile();
                }
                else
                {
                    _file = new UsageStatsFile { WeekStartedUtc = DateTimeOffset.UtcNow };
                }

                RollWeekIfNeededUnlocked();
            }
            catch
            {
                _file = new UsageStatsFile { WeekStartedUtc = DateTimeOffset.UtcNow };
            }
        }
    }

    public UsageStatsSnapshot GetWeek()
    {
        lock (_gate)
        {
            EnsureLoaded();
            RollWeekIfNeededUnlocked();
            return Clone(_file.Week);
        }
    }

    public UsageStatsSnapshot GetAllTime()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return Clone(_file.AllTime);
        }
    }

    public DateTimeOffset? LastDigestShownUtc
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _file.LastDigestShownUtc;
            }
        }
    }

    public void RecordAutocorrect() => Record(s => s.Autocorrect++);
    public void RecordFixAccepted(int count = 1) => Record(s => s.FixAccepted += count);
    public void RecordFixAcceptAll(int count) => Record(s =>
    {
        s.FixAcceptAll += 1;
        s.FixAccepted += Math.Max(0, count);
    });
    public void RecordSnippet() => Record(s => s.Snippets++);
    public void RecordRewrite() => Record(s => s.Rewrites++);
    public void RecordClipboardFix() => Record(s => s.ClipboardFixes++);

    public void MarkDigestShown()
    {
        lock (_gate)
        {
            EnsureLoaded();
            _file.LastDigestShownUtc = DateTimeOffset.UtcNow;
            SaveUnlocked();
        }
    }

    /// <summary>
    /// Show digest when opted in, at least one event this week, and ≥6.5 days since last digest
    /// (or never shown).
    /// </summary>
    public bool ShouldShowWeeklyDigest()
    {
        lock (_gate)
        {
            EnsureLoaded();
            RollWeekIfNeededUnlocked();
            if (_file.Week.Total <= 0)
                return false;
            if (_file.LastDigestShownUtc is null)
                return true;
            return DateTimeOffset.UtcNow - _file.LastDigestShownUtc.Value >= TimeSpan.FromDays(6.5);
        }
    }

    public string FormatWeeklyDigest()
    {
        var w = GetWeek();
        if (w.Total <= 0)
            return "No corrections recorded this week yet.";

        var parts = new List<string>();
        if (w.Autocorrect > 0) parts.Add($"{w.Autocorrect} autocorrect");
        if (w.FixAccepted > 0) parts.Add($"{w.FixAccepted} Fix accepts");
        if (w.Snippets > 0) parts.Add($"{w.Snippets} snippets");
        if (w.Rewrites > 0) parts.Add($"{w.Rewrites} rewrites");
        if (w.ClipboardFixes > 0) parts.Add($"{w.ClipboardFixes} clipboard");
        return "This week: " + string.Join(", ", parts) + ".";
    }

    public void ResetWeek()
    {
        lock (_gate)
        {
            EnsureLoaded();
            _file.Week = new UsageStatsSnapshot();
            _file.WeekStartedUtc = DateTimeOffset.UtcNow;
            SaveUnlocked();
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            EnsureLoaded();
            _file = new UsageStatsFile { WeekStartedUtc = DateTimeOffset.UtcNow };
            SaveUnlocked();
        }
    }

    private void Record(Action<UsageStatsSnapshot> mutate)
    {
        lock (_gate)
        {
            EnsureLoaded();
            RollWeekIfNeededUnlocked();
            mutate(_file.Week);
            mutate(_file.AllTime);
            SaveUnlocked();
        }
    }

    private void RollWeekIfNeededUnlocked()
    {
        if (DateTimeOffset.UtcNow - _file.WeekStartedUtc < TimeSpan.FromDays(7))
            return;
        _file.Week = new UsageStatsSnapshot();
        _file.WeekStartedUtc = DateTimeOffset.UtcNow;
    }

    private void EnsureLoaded()
    {
        if (_loaded && _path is not null)
            return;
        var path = System.IO.Path.Combine(DataPaths.LocalAppDataDir(), "usage-stats.json");
        Load(path);
    }

    private void SaveUnlocked()
    {
        if (string.IsNullOrWhiteSpace(_path))
            return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_file, JsonOptions));
        }
        catch
        {
            // never break typing
        }
    }

    private static UsageStatsSnapshot Clone(UsageStatsSnapshot s) => new()
    {
        Autocorrect = s.Autocorrect,
        FixAccepted = s.FixAccepted,
        FixAcceptAll = s.FixAcceptAll,
        Snippets = s.Snippets,
        Rewrites = s.Rewrites,
        ClipboardFixes = s.ClipboardFixes
    };
}
