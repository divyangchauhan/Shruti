namespace Shruti.Transcription.Abstractions;

public sealed class TranscriptionProviderRegistry : ITranscriptionProviderRegistry
{
    private readonly Dictionary<string, ITranscriptionProvider> _providersById;

    public TranscriptionProviderRegistry(IEnumerable<ITranscriptionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        ITranscriptionProvider[] providerArray = providers.ToArray();
        foreach (ITranscriptionProvider provider in providerArray)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);
        }

        string? duplicateProviderId = providerArray
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateProviderId is not null)
        {
            throw new ArgumentException(
                $"The transcription provider id '{duplicateProviderId}' is registered more than once.",
                nameof(providers));
        }

        Providers = providerArray;
        _providersById = providerArray.ToDictionary(provider => provider.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ITranscriptionProvider> Providers { get; }

    public ITranscriptionProvider? FindById(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providersById.GetValueOrDefault(providerId);
    }
}
