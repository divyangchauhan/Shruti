using System.Buffers.Binary;

namespace Shruti.Transcription.Abstractions;

public static class Pcm16Signal
{
    public static float[] DecodeSamples(ReadOnlySpan<byte> pcmAudio, int maximumBytes)
    {
        if (pcmAudio.Length % sizeof(short) != 0)
        {
            throw new ArgumentException("PCM16 audio must contain complete samples.", nameof(pcmAudio));
        }

        var samples = new float[Math.Min(pcmAudio.Length, maximumBytes) / sizeof(short)];
        for (int index = 0; index < samples.Length; index++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(pcmAudio.Slice(index * sizeof(short), sizeof(short)));
            samples[index] = sample / AudioFormat.Pcm16SampleScale;
        }

        return samples;
    }

    // Reject digital silence and fixed DC offsets. This is only a cheap precheck,
    // not speech classification. The local VAD handles room noise and other sounds.
    public static bool HasSignal(ReadOnlySpan<byte> audio)
    {
        if (audio.Length % sizeof(short) != 0)
            throw new ArgumentException("PCM16 audio must contain complete samples.", nameof(audio));
        int minimum = short.MaxValue;
        int maximum = short.MinValue;
        for (int index = 0; index < audio.Length; index += sizeof(short))
        {
            int sample = BinaryPrimitives.ReadInt16LittleEndian(audio.Slice(index, sizeof(short)));
            minimum = Math.Min(minimum, sample);
            maximum = Math.Max(maximum, sample);
            if (maximum - minimum > 4) return true;
        }
        return false;
    }
}
