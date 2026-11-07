namespace AutoCorrect.Core.Correction;

/// <summary>Token LCS diff — port of DictationFixer Tokenize / ComputeHunks / BuildSuggestionFromHunks.</summary>
public static class WordDiff
{
    public static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            var ch = s[i];
            if (char.IsWhiteSpace(ch))
            {
                var j = i;
                while (j < s.Length && char.IsWhiteSpace(s[j]))
                    j++;
                tokens.Add(s[i..j]);
                i = j;
            }
            else if (char.IsLetterOrDigit(ch) || ch == '\'')
            {
                var j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '\''))
                    j++;
                tokens.Add(s[i..j]);
                i = j;
            }
            else
            {
                tokens.Add(ch.ToString());
                i++;
            }
        }
        return tokens;
    }

    public static List<CorrectionHunk> ComputeHunks(string original, string suggestion)
    {
        var raw = ComputeHunksCore(original, suggestion, allowGiantFallback: true);
        var atomic = ExplodeToAtomicHunks(raw);
        return MergeNearbyHunks(atomic, original);
    }

    /// <summary>
    /// Ensure each queue item is one mistake: re-diff wide hunks and drop shared prefix/suffix.
    /// </summary>
    public static List<CorrectionHunk> ExplodeToAtomicHunks(IEnumerable<CorrectionHunk> hunks)
    {
        var result = new List<CorrectionHunk>();
        foreach (var h in hunks)
            result.AddRange(ExplodeOne(h, depth: 0));
        return result.Where(x => x.OldText != x.NewText).ToList();
    }

    /// <summary>
    /// Group mistakes that sit almost on top of each other into one queue card
    /// (e.g. “i all ready” → “I already”), without gluing distant edits.
    /// </summary>
    public static List<CorrectionHunk> MergeNearbyHunks(
        IReadOnlyList<CorrectionHunk> hunks,
        string original,
        int maxGapChars = 2,
        int maxMergedSpanChars = 40,
        int maxMergedWords = 4)
    {
        if (hunks.Count <= 1)
            return hunks.ToList();

        var ordered = hunks.OrderBy(h => h.StartPos).ToList();
        var merged = new List<CorrectionHunk>();
        var group = new List<CorrectionHunk> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = group[^1];
            var next = ordered[i];
            var prevEnd = prev.StartPos + prev.OldText.Length;
            var gap = next.StartPos - prevEnd;
            var spanStart = group[0].StartPos;
            var spanEnd = next.StartPos + next.OldText.Length;
            var spanLen = spanEnd - spanStart;
            var wordCount = CountWordTokens(original.Length >= spanEnd && spanStart >= 0
                ? original[spanStart..Math.Min(spanEnd, original.Length)]
                : prev.OldText + next.OldText);

            var gapTextOk = gap >= 0 && gap <= maxGapChars;
            if (gapTextOk && gap > 0 && original.Length >= next.StartPos)
            {
                var between = original[prevEnd..next.StartPos];
                // Only glue across tiny whitespace / punctuation bridges.
                gapTextOk = between.All(c => char.IsWhiteSpace(c) || c is '-' or '\'' or ',');
            }

            if (gapTextOk && spanLen <= maxMergedSpanChars && wordCount <= maxMergedWords)
            {
                group.Add(next);
                continue;
            }

            merged.Add(CollapseGroup(group, original));
            group = [next];
        }

        if (group.Count > 0)
            merged.Add(CollapseGroup(group, original));

        return merged;
    }

    private static CorrectionHunk CollapseGroup(List<CorrectionHunk> group, string original)
    {
        if (group.Count == 1)
            return group[0];

        var start = group[0].StartPos;
        var end = group[^1].StartPos + group[^1].OldText.Length;
        start = Math.Clamp(start, 0, original.Length);
        end = Math.Clamp(end, start, original.Length);
        var oldText = original[start..end];

        // Build the replacement for just this span.
        var sb = oldText;
        // Apply from end so earlier offsets in the span stay valid.
        for (var i = group.Count - 1; i >= 0; i--)
        {
            var h = group[i];
            var local = h.StartPos - start;
            if (local < 0 || local + h.OldText.Length > sb.Length)
                continue;
            if (h.OldText.Length > 0 &&
                !string.Equals(sb.Substring(local, h.OldText.Length), h.OldText, StringComparison.Ordinal))
                continue;
            sb = sb[..local] + h.NewText + sb[(local + h.OldText.Length)..];
        }

        var first = group[0];
        return new CorrectionHunk
        {
            StartPos = start,
            OldText = oldText,
            NewText = sb,
            Selected = true,
            Category = first.Category,
            CategoryLabel = first.CategoryLabel,
            ShortMessage = first.ShortMessage,
            RuleId = first.RuleId
        };
    }

    private static List<CorrectionHunk> ExplodeOne(CorrectionHunk hunk, int depth)
    {
        var tight = TightenHunk(hunk);
        if (depth > 4)
            return [tight];

        var oldWords = CountWordTokens(tight.OldText);
        var newWords = CountWordTokens(tight.NewText);

        // Single-token (or empty side) edits are already atomic.
        if (oldWords <= 1 && newWords <= 1)
            return [tight];

        // Multi-word span: re-diff so "yestiday and byed" becomes two queue items.
        var sub = ComputeHunksCore(tight.OldText, tight.NewText, allowGiantFallback: false);
        if (sub.Count <= 1)
            return [tight];

        var expanded = new List<CorrectionHunk>();
        foreach (var s in sub)
        {
            var child = new CorrectionHunk
            {
                StartPos = tight.StartPos + s.StartPos,
                OldText = s.OldText,
                NewText = s.NewText,
                Selected = tight.Selected,
                Category = tight.Category,
                CategoryLabel = tight.CategoryLabel,
                ShortMessage = tight.ShortMessage,
                RuleId = tight.RuleId
            };
            expanded.AddRange(ExplodeOne(child, depth + 1));
        }
        return expanded;
    }

    private static int CountWordTokens(string s) =>
        Tokenize(s).Count(t => t.Length > 0 && (char.IsLetterOrDigit(t[0]) || t[0] == '\''));

    private static List<CorrectionHunk> ComputeHunksCore(
        string original,
        string suggestion,
        bool allowGiantFallback)
    {
        var hunks = new List<CorrectionHunk>();
        if (original == suggestion)
            return hunks;

        var ta = Tokenize(original);
        var tb = Tokenize(suggestion);
        var n = ta.Count;
        var m = tb.Count;

        if (n == 0 || m == 0)
        {
            hunks.Add(new CorrectionHunk { StartPos = 0, OldText = original, NewText = suggestion });
            return hunks;
        }

        // Large docs: chunked LCS instead of one whole-field hunk (that made Apply fix everything).
        if (n > 200 || m > 200 || (long)n * m > 30000)
        {
            if (!allowGiantFallback)
            {
                hunks.Add(new CorrectionHunk { StartPos = 0, OldText = original, NewText = suggestion });
                return hunks;
            }
            return ComputeHunksChunked(original, suggestion, ta, tb);
        }

        return BuildHunksFromLcs(original, ta, tb);
    }

    /// <summary>Split long token streams into windows so we never return one mega-hunk.</summary>
    private static List<CorrectionHunk> ComputeHunksChunked(
        string original,
        string suggestion,
        List<string> ta,
        List<string> tb)
    {
        const int window = 120;
        var hunks = new List<CorrectionHunk>();
        var i = 0;
        var j = 0;
        var origPos = 0;
        var sugPos = 0;

        while (i < ta.Count || j < tb.Count)
        {
            // Consume equal prefix greedily.
            while (i < ta.Count && j < tb.Count && ta[i] == tb[j])
            {
                origPos += ta[i].Length;
                sugPos += tb[j].Length;
                i++;
                j++;
            }

            if (i >= ta.Count && j >= tb.Count)
                break;

            var iEnd = Math.Min(ta.Count, i + window);
            var jEnd = Math.Min(tb.Count, j + window);

            // Extend to next mutual sync token if found nearby.
            var synced = false;
            for (var look = 1; look <= window && !synced; look++)
            {
                if (i + look < ta.Count)
                {
                    var token = ta[i + look];
                    for (var k = 0; k <= window && j + k < tb.Count; k++)
                    {
                        if (tb[j + k] != token)
                            continue;
                        iEnd = i + look;
                        jEnd = j + k;
                        synced = true;
                        break;
                    }
                }
            }

            var oldSlice = string.Concat(ta.Skip(i).Take(iEnd - i));
            var newSlice = string.Concat(tb.Skip(j).Take(jEnd - j));
            if (oldSlice != newSlice)
            {
                var local = BuildHunksFromLcs(oldSlice, Tokenize(oldSlice), Tokenize(newSlice));
                foreach (var h in local)
                {
                    hunks.Add(new CorrectionHunk
                    {
                        StartPos = origPos + h.StartPos,
                        OldText = h.OldText,
                        NewText = h.NewText
                    });
                }
            }

            for (var t = i; t < iEnd; t++)
                origPos += ta[t].Length;
            for (var t = j; t < jEnd; t++)
                sugPos += tb[t].Length;
            i = iEnd;
            j = jEnd;

            // Consume the sync token if we stopped before it.
            if (i < ta.Count && j < tb.Count && ta[i] == tb[j])
            {
                origPos += ta[i].Length;
                sugPos += tb[j].Length;
                i++;
                j++;
            }
            else if (iEnd == i && jEnd == j)
            {
                // Safety: advance at least one token to avoid infinite loop.
                if (i < ta.Count)
                {
                    hunks.Add(new CorrectionHunk
                    {
                        StartPos = origPos,
                        OldText = ta[i],
                        NewText = j < tb.Count ? tb[j] : ""
                    });
                    origPos += ta[i].Length;
                    i++;
                    if (j < tb.Count)
                    {
                        sugPos += tb[j].Length;
                        j++;
                    }
                }
                else if (j < tb.Count)
                {
                    hunks.Add(new CorrectionHunk
                    {
                        StartPos = origPos,
                        OldText = "",
                        NewText = tb[j]
                    });
                    sugPos += tb[j].Length;
                    j++;
                }
            }
        }

        _ = suggestion;
        return hunks.Where(h => h.OldText != h.NewText).Select(TightenHunk).ToList();
    }

    private static List<CorrectionHunk> BuildHunksFromLcs(
        string original,
        List<string> ta,
        List<string> tb)
    {
        var n = ta.Count;
        var m = tb.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < m; j++)
            dp[i + 1, j + 1] = ta[i] == tb[j]
                ? dp[i, j] + 1
                : Math.Max(dp[i, j + 1], dp[i + 1, j]);

        var rev = new List<(string Kind, string A, string B)>();
        var ii = n;
        var jj = m;
        while (ii > 0 || jj > 0)
        {
            if (ii > 0 && jj > 0 && ta[ii - 1] == tb[jj - 1])
            {
                rev.Add(("eq", ta[ii - 1], tb[jj - 1]));
                ii--;
                jj--;
            }
            else if (jj > 0 && (ii == 0 || dp[ii, jj - 1] >= dp[ii - 1, jj]))
            {
                rev.Add(("ins", "", tb[jj - 1]));
                jj--;
            }
            else if (ii > 0)
            {
                rev.Add(("del", ta[ii - 1], ""));
                ii--;
            }
            else
            {
                break;
            }
        }

        rev.Reverse();

        var hunks = new List<CorrectionHunk>();
        var curOld = "";
        var curNew = "";
        var active = false;
        var hunkStart = 0;
        var scanPos = 0;

        // Emit atomic hunks: flush on whitespace equality OR when a replace pair completes
        // across a word boundary (don't glue unrelated mistakes together).
        foreach (var op in rev)
        {
            if (op.Kind == "eq")
            {
                if (active)
                {
                    hunks.Add(new CorrectionHunk { StartPos = hunkStart, OldText = curOld, NewText = curNew });
                    curOld = "";
                    curNew = "";
                    active = false;
                }
                scanPos += op.A.Length;
            }
            else
            {
                if (!active)
                {
                    active = true;
                    hunkStart = scanPos;
                }
                curOld += op.A;
                curNew += op.B;
                scanPos += op.A.Length;

                // If both sides just gained a complete non-space token pair, allow later
                // whitespace eq to split — already handled. Extra: flush when whitespace-only
                // has been absorbed into the hunk mid-run (rare with token ops).
            }
        }

        if (active)
            hunks.Add(new CorrectionHunk { StartPos = hunkStart, OldText = curOld, NewText = curNew });

        var cleaned = hunks.Where(h => h.OldText != h.NewText).Select(TightenHunk).ToList();
        if (cleaned.Count == 0 && original.Length > 0)
            cleaned.Add(new CorrectionHunk { StartPos = 0, OldText = original, NewText = string.Concat(tb) });
        return cleaned;
    }

    /// <summary>
    /// Shrink a hunk to the real edit (drop shared prefix/suffix) so previews aren't
    /// identical-looking long strings truncated at the same point.
    /// Expands to word boundaries for readable spelling/grammar cards.
    /// </summary>
    public static CorrectionHunk TightenHunk(CorrectionHunk hunk)
    {
        var oldText = hunk.OldText ?? "";
        var newText = hunk.NewText ?? "";
        if (oldText.Length == 0 || newText.Length == 0 || oldText == newText)
            return hunk;

        var prefix = 0;
        var maxPre = Math.Min(oldText.Length, newText.Length);
        while (prefix < maxPre && oldText[prefix] == newText[prefix])
            prefix++;

        var suffix = 0;
        var maxSuf = Math.Min(oldText.Length - prefix, newText.Length - prefix);
        while (suffix < maxSuf &&
               oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
            suffix++;

        while (prefix > 0 &&
               (IsWordChar(oldText[prefix - 1]) || IsWordChar(newText[prefix - 1])))
            prefix--;
        while (suffix > 0 &&
               (IsWordChar(oldText[oldText.Length - suffix]) ||
                IsWordChar(newText[newText.Length - suffix])))
            suffix--;

        var oldCore = oldText[prefix..(oldText.Length - suffix)];
        var newCore = newText[prefix..(newText.Length - suffix)];
        if (oldCore.Length == 0 && newCore.Length == 0)
            return hunk;
        if (oldCore == oldText && newCore == newText)
            return hunk;

        return new CorrectionHunk
        {
            StartPos = hunk.StartPos + prefix,
            OldText = oldCore,
            NewText = newCore,
            Selected = hunk.Selected,
            Category = hunk.Category,
            CategoryLabel = hunk.CategoryLabel,
            ShortMessage = hunk.ShortMessage,
            RuleId = hunk.RuleId
        };
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '\'';

    public static string BuildSuggestion(string original, IEnumerable<CorrectionHunk> hunks)
    {
        var ordered = hunks.OrderBy(h => h.StartPos).ToList();
        if (ordered.Count == 0)
            return original;

        var output = "";
        var pos = 0;
        foreach (var h in ordered)
        {
            if (h.StartPos < pos || h.StartPos > original.Length)
                continue;
            output += original[pos..h.StartPos];
            output += h.Selected ? h.NewText : h.OldText;
            pos = Math.Min(original.Length, h.StartPos + h.OldText.Length);
        }
        output += original[pos..];
        return output;
    }
}
