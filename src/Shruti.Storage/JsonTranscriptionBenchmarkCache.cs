using System.Text.Json;
using Shruti.Transcription.Abstractions;

namespace Shruti.Storage;

public sealed class JsonTranscriptionBenchmarkCache : ITranscriptionBenchmarkCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppDataPaths _paths;

    public JsonTranscriptionBenchmarkCache(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<TranscriptionBenchmarkResult?> GetAsync(
        TranscriptionBenchmarkKey key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BenchmarkCacheFile cacheFile = await LoadCacheFileAsync(cancellationToken).ConfigureAwait(false);
            return cacheFile.Results.FirstOrDefault(result => result.Key == key);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        TranscriptionBenchmarkResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = $"{_paths.BenchmarkCacheFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            BenchmarkCacheFile cacheFile = await LoadCacheFileAsync(cancellationToken).ConfigureAwait(false);
            List<TranscriptionBenchmarkResult> results = cacheFile.Results
                .Where(candidate => candidate.Key != result.Key)
                .Append(result)
                .OrderBy(candidate => candidate.Key.ProviderId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Key.ModelId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Key.Backend)
                .ThenBy(candidate => candidate.Key.DeviceName, StringComparer.Ordinal)
                .ToList();

            _paths.EnsureCreated();
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer
                    .SerializeAsync(stream, new BenchmarkCacheFile(results), SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.BenchmarkCacheFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }

    private async Task<BenchmarkCacheFile> LoadCacheFileAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.BenchmarkCacheFilePath))
        {
            return new BenchmarkCacheFile([]);
        }

        await using var stream = new FileStream(
            _paths.BenchmarkCacheFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        BenchmarkCacheFile? cacheFile = await JsonSerializer
            .DeserializeAsync<BenchmarkCacheFile>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return cacheFile ?? new BenchmarkCacheFile([]);
    }

    private sealed record BenchmarkCacheFile(IReadOnlyList<TranscriptionBenchmarkResult> Results);
}
