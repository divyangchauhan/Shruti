namespace Shruti.Platform.Windows;

public interface IWindowsClipboard
{
    WindowsClipboardSnapshot Capture();

    WindowsClipboardWriteResult SetText(
        string text,
        uint expectedSequenceNumber);

    WindowsClipboardRestoreResult RestoreIfUnchanged(
        WindowsClipboardSnapshot snapshot,
        uint expectedSequenceNumber);
}
