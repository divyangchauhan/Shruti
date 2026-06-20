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

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private IntPtr _hookHandle;
    private uint _virtualKey;
    private bool _isPressed;
    private bool _isDisposed;

    public WindowsPushToTalkHook()
    {
        _callback = HookCallback;
    }

    public event EventHandler<WindowsPushToTalkKeyStateChangedEventArgs>? KeyStateChanged;

    public void Configure(bool enabled, uint virtualKey)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!enabled)
        {
            UninstallHook();
            return;
        }

        if (_virtualKey != virtualKey)
        {
            _isPressed = false;
            _virtualKey = virtualKey;
        }
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
        if (code >= 0 && keyboardDataPointer != IntPtr.Zero)
        {
            var keyboardData = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(keyboardDataPointer);
            if (keyboardData.VirtualKey == _virtualKey)
            {
                uint message = unchecked((uint)windowMessage.ToInt64());
                if (message is KeyDown or SystemKeyDown)
                {
                    RaiseKeyStateChanged(isPressed: true);
                }
                else if (message is KeyUp or SystemKeyUp)
                {
                    RaiseKeyStateChanged(isPressed: false);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, windowMessage, keyboardDataPointer);
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

    private void UninstallHook()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            _isPressed = false;
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _isPressed = false;
    }

    private static class NativeMethods
    {
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
    }
}
