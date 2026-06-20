using System.Buffers.Binary;
using Shruti.Transcription.Abstractions;

namespace Shruti.Audio.Windows;

internal sealed class PcmAudioNormalizer
{
    private readonly WindowsAudioStreamFormat _inputFormat;
    private readonly AudioFormat _outputFormat;
    private readonly int _inputFrameSize;
    private readonly double _inputFramesPerOutputFrame;

    private long _inputFramesProcessed;
    private double _nextOutputInputFrame;

    public PcmAudioNormalizer(WindowsAudioStreamFormat inputFormat, AudioFormat outputFormat)
    {
        _inputFormat = inputFormat ?? throw new ArgumentNullException(nameof(inputFormat));
        _outputFormat = outputFormat ?? throw new ArgumentNullException(nameof(outputFormat));

        if (inputFormat.SampleRateHz <= 0 || inputFormat.ChannelCount <= 0)
        {
            throw new ArgumentException("The Windows microphone reported an invalid input format.", nameof(inputFormat));
        }

        if (outputFormat.SampleRateHz <= 0 || outputFormat.ChannelCount != 1 ||
            outputFormat.SampleFormat != AudioSampleFormat.Int16)
        {
            throw new NotSupportedException("Only mono 16-bit PCM output is currently supported.");
        }

        int bytesPerSample = inputFormat.BitsPerSample / 8;
        if (bytesPerSample is < 2 or > 4 || inputFormat.BitsPerSample % 8 != 0)
        {
            throw new NotSupportedException("The Windows microphone sample size is not supported.");
        }

        _inputFrameSize = checked(bytesPerSample * inputFormat.ChannelCount);
        _inputFramesPerOutputFrame = inputFormat.SampleRateHz / (double)outputFormat.SampleRateHz;
    }

    public byte[] Normalize(ReadOnlySpan<byte> input)
    {
        int frameCount = input.Length / _inputFrameSize;
        if (frameCount == 0)
        {
            return [];
        }

        long firstInputFrame = _inputFramesProcessed;
        long endInputFrame = firstInputFrame + frameCount;
        int expectedSamples = Math.Max(1, (int)Math.Ceiling(frameCount / _inputFramesPerOutputFrame));
        byte[] output = new byte[expectedSamples * sizeof(short)];
        int outputOffset = 0;

        while (_nextOutputInputFrame < endInputFrame)
        {
            int inputFrameIndex = (int)Math.Clamp(
                Math.Floor(_nextOutputInputFrame - firstInputFrame),
                0,
                frameCount - 1);
            float sample = ReadMonoSample(input, inputFrameIndex);
            short pcmSample = (short)Math.Clamp(
                (int)Math.Round(sample * short.MaxValue),
                short.MinValue,
                short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(outputOffset, sizeof(short)), pcmSample);
            outputOffset += sizeof(short);
            _nextOutputInputFrame += _inputFramesPerOutputFrame;
        }

        _inputFramesProcessed = endInputFrame;
        return outputOffset == output.Length ? output : output[..outputOffset];
    }

    private float ReadMonoSample(ReadOnlySpan<byte> input, int inputFrameIndex)
    {
        double total = 0;
        for (int channel = 0; channel < _inputFormat.ChannelCount; channel++)
        {
            int byteOffset = checked((inputFrameIndex * _inputFrameSize) +
                (channel * (_inputFormat.BitsPerSample / 8)));
            total += ReadSample(input.Slice(byteOffset, _inputFormat.BitsPerSample / 8));
        }

        return Math.Clamp((float)(total / _inputFormat.ChannelCount), -1f, 1f);
    }

    private float ReadSample(ReadOnlySpan<byte> sample)
    {
        if (_inputFormat.SampleFormat == AudioSampleFormat.Float32)
        {
            if (sample.Length != sizeof(float))
            {
                throw new NotSupportedException("Only 32-bit floating-point microphone audio is supported.");
            }

            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample));
        }

        return sample.Length switch
        {
            sizeof(short) => BinaryPrimitives.ReadInt16LittleEndian(sample) / (float)short.MaxValue,
            3 => ReadInt24LittleEndian(sample) / 8_388_607f,
            sizeof(int) => BinaryPrimitives.ReadInt32LittleEndian(sample) / (float)int.MaxValue,
            _ => throw new NotSupportedException("The Windows microphone PCM format is not supported.")
        };
    }

    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> sample)
    {
        int value = sample[0] | (sample[1] << 8) | (sample[2] << 16);
        return (value & 0x00800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }
}
