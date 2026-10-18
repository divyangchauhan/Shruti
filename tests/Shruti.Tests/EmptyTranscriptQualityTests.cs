using Shruti.Core.TranscriptQuality;
using Shruti.TranscriptQuality;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class EmptyTranscriptQualityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("[NO_SPEECH]")]
    public async Task EmptyTranscript_SkipsEditorAndReturnsNothing(string text)
    {
        var editor = new Editor();
        using var pipeline = new TranscriptQualityPipeline(new Repository(), null, editor);
        await using var session = await pipeline.BeginAsync(null, new() { EnableLocalModel = true, CleanupMode = TranscriptCleanupMode.Light }, default);
        TranscriptResult raw = TranscriptResult.FromText(text);

        var refined = await session.RefineAsync(raw, default);

        Assert.Empty(refined.Text);
        Assert.Same(raw, refined.Raw);
        Assert.Empty(refined.Edits);
        Assert.False(refined.UsedRawFallback);
        Assert.Equal(0, editor.Calls);
    }

    [Fact]
    public void SignalPrecheck_RejectsSilenceAndDcButKeepsVeryQuietVaryingAudio()
    {
        Assert.False(Pcm16Signal.HasSignal([]));
        Assert.False(Pcm16Signal.HasSignal(new byte[32000]));
        Assert.False(Pcm16Signal.HasSignal(new byte[] { 0, 64, 0, 64 }));
        Assert.True(Pcm16Signal.HasSignal(new byte[] { 8, 0, 248, 255 }));
    }

    private sealed class Editor : ILocalTranscriptEditor
    {
        public int Calls { get; private set; }
        public Task PrepareAsync(TranscriptQualitySettings settings, CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<ProposedTranscriptEdit>> ProposeAsync(string text, IReadOnlyList<string> terms,
            TranscriptAppCategory category, TranscriptQualitySettings settings, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Empty input must never reach the editor.");
        }
    }

    private sealed class Repository : IVocabularyRepository
    {
        public Task<IReadOnlyList<VocabularyEntry>> ListVocabularyAsync(string language, CancellationToken token) => Task.FromResult<IReadOnlyList<VocabularyEntry>>([]);
        public Task<IReadOnlyList<CorrectionRule>> ListCorrectionsAsync(string language, CancellationToken token) => Task.FromResult<IReadOnlyList<CorrectionRule>>([]);
        public Task SaveVocabularyAsync(VocabularyEntry entry, CancellationToken token) => throw new NotSupportedException();
        public Task SaveCorrectionAsync(CorrectionRule rule, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteVocabularyAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCorrectionAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
