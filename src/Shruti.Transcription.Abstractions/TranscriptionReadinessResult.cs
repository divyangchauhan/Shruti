namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionReadinessResult(
    TranscriptionReadinessStatus Status,
    string Message,
    ITranscriptionProvider? Provider = null,
    EngineCapability? Capability = null)
{
    public bool CanProceed => Status == TranscriptionReadinessStatus.Ready;

    public ComputeBackend? SelectedBackend => Capability?.Backend;

    public string? DeviceName => Capability?.DeviceName;

    public IReadOnlyList<string> EffectiveWarnings => Capability?.Warnings ?? [];
}
