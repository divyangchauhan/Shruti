namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionReadinessRequest(
    TranscriptionModelDescriptor Model,
    ComputeBackend BackendPreference,
    bool AllowSlowTranscription,
    string? ProviderVersion = null,
    string? ModelHash = null,
    double MaximumRealtimeFactor = 1.0)
{
    public string EffectiveProviderVersion =>
        string.IsNullOrWhiteSpace(ProviderVersion)
            ? TranscriptionBenchmarkKey.UnknownProviderVersion
            : ProviderVersion.Trim();

    public string EffectiveModelHash =>
        string.IsNullOrWhiteSpace(ModelHash)
            ? TranscriptionBenchmarkKey.UnknownModelHash
            : ModelHash.Trim();
}
