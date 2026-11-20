using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Platform.Windows;
using Shruti.Workflow.Dictation;
using WinRT.Interop;

namespace Shruti.App.WinUI;

public sealed class FloatingMicWindow : Window
{
    private readonly Border _root;
    private readonly StackPanel _content = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Border> _bars = [];
    private readonly List<Border> _dots = [];
    private readonly IWindowsWindowVisibility _visibility;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Native.SubclassProc _procedure;
    private readonly IntPtr _handle;
    private IntPtr _monitor;
    private DictationShellState _state = DictationShellState.Initial;
    private DateTimeOffset _noticeUntil;
    private bool _hovered;
    private bool _closing;
    private string _mode = "";
    private string _shortcut = "";
    private string _renderedLabel = string.Empty;
    private double _width = 40;
    private double _height = 8;
    private int _tick;
    private int _regionWidth;
    private int _regionHeight;

    public FloatingMicWindow(IWindowsWindowVisibility visibility, IntPtr monitor)
    {
        _visibility = visibility;
        _monitor = monitor;
        Title = "Shruti dictation";
        _root = new Border
        {
            // Paint the entire client area. The native window region supplies
            // the capsule shape without exposing the white window behind XAML corners.
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 30, 27)),
            BorderThickness = new Thickness(0),
            Child = _content,
            RequestedTheme = ElementTheme.Dark
        };
        Content = _root;
        var menu = PopupAppearance.CreateMenuFlyout();
        var settings = new MenuFlyoutItem { Text = "Settings", Icon = new SymbolIcon(Symbol.Setting) };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var hide = new MenuFlyoutItem { Text = "Hide floating bar", Icon = new FontIcon { Glyph = "\uED1A" } };
        hide.Click += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(settings);
        menu.Items.Add(hide);
        _root.ContextFlyout = menu;
        AutomationProperties.SetName(_root, "Shruti floating dictation bar");
        AutomationProperties.SetLiveSetting(_root, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _handle = WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        _visibility.MakeNonActivating(_handle);
        _procedure = WindowProcedure;
        if (!Native.SetWindowSubclass(_handle, _procedure, (UIntPtr)0x5351, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // OverlappedPresenter can retain WS_DLGFRAME after hiding its border.
        // Remove the native frame so the black client area reaches every edge.
        nint style = Native.GetWindowLongPtr(_handle, -16);
        Native.SetWindowLongPtr(_handle, -16, style & ~(nint)0x00CC0000);
        Native.SetWindowPos(_handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        int doNotRound = 1;
        Native.DwmSetWindowAttribute(_handle, 33, ref doNotRound, sizeof(int));
        int noBorder = -2;
        Native.DwmSetWindowAttribute(_handle, 34, ref noBorder, sizeof(int));
        _timer.Tick += (_, _) =>
        {
            // Resizing the non-activating capsule can invalidate XAML's pointer
            // boundary events. Use its native hit target so hover stays stable.
            if (Native.GetCursorPos(out Native.Point cursor))
            {
                IntPtr hitWindow = Native.WindowFromPoint(cursor);
                bool hovered = hitWindow == _handle || Native.GetAncestor(hitWindow, 2) == _handle;
                if (_hovered != hovered)
                {
                    _hovered = hovered;
                    Render();
                }
            }
            _tick++;
            for (int i = 0; i < _dots.Count; i++) _dots[i].Opacity = (_tick / 2) % 3 == i ? 1 : 0.3;
            if (_mode == "notice" && DateTimeOffset.UtcNow >= _noticeUntil) Render();
            if (_tick % 8 == 0) Place();
        };
        // The manager owns this window's lifetime. A system close request leaves the preference alone.
        AppWindow.Closing += (_, args) => args.Cancel = !_closing;
        Closed += (_, _) =>
        {
            _timer.Stop();
            Native.RemoveWindowSubclass(_handle, _procedure, (UIntPtr)0x5351);
        };
    }

    public event EventHandler? TriggerRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? DismissRequested;
    public event EventHandler? SettingsRequested;
    public bool IsVisible { get; private set; }

    public void Show(DictationShellState state, string shortcut)
    {
        _shortcut = shortcut;
        UpdateState(state);
        if (IsVisible) return;
        Place();
        AppWindow.Show(activateWindow: false);
        IsVisible = true;
        _timer.Start();
    }

    public void UpdateState(DictationShellState state)
    {
        if (state.IsRunning && !_state.IsRunning)
        {
            _noticeUntil = default;
        }
        if (!state.IsRunning && state.LastOutcome is not null &&
            (_state.IsRunning || _state.LastOutcome != state.LastOutcome) && NoticeText(state.LastOutcome) is not null)
        {
            _noticeUntil = DateTimeOffset.UtcNow.AddSeconds(2.4);
        }
        _state = state;
        Render();
    }

    public void UpdateAudioLevel(float peak)
    {
        for (int i = 0; i < _bars.Count; i++)
        {
            double envelope = 0.45 + 0.55 * Math.Sin((i + 1d) / (_bars.Count + 1) * Math.PI);
            _bars[i].Height = 2 + Math.Clamp(peak * 4, 0, 1) * 14 * envelope;
        }
    }

    public void Hide()
    {
        _timer.Stop();
        _visibility.Hide(_handle);
        IsVisible = false;
        _hovered = false;
    }

    public void CloseForApplicationExit()
    {
        _closing = true;
        _timer.Stop();
        Close();
    }

    private void Render()
    {
        string mode = _state.IsRunning
            ? (_state.CanStop ? "capture" : "processing")
            : _state.LastOutcome == DictationRunOutcome.Failed ? "error"
            : DateTimeOffset.UtcNow < _noticeUntil ? "notice"
            : _hovered ? "hover" : "idle";
        string label = mode switch
        {
            "capture" or "processing" when _state.IsPaused => "Paused",
            "capture" or "processing" when _state.SessionState is DictationSessionState.PreparingTarget or DictationSessionState.RequestingMicrophone => "Starting",
            "capture" => "Recording",
            "processing" => "Processing",
            "notice" => NoticeText(_state.LastOutcome) ?? string.Empty,
            "error" => "Dictation failed",
            _ => "Dictate"
        };
        string hint = mode is "idle" or "hover"
            ? string.IsNullOrWhiteSpace(_shortcut) ? "Start dictation" : $"Dictate with {_shortcut}"
            : _state.UserMessage;
        ToolTipService.SetToolTip(_root, hint);
        AutomationProperties.SetName(_root, mode == "error" ? _state.UserMessage : mode is "idle" or "hover" ? hint : label);
        if (_mode == mode && _renderedLabel == label) return;
        _mode = mode;
        _renderedLabel = label;
        _content.Children.Clear();
        _bars.Clear();
        _dots.Clear();
        _height = mode == "idle" ? 8 : 36;

        switch (mode)
        {
            case "hover":
                _content.Children.Add(ActionButton("\uE720", "Start dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty), "Dictate"));
                _content.Children.Add(ActionButton("\uE713", "Open settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
                break;
            case "capture":
                _content.Children.Add(ActionButton("\uE711", "Cancel dictation", () => CancelRequested?.Invoke(this, EventArgs.Empty)));
                if (label == "Recording")
                {
                    var waveform = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 18, VerticalAlignment = VerticalAlignment.Center };
                    var waveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 165, 97));
                    for (int i = 0; i < 7; i++)
                    {
                        var bar = new Border { Width = 2, Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Background = waveBrush };
                        _bars.Add(bar);
                        waveform.Children.Add(bar);
                    }
                    _content.Children.Add(waveform);
                }
                else
                {
                    _content.Children.Add(StateIcon(label == "Paused" ? "\uE769" : "\uE720"));
                }
                _content.Children.Add(StateLabel(label));
                if (_state.IsPaused)
                    _content.Children.Add(ActionButton("\uE768", "Resume recording", () => ResumeRequested?.Invoke(this, EventArgs.Empty), "Resume"));
                _content.Children.Add(ActionButton("\uE73E", "Finish dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty)));
                break;
            case "processing":
                var activity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                for (int i = 0; i < 3; i++)
                {
                    var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
                    _dots.Add(dot);
                    activity.Children.Add(dot);
                }
                _content.Children.Add(activity);
                _content.Children.Add(StateLabel(label));
                break;
            case "notice":
                string glyph = _state.LastOutcome switch
                {
                    DictationRunOutcome.CopyOnly => "\uE8C8",
                    DictationRunOutcome.PreviewRequired => "\uE8A5",
                    DictationRunOutcome.Inserted => "\uE73E",
                    _ => "\uE720"
                };
                _content.Children.Add(StateIcon(glyph));
                _content.Children.Add(StateLabel(label));
                break;
            case "error":
                _content.Children.Add(StateLabel(label));
                _content.Children.Add(ActionButton("\uE72C", "Try dictation again", () => TriggerRequested?.Invoke(this, EventArgs.Empty), "Retry"));
                _content.Children.Add(ActionButton("\uE713", "Open settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
                break;
        }
        _content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _width = mode == "idle" ? 40 : Math.Ceiling(_content.DesiredSize.Width) + 24;
        _root.Width = _width;
        _root.Height = _height;
        Place();
    }

    private static string? NoticeText(DictationRunOutcome? outcome) => outcome switch
    {
        DictationRunOutcome.Inserted => "Text inserted",
        DictationRunOutcome.CopyOnly => "Copied",
        DictationRunOutcome.PreviewRequired => "Review in Shruti",
        DictationRunOutcome.NoSpeech => "No speech detected",
        _ => null
    };

    private static FontIcon StateIcon(string glyph) => new()
    {
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        Glyph = glyph,
        FontSize = 15,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 165, 97))
    };

    private static TextBlock StateLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
    };

    private static Button ActionButton(string glyph, string name, Action action, string? label = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13 };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon);
        if (label is not null) content.Children.Add(StateLabel(label));
        var button = new Button
        {
            Width = label is null ? 28 : double.NaN,
            Height = 28,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(label is null ? 0 : 7, 0, label is null ? 0 : 7, 0),
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 52, 47)),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Content = content
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    public void SetMonitor(IntPtr monitor)
    {
        _monitor = monitor;
        Place();
    }

    private void Place()
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(_monitor, ref info))
        {
            // The owner will reconcile windows when the display configuration changes.
            return;
        }
        if (Native.GetDpiForMonitor(_monitor, 0, out uint dpi, out _) != 0 || dpi == 0) dpi = 96;
        FloatingPillBounds bounds = FloatingPillPlacement.BottomCenter(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, _width, _height, dpi);
        Native.SetWindowPos(_handle, (IntPtr)(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
        _root.UpdateLayout();
        if (_regionWidth == bounds.Width && _regionHeight == bounds.Height) return;
        _regionWidth = bounds.Width;
        _regionHeight = bounds.Height;
        IntPtr region = Native.CreateRoundRectRgn(0, 0, bounds.Width + 1, bounds.Height + 1, bounds.Height, bounds.Height);
        if (Native.SetWindowRgn(_handle, region, true) == 0) Native.DeleteObject(region);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
    {
        if (message == 0x0024) // WM_GETMINMAXINFO: a pill is smaller than a normal app window.
        {
            Native.DefSubclassProc(hwnd, message, wParam, lParam);
            var limits = Marshal.PtrToStructure<Native.MinMaxInfo>(lParam);
            limits.MinTrackSize = new Native.Point { X = 1, Y = 1 };
            Marshal.StructureToPtr(limits, lParam, false);
            return IntPtr.Zero;
        }
        if (message == 0x0021) return (IntPtr)3; // MA_NOACTIVATE: keep the editor's caret.
        if (message == 0x0084) return (IntPtr)1; // HTCLIENT: there is no draggable caption.
        if (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) is 0xF010 or 0xF000) return IntPtr.Zero;
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static class Native
    {
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        public delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);
        [DllImport("comctl32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, IntPtr data);
        [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);
        [DllImport("comctl32.dll")] public static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
        [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeleteObject(IntPtr value);
    }
}
