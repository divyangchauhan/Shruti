using NAudio.Dmo;
using NAudio.Wave;
using Shruti.Audio.Windows;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class WasapiAudioCaptureSourceTests
{
    [Theory]
    [InlineData(WaveFormatEncoding.IeeeFloat)]
    [InlineData(WaveFormatEncoding.Extensible)]
    public void DetermineSampleFormat_RecognizesIeeeFloatFormats(WaveFormatEncoding encoding)
    {
        Guid? subFormat = encoding == WaveFormatEncoding.Extensible
            ? AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT
            : null;

        AudioSampleFormat sampleFormat = WasapiAudioCaptureSource.DetermineSampleFormat(encoding, subFormat);

        Assert.Equal(AudioSampleFormat.Float32, sampleFormat);
    }

    [Fact]
    public void DetermineSampleFormat_LeavesExtensiblePcmAsIntegerAudio()
    {
        AudioSampleFormat sampleFormat = WasapiAudioCaptureSource.DetermineSampleFormat(
            WaveFormatEncoding.Extensible,
            AudioMediaSubtypes.MEDIASUBTYPE_PCM);

        Assert.Equal(AudioSampleFormat.Int16, sampleFormat);
    }
}
