using Shruti.Core.Triggers;
using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsGlobalTriggerServiceTests
{
    [Fact]
    public async Task GlobalHotkey_RegistersAndPublishesEvent()
    {
        var registration = new FakeHotkeyRegistration();
        var pushToTalkHook = new FakePushToTalkHook();
        using var service = new WindowsGlobalTriggerService(registration, pushToTalkHook);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await service.ConfigureAsync(CreateConfiguration(), CancellationToken.None);
        service.AttachWindow((IntPtr)42);

        bool handled = service.HandleWindowMessage(
            WindowsGlobalTriggerService.HotkeyWindowMessage,
            (IntPtr)0x5348);
        DictationTriggerEvent trigger = await ReadFirstAsync(service.Events, cancellation.Token);

        Assert.True(handled);
        Assert.Equal((IntPtr)42, registration.WindowHandle);
        Assert.Equal(
            "Ctrl+Alt+Space",
            registration.GetHotkey(WindowsGlobalTriggerService.GlobalHotkeyId)?.Gesture);
        Assert.Equal(DictationTriggerKind.GlobalHotkey, trigger.Kind);
        Assert.Equal("windows-global-hotkey", trigger.SourceId);
    }

    [Fact]
    public async Task FloatingWindowShortcut_RegistersAndPublishesToggleEvent()
    {
        var registration = new FakeHotkeyRegistration();
        using var service = new WindowsGlobalTriggerService(registration, new FakePushToTalkHook());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await service.ConfigureAsync(CreateConfiguration(), CancellationToken.None);
        service.AttachWindow((IntPtr)42);

        bool handled = service.HandleWindowMessage(
            WindowsGlobalTriggerService.HotkeyWindowMessage,
            (IntPtr)WindowsGlobalTriggerService.FloatingWindowHotkeyId);
        DictationTriggerEvent trigger = await ReadFirstAsync(service.Events, cancellation.Token);

        Assert.True(handled);
        Assert.Equal(
            "Ctrl+Alt+M",
            registration.GetHotkey(WindowsGlobalTriggerService.FloatingWindowHotkeyId)?.Gesture);
        Assert.Equal(DictationTriggerKind.FloatingWindowToggle, trigger.Kind);
        Assert.Equal("windows-floating-window-shortcut", trigger.SourceId);
    }

    [Fact]
    public async Task PushToTalk_EmitsPressAndReleaseEvents()
    {
        var pushToTalkHook = new FakePushToTalkHook();
        using var service = new WindowsGlobalTriggerService(new FakeHotkeyRegistration(), pushToTalkHook);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await service.ConfigureAsync(CreateConfiguration(), CancellationToken.None);
        service.AttachWindow((IntPtr)42);
        pushToTalkHook.Raise(isPressed: true);
        pushToTalkHook.Raise(isPressed: false);

        DictationTriggerEvent first = await ReadFirstAsync(service.Events, cancellation.Token);
        DictationTriggerEvent second = await ReadFirstAsync(service.Events, cancellation.Token);

        Assert.Equal(DictationTriggerKind.PushToTalkPressed, first.Kind);
        Assert.Equal(DictationTriggerKind.PushToTalkReleased, second.Kind);
        Assert.Equal((uint)0xA3, pushToTalkHook.Hotkey?.VirtualKey);
        Assert.Equal("RightControl", pushToTalkHook.Hotkey?.Gesture);
    }

    [Fact]
    public async Task DisabledGlobalHotkey_DoesNotRegister()
    {
        var registration = new FakeHotkeyRegistration();
        using var service = new WindowsGlobalTriggerService(registration, new FakePushToTalkHook());

        await service.ConfigureAsync(
            CreateConfiguration() with { EnableGlobalHotkey = false },
            CancellationToken.None);
        service.AttachWindow((IntPtr)42);

        Assert.Null(registration.GetHotkey(WindowsGlobalTriggerService.GlobalHotkeyId));
        Assert.Equal(
            "Ctrl+Alt+M",
            registration.GetHotkey(WindowsGlobalTriggerService.FloatingWindowHotkeyId)?.Gesture);
    }

    [Fact]
    public async Task PushToTalk_AcceptsDefaultHoldChord()
    {
        var pushToTalkHook = new FakePushToTalkHook();
        using var service = new WindowsGlobalTriggerService(new FakeHotkeyRegistration(), pushToTalkHook);

        await service.ConfigureAsync(
            CreateConfiguration() with
            {
                EnableGlobalHotkey = false,
                PushToTalkKey = "Ctrl+Win+Space"
            },
            CancellationToken.None);

        Assert.True(pushToTalkHook.IsEnabled);
        Assert.Equal("Ctrl+Win+Space", pushToTalkHook.Hotkey?.Gesture);
        Assert.Equal(
            WindowsHotkeyParser.ControlModifier | WindowsHotkeyParser.WindowsModifier,
            pushToTalkHook.Hotkey?.Modifiers);
        Assert.Equal((uint)0x20, pushToTalkHook.Hotkey?.VirtualKey);
    }

    [Fact]
    public async Task ReservedHotkey_IsRejectedBeforeExistingRegistrationChanges()
    {
        var registration = new FakeHotkeyRegistration();
        using var service = new WindowsGlobalTriggerService(registration, new FakePushToTalkHook());

        await service.ConfigureAsync(CreateConfiguration(), CancellationToken.None);
        service.AttachWindow((IntPtr)42);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ConfigureAsync(
            CreateConfiguration() with { HotkeyGesture = "Alt+Tab" },
            CancellationToken.None));

        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, registration.RegisterCount);
        Assert.Equal("Ctrl+Alt+Space", service.Configuration.HotkeyGesture);
    }

    [Fact]
    public void ReorderedReservedHotkey_IsRejected()
    {
        bool parsed = WindowsHotkeyParser.TryParse(
            "Alt+Ctrl+Delete",
            out WindowsHotkey? hotkey,
            out string? error);

        Assert.False(parsed);
        Assert.Null(hotkey);
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatchingFloatingWindowAndDictationShortcuts_AreRejected()
    {
        using var service = new WindowsGlobalTriggerService(new FakeHotkeyRegistration(), new FakePushToTalkHook());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ConfigureAsync(
            CreateConfiguration() with { FloatingWindowShortcut = "Ctrl+Alt+Space" },
            CancellationToken.None));

        Assert.Contains("different", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TriggerConfiguration CreateConfiguration()
    {
        return new TriggerConfiguration(
            EnableGlobalHotkey: true,
            EnablePushToTalk: true,
        EnableFloatingButton: true,
        EnableTrayMenu: true,
        HotkeyGesture: "Ctrl+Alt+Space",
        PushToTalkKey: "RightControl",
        EnableFloatingWindowShortcut: true,
        FloatingWindowShortcut: "Ctrl+Alt+M");
    }

    private static async Task<DictationTriggerEvent> ReadFirstAsync(
        IAsyncEnumerable<DictationTriggerEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (DictationTriggerEvent trigger in events.WithCancellation(cancellationToken))
        {
            return trigger;
        }

        throw new InvalidOperationException("The trigger stream completed before producing an event.");
    }

    private sealed class FakeHotkeyRegistration : IWindowsHotkeyRegistration
    {
        public IntPtr WindowHandle { get; private set; }

        private readonly Dictionary<int, WindowsHotkey> _hotkeys = new();

        public int RegisterCount { get; private set; }

        public int UnregisterCount { get; private set; }

        public bool Register(IntPtr windowHandle, int hotkeyId, WindowsHotkey hotkey)
        {
            WindowHandle = windowHandle;
            _hotkeys[hotkeyId] = hotkey;
            RegisterCount++;
            return true;
        }

        public void Unregister(IntPtr windowHandle, int hotkeyId)
        {
            _hotkeys.Remove(hotkeyId);
            UnregisterCount++;
        }

        public WindowsHotkey? GetHotkey(int hotkeyId)
        {
            return _hotkeys.GetValueOrDefault(hotkeyId);
        }
    }

    private sealed class FakePushToTalkHook : IWindowsPushToTalkHook
    {
        public event EventHandler<WindowsPushToTalkKeyStateChangedEventArgs>? KeyStateChanged;

        public bool IsEnabled { get; private set; }

        public WindowsHotkey? Hotkey { get; private set; }

        public void Configure(bool enabled, WindowsHotkey? hotkey)
        {
            IsEnabled = enabled;
            Hotkey = hotkey;
        }

        public void Raise(bool isPressed)
        {
            KeyStateChanged?.Invoke(this, new WindowsPushToTalkKeyStateChangedEventArgs(isPressed));
        }

        public void Dispose()
        {
        }
    }
}
