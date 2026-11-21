using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
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
    private bool _saving;
    private string? _directory;
    private LocalCleanupModel SelectedModel => (LocalCleanupModel)((ComboBoxItem)ModelPicker.SelectedItem).Tag;

    public TranscriptQualityControl()
    {
        InitializeComponent();
        foreach (var model in new[] { LocalCleanupModel.Qwen, LocalCleanupModel.Utter })
            ModelPicker.Items.Add(new ComboBoxItem { Content = model.Name, Tag = model });
        ModelPicker.SelectedIndex = 0;
    }

    public void Configure(ISettingsRepository repository, TranscriptQualitySettings settings, string modelsDirectory)
    {
        _directory = Path.Combine(modelsDirectory, "Cleanup");
        _repository = repository;
        Settings = settings with { LocalRuntimePath = Path.Combine(AppContext.BaseDirectory, "CleanupRuntime", "llama-server.exe") };
        ModelPicker.SelectedItem = ModelPicker.Items.Cast<ComboBoxItem>().First(item => item.Tag == LocalCleanupModel.FromPath(settings.LocalModelPath));
        UpdateModelCard();
    }

    private string ModelPath(LocalCleanupModel model)
    {
        if (model == LocalCleanupModel.FromPath(Settings.LocalModelPath) && File.Exists(Settings.LocalModelPath))
            return Settings.LocalModelPath;
        return Path.Combine(_directory!, model.FileName);
    }

    private void ModelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MessageText.Text = "";
        UpdateModelCard();
    }

    private void UpdateModelCard()
    {
        if (ModelPicker.SelectedItem is null || _directory is null) return;
        var model = SelectedModel;
        bool installed = File.Exists(ModelPath(model)) && File.Exists(Settings.LocalRuntimePath);
        bool enabled = Settings.EnableLocalModel && Settings.CleanupMode == TranscriptCleanupMode.Light
            && File.Exists(Settings.LocalModelPath) && File.Exists(Settings.LocalRuntimePath);
        var current = LocalCleanupModel.FromPath(Settings.LocalModelPath);
        bool active = enabled && current == model;
        bool available = _repository is not null && !_saving;
        ModelDetailsText.Text = $"{model.Details} · CPU";
        ModelStatusText.Text = active ? "In use" : installed ? "Installed" : "Not installed";
        if (enabled && !active) ModelStatusText.Text += $" · {current.Name} is active";
        LicenseLink.NavigateUri = model.LicenseUri;
        ToggleModelButton.Content = active ? "Disable" : "Set active";
        ToggleModelButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        ToggleModelButton.IsEnabled = available && !_installing;
        AutomationProperties.SetName(ToggleModelButton, $"{ToggleModelButton.Content} {model.Name}");
        InstallButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        InstallButton.IsEnabled = available && !_installing;
        AutomationProperties.SetName(InstallButton, $"Download {model.Name}");
        ModelPicker.IsEnabled = available && !_installing;
        DownloadProgress.Visibility = _installing ? Visibility.Visible : Visibility.Collapsed;
        CleanupBox.IsChecked = Settings.CleanupMode == TranscriptCleanupMode.Light;
        ContextBox.IsChecked = Settings.EnableCursorContext;
        CleanupBox.IsEnabled = ContextBox.IsEnabled = available;
    }

    private async void ToggleModel_Click(object sender, RoutedEventArgs e)
    {
        var model = SelectedModel;
        string path = ModelPath(model);
        if (!File.Exists(path) || !File.Exists(Settings.LocalRuntimePath)) { UpdateModelCard(); return; }
        bool active = Settings.EnableLocalModel && Settings.CleanupMode == TranscriptCleanupMode.Light
            && string.Equals(Settings.LocalModelPath, path, StringComparison.OrdinalIgnoreCase);
        await SaveAsync(Settings with { LocalModelPath = path, EnableLocalModel = !active, CleanupMode = TranscriptCleanupMode.Light });
    }

    private async void Preferences_Click(object sender, RoutedEventArgs e)
    {
        await SaveAsync(Settings with
        {
            CleanupMode = CleanupBox.IsChecked == true ? TranscriptCleanupMode.Light : TranscriptCleanupMode.Off,
            EnableCursorContext = ContextBox.IsChecked == true,
            EnableLocalModel = CleanupBox.IsChecked == true && Settings.EnableLocalModel
        });
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_directory is null) return;
        var model = SelectedModel;
        _installing = true;
        UpdateModelCard();
        MessageText.Text = $"Downloading and verifying {model.Name}. You can keep using dictation.";
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            await new LocalCleanupInstaller(client).InstallAsync(model, _directory,
                Path.Combine(AppContext.BaseDirectory, "CleanupRuntime"), CancellationToken.None);
            MessageText.Text = $"{model.Name} is installed. Choose Set active to use it for cleanup.";
        }
        catch (Exception) { MessageText.Text = $"{model.Name} could not be installed. Try the download again."; }
        finally { _installing = false; UpdateModelCard(); }
    }

    private async Task SaveAsync(TranscriptQualitySettings preferences)
    {
        if (_repository is null || _saving) return;
        _saving = true;
        UpdateModelCard();
        try
        {
            var settings = await _repository.LoadAsync(CancellationToken.None);
            await _repository.SaveAsync(settings with { TranscriptQuality = preferences }, CancellationToken.None);
            Settings = preferences;
            MessageText.Text = "";
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception) { MessageText.Text = "Cleanup preferences could not be saved."; }
        finally { _saving = false; UpdateModelCard(); }
    }
}
