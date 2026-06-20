using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Workflow.Dictation;
using Shruti.Platform.Windows;
using WinRT.Interop;
using Windows.Graphics;

namespace Shruti.App.WinUI;

public sealed class FloatingMicWindow : Window
{
    private readonly Button _triggerButton;
    private readonly Border _root;
    private readonly IWindowsWindowVisibility _windowVisibility;
    private bool _isInitialized;

    public FloatingMicWindow(IWindowsWindowVisibility windowVisibility)
    {
        _windowVisibility = windowVisibility ?? throw new ArgumentNullException(nameof(windowVisibility));
        Title = "Shruti dictation";

        _triggerButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = new SymbolIcon(Symbol.Microphone)
        };

        ToolTipService.SetToolTip(_triggerButton, "Start dictation");
        _triggerButton.Click += TriggerButton_Click;

        _root = new Border
        {
            Padding = new Thickness(8),
            Child = _triggerButton
        };
        Content = _root;
    }

    public event EventHandler? TriggerRequested;

    public void Show(DictationShellState state, ElementTheme theme)
    {
        ApplyTheme(theme);
        UpdateState(state);
        Activate();

        if (_isInitialized)
        {
            return;
        }

        AppWindow.Resize(new SizeInt32(92, 92));
        _windowVisibility.MakeNonActivating(WindowNative.GetWindowHandle(this));
        _isInitialized = true;
    }

    public void ApplyTheme(ElementTheme theme)
    {
        _root.RequestedTheme = theme;
    }

    public void UpdateState(DictationShellState state)
    {
        _triggerButton.Content = new SymbolIcon(state.IsRunning ? Symbol.Stop : Symbol.Microphone);
        ToolTipService.SetToolTip(_triggerButton, state.IsRunning ? "Stop dictation" : "Start dictation");
        _triggerButton.IsEnabled = state.CanStart || state.CanStop;
    }

    private void TriggerButton_Click(object sender, RoutedEventArgs e)
    {
        TriggerRequested?.Invoke(this, EventArgs.Empty);
    }
}
