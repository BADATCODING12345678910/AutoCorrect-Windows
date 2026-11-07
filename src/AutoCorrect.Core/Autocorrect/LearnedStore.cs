using System.Text.Json;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>Persisted auto-learned typo→fix pairs (JSON under Data/).</summary>
public sealed class LearnedStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    public string? Path
    {
        get { lock (_gate) return _path; }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _path = null;
        }
    }

    public void LoadFromFile(string path)
    {
        lock (_gate)
        {
            _map.Clear();
            _path = path;
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<LearnedFile>(json, JsonOptions);
                if (doc?.Pairs is null)
                    return;
                foreach (var p in doc.Pairs)
                {
                    if (string.IsNullOrWhiteSpace(p.Trigger) || string.IsNullOrWhiteSpace(p.Replacement))
                        continue;
                    var t = p.Trigger.Trim().ToLowerInvariant();
                    var r = p.Replacement.Trim().ToLowerInvariant();
                    if (t.Length > 0 && r.Length > 0 && !string.Equals(t, r, StringComparison.Ordinal))
                        _map[t] = r;
                }
            }
            catch
            {
                _map.Clear();
            }
        }
    }

    public bool TryGet(string trigger, out string replacement)
    {
        lock (_gate)
            return _map.TryGetValue(trigger.Trim(), out replacement!);
    }

    public bool ContainsTrigger(string trigger)
    {
        lock (_gate)
            return _map.ContainsKey(trigger.Trim());
    }

    public IReadOnlyList<(string Trigger, string Replacement)> Snapshot()
    {
        lock (_gate)
            return _map.Select(kv => (kv.Key, kv.Value)).OrderBy(p => p.Key).ToList();
    }

    public void Upsert(string trigger, string replacement)
    {
        lock (_gate)
        {
            var t = trigger.Trim().ToLowerInvariant();
            var r = replacement.Trim().ToLowerInvariant();
            if (t.Length == 0 || r.Length == 0 || t == r)
                return;
            _map[t] = r;
            Save_NoLock();
        }
    }

    public bool Remove(string trigger)
    {
        lock (_gate)
        {
            var t = trigger.Trim().ToLowerInvariant();
            if (!_map.Remove(t))
                return false;
            Save_NoLock();
            return true;
        }
    }

    public (string Trigger, string Replacement)? RemoveLast()
    {
        lock (_gate)
        {
            if (_map.Count == 0)
                return null;
            // Last by ledger order isn't tracked here — remove lexicographically last as fallback
            // Callers should prefer AutoLearnService.UndoLast which uses the ledger.
            var last = _map.Keys.OrderBy(k => k).Last();
            var repl = _map[last];
            _map.Remove(last);
            Save_NoLock();
            return (last, repl);
        }
    }

    private void Save_NoLock()
    {
        _path ??= DataPaths.FindOrCreateAutoLearnedPath();
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var file = new LearnedFile
        {
            Pairs = _map.Select(kv => new LearnedPair { Trigger = kv.Key, Replacement = kv.Value })
                .OrderBy(p => p.Trigger)
                .ToList()
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
    }

    private sealed class LearnedFile
    {
        public List<LearnedPair> Pairs { get; set; } = [];
    }

    private sealed class LearnedPair
    {
        public string Trigger { get; set; } = "";
        public string Replacement { get; set; } = "";
    }
}
