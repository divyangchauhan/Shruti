using System.Text.Json;

namespace Shruti.Storage;

public sealed class TranscriptExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task ExportAsync(
        StoredDictationSession session,
        TranscriptExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(destinationPath, Render(session, format), cancellationToken).ConfigureAwait(false);
    }

    public string Render(StoredDictationSession session, TranscriptExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(session);

        return format switch
        {
            TranscriptExportFormat.Text => RenderText(session),
            TranscriptExportFormat.Markdown => RenderMarkdown(session),
            TranscriptExportFormat.Json => JsonSerializer.Serialize(session, JsonOptions),
            TranscriptExportFormat.Srt => RenderSubtitles(session, separator: ','),
            TranscriptExportFormat.Vtt => $"WEBVTT\n\n{RenderSubtitles(session, separator: '.')}",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string RenderText(StoredDictationSession session)
    {
        return string.Join('\n', OrderedSegments(session).Select(segment => segment.Text));
    }

    private static string RenderMarkdown(StoredDictationSession session)
    {
        var lines = new List<string>
        {
            "# Shruti Transcript",
            string.Empty,
            $"- Started: {session.StartedAtUtc:O}",
            $"- Language: {session.Language}",
            $"- Status: {session.Status}",
            string.Empty,
            "## Transcript",
            string.Empty
        };
        lines.AddRange(OrderedSegments(session).Select(segment =>
            $"[{FormatTimestamp(segment.Start, '.')}] {segment.Text}"));
        return string.Join('\n', lines);
    }

    private static string RenderSubtitles(StoredDictationSession session, char separator)
    {
        var lines = new List<string>();
        int subtitleIndex = 1;
        foreach (StoredTranscriptSegment segment in OrderedSegments(session))
        {
            lines.Add(subtitleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            lines.Add($"{FormatTimestamp(segment.Start, separator)} --> {FormatTimestamp(segment.End, separator)}");
            lines.Add(segment.Text);
            lines.Add(string.Empty);
            subtitleIndex++;
        }

        return string.Join('\n', lines);
    }

    private static IEnumerable<StoredTranscriptSegment> OrderedSegments(StoredDictationSession session)
    {
        return session.Segments.OrderBy(segment => segment.Index);
    }

    private static string FormatTimestamp(TimeSpan timestamp, char separator)
    {
        int hours = (int)timestamp.TotalHours;
        return $"{hours:00}:{timestamp.Minutes:00}:{timestamp.Seconds:00}{separator}{timestamp.Milliseconds:000}";
    }
}
