namespace Shruti.Transcription.WhisperCpp;

public interface IWhisperCppTranscriptionEngine
{
    WhisperCppBackendCapabilities Capabilities { get; }

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
