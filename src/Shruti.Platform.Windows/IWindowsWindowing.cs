namespace Shruti.Platform.Windows;

public interface IWindowsWindowing
{
    IntPtr GetForegroundWindow();

    WindowsWindowSnapshot? CaptureWindow(IntPtr windowHandle);

    bool IsWindow(IntPtr windowHandle);

    bool IsMinimized(IntPtr windowHandle);

    bool RestoreWindow(IntPtr windowHandle);

    bool SetForegroundWindow(IntPtr windowHandle);
}
