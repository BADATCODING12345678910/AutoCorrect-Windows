using AutoCorrect.Core.Llm;

namespace AutoCorrect.Core;

/// <summary>
/// Reads/writes LLM credentials. Canonical path is
/// <c>%LocalAppData%\AutoCorrect\PersonalApiKey.ini</c> so in-app updates never wipe the key.
/// Still migrates older <c>Data/PersonalApiKey.ini</c> copies on first load.
/// </summary>
public static class PersonalApiKeyStore
{
    /// <summary>Load usable credentials (placeholders → null ApiKey).</summary>
    public static PersonalApiKeyCredentials? TryLoad(string? path = null)
    {
        var raw = LoadRaw(path);
        if (raw is null)
            return null;

        var key = LooksLikePlaceholder(raw.ApiKey) ? null : raw.ApiKey?.Trim();
        if (key is null &&
            string.IsNullOrWhiteSpace(raw.Model) &&
            string.IsNullOrWhiteSpace(raw.Provider) &&
            string.IsNullOrWhiteSpace(raw.BaseUrl))
            return null;

        return raw with { ApiKey = key };
    }

    /// <summary>Load for Settings editing (keeps placeholders / empty).</summary>
    public static PersonalApiKeyCredentials LoadRaw(string? path = null)
    {
        path ??= DataPaths.FindPersonalApiKey();
        if (path is null || !File.Exists(path))
            return new PersonalApiKeyCredentials("", "", LlmProviderCatalog.OpenAi, "", "");

        string? apiKey = null;
        string? model = null;
        string? provider = null;
        string? baseUrl = null;
        var inSection = false;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = line.Equals("[LLM]", StringComparison.OrdinalIgnoreCase) ||
                            line.Equals("[OpenAI]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
                apiKey = value;
            else if (name.Equals("Model", StringComparison.OrdinalIgnoreCase))
                model = value;
            else if (name.Equals("Provider", StringComparison.OrdinalIgnoreCase))
                provider = value;
            else if (name.Equals("BaseUrl", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                baseUrl = value;
        }

        var info = LlmProviderCatalog.Get(provider);
        return new PersonalApiKeyCredentials(
            apiKey ?? "",
            model ?? "",
            info.Id,
            string.IsNullOrWhiteSpace(baseUrl) ? info.DefaultBaseUrl : baseUrl,
            path);
    }

    /// <summary>
    /// Write credentials under <c>[LLM]</c> to the LocalAppData canonical file.
    /// Returns the path written.
    /// </summary>
    public static string Save(
        string? apiKey,
        string? model = null,
        string? provider = null,
        string? baseUrl = null,
        string? path = null)
    {
        path ??= DataPaths.FindOrCreatePersonalApiKeyPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var info = LlmProviderCatalog.Get(provider);
        var keyValue = (apiKey ?? "").Trim();
        var modelValue = string.IsNullOrWhiteSpace(model) ? info.DefaultModel : model.Trim();
        var baseValue = string.IsNullOrWhiteSpace(baseUrl) ? info.DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');

        // Always rewrite a clean [LLM] file so updates/migrations stay simple.
        // Keep a short [OpenAI] mirror of ApiKey/Model for older AHK readers.
        var text =
            "; AutoCorrect LLM credentials — lives in %LocalAppData%\\AutoCorrect so updates never wipe it.\r\n" +
            "[LLM]\r\n" +
            $"Provider={info.Id}\r\n" +
            $"ApiKey={keyValue}\r\n" +
            $"Model={modelValue}\r\n" +
            $"BaseUrl={baseValue}\r\n" +
            "\r\n" +
            "[OpenAI]\r\n" +
            $"ApiKey={keyValue}\r\n" +
            $"Model={modelValue}\r\n";

        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>True for empty / template keys like <c>paste_your_api_key_here</c>.</summary>
    public static bool LooksLikePlaceholder(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return true;

        var k = apiKey.Trim();
        if (k.Contains("paste", StringComparison.OrdinalIgnoreCase))
            return true;
        if (k.Contains("your", StringComparison.OrdinalIgnoreCase) &&
            k.Contains("key", StringComparison.OrdinalIgnoreCase))
            return true;
        if (k.Equals("CHANGEME", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("xxx", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("...", StringComparison.Ordinal))
            return true;

        return false;
    }
}

/// <param name="SourcePath">File the credentials were read from (may be null when empty defaults).</param>
public sealed record PersonalApiKeyCredentials(
    string? ApiKey,
    string? Model,
    string Provider = "openai",
    string? BaseUrl = null,
    string? SourcePath = null);
