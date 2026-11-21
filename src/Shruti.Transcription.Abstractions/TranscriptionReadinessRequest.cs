namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionReadinessRequest(
    TranscriptionModelDescriptor Model,
    ComputeBackend BackendPreference);
