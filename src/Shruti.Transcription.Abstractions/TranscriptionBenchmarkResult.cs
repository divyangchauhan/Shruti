namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionBenchmarkResult(
    TranscriptionBenchmarkKey Key,
    double RealtimeFactor,
    TimeSpan AudioDuration,
    TimeSpan Elapsed,
    DateTimeOffset MeasuredAt,
    IReadOnlyList<string> Warnings);
