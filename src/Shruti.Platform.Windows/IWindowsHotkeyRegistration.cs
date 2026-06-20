namespace Shruti.Platform.Windows;

public interface IWindowsHotkeyRegistration
{
    bool Register(IntPtr windowHandle, int hotkeyId, WindowsHotkey hotkey);

    void Unregister(IntPtr windowHandle, int hotkeyId);
}
