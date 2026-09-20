namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionSessionOptions(
    TranscriptionModelDescriptor Model,
    ComputeBackend Backend,
    string Language,
    TranscriptionMode Mode,
    TimeSpan? MaximumAudioDuration = null)
{
    public TimeSpan EffectiveMaximumAudioDuration => MaximumAudioDuration ?? TimeSpan.FromMinutes(10);
}
