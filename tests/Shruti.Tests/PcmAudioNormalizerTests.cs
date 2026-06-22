using System.Buffers.Binary;
using Shruti.Audio.Windows;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class PcmAudioNormalizerTests
{
    [Fact]
    public void Normalize_PreservesTheFullPcm16Range()
    {
        var normalizer = CreateNormalizer(AudioFormat.Speech16KhzMono.SampleRateHz);
        byte[] input = new byte[2 * sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(0, sizeof(short)), short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(sizeof(short), sizeof(short)), short.MaxValue);

        byte[] output = normalizer.Normalize(input);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Normalize_DownsamplingAttenuatesFrequenciesAboveTheTargetNyquistLimit()
    {
        double speechBandRms = NormalizeSineWaveAndCalculateRms(2_000);
        double aliasedBandRms = NormalizeSineWaveAndCalculateRms(12_000);

        Assert.InRange(speechBandRms, 0.45, 0.65);
        Assert.True(
            aliasedBandRms < speechBandRms * 0.2,
            $"Expected high-frequency RMS {aliasedBandRms:F4} to be attenuated below 20% of {speechBandRms:F4}.");
    }

    [Fact]
    public void Normalize_PreservesResamplingContinuityAcrossInputBuffers()
    {
        const int inputSampleRateHz = 44_100;
        const int splitInputFrame = 1_337;
        byte[] input = CreatePcm16SineWave(inputSampleRateHz, inputSampleRateHz / 4, 2_000);
        var singleBufferNormalizer = CreateNormalizer(inputSampleRateHz);
        var splitBufferNormalizer = CreateNormalizer(inputSampleRateHz);

        byte[] expected = singleBufferNormalizer.Normalize(input);
        int splitByteOffset = splitInputFrame * sizeof(short);
        byte[] actual = splitBufferNormalizer.Normalize(input.AsSpan(0, splitByteOffset))
            .Concat(splitBufferNormalizer.Normalize(input.AsSpan(splitByteOffset)))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static double NormalizeSineWaveAndCalculateRms(int frequencyHz)
    {
        const int inputSampleRateHz = 48_000;
        const int inputFrameCount = inputSampleRateHz / 2;
        const int outputWarmupSampleCount = 400;
        var normalizer = CreateNormalizer(inputSampleRateHz);

        byte[] output = normalizer.Normalize(CreatePcm16SineWave(
            inputSampleRateHz,
            inputFrameCount,
            frequencyHz));
        int sampleCount = output.Length / sizeof(short);
        double squaredTotal = 0;

        for (int index = outputWarmupSampleCount; index < sampleCount; index++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(index * sizeof(short), sizeof(short)));
            double normalized = sample / (double)short.MaxValue;
            squaredTotal += normalized * normalized;
        }

        return Math.Sqrt(squaredTotal / (sampleCount - outputWarmupSampleCount));
    }

    private static PcmAudioNormalizer CreateNormalizer(int inputSampleRateHz)
    {
        return new PcmAudioNormalizer(
            new WindowsAudioStreamFormat(
                inputSampleRateHz,
                ChannelCount: 1,
                AudioSampleFormat.Int16,
                BitsPerSample: 16),
            AudioFormat.Speech16KhzMono);
    }

    private static byte[] CreatePcm16SineWave(int sampleRateHz, int frameCount, int frequencyHz)
    {
        var pcmAudio = new byte[frameCount * sizeof(short)];
        for (int index = 0; index < frameCount; index++)
        {
            double phase = 2 * Math.PI * frequencyHz * index / sampleRateHz;
            short sample = (short)Math.Round(Math.Sin(phase) * short.MaxValue * 0.8);
            BinaryPrimitives.WriteInt16LittleEndian(pcmAudio.AsSpan(index * sizeof(short), sizeof(short)), sample);
        }

        return pcmAudio;
    }
}
