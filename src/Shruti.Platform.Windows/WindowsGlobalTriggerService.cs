using System.Threading.Channels;
using Shruti.Core.Triggers;

namespace Shruti.Platform.Windows;

public sealed class WindowsGlobalTriggerService : IGlobalTriggerService, IDisposable
{
    public const uint HotkeyWindowMessage = 0x0312;

    public const int GlobalHotkeyId = 0x5348;
    public const int FloatingWindowHotkeyId = 0x5349;

    private readonly IWindowsHotkeyRegistration _hotkeyRegistration;
    private readonly IWindowsPushToTalkHook _pushToTalkHook;
    private readonly Channel<DictationTriggerEvent> _events = Channel.CreateUnbounded<DictationTriggerEvent>();

    private IntPtr _windowHandle;
    private bool _globalHotkeyRegistered;
    private bool _floatingWindowHotkeyRegistered;
    private bool _isDisposed;

    public WindowsGlobalTriggerService(
        IWindowsHotkeyRegistration? hotkeyRegistration = null,
        IWindowsPushToTalkHook? pushToTalkHook = null)
    {
        _hotkeyRegistration = hotkeyRegistration ?? new Win32HotkeyRegistration();
        _pushToTalkHook = pushToTalkHook ?? new WindowsPushToTalkHook();
        _pushToTalkHook.KeyStateChanged += PushToTalkHook_KeyStateChanged;
    }

    public TriggerConfiguration Configuration { get; private set; } = new(
        EnableGlobalHotkey: false,
        EnablePushToTalk: true,
        EnableFloatingButton: true,
        EnableTrayMenu: true,
        HotkeyGesture: "Ctrl+Win+Space",
        PushToTalkKey: "Ctrl+Win+Space",
        EnableFloatingWindowShortcut: true,
        FloatingWindowShortcut: "Ctrl+Alt+M");

    public IAsyncEnumerable<DictationTriggerEvent> Events => _events.Reader.ReadAllAsync();

    public void AttachWindow(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required for global hotkeys.", nameof(windowHandle));
        }

        if (_windowHandle == windowHandle)
        {
            return;
        }

