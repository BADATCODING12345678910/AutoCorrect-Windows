using AutoCorrect.Core;
using AutoCorrect.Core.Autocorrect;
using AutoCorrect.Core.Correction;
using AutoCorrect.Core.Settings;

var sample =
    "so i went too the store yestiday and byed sum milk but then i realized i all ready had milk at home so i was like oh know thats funny their was also a long line";

Console.WriteLine("Rules: " + (DataPaths.FindDictationLocalRules() ?? "(none)"));
Console.WriteLine("ApiKey: " + (DataPaths.FindPersonalApiKey() ?? "(none)"));
Console.WriteLine("Curated: " + (DataPaths.FindAutocorrectCurated() ?? "(none)"));
Console.WriteLine("Input:  " + sample);
Console.WriteLine();

// —— Phase A1: autocorrect matcher smoke ——
var ac = new AutocorrectEngine();
ac.Configure(new AutocorrectSettings { UseLearned = false }); // isolate from user learned store
var acOk = 0;
var acFail = 0;
void Expect(string word, string? expected, string? process = null)
{
    var hit = ac.TryMatch(word, process);
    var got = hit?.CasedReplacement;
    var pass = string.Equals(got, expected, StringComparison.Ordinal);
    if (pass) acOk++;
    else
    {
        acFail++;
        Console.WriteLine($"  FAIL match '{word}' → got '{got ?? "(null)"}' expected '{expected ?? "(null)"}'");
    }
}
Expect("teh", "the");
Expect("thd", "the"); // fat-finger: D under E
Expect("THD", "THE");
Expect("Teh", "The");
Expect("TEH", "THE");
Expect("adn", "and");
Expect("yestiday", "yesterday");
Expect("the", null); // already correct / no mapping
Expect("teh", null, "AutoCorrect.App"); // excluded process
ac.Configure(new AutocorrectSettings { Enabled = true, UseCurated = false, UseLearned = false, UseImportedLibrary = false, UseDictionaryDistance = false });
Expect("teh", null); // all sources off
ac.Configure(new AutocorrectSettings { UseLearned = false }); // restore
Console.WriteLine($"Autocorrect A1: {ac.CuratedCount} curated pairs; matcher {acOk} ok, {acFail} fail");
if (ac.CuratedCount == 0)
    Console.WriteLine("Autocorrect A1: WARN — curated file not found (check Data/AutocorrectCurated.txt)");
else if (acFail == 0)
    Console.WriteLine("Autocorrect A1: OK");
else
    Console.WriteLine("Autocorrect A1: FAIL");
Console.WriteLine();

// —— Phase A3: AHK import ——
Console.WriteLine("AHK lib: " + (DataPaths.FindAhkHotstringsLibrary() ?? "(none)"));
var importedFile = AhkHotstringImporter.EnsureImported(force: true);
Console.WriteLine($"Autocorrect A3: imported {importedFile.Entries.Count} entries → {DataPaths.FindOrCreateAutocorrectImportedPath()}");
ac.Configure(new AutocorrectSettings
{
    Enabled = true,
    UseCurated = false,
    UseLearned = false,
    UseImportedLibrary = true,
    UseDictionaryDistance = false, // isolate library behavior
    Strictness = "strict"
});
var a3Ok = 0;
var a3Fail = 0;
void ExpectImport(string word, string? expected)
{
    var hit = ac.TryMatch(word);
    var got = hit?.CasedReplacement;
    var pass = string.Equals(got, expected, StringComparison.Ordinal);
    if (pass) a3Ok++;
    else
    {
        a3Fail++;
        Console.WriteLine($"  FAIL import '{word}' → got '{got ?? "(null)"}' expected '{expected ?? "(null)"}'");
    }
}
// Strict: classic non-? entry
ExpectImport("occurence", "occurrence");
// Strict: teh is :B0X?C in AHK → skipped under Strict
ExpectImport("teh", null);
// Balanced: includes ? exact words + *? shapes (+ dictionary guard for mid-word)
ac.Configure(new AutocorrectSettings
{
    Enabled = true,
    UseCurated = false,
    UseLearned = false,
    UseImportedLibrary = true,
    UseDictionaryDistance = true,
    Strictness = "balanced"
});
ExpectImport("teh", "the");
ExpectImport("abotu", "about");
// Mid-word "?" suffix (Balanced): …adn → …and; nullifiers + dictionary guard block real words
ExpectImport("badn", "band");
ExpectImport("sadn", "sand");
ExpectImport("scaly", null);   // nullifier :B0:scaly::
ExpectImport("sign", null);    // nullifier :B0:sign::
ExpectImport("design", null);  // nullifier :B0?:design::
ExpectImport("align", null);   // nullifier :B0?:align::
// Strict: no "?" exact or suffix
ac.Configure(new AutocorrectSettings
{
    Enabled = true,
    UseCurated = false,
    UseLearned = false,
    UseImportedLibrary = true,
    UseDictionaryDistance = false,
    Strictness = "strict"
});
ExpectImport("badn", null);
ExpectImport("teh", null);
Console.WriteLine(
    $"Autocorrect A3: engine loaded {ac.ImportedCount}; nullifiers {importedFile.Nullifiers.Count}; " +
    $"matcher {a3Ok} ok, {a3Fail} fail");
