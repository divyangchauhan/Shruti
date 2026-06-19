namespace Shruti.Platform.Windows;

public enum WindowsClipboardWriteOutcome
{
    TemporaryTextWritten,
    SkippedClipboardChanged,
    FailedWithoutModification,
    FailedAfterModification
}
