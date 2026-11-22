namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionSessionOptions(
    TranscriptionModelDescriptor Model,
    ComputeBackend Backend,
    string Language,
    TranscriptionMode Mode,
    TimeSpan? MaximumAudioDuration = null,
    RecognitionHints? RecognitionHints = null)
{
    public TimeSpan EffectiveMaximumAudioDuration => MaximumAudioDuration ?? TimeSpan.FromMinutes(10);

    public int GetMaximumPcm16Bytes(AudioFormat format)
    {
        TimeSpan duration = EffectiveMaximumAudioDuration;
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumAudioDuration), "Maximum audio duration must be positive.");
        }

        return checked((int)Math.Ceiling(duration.TotalSeconds * format.SampleRateHz) *
            format.ChannelCount * sizeof(short));
    }
}
