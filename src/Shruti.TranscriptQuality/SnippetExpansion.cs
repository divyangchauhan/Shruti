using System.Text.RegularExpressions;
using Shruti.Core.TranscriptQuality;

namespace Shruti.TranscriptQuality;

// Replace triggers with protected code tokens before cleanup. Restore saved text only after cleanup.
internal sealed class SnippetExpansion
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    public IReadOnlyList<string> Tokens => _values.Keys.ToArray();
    public IReadOnlyList<string> Values => _values.Values.ToArray();
    public int Count => _values.Count;

    public string Protect(string text, IReadOnlyList<TranscriptSnippet> snippets)
    {
        var rules = snippets.Select(s => new CorrectionRule(s.Id, s.Language, s.Phrase,
            "shruti_snippet_" + Guid.NewGuid().ToString("N"), DateTimeOffset.UnixEpoch)).ToArray();
        string protectedText = DeterministicRefiner.TransformUnprotected(text, [], chunk => CorrectionMatcher.Apply(chunk, rules));
        for (int i = 0; i < rules.Length; i++)
            if (protectedText.Contains(rules[i].ReplacementText, StringComparison.Ordinal))
                _values[rules[i].ReplacementText] = snippets[i].Text;
        // A phrase spoken on its own inserts exactly the saved text, without ASR's trailing punctuation.
        string standalone = protectedText.Trim().TrimEnd('.', '!', '?');
        return _values.ContainsKey(standalone) ? standalone : protectedText;
    }

    public string Restore(string text)
    {
        if (_values.Count == 0) return text;
        return Regex.Replace(text, string.Join("|", _values.Keys.Select(Regex.Escape)),
            match => _values[match.Value], RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    }
}
