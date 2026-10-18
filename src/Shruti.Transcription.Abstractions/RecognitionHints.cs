namespace Shruti.Transcription.Abstractions;

public sealed record RecognitionHints(IReadOnlyList<string> Terms, string? InitialPrompt)
{
    public static RecognitionHints Empty { get; } = new([], null);
}
