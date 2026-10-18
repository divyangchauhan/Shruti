using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Shruti.Transcription.Abstractions;

namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppSpeechActivityDetector(string modelPath) : ISpeechActivityDetector
{
    public async Task<bool> ContainsSpeechAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Pcm16Signal.HasSignal(pcm16Audio.Span)) return false;
        var samples = new float[pcm16Audio.Length / sizeof(short)];
        for (int index = 0; index < samples.Length; index++)
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(pcm16Audio.Span.Slice(index * sizeof(short), sizeof(short))) / AudioFormat.Pcm16SampleScale;
        int result = await Task.Run(() => DetectSpeech(modelPath, samples, samples.Length), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException("Local speech detection is unavailable. Nothing was inserted.")
        };
    }

    [DllImport("shruti_whisper", EntryPoint = "shruti_whisper_detect_speech", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DetectSpeech([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
        [In] float[] samples, int sampleCount);
}
