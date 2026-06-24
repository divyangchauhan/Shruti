using Shruti.Core;
using Shruti.Core.Diagnostics;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;
using Shruti.Storage;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Snapshot_OmitsTranscriptAndWindowTitleByDefault()
    {
        var result = new DictationRunResult(
            DictationRunOutcome.Failed,
            [new DictationStatus(DictationSessionState.Failed, "Failed")],
            new FocusTarget(
                IntPtr.Zero,
                ProcessId: 42,
                ProcessName: "notepad",
                WindowTitle: "Email to divya@example.com about launch numbers"),
            TranscriptResult.FromText("secret transcript that must not appear in diagnostics"),
            Message: "Dictation failed.");

        RedactedDiagnosticsSnapshot snapshot = RedactedDiagnosticsSnapshot.FromResult(result);

        Assert.Equal("notepad", snapshot.TargetProcessName);
        Assert.Null(snapshot.TargetWindowTitle);
        Assert.True(snapshot.TargetWindowTitleRedacted);
        Assert.Null(snapshot.TranscriptText);
        Assert.True(snapshot.TranscriptTextRedacted);
        Assert.Equal(53, snapshot.TranscriptCharacterCount);
        Assert.DoesNotContain("secret transcript", snapshot.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("divya@example.com", snapshot.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Snapshot_RedactsErrorDetailsWhenExplicitlyIncluded()
    {
        var result = new DictationRunResult(
            DictationRunOutcome.Failed,
            [new DictationStatus(DictationSessionState.Failed, "Failed")],
            Target: null,
            Transcript: null,
            Error: new InvalidOperationException(
                "Could not open C:\\Users\\divya\\models\\tiny.bin for divya@example.com from https://models.example/tiny.bin"));

        RedactedDiagnosticsSnapshot snapshot = RedactedDiagnosticsSnapshot.FromResult(
            result,
            new DiagnosticSnapshotOptions(IncludeErrorDetails: true));

        Assert.NotNull(snapshot.ErrorDetails);
        Assert.Contains("[user path]", snapshot.ErrorDetails, StringComparison.Ordinal);
        Assert.Contains("[email]", snapshot.ErrorDetails, StringComparison.Ordinal);
        Assert.Contains("[url]", snapshot.ErrorDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("divya@example.com", snapshot.ErrorDetails, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\divya", snapshot.ErrorDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogEntry_RedactsMessageAndProperties()
    {
        DiagnosticLogEntry entry = DiagnosticLogEntry.Create(
            DateTimeOffset.UnixEpoch,
            DiagnosticLogLevel.Error,
            "test",
            "Failed at C:\\Users\\divya\\audio\\sample.wav for divya@example.com",
            new Dictionary<string, string?>
            {
                ["download"] = "https://models.example/file.bin",
                ["note"] = "Contact divya@example.com"
            });

        Assert.Equal("Failed at [user path] for [email]", entry.Message);
        Assert.Equal("[url]", entry.Properties["download"]);
        Assert.Equal("Contact [email]", entry.Properties["note"]);
    }

    [Fact]
    public void FailureText_MapsInsertionFailureToRecoveryGuidance()
    {
        var result = new DictationRunResult(
            DictationRunOutcome.Failed,
            [new DictationStatus(DictationSessionState.Failed, "Failed")],
            new FocusTarget(
                IntPtr.Zero,
                ProcessId: 42,
                ProcessName: "target-app",
                WindowTitle: "Draft"),
            Transcript: null,
            InsertionResult: new TextInsertionResult(
                Inserted: false,
                TextInsertionMethod.DirectInput,
                "SendInput could not insert into target."));

        string guidance = DiagnosticFailureText.ForDictationResult(result);

        Assert.Contains("Text insertion failed", guidance, StringComparison.Ordinal);
        Assert.Contains("Preview first", guidance, StringComparison.Ordinal);
        Assert.Contains("Copy", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureText_PrioritizesInsertionFailureOverNormalMicrophoneHistory()
    {
        var result = new DictationRunResult(
            DictationRunOutcome.PreviewRequired,
            [
                new DictationStatus(DictationSessionState.RequestingMicrophone, "Starting microphone capture"),
                new DictationStatus(DictationSessionState.Complete, "Preview required")
            ],
            new FocusTarget(
                IntPtr.Zero,
                ProcessId: 42,
                ProcessName: "target-app",
                WindowTitle: "Draft"),
            TranscriptResult.FromText("hello"),
            InsertionResult: new TextInsertionResult(
                Inserted: false,
                TextInsertionMethod.DirectInput,
                "SendInput could not insert into target."));

        string guidance = DiagnosticFailureText.ForDictationResult(result);

        Assert.Contains("Text insertion failed", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("Microphone unavailable", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureText_MapsReadinessFailuresToRecoveryGuidance()
    {
        var missingModel = new TranscriptionReadinessResult(
            TranscriptionReadinessStatus.Unsupported,
            "Model file C:\\Users\\divya\\models\\tiny.bin was not found.");
        var unsupportedHardware = new TranscriptionReadinessResult(
            TranscriptionReadinessStatus.Unsupported,
            "Provider C:\\Users\\divya\\providers\\gpu.dll does not expose Gpu.",
            Warnings: ["See https://shruti.local/help for divya@example.com"]);

        string missingModelGuidance = DiagnosticFailureText.ForReadiness(missingModel);
        string unsupportedHardwareGuidance = DiagnosticFailureText.ForReadiness(unsupportedHardware);

        Assert.Contains("Model unavailable", missingModelGuidance, StringComparison.Ordinal);
        Assert.Contains("Choose Auto or CPU", unsupportedHardwareGuidance, StringComparison.Ordinal);
        Assert.Contains("smaller supported model", unsupportedHardwareGuidance, StringComparison.Ordinal);
        Assert.Contains("[user path]", unsupportedHardwareGuidance, StringComparison.Ordinal);
        Assert.Contains("[url]", unsupportedHardwareGuidance, StringComparison.Ordinal);
        Assert.Contains("[email]", unsupportedHardwareGuidance, StringComparison.Ordinal);
        Assert.DoesNotContain("divya@example.com", unsupportedHardwareGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnosticLogWriter_AppendsRedactedJsonLines()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppDataPaths(rootPath);
            var writer = new JsonDiagnosticLogWriter(paths);
            var entry = new DiagnosticLogEntry(
                DateTimeOffset.UnixEpoch,
                DiagnosticLogLevel.Error,
                "dictation",
                "Failed at C:\\Users\\divya\\audio\\sample.wav for divya@example.com",
                new Dictionary<string, string>
                {
                    ["url"] = "https://models.example/file.bin",
                    ["transcript"] = "transcript length only"
                });

            await writer.AppendAsync(entry, CancellationToken.None);

            string logText = await File.ReadAllTextAsync(paths.DiagnosticLogFilePath, CancellationToken.None);
            Assert.Contains("[user path]", logText, StringComparison.Ordinal);
            Assert.Contains("[email]", logText, StringComparison.Ordinal);
            Assert.Contains("[url]", logText, StringComparison.Ordinal);
            Assert.DoesNotContain("divya@example.com", logText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\Users\\divya", logText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
