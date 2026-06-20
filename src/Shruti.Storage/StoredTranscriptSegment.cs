namespace Shruti.Storage;

public sealed record StoredTranscriptSegment(
    int Index,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    float? Confidence = null);
