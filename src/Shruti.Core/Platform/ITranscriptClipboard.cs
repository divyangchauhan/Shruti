namespace Shruti.Core.Platform;

public interface ITranscriptClipboard
{
    string? LastCopiedText { get; }

    Task CopyTextAsync(
        string text,
        CancellationToken cancellationToken);
}
