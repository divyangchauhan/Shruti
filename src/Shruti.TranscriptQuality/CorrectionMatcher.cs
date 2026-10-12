using System.Text.RegularExpressions;
using Shruti.Core.TranscriptQuality;

namespace Shruti.TranscriptQuality;

public static class CorrectionMatcher
{
    public static string Apply(string text, IReadOnlyList<CorrectionRule> rules)
    {
        var replacements = rules.Where(rule => !string.IsNullOrWhiteSpace(rule.HeardText))
            .GroupBy(rule => Normalize(rule.HeardText), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().ReplacementText, StringComparer.OrdinalIgnoreCase);
        if (replacements.Count == 0) return text;
        string phrases = string.Join("|", replacements.Keys.OrderByDescending(key => key.Length)
            .Select(key => string.Join(@"\s+", key.Split(' ').Select(Regex.Escape))));
        // Allow a possessive suffix, but never match the beginning of a contraction.
        string pattern = @"(?<![\p{L}\p{M}\p{N}_'’])(?:" + phrases + @")(?![\p{L}\p{M}\p{N}_]|['’](?!s\b)\p{L})";
        return Regex.Replace(text, pattern, match => replacements[Normalize(match.Value)],
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    }

    private static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Normalize(System.Text.NormalizationForm.FormC);
}
