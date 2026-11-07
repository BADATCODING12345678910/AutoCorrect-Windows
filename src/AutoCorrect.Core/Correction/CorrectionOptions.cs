namespace AutoCorrect.Core.Correction;

public sealed class CorrectionOptions
{
    public const string DefaultChatGptInstruction =
        "Fix likely speech-to-text mishearings using context. Keep the author's meaning, tone, and approximate length. Fix homophones and garbled words. " +
        "Especially when Windows Voice Typing wrote \"in\" but the speaker said \"and\" (conjunction), restore \"and\" — do not flip real prepositional \"in\". " +
        "When Voice Typing wrote \"where\" but the speaker said \"or\" (conjunction), restore \"or\" — do not flip real location \"where\". " +
        "Do not add commentary. Return only the corrected text.";

    /// <summary>languagetool | chatgpt | both | none</summary>
    public string Provider { get; init; } = "languagetool";

    public string ProviderOrder { get; init; } = "languagetool_then_chatgpt";
    public string Language { get; init; } = "en-US";
    public bool UseLocalRules { get; init; } = true;
    public bool UsePunctuation { get; init; } = true;
    public bool UseLightPolish { get; init; } = true;
    public string? LocalRulesPath { get; init; }
    public string? OpenAiApiKey { get; init; }
    public string OpenAiModel { get; init; } = "gpt-4o-mini";
    /// <summary>openai | openrouter | anthropic | groq | deepseek | custom</summary>
    public string LlmProvider { get; init; } = "openai";
    /// <summary>Chat completions base (…/v1) or Anthropic API root.</summary>
    public string? LlmBaseUrl { get; init; }
    public string ChatGptInstruction { get; init; } = DefaultChatGptInstruction;

    /// <summary>Phase B — second LLM pass for clarity/conciseness. Default off; needs API key.</summary>
    public bool UseWritingSuggestions { get; init; }

    public const string DefaultWritingInstruction =
        "Improve clarity and conciseness while preserving the author's meaning and tone. Tighten wordy phrases. Do not add new ideas or commentary. Return only the revised text.";

    public string WritingInstruction { get; init; } = DefaultWritingInstruction;
}

public sealed record CorrectionResult(
    string Original,
    string Suggestion,
    IReadOnlyList<CorrectionHunk> Hunks,
    string? DetectedLanguageCode = null)
{
    public bool HasChanges =>
        !string.Equals(Original, Suggestion, StringComparison.Ordinal) && Hunks.Count > 0;

    public string BuildFromSelectedHunks() => WordDiff.BuildSuggestion(Original, Hunks);
}
