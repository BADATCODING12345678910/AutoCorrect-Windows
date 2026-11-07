namespace AutoCorrect.Core.Autocorrect;

/// <summary>Shipped allowlist of classic typo → fix pairs (<c>AutocorrectCurated.txt</c>).</summary>
public sealed class CuratedStore
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;

    public string? Path => _path;
    public int Count => _map.Count;
    public bool Loaded { get; private set; }

    public void Clear()
    {
        _map.Clear();
        Loaded = false;
        _path = null;
    }

    public void LoadFromFile(string path)
    {
        _map.Clear();
        _path = path;
        Loaded = true;
        if (!File.Exists(path))
            return;

        foreach (var raw in File.ReadLines(path))
        {
            if (!TryParseLine(raw, out var trigger, out var replacement))
                continue;
            // First wins — keep the earliest curated mapping stable.
            _map.TryAdd(trigger, replacement);
        }
    }

    public bool TryGet(string word, out string replacement)
    {
        replacement = "";
        if (string.IsNullOrWhiteSpace(word))
            return false;
        return _map.TryGetValue(word.Trim(), out replacement!);
    }

    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_map, StringComparer.OrdinalIgnoreCase);

    public static bool TryParseLine(string raw, out string trigger, out string replacement)
    {
        trigger = "";
        replacement = "";
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            return false;

        var tab = line.IndexOf('\t');
        if (tab > 0)
        {
            trigger = line[..tab].Trim();
            replacement = line[(tab + 1)..].Trim();
        }
        else
        {
            // Also accept "typo → fix" / "typo::fix" for hand edits.
            var arrow = line.IndexOf("→", StringComparison.Ordinal);
            if (arrow < 0)
                arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow > 0)
            {
                var sepLen = line[arrow] == '-' ? 2 : 1;
                trigger = line[..arrow].Trim();
                replacement = line[(arrow + sepLen)..].Trim();
            }
            else
            {
                var dbl = line.IndexOf("::", StringComparison.Ordinal);
                if (dbl <= 0)
                    return false;
                trigger = line[..dbl].Trim();
                replacement = line[(dbl + 2)..].Trim();
            }
        }

        if (trigger.Length == 0 || replacement.Length == 0)
            return false;
        if (trigger.Contains(' ') || trigger.Contains('\t'))
            return false; // whole-word single token only
        return true;
    }
}