if (importedFile.Entries.Count == 0)
    Console.WriteLine("Autocorrect A3: WARN — no entries (AHK path missing?)");
else if (importedFile.Nullifiers.Count == 0)
    Console.WriteLine("Autocorrect A3: WARN — no nullifiers imported (mid-word unsafe?)");
else if (a3Fail == 0)
    Console.WriteLine("Autocorrect A3: OK (exact + mid-word/nullifiers)");
else
    Console.WriteLine("Autocorrect A3: FAIL");
Console.WriteLine();
ac.Configure(new AutocorrectSettings()); // restore defaults

// —— Per-app profiles ——
var profileOk = 0;
var profileFail = 0;
void ExpectProfile(string word, string? process, string? expected)
{
    var hit = ac.TryMatch(word, process);
    var got = hit?.CasedReplacement;
    var pass = string.Equals(got, expected, StringComparison.Ordinal);
    if (pass) profileOk++;
    else
    {
        profileFail++;
        Console.WriteLine($"  FAIL profile '{word}'@{process} → got '{got ?? "(null)"}' expected '{expected ?? "(null)"}'");
    }
}
ac.Configure(new AutocorrectSettings
{
    Enabled = true,
    UseCurated = true,
    UseLearned = false,
    UseImportedLibrary = false,
    UseDictionaryDistance = false,
    ExcludedProcesses = "AutoCorrect.App",
    AppProfiles =
    [
        new AppProfile { Process = "Code", Autocorrect = false, Fix = true },
        new AppProfile { Process = "notepad", Strictness = "strict", UseImportedLibrary = true },
        new AppProfile { Process = "game", Fix = false }
    ]
});
ExpectProfile("teh", "Code", null);          // profile autocorrect off
ExpectProfile("teh", "notepad", "the");      // curated still on
ExpectProfile("teh", "slack", "the");        // no profile → global
if (!ac.IsFixEnabledForProcess("game")) profileOk++;
else { profileFail++; Console.WriteLine("  FAIL Fix should be off for game"); }
if (ac.IsFixEnabledForProcess("Code")) profileOk++;
else { profileFail++; Console.WriteLine("  FAIL Fix should stay on for Code"); }
var parsed = AppProfileStore.Parse("discord, off\n# comment\nslack: strictness=strict, dictionary=off");
if (parsed.Count == 2 &&
    parsed[0].Autocorrect == false &&
    parsed[1].Strictness == "strict" &&
    parsed[1].UseDictionaryDistance == false)
    profileOk++;
else
{
    profileFail++;
    Console.WriteLine("  FAIL AppProfileStore.Parse");
}
Console.WriteLine($"Per-app profiles: {profileOk} ok, {profileFail} fail");
Console.WriteLine(profileFail == 0 ? "Per-app profiles: OK" : "Per-app profiles: FAIL");
Console.WriteLine();
ac.Configure(new AutocorrectSettings { UseLearned = false });

