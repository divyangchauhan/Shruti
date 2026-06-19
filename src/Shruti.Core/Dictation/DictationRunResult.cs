using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;

namespace Shruti.Core.Dictation;

public sealed record DictationRunResult(
    DictationRunOutcome Outcome,
    IReadOnlyList<DictationStatus> StatusHistory,
    FocusTarget? Target,
    TranscriptResult? Transcript,
    TextInsertionCapability? InsertionCapability = null,
    FocusRestoreResult? FocusRestoreResult = null,
    TextInsertionResult? InsertionResult = null,
    string? Message = null,
    Exception? Error = null)
{
    public bool Inserted => Outcome == DictationRunOutcome.Inserted && InsertionResult?.Inserted == true;

    public bool RequiresPreview => Outcome == DictationRunOutcome.PreviewRequired;

    public bool ShouldCopyToClipboard => Outcome == DictationRunOutcome.CopyOnly;

    public bool IsCancelled => Outcome == DictationRunOutcome.Cancelled;
}
