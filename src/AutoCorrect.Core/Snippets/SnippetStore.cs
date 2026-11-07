using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Snippets;

public sealed class SnippetEntry
{
    public string Trigger { get; set; } = "";
    public string Expansion { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class SnippetFile
{
    public List<SnippetEntry> Snippets { get; set; } = [];
}

/// <summary>Loads <c>Data/Snippets.json</c> — intentional expansions separate from typo autocorrect.</summary>
public sealed partial class SnippetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
        Clear();
        _path = path;
        Loaded = true;
        if (!File.Exists(path))
        {
            EnsureSampleFile(path);
            if (!File.Exists(path))
                return;
        }

        try
        {
            var file = JsonSerializer.Deserialize<SnippetFile>(File.ReadAllText(path), JsonOptions)
                       ?? new SnippetFile();
            foreach (var e in file.Snippets)
            {
                if (!e.Enabled)
                    continue;
                var trig = NormalizeTrigger(e.Trigger);
                if (trig is null || string.IsNullOrEmpty(e.Expansion))
                    continue;
                _map.TryAdd(trig, e.Expansion);
            }
        }
        catch
        {
            _map.Clear();
        }
    }

    public bool TryGet(string typedTrigger, out string expansion)
    {
        expansion = "";
        var key = NormalizeTrigger(typedTrigger);
        if (key is null)
            return false;
        return _map.TryGetValue(key, out expansion!);
    }

    /// <summary>Read all snippet rows (including disabled) for the GUI editor.</summary>
    public static List<SnippetEntry> LoadEntries(string path)
    {
        if (!File.Exists(path))
        {
            EnsureSampleFile(path);
            if (!File.Exists(path))
                return [];
        }

        try
        {
            var file = JsonSerializer.Deserialize<SnippetFile>(File.ReadAllText(path), JsonOptions)
                       ?? new SnippetFile();
            return file.Snippets ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Persist the full snippet list, then reload this store’s enabled map from <paramref name="path"/>.
    /// </summary>
    public bool SaveEntries(string path, IEnumerable<SnippetEntry> entries, out string error)
    {
        error = "";
        try
        {
            var list = new List<SnippetEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                var trig = NormalizeTrigger(e.Trigger);
                if (trig is null)
                {
                    error = "Triggers must look like ;sig (semicolon + letters/digits/underscore, max 32).";
                    return false;
                }

                if (string.IsNullOrEmpty(e.Expansion))
                {
                    error = $"Snippet {trig} needs an expansion.";
                    return false;
                }

                if (!seen.Add(trig))
                {
                    error = $"Duplicate trigger {trig}.";
                    return false;
                }

                list.Add(new SnippetEntry
                {
                    Trigger = trig,
                    Expansion = e.Expansion,
                    Enabled = e.Enabled
                });
            }

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var file = new SnippetFile { Snippets = list };
            File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
            LoadFromFile(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string? NormalizeTrigger(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        if (!t.StartsWith(';'))
            t = ";" + t;
        if (!TriggerRegex().IsMatch(t))
            return null;
        return t.ToLowerInvariant();
    }

    private static void EnsureSampleFile(string path)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var sample = new SnippetFile
            {
                Snippets =
                [
                    new SnippetEntry
                    {
                        Trigger = ";sig",
                        Expansion = "Best regards,\nYour Name"
                    },
                    new SnippetEntry
                    {
                        Trigger = ";addr",
                        Expansion = "123 Main St"
                    }
                ]
            };
            File.WriteAllText(path, JsonSerializer.Serialize(sample, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }

    [GeneratedRegex(@"^;[A-Za-z0-9_]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TriggerRegex();
}
