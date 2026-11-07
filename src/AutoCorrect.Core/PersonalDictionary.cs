namespace AutoCorrect.Core;

/// <summary>Local “Add to dictionary” word list — skips spelling flags on future Fix runs.</summary>
public sealed class PersonalDictionary
{
    private readonly HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);
    private string? _path;
    private bool _loaded;

    public string? Path => _path;
    public int Count => _words.Count;

    public void Load(string? path = null)
    {
        _path = path ?? DataPaths.FindOrCreatePersonalDictionary();
        _words.Clear();
        _loaded = true;
        if (_path is null || !File.Exists(_path))
            return;

        foreach (var line in File.ReadAllLines(_path))
        {
            var w = line.Trim();
            if (w.Length == 0 || w.StartsWith('#') || w.StartsWith(';'))
                continue;
            _words.Add(w);
        }
    }

    public bool Contains(string? word)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(word))
            return false;
        return _words.Contains(word.Trim());
    }

    public bool Add(string word)
    {
        EnsureLoaded();
        word = word.Trim();
        if (word.Length == 0)
            return false;
        if (!_words.Add(word))
            return false;
        Persist();
        return true;
    }

    public IReadOnlyCollection<string> Snapshot()
    {
        EnsureLoaded();
        return _words.ToArray();
    }

    private void EnsureLoaded()
    {
        if (!_loaded)
            Load();
    }

    private void Persist()
    {
        _path ??= DataPaths.FindOrCreatePersonalDictionary();
        if (_path is null)
            return;

        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var lines = _words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Prepend("# AutoCorrect personal dictionary — one word per line");
        File.WriteAllLines(_path, lines);
    }
}
