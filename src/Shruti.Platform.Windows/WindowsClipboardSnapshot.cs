namespace Shruti.Platform.Windows;

public sealed record WindowsClipboardSnapshot(
    bool CanRestore,
    string? Text,
    uint SequenceNumber,
    string? Message = null)
{
    public static WindowsClipboardSnapshot Empty { get; } = new(
        CanRestore: true,
        Text: null,
        SequenceNumber: 0);

    public static WindowsClipboardSnapshot Unavailable(
        string message,
        uint sequenceNumber = 0)
    {
        return new WindowsClipboardSnapshot(
            CanRestore: false,
            Text: null,
            SequenceNumber: sequenceNumber,
            Message: message);
    }
}
