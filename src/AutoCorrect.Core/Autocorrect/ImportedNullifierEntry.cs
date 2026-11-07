namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// AHK empty-replacement hotstring that blocks a mid-word / exact rule
/// (e.g. <c>:B0:sign::</c> protects against <c>:?:ign::ing</c>).
/// </summary>
public sealed class ImportedNullifierEntry
{
    public string Trigger { get; set; } = "";
    public string Options { get; set; } = "";
    public bool Question { get; set; }
    public bool CaseSensitive { get; set; }
}
