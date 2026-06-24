namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionReadinessResult(
    TranscriptionReadinessStatus Status,
    string Message,
    ITranscriptionProvider? Provider = null,
    EngineCapability? Capability = null,
    TranscriptionBenchmarkResult? Benchmark = null,
    IReadOnlyList<string>? Warnings = null)
{
    public bool CanProceed => Status == TranscriptionReadinessStatus.Ready;

    public bool RequiresSlowModeOptIn => Status == TranscriptionReadinessStatus.SlowModeRequired;

    public ComputeBackend? SelectedBackend => Capability?.Backend;

    public string? DeviceName => Capability?.DeviceName;

    public double? RealtimeFactor => Benchmark?.RealtimeFactor ?? Capability?.MeasuredRealtimeFactor;

    public IReadOnlyList<string> EffectiveWarnings => Warnings ?? [];
}
