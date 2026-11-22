using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    private readonly FloatingBarManager _floatingBars;
    private bool _isUpdatingFloatingMonitorControls;
    private Task? _triggerDispatchTask;
    private bool _allowClose;
    private bool _isDisposed;
    private bool _audioDevicesLoaded;
    private bool _settingsLoaded;
    private bool _isApplyingSettings;
    private bool _isEditingShortcut;
    private string _pushToTalkGesture = "Ctrl+Win+Space";
    private string _globalHotkeyGesture = "Ctrl+Win+Space";
    private bool _isApplyingModelSelection;
    private bool _isModelOperationRunning;
    private bool _isOnboardingModelOperation;
    private int _onboardingStep;
    private ComputeBackend _resolvedBackend = ComputeBackend.Cpu;
    private string _currentPage = "History";
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
        TriggerStatusText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            TriggerStatusText.Visibility = string.IsNullOrEmpty(TriggerStatusText.Text) ? Visibility.Collapsed : Visibility.Visible);
        AppIcon.Apply(AppWindow);
        HistoryPage.ModelsRequested += (_, _) => ShowPage("Models");

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
        _trayIconService.SetDarkTheme(Root.ActualTheme == ElementTheme.Dark);
        _windowVisibility = windowVisibility ?? throw new ArgumentNullException(nameof(windowVisibility));
        _floatingBars = new FloatingBarManager(_windowVisibility);
        _floatingBars.TriggerRequested += async (_, _) =>
            await RaiseTriggerAsync(DictationTriggerKind.FloatingButton, "floating-pill");
        _floatingBars.ResumeRequested += async (_, _) => { if (_controller.State.IsPaused) await _controller.PauseAsync(); };
        _floatingBars.CancelRequested += async (_, _) => await _controller.CancelAsync();
        _floatingBars.DismissRequested += (_, _) => FloatingBarCheckBox.IsOn = false;
        _floatingBars.SettingsRequested += (_, _) => { ShowMainWindow(); ShowPage("Settings"); };
        _floatingBars.DisplaysChanged += (_, _) => RefreshFloatingMonitorControls();
        _windowHandle = WindowNative.GetWindowHandle(this);
        _windowMessageHost = new WindowsWindowMessageHost(_windowHandle);
        _triggerDispatcher = new DictationTriggerDispatcher(_triggerService, _triggerRouter);

        HistoryPage.DictationRequested += async (_, _) =>
        {
            if (!_controller.State.IsRunning && FindInstalledModel(_settings.SelectedModelId) is null)
                ShowPage("Models");
            else
                await RaiseTriggerAsync(DictationTriggerKind.AppButton, "transcriptions");
        };
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

        InsertionModeComboBox.SelectedIndex = 0;
        ThemeComboBox.SelectedIndex = 0;
        BackendPreferenceComboBox.SelectedIndex = 0;
        PopulateModelSelectionComboBox();
        ShowPage("History");
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

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.RetryAsync();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.CopyPreviewTextAsync(TranscriptPreviewBox.Text);
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

        ModelCatalogEntry activeModel = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        if (selectedBackend != ComputeBackend.Auto && !GetAvailableBackends(activeModel).Contains(selectedBackend))
        {
            SelectedModelStatusText.Text =
                $"{activeModel.DisplayName} cannot run on {selectedBackend.ToString().ToUpperInvariant()} on this PC. " +
                "Choose a compatible model first.";
            UpdateComputeButtons();
            return;
        }

        SelectComboBoxItem(BackendPreferenceComboBox, backend);
        UpdateComputeButtons();
    }

    private void ReplayWelcomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.State.IsRunning)
        {
            TriggerStatusText.Text = "Finish dictation before replaying setup.";
            return;
        }
        _ = ReplayOnboardingAsync();
    }

    private void OnboardingNextButton_Click(object sender, RoutedEventArgs e)
    {
        SetOnboardingStep(Math.Min(4, _onboardingStep + 1));
    }

    private async void OnboardingMicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        OnboardingMicrophoneButton.IsEnabled = false;
        OnboardingMicrophoneError.IsOpen = false;
        OnboardingMicrophoneButton.Content = "Checking microphone";
        OnboardingMicStatusText.Text = "Checking access. Allow the Windows microphone prompt if it appears.";
        try
        {
            await LoadAudioDevicesAsync();
            if (!_audioDevicesLoaded)
            {
                ShowOnboardingMicrophoneError();
                return;
            }

            // Enumeration alone does not request access or prove that audio can arrive.
            await using IAudioCaptureSession capture = await Task.Run(() => _audioCaptureService.StartAsync(
                _controller.AudioOptions, AudioFormat.Speech16KhzMono, CancellationToken.None));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var frames = capture.Frames.GetAsyncEnumerator(timeout.Token);
            if (!await frames.MoveNextAsync())
            {
                throw new InvalidOperationException("The microphone did not supply audio.");
            }
            await capture.StopAsync(CancellationToken.None);
            SetOnboardingStep(2);
        }
        catch (Exception)
        {
            ShowOnboardingMicrophoneError();
        }
        finally
        {
            OnboardingMicrophoneButton.IsEnabled = true;
        }
    }

    private void ShowOnboardingMicrophoneError()
    {
        OnboardingMicStatusText.Text = string.Empty;
        OnboardingMicrophoneError.IsOpen = true;
        OnboardingMicrophoneButton.Content = "Retry microphone check";
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
        OnboardingModelStatusText.Text = "Downloading your model. Keep Shruti open until it finishes.";
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
            else
            {
                OnboardingModelStatusText.Text = ModelsStatusText.Text + " Check your connection and try Download again.";
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
        try
        {
            ShrutiSettings completed = _settings with { HasCompletedOnboarding = true };
            await _settingsRepository.SaveAsync(completed, CancellationToken.None);
            _settings = completed;
            OnboardingLayer.Visibility = Visibility.Collapsed;
            AppNavigation.Visibility = Visibility.Visible;
            AppPages.Visibility = Visibility.Visible;
            ShowPage("History");
            HistoryNavButton.Focus(FocusState.Programmatic);
            UpdateFloatingBar();
        }
        catch (Exception ex)
        {
            OnboardingDoneText.Text = $"Could not save setup: {DiagnosticTextRedactor.Redact(ex.Message)}. Try again.";
        }
    }

    private async void ModelSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingModelSelection || _isApplyingSettings)
        {
            return;
        }

        await PersistSettingsAsync();
    }

    private async void TriggerConfigurationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private async void ChangeHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        await ChangeShortcutAsync(holdToTalk: true);
    }

    private async void ChangeSinglePressHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        await ChangeShortcutAsync(holdToTalk: false);
    }

    private async Task ChangeShortcutAsync(bool holdToTalk)
    {
        if (_isEditingShortcut) return;
        await EnsureSettingsLoadedAsync();
        if (_isEditingShortcut) return;
        if (_controller.State.IsRunning)
        {
            TriggerStatusText.Text = "Finish dictation before changing a shortcut.";
            return;
        }

        _isEditingShortcut = true;
        UpdateFloatingBar();
        TriggerConfiguration original = _triggerService.Configuration;
        TriggerConfiguration restore = original;
        try
        {
            await _triggerService.ConfigureAsync(original with
            {
                EnableGlobalHotkey = false,
                EnablePushToTalk = false,
                EnableFloatingWindowShortcut = false
            }, CancellationToken.None);
            _trayIconService.SetDictationCommandsEnabled(false);
            var dialog = new ShortcutCaptureDialog(this, _windowHandle, Root.XamlRoot,
                (holdToTalk ? original.PushToTalkKey : original.HotkeyGesture) ?? string.Empty,
                holdToTalk, async gesture =>
                {
                    TriggerConfiguration updated = holdToTalk
                        ? original with { PushToTalkKey = gesture }
                        : original with { HotkeyGesture = gesture };

                    // Probe availability while our own registrations are suspended.
                    WindowsHotkeyParser.TryParse(gesture, out WindowsHotkey? hotkey, out _);
                    if (hotkey is null && WindowsVirtualKey.TryParse(gesture, out uint key, out _) && key is >= 0x70 and <= 0x87)
                    {
                        hotkey = new WindowsHotkey(0, key, gesture);
                    }
                    if (hotkey is not null)
                    {
                        const int probeId = 0x5350;
                        var registration = new Win32HotkeyRegistration();
                        if (!registration.Register(_windowHandle, probeId, hotkey!))
                            throw new InvalidOperationException("That shortcut is in use by Windows or another app. Record a different shortcut.");
                        registration.Unregister(_windowHandle, probeId);
                    }

                    await _settingsGate.WaitAsync();
                    try
                    {
                        await _triggerService.ConfigureAsync(updated, CancellationToken.None);
                        ShrutiSettings updatedSettings = _settings with { TriggerConfiguration = updated };
                        await _settingsRepository.SaveAsync(updatedSettings, CancellationToken.None);
                        _settings = updatedSettings;
                        _transcriptionOptionsProvider.ApplySettings(_settings);
                        restore = updated;
                    }
                    catch
                    {
                        await _triggerService.ConfigureAsync(original with
                        {
                            EnableGlobalHotkey = false,
                            EnablePushToTalk = false,
                            EnableFloatingWindowShortcut = false
                        }, CancellationToken.None);
                        throw;
                    }
                    finally
                    {
                        _settingsGate.Release();
                    }
                });
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            TriggerStatusText.Text = DiagnosticTextRedactor.Redact(exception.Message);
        }
        finally
        {
            try
            {
                await _triggerService.ConfigureAsync(restore, CancellationToken.None);
                ApplyTriggerConfigurationToControls(restore);
                _trayIconService.SetDictationCommandsEnabled(restore.EnableTrayMenu);
                if (restore != original) TriggerStatusText.Text = "Shortcut saved.";
            }
            catch (Exception exception)
            {
                TriggerStatusText.Text = DiagnosticTextRedactor.Redact(exception.Message);
            }
            _isEditingShortcut = false;
            UpdateFloatingBar();
        }
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
        _trayIconService.SetDarkTheme(Root.ActualTheme == ElementTheme.Dark);
        UpdateAppTitleBarTheme();
        UpdateComputeButtons();
        foreach (var button in new[] { HistoryNavButton, InsightsNavButton, DictionaryNavButton, SnippetsNavButton, ModelsNavButton, SettingsNavButton })
            SetNavigationButtonState(button, Equals(button.Tag, _currentPage));
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
        // Display and device notifications can arrive before Windows finishes arranging monitors.
        if (message.Id is 0x007E or 0x0219) // WM_DISPLAYCHANGE, WM_DEVICECHANGE
            _floatingBars.ScheduleDisplayRefresh();
        return _triggerService.HandleWindowMessage(message.Id, message.WParam) ||
            _trayIconService.HandleWindowMessage(message);
    }

    private void Controller_StateChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            UpdateView();
            if (!_controller.State.IsRunning)
            {
                if (_currentPage == "History") _ = HistoryPage.RefreshAsync();
                if (_currentPage == "Insights") _ = InsightsPage.RefreshAsync();
            }
        });
    }

    private void Controller_AudioLevelChanged(object? sender, AudioLevelFrame level)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _floatingBars.UpdateAudioLevel(level.Peak);
        });
    }

    private async Task LoadAudioDevicesAsync()
    {
        _audioDevicesLoaded = false;
        AudioDeviceComboBox.IsEnabled = false;
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
                TriggerStatusText.Text = DiagnosticFailureText.MicrophoneRecovery;
                return;
            }

            string selectedDeviceId = devices.FirstOrDefault(device => device.Id == _controller.AudioOptions.DeviceId)?.Id ??
                devices.FirstOrDefault(device => device.IsDefault)?.Id ??
                devices[0].Id;
            SelectAudioDevice(selectedDeviceId);
            _audioDevicesLoaded = true;
            AudioDeviceComboBox.IsEnabled = !_controller.State.IsRunning;
        }
        catch (Exception ex)
        {
            AudioDeviceComboBox.PlaceholderText = "Microphone unavailable";
            TriggerStatusText.Text = DiagnosticFailureText.MicrophoneRecovery;
            AutomationProperties.SetHelpText(AudioDeviceComboBox, DiagnosticTextRedactor.Redact(ex.Message));
        }
    }

    private async void AudioDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AudioDeviceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string deviceId)
        {
            ToolTipService.SetToolTip(AudioDeviceComboBox, item.Content?.ToString());
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

    private void UseOriginalButton_Click(object sender, RoutedEventArgs e) => _controller.UseRawTranscript();
    private void ReviewCorrectionButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("Dictionary");
        DictionaryEditor.ReviewPreviewCorrection(_controller.State.RawTranscript, TranscriptPreviewBox.Text);
        DictionaryEditor.StartBringIntoView();
    }

    private void UpdateView()
    {
        DictationShellState state = _controller.State;

        UserMessageText.Text = state.UserMessage;
        TranscriptPreviewBox.Text = state.TranscriptPreview;
        UseOriginalButton.Visibility = state.CanUseRawTranscript ? Visibility.Visible : Visibility.Collapsed;
        ReviewCorrectionButton.Visibility = !state.IsRunning && state.RawTranscript.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        PreviewTools.Visibility = state.CanUseRawTranscript || ReviewCorrectionButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        string errorText = FormatErrorText(state);
        PreviewHeading.Text = state.CanInsertPreview ? $"Insert into {state.TargetDescription}" :
            state.CanCopy ? "Recover transcript" : "Dictation needs attention";
        ErrorText.Text = errorText;
        ErrorNotice.Visibility = string.IsNullOrWhiteSpace(errorText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        RetryButton.IsEnabled = state.CanRetry;
        RetryButton.Visibility = state.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        if (state.CanInsertPreview)
            CopyButton.ClearValue(FrameworkElement.StyleProperty);
        else
            CopyButton.Style = (Style)Application.Current.Resources["LibraryPrimaryButton"];
        CopyButton.Content = state.CanInsertPreview ? "Copy" : "Copy transcript";
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
        SaveAudioSwitch.IsEnabled = !state.IsRunning;
        HistoryPage.UpdateRecordingAction(state, _controller.LastResult?.RecordingSaveFailed == true);
        PendingTranscriptPanel.Visibility = !state.IsRunning &&
            (state.CanInsertPreview || state.LastOutcome is DictationRunOutcome.Failed or DictationRunOutcome.PreviewRequired || !string.IsNullOrWhiteSpace(errorText))
            ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(UserMessageText, UserMessageText.Text);
        UpdateDiagnostics(_controller.LastResult);

        _trayIconService.UpdateDictationState(state.IsRunning);
        UpdateFloatingBar();
    }

    private async void SaveAudioSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded || _isApplyingSettings) return;
        _controller.KeepRecordings = SaveAudioSwitch.IsOn;
        await PersistSettingsAsync();
    }

    private async void OpenRecordingsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(AppDataPaths.CreateDefault().RecordingsDirectory);
            if (await Windows.System.Launcher.LaunchFolderAsync(folder)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        TriggerStatusText.Text = "Couldn't open the recordings folder.";
    }

    private async void FloatingBarCheckBox_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded || _isApplyingSettings) return;
        UpdateFloatingBar();
        await ApplyTriggerConfigurationAsync();
    }

    private void UpdateFloatingBar()
    {
        if (_isDisposed || !_settingsLoaded) return;
        FloatingBarDisplayModeComboBox.IsEnabled = FloatingBarCheckBox.IsOn;
        FloatingBarMonitorComboBox.IsEnabled = FloatingBarCheckBox.IsOn;
        _floatingBars.Update(_controller.State, _pushToTalkGesture,
            FloatingBarCheckBox.IsOn && !_isEditingShortcut && _settings.HasCompletedOnboarding,
            _settings.ShowFloatingBarOnAllMonitors, _settings.FloatingBarMonitorId);
    }

    private void RefreshFloatingMonitorControls()
    {
        if (_isDisposed) return;
        _isUpdatingFloatingMonitorControls = true;
        try
        {
            FloatingBarDisplayModeComboBox.SelectedIndex = _settings.ShowFloatingBarOnAllMonitors ? 1 : 0;
            FloatingBarMonitorComboBox.Items.Clear();
            var primary = new ComboBoxItem { Content = "Primary monitor", Tag = "" };
            FloatingBarMonitorComboBox.Items.Add(primary);
            ComboBoxItem selected = primary;
            bool found = string.IsNullOrEmpty(_settings.FloatingBarMonitorId);
            foreach (var monitor in _floatingBars.Monitors)
            {
                var item = new ComboBoxItem { Content = monitor.Name, Tag = monitor.Id };
                FloatingBarMonitorComboBox.Items.Add(item);
                if (string.Equals(monitor.Id, _settings.FloatingBarMonitorId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = item;
                    found = true;
                }
            }
            if (!found)
            {
                selected = new ComboBoxItem { Content = "Saved monitor is disconnected", Tag = _settings.FloatingBarMonitorId };
                FloatingBarMonitorComboBox.Items.Add(selected);
            }
            FloatingBarMonitorComboBox.SelectedItem = selected;
            FloatingBarMonitorComboBox.Visibility = _settings.ShowFloatingBarOnAllMonitors ? Visibility.Collapsed : Visibility.Visible;
            FloatingBarMonitorStatusText.Text = "Using the primary monitor until your saved monitor reconnects.";
            FloatingBarMonitorStatusText.Visibility = !found && !_settings.ShowFloatingBarOnAllMonitors ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _isUpdatingFloatingMonitorControls = false; }
    }

    private async void FloatingBarDisplayPreference_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded || _isApplyingSettings || _isUpdatingFloatingMonitorControls) return;
        await PersistSettingsAsync();
        if (!_isDisposed)
        {
            RefreshFloatingMonitorControls();
            UpdateFloatingBar();
        }
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
                int compatibleCount = visibleModels.Count;
                ModelsStatusText.Text = compatibleCount == 1
                    ? $"1 model for {_modelBackendFilter.ToString().ToUpperInvariant()}"
                    : $"{compatibleCount} models for {_modelBackendFilter.ToString().ToUpperInvariant()}";
            }
        }

        foreach (ModelCatalogEntry model in visibleModels)
        {
            ModelListPanel.Children.Add(CreateModelCard(model));
        }
        if (visibleModels.Count == 0)
        {
            ModelListPanel.Children.Add(new TextBlock
            {
                Text = "No speech models match this processor.",
                Margin = new Thickness(18),
                Style = (Style)Root.Resources["MutedTextStyle"]
            });
        }
    }

    private ModelCatalogRow CreateModelCard(ModelCatalogEntry model)
    {
        bool isInstalled = FindInstalledModel(model.Id) is not null;
        bool isSelected = string.Equals(_settings.SelectedModelId, model.Id, StringComparison.Ordinal);
        string metadata = FormatModelDetails(model);
        if (isSelected && !isInstalled) metadata = $"Selected  ·  {metadata}";
        var row = new ModelCatalogRow(model.DisplayName, metadata, ModelListPanel.Children.Count > 0);
        row.SetState(isInstalled, isInstalled && isSelected);
        if (!isInstalled)
        {
            if (!model.IsBundle)
            {
                var import = new Button
                {
                    Tag = model.Id,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Content = "Import",
                    IsEnabled = !_isModelOperationRunning
                };
                AutomationProperties.SetName(import, $"Import {model.DisplayName}");
                ToolTipService.SetToolTip(import, "Import a local model file");
                import.Click += ImportModelButton_Click;
                row.AddAction(import);
            }
            var download = new Button
            {
                Tag = model.Id,
                Content = "Download",
                IsEnabled = (model.DownloadUri is not null || model.IsBundle) && !_isModelOperationRunning
            };
            if (isSelected)
            {
                download.Background = GetBrush("ShrutiAccentBrush");
                download.Foreground = GetBrush("ShrutiAccentForegroundBrush");
            }
            AutomationProperties.SetName(download, $"Download {model.DisplayName}");
            ToolTipService.SetToolTip(download, $"Download and verify {model.DisplayName}.");
            download.Click += DownloadModelButton_Click;
            row.AddAction(download);
        }
        else if (!isSelected)
        {
            var remove = new Button
            {
                Tag = model.Id,
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                Content = "Remove",
                IsEnabled = !_isModelOperationRunning
            };
            AutomationProperties.SetName(remove, $"Remove {model.DisplayName}");
            ToolTipService.SetToolTip(remove, "Remove local model");
            remove.Click += RemoveModelButton_Click;
            row.AddAction(remove);
            var select = new Button
            {
                Tag = model.Id,
                Content = "Set active",
                IsEnabled = !_isModelOperationRunning
            };
            AutomationProperties.SetName(select, $"Use {model.DisplayName}");
            ToolTipService.SetToolTip(select, $"Use {model.DisplayName} for dictation.");
            select.Click += SelectModelButton_Click;
            row.AddAction(select);
        }
        return row;
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
            if (await SetSelectedModelAsync(model.Id))
            {
                ModelsStatusText.Text = $"{model.DisplayName} selected for dictation.";
            }
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
                bool selected = await SetSelectedModelAsync(model.Id, refreshModels: false);
                finalStatusText = selected
                    ? $"{model.DisplayName} installed and selected for dictation."
                    : $"{model.DisplayName} installed, but it cannot run on the selected processor.";
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

    private async Task<bool> SetSelectedModelAsync(string modelId, bool refreshModels = true)
    {
        ModelCatalogEntry? selectedModel = _transcriptionOptionsProvider.FindModel(modelId);
        if (selectedModel is null)
        {
            return false;
        }

        ComputeBackend requestedBackend = _modelBackendFilter != ComputeBackend.Auto
            ? _modelBackendFilter
            : _settings.BackendPreference;
        IReadOnlySet<ComputeBackend> availableBackends = GetAvailableBackends(selectedModel);
        ComputeBackend backendPreference = ModelCatalogFiltering.NormalizeBackendPreference(
            selectedModel,
            requestedBackend,
            availableBackends);
        if (_modelBackendFilter != ComputeBackend.Auto && backendPreference != _modelBackendFilter)
        {
            ModelsStatusText.Text =
                $"{selectedModel.DisplayName} cannot run on {_modelBackendFilter.ToString().ToUpperInvariant()} on this PC.";
            return false;
        }

        if (!await _transcriptionOptionsProvider.CanRunModelAsync(
                selectedModel,
                backendPreference,
                CancellationToken.None))
        {
            ModelsStatusText.Text = backendPreference == ComputeBackend.Auto
                ? $"{selectedModel.DisplayName} is not ready to run on this PC."
                : $"{selectedModel.DisplayName} cannot run on {backendPreference.ToString().ToUpperInvariant()} on this PC.";
            return false;
        }

        _settings = _settings with
        {
            SelectedModelId = modelId,
            BackendPreference = backendPreference
        };
        _transcriptionOptionsProvider.ApplySettings(_settings);
        SelectModelComboBoxItem(modelId);
        SelectBackendPreferenceWithoutPersist(backendPreference);
        await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
        UpdateSelectedModelStatus();
        ModelSummaryText.Text = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings).DisplayName;
        UpdateOnboardingModelState();
        await RefreshTranscriptionReadinessAsync();
        if (refreshModels)
        {
            RenderModelCatalog();
        }

        return true;
    }

    private void UpdateSelectedModelStatus()
    {
        ModelCatalogEntry selected = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        InstalledModel? installed = FindInstalledModel(selected.Id);
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
        string language = model.LanguageHint == "en" ? "English" : model.LanguageHint.ToUpperInvariant();
        return $"{FormatBytes(model.SizeBytes)} · {language} · {backends.ToUpperInvariant()}";
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
            ? string.Empty
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
        if (persist && (!_settingsLoaded || _isApplyingSettings || _isEditingShortcut)) return;
        TriggerConfiguration configuration = GetTriggerConfigurationFromControls();

        try
        {
            await _triggerService.ConfigureAsync(configuration, CancellationToken.None);
            _trayIconService.SetDictationCommandsEnabled(configuration.EnableTrayMenu);
            _trayIconService.SetVisible(isVisible: true);
            UpdateView();
            HistoryPage.SetShortcut(configuration);
            TriggerStatusText.Text = string.Empty;
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
            EnableFloatingButton: FloatingBarCheckBox.IsOn,
            EnableTrayMenu: TrayMenuCheckBox.IsOn,
            HotkeyGesture: _globalHotkeyGesture,
            PushToTalkKey: _pushToTalkGesture,
            EnableFloatingWindowShortcut: false,
            FloatingWindowShortcut: null);
    }

    private void ApplyTriggerConfigurationToControls(TriggerConfiguration configuration)
    {
        GlobalHotkeyCheckBox.IsOn = configuration.EnableGlobalHotkey;
        PushToTalkCheckBox.IsOn = configuration.EnablePushToTalk;
        TrayMenuCheckBox.IsOn = configuration.EnableTrayMenu;
        _globalHotkeyGesture = configuration.HotkeyGesture ?? string.Empty;
        _pushToTalkGesture = configuration.PushToTalkKey ?? string.Empty;
        ShortcutCaptureDialog.SetKeycaps(GlobalShortcutKeys, _globalHotkeyGesture);
        ShortcutCaptureDialog.SetKeycaps(PushToTalkShortcutKeys, _pushToTalkGesture);
        HistoryPage.SetShortcut(configuration);
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
                QualityControl.Configure(_settingsRepository, _settings.TranscriptQuality, _modelManager.ModelsDirectory);
                QualityControl.SettingsChanged += (_, _) => _settings = _settings with { TranscriptQuality = QualityControl.Settings };
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
                UpdateFloatingBar();
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
        FloatingBarCheckBox.IsOn = settings.ShowFloatingBar;
        RefreshFloatingMonitorControls();
        SelectComboBoxItem(InsertionModeComboBox, settings.InsertionMode.ToString());
        SelectComboBoxItem(ThemeComboBox, ToElementTheme(settings.ThemePreference).ToString());
        SelectComboBoxItem(BackendPreferenceComboBox, settings.BackendPreference.ToString());
        SelectModelComboBoxItem(settings.SelectedModelId);
        SaveAudioSwitch.IsOn = settings.AudioRetentionPolicy == AudioRetentionPolicy.Keep;
        _controller.KeepRecordings = SaveAudioSwitch.IsOn;
        ApplyTriggerConfigurationToControls(settings.TriggerConfiguration);
        UpdateComputeButtons();
    }

    private async Task PersistSettingsAsync()
    {
        if (!_settingsLoaded || _isApplyingSettings || _isEditingShortcut)
        {
            return;
        }

        await _settingsGate.WaitAsync();
        try
        {
            string selectedModelId = GetSelectedModelId();
            ModelCatalogEntry selectedModel = _transcriptionOptionsProvider.FindModel(selectedModelId) ??
                _transcriptionOptionsProvider.DefaultModel;
            ComputeBackend requestedBackend = GetSelectedBackendPreference();
            ComputeBackend backendPreference = ModelCatalogFiltering.NormalizeBackendPreference(
                selectedModel,
                requestedBackend,
                GetAvailableBackends(selectedModel));
            if (backendPreference != requestedBackend)
            {
                SelectBackendPreferenceWithoutPersist(backendPreference);
                SelectedModelStatusText.Text =
                    $"{selectedModel.DisplayName} cannot run on {requestedBackend.ToString().ToUpperInvariant()} on this PC. " +
                    "The processor setting was reset to automatic.";
            }

            _settings = new ShrutiSettings
            {
                TranscriptQuality = QualityControl.Settings,
                AudioInputDeviceId = _controller.AudioOptions.DeviceId,
                SelectedModelId = selectedModelId,
                InsertionMode = GetSelectedInsertionMode(),
                ThemePreference = GetSelectedThemePreference(),
                AudioRetentionPolicy = SaveAudioSwitch.IsOn ? AudioRetentionPolicy.Keep : AudioRetentionPolicy.DeleteAfterTranscription,
                BackendPreference = backendPreference,
                HasCompletedOnboarding = _settings.HasCompletedOnboarding,
                ShowFloatingBar = FloatingBarCheckBox.IsOn,
                ShowFloatingBarOnAllMonitors = FloatingBarDisplayModeComboBox.SelectedIndex == 1,
                FloatingBarMonitorId = (FloatingBarMonitorComboBox.SelectedItem as ComboBoxItem)?.Tag is string monitorId && monitorId.Length > 0 ? monitorId : null,
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

    private void SelectBackendPreferenceWithoutPersist(ComputeBackend backend)
    {
        bool wasApplyingSettings = _isApplyingSettings;
        _isApplyingSettings = true;
        try
        {
            SelectComboBoxItem(BackendPreferenceComboBox, backend.ToString());
        }
        finally
        {
            _isApplyingSettings = wasApplyingSettings;
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
            [ProcessorAutoButton, ProcessorNpuButton, ProcessorGpuButton, ProcessorCpuButton],
            selected,
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
        AppNavigation.Visibility = Visibility.Collapsed;
        AppPages.Visibility = Visibility.Collapsed;
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

        OnboardingMicStatusText.Text = string.Empty;
        OnboardingMicrophoneError.IsOpen = false;
        OnboardingMicrophoneButton.Content = "Check microphone";
        UpdateSetupShortcut();
        UpdateOnboardingModelState();

        string[] steps = ["Welcome", "Microphone", "Speech model", "Shortcut", "Finish"];
        string[] icons = ["\uE9D9", "\uE720", "\uE81E", "\uE765", "\uE73E"];
        SetupIllustration.Glyph = icons[_onboardingStep];
        SetupSteps.Children.Clear();
        for (int index = 0; index < steps.Length; index++)
        {
            bool current = index == _onboardingStep;
            bool completed = index < _onboardingStep;
            var foreground = GetBrush(current ? "ShrutiAccentBrush" : "ShrutiTextSecondaryBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var glyph = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16,
                Glyph = completed ? "\uE73E" : index == 4 && !current ? "\uE7C1" : icons[index],
                Foreground = foreground
            };
            var label = new TextBlock
            {
                Text = steps[index],
                Foreground = current ? GetBrush("ShrutiTextPrimaryBrush") : foreground,
                FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            };
            row.Children.Add(glyph);
            row.Children.Add(label);
            var item = new Border
            {
                Child = row,
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Background = current ? GetBrush("ShrutiAccentSoftBrush") : null
            };
            AutomationProperties.SetName(item, $"{steps[index]}, {(current ? "current step" : completed ? "completed" : "upcoming")}");
            SetupSteps.Children.Add(item);
        }

        Button[] actions = [SetupWelcomeAction, OnboardingMicrophoneButton, OnboardingModelActionButton, SetupShortcutAction, SetupFinishAction];
        actions[_onboardingStep].Focus(FocusState.Programmatic);
    }

    private void UpdateSetupShortcut()
    {
        var triggers = _triggerService.Configuration;
        string? gesture = null;
        if (triggers.EnablePushToTalk && !string.IsNullOrWhiteSpace(triggers.PushToTalkKey))
        {
            gesture = triggers.PushToTalkKey;
            OnboardingHotkeyText.Text = "Click a text field in the app where you want to write. Hold these keys while you speak, then release to finish.";
        }
        else if (triggers.EnableGlobalHotkey && !string.IsNullOrWhiteSpace(triggers.HotkeyGesture))
        {
            gesture = triggers.HotkeyGesture;
            OnboardingHotkeyText.Text = "Click a text field in the app where you want to write. Press these keys to start, then press them again to finish.";
        }
        else
        {
            OnboardingHotkeyText.Text = _settings.ShowFloatingBar
                ? "Click a text field in the app where you want to write. Use the microphone on the floating bar to start. Select the check mark to finish."
                : "Open Settings and choose a dictation shortcut to record while you write.";
        }
        OnboardingKeys.Visibility = gesture is null ? Visibility.Collapsed : Visibility.Visible;
        ShortcutCaptureDialog.SetKeycaps(OnboardingKeys, gesture ?? string.Empty);
    }

    private void UpdateOnboardingModelState()
    {
        ModelCatalogEntry model = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        bool installed = FindInstalledModel(model.Id) is not null;
        OnboardingModelNameText.Text = model.DisplayName;
        OnboardingModelMetaText.Text = $"{FormatBytes(model.SizeBytes)} · {(model.LanguageHint == "en" ? "English" : model.LanguageHint)}";
        if (installed) OnboardingModelMetaText.Text += " · Installed";
        OnboardingModelActionButton.Content = installed ? "Continue" : "Download";
        OnboardingDoneText.Text = $"{model.DisplayName} is ready. Your dictations will appear in Transcriptions, where you can read and copy them.";
    }

    private async Task RefreshTranscriptionReadinessAsync()
    {
        try
        {
            await RefreshComputeAvailabilityAsync();
            await NormalizeActiveBackendPreferenceAsync();
            TranscriptionModelDescriptor model = _transcriptionOptionsProvider.CreateModelDescriptor();
            TranscriptionReadinessResult readiness = await _transcriptionOptionsProvider
                .EvaluateReadinessAsync(CancellationToken.None);
            string backend = readiness.SelectedBackend?.ToString() ?? _settings.BackendPreference.ToString();
            string device = readiness.DeviceName ?? "No compatible device";
            _resolvedBackend = readiness.SelectedBackend ??
                (_settings.BackendPreference == ComputeBackend.Auto ? ComputeBackend.Cpu : _settings.BackendPreference);

            ModelSummaryText.Text = model.DisplayName;
            bool installed = FindInstalledModel(model.Id) is not null;
            BackendSummaryText.Text = installed ? "Installed locally" : "Download required";
            HistoryPage.SetModelInstalled(installed);
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

    private async Task NormalizeActiveBackendPreferenceAsync()
    {
        ModelCatalogEntry activeModel = _transcriptionOptionsProvider.GetSelectedModelEntry(_settings);
        ComputeBackend normalizedBackend = ModelCatalogFiltering.NormalizeBackendPreference(
            activeModel,
            _settings.BackendPreference,
            GetAvailableBackends(activeModel));
        if (normalizedBackend == _settings.BackendPreference)
        {
            return;
        }

        _settings = _settings with { BackendPreference = normalizedBackend };
        _transcriptionOptionsProvider.ApplySettings(_settings);
        SelectBackendPreferenceWithoutPersist(normalizedBackend);
        await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
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
            return result.Transcript is not null && state.InsertionMode == DictationInsertionMode.CopyOnly
                ? "Couldn't copy the transcript to the clipboard."
                : DiagnosticFailureText.ForDictationResult(result);
        }

        return DiagnosticTextRedactor.Redact(state.ErrorText);
    }

    private void UpdateDiagnostics(DictationRunResult? result)
    {
        DiagnosticsSnapshotText.Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;
        if (result is null)
        {
            DiagnosticsOutcomeText.Style = (Style)Root.Resources["MutedTextStyle"];
            DiagnosticsOutcomeText.Text = "Dictate once to see diagnostic details here.";
            DiagnosticsSnapshotText.Text = string.Empty;
            return;
        }

        RedactedDiagnosticsSnapshot snapshot = RedactedDiagnosticsSnapshot.FromResult(result);
        DiagnosticsOutcomeText.Style = (Style)Root.Resources["SectionLabelStyle"];
        DiagnosticsOutcomeText.Text = result.Outcome switch
        {
            DictationRunOutcome.Inserted when result.Inserted => "Text inserted into the target.",
            DictationRunOutcome.Inserted => "Shruti could not confirm insertion. Check the target field.",
            DictationRunOutcome.PreviewRequired when result.InsertionResult?.Submitted == true =>
                "Shruti sent the text. Check the target field before trying again.",
            DictationRunOutcome.PreviewRequired => "Review your text in Transcriptions before inserting it.",
            DictationRunOutcome.CopyOnly when !string.IsNullOrWhiteSpace(result.Transcript?.Text) =>
                "Text copied to the clipboard. Paste it into the field where you want it.",
            DictationRunOutcome.CopyOnly => "Copy-only mode finished with no text to copy.",
            DictationRunOutcome.Cancelled => "Dictation cancelled.",
            DictationRunOutcome.NoSpeech => "Shruti did not detect speech.",
            DictationRunOutcome.Failed => snapshot.FailureSummary,
            _ => "Dictation finished."
        };

        string targetProcess = string.IsNullOrWhiteSpace(snapshot.TargetProcessName)
            ? "not captured"
            : DiagnosticTextRedactor.Redact(snapshot.TargetProcessName);

        var lines = new List<string>
        {
            $"Session state: {(result.StatusHistory.Count > 0 ? FormatState(snapshot.SessionState) : "not captured")}",
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
        if (result.Outcome == DictationRunOutcome.PreviewRequired)
        {
            AddOptionalLine(lines, "Preview reason", snapshot.FailureSummary);
        }

        DiagnosticsSnapshotText.Text = string.Join(Environment.NewLine, lines);
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
        bool isModels = string.Equals(page, "Models", StringComparison.OrdinalIgnoreCase);
        bool isSettings = string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase);
        bool isLibrary = page is "History" or "Insights" or "Dictionary" or "Snippets";
        if (!isModels && !isSettings && !isLibrary)
        {
            page = "History";
        }

        _currentPage = page;

        foreach (var item in new (string Name, LibraryPage Page, Button Button)[] {
            ("History", HistoryPage, HistoryNavButton), ("Insights", InsightsPage, InsightsNavButton),
            ("Dictionary", DictionaryEditor, DictionaryNavButton), ("Snippets", SnippetsPage, SnippetsNavButton) })
        {
            bool selected = item.Name == page;
            item.Page.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            SetNavigationButtonState(item.Button, selected);
            if (selected) _ = item.Page.RefreshAsync();
        }
        HistoryContainer.Visibility = page == "History" ? Visibility.Visible : Visibility.Collapsed;
        ModelsPage.Visibility = isModels ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

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
        _floatingBars.Dispose();
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
            DictationSessionState.RefiningTranscript => "Refining transcript",
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
