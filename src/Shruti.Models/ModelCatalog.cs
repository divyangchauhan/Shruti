namespace Shruti.Models;

public sealed record ModelCatalog(
    int SchemaVersion,
    string Revision,
    IReadOnlyList<ModelCatalogEntry> Models)
{
    public ModelCatalogEntry GetRequiredModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        return Models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"The model '{modelId}' is not present in the catalog.");
    }
}
