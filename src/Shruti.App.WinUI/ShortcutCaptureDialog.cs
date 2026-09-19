using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Shruti.Core.Diagnostics;
using Shruti.Platform.Windows;

namespace Shruti.App.WinUI;

internal sealed class ShortcutCaptureDialog : ContentDialog
{
    private readonly Window _owner;
    private readonly IntPtr _handle;
    private readonly bool _allowSingleKey;
    private readonly Func<string, Task> _save;
    private readonly StackPanel _keys = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly InfoBar _error = new() { Severity = InfoBarSeverity.Error, IsClosable = false };
    private readonly Button _record = new() { Content = "Record again", HorizontalAlignment = HorizontalAlignment.Center };
    private WindowsShortcutCapture? _capture;
    private string? _gesture;
    private bool _isOpen;

    public ShortcutCaptureDialog(Window owner, IntPtr handle, XamlRoot root, string current, bool holdToTalk, Func<string, Task> save)
    {
        _owner = owner;
        _handle = handle;
        _allowSingleKey = holdToTalk;
        _save = save;
        XamlRoot = root;
        RequestedTheme = ((FrameworkElement)owner.Content).ActualTheme;
        Title = holdToTalk ? "Change hold-to-dictate shortcut" : "Change single-press shortcut";
        PrimaryButtonText = "Save shortcut";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.None;
        IsPrimaryButtonEnabled = false;

        var body = new StackPanel { Spacing = 20, MinWidth = 360, MaxWidth = 440 };
        body.Children.Add(new TextBlock
        {
            Text = holdToTalk ? "Hold this shortcut while you speak. Release it to finish." : "Press this shortcut to start or stop dictation.",
            TextWrapping = TextWrapping.Wrap
        });
        var currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        currentRow.Children.Add(new TextBlock { Text = "Current", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        var currentKeys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        SetKeycaps(currentKeys, current);
        currentRow.Children.Add(currentKeys);
        body.Children.Add(currentRow);
        var captureArea = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        captureArea.Children.Add(_keys);
        captureArea.Children.Add(_status);
        body.Children.Add(new Border
        {
            Padding = new Thickness(20, 28, 20, 28),
            MinHeight = 136,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            Child = captureArea
        });
        body.Children.Add(_record);
        body.Children.Add(_error);
        body.Children.Add(new TextBlock
        {
            Text = (holdToTalk ? "Use Ctrl + Win, or a modifier and a key such as Ctrl + Space. Press Esc to cancel." : "Use a modifier and a key, such as Ctrl + Space. Press Esc to cancel.") +
                (holdToTalk ? " A function key or Right Ctrl can also be used alone." : ""),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7
        });
        Content = body;
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_record, "RecordShortcutAgain");
        _record.Click += (_, _) => StartCapture();
        Opened += (_, _) =>
        {
            _isOpen = true;
            _owner.Activated += OwnerActivated;
            StartCapture();
        };
        Closed += (_, _) =>
        {
            _isOpen = false;
            StopCapture();
            _owner.Activated -= OwnerActivated;
        };
        PrimaryButtonClick += SaveClicked;
    }

    internal static void SetKeycaps(StackPanel panel, string gesture)
    {
        panel.Children.Clear();
        foreach (string key in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            panel.Children.Add(new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Child = new TextBlock { Text = key == "RightControl" ? "Right Ctrl" : key, FontSize = 13 }
            });
        }
        AutomationProperties.SetName(panel, gesture.Replace("+", " + ", StringComparison.Ordinal));
    }

    private void StartCapture()
    {
        StopCapture();
        _gesture = null;
        IsPrimaryButtonEnabled = false;
        _error.IsOpen = false;
        _keys.Children.Clear();
        _keys.Children.Add(new TextBlock { Text = "Press your shortcut", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _status.Text = "Listening for keys…";
        _record.IsEnabled = false;
        try
        {
            _capture = new WindowsShortcutCapture(_handle, _allowSingleKey);
            _capture.Changed += CaptureChanged;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            _record.IsEnabled = true;
        }
    }

    private void CaptureChanged(object? sender, EventArgs args)
    {
        // Keep the native hook callback short. It only records key state.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isOpen || sender is not WindowsShortcutCapture capture || capture != _capture) return;
            WindowsShortcutRecorder recorder = capture.Recorder;
            SetKeycaps(_keys, recorder.DisplayText);
            _status.Text = "Release all keys to finish.";
            if (!recorder.IsComplete) return;
            StopCapture();
            if (recorder.IsCancelled)
            {
                Hide();
                return;
            }
            _gesture = recorder.Gesture;
            _record.IsEnabled = true;
            IsPrimaryButtonEnabled = _gesture is not null;
            _status.Text = _gesture is null ? "Try another shortcut." : "Ready to save";
            if (_gesture is not null) SetKeycaps(_keys, _gesture);
            if (recorder.Error is not null) ShowError(recorder.Error);
        });
    }

    private void OwnerActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated || _capture is null) return;
        StopCapture();
        _gesture = null;
        IsPrimaryButtonEnabled = false;
        _keys.Children.Clear();
        _status.Text = "Capture paused. Select Record again to continue.";
        _record.IsEnabled = true;
    }

    private void StopCapture()
    {
        if (_capture is null) return;
        _capture.Changed -= CaptureChanged;
        _capture.Dispose();
        _capture = null;
    }

    private async void SaveClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_gesture is null) { args.Cancel = true; return; }
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        _record.IsEnabled = false;
        try
        {
            await _save(_gesture);
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            ShowError(exception.Message);
            IsPrimaryButtonEnabled = true;
        }
        finally
        {
            _record.IsEnabled = true;
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        _error.Message = DiagnosticTextRedactor.Redact(message);
        _error.IsOpen = true;
    }
}
