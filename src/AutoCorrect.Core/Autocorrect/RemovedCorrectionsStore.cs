using System.Text.Json;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Permanent user blocklist of triggers that must never autocorrect.
/// Survivives resets of learned pairs; restore undoes a removal.
/// </summary>
public sealed class RemovedCorrectionsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, RemovedCorrection> _map = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    public string? Path
    {
        get { lock (_gate) return _path; }
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
                var list = JsonSerializer.Deserialize<List<RemovedCorrection>>(File.ReadAllText(path), JsonOptions);
                if (list is null)
                    return;
                foreach (var item in list)
                {
                    if (string.IsNullOrWhiteSpace(item.Trigger))
                        continue;
                    var t = item.Trigger.Trim().ToLowerInvariant();
                    item.Trigger = t;
                    item.Replacement = (item.Replacement ?? "").Trim().ToLowerInvariant();
                    item.Source ??= "unknown";
                    _map[t] = item;
                }
            }
            catch
            {
                _map.Clear();
            }
        }
    }

    public bool Contains(string trigger)
    {
        lock (_gate)
            return !string.IsNullOrWhiteSpace(trigger) && _map.ContainsKey(trigger.Trim());
    }

    public IReadOnlyList<RemovedCorrection> Snapshot()
    {
        lock (_gate)
            return _map.Values.OrderBy(v => v.Trigger).Select(Clone).ToList();
    }

    public void Add(string trigger, string? replacement, string source)
    {
        lock (_gate)
        {
            var t = trigger.Trim().ToLowerInvariant();
            if (t.Length == 0)
                return;
            _map[t] = new RemovedCorrection
            {
                Trigger = t,
                Replacement = (replacement ?? "").Trim().ToLowerInvariant(),
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim().ToLowerInvariant(),
                RemovedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
            Save_NoLock();
        }
    }

    public RemovedCorrection? Remove(string trigger)
    {
        lock (_gate)
        {
            var t = trigger.Trim().ToLowerInvariant();
            if (!_map.Remove(t, out var item))
                return null;
            Save_NoLock();
            return item;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            Save_NoLock();
        }
    }

    private void Save_NoLock()
    {
        _path ??= DataPaths.FindOrCreateRemovedCorrectionsPath();
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var list = _map.Values.OrderBy(v => v.Trigger).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOptions));
    }

    private static RemovedCorrection Clone(RemovedCorrection r) => new()
    {
        Trigger = r.Trigger,
        Replacement = r.Replacement,
        Source = r.Source,
        RemovedAt = r.RemovedAt
    };
}

public sealed class RemovedCorrection
{
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
    public string Source { get; set; } = "";
    public string RemovedAt { get; set; } = "";
}
