using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.App.WinUI.Dictation;
using Shruti.Core;
using Shruti.Core.Audio;
using Shruti.Core.Dictation;
using Shruti.Core.Triggers;
using Shruti.Platform.Windows;
using Shruti.Storage;
using WinRT.Interop;

namespace Shruti.App.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly DictationShellController _controller;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ISettingsRepository _settingsRepository;
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
    private ShrutiSettings _settings = ShrutiSettings.Default;

    public MainWindow(
        DictationShellController controller,
        IAudioCaptureService audioCaptureService,
        ISettingsRepository settingsRepository,
        DictationTriggerRouter triggerRouter,
        WindowsGlobalTriggerService triggerService,
        WindowsTrayIconService trayIconService,
        IWindowsWindowVisibility windowVisibility)
    {
        InitializeComponent();

        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _triggerRouter = triggerRouter ?? throw new ArgumentNullException(nameof(triggerRouter));
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

        InsertionModeComboBox.SelectedIndex = 0;
        ThemeComboBox.SelectedIndex = 0;
        AudioRetentionComboBox.SelectedIndex = 0;
        ConfigureNativeTriggers();
        StartTriggerDispatch();
        UpdateView();
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
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
        _floatingMicWindow?.ApplyTheme(GetSelectedTheme());
        await PersistSettingsAsync();
    }

    private async void AudioRetentionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await PersistSettingsAsync();
    }

    private async void TriggerConfigurationCheckBox_Click(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private async void TriggerConfigurationInput_LostFocus(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        await EnsureSettingsLoadedAsync();
        UpdateFloatingMicWindow();

        if (!_audioDevicesLoaded)
        {
            await LoadAudioDevicesAsync();
        }
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
        if (!DispatcherQueue.TryEnqueue(UpdateView))
        {
            UpdateView();
        }
    }

    private void Controller_AudioLevelChanged(object? sender, AudioLevelFrame level)
    {
        DispatcherQueue.TryEnqueue(() =>
            AudioLevelBar.Value = Math.Clamp(level.Peak * 100, AudioLevelBar.Minimum, AudioLevelBar.Maximum));
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
                return;
            }

            string selectedDeviceId = _controller.AudioOptions.DeviceId ??
                devices.FirstOrDefault(device => device.IsDefault)?.Id ??
                devices[0].Id;
            SelectAudioDevice(selectedDeviceId);
            _audioDevicesLoaded = true;
        }
        catch (Exception ex)
        {
            AudioDeviceComboBox.PlaceholderText = "Microphone unavailable";
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

        PrimaryDictationButton.Content = state.IsRunning ? "Stop" : "Start";
        PrimaryDictationButton.IsEnabled = state.CanStart || state.CanStop;
        CancelButton.IsEnabled = state.CanCancel;
        PauseButton.Content = state.IsPaused ? "Resume" : "Pause";
        PauseButton.IsEnabled = state.CanPause;
        RetryButton.IsEnabled = state.CanRetry;
        CopyButton.IsEnabled = state.CanCopy;
        InsertionModeComboBox.IsEnabled = !state.IsRunning;
        AudioDeviceComboBox.IsEnabled = !state.IsRunning && _audioDevicesLoaded;
        if (!state.IsRunning)
        {
            AudioLevelBar.Value = 0;
        }

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
            _trayIconService.SetVisible(_triggerService.Configuration.EnableTrayMenu);
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
            _trayIconService.SetVisible(configuration.EnableTrayMenu);
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
            PushToTalkKey: PushToTalkKeyTextBox.Text);
    }

    private void ApplyTriggerConfigurationToControls(TriggerConfiguration configuration)
    {
        GlobalHotkeyCheckBox.IsChecked = configuration.EnableGlobalHotkey;
        PushToTalkCheckBox.IsChecked = configuration.EnablePushToTalk;
        FloatingButtonCheckBox.IsChecked = configuration.EnableFloatingButton;
        TrayMenuCheckBox.IsChecked = configuration.EnableTrayMenu;
        HotkeyGestureTextBox.Text = configuration.HotkeyGesture ?? string.Empty;
        PushToTalkKeyTextBox.Text = configuration.PushToTalkKey ?? string.Empty;
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
                ApplySettingsToControls(_settings);
                _controller.SetInsertionMode(_settings.InsertionMode);
                _controller.SetAudioInputDevice(_settings.AudioInputDeviceId);
                Root.RequestedTheme = ToElementTheme(_settings.ThemePreference);
                await ApplyTriggerConfigurationAsync(persist: false);
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
                TriggerConfiguration = GetTriggerConfigurationFromControls()
            };
            await _settingsRepository.SaveAsync(_settings, CancellationToken.None);
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

    private void UpdateFloatingMicWindow()
    {
        if (!_triggerService.Configuration.EnableFloatingButton)
        {
            CloseFloatingMicWindow();
            return;
        }

        _floatingMicWindow ??= CreateFloatingMicWindow();
        _floatingMicWindow.Show(_controller.State, GetSelectedTheme());
    }

    private FloatingMicWindow CreateFloatingMicWindow()
    {
        var floatingMicWindow = new FloatingMicWindow(_windowVisibility);
        floatingMicWindow.TriggerRequested += FloatingMicWindow_TriggerRequested;
        return floatingMicWindow;
    }

    private async void FloatingMicWindow_TriggerRequested(object? sender, EventArgs e)
    {
        await RaiseTriggerAsync(DictationTriggerKind.FloatingButton, "floating-button");
    }

    private async void TrayIconService_CommandInvoked(WindowsTrayCommand command)
    {
        switch (command)
        {
            case WindowsTrayCommand.Toggle:
                await RaiseTriggerAsync(DictationTriggerKind.TrayMenu, "tray-menu");
                break;

            case WindowsTrayCommand.Start:
                if (!_controller.State.IsRunning)
                {
                    await RaiseTriggerAsync(DictationTriggerKind.TrayMenu, "tray-menu");
                }

                break;

            case WindowsTrayCommand.Stop:
                if (_controller.State.IsRunning)
                {
                    await RaiseTriggerAsync(DictationTriggerKind.TrayMenu, "tray-menu");
                }

                break;

            case WindowsTrayCommand.Cancel:
                await _controller.CancelAsync();
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
        _windowVisibility.ShowAndActivate(_windowHandle);
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void QuitApplication()
    {
        _allowClose = true;
        CloseFloatingMicWindow();
        DisposeNativeTriggers();
        Close();
    }

    private void CloseFloatingMicWindow()
    {
        if (_floatingMicWindow is null)
        {
            return;
        }

        _floatingMicWindow.TriggerRequested -= FloatingMicWindow_TriggerRequested;
        _floatingMicWindow.Close();
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
