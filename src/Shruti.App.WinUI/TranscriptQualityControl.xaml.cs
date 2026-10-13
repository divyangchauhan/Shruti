using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Models;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed partial class TranscriptQualityControl : UserControl
{
    private ISettingsRepository? _repository;
    private bool _installing;
    public TranscriptQualitySettings Settings { get; private set; } = new();
    public event EventHandler? SettingsChanged;
    public TranscriptQualityControl() { InitializeComponent(); }
    public void Configure(ISettingsRepository repository, TranscriptQualitySettings settings)
    {
        settings = settings with { LocalRuntimePath = Path.Combine(AppContext.BaseDirectory, "CleanupRuntime", "llama-server.exe") };
        _repository = repository; Settings = settings;
        CleanupBox.IsChecked = settings.CleanupMode == TranscriptCleanupMode.Light;
        ContextBox.IsChecked = settings.EnableCursorContext;
        UpdateModelCard();
    }
    private void UpdateModelCard()
    {
        bool installed = File.Exists(Settings.LocalModelPath) && File.Exists(Settings.LocalRuntimePath);
        bool active = installed && Settings.EnableLocalModel && Settings.CleanupMode == TranscriptCleanupMode.Light;
        ModelStatusText.Text = active ? "Active" : installed ? "Installed" : "Not installed";
        ToggleModelButton.Content = active ? "Disable" : "Set active";
        ToggleModelButton.IsEnabled = installed && !_installing;
        InstallButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstallButton.IsEnabled = !_installing;
        DownloadProgress.Visibility = _installing ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void ToggleModel_Click(object sender, RoutedEventArgs e)
    {
        bool active = Settings.EnableLocalModel && Settings.CleanupMode == TranscriptCleanupMode.Light;
        Settings = Settings with
        {
            EnableLocalModel = !active,
            CleanupMode = active ? Settings.CleanupMode : TranscriptCleanupMode.Light
        };
        CleanupBox.IsChecked = Settings.CleanupMode == TranscriptCleanupMode.Light;
        await SaveAsync();
        UpdateModelCard();
    }
    private async void Preferences_Click(object sender, RoutedEventArgs e)
    {
        Settings = Settings with
        {
            CleanupMode = CleanupBox.IsChecked == true ? TranscriptCleanupMode.Light : TranscriptCleanupMode.Off,
            EnableCursorContext = ContextBox.IsChecked == true,
            EnableLocalModel = CleanupBox.IsChecked == true && Settings.EnableLocalModel
        };
        await SaveAsync();
        UpdateModelCard();
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        _installing = true; UpdateModelCard(); MessageText.Text = "Downloading and verifying Qwen3.5-2B. You can keep using dictation.";
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var installation = await new LocalCleanupInstaller(client).InstallAsync(
                Path.Combine(AppDataPaths.CreateDefault().ModelsDirectory, "Cleanup"),
                Path.Combine(AppContext.BaseDirectory, "CleanupRuntime"), CancellationToken.None);
            Settings = Settings with { LocalModelPath = installation.ModelPath, LocalRuntimePath = installation.RuntimePath };
            await SaveAsync();
            MessageText.Text = "Qwen3.5-2B is installed. Choose Set active to use it for cleanup.";
        }
        catch (Exception) { MessageText.Text = "Local cleanup could not be installed. Dictation is still available. Try the download again."; }
        finally { _installing = false; UpdateModelCard(); }
    }
    private async Task SaveAsync()
    {
        if (_repository is null) return;
        try
        {
            var settings = await _repository.LoadAsync(CancellationToken.None);
            await _repository.SaveAsync(settings with { TranscriptQuality = Settings }, CancellationToken.None);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception) { MessageText.Text = "Cleanup preferences could not be saved."; }
    }
}
