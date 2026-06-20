using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.App.WinUI.Dictation;
using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Core.Triggers;
using Shruti.Platform.Windows;
using WinRT.Interop;

namespace Shruti.App.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly DictationShellController _controller;
    private readonly DictationTriggerRouter _triggerRouter;
    private readonly WindowsGlobalTriggerService _triggerService;
    private readonly WindowsTrayIconService _trayIconService;
    private readonly IWindowsWindowVisibility _windowVisibility;
    private readonly WindowsWindowMessageHost _windowMessageHost;
    private readonly DictationTriggerDispatcher _triggerDispatcher;
    private readonly CancellationTokenSource _triggerDispatchCancellation = new();
    private readonly IntPtr _windowHandle;

    private FloatingMicWindow? _floatingMicWindow;
    private Task? _triggerDispatchTask;
    private bool _allowClose;
    private bool _isDisposed;

    public MainWindow(
        DictationShellController controller,
        DictationTriggerRouter triggerRouter,
        WindowsGlobalTriggerService triggerService,
        WindowsTrayIconService trayIconService,
        IWindowsWindowVisibility windowVisibility)
    {
        InitializeComponent();

        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _triggerRouter = triggerRouter ?? throw new ArgumentNullException(nameof(triggerRouter));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _trayIconService = trayIconService ?? throw new ArgumentNullException(nameof(trayIconService));
        _windowVisibility = windowVisibility ?? throw new ArgumentNullException(nameof(windowVisibility));
        _windowHandle = WindowNative.GetWindowHandle(this);
        _windowMessageHost = new WindowsWindowMessageHost(_windowHandle);
        _triggerDispatcher = new DictationTriggerDispatcher(_triggerService, _triggerRouter);

        _controller.StateChanged += Controller_StateChanged;
        _windowMessageHost.MessageReceived += WindowMessageHost_MessageReceived;
        _trayIconService.CommandInvoked += TrayIconService_CommandInvoked;
        AppWindow.Closing += AppWindow_Closing;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;

        InsertionModeComboBox.SelectedIndex = 0;
        ThemeComboBox.SelectedIndex = 0;
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

    private void InsertionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_controller is null)
        {
            return;
        }

        _controller.SetInsertionMode(GetSelectedInsertionMode());
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Root is null)
        {
            return;
        }

        Root.RequestedTheme = GetSelectedTheme();
        _floatingMicWindow?.ApplyTheme(GetSelectedTheme());
    }

    private async void TriggerConfigurationCheckBox_Click(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private async void TriggerConfigurationInput_LostFocus(object sender, RoutedEventArgs e)
    {
        await ApplyTriggerConfigurationAsync();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        UpdateFloatingMicWindow();
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

    private async Task ApplyTriggerConfigurationAsync()
    {
        var configuration = new TriggerConfiguration(
            EnableGlobalHotkey: GlobalHotkeyCheckBox.IsChecked == true,
            EnablePushToTalk: PushToTalkCheckBox.IsChecked == true,
            EnableFloatingButton: FloatingButtonCheckBox.IsChecked == true,
            EnableTrayMenu: TrayMenuCheckBox.IsChecked == true,
            HotkeyGesture: HotkeyGestureTextBox.Text,
            PushToTalkKey: PushToTalkKeyTextBox.Text);

        try
        {
            await _triggerService.ConfigureAsync(configuration, CancellationToken.None);
            _trayIconService.SetVisible(configuration.EnableTrayMenu);
            UpdateFloatingMicWindow();
            TriggerStatusText.Text = "Triggers are active.";
        }
        catch (Exception ex)
        {
            ApplyTriggerConfigurationToControls(_triggerService.Configuration);
            TriggerStatusText.Text = ex.Message;
        }
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
