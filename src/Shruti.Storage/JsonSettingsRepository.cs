using System.Text.Json;

namespace Shruti.Storage;

public sealed class JsonSettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppDataPaths _paths;

    public JsonSettingsRepository(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<ShrutiSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            if (!File.Exists(_paths.SettingsFilePath))
            {
                return ShrutiSettings.Default;
            }

            await using var stream = new FileStream(
                _paths.SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            ShrutiSettings? settings = await JsonSerializer
                .DeserializeAsync<ShrutiSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return Normalize(settings);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ShrutiSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = $"{_paths.SettingsFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            _paths.EnsureCreated();
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.SettingsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }

    private static ShrutiSettings Normalize(ShrutiSettings? settings)
    {
        if (settings is null)
        {
            return ShrutiSettings.Default;
        }

        return new ShrutiSettings
        {
            AudioInputDeviceId = settings.AudioInputDeviceId,
            InsertionMode = Enum.IsDefined(settings.InsertionMode)
                ? settings.InsertionMode
                : ShrutiSettings.Default.InsertionMode,
            ThemePreference = Enum.IsDefined(settings.ThemePreference)
                ? settings.ThemePreference
                : ShrutiSettings.Default.ThemePreference,
            AudioRetentionPolicy = Enum.IsDefined(settings.AudioRetentionPolicy)
                ? settings.AudioRetentionPolicy
                : ShrutiSettings.Default.AudioRetentionPolicy,
            BackendPreference = Enum.IsDefined(settings.BackendPreference)
                ? settings.BackendPreference
                : ShrutiSettings.Default.BackendPreference,
            AllowSlowTranscription = settings.AllowSlowTranscription,
            TriggerConfiguration = settings.TriggerConfiguration ?? ShrutiSettings.Default.TriggerConfiguration
        };
    }
}
