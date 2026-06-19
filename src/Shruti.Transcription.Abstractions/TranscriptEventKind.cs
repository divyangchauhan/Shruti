namespace Shruti.Transcription.Abstractions;

public enum TranscriptEventKind
{
    PartialText,
    SegmentFinalized,
    Completed,
    Warning,
    Failed
}
