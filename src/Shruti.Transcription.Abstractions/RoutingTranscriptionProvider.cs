namespace Shruti.Transcription.Abstractions;

public sealed class RoutingTranscriptionProvider : ITranscriptionProvider
{
    private readonly ITranscriptionProviderRegistry _registry;

    public RoutingTranscriptionProvider(ITranscriptionProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string Id => "routing";

    public string DisplayName => "Shruti transcription router";

    public async Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
    {
        var capabilities = new List<EngineCapability>();
        foreach (ITranscriptionProvider provider in _registry.Providers)
        {
            capabilities.AddRange(await provider.ProbeAsync(cancellationToken).ConfigureAwait(false));
        }

        return capabilities;
    }

    public Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ITranscriptionProvider? provider = _registry.FindById(model.ProviderId);
        return provider is null
            ? Task.FromResult(false)
            : provider.CanRunModelAsync(model, requestedBackend, cancellationToken);
    }

    public Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ITranscriptionProvider provider = _registry.FindById(options.Model.ProviderId)
            ?? throw new InvalidOperationException(
                $"No transcription provider is registered for '{options.Model.ProviderId}'.");
        return provider.CreateSessionAsync(options, cancellationToken);
    }
}
