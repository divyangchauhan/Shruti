namespace Shruti.Core.Diagnostics;

public sealed record DiagnosticLogEntry(
    DateTimeOffset Timestamp,
    DiagnosticLogLevel Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string> Properties)
{
    public static DiagnosticLogEntry Create(
        DateTimeOffset timestamp,
        DiagnosticLogLevel level,
        string category,
        string message,
        IReadOnlyDictionary<string, string?>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Dictionary<string, string> redactedProperties = [];
        if (properties is not null)
        {
            foreach ((string key, string? value) in properties)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                redactedProperties[key] = DiagnosticTextRedactor.Redact(value);
            }
        }

        return new DiagnosticLogEntry(
            timestamp,
            level,
            category,
            DiagnosticTextRedactor.Redact(message),
            redactedProperties);
    }
}
