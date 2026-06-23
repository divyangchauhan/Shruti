namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionSessionOptions(
    TranscriptionModelDescriptor Model,
    ComputeBackend Backend,
    string Language,
    TranscriptionMode Mode,
    StreamingTranscriptionOptions? Streaming = null,
    TimeSpan? MaximumAudioDuration = null)
{
    public StreamingTranscriptionOptions EffectiveStreamingOptions => Streaming ?? StreamingTranscriptionOptions.Default;

    public TimeSpan EffectiveMaximumAudioDuration => MaximumAudioDuration ?? TimeSpan.FromMinutes(10);
}
