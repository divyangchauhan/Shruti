namespace Shruti.Platform.Windows;

public interface IWindowsTrayIconApi
{
    bool AddIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip);

    bool UpdateIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip);

    void RemoveIcon(IntPtr windowHandle, uint iconId);

    WindowsTrayCommand? ShowMenu(IntPtr windowHandle, bool isDictationRunning);
}
