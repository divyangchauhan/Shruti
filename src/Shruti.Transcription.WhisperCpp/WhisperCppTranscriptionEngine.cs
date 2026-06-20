namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppTranscriptionEngine
{
    private readonly IWhisperCppNativeApi _nativeApi;

    public WhisperCppTranscriptionEngine(IWhisperCppNativeApi nativeApi)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
    }

    public Task<WhisperCppTranscriptionResult> TranscribeAsync(
        WhisperCppTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelPath);
        ArgumentNullException.ThrowIfNull(request.Samples);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Language);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Samples.Length == 0)
        {
            throw new ArgumentException("whisper.cpp requires at least one audio sample.", nameof(request));
        }

        if (request.ThreadCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Thread count cannot be negative.");
        }

        using IWhisperCppNativeContext context = _nativeApi.LoadModel(request.ModelPath);
        int status = context.Transcribe(request.Samples, request.Language, request.ThreadCount);
        if (status != 0)
        {
            throw new InvalidOperationException($"whisper.cpp transcription failed with native status {status}.");
        }

        int segmentCount = context.GetSegmentCount();
        if (segmentCount < 0)
        {
            throw new InvalidOperationException("whisper.cpp returned an invalid segment count.");
        }

        var segments = new List<WhisperCppSegment>(segmentCount);
        for (int index = 0; index < segmentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            segments.Add(context.GetSegment(index));
        }

        return Task.FromResult(new WhisperCppTranscriptionResult(segments));
    }
}
