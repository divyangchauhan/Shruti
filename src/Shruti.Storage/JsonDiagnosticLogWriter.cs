using System.Text.Json;
using Shruti.Core.Diagnostics;

namespace Shruti.Storage;

public sealed class JsonDiagnosticLogWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppDataPaths _paths;

    public JsonDiagnosticLogWriter(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task AppendAsync(
        DiagnosticLogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        DiagnosticLogEntry redactedEntry = Redact(entry);
        string line = JsonSerializer.Serialize(redactedEntry, SerializerOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            await File.AppendAllTextAsync(
                    _paths.DiagnosticLogFilePath,
                    $"{line}{Environment.NewLine}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DiagnosticLogEntry Redact(DiagnosticLogEntry entry)
    {
        Dictionary<string, string> properties = entry.Properties
            .ToDictionary(
                pair => pair.Key,
                pair => DiagnosticTextRedactor.Redact(pair.Value),
                StringComparer.Ordinal);

        return entry with
        {
            Message = DiagnosticTextRedactor.Redact(entry.Message),
            Properties = properties
        };
    }
}
