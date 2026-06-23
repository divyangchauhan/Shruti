using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Shruti.Platform.Windows;
using Shruti.Workflow.Dictation;
using WinRT.Interop;
using Windows.Graphics;

namespace Shruti.App.WinUI;

public sealed class FloatingMicWindow : Window
{
    private readonly Button _triggerButton;
    private readonly Button _dismissButton;
    private readonly FontIcon _triggerIcon;
    private readonly TextBlock _titleText;
    private readonly TextBlock _shortcutText;
    private readonly Border _root;
    private readonly IWindowsWindowVisibility _windowVisibility;
    private bool _isInitialized;

    public FloatingMicWindow(IWindowsWindowVisibility windowVisibility)
    {
        _windowVisibility = windowVisibility ?? throw new ArgumentNullException(nameof(windowVisibility));
        Title = "Shruti dictation";

        _triggerIcon = new FontIcon
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            Glyph = "\uE720",
            FontSize = 17
        };
        _triggerButton = new Button
        {
            Width = 42,
            Height = 42,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 185, 79)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 29, 5)),
            Content = _triggerIcon
        };
        AutomationProperties.SetName(_triggerButton, "Start dictation");
        ToolTipService.SetToolTip(_triggerButton, "Start or stop dictation");
        _triggerButton.Click += TriggerButton_Click;

        _titleText = new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = "Dictate"
        };
        _shortcutText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.72,
            Text = "Ctrl+Win+Space"
        };
        _dismissButton = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = null,
            Content = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                Glyph = "\uE711",
                FontSize = 12
            }
        };
        AutomationProperties.SetName(_dismissButton, "Hide floating microphone");
        ToolTipService.SetToolTip(_dismissButton, "Hide floating microphone");
        _dismissButton.Click += DismissButton_Click;

        var content = new Grid
        {
            ColumnSpacing = 10
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(_triggerButton);

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1
        };
        text.Children.Add(_titleText);
        text.Children.Add(_shortcutText);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        Grid.SetColumn(_dismissButton, 2);
        content.Children.Add(_dismissButton);

        _root = new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = content
        };
        Content = _root;
        _root.ActualThemeChanged += Root_ActualThemeChanged;
        AppWindow.Closing += AppWindow_Closing;
    }

    public event EventHandler? TriggerRequested;

    public event EventHandler? DismissRequested;

    public void Show(DictationShellState state, ElementTheme theme, string? shortcut)
    {
        ApplyTheme(theme);
        _shortcutText.Text = string.IsNullOrWhiteSpace(shortcut) ? "Hold shortcut" : shortcut;
        UpdateState(state);
        Activate();
        ApplyTheme(theme);

        if (_isInitialized)
        {
            return;
        }

        AppWindow.Resize(new SizeInt32(262, 64));
        _windowVisibility.MakeNonActivating(WindowNative.GetWindowHandle(this));
        _isInitialized = true;
    }

    public void ApplyTheme(ElementTheme theme)
    {
        _root.RequestedTheme = theme;
        bool isDark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && _root.ActualTheme == ElementTheme.Dark);
        _root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            isDark ? Windows.UI.Color.FromArgb(255, 48, 46, 41) : Windows.UI.Color.FromArgb(255, 255, 252, 246));
        _root.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            isDark ? Windows.UI.Color.FromArgb(255, 73, 69, 61) : Windows.UI.Color.FromArgb(255, 214, 208, 197));
    }

    public void UpdateState(DictationShellState state)
    {
        _triggerIcon.Glyph = state.IsRunning ? "\uE71A" : "\uE720";
        _titleText.Text = state.SessionState switch
        {
            Shruti.Core.DictationSessionState.Recording => "Recording",
            Shruti.Core.DictationSessionState.Paused => "Paused",
            Shruti.Core.DictationSessionState.TranscribingFinalAudio => "Transcribing",
            Shruti.Core.DictationSessionState.InsertingText => "Inserting",
            _ => "Dictate"
        };
        AutomationProperties.SetName(_triggerButton, state.IsRunning ? "Stop dictation" : "Start dictation");
        ToolTipService.SetToolTip(_triggerButton, state.IsRunning ? "Stop dictation" : "Start dictation");
        _triggerButton.IsEnabled = state.CanStart || state.CanStop;
    }

    public void Hide()
    {
        if (!_isInitialized)
        {
            return;
        }

        _windowVisibility.Hide(WindowNative.GetWindowHandle(this));
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        Hide();
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_root.RequestedTheme == ElementTheme.Default)
        {
            ApplyTheme(ElementTheme.Default);
        }
    }

    private void TriggerButton_Click(object sender, RoutedEventArgs e)
    {
        TriggerRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }
}
