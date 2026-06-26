using Shruti.Transcription.WhisperCpp;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class WhisperCppTranscriptionEngineTests
{
    [Fact]
    public async Task InferenceSession_ReusesOneContextForMultipleTranscriptions()
    {
        var context = new FakeNativeContext(
            status: 0,
            [
                new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1.2), " hello"),
                new WhisperCppSegment(TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(2.5), "world ")
            ]);
        var nativeApi = new FakeNativeApi(context);
        var engine = new WhisperCppTranscriptionEngine(nativeApi);

        IWhisperCppInferenceSession session = await engine.CreateSessionAsync(
            new WhisperCppTranscriptionSessionOptions("model.bin", "en", ThreadCount: 3),
            CancellationToken.None);

        WhisperCppTranscriptionResult first = await session.TranscribeAsync([0.1f, -0.1f], CancellationToken.None);
        WhisperCppTranscriptionResult second = await session.TranscribeAsync([0.2f, -0.2f], CancellationToken.None);

        Assert.Equal("model.bin", nativeApi.LoadedModelPath);
        Assert.Equal(ComputeBackend.Cpu, nativeApi.LoadedBackend);
        Assert.Equal(1, nativeApi.LoadCount);
        Assert.Equal(2, context.TranscriptionCount);
        Assert.Equal(3, context.ThreadCount);
        Assert.Equal("en", context.Language);
        Assert.Equal("hello world", first.Text);
        Assert.Equal("hello world", second.Text);
        Assert.False(context.Disposed);

        await session.DisposeAsync();

        Assert.True(context.Disposed);
    }

    [Fact]
    public async Task InferenceSession_UsesUpToFourThreadsWhenNoOverrideIsProvided()
    {
        var context = new FakeNativeContext(status: 0, []);
        var engine = new WhisperCppTranscriptionEngine(new FakeNativeApi(context));

        IWhisperCppInferenceSession session = await engine.CreateSessionAsync(
            new WhisperCppTranscriptionSessionOptions("model.bin"),
            CancellationToken.None);
        await session.TranscribeAsync([0.1f], CancellationToken.None);

        Assert.Equal(4, WhisperCppTranscriptionSessionOptions.MaximumDefaultThreadCount);
        Assert.Equal(
            Math.Clamp(
                Environment.ProcessorCount,
                1,
                WhisperCppTranscriptionSessionOptions.MaximumDefaultThreadCount),
            context.ThreadCount);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task InferenceSession_ThrowsWhenNativeTranscriptionFails()
    {
        var context = new FakeNativeContext(status: -2, []);
        var engine = new WhisperCppTranscriptionEngine(new FakeNativeApi(context));
        IWhisperCppInferenceSession session = await engine.CreateSessionAsync(
            new WhisperCppTranscriptionSessionOptions("model.bin"),
            CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.TranscribeAsync([0.1f], CancellationToken.None));

        Assert.Contains("-2", exception.Message, StringComparison.Ordinal);

        await session.DisposeAsync();

        Assert.True(context.Disposed);
    }

    [Fact]
    public async Task CreateSessionAsync_HonorsCancellationBeforeLoadingModel()
    {
        var nativeApi = new FakeNativeApi(new FakeNativeContext(status: 0, []));
        var engine = new WhisperCppTranscriptionEngine(nativeApi);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.CreateSessionAsync(
            new WhisperCppTranscriptionSessionOptions("model.bin"),
            cancellation.Token));

        Assert.Null(nativeApi.LoadedModelPath);
    }

    [Fact]
    public async Task CreateSessionAsync_PassesRequestedBackendToNativeApi()
    {
        var nativeApi = new FakeNativeApi(new FakeNativeContext(status: 0, []));
        var engine = new WhisperCppTranscriptionEngine(nativeApi);

        IWhisperCppInferenceSession session = await engine.CreateSessionAsync(
            new WhisperCppTranscriptionSessionOptions(
                "model.bin",
                Backend: ComputeBackend.Gpu,
                GpuDevice: 2),
            CancellationToken.None);

        Assert.Equal(ComputeBackend.Gpu, nativeApi.LoadedBackend);
        Assert.Equal(2, nativeApi.LoadedGpuDevice);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task InferenceSession_PropagatesCancellationToTheNativeContext()
    {
        var context = new CancellationAwareNativeContext();
        var engine = new WhisperCppTranscriptionEngine(new FakeNativeApi(context));
        IWhisperCppInferenceSession session = await engine.CreateSessionAsync(
            new WhisperCppTranscriptionSessionOptions("model.bin"),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        Task<WhisperCppTranscriptionResult> transcription = Task.Run(
            () => session.TranscribeAsync([0.1f], cancellation.Token));
        await context.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcription)
            .WaitAsync(TimeSpan.FromSeconds(2));

        await session.DisposeAsync();
    }

    [Fact]
    public void NativeApi_RejectsAMissingModelBeforeLoadingTheNativeLibrary()
    {
        var nativeApi = new WhisperCppNativeApi();

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() => nativeApi.LoadModel(
            "missing-model.bin",
            ComputeBackend.Cpu,
            gpuDevice: 0));

        Assert.Contains("missing-model.bin", exception.FileName, StringComparison.Ordinal);
    }

    private sealed class FakeNativeApi : IWhisperCppNativeApi
    {
        private readonly IWhisperCppNativeContext _context;

        public FakeNativeApi(IWhisperCppNativeContext context)
        {
            _context = context;
        }

        public int LoadCount { get; private set; }

        public string? LoadedModelPath { get; private set; }

        public ComputeBackend? LoadedBackend { get; private set; }

        public int? LoadedGpuDevice { get; private set; }

        public WhisperCppBackendCapabilities GetCapabilities()
        {
            return new WhisperCppBackendCapabilities(
                SupportsCpu: true,
                SupportsGpu: true,
                SupportsNpu: false,
                SystemInfo: "fake native api");
        }

        public IWhisperCppNativeContext LoadModel(string modelPath, ComputeBackend backend, int gpuDevice)
        {
            LoadCount++;
            LoadedModelPath = modelPath;
            LoadedBackend = backend;
            LoadedGpuDevice = gpuDevice;
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

        public int TranscriptionCount { get; private set; }

        private int Status { get; }

        public int Transcribe(float[] samples, string language, int threadCount, CancellationToken cancellationToken)
        {
            Language = language;
            ThreadCount = threadCount;
            TranscriptionCount++;
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

    private sealed class CancellationAwareNativeContext : IWhisperCppNativeContext
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Transcribe(float[] samples, string language, int threadCount, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            return -1;
        }

        public int GetSegmentCount()
        {
            return 0;
        }

        public WhisperCppSegment GetSegment(int index)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public void Dispose()
        {
        }
    }
}
