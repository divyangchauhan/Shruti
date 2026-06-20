namespace Shruti.Workflow.Dictation;

public interface ITranscriptClipboard
{
    string? LastCopiedText { get; }

    Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken);
}
