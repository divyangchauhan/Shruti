using System.Text.RegularExpressions;

namespace Shruti.Core.Diagnostics;

public static class DiagnosticTextRedactor
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    private static readonly Regex UrlPattern = new(
        @"\b(?:https?|file)://[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    private static readonly Regex WindowsUserPathPattern = new(
        @"\b[A-Z]:\\Users\\[^\\\s]+(?:\\[^\s]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    private static readonly Regex WindowsPathPattern = new(
        @"\b[A-Z]:\\(?:[^\\\s]+\\)*[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        MatchTimeout);

    private static readonly Regex UnixHomePathPattern = new(
        @"(?<!\S)/(?:home|Users)/[^\s/]+(?:/[^\s]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string redacted = EmailPattern.Replace(value, "[email]");
        redacted = UrlPattern.Replace(redacted, "[url]");
        redacted = WindowsUserPathPattern.Replace(redacted, "[user path]");
        redacted = WindowsPathPattern.Replace(redacted, "[path]");
        redacted = UnixHomePathPattern.Replace(redacted, "[user path]");
        return redacted;
    }
}
