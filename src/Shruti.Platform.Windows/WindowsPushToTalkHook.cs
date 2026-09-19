using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class WindowsPushToTalkHook : IWindowsPushToTalkHook
{
    private const int LowLevelKeyboardHook = 13;
    private const uint KeyDown = 0x0100;
    private const uint KeyUp = 0x0101;
    private const uint SystemKeyDown = 0x0104;
    private const uint SystemKeyUp = 0x0105;
    private const uint ControlVirtualKey = 0x11;
    private const uint ShiftVirtualKey = 0x10;
    private const uint AltVirtualKey = 0x12;
    private const uint LeftWindowsVirtualKey = 0x5B;
    private const uint RightWindowsVirtualKey = 0x5C;
    private const int KeyPressedMask = 0x8000;

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly object _releaseSync = new();
    private IntPtr _hookHandle;
    private WindowsHotkey? _hotkey;
    private WindowsModifierChord? _modifierChord;
    private CancellationTokenSource? _releasePollingCancellation;
    private bool _isPressed;
    private bool _isMainKeySuppressed;
    private bool _isDisposed;

    public WindowsPushToTalkHook()
    {
        _callback = HookCallback;
    }

    public event EventHandler<WindowsPushToTalkKeyStateChangedEventArgs>? KeyStateChanged;

    public void Configure(bool enabled, WindowsHotkey? hotkey)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!enabled)
        {
            UninstallHook();
            return;
        }

        ArgumentNullException.ThrowIfNull(hotkey);

        if (!Equals(_hotkey, hotkey))
        {
            CancelReleasePolling();
            _isPressed = false;
            _isMainKeySuppressed = false;
            _hotkey = hotkey;
            _modifierChord = hotkey.VirtualKey == 0 ? new WindowsModifierChord(hotkey.Modifiers) : null;
        }
        if (_hookHandle == IntPtr.Zero)
            _modifierChord = hotkey.VirtualKey == 0 ? new WindowsModifierChord(hotkey.Modifiers) : null;
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(
            LowLevelKeyboardHook,
            _callback,
            IntPtr.Zero,
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the push-to-talk keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        UninstallHook();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr windowMessage, IntPtr keyboardDataPointer)
    {
        bool suppressKey = false;
        if (code >= 0 && keyboardDataPointer != IntPtr.Zero)
        {
            var keyboardData = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(keyboardDataPointer);
            uint message = unchecked((uint)windowMessage.ToInt64());
            WindowsHotkey? hotkey = _hotkey;
            if (hotkey is not null && _modifierChord is not null && (keyboardData.Flags & 0x10) == 0 &&
                message is KeyDown or SystemKeyDown or KeyUp or SystemKeyUp)
            {
                bool wasPressed = _modifierChord.IsPressed;
                suppressKey = _modifierChord.Update(keyboardData.VirtualKey, message is KeyDown or SystemKeyDown);
                if (!wasPressed && _modifierChord.IsPressed)
                {
                    // Mark Win/Alt as used so releasing a modifier delivered before
                    // the chord was complete cannot open Start or activate a menu.
                    if ((hotkey.Modifiers & (WindowsHotkeyParser.WindowsModifier | WindowsHotkeyParser.AltModifier)) != 0)
                        MaskModifierMenu();
                }
                RaiseKeyStateChanged(_modifierChord.IsPressed);
            }
            else if (hotkey is not null && keyboardData.VirtualKey == hotkey.VirtualKey)
            {
                if ((message is KeyDown or SystemKeyDown) && AreModifiersPressed(hotkey.Modifiers))
                {
                    RaiseKeyStateChanged(isPressed: true);
                    _isMainKeySuppressed = hotkey.Modifiers != 0;
                    suppressKey = _isMainKeySuppressed;
                }
                else if (message is KeyUp or SystemKeyUp)
                {
                    QueueReleaseWhenHotkeyKeysAreUp(hotkey);
                    suppressKey = _isMainKeySuppressed;
                    _isMainKeySuppressed = false;
                }
                else if (_isMainKeySuppressed)
                {
                    suppressKey = true;
                }
            }
            else if (_isPressed && hotkey is not null &&
                (message is KeyUp or SystemKeyUp) &&
                IsRequiredModifier(keyboardData.VirtualKey, hotkey.Modifiers))
            {
                QueueReleaseWhenHotkeyKeysAreUp(hotkey);
            }
        }

        return suppressKey
            ? (IntPtr)1
            : NativeMethods.CallNextHookEx(_hookHandle, code, windowMessage, keyboardDataPointer);
    }

    private static bool AreModifiersPressed(uint modifiers)
    {
        return (!HasModifier(modifiers, WindowsHotkeyParser.ControlModifier) || IsKeyPressed(ControlVirtualKey)) &&
            (!HasModifier(modifiers, WindowsHotkeyParser.ShiftModifier) || IsKeyPressed(ShiftVirtualKey)) &&
            (!HasModifier(modifiers, WindowsHotkeyParser.AltModifier) || IsKeyPressed(AltVirtualKey)) &&
            (!HasModifier(modifiers, WindowsHotkeyParser.WindowsModifier) ||
                IsKeyPressed(LeftWindowsVirtualKey) || IsKeyPressed(RightWindowsVirtualKey));
    }

    private static void MaskModifierMenu()
    {
        NativeMethods.Input[] inputs =
        [
            new() { Type = 1, VirtualKey = 0xE8 },
            new() { Type = 1, VirtualKey = 0xE8, Flags = 2 }
        ];
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static bool IsHotkeyKeyStillPressed(WindowsHotkey hotkey)
    {
        return IsKeyPressed(hotkey.VirtualKey) ||
            (HasModifier(hotkey.Modifiers, WindowsHotkeyParser.ControlModifier) && IsKeyPressed(ControlVirtualKey)) ||
            (HasModifier(hotkey.Modifiers, WindowsHotkeyParser.ShiftModifier) && IsKeyPressed(ShiftVirtualKey)) ||
            (HasModifier(hotkey.Modifiers, WindowsHotkeyParser.AltModifier) && IsKeyPressed(AltVirtualKey)) ||
            (HasModifier(hotkey.Modifiers, WindowsHotkeyParser.WindowsModifier) &&
                (IsKeyPressed(LeftWindowsVirtualKey) || IsKeyPressed(RightWindowsVirtualKey)));
    }

    private static bool IsRequiredModifier(uint virtualKey, uint modifiers)
    {
        return ((virtualKey is ControlVirtualKey or 0xA2 or 0xA3) &&
                HasModifier(modifiers, WindowsHotkeyParser.ControlModifier)) ||
            ((virtualKey is ShiftVirtualKey or 0xA0 or 0xA1) &&
                HasModifier(modifiers, WindowsHotkeyParser.ShiftModifier)) ||
            ((virtualKey is AltVirtualKey or 0xA4 or 0xA5) &&
                HasModifier(modifiers, WindowsHotkeyParser.AltModifier)) ||
            ((virtualKey is LeftWindowsVirtualKey or RightWindowsVirtualKey) &&
                HasModifier(modifiers, WindowsHotkeyParser.WindowsModifier));
    }

    private static bool HasModifier(uint modifiers, uint modifier)
    {
        return (modifiers & modifier) != 0;
    }

    private static bool IsKeyPressed(uint virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState((int)virtualKey) & KeyPressedMask) != 0;
    }

    private void RaiseKeyStateChanged(bool isPressed)
    {
        if (_isPressed == isPressed)
        {
            return;
        }

        _isPressed = isPressed;

        try
        {
            KeyStateChanged?.Invoke(this, new WindowsPushToTalkKeyStateChangedEventArgs(isPressed));
        }
        catch
        {
            // A hook callback must not allow consumer exceptions to cross into user32.
        }
    }

    private void QueueReleaseWhenHotkeyKeysAreUp(WindowsHotkey hotkey)
    {
        lock (_releaseSync)
        {
            if (!_isPressed)
            {
                return;
            }

            _releasePollingCancellation?.Cancel();
            _releasePollingCancellation?.Dispose();
            _releasePollingCancellation = new CancellationTokenSource();
            _ = ReleaseWhenHotkeyKeysAreUpAsync(hotkey, _releasePollingCancellation.Token);
        }
    }

    private async Task ReleaseWhenHotkeyKeysAreUpAsync(
        WindowsHotkey hotkey,
        CancellationToken cancellationToken)
    {
        try
        {
            while (IsHotkeyKeyStillPressed(hotkey))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken).ConfigureAwait(false);
            }

            RaiseKeyStateChanged(isPressed: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UninstallHook()
    {
        CancelReleasePolling();
        _modifierChord = null;
        if (_hookHandle == IntPtr.Zero)
        {
            _isPressed = false;
            _isMainKeySuppressed = false;
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _isPressed = false;
        _isMainKeySuppressed = false;
    }

    private void CancelReleasePolling()
    {
        lock (_releaseSync)
        {
            _releasePollingCancellation?.Cancel();
            _releasePollingCancellation?.Dispose();
            _releasePollingCancellation = null;
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public ushort VirtualKey;
            [FieldOffset(12)] public uint Flags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, Input[] inputs, int size);

        public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr windowMessage, IntPtr keyboardDataPointer);

        [StructLayout(LayoutKind.Sequential)]
        public struct KbdLlHookStruct
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int hookId,
            LowLevelKeyboardProc callback,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int code,
            IntPtr windowMessage,
            IntPtr keyboardDataPointer);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);
    }
}
