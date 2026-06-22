namespace Shruti.Transcription.WhisperCpp;

public interface IWhisperCppTranscriptionEngine
{
    Task<IWhisperCppInferenceSession> CreateSessionAsync(
        WhisperCppTranscriptionSessionOptions options,
        CancellationToken cancellationToken);
}

public interface IWhisperCppInferenceSession : IAsyncDisposable
{
    Task<WhisperCppTranscriptionResult> TranscribeAsync(
        float[] samples,
        CancellationToken cancellationToken);
}
