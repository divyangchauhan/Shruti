using Shruti.Core.Dictation;
using Shruti.Core.TranscriptQuality;
using Shruti.Storage;
using Shruti.TranscriptQuality;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class TranscriptLibraryTests : IDisposable
{
    private readonly AppDataPaths _paths = new(Path.Combine(Path.GetTempPath(), "shruti-library-tests", Guid.NewGuid().ToString("N")));
    public void Dispose() { if (Directory.Exists(_paths.RootPath)) Directory.Delete(_paths.RootPath, true); }

    [Fact]
    public async Task History_PersistsSearchesUpdatesAndDeletesWithoutCountingSilence()
    {
        var repository = new TranscriptHistoryRepository(_paths);
        var raw = TranscriptResult.FromText("hello um world");
        var result = new DictationRunResult(DictationRunOutcome.CopyOnly, [], null, TranscriptResult.FromText("Hello world"))
        { RefinedTranscript = new(raw, "Hello world", [new("filler", 6, 3, "")], false), AudioDuration = TimeSpan.FromSeconds(2) };
        await repository.SaveAsync(result);
        await repository.SaveAsync(result);
        await repository.SaveAsync(result with { Id = Guid.NewGuid(), Outcome = DictationRunOutcome.NoSpeech });
        await repository.SaveAsync(result with { Id = Guid.NewGuid(), Transcript = TranscriptResult.FromText(" ") });
        var rows = await new TranscriptHistoryRepository(_paths).ListAsync("WORLD");
        Assert.Single(rows); Assert.Equal(raw.Text, rows[0].RawText); Assert.Equal(2, rows[0].Words);
        Assert.Empty(await repository.ListAsync("%"));
        var stats = await repository.GetInsightsAsync();
        Assert.Equal(1, stats.Dictations); Assert.Equal(2, stats.Words); Assert.Equal(2, stats.AudioSeconds); Assert.Single(stats.Activity);
        await repository.SaveAsync(result with { Transcript = TranscriptResult.FromText("Edited preview text") });
        Assert.Equal(3, (await repository.GetInsightsAsync()).Words);
        Assert.Equal(raw.Text, (await repository.ListAsync())[0].RawText);
        await repository.DeleteAsync(result.Id);
        Assert.Equal(0, (await repository.GetInsightsAsync()).Dictations);
        await repository.SaveAsync(result);
        await repository.DeleteAsync(null);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task Dictionary_EditKeepsIdentity()
    {
        var repository = new SqliteVocabularyRepository(_paths);
        var entry = new VocabularyEntry(Guid.NewGuid(), "en", "Shruti", DateTimeOffset.UtcNow);
        await repository.SaveVocabularyAsync(entry, default);
        await repository.SaveVocabularyAsync(entry with { Term = "OpenVINO" }, default);
        Assert.Equal("OpenVINO", Assert.Single(await repository.ListVocabularyAsync("en", default)).Term);
        var rule = new CorrectionRule(Guid.NewGuid(), "en", "cloud", "Claude", DateTimeOffset.UtcNow);
        await repository.SaveCorrectionAsync(rule, default);
        await repository.SaveCorrectionAsync(rule with { HeardText = "clawed" }, default);
        Assert.Equal("clawed", Assert.Single(await repository.ListCorrectionsAsync("en", default)).HeardText);
    }

    [Fact]
    public async Task Snippets_PersistEditAndEnforceUniqueNormalizedTriggers()
    {
        var repository = new SqliteSnippetRepository(_paths);
        var snippet = new TranscriptSnippet(Guid.NewGuid(), "en", "my link", "https://example.com/Exact_Case\nsecond line");
        await repository.SaveAsync(snippet, default);
        Assert.Equal(snippet, Assert.Single(await new SqliteSnippetRepository(_paths).ListAsync("en", default)));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.SaveAsync(snippet with { Id = Guid.NewGuid(), Phrase = "MY   LINK" }, default));
        await repository.SaveAsync(snippet with { Phrase = "my website" }, default);
        Assert.Equal("my website", Assert.Single(await repository.ListAsync("en", default)).Phrase);
        Assert.Empty(await repository.ListAsync("fr", default));
        await repository.DeleteAsync(snippet.Id, default);
        Assert.Empty(await repository.ListAsync("en", default));
    }

    [Theory]
    [InlineData("My EMAIL address.", "Exact.Case@example.com")]
    [InlineData("Send it to my email address please.", "Send it to Exact.Case@example.com please.")]
    [InlineData("my emails address", "My emails address")]
    [InlineData("\"my email address\"", "\"my email address\"")]
    [InlineData("", "")]
    public async Task Snippets_UseWholePhrasesAndPreserveSavedText(string input, string expected)
    {
        var snippets = new SqliteSnippetRepository(_paths);
        await snippets.SaveAsync(new(Guid.NewGuid(), "en", "my email", "wrong short match"), default);
        await snippets.SaveAsync(new(Guid.NewGuid(), "en", "my email address", "Exact.Case@example.com"), default);
        using var pipeline = new TranscriptQualityPipeline(new SqliteVocabularyRepository(_paths), snippets: snippets);
        await using var session = await pipeline.BeginAsync(null, new() { EnableLocalModel = false, CleanupMode = TranscriptCleanupMode.Light }, default);
        var result = await session.RefineAsync(TranscriptResult.FromText(input), default);
        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public async Task Snippets_DoNotRunCleanupOrRecursiveExpansionOnSavedContents()
    {
        var snippets = new SqliteSnippetRepository(_paths);
        const string body = "new line, two actually three\nmy link\nhttps://example.com/Case";
        await snippets.SaveAsync(new(Guid.NewGuid(), "en", "my signature", body), default);
        await snippets.SaveAsync(new(Guid.NewGuid(), "en", "my link", "should not expand"), default);
        using var pipeline = new TranscriptQualityPipeline(new SqliteVocabularyRepository(_paths), snippets: snippets);
        await using var session = await pipeline.BeginAsync(null, new() { EnableLocalModel = false, CleanupMode = TranscriptCleanupMode.Light }, default);
        var result = await session.RefineAsync(TranscriptResult.FromText("my signature."), default);
        Assert.Equal(body, result.Text);
        Assert.Contains(result.Edits, e => e.Rule == "snippet");
    }

    [Fact]
    public async Task Snippets_RejectModelEditsThatChangeAPlaceholder()
    {
        var snippets = new SqliteSnippetRepository(_paths);
        await snippets.SaveAsync(new(Guid.NewGuid(), "en", "my link", "https://example.com/Exact"), default);
        var editor = new PlaceholderChangingEditor();
        using var pipeline = new TranscriptQualityPipeline(new SqliteVocabularyRepository(_paths), null, editor, snippets);
        await using var session = await pipeline.BeginAsync(null, new() { EnableLocalModel = true, CleanupMode = TranscriptCleanupMode.Light }, default);
        var result = await session.RefineAsync(TranscriptResult.FromText("Send my link please."), default);
        Assert.Equal("Send https://example.com/Exact please.", result.Text);
        Assert.Equal(1, editor.Calls);
        Assert.DoesNotContain("shruti_snippet_", result.Text);
    }

    private sealed class PlaceholderChangingEditor : ILocalTranscriptEditor
    {
        public int Calls { get; private set; }
        public Task PrepareAsync(TranscriptQualitySettings settings, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<ProposedTranscriptEdit>> ProposeAsync(string text, IReadOnlyList<string> protectedTerms,
            TranscriptAppCategory category, TranscriptQualitySettings settings, CancellationToken token)
        {
            Calls++;
            var marker = protectedTerms.Single(t => t.StartsWith("shruti_snippet_", StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<ProposedTranscriptEdit>>([new(marker, "another link", "grammar")]);
        }
    }
}
