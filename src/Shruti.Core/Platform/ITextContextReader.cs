using Shruti.Core.TranscriptQuality;

namespace Shruti.Core.Platform;

public interface ITextContextReader
{
    Task<TranscriptContext?> ReadAsync(FocusTarget target, CancellationToken cancellationToken);
}
