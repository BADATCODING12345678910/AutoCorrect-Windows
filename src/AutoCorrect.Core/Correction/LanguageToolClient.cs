using System.Text;
using System.Text.Json;
using AutoCorrect.Core.Net;

namespace AutoCorrect.Core.Correction;

public sealed class LanguageToolClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public LanguageToolClient(HttpClient? http = null)
    {
        if (http is null)
        {
            _http = SharedHttp.Create(TimeSpan.FromSeconds(18));
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public async Task<string> CorrectAsync(string text, string language = "en-US", CancellationToken ct = default)
    {
        var result = await CheckAsync(text, language, dictionary: null, ct).ConfigureAwait(false);
        return result.CorrectedText;
    }

    public async Task<LanguageToolResult> CheckAsync(
        string text,
        string language = "en-US",
        IReadOnlyCollection<string>? dictionary = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new LanguageToolResult(text, Array.Empty<LanguageToolMatch>());

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["language"] = language,
                // More grammar/style matches on the free public API (still not Premium-only rules).
                ["level"] = "picky"
            });
            using var resp = await _http.PostAsync("https://api.languagetool.org/v2/check", content, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new LanguageToolResult(text, Array.Empty<LanguageToolMatch>());

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var detected = TryReadDetectedLanguage(doc.RootElement);
            if (!doc.RootElement.TryGetProperty("matches", out var matchesEl) ||
                matchesEl.ValueKind != JsonValueKind.Array ||
                matchesEl.GetArrayLength() == 0)
                return new LanguageToolResult(text, Array.Empty<LanguageToolMatch>(), detected);

            var dict = dictionary is null
                ? null
                : new HashSet<string>(dictionary, StringComparer.OrdinalIgnoreCase);

            var matches = new List<LanguageToolMatch>();
            foreach (var match in matchesEl.EnumerateArray())
            {
                if (!match.TryGetProperty("offset", out var offEl) ||
                    !match.TryGetProperty("length", out var lenEl))
                    continue;

                var offset = offEl.GetInt32();
                var length = lenEl.GetInt32();
                if (offset < 0 || length < 0 || offset + length > text.Length)
                    continue;

                if (!match.TryGetProperty("replacements", out var reps) ||
                    reps.GetArrayLength() == 0)
                    continue;
                var value = reps[0].GetProperty("value").GetString();
                if (value is null)
                    continue;

                var matchedText = text.Substring(offset, length);
                var message = match.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
                var shortMessage = match.TryGetProperty("shortMessage", out var smEl) ? smEl.GetString() : null;

                string? issueType = null, categoryId = null, categoryName = null, ruleId = null;
                if (match.TryGetProperty("rule", out var ruleEl))
                {
                    issueType = ruleEl.TryGetProperty("issueType", out var it) ? it.GetString() : null;
                    ruleId = ruleEl.TryGetProperty("id", out var id) ? id.GetString() : null;
                    if (ruleEl.TryGetProperty("category", out var catEl))
                    {
                        categoryId = catEl.TryGetProperty("id", out var cid) ? cid.GetString() : null;
                        categoryName = catEl.TryGetProperty("name", out var cname) ? cname.GetString() : null;
                    }
                }

                var ltMatch = new LanguageToolMatch(
                    offset, length, value, matchedText,
                    message, shortMessage, issueType, categoryId, categoryName, ruleId);

                if (dict is not null &&
                    ltMatch.Category == SuggestionCategory.Spelling &&
                    IsDictionaryHit(matchedText, dict))
                    continue;

                matches.Add(ltMatch);
            }

            if (matches.Count == 0)
                return new LanguageToolResult(text, matches, detected);

            // Apply from end so offsets stay valid
            var ordered = matches.OrderByDescending(m => m.Offset).ToList();
            var sb = new StringBuilder(text);
            foreach (var m in ordered)
            {
                if (m.Offset < 0 || m.Offset + m.Length > sb.Length)
                    continue;
                sb.Remove(m.Offset, m.Length);
                sb.Insert(m.Offset, m.Replacement);
            }

            return new LanguageToolResult(sb.ToString(), matches, detected);
        }
        catch
        {
            return new LanguageToolResult(text, Array.Empty<LanguageToolMatch>());
        }
    }

    private static string? TryReadDetectedLanguage(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("language", out var lang))
            {
                if (lang.TryGetProperty("detectedLanguage", out var det) &&
                    det.TryGetProperty("code", out var detCode))
                {
                    var c = detCode.GetString();
                    if (!string.IsNullOrWhiteSpace(c))
                        return c;
                }

                if (lang.TryGetProperty("code", out var codeEl))
                {
                    var c = codeEl.GetString();
                    if (!string.IsNullOrWhiteSpace(c))
                        return c;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool IsDictionaryHit(string matchedText, HashSet<string> dict)
    {
        var token = SuggestionCategoryLabels.DictionaryToken(matchedText);
        return token is not null && dict.Contains(token);
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
