using System.Text.Json;
using Shruti.Core.Dictation;
using Shruti.Core.Triggers;
using Shruti.Storage;
using Xunit;

namespace Shruti.Tests;

public sealed class JsonSettingsRepositoryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultTriggerConfiguration_UsesTheHoldToDictateExperience()
    {
        TriggerConfiguration configuration = ShrutiSettings.Default.TriggerConfiguration;

        Assert.False(configuration.EnableGlobalHotkey);
        Assert.True(configuration.EnablePushToTalk);
        Assert.True(configuration.EnableFloatingButton);
        Assert.Equal("Ctrl+Win+Space", configuration.PushToTalkKey);
    }

    [Fact]
    public async Task LoadAsync_CreatesTheLocalDataLayoutAndReturnsDefaults()
    {
        var paths = new AppDataPaths(_rootPath);
        var repository = new JsonSettingsRepository(paths);

        ShrutiSettings settings = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(ShrutiSettings.Default, settings);
        Assert.True(Directory.Exists(paths.SettingsDirectory));
        Assert.True(Directory.Exists(paths.RecordingsDirectory));
        Assert.True(Directory.Exists(paths.TranscriptsDirectory));
        Assert.True(Directory.Exists(paths.ModelsDirectory));
        Assert.True(Directory.Exists(paths.ExportsDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.False(File.Exists(paths.SettingsFilePath));
    }

    [Fact]
    public async Task SaveAsync_PersistsSettingsAcrossRepositoryInstances()
    {
        var paths = new AppDataPaths(_rootPath);
        var settings = new ShrutiSettings
        {
            AudioInputDeviceId = "usb-microphone",
            InsertionMode = DictationInsertionMode.PreviewFirst,
            ThemePreference = AppThemePreference.Dark,
            AudioRetentionPolicy = AudioRetentionPolicy.Keep,
            TriggerConfiguration = new TriggerConfiguration(
                EnableGlobalHotkey: true,
                EnablePushToTalk: false,
                EnableFloatingButton: false,
                EnableTrayMenu: true,
                HotkeyGesture: "Ctrl+Shift+D",
                PushToTalkKey: "RightAlt")
        };

        await new JsonSettingsRepository(paths).SaveAsync(settings, CancellationToken.None);
        ShrutiSettings loaded = await new JsonSettingsRepository(paths).LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
        Assert.Contains("usb-microphone", await File.ReadAllTextAsync(paths.SettingsFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ThrowsForMalformedSettingsFile()
    {
        var paths = new AppDataPaths(_rootPath);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{ not valid json }");
        var repository = new JsonSettingsRepository(paths);

        await Assert.ThrowsAsync<JsonException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_NormalizesIncompleteSettingsFile()
    {
        var paths = new AppDataPaths(_rootPath);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{\"triggerConfiguration\":null,\"themePreference\":99}");
        var repository = new JsonSettingsRepository(paths);

        ShrutiSettings settings = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(ShrutiSettings.Default.ThemePreference, settings.ThemePreference);
        Assert.Equal(ShrutiSettings.Default.TriggerConfiguration, settings.TriggerConfiguration);
    }

    [Fact]
    public async Task SaveAsync_HonorsCancellationBeforeWriting()
    {
        var paths = new AppDataPaths(_rootPath);
        var repository = new JsonSettingsRepository(paths);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SaveAsync(ShrutiSettings.Default, cancellation.Token));

        Assert.False(File.Exists(paths.SettingsFilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
