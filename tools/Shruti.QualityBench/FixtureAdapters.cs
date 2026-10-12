using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;

namespace Shruti.QualityBench;

internal sealed class FixtureVocabularyRepository(QualityCase fixture) : IVocabularyRepository
{
    public Task<IReadOnlyList<VocabularyEntry>> ListVocabularyAsync(string language, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<VocabularyEntry>>(fixture.Vocabulary.Select(term => new VocabularyEntry(Guid.NewGuid(), language, term, DateTimeOffset.UnixEpoch)).ToArray());
    public Task<IReadOnlyList<CorrectionRule>> ListCorrectionsAsync(string language, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CorrectionRule>>(fixture.Corrections.Select(pair => new CorrectionRule(Guid.NewGuid(), language, pair.Key, pair.Value, DateTimeOffset.UnixEpoch)).ToArray());
    public Task SaveVocabularyAsync(VocabularyEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SaveCorrectionAsync(CorrectionRule rule, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteVocabularyAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteCorrectionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FixtureContextReader(QualityCase fixture) : ITextContextReader
{
    public Task<TranscriptContext?> ReadAsync(FocusTarget target, CancellationToken cancellationToken)
        => Task.FromResult<TranscriptContext?>(new(fixture.BeforeCaret, fixture.SelectedText, fixture.AfterCaret,
            Enum.Parse<TranscriptAppCategory>(fixture.AppCategory)));
}
