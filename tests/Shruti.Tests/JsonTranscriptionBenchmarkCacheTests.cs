using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class JsonTranscriptionBenchmarkCacheTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_PersistsAndUpsertsBenchmarkResults()
    {
        var paths = new AppDataPaths(_rootPath);
        var cache = new JsonTranscriptionBenchmarkCache(paths);
        var key = new TranscriptionBenchmarkKey(
            "whisper.cpp",
            "1",
            "tiny",
            "hash",
            ComputeBackend.Cpu,
            "CPU");

        await cache.SaveAsync(Result(key, realtimeFactor: 0.8), CancellationToken.None);
        await cache.SaveAsync(Result(key, realtimeFactor: 0.6), CancellationToken.None);
        TranscriptionBenchmarkResult? loaded = await new JsonTranscriptionBenchmarkCache(paths)
            .GetAsync(key, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(0.6, loaded.RealtimeFactor);
        Assert.True(File.Exists(paths.BenchmarkCacheFilePath));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForMissingResult()
    {
        var cache = new JsonTranscriptionBenchmarkCache(new AppDataPaths(_rootPath));
        var key = new TranscriptionBenchmarkKey(
            "whisper.cpp",
            "1",
            "tiny",
            "hash",
            ComputeBackend.Cpu,
            "CPU");

        TranscriptionBenchmarkResult? loaded = await cache.GetAsync(key, CancellationToken.None);

        Assert.Null(loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static TranscriptionBenchmarkResult Result(
        TranscriptionBenchmarkKey key,
        double realtimeFactor)
    {
        return new TranscriptionBenchmarkResult(
            key,
            realtimeFactor,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(realtimeFactor),
            DateTimeOffset.UtcNow,
            Warnings: []);
    }
}
