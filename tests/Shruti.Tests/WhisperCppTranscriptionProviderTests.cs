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
        bool npu = await provider.CanRunModelAsync(descriptor, ComputeBackend.Npu, CancellationToken.None);
        bool missing = await provider.CanRunModelAsync(descriptor with { LocalPath = Path.Combine(_rootPath, "missing.bin") }, ComputeBackend.Cpu, CancellationToken.None);

        Assert.True(cpu);
        Assert.False(gpu);
        Assert.False(npu);
        Assert.False(missing);
    }

    [Fact]
    public async Task CanRunModelAsync_AllowsGpuWhenNativeRuntimeReportsGpu()
    {
        string modelPath = await CreateModelFileAsync();
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(
            TranscriptResult: null,
            SupportsGpu: true));
        TranscriptionModelDescriptor descriptor = CreateOptions(
            modelPath,
            supportedBackends: new HashSet<ComputeBackend> { ComputeBackend.Cpu, ComputeBackend.Gpu }).Model;

        bool gpu = await provider.CanRunModelAsync(descriptor, ComputeBackend.Gpu, CancellationToken.None);
        IReadOnlyList<EngineCapability> capabilities = await provider.ProbeAsync(CancellationToken.None);

        Assert.True(gpu);
        Assert.Contains(capabilities, capability => capability.Backend == ComputeBackend.Gpu);
    }

    [Fact]
    public async Task CanRunModelAsync_AutoRequiresMatchingNativeAndModelBackend()
    {
        string modelPath = await CreateModelFileAsync();
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(
            TranscriptResult: null,
            SupportsCpu: false,
            SupportsGpu: true));
        TranscriptionModelDescriptor descriptor = CreateOptions(
            modelPath,
            supportedBackends: new HashSet<ComputeBackend> { ComputeBackend.Cpu }).Model;

        bool canRun = await provider.CanRunModelAsync(descriptor, ComputeBackend.Auto, CancellationToken.None);

        Assert.False(canRun);
    }

    [Fact]
    public async Task CreateSessionAsync_ResolvesAutoToGpuWhenGpuIsAvailable()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FakeEngine(
            TranscriptResult: null,
            SupportsGpu: true);
        var provider = new WhisperCppTranscriptionProvider(engine);
        TranscriptionSessionOptions options = CreateOptions(
            modelPath,
            backend: ComputeBackend.Auto,
            supportedBackends: new HashSet<ComputeBackend> { ComputeBackend.Cpu, ComputeBackend.Gpu });

        ITranscriptionSession session = await provider.CreateSessionAsync(options, CancellationToken.None);

        Assert.Equal(ComputeBackend.Gpu, engine.LastOptions?.Backend);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task CreateSessionAsync_ReusesLoadedInferenceSessionForMatchingOptions()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FakeEngine(TranscriptResult: null);
        var provider = new WhisperCppTranscriptionProvider(engine);
        TranscriptionSessionOptions options = CreateOptions(modelPath);

        ITranscriptionSession first = await provider.CreateSessionAsync(options, CancellationToken.None);
        await first.DisposeAsync();
        ITranscriptionSession second = await provider.CreateSessionAsync(options, CancellationToken.None);
        await second.DisposeAsync();

        Assert.Equal(1, engine.SessionCreationCount);
        Assert.Equal(0, engine.DisposeCount);

        await provider.DisposeAsync();

        Assert.Equal(1, engine.DisposeCount);
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
    public async Task PushAudioAsync_ReachesTheSessionLimitAndRetainsCapturedAudioForFinalization()
    {
        string modelPath = await CreateModelFileAsync();
        var engine = new FakeEngine(TranscriptResult: null);
        var provider = new WhisperCppTranscriptionProvider(engine);
        ITranscriptionSession session = await provider.CreateSessionAsync(
            CreateOptions(modelPath, maximumAudioDuration: TimeSpan.FromMilliseconds(1)),
            CancellationToken.None);

        TranscriptionAudioPushResult atLimit = await session.PushAudioAsync(new byte[32], CancellationToken.None);
        TranscriptionAudioPushResult afterLimit = await session.PushAudioAsync(new byte[sizeof(short)], CancellationToken.None);
        TranscriptResult result = await session.CompleteAsync(CancellationToken.None);

        Assert.True(atLimit.ReachedMaximumDuration);
        Assert.True(afterLimit.ReachedMaximumDuration);
        Assert.Empty(result.Segments);
        Assert.Equal(16, engine.LastSamples?.Length);

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
    public async Task StreamingSession_UsesBoundedIncrementalAudioForPartialText()
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
                    UpdateInterval: TimeSpan.FromMilliseconds(1),
                    MaximumPartialAudioDuration: TimeSpan.FromMilliseconds(3),
                    PartialAudioOverlap: TimeSpan.FromMilliseconds(1))),
            CancellationToken.None);

        await session.PushAudioAsync(new byte[64], CancellationToken.None);
        await engine.WaitForNextTranscriptionAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await session.PushAudioAsync(new byte[64], CancellationToken.None);
        await engine.WaitForNextTranscriptionAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([32, 48], engine.TranscribedSampleCounts.Take(2));

        await session.CancelAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WaitsForAnInFlightPartialTranscriptionBeforeReleasingTheSession()
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
        Task disposal = session.DisposeAsync().AsTask();

        Assert.False(await CompletesWithinAsync(disposal, TimeSpan.FromMilliseconds(100)));
        Assert.False(engine.Disposed.Task.IsCompleted);
        engine.Complete();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(engine.Disposed.Task.IsCompleted);

        await provider.DisposeAsync();
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

        Task disposal = session.DisposeAsync().AsTask();

        Assert.False(await CompletesWithinAsync(disposal, TimeSpan.FromMilliseconds(100)));
        engine.Complete();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        await provider.DisposeAsync();
        await engine.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ProbeAsync_ReportsPartialTranscriptionSupport()
    {
        var provider = new WhisperCppTranscriptionProvider(new FakeEngine(TranscriptResult: null));

        IReadOnlyList<EngineCapability> capabilities = await provider.ProbeAsync(CancellationToken.None);

        EngineCapability capability = Assert.Single(capabilities);
        Assert.Equal(ComputeBackend.Cpu, capability.Backend);
        Assert.True(capability.SupportsStreaming);
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
        StreamingTranscriptionOptions? streaming = null,
        TimeSpan? maximumAudioDuration = null,
        ComputeBackend backend = ComputeBackend.Cpu,
        IReadOnlySet<ComputeBackend>? supportedBackends = null)
    {
        return new TranscriptionSessionOptions(
            new TranscriptionModelDescriptor(
                "test-model",
                "Test model",
                "whisper.cpp",
                modelPath,
                "en",
                1,
                supportedBackends ?? new HashSet<ComputeBackend> { ComputeBackend.Cpu }),
            backend,
            "en",
            TranscriptionMode.Balanced,
            streaming,
            maximumAudioDuration);
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

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        return ReferenceEquals(completedTask, task);
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
        private readonly object _sync = new();
        private readonly SemaphoreSlim _transcriptionsObserved = new(0);
        private readonly List<int> _transcribedSampleCounts = [];

        public FakeEngine(
            WhisperCppTranscriptionResult? TranscriptResult,
            bool SupportsCpu = true,
            bool SupportsGpu = false)
        {
            _result = TranscriptResult;
            Capabilities = new WhisperCppBackendCapabilities(
                SupportsCpu,
                SupportsGpu,
                SupportsNpu: false,
                SystemInfo: "fake whisper.cpp");
        }

        public WhisperCppBackendCapabilities Capabilities { get; }

        public float[]? LastSamples { get; private set; }

        public WhisperCppTranscriptionSessionOptions? LastOptions { get; private set; }

        public int SessionCreationCount { get; private set; }

        public int TranscriptionCount { get; private set; }

        public int DisposeCount { get; private set; }

        public IReadOnlyList<int> TranscribedSampleCounts
        {
            get
            {
                lock (_sync)
                {
                    return _transcribedSampleCounts.ToArray();
                }
            }
        }

        public Task WaitForNextTranscriptionAsync()
        {
            return _transcriptionsObserved.WaitAsync();
        }

        public Task<IWhisperCppInferenceSession> CreateSessionAsync(
            WhisperCppTranscriptionSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SessionCreationCount++;
            LastOptions = options;
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
                lock (_owner._sync)
                {
                    _owner.LastSamples = samples;
                    _owner.TranscriptionCount++;
                    _owner._transcribedSampleCounts.Add(samples.Length);
                }

                _owner._transcriptionsObserved.Release();
                return Task.FromResult(_owner._result ?? new WhisperCppTranscriptionResult([]));
            }

            public ValueTask DisposeAsync()
            {
                lock (_owner._sync)
                {
                    _owner.DisposeCount++;
                }

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

        public WhisperCppBackendCapabilities Capabilities { get; } = new(
            SupportsCpu: true,
            SupportsGpu: false,
            SupportsNpu: false,
            SystemInfo: "fake whisper.cpp");

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

        public WhisperCppBackendCapabilities Capabilities { get; } = new(
            SupportsCpu: true,
            SupportsGpu: false,
            SupportsNpu: false,
            SystemInfo: "fake whisper.cpp");

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
