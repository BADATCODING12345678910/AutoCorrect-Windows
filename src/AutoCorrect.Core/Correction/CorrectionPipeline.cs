namespace AutoCorrect.Core.Correction;

public sealed class CorrectionPipeline : ICorrectionPipeline, IDisposable
{
    private readonly LocalRulesEngine _localRules = new();
    private readonly LanguageToolClient _languageTool;
    private readonly ChatGptCorrectionClient _chatGpt;
    private readonly PersonalDictionary _dictionary;
    private string? _loadedRulesPath;

    public CorrectionPipeline(
        LanguageToolClient? languageTool = null,
        ChatGptCorrectionClient? chatGpt = null,
        PersonalDictionary? dictionary = null)
    {
        _languageTool = languageTool ?? new LanguageToolClient();
        _chatGpt = chatGpt ?? new ChatGptCorrectionClient();
        _dictionary = dictionary ?? new PersonalDictionary();
        _dictionary.Load();
    }

    public PersonalDictionary Dictionary => _dictionary;

    public async Task<CorrectionResult> CorrectAsync(
        string input,
        CorrectionOptions? options = null,
        CancellationToken ct = default,
        IProgress<CorrectionStage>? progress = null)
    {
        options ??= new CorrectionOptions();
        var original = input ?? "";
        var result = original;
        IReadOnlyList<LanguageToolMatch> ltMatches = Array.Empty<LanguageToolMatch>();
        string? ltSourceText = null;
        string? detectedLanguage = null;

        var provider = (options.Provider ?? "languagetool").Trim().ToLowerInvariant();
        var order = (options.ProviderOrder ?? "languagetool_then_chatgpt").Trim().ToLowerInvariant();

        // Preload local rules before network so file I/O is not paid after LT/AI wait.
        if (options.UseLocalRules)
            EnsureLocalRules(options.LocalRulesPath);

        Report(progress, "Starting…", 0.05);

        switch (provider)
        {
            case "none":
                break;

            case "chatgpt":
                Report(progress, "AI correcting…", 0.25);
                result = await RunChatGptAsync(result, options, ct).ConfigureAwait(false);
                break;

            case "both" when order == "chatgpt_then_languagetool":
                Report(progress, "AI correcting…", 0.2);
                result = await RunChatGptAsync(result, options, ct).ConfigureAwait(false);
                Report(progress, "LanguageTool…", 0.55);
                (result, ltMatches, ltSourceText, detectedLanguage) =
                    await RunLanguageToolAsync(result, options, ct).ConfigureAwait(false);
                break;

            case "both":
                // languagetool_then_chatgpt (default) — order preserved for identical results.
                Report(progress, "LanguageTool…", 0.2);
                (result, ltMatches, ltSourceText, detectedLanguage) =
                    await RunLanguageToolAsync(result, options, ct).ConfigureAwait(false);
                Report(progress, "AI polish…", 0.55);
                result = await RunChatGptAsync(result, options, ct).ConfigureAwait(false);
                break;

            default:
                // languagetool
                Report(progress, "LanguageTool…", 0.35);
                (result, ltMatches, ltSourceText, detectedLanguage) =
                    await RunLanguageToolAsync(result, options, ct).ConfigureAwait(false);
                break;
        }

        // LT match offsets are only valid vs the original when LT ran on the original text.
        if (ltSourceText is null ||
            !string.Equals(ltSourceText, original, StringComparison.Ordinal))
            ltMatches = Array.Empty<LanguageToolMatch>();

        Report(progress, "Local polish…", 0.82);

        if (options.UseLocalRules)
            result = _localRules.Apply(result);

        // STT: user said "and", VT often writes "in" — restore from context (local, no cloud).
        result = SttContextFixer.Apply(result);

        // Accidental doubles (spaces/commas/..) vs intentional style — paragraph context.
        result = ContextualAccidentFixer.Apply(result);

        if (options.UsePunctuation)
            result = PunctuationFixer.Apply(result);

        if (options.UseLightPolish)
            result = LightPolishFixer.Apply(result);

        if (string.IsNullOrEmpty(result))
            result = original;

        // Snapshot correctness-only suggestion before optional writing pass.
        var correctedOnly = result;
        var correctnessHunks = WordDiff.ComputeHunks(original, correctedOnly);
        HunkAnnotator.Annotate(correctnessHunks, ltMatches, _dictionary);

        if (options.UseWritingSuggestions)
        {
            Report(progress, "Writing suggestions…", 0.9);
            result = await RunWritingSuggestionsAsync(result, options, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(result))
            result = correctedOnly;

        List<CorrectionHunk> hunks;
        if (string.Equals(result, correctedOnly, StringComparison.Ordinal))
        {
            hunks = correctnessHunks.ToList();
        }
        else
        {
            hunks = WordDiff.ComputeHunks(original, result);
            HunkAnnotator.Annotate(hunks, ltMatches, _dictionary);
            WritingSuggestionAnnotator.MarkStyleOnlyHunks(hunks, correctnessHunks);
        }

        // If dictionary filtering removed everything but text still differs, keep a single Other hunk.
        if (hunks.Count == 0 && !string.Equals(original, result, StringComparison.Ordinal))
        {
            hunks.Add(new CorrectionHunk
            {
                StartPos = 0,
                OldText = original,
                NewText = result,
                Category = SuggestionCategory.Other,
                CategoryLabel = "Correct your writing"
            });
        }

        Report(progress, "Done", 1.0);
        return new CorrectionResult(original, result, hunks, detectedLanguage);
    }

    private static void Report(IProgress<CorrectionStage>? progress, string message, double fraction) =>
        progress?.Report(new CorrectionStage(message, fraction));

    private async Task<string> RunWritingSuggestionsAsync(
        string text,
        CorrectionOptions options,
        CancellationToken ct)
    {
        // Fail closed without a key — correctness result already computed.
        if (string.IsNullOrWhiteSpace(options.OpenAiApiKey) || string.IsNullOrWhiteSpace(text))
            return text;

        var instruction = string.IsNullOrWhiteSpace(options.WritingInstruction)
            ? CorrectionOptions.DefaultWritingInstruction
            : options.WritingInstruction;

        return await _chatGpt.CorrectAsync(
                text,
                options.OpenAiApiKey,
                options.OpenAiModel,
                instruction,
                options.LlmProvider,
                options.LlmBaseUrl,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<(string Text, IReadOnlyList<LanguageToolMatch> Matches, string Source, string? Detected)>
        RunLanguageToolAsync(string text, CorrectionOptions options, CancellationToken ct)
    {
        var source = text;
        var lt = await _languageTool.CheckAsync(
                text,
                options.Language,
                _dictionary.Snapshot(),
                ct)
            .ConfigureAwait(false);
        var corrected = string.IsNullOrEmpty(lt.CorrectedText) ? text : lt.CorrectedText;
        return (corrected, lt.Matches, source, lt.DetectedLanguageCode);
    }

    private async Task<string> RunChatGptAsync(string text, CorrectionOptions options, CancellationToken ct)
    {
        var key = options.OpenAiApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return text;

        return await _chatGpt.CorrectAsync(
                text,
                key,
                options.OpenAiModel,
                options.ChatGptInstruction,
                options.LlmProvider,
                options.LlmBaseUrl,
                ct)
            .ConfigureAwait(false);
    }

    private void EnsureLocalRules(string? path)
    {
        var resolved = path ?? DataPaths.FindDictationLocalRules();
        if (resolved is null)
        {
            if (!string.Equals(_loadedRulesPath, "", StringComparison.Ordinal))
            {
                _localRules.Clear();
                _loadedRulesPath = "";
            }
            return;
        }

        if (string.Equals(_loadedRulesPath, resolved, StringComparison.OrdinalIgnoreCase) &&
            _localRules.Loaded)
            return;

        _localRules.LoadFromFile(resolved);
        _loadedRulesPath = resolved;
    }

    public void Dispose()
    {
        _languageTool.Dispose();
        _chatGpt.Dispose();
    }
}
