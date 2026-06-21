using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsTranscriptClipboard : ITranscriptClipboard
{
    private readonly IWindowsClipboard _clipboard;

    public WindowsTranscriptClipboard(IWindowsClipboard? clipboard = null)
    {
        _clipboard = clipboard ?? new WindowsClipboard();
    }

    public string? LastCopiedText { get; private set; }

    public Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        WindowsClipboardSnapshot snapshot = _clipboard.Capture();
        WindowsClipboardWriteResult write = _clipboard.SetText(text, snapshot.SequenceNumber);
        if (!write.TemporaryTextWritten)
        {
            throw new InvalidOperationException(
                write.Message ?? "Shruti could not copy the transcript to the clipboard.");
        }

        LastCopiedText = text;
        return Task.CompletedTask;
    }
}
