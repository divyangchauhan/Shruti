using System.Runtime.InteropServices;
using System.Text;

namespace Shruti.Platform.Windows;

public sealed class Win32Windowing : IWindowsWindowing
{
    private const int ShowWindowRestore = 9;

    public IntPtr GetForegroundWindow()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public WindowsWindowSnapshot? CaptureWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
        if (threadId == 0 || processId == 0)
        {
            return null;
        }

        return new WindowsWindowSnapshot(
            windowHandle,
            checked((int)processId),
            checked((int)threadId),
            GetWindowTitle(windowHandle));
    }

    public bool IsWindow(IntPtr windowHandle)
    {
        return NativeMethods.IsWindow(windowHandle);
    }

    public bool IsMinimized(IntPtr windowHandle)
    {
        return NativeMethods.IsIconic(windowHandle);
    }

    public bool RestoreWindow(IntPtr windowHandle)
    {
        return NativeMethods.ShowWindow(windowHandle, ShowWindowRestore);
    }

    public bool SetForegroundWindow(IntPtr windowHandle)
    {
        return NativeMethods.SetForegroundWindow(windowHandle);
    }

    private static string? GetWindowTitle(IntPtr windowHandle)
    {
        int length = NativeMethods.GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(length + 1);
        int copied = NativeMethods.GetWindowText(windowHandle, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : null;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(
            IntPtr hWnd,
            StringBuilder text,
            int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(
            IntPtr hWnd,
            int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
