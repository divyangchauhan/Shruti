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
                edit.Reason is not ("filler" or "self-correction" or "grammar")) return false;
            int start = original.IndexOf(edit.Original, StringComparison.Ordinal);
            if (start < 0 || original.IndexOf(edit.Original, start + 1, StringComparison.Ordinal) >= 0) return false;
            if (edit.Replacement.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t'))) return false;
            edits.Add(new("local-" + edit.Reason, start, edit.Original.Length, edit.Replacement));
        }
        var ordered = edits.OrderBy(edit => edit.Start).ToArray();
        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i].Start < ordered[i - 1].Start + ordered[i - 1].Length) return false;
        foreach (var edit in Enumerable.Reverse(ordered)) result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        if (string.IsNullOrWhiteSpace(result) || result.Length > original.Length * 1.35 + 8) { result = original; return false; }
        // Keep every protected occurrence, not just membership in a set.
        string[] fixedTokens = Regex.Matches(original,
            "\"[^\"]*\"|“[^”]*”|`[^`]*`|(?:https?://|www\\.)\\S+|\\b\\S+@\\S+\\b|\\b[\\w-]+\\.[\\w.]+\\b|\\b[\\w]+[_/\\\\][\\w/\\\\.-]+\\b")
            .Select(match => match.Value).Concat(protectedTerms).Concat(InferProtectedTerms(original)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string token in fixedTokens)
            if (Count(original, token) != Count(result, token)) { result = original; return false; }
        string protectedPattern = @"\b(?:\d+(?:[.,:/-]\d+)*|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|million|billion|not|no|never|cannot|without|neither|nor|\p{L}+n't|\p{L}+n’t)\b";
        string[] before = Regex.Matches(original, protectedPattern, RegexOptions.IgnoreCase).Select(match => match.Value.ToLowerInvariant()).ToArray();
        string[] after = Regex.Matches(result, protectedPattern, RegexOptions.IgnoreCase).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (!before.SequenceEqual(after)) { result = original; return false; }
        int wordCount = Regex.Matches(original, @"\S+").Count;
        int changedWords = WordDistance(original, result);
        if (changedWords > Math.Max(2, Math.Ceiling(wordCount * 0.35))) { result = original; return false; }
        // Returned edits replay in order without shifting later offsets.
        applied = Enumerable.Reverse(ordered).ToArray();
        return true;
    }

    internal static IEnumerable<string> InferProtectedTerms(string text)
    {
        foreach (Match match in Regex.Matches(text, @"\b\p{Lu}[\p{L}\p{M}\p{N}_-]+\b"))
        {
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
