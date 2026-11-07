using System.IO;
using System.Text.Json;
using FeatureDebugLab.Models;

namespace FeatureDebugLab.Services;

public static class LabConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect",
            "feature-debug-lab.json");

    public static LabConfig Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                return JsonSerializer.Deserialize<LabConfig>(json, JsonOptions) ?? new LabConfig();
            }
        }
        catch
        {
            /* defaults */
        }

        return new LabConfig();
    }

    public static void Save(LabConfig config)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(Path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
