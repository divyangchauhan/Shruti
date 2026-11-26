using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private bool _saving;
    private string? _directory;
    private readonly List<(LocalCleanupModel Model, ModelCatalogRow Row, Button Action)> _rows = [];

    public TranscriptQualityControl()
    {
        InitializeComponent();
        foreach (var model in new[] { LocalCleanupModel.Qwen, LocalCleanupModel.Utter })
        {
            var row = new ModelCatalogRow(model.Name, model.Details, _rows.Count > 0, model.LicenseUri);
            var action = new Button { IsEnabled = false };
            action.Click += async (_, _) => await ChooseModelAsync(model);
            row.AddAction(action);
            Models.Children.Add(row);
            _rows.Add((model, row, action));
        }
    }

    private async void CompareModels_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = "Compare cleanup models",
            Content = ((DataTemplate)Resources["CleanupBenchmark"]).LoadContent(),
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }

    internal void FocusFirstOption() => CleanupBox.Focus(FocusState.Programmatic);

    public void Configure(ISettingsRepository repository, TranscriptQualitySettings settings, string modelsDirectory)
    {
        _directory = Path.Combine(modelsDirectory, "Cleanup");
        _repository = repository;
        Settings = settings with { LocalRuntimePath = Path.Combine(AppContext.BaseDirectory, "CleanupRuntime", "llama-server.exe") };
        UpdateModelCard();
    }

    private string ModelPath(LocalCleanupModel model)
    {
        if (model == LocalCleanupModel.FromPath(Settings.LocalModelPath) && File.Exists(Settings.LocalModelPath))
            return Settings.LocalModelPath;
        return Path.Combine(_directory!, model.FileName);
    }

    private bool IsInstalled(LocalCleanupModel model) =>
        File.Exists(ModelPath(model)) && File.Exists(Settings.LocalRuntimePath);

    private bool IsActive(LocalCleanupModel model) =>
        Settings.EnableLocalModel && Settings.CleanupMode == TranscriptCleanupMode.Light &&
        model == LocalCleanupModel.FromPath(Settings.LocalModelPath) && File.Exists(Settings.LocalModelPath);

    private void UpdateModelCard()
    {
        if (_directory is null) return;
        string? activeModel = null;
        bool anyInstalled = false;
        foreach (var (model, row, action) in _rows)
        {
            bool installed = IsInstalled(model);
            bool active = installed && IsActive(model);
            anyInstalled |= installed;
            if (active) activeModel = model.Name;
            row.SetState(installed, active);
            string label = active ? "Disable" : installed ? "Set active" : "Download";
            action.Content = label;
            action.IsEnabled = !_saving && !_installing;
            AutomationProperties.SetName(action, $"{label} {model.Name} for cleanup");
        }
        ModelGuidance.Text = activeModel is not null ? $"{activeModel} is active for new dictations."
            : anyInstalled ? "Choose an installed model for new dictations."
            : "Download a model to add an editing pass.";
        DownloadProgress.Visibility = _installing ? Visibility.Visible : Visibility.Collapsed;
        CleanupBox.IsChecked = Settings.CleanupMode == TranscriptCleanupMode.Light;
        ContextBox.IsChecked = Settings.EnableCursorContext;
        CleanupBox.IsEnabled = ContextBox.IsEnabled = !_saving;
    }

    private async Task ChooseModelAsync(LocalCleanupModel model)
    {
        if (_directory is null || _installing || _saving) return;
        if (!IsInstalled(model))
        {
            await InstallAsync(model);
            return;
        }
        await SaveAsync(Settings with
        {
            LocalModelPath = ModelPath(model),
            EnableLocalModel = !IsActive(model),
            CleanupMode = TranscriptCleanupMode.Light
        });
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

    private async Task InstallAsync(LocalCleanupModel model)
    {
        if (_directory is null) return;
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
