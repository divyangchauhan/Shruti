namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptSegment(
    int Index,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    double? Confidence = null);
