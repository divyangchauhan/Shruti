namespace Shruti.Platform.Windows;

public sealed record WindowsClipboardRestoreResult(
    WindowsClipboardRestoreOutcome Outcome,
    string? Message = null)
{
    public bool RestoredOrSuperseded => Outcome is WindowsClipboardRestoreOutcome.Restored
        or WindowsClipboardRestoreOutcome.SkippedClipboardChanged;
}