// —— Phase A4: dictionary unique 1-edit ——
Console.WriteLine("Wordlist: " + (DataPaths.FindWordlist() ?? "(none)"));
ac.Configure(new AutocorrectSettings
{
    Enabled = true,
    UseCurated = false,
    UseLearned = false,
    UseImportedLibrary = false,
    UseDictionaryDistance = true,
    Strictness = "strict"
});
var a4Ok = 0;
var a4Fail = 0;
void ExpectDict(string word, string? expected)
{
    var hit = ac.TryMatch(word);
    var got = hit?.CasedReplacement;
    var pass = string.Equals(got, expected, StringComparison.Ordinal);
    if (pass) a4Ok++;
    else
    {
        a4Fail++;
        Console.WriteLine($"  FAIL dict '{word}' → got '{got ?? "(null)"}' expected '{expected ?? "(null)"}'");
    }
}
ExpectDict("accomodate", "accommodate"); // unique 1-edit
ExpectDict("seperate", "separate");
ExpectDict("definately", "definitely");
ExpectDict("thd", "the"); // many 1-edit neighbors, but unique keyboard-adjacent (d↔e)
// Frequency tie-break may pick a unique best among multi-neighbor typos — assert it stays a real word.
{
    var hit = ac.TryMatch("recieve");
    if (hit is null)
        a4Ok++;
    else if (string.Equals(hit.CasedReplacement, "receive", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(hit.CasedReplacement, "relieve", StringComparison.OrdinalIgnoreCase))
        a4Ok++;
    else
    {
        a4Fail++;
        Console.WriteLine($"  FAIL dict 'recieve' → got '{hit.CasedReplacement}' expected receive|relieve|null");
    }
}
ExpectDict("receive", null); // already in dictionary
var a4DictCount = ac.DictionaryCount;
Console.WriteLine($"Autocorrect A4: {a4DictCount} words; matcher {a4Ok} ok, {a4Fail} fail");
if (a4DictCount == 0)
    Console.WriteLine("Autocorrect A4: WARN — wordlist not loaded");
else if (a4Fail == 0)
    Console.WriteLine("Autocorrect A4: OK");
else
    Console.WriteLine("Autocorrect A4: FAIL");
Console.WriteLine();
ac.Configure(new AutocorrectSettings());

// —— Phase B: writing-suggestion annotator (no API required) ——
var correctnessOnly = new List<CorrectionHunk>
{
    new() { StartPos = 0, OldText = "teh", NewText = "the", Category = SuggestionCategory.Spelling, CategoryLabel = "Correct your spelling" }
};
var mixedHunks = new List<CorrectionHunk>
{
    new() { StartPos = 0, OldText = "teh", NewText = "the", Category = SuggestionCategory.Spelling, CategoryLabel = "Correct your spelling" },
    new() { StartPos = 10, OldText = "due to the fact that", NewText = "because", Category = SuggestionCategory.Other, CategoryLabel = "x" }
};
WritingSuggestionAnnotator.MarkStyleOnlyHunks(mixedHunks, correctnessOnly);
var writingAnnotatorOk =
    mixedHunks[0].Category == SuggestionCategory.Spelling &&
    mixedHunks[1].Category == SuggestionCategory.Clarity &&
    mixedHunks[1].CategoryLabel == "Make concise";
Console.WriteLine(writingAnnotatorOk
    ? "Writing suggestions B (annotator): OK"
    : "Writing suggestions B (annotator): FAIL");
Console.WriteLine();

// —— Phase D: rewrite client contract (no network) ——
var rewriteDefaultsOk =
    !string.IsNullOrWhiteSpace(ChatGptCorrectionClient.DefaultRewriteSystem) &&
    new AppSettings().General.RewriteHotkey.Equals("Win+Alt+R", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(rewriteDefaultsOk
    ? "Selection rewrite D (defaults): OK"
    : "Selection rewrite D (defaults): FAIL");
Console.WriteLine();

// —— AutoLearn: service filters + learned store round-trip ——
{
    var learnedPath = Path.Combine(Path.GetTempPath(), "ac-learned-smoke-" + Guid.NewGuid().ToString("n") + ".json");
    var learned = new LearnedStore();
    learned.LoadFromFile(learnedPath);
    var learn = new AutoLearnService();
    learn.Configure(new AutoLearnSettings { Enabled = true, LearnMode = "silent", MinOccurrences = 2 }, learned);
    learn.RecordOccurrence("teh", "the");
    learn.RecordOccurrence("teh", "the"); // should promote silent
    var pending = learn.DequeuePending();
    var learnOk = pending is not null && pending.Trigger == "teh" && pending.Replacement == "the";
    if (learnOk && learn.Accept(pending!))
        learnOk = learned.TryGet("teh", out var r) && r == "the";
    try { File.Delete(learnedPath); } catch { /* ignore */ }
    Console.WriteLine(learnOk ? "AutoLearn: OK" : "AutoLearn: FAIL");
    Console.WriteLine();
}

// —— STT context: user said "and", VT wrote "in" ——
var sttOk = 0;
var sttFail = 0;
void ExpectStt(string input, string expected)
{
    var got = SttContextFixer.Apply(input);
    if (string.Equals(got, expected, StringComparison.Ordinal))
        sttOk++;
    else
    {
        sttFail++;
        Console.WriteLine($"  FAIL STT '{input}' → got '{got}' expected '{expected}'");
    }
}
ExpectStt("go in get milk", "go and get milk");
ExpectStt("wait in see", "wait and see");
ExpectStt("back in forth", "back and forth");
ExpectStt("sit in wait", "sit and wait");
ExpectStt("up in running", "up and running");
ExpectStt("put in the box", "put in the box"); // real preposition — keep
ExpectStt("in fact I agree", "in fact I agree"); // real "in fact" — keep
ExpectStt("this where that", "this or that");
ExpectStt("yes where no", "yes or no");
ExpectStt("sooner where later", "sooner or later");
ExpectStt("where the cat is", "where the cat is"); // real where — keep
Console.WriteLine(sttFail == 0
    ? $"STT context (and←in / or←where): OK ({sttOk})"
    : $"STT context: FAIL ({sttFail} failed)");
Console.WriteLine();

// —— Contextual accidents (spaces / commas / glued sentences) ——
var ctxOk = 0;
var ctxFail = 0;
void ExpectCtx(string input, string expected, string label)
{
    var got = ContextualAccidentFixer.Apply(input);
    if (string.Equals(got, expected, StringComparison.Ordinal))
        ctxOk++;
    else
    {
        ctxFail++;
        Console.WriteLine($"  FAIL ctx [{label}] '{input}' → got '{got}' expected '{expected}'");
    }
}
ExpectCtx("hello  world", "hello world", "mid double space");
ExpectCtx("one  two  three", "one two three", "multi doubles");
ExpectCtx(
    "First.  Second.  Third.",
    "First.  Second.  Third.",
    "keep double-after-sentence style");
ExpectCtx("Hi,, there", "Hi, there", "double comma");
ExpectCtx("end.Start", "end. Start", "glued sentence");
ExpectCtx("wait.. please", "wait. please", "orphan double period");
ExpectCtx("wait... please", "wait... please", "keep ellipsis");
ExpectCtx("a , b", "a, b", "space before comma");
Console.WriteLine(ctxFail == 0
    ? $"Contextual accidents: OK ({ctxOk})"
    : $"Contextual accidents: FAIL ({ctxFail} failed)");
Console.WriteLine();

// —— Fix quality → provider mapping (no network) ——
var fqOk = 0;
var fqFail = 0;
void ExpectFq(string quality, bool premium, bool key, string wantProvider, string wantOrder)
{
    var (p, o, _, _) = FixQualityResolver.Resolve(quality, "languagetool", "chatgpt_then_languagetool", premium, key);
    if (p == wantProvider && o == wantOrder) fqOk++;
    else
    {
        fqFail++;
        Console.WriteLine($"  FAIL FixQuality '{quality}' premium={premium} key={key} → {p}/{o} expected {wantProvider}/{wantOrder}");
    }
}
ExpectFq("perfect", true, true, "both", "languagetool_then_chatgpt");
ExpectFq("perfect", true, false, "languagetool", "chatgpt_then_languagetool");
ExpectFq("perfect", false, true, "languagetool", "chatgpt_then_languagetool");
ExpectFq("standard", true, true, "languagetool", "chatgpt_then_languagetool");
Console.WriteLine(fqFail == 0
    ? $"FixQuality resolver: OK ({fqOk})"
    : $"FixQuality resolver: FAIL ({fqFail} failed)");
Console.WriteLine();

using var pipeline = new CorrectionPipeline();
var result = await pipeline.CorrectAsync(sample, new CorrectionOptions
{
    Provider = "languagetool",
    LocalRulesPath = DataPaths.FindDictationLocalRules()
});

Console.WriteLine("Output: " + result.Suggestion);
var atomic = WordDiff.ExplodeToAtomicHunks(result.Hunks);
var queued = WordDiff.MergeNearbyHunks(atomic, result.Original);
Console.WriteLine($"Hunks:  {result.Hunks.Count} (atomic: {atomic.Count}, nearby-merged queue: {queued.Count})");
foreach (var h in queued.Take(16))
    Console.WriteLine($"  - [{h.Category}] {h.CategoryLabel}: {h.Label}");

// Prove one apply only changes one span.
if (queued.Count > 1)
{
    var once = queued[0].ApplyTo(result.Original);
    var changed = once != result.Original && once != result.Suggestion;
    Console.WriteLine(changed
        ? "Apply-one check: OK (first hunk ≠ full rewrite)"
        : "Apply-one check: WARN (first apply matched original or full suggestion)");

    // Safe Accept-all: surgical multi-hunk apply must not equal dumping Suggestion blindly
    // when guards would skip (here we just verify sequential TryApply stays locatable).
    var batchText = result.Original;
    var batchApplied = 0;
    foreach (var h in WordDiff.ExplodeToAtomicHunks(result.Hunks).OrderBy(h => h.StartPos))
    {
        if (!h.TryApply(batchText, out var next, out _, out _))
            continue;
        batchText = next;
        batchApplied++;
    }
    Console.WriteLine(batchApplied > 1
        ? $"Accept-all safe batch: OK ({batchApplied} hunks applied surgically)"
        : "Accept-all safe batch: WARN (fewer than 2 surgical applies)");
}

// Optional ChatGPT smoke — only with a real ApiKey (never required for CI).
var creds = PersonalApiKeyStore.TryLoad();
if (!string.IsNullOrWhiteSpace(creds?.ApiKey))
{
    Console.WriteLine();
    Console.WriteLine("ChatGPT smoke (key present)…");
    var cg = await pipeline.CorrectAsync(sample, new CorrectionOptions
    {
        Provider = "chatgpt",
        UseLocalRules = false,
        UsePunctuation = false,
        UseLightPolish = false,
        OpenAiApiKey = creds.ApiKey,
        OpenAiModel = creds.Model ?? "gpt-4o-mini"
    });
    Console.WriteLine("ChatGPT: " + cg.Suggestion);
    Console.WriteLine(cg.HasChanges || !string.Equals(cg.Suggestion, sample, StringComparison.Ordinal)
        ? "ChatGPT smoke: OK"
        : "ChatGPT smoke: WARN (unchanged — key/model/network may have failed closed)");
}
else
{
    Console.WriteLine();
    Console.WriteLine("ChatGPT smoke: skipped (no usable ApiKey in PersonalApiKey.ini)");
}

// —— Latency budget: local TryMatch should stay well under 100ms ——
Console.WriteLine();
ac.Configure(new AutocorrectSettings { UseLearned = false });
var warmWords = new[] { "teh", "the", "adn", "yestiday", "design", "scaly", "xxxxz", "thier" };
foreach (var w in warmWords)
    _ = ac.TryMatch(w);
var sw = System.Diagnostics.Stopwatch.StartNew();
const int iters = 200;
for (var i = 0; i < iters; i++)
{
    foreach (var w in warmWords)
        _ = ac.TryMatch(w);
}
sw.Stop();
var perMatchMs = sw.Elapsed.TotalMilliseconds / (iters * warmWords.Length);
Console.WriteLine($"Latency: avg TryMatch {perMatchMs:0.00} ms (budget local match ≪ 100ms)");
var latencyOk = perMatchMs < 5.0; // match alone; inject settle is separate
Console.WriteLine(latencyOk ? "Latency: OK" : "Latency: FAIL (TryMatch too slow)");

var exit = 0;
if (!result.HasChanges) exit = 1;
if (acFail > 0 || ac.CuratedCount == 0) exit = 1;
if (a3Fail > 0 || importedFile.Entries.Count == 0 || importedFile.Nullifiers.Count == 0) exit = 1;
if (profileFail > 0) exit = 1;
if (a4Fail > 0 || a4DictCount == 0) exit = 1;
if (!writingAnnotatorOk) exit = 1;
if (sttFail > 0) exit = 1;
if (fqFail > 0) exit = 1;
if (!latencyOk) exit = 1;
return exit;
