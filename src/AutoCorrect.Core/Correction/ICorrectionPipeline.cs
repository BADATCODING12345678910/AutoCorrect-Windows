namespace AutoCorrect.Core.Correction;

/// <summary>
/// Shared correction pipeline (LanguageTool → local rules → punctuation → polish).
/// Port of Includes/DictationFixer.ahk GetSuggestion + hunk helpers.
/// </summary>
public interface ICorrectionPipeline
{
    Task<CorrectionResult> CorrectAsync(
        string input,
        CorrectionOptions? options = null,
        CancellationToken ct = default,
        IProgress<CorrectionStage>? progress = null);
}

/// <summary>User-visible Fix/Rewrite pipeline stage (same final result; progress only).</summary>
public readonly record struct CorrectionStage(string Message, double? Fraction = null);
