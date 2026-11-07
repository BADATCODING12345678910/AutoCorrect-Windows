using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoCorrect.Core.Llm;
using AutoCorrect.Core.Net;

namespace AutoCorrect.Core.Correction;

/// <summary>Result of an OpenAI chat completion — never throw; check <see cref="Ok"/>.</summary>
public sealed record ChatGptResult(bool Ok, string Text, string? Error = null, int? RetryAfterMs = null)
{
    public static ChatGptResult Success(string text) => new(true, text);
    public static ChatGptResult Fail(string original, string error, int? retryAfterMs = null) =>
        new(false, original, error, retryAfterMs);
}

/// <summary>OpenAI chat-completions correctness / rewrite client.</summary>
public sealed class ChatGptCorrectionClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ChatGptCorrectionClient(HttpClient? http = null)
    {
        if (http is null)
        {
            _http = SharedHttp.Create(TimeSpan.FromSeconds(60));
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public const string DefaultRewriteSystem =
        "You rewrite the user's selected text per their instruction. " +
        "Preserve meaning unless they ask otherwise. Return only the rewritten text — no quotes, labels, or commentary.";

    /// <summary>
    /// Returns corrected text, or <paramref name="text"/> unchanged on missing key / HTTP / parse failure.
    /// </summary>
    public async Task<string> CorrectAsync(
        string text,
        string? apiKey,
        string? model = null,
        string? instruction = null,
        string? provider = null,
        string? baseUrl = null,
        CancellationToken ct = default)
    {
        var result = await CorrectDetailedAsync(text, apiKey, model, instruction, provider, baseUrl, ct)
            .ConfigureAwait(false);
        return result.Text;
    }

    public Task<ChatGptResult> CorrectDetailedAsync(
        string text,
        string? apiKey,
        string? model = null,
        string? instruction = null,
        string? provider = null,
        string? baseUrl = null,
        CancellationToken ct = default) =>
        CompleteDetailedAsync(
            text,
            apiKey,
            model,
            string.IsNullOrWhiteSpace(instruction)
                ? CorrectionOptions.DefaultChatGptInstruction
                : instruction.Trim(),
            embedAsSelectedText: false,
            preferredTemperature: 0.2,
            provider,
            baseUrl,
            ct);

    /// <summary>Selection rewrite (Phase D).</summary>
    public async Task<string> RewriteAsync(
        string selectedText,
        string userInstruction,
        string? apiKey,
        string? model = null,
        string? provider = null,
        string? baseUrl = null,
        CancellationToken ct = default)
    {
        var result = await RewriteDetailedAsync(selectedText, userInstruction, apiKey, model, provider, baseUrl, ct)
            .ConfigureAwait(false);
        return result.Text;
    }

    public Task<ChatGptResult> RewriteDetailedAsync(
        string selectedText,
        string userInstruction,
        string? apiKey,
        string? model = null,
        string? provider = null,
        string? baseUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userInstruction))
            return Task.FromResult(ChatGptResult.Fail(selectedText, "Instruction is empty."));

        var instruction =
            DefaultRewriteSystem +
            "\n\nInstruction: " + userInstruction.Trim() +
            "\n\nSelected text:\n";
        return CompleteDetailedAsync(
            selectedText,
            apiKey,
            model,
            instruction,
            embedAsSelectedText: true,
            preferredTemperature: 0.4,
            provider,
            baseUrl,
            ct);
    }

    private async Task<ChatGptResult> CompleteDetailedAsync(
        string text,
        string? apiKey,
        string? model,
        string instruction,
        bool embedAsSelectedText,
        double preferredTemperature,
        string? provider,
        string? baseUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ChatGptResult.Fail(text, "No text to send.");
        if (string.IsNullOrWhiteSpace(apiKey))
            return ChatGptResult.Fail(text, "API key is missing.");

        var info = LlmProviderCatalog.Get(provider);
        var endpointRoot = string.IsNullOrWhiteSpace(baseUrl) ? info.DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
        model = string.IsNullOrWhiteSpace(model) ? info.DefaultModel : model.Trim();
        var prompt = embedAsSelectedText
            ? instruction + text
            : instruction + "\n\nText:\n" + text;

        if (info.ApiKind == LlmApiKind.AnthropicMessages)
            return await SendAnthropicAsync(text, apiKey, model, prompt, endpointRoot, ct).ConfigureAwait(false);

        // o-series / reasoning models often reject temperature — omit it for those.
        var includeTemperature = !IsReasoningModel(model);

        var result = await SendWithRetriesAsync(
                text, apiKey, model, prompt, includeTemperature, preferredTemperature, endpointRoot, info.DisplayName, ct)
            .ConfigureAwait(false);
        if (!result.Ok &&
            includeTemperature &&
            result.Error is not null &&
            result.Error.Contains("temperature", StringComparison.OrdinalIgnoreCase))
        {
            result = await SendWithRetriesAsync(
                    text, apiKey, model, prompt, includeTemperature: false, preferredTemperature, endpointRoot, info.DisplayName, ct)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<ChatGptResult> SendWithRetriesAsync(
        string original,
        string apiKey,
        string model,
        string prompt,
        bool includeTemperature,
        double temperature,
        string baseUrl,
        string providerLabel,
        CancellationToken ct)
    {
        ChatGptResult? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            last = await SendOpenAiCompatibleOnceAsync(
                    original, apiKey, model, prompt, includeTemperature, temperature, baseUrl, providerLabel, ct)
                .ConfigureAwait(false);
            if (last.Ok)
                return last;

            if (last.Error is null ||
                !last.Error.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                last.Error.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                last.Error.Contains("billing", StringComparison.OrdinalIgnoreCase))
                return last;

            var delayMs = last.RetryAfterMs ?? (1000 * (attempt + 1));
            delayMs = Math.Clamp(delayMs, 500, 15000);
            try
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return last;
            }
        }

        return last ?? ChatGptResult.Fail(original, providerLabel + " rate limit (429).");
    }

    private async Task<ChatGptResult> SendOpenAiCompatibleOnceAsync(
        string original,
        string apiKey,
        string model,
        string prompt,
        bool includeTemperature,
        double temperature,
        string baseUrl,
        string providerLabel,
        CancellationToken ct)
    {
        object body = includeTemperature
            ? new
            {
                model,
                temperature,
                messages = new[] { new { role = "user", content = prompt } }
            }
            : new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } }
            };

        try
        {
            var url = baseUrl.TrimEnd('/') + "/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            // OpenRouter optionally uses these; harmless elsewhere.
            if (!req.Headers.Contains("HTTP-Referer"))
                req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://autocorrect-windows.app");
            if (!req.Headers.Contains("X-Title"))
                req.Headers.TryAddWithoutValidation("X-Title", "AutoCorrect for Windows");
            req.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                int? retryMs = null;
                if (resp.Headers.RetryAfter?.Delta is { } delta)
                    retryMs = (int)Math.Ceiling(delta.TotalMilliseconds);
                else if (resp.Headers.RetryAfter?.Date is { } until)
                    retryMs = (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMilliseconds);

                return ChatGptResult.Fail(
                    original,
                    FormatHttpError((int)resp.StatusCode, raw, model, providerLabel),
                    retryMs);
            }

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
                return ChatGptResult.Fail(original, providerLabel + " returned no choices.");

            var content = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            content = StripWrappingQuotes(content?.Trim());
            if (string.IsNullOrEmpty(content))
                return ChatGptResult.Fail(original, providerLabel + " returned empty text.");

            return ChatGptResult.Success(content);
        }
        catch (TaskCanceledException)
        {
            return ChatGptResult.Fail(original, providerLabel + " request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ChatGptResult.Fail(original, "Network error talking to " + providerLabel + ": " + Truncate(ex.Message, 160));
        }
        catch (Exception ex)
        {
            return ChatGptResult.Fail(original, providerLabel + " call failed: " + Truncate(ex.Message, 160));
        }
    }

    private async Task<ChatGptResult> SendAnthropicAsync(
        string original,
        string apiKey,
        string model,
        string prompt,
        string baseUrl,
        CancellationToken ct)
    {
        try
        {
            var url = baseUrl.TrimEnd('/') + "/v1/messages";
            var body = new
            {
                model,
                max_tokens = 4096,
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            req.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return ChatGptResult.Fail(original, FormatHttpError((int)resp.StatusCode, raw, model, "Anthropic"));

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("content", out var contentEl) ||
                contentEl.ValueKind != JsonValueKind.Array ||
                contentEl.GetArrayLength() == 0)
                return ChatGptResult.Fail(original, "Anthropic returned no content.");

            string? textOut = null;
            foreach (var block in contentEl.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) &&
                    t.GetString() == "text" &&
                    block.TryGetProperty("text", out var tx))
                {
                    textOut = tx.GetString();
                    break;
                }
            }

            textOut = StripWrappingQuotes(textOut?.Trim());
            if (string.IsNullOrEmpty(textOut))
                return ChatGptResult.Fail(original, "Anthropic returned empty text.");

            return ChatGptResult.Success(textOut);
        }
        catch (TaskCanceledException)
        {
            return ChatGptResult.Fail(original, "Anthropic request timed out.");
        }
        catch (Exception ex)
        {
            return ChatGptResult.Fail(original, "Anthropic call failed: " + Truncate(ex.Message, 160));
        }
    }

    private static bool IsReasoningModel(string model)
    {
        var m = model.Trim().ToLowerInvariant();
        return m.StartsWith("o1", StringComparison.Ordinal) ||
               m.StartsWith("o3", StringComparison.Ordinal) ||
               m.StartsWith("o4", StringComparison.Ordinal) ||
               m.Contains("reason", StringComparison.Ordinal);
    }

    private static string FormatHttpError(int status, string raw, string model, string providerLabel)
    {
        var (code, message) = ExtractOpenAiError(raw);
        var detail = message;
        if (string.IsNullOrWhiteSpace(detail))
            detail = Truncate(raw.Replace('\r', ' ').Replace('\n', ' ').Trim(), 180);

        return status switch
        {
            401 => providerLabel + " rejected the API key (401).",
            403 => providerLabel + " forbidden (403) — check key permissions.",
            404 => $"{providerLabel} model not found (404): “{model}”. Pick another model in Settings.",
            429 => Format429(code, detail, providerLabel),
            _ => $"{providerLabel} HTTP {status}" + (string.IsNullOrWhiteSpace(detail) ? "." : ": " + detail)
        };
    }

    private static string Format429(string? code, string? detail, string providerLabel)
    {
        var blob = ((code ?? "") + " " + (detail ?? "")).ToLowerInvariant();
        if (blob.Contains("insufficient_quota") || blob.Contains("quota") || blob.Contains("billing"))
        {
            return providerLabel + " quota/billing exhausted (429). Add credits with that provider — this is not fixed by waiting.";
        }

        if (blob.Contains("rate_limit") || blob.Contains("rate limit"))
        {
            return providerLabel + " rate limit (429) — too many requests right now. Wait ~20–60s, or pick a lighter model in Settings.";
        }

        return providerLabel + " rate limit / quota (429). Wait a minute and retry. Detail: " + Truncate(detail, 120);
    }

    private static (string? Code, string? Message) ExtractOpenAiError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                string? code = null;
                string? message = null;
                if (err.TryGetProperty("code", out var c))
                    code = c.GetString();
                if (err.TryGetProperty("type", out var t) && string.IsNullOrWhiteSpace(code))
                    code = t.GetString();
                if (err.TryGetProperty("message", out var msg))
                    message = Truncate(msg.GetString(), 180);
                return (code, message);
            }
        }
        catch
        {
            // ignore parse
        }

        return (null, null);
    }

    private static string? StripWrappingQuotes(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content;
        if (content.Length >= 2 && content[0] == '"' && content[^1] == '"')
            return content[1..^1].Trim();
        return content;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
