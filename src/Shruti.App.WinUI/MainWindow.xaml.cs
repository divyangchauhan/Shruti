using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Runtime.InteropServices;
using Shruti.Workflow.Dictation;
using Shruti.Core;
using Shruti.Core.Audio;
using Shruti.Core.Diagnostics;
using Shruti.Core.Dictation;
using Shruti.Core.Triggers;
using Shruti.Models;
using Shruti.Platform.Windows;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Windows.Graphics;

namespace Shruti.App.WinUI;

public sealed partial class MainWindow : Window
{
    private const int WaveformBarCount = 28;
    private const double PreferredWindowWidthDip = 1080;
    private const double PreferredWindowHeightDip = 720;
    private const double DefaultDpi = 96;
    private const double PreferredTitleBarHeightDip = 40;
    private const int WorkAreaMarginPixels = 48;

    private readonly DictationShellController _controller;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly TranscriptionOptionsProvider _transcriptionOptionsProvider;
    private readonly ModelCatalog _modelCatalog;
    private readonly IModelManager _modelManager;
    private readonly DictationTriggerRouter _triggerRouter;
    private readonly WindowsTargetFocusService _targetFocusService;
    private readonly WindowsGlobalTriggerService _triggerService;
    private readonly WindowsTrayIconService _trayIconService;
    private readonly IWindowsWindowVisibility _windowVisibility;
    private readonly WindowsWindowMessageHost _windowMessageHost;
    private readonly DictationTriggerDispatcher _triggerDispatcher;
    private readonly CancellationTokenSource _triggerDispatchCancellation = new();
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly IntPtr _windowHandle;
    private readonly List<Border> _waveformBars = [];

    private Storyboard? _micPulseStoryboard;
    private Task? _triggerDispatchTask;
    private bool _allowClose;
    private bool _isDisposed;
    private bool _audioDevicesLoaded;
    private bool _settingsLoaded;
    private bool _isApplyingSettings;
    private bool _isApplyingModelSelection;
    private bool _isModelOperationRunning;
    private bool _isOnboardingModelOperation;
    private int _onboardingStep;
    private ComputeBackend _resolvedBackend = ComputeBackend.Cpu;
    private string _currentPage = "Home";
    private IReadOnlyList<InstalledModel> _installedModels = [];
    private IReadOnlyDictionary<string, IReadOnlySet<ComputeBackend>> _availableBackendsByModel =
        new Dictionary<string, IReadOnlySet<ComputeBackend>>(StringComparer.Ordinal);
    private ComputeBackend _modelBackendFilter = ComputeBackend.Auto;
    private ShrutiSettings _settings = ShrutiSettings.Default;

    public MainWindow(
        DictationShellController controller,
        IAudioCaptureService audioCaptureService,
        ISettingsRepository settingsRepository,
        TranscriptionOptionsProvider transcriptionOptionsProvider,
        ModelCatalog modelCatalog,
        IModelManager modelManager,
        DictationTriggerRouter triggerRouter,
        WindowsTargetFocusService targetFocusService,
        WindowsGlobalTriggerService triggerService,
        WindowsTrayIconService trayIconService,
        IWindowsWindowVisibility windowVisibility)
    {
        InitializeComponent();
        AppIcon.Apply(AppWindow);

        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _transcriptionOptionsProvider = transcriptionOptionsProvider ?? throw new ArgumentNullException(nameof(transcriptionOptionsProvider));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _triggerRouter = triggerRouter ?? throw new ArgumentNullException(nameof(triggerRouter));
        _targetFocusService = targetFocusService ?? throw new ArgumentNullException(nameof(targetFocusService));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
        _windowVisibility = windowVisibility ?? throw new ArgumentNullException(nameof(windowVisibility));
        _windowHandle = WindowNative.GetWindowHandle(this);
        _windowMessageHost = new WindowsWindowMessageHost(_windowHandle);
        _triggerDispatcher = new DictationTriggerDispatcher(_triggerService, _triggerRouter);

        _controller.StateChanged += Controller_StateChanged;
        _controller.AudioLevelChanged += Controller_AudioLevelChanged;
        _windowMessageHost.MessageReceived += WindowMessageHost_MessageReceived;
        _trayIconService.CommandInvoked += TrayIconService_CommandInvoked;
        AppWindow.Closing += AppWindow_Closing;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        Root.ActualThemeChanged += Root_ActualThemeChanged;
        ResizeForCurrentDisplay();
        ConfigureAppTitleBar();
        InitializeWaveform();

        InsertionModeComboBox.SelectedIndex = 0;
        ThemeComboBox.SelectedIndex = 0;
        AudioRetentionComboBox.SelectedIndex = 0;
        BackendPreferenceComboBox.SelectedIndex = 0;
        PopulateModelSelectionComboBox();
        ShowPage("Home");
        ConfigureNativeTriggers();
        StartTriggerDispatch();
        UpdateView();
    }

    public void ShowFromExternalActivation()
    {
        ShowMainWindow();
    }

    private void ResizeForCurrentDisplay()
    {
        uint dpi = GetDpiForWindow(_windowHandle);
        double dpiScale = dpi == 0 ? 1 : dpi / DefaultDpi;
        double desiredWidth = PreferredWindowWidthDip * dpiScale;
        double desiredHeight = PreferredWindowHeightDip * dpiScale;

        DisplayArea displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        double availableWidth = Math.Max(1, workArea.Width - WorkAreaMarginPixels);
        double availableHeight = Math.Max(1, workArea.Height - WorkAreaMarginPixels);
        double fitScale = Math.Min(1, Math.Min(availableWidth / desiredWidth, availableHeight / desiredHeight));

        AppWindow.Resize(new SizeInt32(
            checked((int)Math.Round(desiredWidth * fitScale)),
            checked((int)Math.Round(desiredHeight * fitScale))));
    }

    private async void PrimaryDictationButton_Click(object sender, RoutedEventArgs e)
    {
        await RaiseTriggerAsync(DictationTriggerKind.AppButton, "main-window");
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.CancelAsync();
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.PauseAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.RetryAsync();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.CopyTranscriptAsync();
    }

