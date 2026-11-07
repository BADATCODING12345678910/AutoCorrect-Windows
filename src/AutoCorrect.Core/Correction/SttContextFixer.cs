using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Correction;

/// <summary>
/// Speech-to-text context fixes Windows Voice Typing often misses.
/// Classic: user says <c>and</c>, VT writes <c>in</c>; or says <c>or</c>, VT writes <c>where</c>.
/// These run in the normal Fix pipeline (local pass) — not a separate Voice Typing mode.
/// </summary>
public static partial class SttContextFixer
{
    /// <summary>Fixed phrases / binomials where VT often writes <c>in</c> for <c>and</c>.</summary>
    private static readonly (string Find, string Replace)[] FixedPhrases =
    [
        ("back in forth", "back and forth"),
        ("to in fro", "to and fro"),
        ("here in there", "here and there"),
        ("this in that", "this and that"),
        ("now in then", "now and then"),
        ("by in large", "by and large"),
        ("wait in see", "wait and see"),
        ("try in see", "try and see"),
        ("come in get", "come and get"),
        ("go in get", "go and get"),
        ("go in find", "go and find"),
        ("go in buy", "go and buy"),
        ("come in see", "come and see"),
        ("sit in wait", "sit and wait"),
        ("stop in think", "stop and think"),
        ("stop in check", "stop and check"),
        ("up in leave", "up and leave"),
        ("up in go", "up and go"),
        ("up in running", "up and running"),
        ("alive in well", "alive and well"),
        ("sick in tired", "sick and tired"),
        ("tried in true", "tried and true"),
        ("nice in easy", "nice and easy"),
        ("safe in sound", "safe and sound"),
        ("peace in quiet", "peace and quiet"),
        ("bread in butter", "bread and butter"),
        ("fish in chips", "fish and chips"),
        ("rock in roll", "rock and roll"),
        ("pros in cons", "pros and cons"),
        ("odds in ends", "odds and ends"),
        ("bits in pieces", "bits and pieces"),
        ("cats in dogs", "cats and dogs"),
        ("hue in cry", "hue and cry"),
        ("law in order", "law and order"),
        ("research in development", "research and development"),
        ("black in white", "black and white"),
        ("day in night", "day and night"),
        ("yes in no", "yes and no"),
    ];

    /// <summary>User said <c>or</c>, VT wrote <c>where</c> — high-confidence phrase pairs.</summary>
    private static readonly (string Find, string Replace)[] OrWherePhrases =
    [
        ("this where that", "this or that"),
        ("these where those", "these or those"),
        ("one where the other", "one or the other"),
        ("yes where no", "yes or no"),
        ("true where false", "true or false"),
        ("black where white", "black or white"),
        ("sooner where later", "sooner or later"),
        ("more where less", "more or less"),
        ("now where never", "now or never"),
        ("sink where swim", "sink or swim"),
        ("do where die", "do or die"),
        ("take it where leave it", "take it or leave it"),
        ("make where break", "make or break"),
        ("life where death", "life or death"),
        ("all where nothing", "all or nothing"),
        ("rain where shine", "rain or shine"),
        ("heads where tails", "heads or tails"),
        ("win where lose", "win or lose"),
        ("pass where fail", "pass or fail"),
        ("and/where", "and/or"),
    ];

    /// <summary>
    /// Left verbs that commonly pair as <c>VERB and VERB</c> (not <c>VERB in …</c>).
    /// </summary>
    private static readonly HashSet<string> ConjunctionVerbLeft = new(StringComparer.OrdinalIgnoreCase)
    {
        "go", "goes", "went", "going",
        "come", "comes", "came", "coming",
        "sit", "sits", "sat", "sitting",
        "stand", "stands", "stood", "standing",
        "stop", "stops", "stopped", "stopping",
        "wait", "waits", "waited", "waiting",
        "try", "tries", "tried", "trying",
        "run", "runs", "ran", "running",
        "walk", "walks", "walked", "walking",
        "turn", "turns", "turned", "turning",
        "get", "gets", "got", "getting",
        "up", // up and leave / up and go
    };

