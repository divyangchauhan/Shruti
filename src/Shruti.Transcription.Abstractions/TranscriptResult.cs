namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptResult(
    string Text,
    IReadOnlyList<TranscriptSegment> Segments)
{
    public static TranscriptResult FromText(string text)
    {
        var segment = new TranscriptSegment(
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            text);

        return new TranscriptResult(text, [segment]);
    }
}
