using Shruti.Core.Platform;

namespace Shruti.Workflow.Dictation;

public sealed class MockTranscriptClipboard : ITranscriptClipboard
{
    public string? LastCopiedText { get; private set; }

    public Exception? CopyException { get; set; }

    public Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CopyException is not null)
        {
            throw CopyException;
        }

        LastCopiedText = text;
        return Task.CompletedTask;
    }
}
