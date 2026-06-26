namespace Shruti.Models;

public interface IModelManager
{
    string ModelsDirectory { get; }

    Task<IReadOnlyList<InstalledModel>> ListInstalledAsync(CancellationToken cancellationToken);

    Task<ModelInstallResult> DownloadAsync(
        ModelCatalogEntry entry,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);

    Task<ModelInstallResult> ImportAsync(
        ModelImportRequest request,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string modelId, CancellationToken cancellationToken);
}
