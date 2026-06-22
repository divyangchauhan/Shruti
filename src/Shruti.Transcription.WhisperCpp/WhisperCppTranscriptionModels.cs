namespace Shruti.Transcription.WhisperCpp;

public sealed record WhisperCppTranscriptionSessionOptions(
    string ModelPath,
    string Language = "en",
    int ThreadCount = 0)
{
    public const int MaximumDefaultThreadCount = 4;

    public int EffectiveThreadCount => ThreadCount > 0
        ? ThreadCount
        : Math.Clamp(Environment.ProcessorCount, 1, MaximumDefaultThreadCount);
}

public sealed record WhisperCppTranscriptionRequest(
    string ModelPath,
    float[] Samples,
    string Language = "en",
    int ThreadCount = 0);

public sealed record WhisperCppSegment(TimeSpan Start, TimeSpan End, string Text);

public sealed record WhisperCppTranscriptionResult(IReadOnlyList<WhisperCppSegment> Segments)
{
    public string Text => string.Join(" ", Segments
        .Select(segment => segment.Text.Trim())
        .Where(text => !string.IsNullOrWhiteSpace(text)));
}
