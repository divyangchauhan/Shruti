namespace Shruti.Models;

public interface IModelDownloadClient
{
    Task DownloadAsync(
        Uri source,
        string destinationPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}
