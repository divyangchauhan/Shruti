namespace Shruti.Transcription.Abstractions;

public sealed record AudioFormat(
    int SampleRateHz,
    int ChannelCount,
    AudioSampleFormat SampleFormat)
{
    public static AudioFormat Speech16KhzMono { get; } = new(
        16_000,
        1,
        AudioSampleFormat.Int16);
}
