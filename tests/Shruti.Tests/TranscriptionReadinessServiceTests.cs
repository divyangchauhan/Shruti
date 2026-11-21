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

    [Theory]
    [InlineData(true, ComputeBackend.Auto, ComputeBackend.Gpu)]
    [InlineData(false, ComputeBackend.Auto, ComputeBackend.Cpu)]
    [InlineData(true, ComputeBackend.Cpu, ComputeBackend.Cpu)]
    public async Task EvaluateAsync_DefaultPrefersGpuAndPreservesCpuFallbackAndOverride(
        bool hasGpu, ComputeBackend preference, ComputeBackend expected)
    {
        var capabilities = new List<EngineCapability> { Capability("provider-a", ComputeBackend.Cpu, "CPU") };
        if (hasGpu)
        {
            capabilities.Add(Capability("provider-a", ComputeBackend.Gpu, "GPU"));
            capabilities.Add(Capability("provider-a", ComputeBackend.Npu, "NPU"));
        }
        var provider = new FakeProvider("provider-a", capabilities);
        var service = new TranscriptionReadinessService(new TranscriptionProviderRegistry([provider]));

        TranscriptionReadinessResult readiness = await service.EvaluateAsync(
            new TranscriptionReadinessRequest(
                Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Cpu, ComputeBackend.Gpu, ComputeBackend.Npu }),
                preference),
            CancellationToken.None);

        Assert.True(readiness.CanProceed);
        Assert.Equal(expected, readiness.SelectedBackend);
    }

    [Fact]
    public async Task EvaluateAsync_ReportsUnsupportedRequestedBackend()
    {
        var provider = new FakeProvider("provider-a", [Capability("provider-a", ComputeBackend.Cpu, "CPU")]);
        var service = new TranscriptionReadinessService(new TranscriptionProviderRegistry([provider]));

        TranscriptionReadinessResult readiness = await service.EvaluateAsync(
            new TranscriptionReadinessRequest(
                Model("provider-a", new HashSet<ComputeBackend> { ComputeBackend.Cpu }),
                ComputeBackend.Gpu),
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
                ComputeBackend.Cpu),
            CancellationToken.None);

        Assert.Equal(TranscriptionReadinessStatus.Unsupported, readiness.Status);
        Assert.Contains("not listed", readiness.Message, StringComparison.Ordinal);
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
            SupportsTimestamps: true,
            SupportsLanguageDetection: false,
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
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
