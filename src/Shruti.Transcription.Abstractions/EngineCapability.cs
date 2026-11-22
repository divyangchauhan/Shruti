namespace Shruti.Transcription.Abstractions;

public sealed record EngineCapability(
    string ProviderId,
    string ProviderDisplayName,
    ComputeBackend Backend,
    string DeviceName,
    bool SupportsTimestamps,
    bool SupportsLanguageDetection,
    IReadOnlyList<string> Warnings);
