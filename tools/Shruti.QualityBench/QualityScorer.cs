using System.Text.RegularExpressions;

namespace Shruti.QualityBench;

public static class QualityScorer
{
    public static (int Errors, int ReferenceWords) WordError(string expected, string actual)
    {
        string[] reference = Words(expected);
        string[] hypothesis = Words(actual);
        int[] previous = Enumerable.Range(0, hypothesis.Length + 1).ToArray();
        for (int i = 1; i <= reference.Length; i++)
        {
            int[] current = new int[hypothesis.Length + 1];
            current[0] = i;
            for (int j = 1; j <= hypothesis.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
            previous = current;
        }
        return (previous[^1], reference.Length);
    }

    public static bool ContainsTerm(string text, string term) => Regex.IsMatch(text,
        @"(?<![\p{L}\p{M}\p{N}_])" + Regex.Escape(term) + @"(?![\p{L}\p{M}\p{N}_])",
        RegexOptions.CultureInvariant);

    public static int CountTerm(string text, string term) => Regex.Matches(text,
        @"(?<![\p{L}\p{M}\p{N}_])" + Regex.Escape(term) + @"(?![\p{L}\p{M}\p{N}_])",
        RegexOptions.CultureInvariant).Count;

    private static string[] Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{M}\p{N}]+(?:['’][\p{L}]+)?")
        .Select(match => match.Value).ToArray();
}
