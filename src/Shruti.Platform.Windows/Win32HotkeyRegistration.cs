using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class Win32HotkeyRegistration : IWindowsHotkeyRegistration
{
    private const uint NoRepeatModifier = 0x4000;

    public bool Register(IntPtr windowHandle, int hotkeyId, WindowsHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        return NativeMethods.RegisterHotKey(
            windowHandle,
            hotkeyId,
            hotkey.Modifiers | NoRepeatModifier,
            hotkey.VirtualKey);
    }

    public void Unregister(IntPtr windowHandle, int hotkeyId)
    {
        NativeMethods.UnregisterHotKey(windowHandle, hotkeyId);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(
            IntPtr windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    }
}
