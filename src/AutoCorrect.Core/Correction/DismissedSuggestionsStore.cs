using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Correction;

/// <summary>
/// Remembers Fix suggestions the user Skip'd or Never'd so they don't reappear.
/// Keys: ruleId|category + old→new (normalized).
/// </summary>
public sealed class DismissedSuggestionsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;

    public int Count
    {
        get { lock (_gate) return _keys.Count; }
    }

    public void LoadFromFile(string path)
    {
        lock (_gate)
        {
            _path = path;
            _keys.Clear();
            if (!File.Exists(path))
                return;
            try
            {
                var file = JsonSerializer.Deserialize<DismissedSuggestionsFile>(
                    File.ReadAllText(path), JsonOptions);
                if (file?.Keys is null)
                    return;
                foreach (var k in file.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                        _keys.Add(k.Trim());
                }
            }
            catch
            {
                // keep empty
            }
        }
    }

    public bool Contains(CorrectionHunk hunk)
    {
        lock (_gate)
            return _keys.Contains(KeyFor(hunk));
    }

    public void Remember(CorrectionHunk hunk, bool persist)
    {
        var key = KeyFor(hunk);
        lock (_gate)
        {
            if (!_keys.Add(key))
                return;
            if (persist)
                Save_NoLock();
        }
    }

    public static string KeyFor(CorrectionHunk hunk)
    {
        var rule = string.IsNullOrWhiteSpace(hunk.RuleId)
            ? hunk.Category.ToString()
            : hunk.RuleId.Trim();
        var oldT = Normalize(hunk.OldText);
        var newT = Normalize(hunk.NewText);
        return rule + "|" + oldT + "→" + newT;
    }

    private static string Normalize(string s) =>
        string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private void Save_NoLock()
    {
        if (string.IsNullOrWhiteSpace(_path))
            return;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var file = new DismissedSuggestionsFile
            {
                Keys = _keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }

    private sealed class DismissedSuggestionsFile
    {
        public List<string> Keys { get; set; } = [];
    }
}
