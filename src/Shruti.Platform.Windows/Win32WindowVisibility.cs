using System.Runtime.InteropServices;

namespace Shruti.Platform.Windows;

public sealed class Win32WindowVisibility : IWindowsWindowVisibility
{
    private const int HideWindow = 0;
    private const int ShowWindowNormal = 1;
    private const int ShowWindowNoActivate = 4;
    private const int ExtendedWindowStyleIndex = -20;
    private const nint NoActivateWindowStyle = 0x08000000;

    public void Hide(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(windowHandle, HideWindow);
        }
    }

    public void ShowAndActivate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ShowWindow(windowHandle, ShowWindowNormal);
        NativeMethods.SetForegroundWindow(windowHandle);
    }

    public void ShowWithoutActivating(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(windowHandle, ShowWindowNoActivate);
        }
    }

    public void MakeNonActivating(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        nint currentStyle = NativeMethods.GetWindowLongPtr(windowHandle, ExtendedWindowStyleIndex);
        NativeMethods.SetWindowLongPtr(
            windowHandle,
            ExtendedWindowStyleIndex,
            currentStyle | NoActivateWindowStyle);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern nint GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern nint SetWindowLongPtr(IntPtr windowHandle, int index, nint newValue);
    }
}
