namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionModelDescriptor(
    string Id,
    string DisplayName,
    string ProviderId,
    string LocalPath,
    string LanguageHint,
    long SizeBytes,
    IReadOnlySet<ComputeBackend> SupportedBackends);
