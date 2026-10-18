namespace Shruti.Core.TranscriptQuality;

public sealed record VocabularyEntry(Guid Id, string Language, string Term, DateTimeOffset CreatedAtUtc)
{
    public override string ToString() => Term;
}

public sealed record CorrectionRule(Guid Id, string Language, string HeardText, string ReplacementText, DateTimeOffset CreatedAtUtc)
{
    public override string ToString() => $"{HeardText} → {ReplacementText}";
}

public interface IVocabularyRepository
{
    Task<IReadOnlyList<VocabularyEntry>> ListVocabularyAsync(string language, CancellationToken cancellationToken);
    Task<IReadOnlyList<CorrectionRule>> ListCorrectionsAsync(string language, CancellationToken cancellationToken);
    Task SaveVocabularyAsync(VocabularyEntry entry, CancellationToken cancellationToken);
    Task SaveCorrectionAsync(CorrectionRule rule, CancellationToken cancellationToken);
    Task DeleteVocabularyAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteCorrectionAsync(Guid id, CancellationToken cancellationToken);
}
