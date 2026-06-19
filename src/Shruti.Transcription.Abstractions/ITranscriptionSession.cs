namespace Shruti.Transcription.Abstractions;

public interface ITranscriptionSession : IAsyncDisposable
{
    AudioFormat RequiredInputFormat { get; }

    IAsyncEnumerable<TranscriptEvent> Events { get; }

    ValueTask PushAudioAsync(
        ReadOnlyMemory<byte> pcmAudio,
        CancellationToken cancellationToken);

    Task<TranscriptResult> CompleteAsync(
        CancellationToken cancellationToken);

    Task CancelAsync();
}
