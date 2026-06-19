namespace Shruti.Transcription.Abstractions;

public sealed record EngineCapability(
    string ProviderId,
    string ProviderDisplayName,
    ComputeBackend Backend,
    string DeviceName,
    bool SupportsStreaming,
    bool SupportsTimestamps,
    bool SupportsLanguageDetection,
    double? MeasuredRealtimeFactor,
    IReadOnlyList<string> Warnings);
