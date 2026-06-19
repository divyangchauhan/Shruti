namespace Shruti.Core.Triggers;

public sealed record DictationTriggerEvent(
    DictationTriggerKind Kind,
    DateTimeOffset OccurredAt,
    string? SourceId = null);
