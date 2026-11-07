using System.Text;

namespace AutoCorrect.Core.Correction;

public sealed class LocalRule
{
    public required string Find { get; init; }
    public required string Replace { get; init; }
}

public sealed class LocalRulesEngine
{
    private readonly List<LocalRule> _rules = [];

    public IReadOnlyList<LocalRule> Rules => _rules;
    public bool Loaded { get; private set; }

    public void Clear()
    {
        _rules.Clear();
        Loaded = false;
    }

    public void LoadFromFile(string path)
    {
        _rules.Clear();
        Loaded = true;
        if (!File.Exists(path))
            return;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0)
                continue;
            var find = line[..tab].Trim();
            var repl = line[(tab + 1)..].Trim();
            if (find.Length == 0 || repl.Length == 0)
                continue;
            _rules.Add(new LocalRule { Find = find, Replace = repl });
        }
    }

    public string Apply(string text)
    {
        var outText = text;
        foreach (var rule in _rules)
            outText = ReplaceInsensitivePreserveCase(outText, rule.Find, rule.Replace);
        return outText;
    }

    public static string ReplaceInsensitivePreserveCase(string text, string find, string repl)
    {
        if (find.Length == 0 || text.Length == 0)
            return text;

        // Fast path: most rules miss — avoid allocating a StringBuilder.
        if (text.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0)
            return text;

        var sb = new StringBuilder(text.Length + 16);
        var hay = text;
        while (true)
        {
            var pos = hay.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                sb.Append(hay);
                break;
            }

            sb.Append(hay.AsSpan(0, pos));
            var matched = hay.AsSpan(pos, find.Length);
            var useRepl = repl;
            if (matched.Length > 0 && char.IsUpper(matched[0]) && useRepl.Length > 0)
                useRepl = char.ToUpperInvariant(useRepl[0]) + useRepl[1..];
            sb.Append(useRepl);
            hay = hay[(pos + find.Length)..];
        }

        return sb.ToString();
    }
}
