using System.Runtime.InteropServices;
using NAudio.Wave;
using Shruti.Transcription.Abstractions;

namespace Shruti.Audio.Windows;

public sealed class DictationRecordingWriter(string directory)
{
    public void Save(ReadOnlyMemory<byte> pcm, AudioFormat format)
    {
        string name = $"Dictation-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string path = Path.Combine(directory, name + ".wav");
        string temporary = Path.Combine(directory, name + ".tmp");
        Directory.CreateDirectory(directory);
        var waveFormat = format.SampleFormat == AudioSampleFormat.Float32
            ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRateHz, format.ChannelCount)
            : new WaveFormat(format.SampleRateHz, 16, format.ChannelCount);
        try
        {
            using (var writer = new WaveFileWriter(temporary, waveFormat))
            {
                if (!MemoryMarshal.TryGetArray(pcm, out var samples)) samples = pcm.ToArray();
                writer.Write(samples.Array!, samples.Offset, samples.Count);
            }
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
