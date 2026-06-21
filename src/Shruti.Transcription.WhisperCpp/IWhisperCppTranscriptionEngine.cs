namespace Shruti.Transcription.WhisperCpp;

public interface IWhisperCppTranscriptionEngine
{
    Task<WhisperCppTranscriptionResult> TranscribeAsync(
        WhisperCppTranscriptionRequest request,
        CancellationToken cancellationToken);
}
