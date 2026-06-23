namespace Shruti.Transcription.Abstractions;

public sealed record StreamingTranscriptionOptions(
    bool EnablePartialTranscription = true,
    TimeSpan? MinimumAudioDuration = null,
    TimeSpan? UpdateInterval = null,
    TimeSpan? MaximumPartialAudioDuration = null,
    TimeSpan? PartialAudioOverlap = null)
{
    public static StreamingTranscriptionOptions Default { get; } = new();

    public TimeSpan EffectiveMinimumAudioDuration => MinimumAudioDuration ?? TimeSpan.FromSeconds(1.5);

    public TimeSpan EffectiveUpdateInterval => UpdateInterval ?? TimeSpan.FromSeconds(1);

    public TimeSpan EffectiveMaximumPartialAudioDuration => MaximumPartialAudioDuration ?? TimeSpan.FromSeconds(3);

    public TimeSpan EffectivePartialAudioOverlap => PartialAudioOverlap ?? TimeSpan.FromMilliseconds(250);
}
