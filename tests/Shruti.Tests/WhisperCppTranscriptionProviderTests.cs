using Shruti.Transcription.Abstractions;
using Shruti.Transcription.WhisperCpp;
using Xunit;

namespace Shruti.Tests;

public sealed class WhisperCppTranscriptionProviderTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompleteAsync_NormalizesPcmAndMapsNativeSegmentsAndEvents()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FakeEngine(new WhisperCppTranscriptionResult(
        [
            new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(0.5), "hello"),
            new WhisperCppSegment(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), "world")
        ]));
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(CreateOptions(modelPath), CancellationToken.None);

        await session.PushAudioAsync(new byte[] { 0, 64, 0, 192 }, CancellationToken.None);
        TranscriptResult result = await session.CompleteAsync(CancellationToken.None);
        IReadOnlyList<TranscriptEvent> events = await ReadAllAsync(session.Events);

        Assert.NotNull(engine.LastRequest);
        Assert.Equal([0.5f, -0.5f], engine.LastRequest.Samples);
        Assert.Equal("hello world", result.Text);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(3, events.Count);
        Assert.Equal(TranscriptEventKind.SegmentFinalized, events[0].Kind);
        Assert.Equal(TranscriptEventKind.Completed, events[^1].Kind);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task CanRunModelAsync_RequiresCpuAndExistingWhisperModel()
    {
        string modelPath = await CreateModelFileAsync();
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(TranscriptResult: null));
        TranscriptionModelDescriptor descriptor = CreateOptions(modelPath).Model;

        bool cpu = await provider.CanRunModelAsync(descriptor, ComputeBackend.Cpu, CancellationToken.None);
        bool gpu = await provider.CanRunModelAsync(descriptor, ComputeBackend.Gpu, CancellationToken.None);
        bool missing = await provider.CanRunModelAsync(descriptor with { LocalPath = Path.Combine(_rootPath, "missing.bin") }, ComputeBackend.Cpu, CancellationToken.None);

        Assert.True(cpu);
        Assert.False(gpu);
        Assert.False(missing);
    }

    [Fact]
    public async Task CompleteAsync_RejectsOddLengthPcm()
    {
        string modelPath = await CreateModelFileAsync();
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(TranscriptResult: null));
        ITranscriptionSession session = await provider.CreateSessionAsync(CreateOptions(modelPath), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => session.PushAudioAsync(new byte[] { 1 }, CancellationToken.None).AsTask());

        await session.DisposeAsync();
    }

    [Fact]
    public async Task CancelAsync_PreventsCompletion()
    {
        string modelPath = await CreateModelFileAsync();
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(TranscriptResult: null));
        ITranscriptionSession session = await provider.CreateSessionAsync(CreateOptions(modelPath), CancellationToken.None);

        await session.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.CompleteAsync(CancellationToken.None));
        await session.DisposeAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private async Task<string> CreateModelFileAsync()
    {
        Directory.CreateDirectory(_rootPath);
        string modelPath = Path.Combine(_rootPath, "test-model.bin");
        await File.WriteAllBytesAsync(modelPath, [1]);
        return modelPath;
    }

    private static TranscriptionSessionOptions CreateOptions(string modelPath)
    {
        return new TranscriptionSessionOptions(
            new TranscriptionModelDescriptor(
                "test-model",
                "Test model",
                "whisper.cpp",
                modelPath,
                "en",
                1,
                new HashSet<ComputeBackend> { ComputeBackend.Cpu }),
            ComputeBackend.Cpu,
            "en",
            TranscriptionMode.Balanced);
    }

    private static async Task<IReadOnlyList<TranscriptEvent>> ReadAllAsync(IAsyncEnumerable<TranscriptEvent> events)
    {
        var values = new List<TranscriptEvent>();
        await foreach (TranscriptEvent value in events)
        {
            values.Add(value);
        }

        return values;
    }

    private sealed class FakeEngine : IWhisperCppTranscriptionEngine
    {
        private readonly WhisperCppTranscriptionResult? _result;

        public FakeEngine(WhisperCppTranscriptionResult? TranscriptResult)
        {
            _result = TranscriptResult;
        }

        public WhisperCppTranscriptionRequest? LastRequest { get; private set; }

        public Task<WhisperCppTranscriptionResult> TranscribeAsync(
            WhisperCppTranscriptionRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_result ?? new WhisperCppTranscriptionResult([]));
        }
    }
}
