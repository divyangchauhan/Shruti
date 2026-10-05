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
    private readonly StackPanel _content = new() { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
    private double _width = 40;
    private double _height = 8;
    private int _tick;
    private int _regionWidth;
    private int _regionHeight;

    public FloatingMicWindow(IWindowsWindowVisibility visibility)
    {
        _visibility = visibility;
        Title = "Shruti dictation";
        _root = new Border
        {
            // Paint the entire client area. The native window region supplies
            // the capsule shape without exposing the white window behind XAML corners.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            BorderThickness = new Thickness(0),
            Child = _content,
            RequestedTheme = ElementTheme.Dark
        };
        Content = _root;
        var menu = new MenuFlyout();
        var settings = new MenuFlyoutItem { Text = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var hide = new MenuFlyoutItem { Text = "Hide floating bar" };
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
        SelectMonitor();
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
        AppWindow.Closing += (_, args) => { if (!_closing) { args.Cancel = true; DismissRequested?.Invoke(this, EventArgs.Empty); } };
        Closed += (_, _) =>
        {
            _timer.Stop();
            Native.RemoveWindowSubclass(_handle, _procedure, (UIntPtr)0x5351);
        };
    }

    public event EventHandler? TriggerRequested;
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
            SelectMonitor();
            _noticeUntil = default;
        }
        if (!state.IsRunning && _state.IsRunning && state.LastOutcome == DictationRunOutcome.NoSpeech)
            _noticeUntil = DateTimeOffset.UtcNow.AddSeconds(2);
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
            ? (_state.CanStop ? "recording" : "processing")
            : DateTimeOffset.UtcNow < _noticeUntil ? "notice"
            : _state.LastOutcome == DictationRunOutcome.Failed ? "error"
            : _hovered ? "hover" : "idle";
        ToolTipService.SetToolTip(_root, mode == "idle" || mode == "hover" ? $"Dictate � {_shortcut}" : _state.UserMessage);
        if (_mode == mode) return;
        _mode = mode;
        _content.Children.Clear();
        _bars.Clear();
        _dots.Clear();
        (_width, _height) = mode switch
        {
            "idle" => (40, 8),
            "hover" => (76, 28),
            "recording" => (112, 28),
            "processing" => (48, 24),
            "notice" => (112, 24),
            _ => (76, 28)
        };
        _root.Width = _width;
        _root.Height = _height;
        if (mode == "hover")
        {
            _content.Children.Add(ActionButton("\uE720", "Start dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty)));
            _content.Children.Add(ActionButton("\uE713", "Open settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        }
        else if (mode == "recording")
        {
            _content.Children.Add(ActionButton("\uE711", "Cancel dictation", () => CancelRequested?.Invoke(this, EventArgs.Empty)));
            var waveform = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 7; i++)
            {
                var bar = new Border { Width = 2, Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
                _bars.Add(bar);
                waveform.Children.Add(bar);
            }
            _content.Children.Add(waveform);
            _content.Children.Add(ActionButton("\uE73E", "Finish dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty)));
        }
        else if (mode == "processing")
        {
            for (int i = 0; i < 3; i++)
            {
                var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
                _dots.Add(dot);
                _content.Children.Add(dot);
            }
        }
        else if (mode == "notice")
            _content.Children.Add(new TextBlock { Text = "No speech detected", FontSize = 10, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
        else if (mode == "error")
        {
            _content.Children.Add(ActionButton("\uE72C", "Try dictation again", () => TriggerRequested?.Invoke(this, EventArgs.Empty)));
            _content.Children.Add(ActionButton("\uE713", "Open settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        }
        AutomationProperties.SetName(_root, mode switch
        {
            "recording" => "Recording",
            "processing" => "Processing speech",
            "notice" => "No speech detected",
            "error" => _state.UserMessage,
            _ => $"Dictate, {_shortcut}"
        });
        Place();
    }

    private static Button ActionButton(string glyph, string name, Action action)
    {
        var button = new Button
        {
            Width = 22,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Content = new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 11 }
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private void SelectMonitor() => _monitor = Native.MonitorFromWindow(Native.GetForegroundWindow(), 2);

    private void Place()
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(_monitor, ref info))
        {
            SelectMonitor();
            if (!Native.GetMonitorInfo(_monitor, ref info)) return;
        }
        Native.GetDpiForMonitor(_monitor, 0, out uint dpi, out _);
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
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
        [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeleteObject(IntPtr value);
    }
}
