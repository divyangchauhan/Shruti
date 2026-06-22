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

        Assert.NotNull(engine.LastSamples);
        Assert.Equal([0.5f, -0.5f], engine.LastSamples);
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

    [Fact]
    public async Task StreamingSession_EmitsPartialTextBeforeFinalTranscript()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FakeEngine(new WhisperCppTranscriptionResult(
        [
            new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "live transcript")
        ]));
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(
            CreateOptions(
                modelPath,
                new StreamingTranscriptionOptions(
                    MinimumAudioDuration: TimeSpan.FromMilliseconds(1),
                    UpdateInterval: TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);
        var partialSeen = new TaskCompletionSource<TranscriptEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<TranscriptEvent>();
        Task eventReader = ReadEventsAsync(
            session.Events,
            events,
            partialSeen,
            transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.PartialText);

        await session.PushAudioAsync(new byte[64], CancellationToken.None);

        TranscriptEvent partial = await partialSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        TranscriptResult result = await session.CompleteAsync(CancellationToken.None);
        await eventReader;

        Assert.Equal(TranscriptEventKind.PartialText, partial.Kind);
        Assert.Equal("live transcript", partial.Text);
        Assert.Equal("live transcript", result.Text);
        Assert.Equal(1, engine.SessionCreationCount);
        Assert.Equal(2, engine.TranscriptionCount);
        Assert.Contains(events, transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.Completed);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task StreamingSession_ReportsPartialFailuresAndCompletesTheFinalTranscript()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FailingFirstEngine(new WhisperCppTranscriptionResult(
        [
            new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "final transcript")
        ]));
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(
            CreateOptions(
                modelPath,
                new StreamingTranscriptionOptions(
                    MinimumAudioDuration: TimeSpan.FromMilliseconds(1),
                    UpdateInterval: TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);
        var warningSeen = new TaskCompletionSource<TranscriptEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<TranscriptEvent>();
        Task eventReader = ReadEventsAsync(
            session.Events,
            events,
            warningSeen,
            transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.Warning);

        await session.PushAudioAsync(new byte[64], CancellationToken.None);

        TranscriptEvent warning = await warningSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
        TranscriptResult result = await session.CompleteAsync(CancellationToken.None);
        await eventReader;

        Assert.Equal(TranscriptEventKind.Warning, warning.Kind);
        Assert.Equal("final transcript", result.Text);
        Assert.Equal(2, engine.TranscriptionCount);
        Assert.Contains(events, transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.Completed);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task StreamingSession_UsesOnlyTheConfiguredTrailingAudioWindowForPartialText()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FakeEngine(new WhisperCppTranscriptionResult(
        [
            new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "bounded transcript")
        ]));
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(
            CreateOptions(
                modelPath,
                new StreamingTranscriptionOptions(
                    MinimumAudioDuration: TimeSpan.FromMilliseconds(1),
                    UpdateInterval: TimeSpan.FromSeconds(1),
                    PartialAudioWindow: TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);
        var partialSeen = new TaskCompletionSource<TranscriptEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task eventReader = ReadEventsAsync(
            session.Events,
            [],
            partialSeen,
            transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.PartialText);

        await session.PushAudioAsync(new byte[64], CancellationToken.None);
        await partialSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(16, engine.LastSamples?.Length);

        await session.CancelAsync();
        await session.DisposeAsync();
        await eventReader;
    }

    [Fact]
    public async Task CancelAndDispose_DoNotWaitForAnInFlightPartialTranscription()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new BlockingEngine(new WhisperCppTranscriptionResult(
        [
            new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "delayed transcript")
        ]));
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(
            CreateOptions(
                modelPath,
                new StreamingTranscriptionOptions(
                    MinimumAudioDuration: TimeSpan.FromMilliseconds(1),
                    UpdateInterval: TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);

        await session.PushAudioAsync(new byte[64], CancellationToken.None);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await session.CancelAsync();
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(engine.Disposed.Task.IsCompleted);
        engine.Complete();
        await engine.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CompleteAsync_CancellationDoesNotWaitForAnInFlightPartialTranscription()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new BlockingEngine(new WhisperCppTranscriptionResult(
        [
            new WhisperCppSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "delayed transcript")
        ]));
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(
            CreateOptions(
                modelPath,
                new StreamingTranscriptionOptions(
                    MinimumAudioDuration: TimeSpan.FromMilliseconds(1),
                    UpdateInterval: TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        await session.PushAudioAsync(new byte[64], CancellationToken.None);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<TranscriptResult> finalization = session.CompleteAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finalization)
            .WaitAsync(TimeSpan.FromSeconds(1));

        await session.CancelAsync();
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        engine.Complete();
        await engine.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ProbeAsync_ReportsPartialTranscriptionSupport()
    {
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(TranscriptResult: null));

        IReadOnlyList<EngineCapability> capabilities = await provider.ProbeAsync(CancellationToken.None);

        Assert.Single(capabilities);
        Assert.True(capabilities[0].SupportsStreaming);
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

    private static TranscriptionSessionOptions CreateOptions(
        string modelPath,
        StreamingTranscriptionOptions? streaming = null)
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
            TranscriptionMode.Balanced,
            streaming);
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

    private static async Task ReadEventsAsync(
        IAsyncEnumerable<TranscriptEvent> source,
        ICollection<TranscriptEvent> destination,
        TaskCompletionSource<TranscriptEvent> observedEvent,
        Func<TranscriptEvent, bool> isObservedEvent)
    {
        await foreach (TranscriptEvent transcriptEvent in source)
        {
            destination.Add(transcriptEvent);
            if (isObservedEvent(transcriptEvent))
            {
                observedEvent.TrySetResult(transcriptEvent);
            }
        }
    }

    private sealed class FakeEngine : IWhisperCppTranscriptionEngine
    {
        private readonly WhisperCppTranscriptionResult? _result;

        public FakeEngine(WhisperCppTranscriptionResult? TranscriptResult)
        {
            _result = TranscriptResult;
        }

        public float[]? LastSamples { get; private set; }

        public int SessionCreationCount { get; private set; }

        public int TranscriptionCount { get; private set; }

        public Task<IWhisperCppInferenceSession> CreateSessionAsync(
            WhisperCppTranscriptionSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SessionCreationCount++;
            return Task.FromResult<IWhisperCppInferenceSession>(new FakeInferenceSession(this));
        }

        private sealed class FakeInferenceSession : IWhisperCppInferenceSession
        {
            private readonly FakeEngine _owner;

            public FakeInferenceSession(FakeEngine owner)
            {
                _owner = owner;
            }

            public Task<WhisperCppTranscriptionResult> TranscribeAsync(
                float[] samples,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _owner.LastSamples = samples;
                _owner.TranscriptionCount++;
                return Task.FromResult(_owner._result ?? new WhisperCppTranscriptionResult([]));
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FailingFirstEngine : IWhisperCppTranscriptionEngine
    {
        private readonly WhisperCppTranscriptionResult _result;

        public FailingFirstEngine(WhisperCppTranscriptionResult result)
        {
            _result = result;
        }

        public int TranscriptionCount { get; private set; }

        public Task<IWhisperCppInferenceSession> CreateSessionAsync(
            WhisperCppTranscriptionSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IWhisperCppInferenceSession>(new FailingFirstInferenceSession(this));
        }

        private sealed class FailingFirstInferenceSession : IWhisperCppInferenceSession
        {
            private readonly FailingFirstEngine _owner;

            public FailingFirstInferenceSession(FailingFirstEngine owner)
            {
                _owner = owner;
            }

            public Task<WhisperCppTranscriptionResult> TranscribeAsync(
                float[] samples,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _owner.TranscriptionCount++;
                if (_owner.TranscriptionCount == 1)
                {
                    throw new InvalidOperationException("Transient live transcription failure.");
                }

                return Task.FromResult(_owner._result);
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class BlockingEngine : IWhisperCppTranscriptionEngine
    {
        private readonly WhisperCppTranscriptionResult _result;
        private readonly TaskCompletionSource<WhisperCppTranscriptionResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingEngine(WhisperCppTranscriptionResult result)
        {
            _result = result;
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IWhisperCppInferenceSession> CreateSessionAsync(
            WhisperCppTranscriptionSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IWhisperCppInferenceSession>(new BlockingInferenceSession(this));
        }

        public void Complete()
        {
            _completion.TrySetResult(_result);
        }

        private sealed class BlockingInferenceSession : IWhisperCppInferenceSession
        {
            private readonly BlockingEngine _owner;

            public BlockingInferenceSession(BlockingEngine owner)
            {
                _owner = owner;
            }

            public Task<WhisperCppTranscriptionResult> TranscribeAsync(
                float[] samples,
                CancellationToken cancellationToken)
            {
                _owner.Started.TrySetResult();
                return _owner._completion.Task;
            }

            public ValueTask DisposeAsync()
            {
                _owner.Disposed.TrySetResult();
                return ValueTask.CompletedTask;
            }
        }
    }
}
