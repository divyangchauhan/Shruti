namespace Shruti.Transcription.Abstractions;

public sealed class InMemoryTranscriptionBenchmarkCache : ITranscriptionBenchmarkCache
{
    private readonly object _sync = new();
    private readonly Dictionary<TranscriptionBenchmarkKey, TranscriptionBenchmarkResult> _results = [];

    public Task<TranscriptionBenchmarkResult?> GetAsync(
        TranscriptionBenchmarkKey key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(_results.GetValueOrDefault(key));
        }
    }

    public Task SaveAsync(
        TranscriptionBenchmarkResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _results[result.Key] = result;
        }

        return Task.CompletedTask;
    }
}
