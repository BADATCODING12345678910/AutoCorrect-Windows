using System.Text.Json;

namespace AutoCorrect.Core.Settings;

public sealed class SavedRewritePreset
{
    public string Label { get; set; } = "";
    public string Instruction { get; set; } = "";
}

/// <summary>User-named rewrite presets under Data/RewritePresets.json.</summary>
public static class RewritePresetsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<SavedRewritePreset> Load()
    {
        var path = DataPaths.FindDataFilePublic("RewritePresets.json");
        if (path is null || !File.Exists(path))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<SavedRewritePreset>>(File.ReadAllText(path), JsonOptions);
            return list?.Where(p => !string.IsNullOrWhiteSpace(p.Label) && !string.IsNullOrWhiteSpace(p.Instruction))
                       .ToList()
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<SavedRewritePreset> presets)
    {
        var path = DataPaths.FindOrCreateDataFilePublic("RewritePresets.json");
        var list = presets
            .Where(p => !string.IsNullOrWhiteSpace(p.Label) && !string.IsNullOrWhiteSpace(p.Instruction))
            .Select(p => new SavedRewritePreset
            {
                Label = p.Label.Trim(),
                Instruction = p.Instruction.Trim()
            })
            .ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(list, JsonOptions));
    }

    public static void Upsert(string label, string instruction)
    {
        var list = Load().ToList();
        var existing = list.FindIndex(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase));
        var item = new SavedRewritePreset { Label = label.Trim(), Instruction = instruction.Trim() };
        if (existing >= 0)
            list[existing] = item;
        else
            list.Add(item);
        Save(list);
    }
}
