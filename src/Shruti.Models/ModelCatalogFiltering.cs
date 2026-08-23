using Shruti.Transcription.Abstractions;

namespace Shruti.Models;

public static class ModelCatalogFiltering
{
    public static ComputeBackend NormalizeBackendPreference(
        ModelCatalogEntry model,
        ComputeBackend requestedBackend,
        IReadOnlySet<ComputeBackend> availableBackends)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(availableBackends);

        return requestedBackend == ComputeBackend.Auto ||
            (model.SupportedBackends.Contains(requestedBackend) && availableBackends.Contains(requestedBackend))
                ? requestedBackend
                : ComputeBackend.Auto;
    }

    public static bool IsVisibleForBackend(
        ModelCatalogEntry model,
        string activeModelId,
        ComputeBackend backendFilter,
        IReadOnlySet<ComputeBackend> availableBackends)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeModelId);
        ArgumentNullException.ThrowIfNull(availableBackends);

        return string.Equals(model.Id, activeModelId, StringComparison.Ordinal) ||
            backendFilter == ComputeBackend.Auto ||
            (model.SupportedBackends.Contains(backendFilter) && availableBackends.Contains(backendFilter));
    }
}
