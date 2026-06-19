using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.App.WinUI.Dictation;
using Shruti.Core;
using Shruti.Core.Dictation;

namespace Shruti.App.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly DictationShellController _controller;

    public MainWindow(DictationShellController controller)
    {
        InitializeComponent();

        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.StateChanged += Controller_StateChanged;

        InsertionModeComboBox.SelectedIndex = 0;
        ThemeComboBox.SelectedIndex = 0;
        UpdateView();
    }

    private async void PrimaryDictationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.State.IsRunning)
        {
            await _controller.StopAsync();
            return;
        }

        await _controller.StartAsync(GetSelectedInsertionMode());
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
