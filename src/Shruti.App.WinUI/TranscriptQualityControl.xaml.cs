using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Models;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed partial class TranscriptQualityControl : UserControl
{
    private ISettingsRepository? _repository;
    public TranscriptQualitySettings Settings { get; private set; } = new();
    public event EventHandler? SettingsChanged;
    public TranscriptQualityControl() { InitializeComponent(); }
    public void Configure(ISettingsRepository repository, TranscriptQualitySettings settings)
    {
        settings = settings with { LocalRuntimePath = Path.Combine(AppContext.BaseDirectory, "CleanupRuntime", "llama-server.exe") };
        _repository = repository; Settings = settings;
        CleanupBox.IsChecked = settings.CleanupMode == TranscriptCleanupMode.Light;
        ContextBox.IsChecked = settings.EnableCursorContext;
        ModelBox.IsChecked = settings.EnableLocalModel;
        ModelBox.IsEnabled = File.Exists(settings.LocalModelPath) && File.Exists(settings.LocalRuntimePath);
    }
    private async void Preferences_Click(object sender, RoutedEventArgs e)
    {
        Settings = Settings with
        {
            CleanupMode = CleanupBox.IsChecked == true ? TranscriptCleanupMode.Light : TranscriptCleanupMode.Off,
            EnableCursorContext = ContextBox.IsChecked == true,
            EnableLocalModel = ModelBox.IsChecked == true
        };
        await SaveAsync();
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false; MessageText.Text = "Downloading and verifying local cleanup. You can keep using dictation.";
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var installation = await new LocalCleanupInstaller(client).InstallAsync(
                Path.Combine(AppDataPaths.CreateDefault().ModelsDirectory, "Cleanup"),
                Path.Combine(AppContext.BaseDirectory, "CleanupRuntime"), CancellationToken.None);
            Settings = Settings with { LocalModelPath = installation.ModelPath, LocalRuntimePath = installation.RuntimePath, EnableLocalModel = true };
            ModelBox.IsEnabled = true; ModelBox.IsChecked = true;
            await SaveAsync();
            MessageText.Text = "Local cleanup is installed. The model loads before the first recording.";
        }
        catch (Exception) { MessageText.Text = "Local cleanup could not be installed. Dictation is still available. Try the download again."; }
        finally { InstallButton.IsEnabled = true; }
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
