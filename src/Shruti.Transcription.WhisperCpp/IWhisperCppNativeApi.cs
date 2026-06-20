namespace Shruti.Transcription.WhisperCpp;

public interface IWhisperCppNativeApi
{
    IWhisperCppNativeContext LoadModel(string modelPath);
}

public interface IWhisperCppNativeContext : IDisposable
{
    int Transcribe(float[] samples, string language, int threadCount);

    int GetSegmentCount();

    WhisperCppSegment GetSegment(int index);
}
