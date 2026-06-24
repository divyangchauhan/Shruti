using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Shruti.Platform.Windows;
using Shruti.Workflow.Dictation;
using WinRT.Interop;
using Windows.Graphics;

namespace Shruti.App.WinUI;

public sealed class FloatingMicWindow : Window
{
    private const double PreferredWindowWidthDip = 304;
    private const double PreferredWindowHeightDip = 112;
    private const double DefaultDpi = 96;

    private readonly Button _triggerButton;
    private readonly Button _dismissButton;
    private readonly FontIcon _triggerIcon;
    private readonly TextBlock _titleText;
    private readonly TextBlock _shortcutText;
    private readonly Border _root;
    private readonly IWindowsWindowVisibility _windowVisibility;
    private bool _isInitialized;
    private bool _allowClose;

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
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 247, 190, 85)),
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
        ConfigurePresenter();
    }

    public event EventHandler? TriggerRequested;

    public event EventHandler? DismissRequested;

    public bool IsVisible { get; private set; }

    public void Show(DictationShellState state, ElementTheme theme, string? shortcut)
    {
        ApplyTheme(theme);
        _shortcutText.Text = string.IsNullOrWhiteSpace(shortcut) ? "Hold shortcut" : shortcut;
        UpdateState(state);
        ResizeForCurrentDpi();

        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        if (!_isInitialized)
        {
            _windowVisibility.MakeNonActivating(windowHandle);
            Activate();
            _windowVisibility.MakeNonActivating(windowHandle);
            ApplyTheme(theme);
            _isInitialized = true;
            IsVisible = true;
            return;
        }

        if (!IsVisible)
        {
            _windowVisibility.ShowWithoutActivating(windowHandle);
        }

        IsVisible = true;
    }

    public void ApplyTheme(ElementTheme theme)
    {
        _root.RequestedTheme = theme;
        bool isDark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && _root.ActualTheme == ElementTheme.Dark);
        _root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            isDark ? Windows.UI.Color.FromArgb(255, 35, 33, 29) : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        _root.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            isDark ? Windows.UI.Color.FromArgb(255, 63, 58, 50) : Windows.UI.Color.FromArgb(255, 216, 210, 199));
        ApplyTitleBarTheme(isDark);
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
        IsVisible = false;

        if (!_isInitialized)
        {
            return;
        }

        _windowVisibility.Hide(WindowNative.GetWindowHandle(this));
    }

    public void CloseForApplicationExit()
    {
        _allowClose = true;
        IsVisible = false;
        Close();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        Hide();
        DismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
    }

    private void ResizeForCurrentDpi()
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(windowHandle);
        double scale = dpi == 0 ? 1 : dpi / DefaultDpi;
        AppWindow.Resize(new SizeInt32(
            checked((int)Math.Round(PreferredWindowWidthDip * scale)),
            checked((int)Math.Round(PreferredWindowHeightDip * scale))));
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_root.RequestedTheme == ElementTheme.Default)
        {
            ApplyTheme(ElementTheme.Default);
        }
    }

    private void ApplyTitleBarTheme(bool isDark)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

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

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = mutedForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = mutedForeground;
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
