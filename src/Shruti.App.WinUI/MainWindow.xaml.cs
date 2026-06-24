using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Workflow.Dictation;
using Shruti.Core;
using Shruti.Core.Audio;
using Shruti.Core.Dictation;
using Shruti.Core.Triggers;
using Shruti.Platform.Windows;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using WinRT.Interop;
using Windows.Graphics;

namespace Shruti.App.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly DictationShellController _controller;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly TranscriptionOptionsProvider _transcriptionOptionsProvider;
    private readonly DictationTriggerRouter _triggerRouter;
    private readonly WindowsGlobalTriggerService _triggerService;
    private readonly WindowsTrayIconService _trayIconService;
    private readonly IWindowsWindowVisibility _windowVisibility;
    private readonly WindowsWindowMessageHost _windowMessageHost;
    private readonly DictationTriggerDispatcher _triggerDispatcher;
    private readonly CancellationTokenSource _triggerDispatchCancellation = new();
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly IntPtr _windowHandle;

    private FloatingMicWindow? _floatingMicWindow;
    private Task? _triggerDispatchTask;
    private bool _allowClose;
    private bool _isDisposed;
    private bool _audioDevicesLoaded;
    private bool _settingsLoaded;
    private bool _isApplyingSettings;
    private bool _floatingMicDismissedForSession;
    private bool _floatingMicShownForSession;
    private ShrutiSettings _settings = ShrutiSettings.Default;

    public MainWindow(
        DictationShellController controller,
        IAudioCaptureService audioCaptureService,
        ISettingsRepository settingsRepository,
        TranscriptionOptionsProvider transcriptionOptionsProvider,
        DictationTriggerRouter triggerRouter,
        WindowsGlobalTriggerService triggerService,
        WindowsTrayIconService trayIconService,
        IWindowsWindowVisibility windowVisibility)
    {
        InitializeComponent();

        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _transcriptionOptionsProvider = transcriptionOptionsProvider ?? throw new ArgumentNullException(nameof(transcriptionOptionsProvider));
        _triggerRouter = triggerRouter ?? throw new ArgumentNullException(nameof(triggerRouter));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
        _windowVisibility = windowVisibility ?? throw new ArgumentNullException(nameof(windowVisibility));
        _windowHandle = WindowNative.GetWindowHandle(this);
        _windowMessageHost = new WindowsWindowMessageHost(_windowHandle);
        _triggerDispatcher = new DictationTriggerDispatcher(_triggerService, _triggerRouter);

        _controller.StateChanged += Controller_StateChanged;
        _controller.AudioLevelChanged += Controller_AudioLevelChanged;
        _triggerRouter.FloatingWindowToggleRequested += TriggerRouter_FloatingWindowToggleRequested;
        _windowMessageHost.MessageReceived += WindowMessageHost_MessageReceived;
        _trayIconService.CommandInvoked += TrayIconService_CommandInvoked;
        AppWindow.Closing += AppWindow_Closing;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        Root.ActualThemeChanged += Root_ActualThemeChanged;
        AppWindow.Resize(new SizeInt32(1020, 760));
        ConfigureAppTitleBar();

        InsertionModeComboBox.SelectedIndex = 0;
        ThemeComboBox.SelectedIndex = 0;
        AudioRetentionComboBox.SelectedIndex = 0;
        BackendPreferenceComboBox.SelectedIndex = 0;
        ShowPage("Home");
        ConfigureNativeTriggers();
        StartTriggerDispatch();
        UpdateView();
    }

    public void ShowFromExternalActivation()
    {
        ShowMainWindow();
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
        _floatingMicWindow?.ApplyTheme(GetSelectedTheme());
        await PersistSettingsAsync();
    }

    private async void AudioRetentionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await PersistSettingsAsync();
    }

    private async void BackendPreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await PersistSettingsAsync();
    }

    private async void AllowSlowTranscriptionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        await PersistSettingsAsync();
    }

    private async void TriggerConfigurationCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, FloatingButtonCheckBox))
        {
            _floatingMicDismissedForSession = false;
            _floatingMicShownForSession = FloatingButtonCheckBox.IsChecked == true;
        }

        await ApplyTriggerConfigurationAsync();
    }

    private async void TriggerConfigurationInput_LostFocus(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        await EnsureSettingsLoadedAsync();
        await RefreshTranscriptionReadinessAsync();
        UpdateFloatingMicWindow();

        if (!_audioDevicesLoaded)
        {
            await LoadAudioDevicesAsync();
        }
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateAppTitleBarTheme();
    }

    private void ConfigureAppTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        UpdateAppTitleBarTheme();
    }

    private void UpdateAppTitleBarTheme()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        bool isDark = Root.ActualTheme == ElementTheme.Dark;
        Windows.UI.Color background = isDark
            ? Windows.UI.Color.FromArgb(255, 17, 16, 14)
            : Windows.UI.Color.FromArgb(255, 247, 245, 240);
        Windows.UI.Color foreground = isDark
            ? Windows.UI.Color.FromArgb(255, 250, 247, 239)
            : Windows.UI.Color.FromArgb(255, 35, 33, 29);
        Windows.UI.Color mutedForeground = isDark
            ? Windows.UI.Color.FromArgb(255, 207, 199, 186)
            : Windows.UI.Color.FromArgb(255, 98, 93, 84);
        Windows.UI.Color hoverBackground = isDark
            ? Windows.UI.Color.FromArgb(255, 35, 33, 29)
            : Windows.UI.Color.FromArgb(255, 238, 234, 226);
        Windows.UI.Color pressedBackground = isDark
            ? Windows.UI.Color.FromArgb(255, 63, 58, 50)
            : Windows.UI.Color.FromArgb(255, 216, 210, 199);

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
            AudioLevelBar.Value = Math.Clamp(level.Peak * 100, AudioLevelBar.Minimum, AudioLevelBar.Maximum));
    }

    private void TriggerRouter_FloatingWindowToggleRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ToggleFloatingMicWindow);
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
            TriggerStatusText.Text = ex.Message;
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
        StatusText.Text = state.StatusText;
        TargetText.Text = state.TargetDescription;
        UserMessageText.Text = state.UserMessage;
        TranscriptPreviewBox.Text = state.TranscriptPreview;

        ErrorText.Text = state.ErrorText ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(state.ErrorText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PrimaryButtonLabel.Text = state.IsRunning ? "Stop dictation" : "Start dictation";
        PrimaryButtonIcon.Glyph = state.IsRunning ? "\uE71A" : "\uE720";
        PrimaryDictationButton.IsEnabled = state.CanStart || state.CanStop;
        CancelButton.IsEnabled = state.CanCancel;
        PauseButtonLabel.Text = state.IsPaused ? "Resume" : "Pause";
        PauseButton.IsEnabled = state.CanPause;
        RetryButton.IsEnabled = state.CanRetry;
        CopyButton.IsEnabled = state.CanCopy;
        InsertPreviewButton.IsEnabled = state.CanInsertPreview;
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
        }

        StatusPillText.Text = state.SessionState == DictationSessionState.Idle
            ? "Ready"
            : FormatState(state.SessionState);

        _trayIconService.UpdateDictationState(state.IsRunning);
        _floatingMicWindow?.UpdateState(state);
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
            DispatcherQueue.TryEnqueue(() => TriggerStatusText.Text = ex.Message);
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
            UpdateFloatingMicWindow();
            TriggerStatusText.Text = "Triggers are active.";
            if (persist)
            {
                await PersistSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            ApplyTriggerConfigurationToControls(_triggerService.Configuration);
            TriggerStatusText.Text = ex.Message;
        }
    }

    private TriggerConfiguration GetTriggerConfigurationFromControls()
    {
        return new TriggerConfiguration(
            EnableGlobalHotkey: GlobalHotkeyCheckBox.IsChecked == true,
            EnablePushToTalk: PushToTalkCheckBox.IsChecked == true,
            EnableFloatingButton: FloatingButtonCheckBox.IsChecked == true,
            EnableTrayMenu: TrayMenuCheckBox.IsChecked == true,
            HotkeyGesture: HotkeyGestureTextBox.Text,
            PushToTalkKey: PushToTalkKeyTextBox.Text,
            EnableFloatingWindowShortcut: FloatingWindowShortcutCheckBox.IsChecked == true,
            FloatingWindowShortcut: FloatingWindowShortcutTextBox.Text);
    }

    private void ApplyTriggerConfigurationToControls(TriggerConfiguration configuration)
    {
        GlobalHotkeyCheckBox.IsChecked = configuration.EnableGlobalHotkey;
        PushToTalkCheckBox.IsChecked = configuration.EnablePushToTalk;
        FloatingButtonCheckBox.IsChecked = configuration.EnableFloatingButton;
        FloatingWindowShortcutCheckBox.IsChecked = configuration.EnableFloatingWindowShortcut;
        TrayMenuCheckBox.IsChecked = configuration.EnableTrayMenu;
        HotkeyGestureTextBox.Text = configuration.HotkeyGesture ?? string.Empty;
        PushToTalkKeyTextBox.Text = configuration.PushToTalkKey ?? string.Empty;
        FloatingWindowShortcutTextBox.Text = configuration.FloatingWindowShortcut ?? string.Empty;
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
                _isApplyingSettings = true;
                _transcriptionOptionsProvider.ApplySettings(_settings);
                ApplySettingsToControls(_settings);
                _controller.SetInsertionMode(_settings.InsertionMode);
                _controller.SetAudioInputDevice(_settings.AudioInputDeviceId);
                Root.RequestedTheme = ToElementTheme(_settings.ThemePreference);
                await ApplyTriggerConfigurationAsync(persist: false);
                await RefreshTranscriptionReadinessAsync();
            }
            catch (Exception ex)
            {
                TriggerStatusText.Text = $"Settings could not be loaded: {ex.Message}";
            }
            finally
            {
                _isApplyingSettings = false;
                _settingsLoaded = true;
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
        AllowSlowTranscriptionCheckBox.IsChecked = settings.AllowSlowTranscription;
        ApplyTriggerConfigurationToControls(settings.TriggerConfiguration);
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
                InsertionMode = GetSelectedInsertionMode(),
                ThemePreference = GetSelectedThemePreference(),
                AudioRetentionPolicy = GetSelectedAudioRetentionPolicy(),
                BackendPreference = GetSelectedBackendPreference(),
                AllowSlowTranscription = AllowSlowTranscriptionCheckBox.IsChecked == true,
                TriggerConfiguration = GetTriggerConfigurationFromControls()
            };
            _transcriptionOptionsProvider.ApplySettings(_settings);
            await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
            await RefreshTranscriptionReadinessAsync();
        }
        catch (Exception ex)
        {
            TriggerStatusText.Text = $"Settings could not be saved: {ex.Message}";
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

    private async Task RefreshTranscriptionReadinessAsync()
    {
        try
        {
            TranscriptionModelDescriptor model = _transcriptionOptionsProvider.CreateModelDescriptor();
            TranscriptionReadinessResult readiness = await _transcriptionOptionsProvider
                .EvaluateReadinessAsync(CancellationToken.None);
            string backend = readiness.SelectedBackend?.ToString() ?? _settings.BackendPreference.ToString();
            string device = readiness.DeviceName ?? "No compatible device";

            ModelSummaryText.Text = model.DisplayName;
            BackendSummaryText.Text = readiness.CanProceed
                ? $"{readiness.Provider?.DisplayName ?? model.ProviderId} / {backend}"
                : "Unavailable";
            ActiveBackendText.Text = readiness.CanProceed
                ? $"{readiness.Provider?.DisplayName ?? model.ProviderId} / {backend} / {device}"
                : $"{model.ProviderId} / {backend} / unavailable";
            BackendReadinessText.Text = FormatReadiness(readiness);
        }
        catch (Exception ex)
        {
            BackendSummaryText.Text = "Unavailable";
            ActiveBackendText.Text = "Unavailable";
            BackendReadinessText.Text = ex.Message;
        }
    }

    private static string FormatReadiness(TranscriptionReadinessResult readiness)
    {
        string warnings = readiness.EffectiveWarnings.Count == 0
            ? string.Empty
            : $" {string.Join(" ", readiness.EffectiveWarnings)}";
        return readiness.Status switch
        {
            TranscriptionReadinessStatus.Ready => $"{readiness.Message}{warnings}",
            TranscriptionReadinessStatus.SlowModeRequired => $"{readiness.Message}{warnings}",
            TranscriptionReadinessStatus.Unsupported => readiness.Message,
            _ => readiness.Message
        };
    }

    private void UpdateFloatingMicWindow()
    {
        bool shouldShow = (_triggerService.Configuration.EnableFloatingButton || _floatingMicShownForSession) &&
            !_floatingMicDismissedForSession;
        if (!shouldShow)
        {
            HideFloatingMicWindow();
            return;
        }

        _floatingMicWindow ??= CreateFloatingMicWindow();
        _floatingMicWindow.Show(
            _controller.State,
            GetSelectedTheme(),
            _triggerService.Configuration.PushToTalkKey);
    }

    private FloatingMicWindow CreateFloatingMicWindow()
    {
        var floatingMicWindow = new FloatingMicWindow(_windowVisibility);
        floatingMicWindow.TriggerRequested += FloatingMicWindow_TriggerRequested;
        floatingMicWindow.DismissRequested += FloatingMicWindow_DismissRequested;
        return floatingMicWindow;
    }

    private async void FloatingMicWindow_TriggerRequested(object? sender, EventArgs e)
    {
        await RaiseTriggerAsync(DictationTriggerKind.FloatingButton, "floating-button");
    }

    private void FloatingMicWindow_DismissRequested(object? sender, EventArgs e)
    {
        _floatingMicDismissedForSession = true;
        _floatingMicShownForSession = false;
        HideFloatingMicWindow();
    }

    private void ToggleFloatingMicWindow()
    {
        if (_floatingMicWindow?.IsVisible == true)
        {
            _floatingMicDismissedForSession = true;
            _floatingMicShownForSession = false;
            HideFloatingMicWindow();
            return;
        }

        _floatingMicDismissedForSession = false;
        _floatingMicShownForSession = true;
        UpdateFloatingMicWindow();
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
        bool isHistory = string.Equals(page, "History", StringComparison.OrdinalIgnoreCase);
        bool isModels = string.Equals(page, "Models", StringComparison.OrdinalIgnoreCase);
        bool isSettings = string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase);

        HomePage.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = isHistory ? Visibility.Visible : Visibility.Collapsed;
        ModelsPage.Visibility = isModels ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

        PageTitleText.Text = page switch
        {
            "History" => "Dictation history",
            "Models" => "Models",
            "Settings" => "Settings",
            _ => "Ready to dictate"
        };
        PageSubtitleText.Text = page switch
        {
            "History" => "Review dictations when local history is connected.",
            "Models" => "Inspect the local speech models Shruti can use.",
            "Settings" => "Configure Shruti for the way you work.",
            _ => "Hold your shortcut, speak naturally, and release to finish."
        };

        SetNavigationButtonState(HomeNavButton, isHome);
        SetNavigationButtonState(HistoryNavButton, isHistory);
        SetNavigationButtonState(ModelsNavButton, isModels);
        SetNavigationButtonState(SettingsNavButton, isSettings);
    }

    private static void SetNavigationButtonState(Button button, bool isSelected)
    {
        button.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        button.Opacity = isSelected ? 1 : 0.72;
    }

    private void QuitApplication()
    {
        _allowClose = true;
        CloseFloatingMicWindowForApplicationExit();
        DisposeNativeTriggers();
        Close();
        Application.Current.Exit();
    }

    private void HideFloatingMicWindow()
    {
        _floatingMicWindow?.Hide();
    }

    private void CloseFloatingMicWindowForApplicationExit()
    {
        if (_floatingMicWindow is null)
        {
            return;
        }

        _floatingMicWindow.TriggerRequested -= FloatingMicWindow_TriggerRequested;
        _floatingMicWindow.DismissRequested -= FloatingMicWindow_DismissRequested;
        _floatingMicWindow.CloseForApplicationExit();
        _floatingMicWindow = null;
    }

    private void DisposeNativeTriggers()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _controller.AudioLevelChanged -= Controller_AudioLevelChanged;
        _triggerRouter.FloatingWindowToggleRequested -= TriggerRouter_FloatingWindowToggleRequested;
        _triggerDispatchCancellation.Cancel();
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
}
