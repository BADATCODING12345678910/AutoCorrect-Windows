namespace AutoCorrect.Core.Autocorrect;

/// <summary>On-disk cache of parsed AHK hotstrings (<c>Data/AutocorrectImported.json</c>).</summary>
public sealed class ImportedLibraryFile
{
    public string? SourcePath { get; set; }
    public DateTimeOffset ImportedUtc { get; set; }
    public List<ImportedHotstringEntry> Entries { get; set; } = [];

    /// <summary>Empty-replacement AHK rules that block dangerous mid-word fixes.</summary>
    public List<ImportedNullifierEntry> Nullifiers { get; set; } = [];
}
