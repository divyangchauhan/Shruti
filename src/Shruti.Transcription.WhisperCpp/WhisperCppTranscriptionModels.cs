namespace Shruti.Transcription.WhisperCpp;

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
