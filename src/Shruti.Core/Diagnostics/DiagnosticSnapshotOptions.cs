namespace Shruti.Core.Diagnostics;

public sealed record DiagnosticSnapshotOptions(
    bool IncludeTranscriptText = false,
    bool IncludeTargetWindowTitle = false,
    bool IncludeErrorDetails = false)
{
    public static DiagnosticSnapshotOptions Redacted { get; } = new();
}