    private async void InsertPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.InsertPreviewAsync(
            TranscriptPreviewBox.Text,
            ReplaceSelectionCheckBox.IsChecked == true);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("Settings");
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
        {
            ShowPage(page);
        }
    }

    private async void InsertionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_controller is null)
        {
            return;
        }

        _controller.SetInsertionMode(GetSelectedInsertionMode());
        await PersistSettingsAsync();
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Root is null)
        {
            return;
        }

        Root.RequestedTheme = GetSelectedTheme();
        UpdateAppTitleBarTheme();
        await PersistSettingsAsync();
    }

    private async void AudioRetentionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await PersistSettingsAsync();
    }

    private async void BackendPreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateComputeButtons();
        await PersistSettingsAsync();
    }

    private void BackendButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string backend } button ||
            !Enum.TryParse(backend, out ComputeBackend selectedBackend))
        {
            return;
        }

        if (ReferenceEquals(button, BackendNpuButton) ||
            ReferenceEquals(button, BackendGpuButton) ||
            ReferenceEquals(button, BackendCpuButton) ||
            ReferenceEquals(button, BackendAutoButton))
        {
            _modelBackendFilter = selectedBackend;
            UpdateComputeButtons();
            RenderModelCatalog();
            return;
        }

        SelectComboBoxItem(BackendPreferenceComboBox, backend);
        UpdateComputeButtons();
    }

    private void ReplayWelcomeButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ReplayOnboardingAsync();
    }

    private void OnboardingNextButton_Click(object sender, RoutedEventArgs e)
    {
        SetOnboardingStep(Math.Min(4, _onboardingStep + 1));
    }

    private async void OnboardingMicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_audioDevicesLoaded)
        {
            await LoadAudioDevicesAsync();
        }

        if (_audioDevicesLoaded)
        {
            OnboardingMicStatusText.Text = "Microphone ready";
            SetOnboardingStep(2);
        }
        else
        {
            OnboardingMicStatusText.Text = DiagnosticFailureText.MicrophoneRecovery;
        }
    }

    private async void OnboardingModelActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOnboardingModelOperation)
        {
            return;
        }

        ModelCatalogEntry model = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        if (FindInstalledModel(model.Id) is not null)
        {
            SetOnboardingStep(3);
            return;
        }

        _isOnboardingModelOperation = true;
        OnboardingModelActionButton.IsEnabled = false;
        OnboardingModelProgressBar.Value = 0;
        OnboardingModelProgressBar.Visibility = Visibility.Visible;
        try
        {
            await RunModelInstallOperationAsync(
                model,
                $"Downloading {model.DisplayName}.",
                progress => _modelManager.DownloadAsync(model, progress, CancellationToken.None));
            if (FindInstalledModel(model.Id) is not null)
            {
                SetOnboardingStep(3);
            }
        }
        finally
        {
            _isOnboardingModelOperation = false;
            OnboardingModelActionButton.IsEnabled = true;
            OnboardingModelProgressBar.Visibility = Visibility.Collapsed;
            UpdateOnboardingModelState();
        }
    }

    private async void OnboardingFinishButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { HasCompletedOnboarding = true };
        await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
        OnboardingLayer.Visibility = Visibility.Collapsed;
    }

    private async void ModelSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingModelSelection || _isApplyingSettings)
        {
            return;
        }

        await PersistSettingsAsync();
    }

    private async void AllowSlowTranscriptionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        await PersistSettingsAsync();
    }

    private async void TriggerConfigurationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private async void TriggerConfigurationInput_LostFocus(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private void ChangeHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        PushToTalkKeyTextBox.Focus(FocusState.Programmatic);
        PushToTalkKeyTextBox.SelectAll();
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _ = RememberExternalTargetAfterDeactivationAsync();
            return;
        }

        await EnsureSettingsLoadedAsync();
        await RefreshInstalledModelsAsync();
        await RefreshTranscriptionReadinessAsync();
        if (!_audioDevicesLoaded)
        {
            await LoadAudioDevicesAsync();
        }
    }

    private async Task RememberExternalTargetAfterDeactivationAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(125)).ConfigureAwait(false);
            if (!_isDisposed)
            {
                await _targetFocusService
                    .RememberCurrentForegroundTargetAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort cache refresh; dictation can still fall back to preview.
        }
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateAppTitleBarTheme();
        UpdateComputeButtons();
        SetNavigationButtonState(HomeNavButton, string.Equals(_currentPage, "Home", StringComparison.OrdinalIgnoreCase));
        SetNavigationButtonState(ModelsNavButton, string.Equals(_currentPage, "Models", StringComparison.OrdinalIgnoreCase));
        SetNavigationButtonState(SettingsNavButton, string.Equals(_currentPage, "Settings", StringComparison.OrdinalIgnoreCase));
        RenderModelCatalog();
        UpdateView();
    }

    private void ConfigureAppTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        SetTitleBar(AppTitleBar);
        UpdateAppTitleBarLayout();
        UpdateAppTitleBarTheme();
    }

    private void UpdateAppTitleBarLayout()
    {
        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        uint dpi = GetDpiForWindow(_windowHandle);
        double scale = dpi == 0 ? 1 : dpi / DefaultDpi;

        double rightInset = titleBar.RightInset > 0 ? titleBar.RightInset / scale : 140;
        TitleBarRow.Height = new GridLength(PreferredTitleBarHeightDip);
        CaptionButtonInsetColumn.Width = new GridLength(rightInset);
    }

    private void UpdateAppTitleBarTheme()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool isDark = Root.ActualTheme == ElementTheme.Dark;
        Windows.UI.Color background = isDark
            ? Windows.UI.Color.FromArgb(255, 30, 28, 25)
            : Windows.UI.Color.FromArgb(255, 255, 255, 255);
        Windows.UI.Color foreground = isDark
            ? Windows.UI.Color.FromArgb(255, 243, 241, 238)
            : Windows.UI.Color.FromArgb(255, 30, 28, 25);
        Windows.UI.Color mutedForeground = isDark
            ? Windows.UI.Color.FromArgb(255, 169, 164, 155)
            : Windows.UI.Color.FromArgb(255, 92, 88, 80);
        Windows.UI.Color hoverBackground = isDark
            ? Windows.UI.Color.FromArgb(255, 42, 39, 35)
            : Windows.UI.Color.FromArgb(255, 243, 241, 238);
        Windows.UI.Color pressedBackground = isDark
            ? Windows.UI.Color.FromArgb(255, 52, 48, 43)
            : Windows.UI.Color.FromArgb(255, 231, 228, 223);

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = mutedForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = mutedForeground;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        _windowVisibility.Hide(_windowHandle);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        DisposeNativeTriggers();
    }

    private bool WindowMessageHost_MessageReceived(WindowsWindowMessage message)
    {
        return _triggerService.HandleWindowMessage(message.Id, message.WParam) ||
            _trayIconService.HandleWindowMessage(message);
    }

    private void Controller_StateChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(UpdateView);
    }

    private void Controller_AudioLevelChanged(object? sender, AudioLevelFrame level)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AudioLevelBar.Value = Math.Clamp(level.Peak * 100, AudioLevelBar.Minimum, AudioLevelBar.Maximum);
            UpdateAudioWaveform(level.Peak);
        });
    }

    private async Task LoadAudioDevicesAsync()
    {
        try
        {
            IReadOnlyList<AudioInputDevice> devices = await _audioCaptureService
                .ListInputDevicesAsync(CancellationToken.None);

            AudioDeviceComboBox.Items.Clear();
            foreach (AudioInputDevice device in devices)
            {
                AudioDeviceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = device.DisplayName,
                    Tag = device.Id
                });
            }

            if (devices.Count == 0)
            {
                AudioDeviceComboBox.PlaceholderText = "No microphone available";
                MicrophoneReadinessText.Text = "No microphone found";
                TriggerStatusText.Text = DiagnosticFailureText.MicrophoneRecovery;
                return;
            }

            string selectedDeviceId = _controller.AudioOptions.DeviceId ??
                devices.FirstOrDefault(device => device.IsDefault)?.Id ??
                devices[0].Id;
            SelectAudioDevice(selectedDeviceId);
            _audioDevicesLoaded = true;
            MicrophoneReadinessText.Text = "Ready";
        }
        catch (Exception ex)
        {
            AudioDeviceComboBox.PlaceholderText = "Microphone unavailable";
            MicrophoneReadinessText.Text = "Unavailable";
            TriggerStatusText.Text = DiagnosticFailureText.MicrophoneRecovery;
            AutomationProperties.SetHelpText(AudioDeviceComboBox, DiagnosticTextRedactor.Redact(ex.Message));
        }
    }

    private async void AudioDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AudioDeviceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string deviceId)
        {
            _controller.SetAudioInputDevice(deviceId);
            await PersistSettingsAsync();
        }
    }

    private void SelectAudioDevice(string deviceId)
    {
        for (int index = 0; index < AudioDeviceComboBox.Items.Count; index++)
        {
            if (AudioDeviceComboBox.Items[index] is ComboBoxItem item && item.Tag is string candidateId &&
                string.Equals(candidateId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                AudioDeviceComboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private void PopulateModelSelectionComboBox()
    {
        _isApplyingModelSelection = true;
        try
        {
            ModelSelectionComboBox.Items.Clear();
            foreach (ModelCatalogEntry model in _modelCatalog.Models)
            {
                ModelSelectionComboBox.Items.Add(new ComboBoxItem
                {
                    Content = model.DisplayName,
                    Tag = model.Id
                });
            }

            SelectModelComboBoxItem(ShrutiSettings.DefaultModelId);
        }
        finally
        {
            _isApplyingModelSelection = false;
        }
    }

    private void SelectModelComboBoxItem(string modelId)
    {
        _isApplyingModelSelection = true;
        try
        {
            for (int index = 0; index < ModelSelectionComboBox.Items.Count; index++)
            {
                if (ModelSelectionComboBox.Items[index] is ComboBoxItem item &&
                    item.Tag is string candidateId &&
                    string.Equals(candidateId, modelId, StringComparison.Ordinal))
                {
                    ModelSelectionComboBox.SelectedIndex = index;
                    return;
                }
            }

            SelectComboBoxItem(ModelSelectionComboBox, ShrutiSettings.DefaultModelId);
        }
        finally
        {
            _isApplyingModelSelection = false;
        }
    }

    private string GetSelectedModelId()
    {
        if (ModelSelectionComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string modelId &&
            _transcriptionOptionsProvider.FindModel(modelId) is not null)
        {
            return modelId;
        }

        return ShrutiSettings.DefaultModelId;
    }

    private DictationInsertionMode GetSelectedInsertionMode()
    {
        if (InsertionModeComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string value &&
            Enum.TryParse(value, out DictationInsertionMode insertionMode))
        {
            return insertionMode;
        }

        return DictationInsertionMode.AutoInsert;
    }

    private void UpdateView()
    {
        DictationShellState state = _controller.State;

        StateText.Text = FormatState(state.SessionState);
        StatusText.Text = FormatPrimaryStatus(state);
        StatusSubText.Text = FormatSecondaryStatus(state);
        TargetText.Text = state.TargetDescription;
        UserMessageText.Text = state.UserMessage;
        TranscriptPreviewBox.Text = state.TranscriptPreview;

        string errorText = FormatErrorText(state);
        ErrorText.Text = errorText;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(errorText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PrimaryButtonLabel.Text = state.IsRunning ? "Stop dictation" : "Start dictation";
        PrimaryButtonIcon.Glyph = state.IsRunning ? "\uE71A" : "\uE720";
        ApplyMicrophoneVisualState(state);
        AutomationProperties.SetName(PrimaryDictationButton, PrimaryButtonLabel.Text);
        AutomationProperties.SetHelpText(PrimaryDictationButton, state.IsRunning
            ? "Stop recording and finalize the current dictation."
            : "Start recording from the selected microphone.");
        PrimaryDictationButton.IsEnabled = state.CanStart || state.CanStop;
        CancelButton.IsEnabled = state.CanCancel;
        CancelButton.Visibility = state.CanCancel ? Visibility.Visible : Visibility.Collapsed;
        PauseButtonLabel.Text = state.IsPaused ? "Resume" : "Pause";
        AutomationProperties.SetName(PauseButton, state.IsPaused ? "Resume recording" : "Pause recording");
        PauseButton.IsEnabled = state.CanPause;
        PauseButton.Visibility = state.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneReadinessPill.Visibility = state.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        RetryButton.IsEnabled = state.CanRetry;
        RetryButton.Visibility = state.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = state.CanCopy;
        CopyButton.Visibility = state.CanCopy ? Visibility.Visible : Visibility.Collapsed;
        InsertPreviewButton.IsEnabled = state.CanInsertPreview;
        InsertPreviewButton.Visibility = state.CanInsertPreview ? Visibility.Visible : Visibility.Collapsed;
        ReplaceSelectionCheckBox.IsEnabled = state.CanInsertPreview;
        ReplaceSelectionCheckBox.Visibility = state.CanInsertPreview
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!state.CanInsertPreview)
        {
            ReplaceSelectionCheckBox.IsChecked = false;
        }

        TranscriptPreviewBox.IsReadOnly = !state.CanInsertPreview;
        InsertionModeComboBox.IsEnabled = !state.IsRunning;
        AudioDeviceComboBox.IsEnabled = !state.IsRunning && _audioDevicesLoaded;
        if (!state.IsRunning)
        {
            AudioLevelBar.Value = 0;
            UpdateAudioWaveform(0);
        }

        StatusPillText.Text = state.SessionState == DictationSessionState.Idle
            ? "Ready"
            : FormatState(state.SessionState);
        ListeningNavDot.Visibility = state.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        bool hasRecent = !string.IsNullOrWhiteSpace(state.TranscriptPreview) ||
            state.CanRetry ||
            state.CanCopy ||
            state.CanInsertPreview;
        CurrentTranscriptCard.Visibility = hasRecent ? Visibility.Visible : Visibility.Collapsed;
        EmptyRecentCard.Visibility = hasRecent ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(StateText, $"State: {FormatState(state.SessionState)}");
        AutomationProperties.SetName(StatusText, $"Status: {FormatPrimaryStatus(state)}");
        AutomationProperties.SetName(UserMessageText, state.UserMessage);
        AutomationProperties.SetName(TargetText, $"Target: {state.TargetDescription}");
        DiagnosticsSnapshotText.Text = FormatDiagnosticsSnapshot(_controller.LastResult);

        _trayIconService.UpdateDictationState(state.IsRunning);
    }

    private string FormatPrimaryStatus(DictationShellState state)
    {
        string shortcut = string.IsNullOrWhiteSpace(_triggerService.Configuration.PushToTalkKey)
            ? "your shortcut"
            : _triggerService.Configuration.PushToTalkKey;
        return state.SessionState switch
        {
            DictationSessionState.Recording => "Listening…",
            DictationSessionState.Paused => "Paused",
            DictationSessionState.TranscribingFinalAudio => "Catching up…",
            DictationSessionState.InsertingText => "Adding your words…",
            DictationSessionState.PreparingTarget => "Getting ready…",
            DictationSessionState.RequestingMicrophone => "Checking your microphone…",
            DictationSessionState.Failed => "Something needs attention",
            DictationSessionState.Cancelled => "Dictation cancelled",
            _ => $"Hold {shortcut} to dictate anywhere"
        };
    }

    private string FormatSecondaryStatus(DictationShellState state)
    {
        ModelCatalogEntry model = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        string compute = (_settings.BackendPreference == ComputeBackend.Auto
            ? "automatic compute"
            : _settings.BackendPreference.ToString().ToUpperInvariant());
        return state.SessionState switch
        {
            DictationSessionState.Recording => "Speak naturally — your audio stays on this PC",
            DictationSessionState.Paused => "Resume when you're ready",
            DictationSessionState.TranscribingFinalAudio => $"Transcribing with {model.DisplayName} · nothing leaves this PC",
            DictationSessionState.InsertingText => "Returning your text to the app where you were typing",
            DictationSessionState.Failed => state.UserMessage,
            _ => $"{model.DisplayName} · on this PC · {compute}"
        };
    }

    private void InitializeWaveform()
    {
        AudioWaveformPanel.Children.Clear();
        _waveformBars.Clear();
        for (int index = 0; index < WaveformBarCount; index++)
        {
            double idleHeight = 4 + ((index * 7) % 5);
            var bar = new Border
            {
                Width = 3,
                Height = idleHeight,
                VerticalAlignment = VerticalAlignment.Center,
                Background = GetBrush("ShrutiBorderStrongBrush"),
                CornerRadius = new CornerRadius(2),
                Opacity = 0
            };
            _waveformBars.Add(bar);
            AudioWaveformPanel.Children.Add(bar);
        }
    }

    private void UpdateAudioWaveform(double peak)
    {
        bool active = _controller.State.SessionState == DictationSessionState.Recording;
        double normalizedPeak = Math.Clamp(peak, 0, 1);
        for (int index = 0; index < _waveformBars.Count; index++)
        {
            double shape = 0.35 + (Math.Abs(Math.Sin(index * 1.73)) * 0.65);
            _waveformBars[index].Height = active
                ? Math.Max(4, 5 + (normalizedPeak * 23 * shape))
                : 4 + ((index * 7) % 5);
            _waveformBars[index].Background = GetBrush(
                active ? "ShrutiAccentVividBrush" : "ShrutiBorderStrongBrush");
            _waveformBars[index].Opacity = active ? 1 : 0;
        }
    }

    private void ApplyMicrophoneVisualState(DictationShellState state)
    {
        string backgroundKey;
        string foregroundKey;
        string borderKey;
        bool pulse = false;
        switch (state.SessionState)
        {
            case DictationSessionState.Recording:
                backgroundKey = "ShrutiAccentVividBrush";
                foregroundKey = "ShrutiAccentForegroundBrush";
                borderKey = "ShrutiAccentVividBrush";
                pulse = true;
                break;
            case DictationSessionState.Paused:
                backgroundKey = "ShrutiWarningSoftBrush";
                foregroundKey = "ShrutiWarningBrush";
                borderKey = "ShrutiWarningSoftBrush";
                break;
            case DictationSessionState.Failed:
                backgroundKey = "ShrutiDangerSoftBrush";
                foregroundKey = "ShrutiDangerBrush";
                borderKey = "ShrutiDangerSoftBrush";
                break;
            case DictationSessionState.TranscribingFinalAudio:
            case DictationSessionState.InsertingText:
                backgroundKey = "ShrutiCardBrush";
                foregroundKey = "ShrutiAccentBrush";
                borderKey = "ShrutiBorderStrongBrush";
                break;
            default:
                backgroundKey = "ShrutiCardBrush";
                foregroundKey = "ShrutiTextSecondaryBrush";
                borderKey = "ShrutiBorderStrongBrush";
                break;
        }

        PrimaryDictationButton.Background = GetBrush(backgroundKey);
        PrimaryDictationButton.Foreground = GetBrush(foregroundKey);
        PrimaryDictationButton.BorderBrush = GetBrush(borderKey);
        if (pulse)
        {
            StartMicPulse();
        }
        else
        {
            StopMicPulse();
        }
    }

    private void StartMicPulse()
    {
        if (_micPulseStoryboard is not null)
        {
            return;
        }

        var duration = new Duration(TimeSpan.FromSeconds(1.6));
        var scaleX = new DoubleAnimation { From = 1, To = 1.35, Duration = duration };
        var scaleY = new DoubleAnimation { From = 1, To = 1.35, Duration = duration };
        var opacity = new DoubleAnimation { From = 0.38, To = 0, Duration = duration };
        Storyboard.SetTarget(scaleX, MicPulseScale);
        Storyboard.SetTarget(scaleY, MicPulseScale);
        Storyboard.SetTarget(opacity, MicPulseRing);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        Storyboard.SetTargetProperty(opacity, "Opacity");
        _micPulseStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        _micPulseStoryboard.Children.Add(scaleX);
        _micPulseStoryboard.Children.Add(scaleY);
        _micPulseStoryboard.Children.Add(opacity);
        _micPulseStoryboard.Begin();
    }

    private void StopMicPulse()
    {
        _micPulseStoryboard?.Stop();
        _micPulseStoryboard = null;
        MicPulseScale.ScaleX = 1;
        MicPulseScale.ScaleY = 1;
        MicPulseRing.Opacity = 0;
    }

    private SolidColorBrush GetBrush(string key)
    {
        ResourceDictionary applicationResources = Application.Current.Resources;
        string themeKey = Root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        if (applicationResources.ThemeDictionaries.TryGetValue(themeKey, out object? themeResources) &&
            themeResources is ResourceDictionary themeDictionary &&
            themeDictionary.TryGetValue(key, out object? themeValue) &&
            themeValue is SolidColorBrush themeBrush)
        {
            return themeBrush;
        }

        return (SolidColorBrush)applicationResources[key];
    }

    private ElementTheme GetSelectedTheme()
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string value &&
            Enum.TryParse(value, out ElementTheme theme))
        {
            return theme;
        }

        return ElementTheme.Default;
    }

    private AppThemePreference GetSelectedThemePreference()
    {
        return GetSelectedTheme() switch
        {
            ElementTheme.Light => AppThemePreference.Light,
            ElementTheme.Dark => AppThemePreference.Dark,
            _ => AppThemePreference.System
        };
    }

    private AudioRetentionPolicy GetSelectedAudioRetentionPolicy()
    {
        if (AudioRetentionComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string value &&
            Enum.TryParse(value, out AudioRetentionPolicy policy))
        {
            return policy;
        }

        return AudioRetentionPolicy.DeleteAfterTranscription;
    }

    private ComputeBackend GetSelectedBackendPreference()
    {
        if (BackendPreferenceComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string value &&
            Enum.TryParse(value, out ComputeBackend backend))
        {
            return backend;
        }

        return ComputeBackend.Auto;
    }

    private async Task RefreshInstalledModelsAsync()
    {
        try
        {
            _installedModels = await _modelManager.ListInstalledAsync(CancellationToken.None);
            ModelsDirectoryText.Text = $"Storage: {_modelManager.ModelsDirectory}";
            RenderModelCatalog();
            UpdateSelectedModelStatus();
            UpdateOnboardingModelState();
        }
        catch (Exception ex)
        {
            ModelsStatusText.Text = $"Models could not be loaded: {DiagnosticTextRedactor.Redact(ex.Message)}";
            SelectedModelStatusText.Text = "Model status unavailable.";
        }
    }

    private void RenderModelCatalog()
    {
        ModelListPanel.Children.Clear();
        int installedCount = _modelCatalog.Models.Count(model => FindInstalledModel(model.Id) is not null);
        IReadOnlyList<ModelCatalogEntry> visibleModels = _modelCatalog.Models
            .Where(model => ModelCatalogFiltering.IsVisibleForBackend(
                model,
                _settings.SelectedModelId,
                _modelBackendFilter,
                GetAvailableBackends(model)))
            .ToArray();
        if (!_isModelOperationRunning)
        {
            if (_modelBackendFilter == ComputeBackend.Auto)
            {
                ModelsStatusText.Text = installedCount == 1
                    ? "1 model on this PC"
                    : $"{installedCount} models on this PC";
            }
            else
            {
                int compatibleCount = visibleModels.Count(model =>
                    GetAvailableBackends(model).Contains(_modelBackendFilter));
                ModelsStatusText.Text = compatibleCount == 1
                    ? $"1 model for {_modelBackendFilter.ToString().ToUpperInvariant()}"
                    : $"{compatibleCount} models for {_modelBackendFilter.ToString().ToUpperInvariant()}";
            }
        }

        foreach (ModelCatalogEntry model in visibleModels)
        {
            ModelListPanel.Children.Add(CreateModelCard(model));
        }
    }

    private Border CreateModelCard(ModelCatalogEntry model)
    {
        InstalledModel? installed = FindInstalledModel(model.Id);
        bool isInstalled = installed is not null;
        bool isSelected = string.Equals(_settings.SelectedModelId, model.Id, StringComparison.Ordinal);
        ComputeBackend compute = isSelected
            ? _settings.BackendPreference == ComputeBackend.Auto
                ? _resolvedBackend
                : _settings.BackendPreference
            : _modelBackendFilter != ComputeBackend.Auto && GetAvailableBackends(model).Contains(_modelBackendFilter)
                ? _modelBackendFilter
                : model.SupportedBackends.FirstOrDefault(ComputeBackend.Cpu);

        var card = new Border
        {
            Style = (Style)Root.Resources["CardBorderStyle"],
            Padding = new Thickness(20, 16, 20, 16),
            BorderBrush = GetBrush(isSelected ? "ShrutiAccentBrush" : "ShrutiBorderSubtleBrush")
        };
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var details = new StackPanel { Spacing = 5 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = model.DisplayName,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        if (isSelected)
        {
            titleRow.Children.Add(CreateBadge("Active", "ShrutiAccentBrush", "ShrutiAccentSoftBrush"));
        }
        else if (isInstalled)
        {
            titleRow.Children.Add(CreateBadge("✓ Installed", "ShrutiSuccessBrush", "ShrutiSuccessSoftBrush"));
        }

        (string computeForeground, string computeBackground) = GetComputeBrushKeys(compute);
        titleRow.Children.Add(CreateBadge(compute.ToString().ToUpperInvariant(), computeForeground, computeBackground));
        details.Children.Add(titleRow);
        details.Children.Add(new TextBlock
        {
            Text = FormatModelDetails(model),
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Style = (Style)Root.Resources["MutedTextStyle"]
        });
        grid.Children.Add(details);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);

        if (isSelected)
        {
            actions.Children.Add(CreateBadge("In use", "ShrutiAccentBrush", "ShrutiAccentSoftBrush"));
        }
        else if (!isInstalled)
        {
            var selectButton = new Button
            {
                Content = "Set active",
                Tag = model.Id,
                IsEnabled = isInstalled && !_isModelOperationRunning
            };
            ToolTipService.SetToolTip(selectButton, isInstalled
                ? $"Use {model.DisplayName} for dictation."
                : "Install this model before selecting it.");
            selectButton.Click += SelectModelButton_Click;
            actions.Children.Add(selectButton);
        }

        if (!isInstalled)
        {
            var downloadButton = new Button
            {
                Content = "Download",
                Tag = model.Id,
                IsEnabled = (model.DownloadUri is not null || model.IsBundle) && !_isModelOperationRunning
            };
            ToolTipService.SetToolTip(downloadButton, model.DownloadUri is null && !model.IsBundle
                ? "This catalog entry does not have a download URL."
                : $"Download and verify {model.DisplayName}.");
            downloadButton.Click += DownloadModelButton_Click;
            actions.Children.Add(downloadButton);

            if (!model.IsBundle)
            {
                var importButton = new Button
                {
                    Content = "Import",
                    Tag = model.Id,
                    IsEnabled = !_isModelOperationRunning
                };
                ToolTipService.SetToolTip(importButton, $"Import a local file for {model.DisplayName} and verify it.");
                importButton.Click += ImportModelButton_Click;
                actions.Children.Add(importButton);
            }
        }
        else if (!isSelected)
        {
            var removeButton = new Button
            {
                Content = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 15,
                    Glyph = "\uE74D"
                },
                Tag = model.Id,
                IsEnabled = !_isModelOperationRunning,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0)
            };
            AutomationProperties.SetName(removeButton, $"Remove {model.DisplayName}");
            ToolTipService.SetToolTip(removeButton, $"Remove {model.DisplayName} from local storage.");
            removeButton.Click += RemoveModelButton_Click;
            actions.Children.Add(removeButton);

            var selectButton = new Button
            {
                Content = "Set active",
                Tag = model.Id,
                IsEnabled = !_isModelOperationRunning
            };
            ToolTipService.SetToolTip(selectButton, $"Use {model.DisplayName} for dictation.");
            selectButton.Click += SelectModelButton_Click;
            actions.Children.Add(selectButton);
        }

        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private Border CreateBadge(string text, string foregroundKey, string backgroundKey)
    {
        return new Border
        {
            Height = 22,
            Padding = new Thickness(9, 0, 9, 0),
            Background = GetBrush(backgroundKey),
            CornerRadius = new CornerRadius(11),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = GetBrush(foregroundKey),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static (string Foreground, string Background) GetComputeBrushKeys(ComputeBackend backend)
    {
        return backend switch
        {
            ComputeBackend.Npu => ("ShrutiNpuBrush", "ShrutiNpuSoftBrush"),
            ComputeBackend.Gpu => ("ShrutiGpuBrush", "ShrutiGpuSoftBrush"),
            ComputeBackend.Cpu => ("ShrutiCpuBrush", "ShrutiCpuSoftBrush"),
            _ => ("ShrutiTextSecondaryBrush", "ShrutiSunkenBrush")
        };
    }

    private InstalledModel? FindInstalledModel(string modelId)
    {
        return _installedModels.FirstOrDefault(model =>
            model.IsAvailable &&
            string.Equals(model.CatalogEntry.Id, modelId, StringComparison.Ordinal));
    }

    private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetModelFromSender(sender, out ModelCatalogEntry model))
        {
            await RunModelInstallOperationAsync(
                model,
                $"Downloading {model.DisplayName}.",
                progress => _modelManager.DownloadAsync(model, progress, CancellationToken.None));
        }
    }

    private async void ImportModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetModelFromSender(sender, out ModelCatalogEntry model))
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".bin");
        picker.FileTypeFilter.Add(".gguf");
        InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await RunModelInstallOperationAsync(
            model,
            $"Importing {model.DisplayName}.",
            _ => _modelManager.ImportAsync(new ModelImportRequest(model, file.Path), CancellationToken.None),
            showDeterminateProgress: false);
    }

    private async void SelectModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetModelFromSender(sender, out ModelCatalogEntry model) &&
            FindInstalledModel(model.Id) is not null)
        {
            await SetSelectedModelAsync(model.Id);
            ModelsStatusText.Text = $"{model.DisplayName} selected for dictation.";
        }
    }

    private async void RemoveModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetModelFromSender(sender, out ModelCatalogEntry model) ||
            string.Equals(_settings.SelectedModelId, model.Id, StringComparison.Ordinal))
        {
            return;
        }

        _isModelOperationRunning = true;
        ModelsStatusText.Text = $"Removing {model.DisplayName}.";
        RenderModelCatalog();
        try
        {
            bool removed = await _modelManager.RemoveAsync(model.Id, CancellationToken.None);
            ModelsStatusText.Text = removed
                ? $"{model.DisplayName} removed from local storage."
                : $"{model.DisplayName} was not installed.";
        }
        catch (Exception ex)
        {
            ModelsStatusText.Text = $"Remove failed: {DiagnosticTextRedactor.Redact(ex.Message)}";
        }
        finally
        {
            _isModelOperationRunning = false;
            await RefreshInstalledModelsAsync();
            await RefreshTranscriptionReadinessAsync();
        }
    }

    private bool TryGetModelFromSender(object sender, out ModelCatalogEntry model)
    {
        model = null!;
        if (sender is FrameworkElement { Tag: string modelId })
        {
            ModelCatalogEntry? found = _transcriptionOptionsProvider.FindModel(modelId);
            if (found is not null)
            {
                model = found;
                return true;
            }
        }

        return false;
    }

    private async Task RunModelInstallOperationAsync(
        ModelCatalogEntry model,
        string statusText,
        Func<IProgress<ModelDownloadProgress>, Task<ModelInstallResult>> operation,
        bool showDeterminateProgress = true)
    {
        if (_isModelOperationRunning)
        {
            return;
        }

        _isModelOperationRunning = true;
        ModelsStatusText.Text = statusText;
        ModelDownloadProgressBar.Value = 0;
        ModelDownloadProgressBar.IsIndeterminate = !showDeterminateProgress;
        ModelDownloadProgressBar.Visibility = Visibility.Visible;
        RenderModelCatalog();

        var progress = new Progress<ModelDownloadProgress>(ReportModelDownloadProgress);
        string? finalStatusText = null;
        try
        {
            ModelInstallResult result = await operation(progress);
            if (result.Succeeded)
            {
                await RefreshInstalledModelsAsync();
                await SetSelectedModelAsync(model.Id, refreshModels: false);
                finalStatusText = $"{model.DisplayName} installed and selected for dictation.";
            }
            else
            {
                finalStatusText = string.IsNullOrWhiteSpace(result.Message)
                    ? $"{model.DisplayName} could not be installed."
                    : DiagnosticTextRedactor.Redact(result.Message);
            }
        }
        catch (Exception ex)
        {
            finalStatusText = $"Install failed: {DiagnosticTextRedactor.Redact(ex.Message)}";
        }
        finally
        {
            _isModelOperationRunning = false;
            ModelDownloadProgressBar.Visibility = Visibility.Collapsed;
            await RefreshInstalledModelsAsync();
            await RefreshTranscriptionReadinessAsync();
            if (!string.IsNullOrWhiteSpace(finalStatusText))
            {
                ModelsStatusText.Text = finalStatusText;
            }
        }
    }

    private void ReportModelDownloadProgress(ModelDownloadProgress progress)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (progress.Fraction is double fraction)
            {
                ModelDownloadProgressBar.IsIndeterminate = false;
                ModelDownloadProgressBar.Value = Math.Clamp(fraction * 100, 0, 100);
                OnboardingModelProgressBar.IsIndeterminate = false;
                OnboardingModelProgressBar.Value = Math.Clamp(fraction * 100, 0, 100);
            }
            else
            {
                ModelDownloadProgressBar.IsIndeterminate = true;
                OnboardingModelProgressBar.IsIndeterminate = true;
            }
        });
    }

    private async Task SetSelectedModelAsync(string modelId, bool refreshModels = true)
    {
        if (_transcriptionOptionsProvider.FindModel(modelId) is null)
        {
            return;
        }

        ModelCatalogEntry selectedModel = _transcriptionOptionsProvider.FindModel(modelId)!;
        ComputeBackend backendPreference = _settings.BackendPreference;
        IReadOnlySet<ComputeBackend> availableBackends = GetAvailableBackends(selectedModel);
        if (_modelBackendFilter != ComputeBackend.Auto && availableBackends.Contains(_modelBackendFilter))
        {
            backendPreference = _modelBackendFilter;
        }
        else if (backendPreference != ComputeBackend.Auto && !availableBackends.Contains(backendPreference))
        {
            backendPreference = ComputeBackend.Auto;
        }

        _settings = _settings with
        {
            SelectedModelId = modelId,
            BackendPreference = backendPreference
        };
        _transcriptionOptionsProvider.ApplySettings(_settings);
        SelectModelComboBoxItem(modelId);
        SelectComboBoxItem(BackendPreferenceComboBox, backendPreference.ToString());
        await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
        UpdateSelectedModelStatus();
        ModelSummaryText.Text = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings).DisplayName;
        UpdateOnboardingModelState();
        await RefreshTranscriptionReadinessAsync();
        if (refreshModels)
        {
            RenderModelCatalog();
        }
    }

    private void UpdateSelectedModelStatus()
    {
        ModelCatalogEntry selected = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        InstalledModel? installed = FindInstalledModel(selected.Id);
        SettingsActiveModelNameText.Text = "Active model";
        string compute = _settings.BackendPreference == ComputeBackend.Auto
            ? _resolvedBackend.ToString().ToUpperInvariant()
            : _settings.BackendPreference.ToString().ToUpperInvariant();
        SelectedModelStatusText.Text = installed is null
            ? $"{selected.DisplayName} · not installed"
            : $"{selected.DisplayName} · {compute} · on this PC";
    }

    private static string FormatModelDetails(ModelCatalogEntry model)
    {
        string backends = string.Join(
            ", ",
            model.SupportedBackends
                .Where(backend => backend != ComputeBackend.Auto)
                .DefaultIfEmpty(ComputeBackend.Cpu));
        return $"{model.LanguageHint.ToUpperInvariant()} · {FormatBytes(model.SizeBytes)} · {model.FileFormat} · {backends}";
    }

    private static string FormatBytes(long bytes)
    {
        double mebibytes = bytes / 1024d / 1024d;
        return $"{mebibytes:0.#} MB";
    }

    private async Task RaiseTriggerAsync(DictationTriggerKind kind, string sourceId)
    {
        await _triggerRouter.HandleAsync(new DictationTriggerEvent(
            kind,
            DateTimeOffset.UtcNow,
            SourceId: sourceId));
    }

    private void ConfigureNativeTriggers()
    {
        var errors = new List<string>();

        try
        {
            _triggerService.AttachWindow(_windowHandle);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        try
        {
            _trayIconService.AttachWindow(_windowHandle);
            _trayIconService.SetDictationCommandsEnabled(_triggerService.Configuration.EnableTrayMenu);
            _trayIconService.SetVisible(isVisible: true);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        TriggerStatusText.Text = errors.Count == 0
            ? "Triggers are active."
            : string.Join(" ", errors);
    }

    private void StartTriggerDispatch()
    {
        _triggerDispatchTask = DispatchTriggersAsync(_triggerDispatchCancellation.Token);
    }

    private async Task DispatchTriggersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _triggerDispatcher.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() => TriggerStatusText.Text = DiagnosticTextRedactor.Redact(ex.Message));
        }
    }

    private async Task ApplyTriggerConfigurationAsync(bool persist = true)
    {
        TriggerConfiguration configuration = GetTriggerConfigurationFromControls();

        try
        {
            await _triggerService.ConfigureAsync(configuration, CancellationToken.None);
            _trayIconService.SetDictationCommandsEnabled(configuration.EnableTrayMenu);
            _trayIconService.SetVisible(isVisible: true);
            HoldShortcutText.Text = string.IsNullOrWhiteSpace(configuration.PushToTalkKey)
                ? "Hold shortcut"
                : configuration.PushToTalkKey;
            UpdateView();
            TriggerStatusText.Text = "Triggers are active.";
            if (persist)
            {
                await PersistSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            ApplyTriggerConfigurationToControls(_triggerService.Configuration);
            TriggerStatusText.Text = DiagnosticTextRedactor.Redact(ex.Message);
        }
    }

    private TriggerConfiguration GetTriggerConfigurationFromControls()
    {
        return new TriggerConfiguration(
            EnableGlobalHotkey: GlobalHotkeyCheckBox.IsOn,
            EnablePushToTalk: PushToTalkCheckBox.IsOn,
            EnableFloatingButton: false,
            EnableTrayMenu: TrayMenuCheckBox.IsOn,
            HotkeyGesture: HotkeyGestureTextBox.Text,
            PushToTalkKey: PushToTalkKeyTextBox.Text,
            EnableFloatingWindowShortcut: false,
            FloatingWindowShortcut: null);
    }

    private void ApplyTriggerConfigurationToControls(TriggerConfiguration configuration)
    {
        GlobalHotkeyCheckBox.IsOn = configuration.EnableGlobalHotkey;
        PushToTalkCheckBox.IsOn = configuration.EnablePushToTalk;
        TrayMenuCheckBox.IsOn = configuration.EnableTrayMenu;
        HotkeyGestureTextBox.Text = configuration.HotkeyGesture ?? string.Empty;
        PushToTalkKeyTextBox.Text = configuration.PushToTalkKey ?? string.Empty;
        HoldShortcutText.Text = string.IsNullOrWhiteSpace(configuration.PushToTalkKey)
            ? "Hold shortcut"
            : configuration.PushToTalkKey;
    }

    private async Task EnsureSettingsLoadedAsync()
    {
        if (_settingsLoaded)
        {
            return;
        }

        await _settingsGate.WaitAsync();
        try
        {
            if (_settingsLoaded)
            {
                return;
            }

            try
            {
                _settings = await _settingsRepository.LoadAsync(CancellationToken.None);
                if (_transcriptionOptionsProvider.FindModel(_settings.SelectedModelId) is null)
                {
                    _settings = _settings with { SelectedModelId = ShrutiSettings.DefaultModelId };
                }

                _isApplyingSettings = true;
                _transcriptionOptionsProvider.ApplySettings(_settings);
                ApplySettingsToControls(_settings);
                _controller.SetInsertionMode(_settings.InsertionMode);
                _controller.SetAudioInputDevice(_settings.AudioInputDeviceId);
                Root.RequestedTheme = ToElementTheme(_settings.ThemePreference);
                await RefreshInstalledModelsAsync();
                await ApplyTriggerConfigurationAsync(persist: false);
                await RefreshTranscriptionReadinessAsync();
            }
            catch (Exception ex)
            {
                TriggerStatusText.Text = $"Settings could not be loaded: {DiagnosticTextRedactor.Redact(ex.Message)}";
            }
            finally
            {
                _isApplyingSettings = false;
                _settingsLoaded = true;
            }

            UpdateComputeButtons();
            if (!_settings.HasCompletedOnboarding)
            {
                ShowOnboarding();
            }
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private void ApplySettingsToControls(ShrutiSettings settings)
    {
        SelectComboBoxItem(InsertionModeComboBox, settings.InsertionMode.ToString());
        SelectComboBoxItem(ThemeComboBox, ToElementTheme(settings.ThemePreference).ToString());
        SelectComboBoxItem(AudioRetentionComboBox, settings.AudioRetentionPolicy.ToString());
        SelectComboBoxItem(BackendPreferenceComboBox, settings.BackendPreference.ToString());
        SelectModelComboBoxItem(settings.SelectedModelId);
        AllowSlowTranscriptionCheckBox.IsChecked = settings.AllowSlowTranscription;
        ApplyTriggerConfigurationToControls(settings.TriggerConfiguration);
        UpdateComputeButtons();
    }

    private async Task PersistSettingsAsync()
    {
        if (!_settingsLoaded || _isApplyingSettings)
        {
            return;
        }

        await _settingsGate.WaitAsync();
        try
        {
            _settings = new ShrutiSettings
            {
                AudioInputDeviceId = _controller.AudioOptions.DeviceId,
                SelectedModelId = GetSelectedModelId(),
                InsertionMode = GetSelectedInsertionMode(),
                ThemePreference = GetSelectedThemePreference(),
                AudioRetentionPolicy = GetSelectedAudioRetentionPolicy(),
                BackendPreference = GetSelectedBackendPreference(),
                AllowSlowTranscription = AllowSlowTranscriptionCheckBox.IsChecked == true,
                HasCompletedOnboarding = _settings.HasCompletedOnboarding,
                TriggerConfiguration = GetTriggerConfigurationFromControls()
            };
            _transcriptionOptionsProvider.ApplySettings(_settings);
            await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
            await RefreshTranscriptionReadinessAsync();
        }
        catch (Exception ex)
        {
            TriggerStatusText.Text = $"Settings could not be saved: {DiagnosticTextRedactor.Redact(ex.Message)}";
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        for (int index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item && item.Tag is string candidate &&
                string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private static ElementTheme ToElementTheme(AppThemePreference preference)
    {
        return preference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void UpdateComputeButtons()
    {
        ComputeBackend selected = GetSelectedBackendPreference();
        UpdateComputeButtonGroup(
            [BackendAutoButton, BackendNpuButton, BackendGpuButton, BackendCpuButton],
            _modelBackendFilter,
            GetCatalogAvailableBackends(),
            filtersModels: true);
        UpdateComputeButtonGroup(
            [SettingsBackendNpuButton, SettingsBackendGpuButton, SettingsBackendCpuButton],
            selected == ComputeBackend.Auto ? _resolvedBackend : selected,
            GetAvailableBackends(_transcriptionOptionsProvider.GetSelectedModelEntry(_settings)),
            filtersModels: false);

        UpdateActiveComputeBadge(selected);
    }

    private void UpdateComputeButtonGroup(
        IEnumerable<Button> buttons,
        ComputeBackend selected,
        IReadOnlySet<ComputeBackend> availableBackends,
        bool filtersModels)
    {
        foreach (Button button in buttons)
        {
            bool isSelected = button.Tag is string tag &&
                Enum.TryParse(tag, out ComputeBackend candidate) &&
                candidate == selected;
            if (button.Tag is string backendTag && Enum.TryParse(backendTag, out ComputeBackend backend))
            {
                button.IsEnabled = backend == ComputeBackend.Auto || availableBackends.Contains(backend);
                ToolTipService.SetToolTip(
                    button,
                    button.IsEnabled
                        ? filtersModels
                            ? $"Show models that can run on {backend}."
                            : $"Run the active model on {backend}."
                        : filtersModels
                            ? $"No catalog model can run on {backend} on this PC."
                            : $"{backend} is not available for the active model on this PC.");
            }
            button.Background = isSelected
                ? GetBrush("ShrutiCardBrush")
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            button.BorderBrush = isSelected
                ? GetBrush("ShrutiBorderSubtleBrush")
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            button.Foreground = GetBrush(isSelected ? "ShrutiTextPrimaryBrush" : "ShrutiTextSecondaryBrush");
        }
    }

    private void UpdateActiveComputeBadge(ComputeBackend backend)
    {
        (string foreground, string background) = GetComputeBrushKeys(backend);
        ActiveComputeBadgeText.Text = backend == ComputeBackend.Auto
            ? "AUTO"
            : backend.ToString().ToUpperInvariant();
        ActiveComputeBadgeText.Foreground = GetBrush(foreground);
        ActiveComputeBadgeBorder.Background = GetBrush(background);
    }

    private void ShowOnboarding()
    {
        OnboardingLayer.Visibility = Visibility.Visible;
        SetOnboardingStep(0);
    }

    private async Task ReplayOnboardingAsync()
    {
        _settings = _settings with { HasCompletedOnboarding = false };
        if (_settingsLoaded)
        {
            try
            {
                await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
            }
            catch (Exception ex)
            {
                TriggerStatusText.Text = $"Welcome could not be reset: {DiagnosticTextRedactor.Redact(ex.Message)}";
            }
        }

        ShowOnboarding();
    }

    private void SetOnboardingStep(int step)
    {
        _onboardingStep = Math.Clamp(step, 0, 4);
        OnboardingWelcomeStep.Visibility = _onboardingStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingMicrophoneStep.Visibility = _onboardingStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingModelStep.Visibility = _onboardingStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingHotkeyStep.Visibility = _onboardingStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        OnboardingDoneStep.Visibility = _onboardingStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        OnboardingMicStatusText.Text = _audioDevicesLoaded
            ? "Microphone ready"
            : "We will check your Windows microphone access.";
        OnboardingHotkeyText.Text = string.IsNullOrWhiteSpace(_triggerService.Configuration.PushToTalkKey)
            ? "Hold shortcut"
            : _triggerService.Configuration.PushToTalkKey;
        UpdateOnboardingModelState();

        Border[] dots = [OnboardingDot0, OnboardingDot1, OnboardingDot2, OnboardingDot3, OnboardingDot4];
        for (int index = 0; index < dots.Length; index++)
        {
            dots[index].Width = index == _onboardingStep ? 20 : 8;
            dots[index].Background = GetBrush(
                index <= _onboardingStep ? "ShrutiAccentBrush" : "ShrutiBorderStrongBrush");
        }
    }

    private void UpdateOnboardingModelState()
    {
        ModelCatalogEntry model = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        bool installed = FindInstalledModel(model.Id) is not null;
        OnboardingModelNameText.Text = model.DisplayName;
        OnboardingModelMetaText.Text = $"{FormatBytes(model.SizeBytes)} · {model.LanguageHint.ToUpperInvariant()} · on this PC";
        OnboardingModelActionButton.Content = installed ? "Continue" : "Download";
        OnboardingDoneText.Text = $"{model.DisplayName} is ready. Hold {(_triggerService.Configuration.PushToTalkKey ?? "your shortcut")} to dictate anywhere.";
    }

    private async Task RefreshTranscriptionReadinessAsync()
    {
        try
        {
            await RefreshComputeAvailabilityAsync();
            TranscriptionModelDescriptor model = _transcriptionOptionsProvider.CreateModelDescriptor();
            TranscriptionReadinessResult readiness = await _transcriptionOptionsProvider
                .EvaluateReadinessAsync(CancellationToken.None);
            string backend = readiness.SelectedBackend?.ToString() ?? _settings.BackendPreference.ToString();
            string device = readiness.DeviceName ?? "No compatible device";
            _resolvedBackend = readiness.SelectedBackend ??
                (_settings.BackendPreference == ComputeBackend.Auto ? ComputeBackend.Cpu : _settings.BackendPreference);

            ModelSummaryText.Text = model.DisplayName;
            BackendSummaryText.Text = "on this PC";
            ActiveBackendText.Text = readiness.CanProceed
                ? $"{readiness.Provider?.DisplayName ?? model.ProviderId} / {backend} / {device}"
                : $"{model.ProviderId} / {backend} / unavailable";
            BackendReadinessText.Text = FormatReadiness(readiness);
            UpdateActiveComputeBadge(readiness.SelectedBackend ?? _settings.BackendPreference);
            UpdateComputeButtons();
            UpdateSelectedModelStatus();
        }
        catch (Exception ex)
        {
            BackendSummaryText.Text = "Unavailable";
            ActiveBackendText.Text = "Unavailable";
            BackendReadinessText.Text = DiagnosticTextRedactor.Redact(ex.Message);
            SelectedModelStatusText.Text = "Model status unavailable.";
            UpdateActiveComputeBadge(_settings.BackendPreference);
        }
    }

    private async Task RefreshComputeAvailabilityAsync()
    {
        var availability = new Dictionary<string, IReadOnlySet<ComputeBackend>>(StringComparer.Ordinal);
        foreach (ModelCatalogEntry model in _modelCatalog.Models)
        {
            availability[model.Id] = await _transcriptionOptionsProvider
                .GetAvailableBackendsAsync(model, CancellationToken.None);
        }

        _availableBackendsByModel = availability;
        UpdateComputeButtons();
        RenderModelCatalog();
    }

    private IReadOnlySet<ComputeBackend> GetAvailableBackends(ModelCatalogEntry model)
    {
        return _availableBackendsByModel.TryGetValue(model.Id, out IReadOnlySet<ComputeBackend>? backends)
            ? backends
            : new HashSet<ComputeBackend>();
    }

    private IReadOnlySet<ComputeBackend> GetCatalogAvailableBackends()
    {
        return _availableBackendsByModel.Values.SelectMany(backends => backends).ToHashSet();
    }

    private static string FormatReadiness(TranscriptionReadinessResult readiness)
    {
        return DiagnosticFailureText.ForReadiness(readiness);
    }

    private string FormatErrorText(DictationShellState state)
    {
        if (state.LastOutcome == DictationRunOutcome.Failed && _controller.LastResult is { } result)
        {
            return DiagnosticFailureText.ForDictationResult(result);
        }

        return DiagnosticTextRedactor.Redact(state.ErrorText);
    }

    private static string FormatDiagnosticsSnapshot(DictationRunResult? result)
    {
        if (result is null)
        {
            return "Run dictation to populate a redacted diagnostics snapshot.";
        }

        RedactedDiagnosticsSnapshot snapshot = RedactedDiagnosticsSnapshot.FromResult(result);
        string targetProcess = string.IsNullOrWhiteSpace(snapshot.TargetProcessName)
            ? "none"
            : snapshot.TargetProcessName;

        var lines = new List<string>
        {
            $"State: {FormatState(snapshot.SessionState)}",
            $"Outcome: {snapshot.Outcome}",
            $"Target process: {targetProcess}",
            snapshot.TargetWindowTitleRedacted
                ? "Target window title: omitted"
                : "Target window title: not captured",
            $"Transcript characters: {snapshot.TranscriptCharacterCount}",
            snapshot.TranscriptTextRedacted
                ? "Transcript text: omitted"
                : "Transcript text: not captured"
        };

        AddOptionalLine(lines, "Focus target HWND", snapshot.FocusTargetWindowHandle);
        AddOptionalLine(lines, "Focus foreground before", snapshot.FocusForegroundWindowBefore);
        AddOptionalLine(lines, "Focus foreground after", snapshot.FocusForegroundWindowAfter);
        AddOptionalLine(lines, "Focus requested foreground", snapshot.FocusRequestedForeground?.ToString());
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "targetWindowHandle", "Target HWND");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "targetThreadId", "Target thread");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "targetProfile", "Insertion profile");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "foregroundWindowBefore", "Foreground before");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "foregroundWindowAfter", "Foreground after");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "focusedElementAutomationId", "Focused element");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "focusedElementIsEditable", "Focused editable");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "pasteShortcut", "Paste shortcut");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "unicodeInputMode", "Unicode mode");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "sendInputOutcome", "SendInput outcome");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "sendInputSentCount", "SendInput sent");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "sendInputRequestedCount", "SendInput requested");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "sendInputLastError", "SendInput last error");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "clipboardSequenceBefore", "Clipboard sequence before");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "clipboardWriteOutcome", "Clipboard write");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "clipboardRestoreOutcome", "Clipboard restore");
        AddDiagnosticLine(lines, snapshot.InsertionDiagnostics, "recoveryClipboardTextAvailable", "Recovery clipboard");
        lines.Add($"Failure summary: {snapshot.FailureSummary}");

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddOptionalLine(
        List<string> lines,
        string label,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {DiagnosticTextRedactor.Redact(value)}");
        }
    }

    private static void AddDiagnosticLine(
        List<string> lines,
        IReadOnlyDictionary<string, string?> diagnostics,
        string key,
        string label)
    {
        if (diagnostics.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {DiagnosticTextRedactor.Redact(value)}");
        }
    }

    private async void TrayIconService_CommandInvoked(WindowsTrayCommand command)
    {
        switch (command)
        {
            case WindowsTrayCommand.ShowWindow:
                ShowMainWindow();
                break;

            case WindowsTrayCommand.Toggle:
                if (_triggerService.Configuration.EnableTrayMenu)
                {
                    await RaiseTriggerAsync(DictationTriggerKind.TrayMenu, "tray-menu");
                }
                else
                {
                    ShowMainWindow();
                }

                break;

            case WindowsTrayCommand.Start:
                if (_triggerService.Configuration.EnableTrayMenu && !_controller.State.IsRunning)
                {
                    await RaiseTriggerAsync(DictationTriggerKind.TrayMenu, "tray-menu");
                }

                break;

            case WindowsTrayCommand.Stop:
                if (_triggerService.Configuration.EnableTrayMenu && _controller.State.IsRunning)
                {
                    await RaiseTriggerAsync(DictationTriggerKind.TrayMenu, "tray-menu");
                }

                break;

            case WindowsTrayCommand.Cancel:
                if (_triggerService.Configuration.EnableTrayMenu)
                {
                    await _controller.CancelAsync();
                }

                break;

            case WindowsTrayCommand.ShowSettings:
                ShowSettings();
                break;

            case WindowsTrayCommand.Quit:
                await _controller.CancelAsync();
                QuitApplication();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private void ShowSettings()
    {
        ShowMainWindow();
        ShowPage("Settings");
    }

    private void ShowMainWindow()
    {
        _windowVisibility.ShowAndActivate(_windowHandle);
    }

    private void ShowPage(string page)
    {
        bool isHome = string.Equals(page, "Home", StringComparison.OrdinalIgnoreCase);
        bool isModels = string.Equals(page, "Models", StringComparison.OrdinalIgnoreCase);
        bool isSettings = string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase);
        if (!isHome && !isModels && !isSettings)
        {
            isHome = true;
            page = "Home";
        }

        _currentPage = page;

        HomePage.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        ModelsPage.Visibility = isModels ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

        SetNavigationButtonState(HomeNavButton, isHome);
        SetNavigationButtonState(ModelsNavButton, isModels);
        SetNavigationButtonState(SettingsNavButton, isSettings);

        if (isModels && _settingsLoaded)
        {
            _ = RefreshInstalledModelsAsync();
        }
    }

    private void SetNavigationButtonState(Button button, bool isSelected)
    {
        SolidColorBrush foreground = GetBrush(isSelected ? "ShrutiAccentBrush" : "ShrutiTextSecondaryBrush");
        SolidColorBrush background = isSelected
            ? GetBrush("ShrutiAccentSoftBrush")
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        button.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        button.Opacity = 1;
        button.Background = background;
        button.Foreground = foreground;

        // The stock Button template supplies its own pointer-over and pressed colors.
        // Override those resources per navigation state so hovering the selected item
        // cannot replace its accent treatment with the default button foreground.
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonBackgroundPointerOver"] = isSelected
            ? background
            : GetBrush("ShrutiHoverBrush");
        button.Resources["ButtonBackgroundPressed"] = isSelected
            ? background
            : GetBrush("ShrutiPressedBrush");
    }

    private void QuitApplication()
    {
        _allowClose = true;
        DisposeNativeTriggers();
        Close();
        Application.Current.Exit();
    }

    private void DisposeNativeTriggers()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _controller.AudioLevelChanged -= Controller_AudioLevelChanged;
        _triggerDispatchCancellation.Cancel();
        _targetFocusService.Dispose();
        _trayIconService.CommandInvoked -= TrayIconService_CommandInvoked;
        _trayIconService.Dispose();
        _windowMessageHost.MessageReceived -= WindowMessageHost_MessageReceived;
        _windowMessageHost.Dispose();
        _triggerService.Dispose();
        _triggerDispatchCancellation.Dispose();
    }

    private static string FormatState(DictationSessionState state)
    {
        return state switch
        {
            DictationSessionState.Idle => "Idle",
            DictationSessionState.PreparingTarget => "Preparing target",
            DictationSessionState.RequestingMicrophone => "Requesting microphone",
            DictationSessionState.Recording => "Recording",
            DictationSessionState.Paused => "Paused",
            DictationSessionState.TranscribingFinalAudio => "Transcribing",
            DictationSessionState.InsertingText => "Inserting text",
            DictationSessionState.Complete => "Complete",
            DictationSessionState.Cancelled => "Cancelled",
            DictationSessionState.Failed => "Failed",
            _ => state.ToString()
        };
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
