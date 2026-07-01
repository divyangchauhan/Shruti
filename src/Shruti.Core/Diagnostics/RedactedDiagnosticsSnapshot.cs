using Shruti.Core;
using Shruti.Core.Dictation;

namespace Shruti.Core.Diagnostics;

public sealed record RedactedDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    DictationSessionState SessionState,
    DictationRunOutcome Outcome,
    string? TargetProcessName,
    string? TargetWindowTitle,
    bool TargetWindowTitleRedacted,
    bool TargetIsElevated,
    bool? TargetIsEditable,
    bool? TargetHasSelectedText,
    string? FocusTargetWindowHandle,
    string? FocusForegroundWindowBefore,
    string? FocusForegroundWindowAfter,
    bool? FocusRequestedForeground,
    int TranscriptCharacterCount,
    int TranscriptSegmentCount,
    string? TranscriptText,
    bool TranscriptTextRedacted,
    IReadOnlyDictionary<string, string?> InsertionDiagnostics,
    IReadOnlyList<DiagnosticLogEntry> StatusHistory,
    string FailureSummary,
    string? ErrorDetails)
{
    public static RedactedDiagnosticsSnapshot FromResult(
        DictationRunResult result,
        DiagnosticSnapshotOptions? options = null,
        DateTimeOffset? capturedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        DiagnosticSnapshotOptions effectiveOptions = options ?? DiagnosticSnapshotOptions.Redacted;
        DateTimeOffset effectiveCapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        string? transcriptText = result.Transcript?.Text;
        string? windowTitle = result.Target?.WindowTitle;
        string? errorDetails = result.Error?.Message ?? result.Message;

        return new RedactedDiagnosticsSnapshot(
            effectiveCapturedAt,
            result.StatusHistory.LastOrDefault()?.State ?? DictationSessionState.Failed,
            result.Outcome,
            result.Target?.ProcessName,
            effectiveOptions.IncludeTargetWindowTitle
                ? DiagnosticTextRedactor.Redact(windowTitle)
                : null,
            !effectiveOptions.IncludeTargetWindowTitle && !string.IsNullOrWhiteSpace(windowTitle),
            result.Target?.IsElevated ?? false,
            result.Target?.IsEditable,
            result.Target?.HasSelectedText,
            FormatWindowHandle(result.FocusRestoreResult?.TargetWindowHandle),
            FormatWindowHandle(result.FocusRestoreResult?.ForegroundWindowBefore),
            FormatWindowHandle(result.FocusRestoreResult?.ForegroundWindowAfter),
            result.FocusRestoreResult?.RequestedForeground,
            transcriptText?.Length ?? 0,
            result.Transcript?.Segments.Count ?? 0,
            effectiveOptions.IncludeTranscriptText ? transcriptText : null,
            !effectiveOptions.IncludeTranscriptText && !string.IsNullOrWhiteSpace(transcriptText),
            result.InsertionResult?.OperationalDiagnostics ?? new Dictionary<string, string?>(),
            CreateStatusEntries(result, effectiveCapturedAt),
            DiagnosticFailureText.ForDictationResult(result),
            effectiveOptions.IncludeErrorDetails
                ? DiagnosticTextRedactor.Redact(errorDetails)
                : null);
    }

    private static string? FormatWindowHandle(IntPtr? windowHandle)
    {
        if (windowHandle is null)
        {
            return null;
        }

        return windowHandle.Value == IntPtr.Zero
            ? "0x0"
            : $"0x{windowHandle.Value.ToInt64():X}";
    }

    private static IReadOnlyList<DiagnosticLogEntry> CreateStatusEntries(
        DictationRunResult result,
        DateTimeOffset capturedAt)
    {
        return result.StatusHistory
            .Select((status, index) => DiagnosticLogEntry.Create(
                capturedAt,
                status.State == DictationSessionState.Failed ? DiagnosticLogLevel.Error : DiagnosticLogLevel.Info,
                "dictation.status",
                status.Message,
                new Dictionary<string, string?>
                {
                    ["index"] = index.ToString(),
                    ["state"] = status.State.ToString()
                }))
            .ToArray();
    }
}
