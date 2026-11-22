namespace Shruti.Transcription.Abstractions;

public interface ITranscriptionSession : IAsyncDisposable
{
    AudioFormat RequiredInputFormat { get; }

    Task<TranscriptResult> TranscribeAsync(
        ReadOnlyMemory<byte> pcmAudio,
        CancellationToken cancellationToken);

    Task CancelAsync();
}
