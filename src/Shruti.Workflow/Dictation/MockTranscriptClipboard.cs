using Shruti.Core.Platform;

namespace Shruti.Workflow.Dictation;

public sealed class MockTranscriptClipboard : ITranscriptClipboard
{
    public string? LastCopiedText { get; private set; }

    public Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastCopiedText = text;
        return Task.CompletedTask;
    }
}
