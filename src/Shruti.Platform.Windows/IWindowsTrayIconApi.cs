namespace Shruti.Platform.Windows;

public interface IWindowsTrayIconApi
{
    event Action<WindowsTrayCommand>? CommandInvoked;

    bool AddIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip);

    bool UpdateIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip);

    void RemoveIcon(IntPtr windowHandle, uint iconId);

    void SetCommandState(bool isDictationRunning, bool areDictationCommandsEnabled);

    WindowsTrayCommand? ShowMenu(
        IntPtr windowHandle,
        bool isDictationRunning,
        bool areDictationCommandsEnabled);
}
