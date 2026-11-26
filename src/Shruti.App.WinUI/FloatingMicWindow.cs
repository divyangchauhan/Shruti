using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Platform.Windows;
using Shruti.Workflow.Dictation;
using Windows.System;
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
    private MenuFlyout _actionsMenu;
    private Style? _stripMenuStyle;
    private bool _horizontalMenu;
    private const int MenuHotKeyBase = 0x5300;
    private readonly List<VirtualKey> _reservedMenuKeys = [];
    private readonly List<(MenuFlyoutItem Item, Action Invoke)> _menuCommands = [];
    private int _keyboardMenuIndex = -1;
    private IntPtr _menuForegroundWindow;
    private WindowsForegroundWindowTracker? _menuForegroundTracker;
    private bool _menuItemInvoked;
    private string _hint = string.Empty;
    private string _mode = "";
    private string _shortcut = "";
    private string _renderedLabel = string.Empty;
    private const double ActiveWidth = 198;
    private double _width = 76;
    private double _height = 20;
    private int _tick;
    private int _regionWidth;
    private int _regionHeight;
    private FloatingPillBounds? _placedBounds;

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
        _actionsMenu = PopupAppearance.CreateMenuFlyout();
        ConfigureActionsMenu(_actionsMenu);
        _root.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler((_, args) =>
        {
            args.Handled = true;
            ShowActionsMenu();
        }), true);
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
                if (_actionsMenu.IsOpen)
                {
                    // Freeze the anchor while the pointer crosses into the popup window.
                    bool overMenuOrBar = Native.GetAncestor(hitWindow, 3) == _handle; // GA_ROOTOWNER
                    bool mousePressed = ReadMouseButtons();
                    if ((!overMenuOrBar && mousePressed) || Native.GetForegroundWindow() != _menuForegroundWindow)
                        _actionsMenu.Hide();
                }
                else if (_hovered != hovered)
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
        // AppWindow.Show can reset the native window order. Join the topmost
        // group once per show, then let fullscreen apps remain above the bar.
        Native.SetWindowPos(_handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0013);
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

    private static bool ReadMouseButtons()
    {
        // Read both buttons on every menu tick, including clicks shorter than the timer interval.
        int buttons = Native.GetAsyncKeyState(1) | Native.GetAsyncKeyState(2);
        return buttons != 0;
    }

    private void ConfigureActionsMenu(MenuFlyout menu)
    {
        if (_horizontalMenu)
        {
            if (_stripMenuStyle is null)
            {
                var resources = new ResourceDictionary { Source = new Uri("ms-appx:///Styles/PopupResources.xaml") };
                _stripMenuStyle = (Style)resources["FloatingMenuStripStyle"];
            }
            menu.MenuFlyoutPresenterStyle = _stripMenuStyle;
        }
        AddMenuCommand(menu, "Settings", new SymbolIcon(Symbol.Setting), () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        AddMenuCommand(menu, "Hide floating bar", new FontIcon { Glyph = "\uED1A" }, () => DismissRequested?.Invoke(this, EventArgs.Empty));
        menu.ShouldConstrainToRootBounds = false;
        menu.Placement = FlyoutPlacementMode.Top;
        menu.ShowMode = FlyoutShowMode.Transient;
        menu.Opening += (_, _) =>
        {
            // Keep keyboard focus in the editor while the menu handles its own navigation.
            _menuItemInvoked = false;
            SetMenuToolTips(visible: false);
            _menuForegroundWindow = Native.GetForegroundWindow();
            _menuForegroundTracker = new WindowsForegroundWindowTracker();
            _menuForegroundTracker.ForegroundWindowChanged += MenuForegroundChanged;
            _menuForegroundTracker.Start();
            ReadMouseButtons();
            if (!ReserveMenuKey(VirtualKey.Enter) || !ReserveMenuKey(VirtualKey.Up) || !ReserveMenuKey(VirtualKey.Down))
                ReleaseMenuKeys();
            else if (_horizontalMenu)
            {
                ReserveMenuKey(VirtualKey.Left);
                ReserveMenuKey(VirtualKey.Right);
            }
            ReserveMenuKey(VirtualKey.Escape);
        };
        menu.Closing += (_, _) => StopMenuInput();
        menu.Closed += (_, _) =>
        {
            if (menu != _actionsMenu) return;
            SetKeyboardMenuItem(-1);
            SetMenuToolTips(visible: true);
        };
    }

    private void ShowActionsMenu()
    {
        if (_actionsMenu.IsOpen) return;
        var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top, ShowMode = FlyoutShowMode.Transient };
        bool horizontalMenu = false;
        var input = new Native.GuiThreadInfo { Size = Marshal.SizeOf<Native.GuiThreadInfo>() };
        if (_placedBounds is { } bar && Native.GetGUIThreadInfo(0, ref input) && input.Caret != IntPtr.Zero &&
            Native.GetWindowRect(input.Caret, out var field))
        {
            // Keep the focused text field readable when opening the menu over an editor.
            double scale = _root.XamlRoot.RasterizationScale;
            options.ExclusionRect = new Windows.Foundation.Rect(
                (field.Left - bar.X) / scale, (field.Top - bar.Y) / scale,
                (field.Right - field.Left) / scale, (field.Bottom - field.Top) / scale);
            var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            double menuWidth = 200 * scale;
            bool nearMenu = field.Right > bar.X - menuWidth / 2 && field.Left < bar.X + bar.Width + menuWidth / 2
                && field.Bottom > bar.Y - 110 * scale && field.Top < bar.Y + bar.Height;
            if (nearMenu && Native.GetMonitorInfo(_monitor, ref monitor))
            {
                bool fitsRight = monitor.Work.Right - field.Right >= menuWidth;
                bool fitsLeft = field.Left - monitor.Work.Left >= menuWidth;
                if (fitsRight || fitsLeft)
                {
                    double edge = fitsRight ? (field.Right - bar.X) / scale + 6 : (field.Left - bar.X) / scale - 6;
                    options.Position = new Windows.Foundation.Point(edge, 0);
                    options.Placement = fitsRight ? FlyoutPlacementMode.RightEdgeAlignedBottom : FlyoutPlacementMode.LeftEdgeAlignedBottom;
                }
                else if (field.Top >= bar.Y - 110 * scale && field.Top - monitor.Work.Top >= 110 * scale)
                {
                    // A wide, shallow field can leave room just above it when neither side fits.
                    options.Position = new Windows.Foundation.Point(bar.Width / (2 * scale), (field.Top - bar.Y) / scale - 6);
                    options.Placement = FlyoutPlacementMode.Top;
                }
                else if (monitor.Work.Bottom - field.Bottom >= 44 * scale)
                {
                    // Keep both commands in the strip below a tall field, beside the bottom-center bar.
                    horizontalMenu = true;
                    options.ExclusionRect = new Windows.Foundation.Rect(0, (monitor.Work.Top - bar.Y) / scale,
                        (monitor.Work.Right - bar.X) / scale, (monitor.Work.Bottom - monitor.Work.Top) / scale);
                    options.Position = new Windows.Foundation.Point(-6, (monitor.Work.Bottom - bar.Y) / scale - 3);
                    options.Placement = FlyoutPlacementMode.LeftEdgeAlignedBottom;
                }
                else
                {
                    // When the editor fills the work area, keep the menu close and protect the insertion point.
                    var start = new Native.Point { X = input.CaretBounds.Left, Y = input.CaretBounds.Top };
                    var end = new Native.Point { X = input.CaretBounds.Right, Y = input.CaretBounds.Bottom };
                    if (Native.ClientToScreen(input.Caret, ref start) && Native.ClientToScreen(input.Caret, ref end))
                    {
                        int left = Math.Min(start.X, end.X), top = Math.Min(start.Y, end.Y);
                        const double caretClearance = 16; // Include the popup shadow around the insertion point.
                        options.ExclusionRect = new Windows.Foundation.Rect((left - bar.X) / scale - caretClearance, (top - bar.Y) / scale - caretClearance,
                            Math.Abs(end.X - start.X) / scale + 2 * caretClearance, Math.Abs(end.Y - start.Y) / scale + 2 * caretClearance);
                        bool menuOnRight = (start.X + (long)end.X) / 2 < bar.X + bar.Width / 2;
                        options.Position = new Windows.Foundation.Point(menuOnRight ? bar.Width / scale + 6 : -6, 0);
                        options.Placement = menuOnRight ? FlyoutPlacementMode.RightEdgeAlignedBottom : FlyoutPlacementMode.LeftEdgeAlignedBottom;
                    }
                }
            }
        }
        // WinUI keeps the presenter's original ItemsPanel after the menu is closed.
        if (horizontalMenu != _horizontalMenu)
        {
            SetKeyboardMenuItem(-1);
            _menuCommands.Clear();
            _horizontalMenu = horizontalMenu;
            _actionsMenu = PopupAppearance.CreateMenuFlyout();
            ConfigureActionsMenu(_actionsMenu);
        }
        _actionsMenu.ShowAt(_root, options);
    }

    private void AddMenuCommand(MenuFlyout menu, string label, IconElement icon, Action action)
    {
        var item = new MenuFlyoutItem { Text = label, Icon = icon };
        if (_horizontalMenu) item.Padding = new Thickness(8, 2, 8, 2);
        uint? pressedPointer = null;
        void Invoke()
        {
            if (_menuItemInvoked || menu != _actionsMenu || !menu.IsOpen) return;
            _menuItemInvoked = true;
            menu.Hide();
            action();
        }

        item.Click += (_, _) => Invoke();
        // WinUI can omit Click on a non-activating popup even after the item receives both pointer events.
        item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            pressedPointer = args.GetCurrentPoint(item).Properties.IsLeftButtonPressed ? args.Pointer.PointerId : null;
        }), true);
        item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) =>
        {
            if (pressedPointer != args.Pointer.PointerId) return;
            pressedPointer = null;
            var point = args.GetCurrentPoint(item).Position;
            if (point.X >= 0 && point.X < item.ActualWidth && point.Y >= 0 && point.Y < item.ActualHeight) Invoke();
        }), true);
        item.PointerCanceled += (_, _) => pressedPointer = null;
        menu.Closed += (_, _) => pressedPointer = null;
        menu.Items.Add(item);
        _menuCommands.Add((item, Invoke));
    }

    private void SetMenuToolTips(bool visible)
    {
        // A tooltip uses its own HWND, which can cover a flyout row on this non-activating window.
        SetHint(_root, visible ? _hint : null);
        foreach (var button in _content.Children.OfType<Button>())
        {
            SetHint(button, visible ? AutomationProperties.GetName(button) : null);
        }
    }

    private static void SetHint(FrameworkElement element, string? hint)
    {
        if (ToolTipService.GetToolTip(element) is ToolTip previous)
        {
            if (hint is not null && Equals(previous.Content, hint)) return;
            previous.IsOpen = false;
        }
        ToolTipService.SetToolTip(element, hint is null ? null : new ToolTip { Content = hint });
    }

    private bool ReserveMenuKey(VirtualKey key)
    {
        if (!Native.RegisterHotKey(_handle, MenuHotKeyBase + (int)key, 0x4000, (uint)key)) return false;
        _reservedMenuKeys.Add(key);
        return true;
    }

    private void ReleaseMenuKeys()
    {
        foreach (var key in _reservedMenuKeys)
            Native.UnregisterHotKey(_handle, MenuHotKeyBase + (int)key);
        _reservedMenuKeys.Clear();
    }

    private void MenuForegroundChanged(object? sender, IntPtr foreground)
    {
        if (foreground != _menuForegroundWindow && Native.GetForegroundWindow() == foreground && _actionsMenu.IsOpen)
            _actionsMenu.Hide();
    }

    private void StopMenuInput()
    {
        ReleaseMenuKeys();
        _menuForegroundTracker?.Dispose();
        _menuForegroundTracker = null;
    }

    private void SetKeyboardMenuItem(int index)
    {
        if (_keyboardMenuIndex >= 0)
        {
            var previous = _menuCommands[_keyboardMenuIndex].Item;
            previous.ClearValue(Control.BackgroundProperty);
            previous.ClearValue(Control.FontWeightProperty);
            AutomationProperties.SetItemStatus(previous, string.Empty);
        }
        _keyboardMenuIndex = index;
        if (index < 0) return;
        var selected = _menuCommands[index].Item;
        selected.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 64, 35));
        if (!_horizontalMenu) selected.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        AutomationProperties.SetItemStatus(selected, "Selected");
    }

    private void HandleMenuKey(VirtualKey key)
    {
        if (!_actionsMenu.IsOpen || Native.GetForegroundWindow() != _menuForegroundWindow || key == VirtualKey.Escape)
        {
            _actionsMenu.Hide();
            return;
        }
        if (key == VirtualKey.Enter)
        {
            if (_keyboardMenuIndex >= 0) _menuCommands[_keyboardMenuIndex].Invoke();
            return;
        }
        int count = _menuCommands.Count;
        int next = key is VirtualKey.Down or VirtualKey.Right ? (_keyboardMenuIndex + 1) % count
            : _keyboardMenuIndex <= 0 ? count - 1 : _keyboardMenuIndex - 1;
        SetKeyboardMenuItem(next);
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
        _actionsMenu.Hide();
        StopMenuInput();
        _visibility.Hide(_handle);
        IsVisible = false;
        _hovered = false;
    }

    public void CloseForApplicationExit()
    {
        _closing = true;
        _actionsMenu.Hide();
        StopMenuInput();
        _timer.Stop();
        Close();
    }

    private void Render()
    {
        bool starting = _state.SessionState is DictationSessionState.PreparingTarget or DictationSessionState.RequestingMicrophone;
        string mode = _state.IsRunning
            ? (_state.IsPaused ? "paused" : starting ? "starting" : _state.CanStop ? "capture" : "processing")
            : _state.LastOutcome == DictationRunOutcome.Failed ? "error"
            : DateTimeOffset.UtcNow < _noticeUntil ? "notice"
            : _hovered ? "hover" : "idle";
        string label = mode switch
        {
            "paused" => "Paused",
            "starting" => "Starting",
            "capture" => "Recording",
            "processing" => "Processing",
            "notice" => NoticeText(_state.LastOutcome) ?? string.Empty,
            "error" => _state.FailedOperation switch
            {
                DictationSessionState.RequestingMicrophone => "Mic unavailable",
                DictationSessionState.Recording => "Recording failed",
                DictationSessionState.Complete when _state.InsertionMode == DictationInsertionMode.CopyOnly => "Copy failed",
                DictationSessionState.TranscribingFinalAudio => "No transcript",
                DictationSessionState.RefiningTranscript => "Format failed",
                DictationSessionState.InsertingText => "Insertion failed",
                _ => "Dictation failed"
            },
            _ => "Dictate"
        };
        string hint = mode is "idle" or "hover"
            ? string.IsNullOrWhiteSpace(_shortcut) ? "Start dictation" : $"Dictate with {_shortcut}"
            : mode == "error" && _state.MicrophoneUnavailable ? "Open Mic settings to choose an input or check Windows microphone access. Then try dictation again."
            : mode == "error" ? "Record again to start a new dictation. Right-click for Settings and expand Diagnostics for details."
            : _state.UserMessage;
        _hint = hint;
        if (!_actionsMenu.IsOpen) SetHint(_root, hint);
        AutomationProperties.SetName(_root, mode == "error" ? $"{label}. {hint}" : mode is "idle" or "hover" ? hint : label);
        if (_mode == mode && _renderedLabel == label) return;
        _mode = mode;
        _renderedLabel = label;
        _content.Children.Clear();
        _bars.Clear();
        _dots.Clear();
        _height = mode == "idle" ? 20 : 32;

        switch (mode)
        {
            case "idle":
                _content.Children.Add(StateIcon("\uE720"));
                _content.Children.Add(StateLabel("Dictate"));
                break;
            case "hover":
                _content.Children.Add(ActionButton("\uE720", "Start dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty), "Dictate"));
                _content.Children.Add(ActionButton("\uE713", "Open settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
                break;
            case "starting":
            case "capture":
                _content.Children.Add(ActionButton(null, "Cancel dictation", () => CancelRequested?.Invoke(this, EventArgs.Empty), "Cancel"));
                if (label == "Recording")
                {
                    var waveform = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 18, VerticalAlignment = VerticalAlignment.Center };
                    var waveBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 165, 97));
                    for (int i = 0; i < 4; i++)
                    {
                        var bar = new Border { Width = 2, Height = 2, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Center, Background = waveBrush };
                        _bars.Add(bar);
                        waveform.Children.Add(bar);
                    }
                    _content.Children.Add(waveform);
                }
                else
                {
                    _content.Children.Add(StateIcon("\uE720"));
                }
                _content.Children.Add(StateLabel(label));
                _content.Children.Add(ActionButton(null, "Finish dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty), "Finish", primary: true));
                break;
            case "paused":
                _content.Children.Add(ActionButton(null, "Cancel dictation", () => CancelRequested?.Invoke(this, EventArgs.Empty), "Cancel"));
                _content.Children.Add(StateIcon("\uE769"));
                _content.Children.Add(ActionButton(null, "Resume recording", () => ResumeRequested?.Invoke(this, EventArgs.Empty), "Resume", primary: true));
                _content.Children.Add(ActionButton(null, "Finish dictation", () => TriggerRequested?.Invoke(this, EventArgs.Empty), "Finish"));
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
                if (_state.MicrophoneUnavailable)
                {
                    _content.Children.Add(ActionButton(null, "Open microphone settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty), "Mic settings", primary: true));
                }
                else
                {
                    _content.Children.Add(ActionButton(null, "Record again", () => TriggerRequested?.Invoke(this, EventArgs.Empty), "Record again", primary: true));
                }
                break;
        }
        if (_actionsMenu.IsOpen) SetMenuToolTips(visible: false);
        _content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double contentWidth = Math.Ceiling(_content.DesiredSize.Width) + (mode is "starting" or "capture" or "paused" or "error" ? 14 : 24);
        // Keep the same footprint from recording through insertion. Larger text can still expand it.
        _width = mode switch
        {
            "idle" => Math.Max(76, contentWidth),
            "hover" => contentWidth,
            _ => Math.Max(ActiveWidth, contentWidth)
        };
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

    private static Button ActionButton(string? glyph, string name, Action action, string? label = null, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null) content.Children.Add(new FontIcon { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13 });
        if (label is not null) content.Children.Add(StateLabel(label));
        var button = new Button
        {
            Width = label is null ? 28 : double.NaN,
            Height = 28,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(label is null ? 0 : 7, 0, label is null ? 0 : 7, 0),
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(primary ? Windows.UI.Color.FromArgb(255, 106, 64, 35) : Windows.UI.Color.FromArgb(255, 58, 52, 47)),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Content = content
        };
        AutomationProperties.SetName(button, name);
        SetHint(button, name);
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
        if (_placedBounds == bounds) return;
        // SWP_NOACTIVATE | SWP_NOZORDER: resize without disturbing other apps.
        if (!Native.SetWindowPos(_handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0014)) return;
        _placedBounds = bounds;
        _root.UpdateLayout();
        if (_regionWidth == bounds.Width && _regionHeight == bounds.Height) return;
        _regionWidth = bounds.Width;
        _regionHeight = bounds.Height;
        IntPtr region = Native.CreateRoundRectRgn(0, 0, bounds.Width + 1, bounds.Height + 1, bounds.Height, bounds.Height);
        if (Native.SetWindowRgn(_handle, region, true) == 0) Native.DeleteObject(region);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data)
    {
        if (message == 0x0312) // WM_HOTKEY
        {
            var key = (VirtualKey)(wParam.ToInt32() - MenuHotKeyBase);
            if (_reservedMenuKeys.Contains(key))
            {
                HandleMenuKey(key);
                return IntPtr.Zero;
            }
        }
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
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [StructLayout(LayoutKind.Sequential)]
        public struct GuiThreadInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretBounds;
        }
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
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
