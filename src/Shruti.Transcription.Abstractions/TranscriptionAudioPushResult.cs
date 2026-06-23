namespace Shruti.Transcription.Abstractions;

public readonly record struct TranscriptionAudioPushResult(bool ReachedMaximumDuration)
{
    public static TranscriptionAudioPushResult Continue { get; } = new(ReachedMaximumDuration: false);

    public static TranscriptionAudioPushResult FinalizeAtMaximumDuration { get; } = new(ReachedMaximumDuration: true);
}
