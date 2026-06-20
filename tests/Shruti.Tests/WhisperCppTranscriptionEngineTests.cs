using Shruti.Transcription.WhisperCpp;
using Xunit;

namespace Shruti.Tests;

public sealed class WhisperCppTranscriptionEngineTests
{
    [Fact]
    public async Task TranscribeAsync_MapsNativeSegmentsAndDisposesContext()
    {
        var context = new FakeNativeContext(
            status: 0,
            [
                new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1.2), " hello"),
                new WhisperCppSegment(TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(2.5), "world ")
            ]);
        var nativeApi = new FakeNativeApi(context);
        var engine = new WhisperCppTranscriptionEngine(nativeApi);

        WhisperCppTranscriptionResult result = await engine.TranscribeAsync(
            new WhisperCppTranscriptionRequest("model.bin", [0.1f, -0.1f], "en", ThreadCount: 4),
            CancellationToken.None);

        Assert.Equal("model.bin", nativeApi.LoadedModelPath);
        Assert.Equal(4, context.ThreadCount);
        Assert.Equal("en", context.Language);
        Assert.Equal("hello world", result.Text);
        Assert.Equal(2, result.Segments.Count);
        Assert.True(context.Disposed);
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsWhenNativeTranscriptionFails()
    {
        var context = new FakeNativeContext(status: -2, []);
        var engine = new WhisperCppTranscriptionEngine(new FakeNativeApi(context));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.TranscribeAsync(
            new WhisperCppTranscriptionRequest("model.bin", [0.1f]),
            CancellationToken.None));

        Assert.Contains("-2", exception.Message, StringComparison.Ordinal);
        Assert.True(context.Disposed);
    }

    [Fact]
    public async Task TranscribeAsync_HonorsCancellationBeforeLoadingModel()
    {
        var nativeApi = new FakeNativeApi(new FakeNativeContext(status: 0, []));
        var engine = new WhisperCppTranscriptionEngine(nativeApi);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.TranscribeAsync(
            new WhisperCppTranscriptionRequest("model.bin", [0.1f]),
            cancellation.Token));

        Assert.Null(nativeApi.LoadedModelPath);
    }

    [Fact]
    public void NativeApi_RejectsAMissingModelBeforeLoadingTheNativeLibrary()
    {
        var nativeApi = new WhisperCppNativeApi();

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() => nativeApi.LoadModel("missing-model.bin"));

        Assert.Contains("missing-model.bin", exception.FileName, StringComparison.Ordinal);
    }

    private sealed class FakeNativeApi : IWhisperCppNativeApi
    {
        private readonly IWhisperCppNativeContext _context;

        public FakeNativeApi(IWhisperCppNativeContext context)
        {
            _context = context;
        }

        public string? LoadedModelPath { get; private set; }

        public IWhisperCppNativeContext LoadModel(string modelPath)
        {
            LoadedModelPath = modelPath;
            return _context;
        }
    }

    private sealed class FakeNativeContext : IWhisperCppNativeContext
    {
        private readonly IReadOnlyList<WhisperCppSegment> _segments;

        public FakeNativeContext(int status, IReadOnlyList<WhisperCppSegment> segments)
        {
            Status = status;
            _segments = segments;
        }

        public bool Disposed { get; private set; }

        public string? Language { get; private set; }

        public int ThreadCount { get; private set; }

        private int Status { get; }

        public int Transcribe(float[] samples, string language, int threadCount)
        {
            Language = language;
            ThreadCount = threadCount;
            return Status;
        }

        public int GetSegmentCount()
        {
            return _segments.Count;
        }

        public WhisperCppSegment GetSegment(int index)
        {
            return _segments[index];
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
