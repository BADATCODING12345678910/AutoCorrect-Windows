using System.Text.Json;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Local anonymized tallies of accepted auto-learned pairs.
/// Future updates can opt-in upload these so popular pairs graduate into the shipped curated list.
/// No PII — trigger/replacement strings and counts only.
/// </summary>
public sealed class LearnContributionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;

    public void Load()
    {
        lock (_gate)
        {
            _counts.Clear();
            _path = DataPaths.FindLearnContribution() ?? DataPaths.FindOrCreateLearnContributionPath();
            if (!File.Exists(_path))
                return;
            try
            {
                var doc = JsonSerializer.Deserialize<ContributionFile>(File.ReadAllText(_path), JsonOptions);
                if (doc?.Pairs is null)
                    return;
                foreach (var p in doc.Pairs)
                {
                    if (string.IsNullOrWhiteSpace(p.Trigger) || string.IsNullOrWhiteSpace(p.Replacement))
                        continue;
                    _counts[PairKey(p.Trigger, p.Replacement)] = Math.Max(1, p.Count);
                }
            }
            catch
            {
                _counts.Clear();
            }
        }
    }

    public void RecordAccepted(string trigger, string replacement)
    {
        lock (_gate)
        {
            var key = PairKey(trigger, replacement);
            _counts.TryGetValue(key, out var n);
            _counts[key] = n + 1;
            Save_NoLock();
        }
    }

    public IReadOnlyList<(string Trigger, string Replacement, int Count)> Snapshot()
    {
        lock (_gate)
        {
            return _counts.Select(kv =>
            {
                var parts = kv.Key.Split('\t');
                return (parts[0], parts.Length > 1 ? parts[1] : "", kv.Value);
            }).OrderByDescending(x => x.Item3).ToList();
        }
    }

    private void Save_NoLock()
    {
        _path ??= DataPaths.FindOrCreateLearnContributionPath();
        var file = new ContributionFile
        {
            Note = "Local tallies only. Future Opt-in: popular pairs may ship in curated defaults.",
            Pairs = _counts.Select(kv =>
            {
                var parts = kv.Key.Split('\t');
                return new ContributionPair
                {
                    Trigger = parts[0],
                    Replacement = parts.Length > 1 ? parts[1] : "",
                    Count = kv.Value
                };
            }).OrderByDescending(p => p.Count).ToList()
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
    }

    private static string PairKey(string t, string r) =>
        t.Trim().ToLowerInvariant() + "\t" + r.Trim().ToLowerInvariant();

    private sealed class ContributionFile
    {
        public string? Note { get; set; }
        public List<ContributionPair> Pairs { get; set; } = [];
    }

    private sealed class ContributionPair
    {
        public string Trigger { get; set; } = "";
        public string Replacement { get; set; } = "";
        public int Count { get; set; }
    }
}
