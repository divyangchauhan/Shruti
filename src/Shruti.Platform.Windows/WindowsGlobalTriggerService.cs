using System.Threading.Channels;
using Shruti.Core.Triggers;

namespace Shruti.Platform.Windows;

public sealed class WindowsGlobalTriggerService : IGlobalTriggerService, IDisposable
{
    public const uint HotkeyWindowMessage = 0x0312;

    private const int GlobalHotkeyId = 0x5348;

    private readonly IWindowsHotkeyRegistration _hotkeyRegistration;
    private readonly IWindowsPushToTalkHook _pushToTalkHook;
    private readonly Channel<DictationTriggerEvent> _events = Channel.CreateUnbounded<DictationTriggerEvent>();

    private IntPtr _windowHandle;
    private bool _hotkeyRegistered;
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
        PushToTalkKey: "Ctrl+Win+Space");

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

        UnregisterHotkey();
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
            UnregisterHotkey();
            Configuration = configuration;
            ApplyConfiguration(configuration);
        }
        catch
        {
            UnregisterHotkey();
            Configuration = previousConfiguration;
            ApplyConfiguration(previousConfiguration);
            throw;
        }

        return Task.CompletedTask;
    }

    public bool HandleWindowMessage(uint message, IntPtr windowParameter)
    {
        if (message != HotkeyWindowMessage || windowParameter.ToInt64() != GlobalHotkeyId)
        {
            return false;
        }

        if (!Configuration.EnableGlobalHotkey)
        {
            return true;
        }

        Publish(DictationTriggerKind.GlobalHotkey, "windows-global-hotkey");
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pushToTalkHook.KeyStateChanged -= PushToTalkHook_KeyStateChanged;
        UnregisterHotkey();
        _pushToTalkHook.Dispose();
        _events.Writer.TryComplete();
        GC.SuppressFinalize(this);
    }

    private void ApplyConfiguration(TriggerConfiguration configuration)
    {
        if (configuration.EnableGlobalHotkey && _windowHandle != IntPtr.Zero)
        {
            if (!WindowsHotkeyParser.TryParse(configuration.HotkeyGesture, out WindowsHotkey? hotkey, out string? error))
            {
                throw new InvalidOperationException(error);
            }

            if (!_hotkeyRegistration.Register(_windowHandle, GlobalHotkeyId, hotkey!))
            {
                throw new InvalidOperationException(
                    $"The global hotkey {hotkey!.Gesture} is already in use by another application.");
            }

            _hotkeyRegistered = true;
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

    private static void ValidateConfiguration(TriggerConfiguration configuration)
    {
        if (configuration.EnableGlobalHotkey &&
            !WindowsHotkeyParser.TryParse(configuration.HotkeyGesture, out _, out string? hotkeyError))
        {
            throw new ArgumentException(hotkeyError, nameof(configuration));
        }

        if (configuration.EnablePushToTalk &&
            !TryParsePushToTalkHotkey(configuration.PushToTalkKey, out _, out string? pushToTalkError))
        {
            throw new ArgumentException(pushToTalkError, nameof(configuration));
        }
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

    private void UnregisterHotkey()
    {
        if (!_hotkeyRegistered || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        _hotkeyRegistration.Unregister(_windowHandle, GlobalHotkeyId);
        _hotkeyRegistered = false;
    }
}
