namespace Shruti.Platform.Windows;

public interface IWindowsTextInput
{
    WindowsInputSendResult SendUnicodeText(string text);

    WindowsInputSendResult SendUnicodeTextSlow(string text);

    WindowsInputSendResult SendPasteShortcut(WindowsPasteShortcut shortcut = WindowsPasteShortcut.ControlV);

    WindowsInputSendResult ReleasePasteShortcutKeys();
}
