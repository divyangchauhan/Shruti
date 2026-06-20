namespace Shruti.Storage;

public sealed record StoredDictationSession(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string SourceTrigger,
    string? TargetProcessName,
    string? TargetWindowTitle,
    string? ModelId,
    string? ProviderId,
    string? Backend,
    string Language,
    string Status,
    IReadOnlyList<StoredTranscriptSegment> Segments);
