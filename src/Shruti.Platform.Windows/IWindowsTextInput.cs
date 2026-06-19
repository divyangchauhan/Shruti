namespace Shruti.Platform.Windows;

public interface IWindowsTextInput
{
    WindowsInputSendResult SendUnicodeText(string text);

    WindowsInputSendResult SendPasteShortcut();

    WindowsInputSendResult ReleasePasteShortcutKeys();
}
