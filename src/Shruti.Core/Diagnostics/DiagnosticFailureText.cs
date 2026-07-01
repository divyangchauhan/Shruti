using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Transcription.Abstractions;

namespace Shruti.Core.Diagnostics;

public static class DiagnosticFailureText
{
    public static string MicrophoneRecovery { get; } =
        "Microphone unavailable. Check Windows Settings > Privacy & security > Microphone, confirm Shruti and desktop apps are allowed, then reconnect or choose a microphone and retry.";

    public static string MissingModelRecovery { get; } =
        "Model unavailable. Install or select a local speech model in Models, then retry once the model is present on this device.";

    public static string UnsupportedHardwareRecovery { get; } =
        "This model/backend is not ready on this hardware. Choose Auto or CPU, install a smaller supported model, or enable slow mode only if slower-than-real-time transcription is acceptable.";

    public static string TargetInsertionRecovery { get; } =
        "Text insertion failed. Return to the target field, confirm it accepts typing, then retry with Preview first or use Copy if the app blocks insertion.";

    public static string ForDictationResult(DictationRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string combined = string.Join(
            " ",
            result.Message,
            result.Error?.Message,
            result.InsertionResult?.Message,
            result.FocusRestoreResult?.Message);

        if (LooksLikeMissingModel(combined))
        {
            return MissingModelRecovery;
        }

        if (result.InsertionResult is { Submitted: true, Inserted: false })
        {
            string submittedMessage = DiagnosticTextRedactor.Redact(
                result.InsertionResult.Message ?? result.Message);
            return string.IsNullOrWhiteSpace(submittedMessage)
                ? "Paste was submitted but insertion could not be confirmed. The transcript remains available for manual paste."
                : submittedMessage;
        }

        if (LooksLikeInsertionFailure(combined, result))
        {
            return result.Target?.IsElevated == true
                ? "Text insertion failed because the target may be elevated. Start Shruti with matching permissions or use Copy/Preview first for that app."
                : TargetInsertionRecovery;
        }

        if (LooksLikeMicrophoneFailure(combined, result))
        {
            return MicrophoneRecovery;
        }

        if (result.Outcome == DictationRunOutcome.Failed)
        {
            return "Dictation failed. Try again, then check the diagnostics snapshot if the problem repeats.";
        }

        string message = DiagnosticTextRedactor.Redact(result.Message);
        return string.IsNullOrWhiteSpace(message)
            ? "Dictation finished."
            : message;
    }

    public static string ForReadiness(TranscriptionReadinessResult readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        string message = DiagnosticTextRedactor.Redact(readiness.Message);
        string warnings = readiness.EffectiveWarnings.Count == 0
            ? string.Empty
            : $" {string.Join(" ", readiness.EffectiveWarnings.Select(DiagnosticTextRedactor.Redact))}";

        return readiness.Status switch
        {
            TranscriptionReadinessStatus.Ready => $"{message}{warnings}",
            TranscriptionReadinessStatus.SlowModeRequired => $"{message} {UnsupportedHardwareRecovery}{warnings}",
            TranscriptionReadinessStatus.Unsupported when LooksLikeMissingModel(message) => MissingModelRecovery,
            TranscriptionReadinessStatus.Unsupported => $"{message} {UnsupportedHardwareRecovery}{warnings}",
            _ => message
        };
    }

    private static bool LooksLikeMicrophoneFailure(string text, DictationRunResult result)
    {
        return ContainsAny(text, "microphone", "audio input", "capture device", "wasapi") ||
            result.StatusHistory.Any(status => status.State == DictationSessionState.RequestingMicrophone);
    }

    private static bool LooksLikeMissingModel(string text)
    {
        return ContainsAny(text, "no model", "model unavailable") ||
            (ContainsAny(text, "model", ".bin", ".gguf", ".ggml") &&
                ContainsAny(text, "not found", "missing", "file", "unavailable", "open"));
    }

    private static bool LooksLikeInsertionFailure(string text, DictationRunResult result)
    {
        if (result.Outcome == DictationRunOutcome.Inserted &&
            result.InsertionResult?.Succeeded == true &&
            result.FocusRestoreResult?.Restored is not false)
        {
            return false;
        }

        return result.InsertionResult?.Succeeded == false ||
            result.FocusRestoreResult?.Restored == false ||
            ContainsAny(text, "insert", "target", "focus", "clipboard", "typing", "elevated");
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
