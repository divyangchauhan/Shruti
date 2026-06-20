namespace Shruti.Storage;

public interface ISettingsRepository
{
    Task<ShrutiSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ShrutiSettings settings, CancellationToken cancellationToken);
}
