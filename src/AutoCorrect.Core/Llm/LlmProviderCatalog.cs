namespace AutoCorrect.Core.Llm;

/// <summary>Built-in LLM provider presets (OpenAI-compatible chat + Anthropic Messages).</summary>
public static class LlmProviderCatalog
{
    public const string OpenAi = "openai";
    public const string OpenRouter = "openrouter";
    public const string Anthropic = "anthropic";
    public const string Groq = "groq";
    public const string DeepSeek = "deepseek";
    public const string Custom = "custom";

    public static IReadOnlyList<LlmProviderInfo> All { get; } =
    [
        new(OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", LlmApiKind.OpenAiCompatible,
            "platform.openai.com keys (sk-…)."),
        new(OpenRouter, "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4o-mini", LlmApiKind.OpenAiCompatible,
            "One key → many models (OpenAI, Anthropic, Google, Meta, …). openrouter.ai"),
        new(Anthropic, "Anthropic", "https://api.anthropic.com", "claude-3-5-haiku-latest", LlmApiKind.AnthropicMessages,
            "console.anthropic.com keys (sk-ant-…)."),
        new(Groq, "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", LlmApiKind.OpenAiCompatible,
            "console.groq.com — fast OpenAI-compatible API."),
        new(DeepSeek, "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", LlmApiKind.OpenAiCompatible,
            "platform.deepseek.com keys."),
        new(Custom, "Custom (OpenAI-compatible)", "https://api.openai.com/v1", "gpt-4o-mini", LlmApiKind.OpenAiCompatible,
            "Any OpenAI-compatible base URL (Together, Fireworks, Azure proxy, local, …)."),
    ];

    public static LlmProviderInfo Get(string? id)
    {
        id = string.IsNullOrWhiteSpace(id) ? OpenAi : id.Trim().ToLowerInvariant();
        foreach (var p in All)
        {
            if (p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return All[0];
    }

    public static IReadOnlyList<string> ModelPresetsFor(string? providerId)
    {
        var id = string.IsNullOrWhiteSpace(providerId) ? OpenAi : providerId.Trim().ToLowerInvariant();
        return id switch
        {
            OpenRouter =>
            [
                "openai/gpt-4o-mini",
                "openai/gpt-4o",
                "anthropic/claude-3.5-sonnet",
                "google/gemini-2.0-flash-001",
                "meta-llama/llama-3.3-70b-instruct",
            ],
            Anthropic =>
            [
                "claude-3-5-haiku-latest",
                "claude-3-5-sonnet-latest",
                "claude-3-opus-latest",
            ],
            Groq =>
            [
                "llama-3.3-70b-versatile",
                "llama-3.1-8b-instant",
                "mixtral-8x7b-32768",
            ],
            DeepSeek =>
            [
                "deepseek-chat",
                "deepseek-reasoner",
            ],
            _ =>
            [
                "gpt-4o-mini",
                "gpt-4o",
                "gpt-4.1-mini",
                "gpt-4.1",
                "o4-mini",
                "o3-mini",
            ],
        };
    }
}

public enum LlmApiKind
{
    OpenAiCompatible,
    AnthropicMessages,
}

public sealed record LlmProviderInfo(
    string Id,
    string DisplayName,
    string DefaultBaseUrl,
    string DefaultModel,
    LlmApiKind ApiKind,
    string Hint);
