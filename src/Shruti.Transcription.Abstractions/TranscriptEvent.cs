namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptEvent(
    TranscriptEventKind Kind,
    string? Text = null,
    TranscriptSegment? Segment = null,
    string? Message = null,
    Exception? Error = null);
