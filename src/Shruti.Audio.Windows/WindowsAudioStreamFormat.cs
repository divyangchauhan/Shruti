using Shruti.Transcription.Abstractions;

namespace Shruti.Audio.Windows;

public sealed record WindowsAudioStreamFormat(
    int SampleRateHz,
    int ChannelCount,
    AudioSampleFormat SampleFormat,
    int BitsPerSample);
