using System.Text.RegularExpressions;
using Shruti.Core.TranscriptQuality;

namespace Shruti.TranscriptQuality;

public static class DeterministicRefiner
{
    private static readonly Dictionary<string, string> Numbers = new(StringComparer.OrdinalIgnoreCase)
    { ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9", ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12" };

    public static string Refine(string text, IReadOnlyList<string> vocabulary, TranscriptContext? context,
        TranscriptCleanupMode mode, List<TranscriptEdit> edits)
    {
        if (mode == TranscriptCleanupMode.Off) return text;
        // Quoted text, URLs, file names, code, and names bypass word-command rewriting.
        text = TransformUnprotected(text, vocabulary, chunk =>
        {
            chunk = Regex.Replace(chunk, @"\bnew paragraph\b[.,]?\s*", "\n\n", RegexOptions.IgnoreCase);
            chunk = Regex.Replace(chunk, @"\bnew line\b[.,]?\s*", "\n", RegexOptions.IgnoreCase);
            foreach (var command in new Dictionary<string, string>
            { ["full stop"] = ".", ["question mark"] = "?", ["exclamation mark"] = "!", ["comma"] = ",", ["semicolon"] = ";", ["colon"] = ":" })
                chunk = Regex.Replace(chunk, @"\b" + command.Key + @"\b[.,]?", command.Value, RegexOptions.IgnoreCase);
            chunk = Regex.Replace(chunk, @"\b(zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|\d+)\s*,?\s+actually\s+(zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|\d+)\b",
                match => Numbers.GetValueOrDefault(match.Groups[2].Value, match.Groups[2].Value), RegexOptions.IgnoreCase);
            chunk = Regex.Replace(chunk, @"(?i)\bat (zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b",
                match => "at " + Numbers[match.Groups[1].Value]);
            return chunk;
        });
        text = Regex.Replace(text, @"[^\S\r\n]+([,.;:!?])", "$1");
        text = Regex.Replace(text, @"[ \t]*\n[ \t]*", "\n");
        text = Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        // Numbered lists require an explicit introduction and a complete sequence starting with 1 and 2.
        Match list = Regex.Match(text, @"(?is)^numbered list\s*[:.,]?\s*(?:one|1)[.)]?\s+(.+?)\s+(?:two|2)[.)]?\s+(.+?)(?:\s+(?:three|3)[.)]?\s+(.+))?$", RegexOptions.CultureInvariant);
        if (list.Success)
            text = "1. " + list.Groups[1].Value.Trim() + "\n2. " + list.Groups[2].Value.Trim() +
                (list.Groups[3].Success ? "\n3. " + list.Groups[3].Value.Trim() : "");
        text = SentenceCase(text, vocabulary);
        return text;
    }

    internal static string TransformUnprotected(string text, IReadOnlyList<string> vocabulary, Func<string, string> transform)
    {
        string names = string.Join("|", vocabulary.Where(term => !string.IsNullOrWhiteSpace(term)).OrderByDescending(term => term.Length).Select(Regex.Escape));
        string pattern = "\"[^\"]*\"|“[^”]*”|`[^`]*`|(?:https?://|www\\.)\\S+|\\b\\S+@\\S+\\b|\\b[\\w-]+\\.[\\w.]+\\b|\\b[\\w]+[_/\\\\][\\w/\\\\.-]+\\b";
        if (names.Length > 0) pattern += @"|(?<![\p{L}\p{M}\p{N}_])(?:" + names + @")(?![\p{L}\p{M}\p{N}_])";
        var result = new System.Text.StringBuilder();
        int index = 0;
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant))
        {
            result.Append(transform(text[index..match.Index]));
            result.Append(match.Value);
            index = match.Index + match.Length;
        }
        return result.Append(transform(text[index..])).ToString();
    }

    internal static string SentenceCase(string text, IReadOnlyList<string> vocabulary)
    {
        return Regex.Replace(text, @"(^|[.!?]\s+|\n+)(\p{Ll})", match =>
            match.Groups[1].Value + match.Groups[2].Value.ToUpperInvariant());
    }

    public static string Join(string text, TranscriptContext? context, IReadOnlyList<string> vocabulary)
    {
        if (context is null || string.IsNullOrWhiteSpace(text)) return text;
        string before = context.BeforeCaret ?? "";
        string after = context.AfterCaret ?? "";
        string trimmedBefore = before.TrimEnd(' ', '\t');
        bool continuation = trimmedBefore.Length > 0 && !".!?\r\n".Contains(trimmedBefore[^1]);
        bool proper = vocabulary.Any(term => text.StartsWith(term, StringComparison.Ordinal) &&
            (text.Length == term.Length || !char.IsLetterOrDigit(text[term.Length]))) ||
            Regex.IsMatch(text, @"^I(?:\b|['’])");
        if (continuation && !proper && char.IsUpper(text[0])) text = char.ToLowerInvariant(text[0]) + text[1..];
        if (after.Length > 0 && ".,!?;:".Contains(after[0]) && text.Length > 0 && ".,!?;:".Contains(text[^1])) text = text[..^1];
        if (before.Length > 0 && !char.IsWhiteSpace(before[^1]) && !"([{\"'".Contains(before[^1]) &&
            !char.IsWhiteSpace(text[0]) && !".,!?;:)]}".Contains(text[0])) text = " " + text;
        if (after.Length > 0 && !char.IsWhiteSpace(after[0]) && !".,!?;:)]}\"'".Contains(after[0]) &&
            !char.IsWhiteSpace(text[^1])) text += " ";
        return text;
    }
}
