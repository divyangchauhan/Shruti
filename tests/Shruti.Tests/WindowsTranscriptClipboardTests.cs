using Shruti.Core.Platform;
using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsTranscriptClipboardTests
{
    [Fact]
    public async Task CopyTextAsync_WritesTranscriptWithoutRestoringClipboard()
    {
        var clipboard = new FakeClipboard(
            new WindowsClipboardSnapshot(CanRestore: false, Text: null, SequenceNumber: 42));
        var transcriptClipboard = new WindowsTranscriptClipboard(clipboard);

        await transcriptClipboard.CopyTextAsync("copied transcript", CancellationToken.None);

        Assert.Equal("copied transcript", transcriptClipboard.LastCopiedText);
        Assert.Equal("copied transcript", clipboard.LastText);
        Assert.Equal((uint)42, clipboard.LastExpectedSequenceNumber);
        Assert.Equal(0, clipboard.RestoreCount);
    }

    [Fact]
    public async Task CopyTextAsync_ThrowsWhenClipboardWriteFails()
    {
        var clipboard = new FakeClipboard(
            WindowsClipboardSnapshot.Empty,
            new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.FailedWithoutModification,
                ExpectedSequenceNumber: 0,
                Message: "Clipboard is unavailable."));
        var transcriptClipboard = new WindowsTranscriptClipboard(clipboard);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transcriptClipboard.CopyTextAsync("copied transcript", CancellationToken.None));

        Assert.Equal("Clipboard is unavailable.", exception.Message);
        Assert.Null(transcriptClipboard.LastCopiedText);
    }

    private sealed class FakeClipboard : IWindowsClipboard
    {
        private readonly WindowsClipboardSnapshot _snapshot;
        private readonly WindowsClipboardWriteResult? _writeResult;

        public FakeClipboard(
            WindowsClipboardSnapshot snapshot,
            WindowsClipboardWriteResult? writeResult = null)
        {
            _snapshot = snapshot;
            _writeResult = writeResult;
        }

        public string? LastText { get; private set; }

        public uint LastExpectedSequenceNumber { get; private set; }

        public int RestoreCount { get; private set; }

        public WindowsClipboardSnapshot Capture()
        {
            return _snapshot;
        }

        public WindowsClipboardWriteResult SetText(string text, uint expectedSequenceNumber)
        {
            LastText = text;
            LastExpectedSequenceNumber = expectedSequenceNumber;
            return _writeResult ?? new WindowsClipboardWriteResult(
                WindowsClipboardWriteOutcome.TemporaryTextWritten,
                expectedSequenceNumber + 1);
        }

        public WindowsClipboardRestoreResult RestoreIfUnchanged(
            WindowsClipboardSnapshot snapshot,
            uint expectedSequenceNumber)
        {
            RestoreCount++;
            return new WindowsClipboardRestoreResult(WindowsClipboardRestoreOutcome.Restored);
        }
    }
}
