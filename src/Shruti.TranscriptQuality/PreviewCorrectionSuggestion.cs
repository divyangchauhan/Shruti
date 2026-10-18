using System.Text.RegularExpressions;

namespace Shruti.TranscriptQuality;

public sealed record PreviewCorrectionSuggestion(string HeardText, string ReplacementText)
{
    public static PreviewCorrectionSuggestion? Create(string raw, string edited)
    {
        Match[] before = Regex.Matches(raw, @"[\p{L}\p{M}\p{N}]+(?:['’][\p{L}\p{M}]+)?").ToArray();
        Match[] after = Regex.Matches(edited, @"[\p{L}\p{M}\p{N}]+(?:['’][\p{L}\p{M}]+)?").ToArray();
        if (before.Length != after.Length) return null;
        int[] differences = Enumerable.Range(0, before.Length).Where(i => before[i].Value != after[i].Value).ToArray();
        if (differences.Length != 1) return null;
        int index = differences[0];
        string heard = before[index].Value, replacement = after[index].Value;
        if (heard.Length > 256 || replacement.Length > 256 || heard.Any(char.IsDigit) || replacement.Any(char.IsDigit)) return null;
        return new(heard, replacement);
    }
}
