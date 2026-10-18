using System.Text;
using System.Text.RegularExpressions;
using Shruti.Core.TranscriptQuality;
using Shruti.Transcription.Abstractions;

namespace Shruti.TranscriptQuality;

public static class RecognitionHintBuilder
{
    public static RecognitionHints Build(IReadOnlyList<VocabularyEntry> vocabulary, TranscriptContext? context,
        IReadOnlyList<CorrectionRule> corrections, int maximumTokens, IReadOnlyList<string>? applicationTerms = null)
    {
        // A UTF-8 byte budget conservatively bounds byte-based Whisper tokens without loading a tokenizer/model.
        int budget = Math.Clamp(maximumTokens, 0, 224);
        IEnumerable<string> nearby = ContextTerms(context);
        var terms = new List<string>();
        int bytes = 0;
        foreach (string term in vocabulary.Select(entry => entry.Term).Concat(nearby).Concat(applicationTerms ?? [])
            .Concat(corrections.OrderByDescending(rule => rule.CreatedAtUtc).Select(rule => rule.ReplacementText))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(term) || term.Any(char.IsControl)) continue;
            int cost = Encoding.UTF8.GetByteCount(term) + (terms.Count == 0 ? 0 : 2);
            if (bytes + cost > budget) continue;
            terms.Add(term); bytes += cost;
            if (terms.Count == 32) break;
        }
        return new(terms, terms.Count == 0 ? null : string.Join(", ", terms));
    }

    internal static IEnumerable<string> ContextTerms(TranscriptContext? context) =>
        Regex.Matches((context?.BeforeCaret ?? "") + " " + context?.AfterCaret,
            @"\b\p{Lu}[\p{L}\p{M}\p{N}_-]{2,}\b").Select(match => match.Value);

    internal static IReadOnlyList<string> ApplicationTerms(string? processName) => processName?.ToLowerInvariant().Replace(".exe", "") switch
    {
        "outlook" or "olk" => ["Outlook"],
        "slack" => ["Slack"],
        "teams" or "ms-teams" => ["Microsoft Teams"],
        "whatsapp" => ["WhatsApp"],
        "telegram" => ["Telegram"],
        "notion" => ["Notion"],
        "winword" => ["Microsoft Word"],
        _ => []
    };
}
