namespace Shruti.Transcription.Abstractions;

public static class TranscriptText
{
    // These are model annotations, not dictated words. Do not discard ordinary
    // bracketed text or phrases such as "thank you" which may be genuine speech.
    public static bool IsEmptyOrNonSpeech([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return string.IsNullOrWhiteSpace(text
            .Replace("[BLANK_AUDIO]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("[NO_SPEECH]", string.Empty, StringComparison.OrdinalIgnoreCase));
    }
}
