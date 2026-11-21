namespace Shruti.Transcription.Abstractions;

public sealed class TranscriptionReadinessService
{
    private readonly ITranscriptionProviderRegistry _providerRegistry;

    public TranscriptionReadinessService(ITranscriptionProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
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

        var candidates = new List<EngineCapability>();
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

            candidates.Add(capability);
        }

        if (candidates.Count == 0)
        {
            return Unsupported(
                incompatibilities.Count == 0
                    ? $"No compatible backend is available for '{request.Model.DisplayName}'."
                    : string.Join(" ", incompatibilities));
        }

        EngineCapability selected = candidates.OrderBy(candidate => BackendRank(candidate.Backend)).First();
        return new TranscriptionReadinessResult(
            TranscriptionReadinessStatus.Ready,
            $"{provider.DisplayName} is ready on {selected.Backend}.",
            provider,
            selected);
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

    private static int BackendRank(ComputeBackend backend)
    {
        return backend switch
        {
            ComputeBackend.Gpu => 0,
            ComputeBackend.Npu => 1,
            ComputeBackend.Cpu => 2,
            _ => 3
        };
    }

    private static TranscriptionReadinessResult Unsupported(string message)
    {
        return new TranscriptionReadinessResult(
            TranscriptionReadinessStatus.Unsupported,
            message);
    }
}
