using System.Text.Json;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Import bulk typo→fix pairs from JSON / TSV / CSV / curated-style text.
/// Shared-list overlaps across users are tallied for future curated updates.
/// </summary>
public static class WordListImporter
{
    public sealed record Result(int Added, int Updated, int Skipped, int TotalParsed, string Summary);

    public static Result ImportInto(
        AutocorrectEngine engine,
        string filePath,
        LearnContributionStore? contribution = null)
    {
        if (!File.Exists(filePath))
            return new Result(0, 0, 0, 0, "File not found.");

        var text = File.ReadAllText(filePath);
        var pairs = Parse(filePath, text);
        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var (trig, repl) in pairs)
        {
            var existed = engine.Learned.ContainsTrigger(trig);
            if (!engine.AddCustomPair(trig, repl, out _))
            {
                skipped++;
                continue;
            }

            if (existed)
                updated++;
            else
                added++;

            try { contribution?.RecordAccepted(trig, repl); }
            catch { /* optional */ }
        }

        var summary =
            $"Imported {added + updated} of {pairs.Count} pairs " +
            $"({added} new, {updated} updated, {skipped} skipped).";
        return new Result(added, updated, skipped, pairs.Count, summary);
    }

    public static List<(string Trigger, string Replacement)> Parse(string filePath, string text)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".json")
            return ParseJson(text);

        return ParseLines(text, csv: ext is ".csv");
    }

    private static List<(string, string)> ParseJson(string text)
    {
        var list = new List<(string, string)>();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray())
                TryAddJsonObject(el, list);
            return Dedup(list);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in pairs.EnumerateArray())
                    TryAddJsonObject(el, list);
                return Dedup(list);
            }

            // { "teh": "the", ... } or AutoLearned-style ignored — flat map of strings
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    Add(list, prop.Name, prop.Value.GetString());
            }
        }

        return Dedup(list);
    }

    private static void TryAddJsonObject(JsonElement el, List<(string, string)> list)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return;
        string? trig = null;
        string? repl = null;
        if (el.TryGetProperty("trigger", out var t))
            trig = t.GetString();
        else if (el.TryGetProperty("typo", out var ty))
            trig = ty.GetString();
        else if (el.TryGetProperty("from", out var f))
            trig = f.GetString();

        if (el.TryGetProperty("replacement", out var r))
            repl = r.GetString();
        else if (el.TryGetProperty("fix", out var c))
            repl = c.GetString();
        else if (el.TryGetProperty("to", out var to))
            repl = to.GetString();

        Add(list, trig, repl);
    }

    private static List<(string, string)> ParseLines(string text, bool csv)
    {
        var list = new List<(string, string)>();
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (CuratedStore.TryParseLine(line, out var trig, out var repl))
            {
                Add(list, trig, repl);
                continue;
            }

            char sep = csv ? ',' : '\t';
            var idx = line.IndexOf(sep);
            if (idx <= 0 && csv)
                idx = line.IndexOf('\t');
            if (idx <= 0 && !csv)
                idx = line.IndexOf(',');
            if (idx <= 0)
                continue;
            Add(list, line[..idx].Trim().Trim('"'), line[(idx + 1)..].Trim().Trim('"'));
        }

        return Dedup(list);
    }

    private static void Add(List<(string, string)> list, string? trig, string? repl)
    {
        if (string.IsNullOrWhiteSpace(trig) || string.IsNullOrWhiteSpace(repl))
            return;
        list.Add((trig.Trim().ToLowerInvariant(), repl.Trim().ToLowerInvariant()));
    }

    private static List<(string, string)> Dedup(List<(string, string)> list)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (t, r) in list)
        {
            if (t.Length == 0 || r.Length == 0 || t == r)
                continue;
            if (t.Any(char.IsWhiteSpace) || r.Any(char.IsWhiteSpace))
                continue;
            map[t] = r; // last wins
        }
        return map.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
