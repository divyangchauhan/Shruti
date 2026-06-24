namespace Shruti.Transcription.Abstractions;

public sealed class TranscriptionReadinessService
{
    private readonly ITranscriptionProviderRegistry _providerRegistry;
    private readonly ITranscriptionBenchmarkCache _benchmarkCache;

    public TranscriptionReadinessService(
        ITranscriptionProviderRegistry providerRegistry,
        ITranscriptionBenchmarkCache? benchmarkCache = null)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _benchmarkCache = benchmarkCache ?? new InMemoryTranscriptionBenchmarkCache();
    }

    public async Task<TranscriptionReadinessResult> EvaluateAsync(
        TranscriptionReadinessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Model);
        if (!Enum.IsDefined(request.BackendPreference))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Backend preference is not recognized.");
        }

        if (request.MaximumRealtimeFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum realtime factor must be positive.");
        }

        ITranscriptionProvider? provider = _providerRegistry.FindById(request.Model.ProviderId);
        if (provider is null)
        {
            return Unsupported($"No transcription provider is registered for '{request.Model.ProviderId}'.");
        }

        IReadOnlyList<EngineCapability> capabilities = await provider
            .ProbeAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<EngineCapability> preferredCapabilities = FilterByPreference(
                capabilities,
                request.BackendPreference)
            .Where(capability => capability.Backend != ComputeBackend.Auto)
            .ToArray();

        if (preferredCapabilities.Count == 0)
        {
            return Unsupported(
                request.BackendPreference == ComputeBackend.Auto
                    ? $"Provider '{provider.DisplayName}' did not report any usable backend."
                    : $"Provider '{provider.DisplayName}' does not expose {request.BackendPreference}.");
        }

        var candidates = new List<ReadinessCandidate>();
        var incompatibilities = new List<string>();
        foreach (EngineCapability capability in preferredCapabilities)
        {
            if (!SupportsBackend(request.Model, capability.Backend))
            {
                incompatibilities.Add($"{capability.Backend} is not listed for model '{request.Model.DisplayName}'.");
                continue;
            }

            bool canRun = await provider
                .CanRunModelAsync(request.Model, capability.Backend, cancellationToken)
                .ConfigureAwait(false);
            if (!canRun)
            {
                incompatibilities.Add($"{provider.DisplayName} cannot load '{request.Model.DisplayName}' on {capability.Backend}.");
                continue;
            }

            TranscriptionBenchmarkKey key = TranscriptionBenchmarkKey.Create(
                provider,
                request.EffectiveProviderVersion,
                request.Model,
                request.EffectiveModelHash,
                capability);
            TranscriptionBenchmarkResult? benchmark = await _benchmarkCache
                .GetAsync(key, cancellationToken)
                .ConfigureAwait(false);
            candidates.Add(new ReadinessCandidate(capability, benchmark));
        }

        if (candidates.Count == 0)
        {
            return Unsupported(
                incompatibilities.Count == 0
                    ? $"No compatible backend is available for '{request.Model.DisplayName}'."
                    : string.Join(" ", incompatibilities));
        }

        ReadinessCandidate selected = SelectCandidate(candidates, request.BackendPreference);
        double? realtimeFactor = selected.Benchmark?.RealtimeFactor ?? selected.Capability.MeasuredRealtimeFactor;
        IReadOnlyList<string> warnings = CreateWarnings(selected, realtimeFactor);
        if (realtimeFactor.HasValue &&
            realtimeFactor.Value > request.MaximumRealtimeFactor &&
            !request.AllowSlowTranscription)
        {
            return new TranscriptionReadinessResult(
                TranscriptionReadinessStatus.SlowModeRequired,
                $"{selected.Capability.Backend} measured {realtimeFactor.Value:0.##}x realtime; enable slow mode to use it.",
                provider,
                selected.Capability,
                selected.Benchmark,
                warnings);
        }

        return new TranscriptionReadinessResult(
            TranscriptionReadinessStatus.Ready,
            $"{provider.DisplayName} is ready on {selected.Capability.Backend}.",
            provider,
            selected.Capability,
            selected.Benchmark,
            warnings);
    }

    private static IEnumerable<EngineCapability> FilterByPreference(
        IEnumerable<EngineCapability> capabilities,
        ComputeBackend backendPreference)
    {
        return backendPreference == ComputeBackend.Auto
            ? capabilities
            : capabilities.Where(capability => capability.Backend == backendPreference);
    }

    private static bool SupportsBackend(TranscriptionModelDescriptor model, ComputeBackend backend)
    {
        return model.SupportedBackends.Contains(backend);
    }

    private static ReadinessCandidate SelectCandidate(
        IEnumerable<ReadinessCandidate> candidates,
        ComputeBackend backendPreference)
    {
        if (backendPreference != ComputeBackend.Auto)
        {
            return candidates.First();
        }

        return candidates
            .OrderBy(candidate => candidate.Benchmark?.RealtimeFactor ??
                candidate.Capability.MeasuredRealtimeFactor ??
                double.MaxValue)
            .ThenBy(candidate => BackendRank(candidate.Capability.Backend))
            .First();
    }

    private static int BackendRank(ComputeBackend backend)
    {
        return backend switch
        {
            ComputeBackend.Npu => 0,
            ComputeBackend.Gpu => 1,
            ComputeBackend.Cpu => 2,
            _ => 3
        };
    }

    private static IReadOnlyList<string> CreateWarnings(
        ReadinessCandidate selected,
        double? realtimeFactor)
    {
        List<string> warnings = [.. selected.Capability.Warnings];
        if (selected.Benchmark is null && selected.Capability.MeasuredRealtimeFactor is null)
        {
            warnings.Add("No benchmark result is cached for this model/backend/device.");
        }
        else if (realtimeFactor is > 1.0)
        {
            warnings.Add("This model/backend is slower than real time on this device.");
        }

        if (selected.Benchmark is not null)
        {
            warnings.Add($"Benchmark measured {selected.Benchmark.RealtimeFactor:0.##}x realtime.");
        }

        return warnings;
    }

    private static TranscriptionReadinessResult Unsupported(string message)
    {
        return new TranscriptionReadinessResult(
            TranscriptionReadinessStatus.Unsupported,
            message);
    }

    private sealed record ReadinessCandidate(
        EngineCapability Capability,
        TranscriptionBenchmarkResult? Benchmark);
}
