namespace Shruti.Storage;

public interface ISessionRepository
{
    Task SaveAsync(StoredDictationSession session, CancellationToken cancellationToken);

    Task<StoredDictationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
}
