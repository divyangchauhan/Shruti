namespace Shruti.Transcription.Abstractions;

public interface ITranscriptionProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<EngineCapability>> ProbeAsync(
        CancellationToken cancellationToken);

    Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken);

    Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken);
}