    /// <summary>Right verbs after those left verbs → prefer <c>and</c>.</summary>
    private static readonly HashSet<string> ConjunctionVerbRight = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "gets", "got", "getting",
        "buy", "buys", "bought", "buying",
        "find", "finds", "found", "finding",
        "see", "sees", "saw", "seeing",
        "check", "checks", "checked", "checking",
        "think", "thinks", "thought", "thinking",
        "wait", "waits", "waited", "waiting",
        "leave", "leaves", "left", "leaving",
        "go", "goes", "went", "going",
        "say", "says", "said", "saying",
        "tell", "tells", "told", "telling",
        "ask", "asks", "asked", "asking",
        "look", "looks", "looked", "looking",
        "watch", "watches", "watched", "watching",
        "listen", "listens", "listened", "listening",
        "read", "reads", "reading",
        "write", "writes", "wrote", "writing",
        "type", "types", "typed", "typing",
        "fix", "fixes", "fixed", "fixing",
        "make", "makes", "made", "making",
        "take", "takes", "took", "taking",
        "give", "gives", "gave", "giving",
        "pick", "picks", "picked", "picking",
        "grab", "grabs", "grabbed", "grabbing",
        "open", "opens", "opened", "opening",
        "close", "closes", "closed", "closing",
        "start", "starts", "started", "starting",
        "finish", "finishes", "finished", "finishing",
        "help", "helps", "helped", "helping",
        "do", "does", "did", "doing",
        "be", "am", "is", "are", "was", "were", "been", "being",
        "running", // up and running
    };

    /// <summary>Right side where <c>in</c> is almost certainly the real preposition — never flip.</summary>
    private static readonly HashSet<string> RealInRight = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "my", "your", "his", "her", "our", "their", "its",
        "this", "that", "these", "those",
        "fact", "order", "addition", "front", "spite", "case", "charge", "control",
        "time", "advance", "general", "particular", "short", "total", "public", "private",
        "progress", "practice", "theory", "reality", "effect", "turn", "return",
        "comparison", "contrast", "favor", "favour", "lieu", "light", "view", "mind",
        "memory", "stock", "store", "situ", "sync", "parallel", "series", "sequence",
        "line", "place", "position", "location", "room", "bed", "school", "college",
        "university", "class", "office", "town", "city", "country",
        "english", "french", "spanish", "german", "code", "python", "javascript",
        "cursor", "word", "excel", "notepad", "browser", "chrome", "edge",
        "windows", "linux", "macos", "teams", "outlook", "slack", "discord",
        "here", "there", "between", "among", "amongst", "love", "pain", "trouble",
        "danger", "debt", "need", "use", "vain", "earnest", "detail", "depth",
        "minutes", "hours", "seconds", "days", "weeks", "months", "years",
        "while", "hindsight", "retrospect", "future", "past", "present",
        "question", "doubt", "error", "mistake", "bug", "issue",
        "writing", "typing", "speaking", "reading", "learning", "teaching",
    };

    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var t = text;
        foreach (var (find, repl) in FixedPhrases)
            t = LocalRulesEngine.ReplaceInsensitivePreserveCase(t, find, repl);

        foreach (var (find, repl) in OrWherePhrases)
            t = LocalRulesEngine.ReplaceInsensitivePreserveCase(t, find, repl);

        return InToken().Replace(t, MatchEvaluator);
    }

    private static string MatchEvaluator(Match m)
    {
        var left = m.Groups["left"].Value;
        var inWord = m.Groups["in"].Value;
        var space = m.Groups["sp"].Value;
        var right = m.Groups["right"].Value;

        if (left.Length == 0)
            return m.Value;

        // Never steal real prepositional "in the/a/…"
        if (RealInRight.Contains(right))
            return m.Value;

        if (!ConjunctionVerbLeft.Contains(left))
            return m.Value;

        if (!ConjunctionVerbRight.Contains(right))
            return m.Value;

        var and = PreserveCase(inWord, "and");
        return left + m.Groups["mid"].Value + and + space + right;
    }

    private static string PreserveCase(string matchedIn, string replacement)
    {
        if (matchedIn.Length > 0 && matchedIn.All(char.IsUpper))
            return replacement.ToUpperInvariant();
        if (matchedIn.Length > 0 && char.IsUpper(matchedIn[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }

    [GeneratedRegex(
        @"(?ix)
        \b(?<left>[A-Za-z][\w']*)(?<mid>\s+)(?<in>In|IN|in)(?<sp>\s+)(?<right>[A-Za-z][\w'-]*)")]
    private static partial Regex InToken();
}