        UnregisterHotkeys();
        _windowHandle = windowHandle;
        ApplyConfiguration(Configuration);
    }

    public Task ConfigureAsync(
        TriggerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        ValidateConfiguration(configuration);
        TriggerConfiguration previousConfiguration = Configuration;
        try
        {
            UnregisterHotkeys();
            Configuration = configuration;
            ApplyConfiguration(configuration);
        }
        catch
        {
            UnregisterHotkeys();
            Configuration = previousConfiguration;
            ApplyConfiguration(previousConfiguration);
            throw;
        }

        return Task.CompletedTask;
    }

    public bool HandleWindowMessage(uint message, IntPtr windowParameter)
    {
        if (message != HotkeyWindowMessage)
        {
            return false;
        }

        int hotkeyId = checked((int)windowParameter.ToInt64());
        if (hotkeyId == GlobalHotkeyId)
        {
            if (Configuration.EnableGlobalHotkey)
            {
                Publish(DictationTriggerKind.GlobalHotkey, "windows-global-hotkey");
            }

            return true;
        }

        if (hotkeyId == FloatingWindowHotkeyId)
        {
            if (Configuration.EnableFloatingWindowShortcut)
            {
                Publish(DictationTriggerKind.FloatingWindowToggle, "windows-floating-window-shortcut");
            }

            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pushToTalkHook.KeyStateChanged -= PushToTalkHook_KeyStateChanged;
        UnregisterHotkeys();
        _pushToTalkHook.Dispose();
        _events.Writer.TryComplete();
        GC.SuppressFinalize(this);
    }

    private void ApplyConfiguration(TriggerConfiguration configuration)
    {
        try
        {
            if (configuration.EnableGlobalHotkey && _windowHandle != IntPtr.Zero)
            {
                if (!WindowsHotkeyParser.TryParse(configuration.HotkeyGesture, out WindowsHotkey? hotkey, out string? error))
                {
                    throw new InvalidOperationException(error);
                }

                RegisterHotkey(GlobalHotkeyId, hotkey!, "global dictation trigger");
            }

            if (configuration.EnableFloatingWindowShortcut && _windowHandle != IntPtr.Zero)
            {
                if (!WindowsHotkeyParser.TryParse(configuration.FloatingWindowShortcut, out WindowsHotkey? hotkey, out string? error))
                {
                    throw new InvalidOperationException(error);
                }

                RegisterHotkey(FloatingWindowHotkeyId, hotkey!, "floating window shortcut");
            }

            if (configuration.EnablePushToTalk)
            {
                if (!TryParsePushToTalkHotkey(configuration.PushToTalkKey, out WindowsHotkey? hotkey, out string? error))
                {
                    throw new InvalidOperationException(error);
                }

                _pushToTalkHook.Configure(enabled: true, hotkey!);
            }
            else
            {
                _pushToTalkHook.Configure(enabled: false, hotkey: null);
            }
        }
        catch
        {
            UnregisterHotkeys();
            throw;
        }
    }

    private static void ValidateConfiguration(TriggerConfiguration configuration)
    {
        WindowsHotkey? globalHotkey = null;
        WindowsHotkey? floatingWindowHotkey = null;
        WindowsHotkey? pushToTalkHotkey = null;

        if (configuration.EnableGlobalHotkey &&
            !WindowsHotkeyParser.TryParse(configuration.HotkeyGesture, out globalHotkey, out string? hotkeyError))
        {
            throw new ArgumentException(hotkeyError, nameof(configuration));
        }

        if (configuration.EnableFloatingWindowShortcut &&
            !WindowsHotkeyParser.TryParse(configuration.FloatingWindowShortcut, out floatingWindowHotkey, out string? floatingWindowError))
        {
            throw new ArgumentException(floatingWindowError, nameof(configuration));
        }

        if (configuration.EnablePushToTalk &&
            !TryParsePushToTalkHotkey(configuration.PushToTalkKey, out pushToTalkHotkey, out string? pushToTalkError))
        {
            throw new ArgumentException(pushToTalkError, nameof(configuration));
        }

        if (HotkeysMatch(globalHotkey, floatingWindowHotkey) ||
            HotkeysMatch(globalHotkey, pushToTalkHotkey) ||
            HotkeysMatch(floatingWindowHotkey, pushToTalkHotkey))
        {
            throw new ArgumentException(
                "Each enabled shortcut must use a different key combination.",
                nameof(configuration));
        }
    }

    private static bool HotkeysMatch(WindowsHotkey? first, WindowsHotkey? second)
    {
        return first is not null && second is not null &&
            first.Modifiers == second.Modifiers &&
            first.VirtualKey == second.VirtualKey;
    }

    private static bool TryParsePushToTalkHotkey(
        string? gesture,
        out WindowsHotkey? hotkey,
        out string? error)
    {
        if (WindowsHotkeyParser.TryParse(gesture, out hotkey, out error))
        {
            return true;
        }

        if (WindowsVirtualKey.TryParse(gesture, out uint virtualKey, out string? canonicalKey))
        {
            hotkey = new WindowsHotkey(0, virtualKey, canonicalKey!);
            error = null;
            return true;
        }

        error = "The hold-to-dictate key or key combination is not supported.";
        return false;
    }

    private void PushToTalkHook_KeyStateChanged(object? sender, WindowsPushToTalkKeyStateChangedEventArgs e)
    {
        if (!Configuration.EnablePushToTalk)
        {
            return;
        }

        Publish(
            e.IsPressed ? DictationTriggerKind.PushToTalkPressed : DictationTriggerKind.PushToTalkReleased,
            "windows-push-to-talk");
    }

    private void Publish(DictationTriggerKind kind, string sourceId)
    {
        _events.Writer.TryWrite(new DictationTriggerEvent(kind, DateTimeOffset.UtcNow, sourceId));
    }

    private void RegisterHotkey(int hotkeyId, WindowsHotkey hotkey, string description)
    {
        if (!_hotkeyRegistration.Register(_windowHandle, hotkeyId, hotkey))
        {
            throw new InvalidOperationException($"The {description} {hotkey.Gesture} is already in use by another application.");
        }

        if (hotkeyId == GlobalHotkeyId)
        {
            _globalHotkeyRegistered = true;
        }
        else if (hotkeyId == FloatingWindowHotkeyId)
        {
            _floatingWindowHotkeyRegistered = true;
        }
    }

    private void UnregisterHotkeys()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (_globalHotkeyRegistered)
        {
            _hotkeyRegistration.Unregister(_windowHandle, GlobalHotkeyId);
            _globalHotkeyRegistered = false;
        }

        if (_floatingWindowHotkeyRegistered)
        {
            _hotkeyRegistration.Unregister(_windowHandle, FloatingWindowHotkeyId);
            _floatingWindowHotkeyRegistered = false;
        }
    }
}
