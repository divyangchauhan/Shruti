using Shruti.Core;
using Shruti.Core.Dictation;

namespace Shruti.App.WinUI.Dictation;

public sealed record DictationShellState(
    DictationSessionState SessionState,
    DictationInsertionMode InsertionMode,
    string StatusText,
    string UserMessage,
    string TranscriptPreview,
    string TargetDescription,
    bool IsRunning,
    bool CanStart,
    bool CanStop,
    bool CanCancel,
    bool CanPause,
    bool IsPaused,
    bool CanRetry,
    bool CanCopy,
    DictationRunOutcome? LastOutcome = null,
    string? ErrorText = null)
{
    public static DictationShellState Initial { get; } = new(
        DictationSessionState.Idle,
        DictationInsertionMode.AutoInsert,
        "Ready",
        "Mock dictation is ready.",
        string.Empty,
        "No target captured",
        IsRunning: false,
        CanStart: true,
        CanStop: false,
        CanCancel: false,
        CanPause: false,
        IsPaused: false,
        CanRetry: false,
        CanCopy: false);
}
