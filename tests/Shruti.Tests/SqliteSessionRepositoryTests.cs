using Shruti.Storage;
using Xunit;

namespace Shruti.Tests;

public sealed class SqliteSessionRepositoryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_PersistsSessionAndSegmentsInIndexOrder()
    {
        var paths = new AppDataPaths(_rootPath);
        var repository = new SqliteSessionRepository(paths);
        StoredDictationSession session = CreateSession();

        await repository.SaveAsync(session, CancellationToken.None);
        StoredDictationSession? loaded = await repository.GetAsync(session.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(session.StartedAtUtc, loaded.StartedAtUtc);
        Assert.Equal(session.EndedAtUtc, loaded.EndedAtUtc);
        Assert.Equal("GlobalHotkey", loaded.SourceTrigger);
        Assert.Equal("notepad", loaded.TargetProcessName);
        Assert.Equal("tiny.en", loaded.ModelId);
        Assert.Equal("whisper.cpp", loaded.ProviderId);
        Assert.Equal("Cpu", loaded.Backend);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("Complete", loaded.Status);
        Assert.Collection(
            loaded.Segments,
            first =>
            {
                Assert.Equal(0, first.Index);
                Assert.Equal("First segment.", first.Text);
                Assert.Equal(0.91f, first.Confidence);
            },
            second =>
            {
                Assert.Equal(1, second.Index);
                Assert.Equal("Second segment.", second.Text);
                Assert.Null(second.Confidence);
            });
        Assert.True(File.Exists(paths.DatabaseFilePath));
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingSegmentsWhenSessionIsUpdated()
    {
        var repository = new SqliteSessionRepository(new AppDataPaths(_rootPath));
        StoredDictationSession initial = CreateSession();
        await repository.SaveAsync(initial, CancellationToken.None);

        StoredDictationSession updated = initial with
        {
            Status = "Corrected",
            Segments = [new StoredTranscriptSegment(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Corrected text.")]
        };
        await repository.SaveAsync(updated, CancellationToken.None);
        StoredDictationSession? loaded = await repository.GetAsync(initial.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Corrected", loaded.Status);
        StoredTranscriptSegment segment = Assert.Single(loaded.Segments);
        Assert.Equal("Corrected text.", segment.Text);
    }

    [Fact]
    public async Task SaveAsync_RejectsSegmentsWithInvalidTimestamps()
    {
        var repository = new SqliteSessionRepository(new AppDataPaths(_rootPath));
        StoredDictationSession invalid = CreateSession() with
        {
            Segments = [new StoredTranscriptSegment(0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), "Invalid")]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(invalid, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static StoredDictationSession CreateSession()
    {
        return new StoredDictationSession(
            Guid.Parse("a28931aa-5750-44aa-bef0-e827e77f4a35"),
            new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 20, 9, 0, 4, TimeSpan.Zero),
            "GlobalHotkey",
            "notepad",
            "Untitled - Notepad",
            "tiny.en",
            "whisper.cpp",
            "Cpu",
            "en",
            "Complete",
            [
                new StoredTranscriptSegment(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3.25), "Second segment."),
                new StoredTranscriptSegment(0, TimeSpan.Zero, TimeSpan.FromSeconds(1.25), "First segment.", 0.91f)
            ]);
    }
}
