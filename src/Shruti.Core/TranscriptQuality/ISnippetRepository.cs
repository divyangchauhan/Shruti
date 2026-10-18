namespace Shruti.Core.TranscriptQuality;

public sealed record TranscriptSnippet(Guid Id, string Language, string Phrase, string Text);

public interface ISnippetRepository
{
    Task<IReadOnlyList<TranscriptSnippet>> ListAsync(string language, CancellationToken cancellationToken);
    Task SaveAsync(TranscriptSnippet snippet, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
