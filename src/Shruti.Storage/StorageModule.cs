namespace Shruti.Storage;

public sealed class StorageModule
{
    public string Name => "Shruti local storage";

    public ISettingsRepository CreateSettingsRepository()
    {
        return new JsonSettingsRepository(AppDataPaths.CreateDefault());
    }

    public ISessionRepository CreateSessionRepository()
    {
        return new SqliteSessionRepository(AppDataPaths.CreateDefault());
    }
}
