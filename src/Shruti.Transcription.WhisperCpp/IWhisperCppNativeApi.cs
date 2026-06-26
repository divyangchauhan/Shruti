using Shruti.Transcription.Abstractions;

namespace Shruti.Transcription.WhisperCpp;

public interface IWhisperCppNativeApi
{
    WhisperCppBackendCapabilities GetCapabilities();

    IWhisperCppNativeContext LoadModel(string modelPath, ComputeBackend backend, int gpuDevice);
}

public interface IWhisperCppNativeContext : IDisposable
{
    int Transcribe(float[] samples, string language, int threadCount, CancellationToken cancellationToken);

    int GetSegmentCount();

    WhisperCppSegment GetSegment(int index);
}
