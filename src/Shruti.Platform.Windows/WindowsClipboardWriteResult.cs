namespace Shruti.Platform.Windows;

public sealed record WindowsClipboardWriteResult(
    WindowsClipboardWriteOutcome Outcome,
    uint ExpectedSequenceNumber,
    string? Message = null)
{
    public bool TemporaryTextWritten => Outcome == WindowsClipboardWriteOutcome.TemporaryTextWritten;

    public bool ClipboardMayNeedRestore => Outcome is WindowsClipboardWriteOutcome.TemporaryTextWritten
        or WindowsClipboardWriteOutcome.FailedAfterModification;
}
