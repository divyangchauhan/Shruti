using System.Threading.Channels;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class TranscriptionReadinessServiceTests
{
    [Fact]
    public void Registry_FindsProvidersByIdAndRejectsDuplicates()
    {
        var provider = new FakeProvider("provider-a");
        var registry = new TranscriptionProviderRegistry([provider]);

        Assert.Same(provider, registry.FindById("provider-a"));
        Assert.Throws<ArgumentException>(() => new TranscriptionProviderRegistry(
        [
            new FakeProvider("duplicate"),
            new FakeProvider("duplicate")
        ]));
    }

    [Fact]
    public async Task EvaluateAsync_AutoChoosesFastestCachedEligibleBackend()
    {
        var provider = new FakeProvider(
            "provider-a",
            [
                Capability("provider-a", ComputeBackend.Cpu, "CPU"),
                Capability("provider-a", ComputeBackend.Gpu, "GPU")
            ]);
        var cache = new InMemoryTranscriptionBenchmarkCache();
        await cache.SaveAsync(Benchmark(provider, ComputeBackend.Cpu, "CPU", 0.8), CancellationToken.None);
        await cache.SaveAsync(Benchmark(provider, ComputeBackend.Gpu, "GPU", 0.4), CancellationToken.None);
        var service = new TranscriptionReadinessService(
            new TranscriptionProviderRegistry([provider]),
            cache);

        TranscriptionReadinessResult readiness = await service.EvaluateAsync(
            new TranscriptionReadinessRequest(
                Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Cpu, ComputeBackend.Gpu }),
                ComputeBackend.Auto,
                AllowSlowTranscription: false,
                ProviderVersion: "1",
                ModelHash: "hash"),
            CancellationToken.None);

        Assert.True(readiness.CanProceed);
        Assert.Equal(ComputeBackend.Gpu, readiness.SelectedBackend);
        Assert.Equal(0.4, readiness.RealtimeFactor);
    }

    [Fact]
    public async Task EvaluateAsync_RequiresSlowModeForSlowCachedBackend()
    {
        var provider = new FakeProvider("provider-a", [Capability("provider-a", ComputeBackend.Cpu, "CPU")]);
        var cache = new InMemoryTranscriptionBenchmarkCache();
        await cache.SaveAsync(Benchmark(provider, ComputeBackend.Cpu, "CPU", 1.4), CancellationToken.None);
        var service = new TranscriptionReadinessService(
            new TranscriptionProviderRegistry([provider]),
            cache);
        TranscriptionReadinessRequest request = new(
            Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Cpu }),
            ComputeBackend.Cpu,
            AllowSlowTranscription: false,
            ProviderVersion: "1",
            ModelHash: "hash");

        TranscriptionReadinessResult blocked = await service.EvaluateAsync(request, CancellationToken.None);
        TranscriptionReadinessResult allowed = await service.EvaluateAsync(
            request with { AllowSlowTranscription = true },
            CancellationToken.None);

        Assert.Equal(TranscriptionReadinessStatus.SlowModeRequired, blocked.Status);
        Assert.True(blocked.RequiresSlowModeOptIn);
        Assert.True(allowed.CanProceed);
        Assert.Contains(allowed.EffectiveWarnings, warning => warning.Contains("slower than real time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_ReportsUnsupportedRequestedBackend()
    {
        var provider = new FakeProvider("provider-a", [Capability("provider-a", ComputeBackend.Cpu, "CPU")]);
        var service = new TranscriptionReadinessService(new TranscriptionProviderRegistry([provider]));

        TranscriptionReadinessResult readiness = await service.EvaluateAsync(
            new TranscriptionReadinessRequest(
                Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Cpu }),
                ComputeBackend.Gpu,
                AllowSlowTranscription: false),
            CancellationToken.None);

        Assert.Equal(TranscriptionReadinessStatus.Unsupported, readiness.Status);
        Assert.Contains("does not expose Gpu", readiness.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotTreatAutoAsConcreteModelBackend()
    {
        var provider = new FakeProvider("provider-a", [Capability("provider-a", ComputeBackend.Cpu, "CPU")]);
        var service = new TranscriptionReadinessService(new TranscriptionProviderRegistry([provider]));

        TranscriptionReadinessResult readiness = await service.EvaluateAsync(
            new TranscriptionReadinessRequest(
                Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Auto }),
                ComputeBackend.Cpu,
                AllowSlowTranscription: false),
            CancellationToken.None);

        Assert.Equal(TranscriptionReadinessStatus.Unsupported, readiness.Status);
        Assert.Contains("not listed", readiness.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BenchmarkRunner_MeasuresRealtimeFactorAndCachesTheResult()
    {
        var provider = new FakeProvider("provider-a", [Capability("provider-a", ComputeBackend.Cpu, "CPU")]);
        var cache = new InMemoryTranscriptionBenchmarkCache();
        var runner = new TranscriptionBenchmarkRunner(cache);
        TranscriptionModelDescriptor model = Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Cpu });

        TranscriptionBenchmarkResult result = await runner.RunAsync(
            provider,
            model,
            ComputeBackend.Cpu,
            providerVersion: "1",
            modelHash: "hash",
            deviceName: "CPU",
            audioDuration: TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        TranscriptionBenchmarkResult? cached = await cache.GetAsync(result.Key, CancellationToken.None);
        Assert.NotNull(cached);
        Assert.True(result.RealtimeFactor >= 0);
        Assert.Equal(640, provider.LastSession?.PushedByteCount);
    }

    private static EngineCapability Capability(
        string providerId,
        ComputeBackend backend,
        string deviceName)
    {
        return new EngineCapability(
            providerId,
            "Fake provider",
            backend,
            deviceName,
            SupportsStreaming: true,
            SupportsTimestamps: true,
            SupportsLanguageDetection: false,
            MeasuredRealtimeFactor: null,
            Warnings: []);
    }

    private static TranscriptionBenchmarkResult Benchmark(
        FakeProvider provider,
        ComputeBackend backend,
        string deviceName,
        double realtimeFactor)
    {
        TranscriptionModelDescriptor model = Model(
            provider.Id,
            new HashSet<ComputeBackend> { ComputeBackend.Cpu, ComputeBackend.Gpu });
        var capability = Capability(provider.Id, backend, deviceName);
        return new TranscriptionBenchmarkResult(
            TranscriptionBenchmarkKey.Create(provider, "1", model, "hash", capability),
            realtimeFactor,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(realtimeFactor),
            DateTimeOffset.UtcNow,
            Warnings: []);
    }

    private static TranscriptionModelDescriptor Model(
        string providerId,
        IReadOnlySet<ComputeBackend> supportedBackends)
    {
        return new TranscriptionModelDescriptor(
            "model-a",
            "Model A",
            providerId,
            "model.bin",
            "en",
            1,
            supportedBackends);
    }

    private sealed class FakeProvider : ITranscriptionProvider
    {
        private readonly IReadOnlyList<EngineCapability> _capabilities;

        public FakeProvider(
            string id,
            IReadOnlyList<EngineCapability>? capabilities = null)
        {
            Id = id;
            _capabilities = capabilities ?? [Capability(id, ComputeBackend.Cpu, "CPU")];
        }

        public string Id { get; }

        public string DisplayName => "Fake provider";

        public FakeSession? LastSession { get; private set; }

        public Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_capabilities);
        }

        public Task<bool> CanRunModelAsync(
            TranscriptionModelDescriptor model,
            ComputeBackend requestedBackend,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                string.Equals(model.ProviderId, Id, StringComparison.Ordinal) &&
                _capabilities.Any(capability => capability.Backend == requestedBackend));
        }

        public Task<ITranscriptionSession> CreateSessionAsync(
            TranscriptionSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSession = new FakeSession();
            return Task.FromResult<ITranscriptionSession>(LastSession);
        }
    }

    private sealed class FakeSession : ITranscriptionSession
    {
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public IAsyncEnumerable<TranscriptEvent> Events => _events.Reader.ReadAllAsync();

        public int PushedByteCount { get; private set; }

        public ValueTask<TranscriptionAudioPushResult> PushAudioAsync(
            ReadOnlyMemory<byte> pcmAudio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PushedByteCount += pcmAudio.Length;
            return ValueTask.FromResult(TranscriptionAudioPushResult.Continue);
        }

        public Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _events.Writer.TryComplete();
            return Task.FromResult(TranscriptResult.FromText("benchmark"));
        }

        public Task CancelAsync()
        {
            _events.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
