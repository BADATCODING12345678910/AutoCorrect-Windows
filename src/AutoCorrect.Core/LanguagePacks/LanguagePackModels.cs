using System.Text.Json.Serialization;

namespace AutoCorrect.Core.LanguagePacks;

public sealed class LanguagePackManifest
{
    public int Version { get; set; } = 1;
    public List<LanguagePackEntry> Packs { get; set; } = [];
}

public sealed class LanguagePackEntry
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string PackVersion { get; set; } = "1.0.0";

    /// <summary>When true, ships with the app (no download). Typically en-US.</summary>
    public bool Bundled { get; set; }

    public string? Url { get; set; }
    public string? Sha256 { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Fallback codes to try if this exact pack is missing (e.g. es-AR → es-ES).</summary>
    public List<string>? Fallbacks { get; set; }
}

public sealed class InstalledPackMeta
{
    public string Code { get; set; } = "";
    public string PackVersion { get; set; } = "1.0.0";
    public DateTimeOffset InstalledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
