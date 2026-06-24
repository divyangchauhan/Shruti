namespace Shruti.Transcription.Abstractions;

public interface ITranscriptionBenchmarkCache
{
    Task<TranscriptionBenchmarkResult?> GetAsync(
        TranscriptionBenchmarkKey key,
        CancellationToken cancellationToken);

    Task SaveAsync(
        TranscriptionBenchmarkResult result,
        CancellationToken cancellationToken);
}
