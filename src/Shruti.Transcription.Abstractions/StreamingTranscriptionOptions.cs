namespace Shruti.Transcription.Abstractions;

public sealed record StreamingTranscriptionOptions(
    bool EnablePartialTranscription = true,
    TimeSpan? MinimumAudioDuration = null,
    TimeSpan? UpdateInterval = null,
    TimeSpan? PartialAudioWindow = null)
{
    public static StreamingTranscriptionOptions Default { get; } = new();

    public TimeSpan EffectiveMinimumAudioDuration => MinimumAudioDuration ?? TimeSpan.FromSeconds(1.5);

    public TimeSpan EffectiveUpdateInterval => UpdateInterval ?? TimeSpan.FromSeconds(1);

    public TimeSpan EffectivePartialAudioWindow => PartialAudioWindow ?? TimeSpan.FromSeconds(15);
}
