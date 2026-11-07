namespace AutoCorrect.Core.Autocorrect;

/// <summary>One imported AHK hotstring (serialized in AutocorrectImported.json).</summary>
public sealed class ImportedHotstringEntry
{
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
    public string Options { get; set; } = "";
    public bool Star { get; set; }
    public bool Question { get; set; }
    public bool CaseSensitive { get; set; }
}
