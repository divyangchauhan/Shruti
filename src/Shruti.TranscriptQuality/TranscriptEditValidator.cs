using System.Text.RegularExpressions;
using Shruti.Core.TranscriptQuality;

namespace Shruti.TranscriptQuality;

internal static class TranscriptEditValidator
{
    public static bool TryApply(string original, IReadOnlyList<ProposedTranscriptEdit> proposed,
        IReadOnlyList<string> protectedTerms, out string result, out IReadOnlyList<TranscriptEdit> applied)
    {
        result = original; applied = [];
        if (proposed.Count > 8) return false;
        var edits = new List<TranscriptEdit>();
        foreach (var edit in proposed)
        {
            if (string.IsNullOrEmpty(edit.Original) || edit.Replacement is null ||
                edit.Reason is not ("filler" or "self-correction" or "grammar" or "restart" or "rewrite")) return false;
            int start = original.IndexOf(edit.Original, StringComparison.Ordinal);
            if (start < 0 || original.IndexOf(edit.Original, start + 1, StringComparison.Ordinal) >= 0) return false;
            if (edit.Replacement.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t'))) return false;
            if (edit.Reason == "restart" && !IsCompletedRestart(edit.Original, edit.Replacement)) return false;
            // Models sometimes call a restart filler removal or self-correction. The
            // actual edit must retain an exact completed suffix regardless of its label.
            string reason = IsCompletedRestart(edit.Original, edit.Replacement) ? "restart" : edit.Reason;
            edits.Add(new("local-" + reason, start, edit.Original.Length, edit.Replacement));
        }
        var ordered = edits.OrderBy(edit => edit.Start).ToArray();
        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i].Start < ordered[i - 1].Start + ordered[i - 1].Length) return false;
        foreach (var edit in Enumerable.Reverse(ordered)) result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        if (string.IsNullOrWhiteSpace(result) || result.Length > original.Length * 1.35 + 8) { result = original; return false; }
        // A restart discards the abandoned clause, including any facts explicitly superseded
        // by the completed clause. Validate other edits against that retained text, not the
        // discarded words. Otherwise long restarts and repeated names always fail validation.
        string retained = original;
        foreach (var edit in Enumerable.Reverse(ordered).Where(edit => edit.Rule == "local-restart"))
            retained = retained.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        bool numericCorrection = retained != result && ExplicitNumericCorrection.Matches(retained, result);
        // Keep every protected occurrence, not just membership in a set.
        string[] fixedTokens = Regex.Matches(retained,
            "\"[^\"]*\"|“[^”]*”|`[^`]*`|(?:https?://|www\\.)\\S+|\\b\\S+@\\S+\\b|\\b[\\w-]+\\.[\\w.]+\\b|\\b[\\w]+[_/\\\\][\\w/\\\\.-]+\\b")
            .Select(match => match.Value)
            // The dotted-filename pattern also matches a bare numeric version such as 4.2.
            .Where(token => !numericCorrection || !Regex.IsMatch(token, @"^[0-9]+\.[0-9]+$"))
            .Concat(protectedTerms).Concat(InferProtectedTerms(retained)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string token in fixedTokens)
            if (Count(retained, token) != Count(result, token)) { result = original; return false; }
        string protectedPattern = @"\b(?:\d+(?:[.,:/-]\d+)*|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|million|billion|not|no|never|cannot|without|neither|nor|\p{L}+n't|\p{L}+n’t)\b";
        string[] before = Regex.Matches(retained, protectedPattern, RegexOptions.IgnoreCase).Select(match => match.Value.ToLowerInvariant()).ToArray();
        string[] after = Regex.Matches(result, protectedPattern, RegexOptions.IgnoreCase).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (!before.SequenceEqual(after) && !numericCorrection) { result = original; return false; }
        int wordCount = Regex.Matches(retained, @"\S+").Count;
        int changedWords = WordDistance(retained, result);
        // A complete rewrite can express the same meaning with different words.
        // The edit-distance budget remains only for the legacy small-edit protocol.
        if (!ordered.Any(edit => edit.Rule == "local-rewrite") &&
            changedWords > Math.Max(2, Math.Ceiling(wordCount * 0.35))) { result = original; return false; }
        // Returned edits replay in order without shifting later offsets.
        applied = Enumerable.Reverse(ordered).ToArray();
        return true;
    }

    private static bool IsCompletedRestart(string original, string replacement)
    {
        // The model decides whether speech is a restart; it cannot use this edit type
        // to invent a replacement, remove the finished clause, or delete all text.
        if (string.IsNullOrWhiteSpace(replacement) || replacement.Length >= original.Length) return false;
        int removedLength = original.Length - replacement.Length;
        // Sentence casing may change the first letter of the retained clause, but
        // spelling and casing inside it must still match the speaker's words.
        if (char.ToUpperInvariant(original[removedLength]) != char.ToUpperInvariant(replacement[0]) ||
            !original.AsSpan(removedLength + 1).SequenceEqual(replacement.AsSpan(1))) return false;
        return removedLength > 0 && !ExplicitNumericCorrection.DiscardsCorrection(original, removedLength) && !char.IsLetterOrDigit(original[removedLength - 1]) &&
            Regex.Matches(original[..removedLength], @"\b[\p{L}\p{N}]+\b").Count >= 2 &&
            Regex.Matches(replacement, @"\b[\p{L}\p{N}]+\b").Count >= 3;
    }

    internal static IEnumerable<string> InferProtectedTerms(string text)
    {
        // Some recognizers title-case ordinary prose. In that case an initial
        // capital is not evidence of a proper name. Explicit dictionary/context
        // terms are still protected by the caller, as are dates and technical casing.
        Match[] words = Regex.Matches(text, @"\b\p{L}[\p{L}\p{M}\p{N}_-]*\b").ToArray();
        bool titleCased = words.Length >= 5 &&
            words.Count(word => char.IsUpper(word.Value[0])) >= Math.Ceiling(words.Length * 0.8);
        foreach (Match match in Regex.Matches(text, @"\b\p{Lu}[\p{L}\p{M}\p{N}_-]+\b"))
        {
            if (titleCased && !match.Value.Skip(1).Any(char.IsUpper)) continue;
            // Skip common sentence starters and fillers. Unknown capitalized words remain protected.
            if (!Regex.IsMatch(match.Value, @"^(?:Um|Uh|Er|Erm|The|This|That|These|Those|Please|Send|Use|Do|Can|Could|Would|Will|We|You|It|Let|Hello|Hi|Dear|Actually)$"))
                yield return match.Value;
        }
        foreach (Match match in Regex.Matches(text, @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b", RegexOptions.IgnoreCase))
            yield return match.Value;
    }
    private static int Count(string text, string term) => string.IsNullOrEmpty(term) ? 0 : Regex.Matches(text,
        @"(?<![\p{L}\p{M}\p{N}_])" + Regex.Escape(term) + @"(?![\p{L}\p{M}\p{N}_])").Count;

    private static int WordDistance(string before, string after)
    {
        string[] left = Regex.Matches(before, @"\S+").Select(match => match.Value).ToArray();
        string[] right = Regex.Matches(after, @"\S+").Select(match => match.Value).ToArray();
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int i = 1; i <= left.Length; i++)
        {
            int[] current = new int[right.Length + 1]; current[0] = i;
            for (int j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1];
    }
}
