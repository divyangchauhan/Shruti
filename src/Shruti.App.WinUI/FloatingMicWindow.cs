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
    private const double MinimumWindowWidthDip = 356;
    private const double PreferredWindowHeightDip = 60;
    private const double DefaultDpi = 96;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowBorderColor = 34;
    private const int DwmRoundPreference = 2;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    private readonly Button _triggerButton;
    private readonly Button _dismissButton;
    private readonly FontIcon _triggerIcon;
    private readonly TextBlock _titleText;
    private readonly TextBlock _shortcutText;
    private readonly Border _root;
    private readonly List<Border> _waveformBars = [];
    private readonly IWindowsWindowVisibility _windowVisibility;
    private DictationShellState? _lastState;
    private bool _isInitialized;
    private bool _allowClose;
    private bool _isDark;

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
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 233, 216)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 188, 86, 16)),
            BorderThickness = new Thickness(0),
            IsTabStop = false,
            UseSystemFocusVisuals = false,
            Content = _triggerIcon
        };
        AutomationProperties.SetName(_triggerButton, "Start dictation");
        ToolTipService.SetToolTip(_triggerButton, "Start or stop dictation");
        _triggerButton.Click += TriggerButton_Click;

        _dismissButton = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            Content = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                Glyph = "\uE8BB",
                FontSize = 12
            }
        };
        AutomationProperties.SetName(_dismissButton, "Close floating dictation window");
        ToolTipService.SetToolTip(_dismissButton, "Close");
        _dismissButton.Click += DismissButton_Click;

        _titleText = new TextBlock
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = "Ready to dictate"
        };
        _shortcutText = new TextBlock
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            Opacity = 0.7,
            Text = "Ctrl+Win+Space · on this PC"
        };
        var content = new Grid
        {
            ColumnSpacing = 10
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(_triggerButton);

        var waveform = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center
        };
        for (int index = 0; index < 12; index++)
        {
            var bar = new Border
            {
                Width = 2.5,
                Height = 4 + ((index * 7) % 13),
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(2)
            };
            _waveformBars.Add(bar);
            waveform.Children.Add(bar);
        }

        Grid.SetColumn(waveform, 1);
        content.Children.Add(waveform);

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1
        };
        text.Children.Add(_titleText);
        text.Children.Add(_shortcutText);
        Grid.SetColumn(text, 2);
        content.Children.Add(text);

        Grid.SetColumn(_dismissButton, 3);
        content.Children.Add(_dismissButton);

        _root = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(30),
            BorderThickness = new Thickness(1),
            Child = content
        };
        Content = _root;
        _root.ActualThemeChanged += Root_ActualThemeChanged;
        AppIcon.Apply(AppWindow);
        AppWindow.Closing += AppWindow_Closing;
        ConfigurePresenter();
    }

    public event EventHandler? TriggerRequested;

    public event EventHandler? DismissRequested;

    public bool IsVisible { get; private set; }

    public void Show(DictationShellState state, ElementTheme theme, string? modelName)
    {
        ApplyTheme(theme);
        _shortcutText.Text = $"{(string.IsNullOrWhiteSpace(modelName) ? "Local model" : modelName)} · on this PC";
        UpdateState(state);
        ResizeForCurrentDpi(modelName);

        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        ApplyRoundedWindowCorners(windowHandle);
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
        _isDark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && _root.ActualTheme == ElementTheme.Dark);
        _root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            _isDark ? Windows.UI.Color.FromArgb(255, 44, 41, 37) : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        _root.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            _isDark ? Windows.UI.Color.FromArgb(255, 68, 64, 58) : Windows.UI.Color.FromArgb(255, 231, 228, 223));
        Windows.UI.Color textColor = _isDark
            ? Windows.UI.Color.FromArgb(255, 243, 241, 238)
            : Windows.UI.Color.FromArgb(255, 30, 28, 25);
        _titleText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(textColor);
        _shortcutText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(textColor);
        _dismissButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(textColor);
        ApplyTitleBarTheme(_isDark);
        if (_lastState is not null)
        {
            UpdateState(_lastState);
        }
    }

    public void UpdateState(DictationShellState state)
    {
        _lastState = state;
        _triggerIcon.Glyph = state.IsRunning ? "\uE71A" : "\uE720";
        bool listening = state.SessionState == Shruti.Core.DictationSessionState.Recording;
        _titleText.Text = state.SessionState switch
        {
            Shruti.Core.DictationSessionState.Recording => "Listening…",
            Shruti.Core.DictationSessionState.Paused => "Paused",
            Shruti.Core.DictationSessionState.TranscribingFinalAudio => "Catching up…",
            Shruti.Core.DictationSessionState.InsertingText => "Adding your words…",
            _ => "Ready to dictate"
        };
        _triggerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(listening
            ? (_isDark ? Windows.UI.Color.FromArgb(255, 234, 141, 70) : Windows.UI.Color.FromArgb(255, 222, 110, 30))
            : (_isDark ? Windows.UI.Color.FromArgb(255, 51, 34, 24) : Windows.UI.Color.FromArgb(255, 252, 233, 216)));
        _triggerButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(listening
            ? (_isDark ? Windows.UI.Color.FromArgb(255, 44, 19, 5) : Windows.UI.Color.FromArgb(255, 255, 255, 255))
            : (_isDark ? Windows.UI.Color.FromArgb(255, 234, 141, 70) : Windows.UI.Color.FromArgb(255, 188, 86, 16)));
        foreach (Border bar in _waveformBars)
        {
            bar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(listening
                ? (_isDark ? Windows.UI.Color.FromArgb(255, 234, 141, 70) : Windows.UI.Color.FromArgb(255, 222, 110, 30))
                : (_isDark ? Windows.UI.Color.FromArgb(255, 68, 64, 58) : Windows.UI.Color.FromArgb(255, 212, 208, 201)));
            bar.Opacity = listening ? 1 : 0;
        }
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
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
    }

    private void ResizeForCurrentDpi(string? modelName)
    {
        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(windowHandle);
        double scale = dpi == 0 ? 1 : dpi / DefaultDpi;
        int subtitleCharacterCount = (modelName?.Length ?? "Local model".Length) + " · on this PC".Length;
        double subtitleWidthDip = Math.Max(132, subtitleCharacterCount * 7.2);
        double preferredWidthDip = Math.Max(MinimumWindowWidthDip, 192 + subtitleWidthDip);
        AppWindow.Resize(new SizeInt32(
            checked((int)Math.Round(preferredWidthDip * scale)),
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

    private static void ApplyRoundedWindowCorners(IntPtr windowHandle)
    {
        int preference = DwmRoundPreference;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowCornerPreference,
            ref preference,
            Marshal.SizeOf<int>());

        int borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());

        if (!GetWindowRect(windowHandle, out WindowRect bounds))
        {
            return;
        }

        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, height, height);
        if (region != IntPtr.Zero && SetWindowRgn(windowHandle, region, true) == 0)
        {
            _ = DeleteObject(region);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect bounds);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
