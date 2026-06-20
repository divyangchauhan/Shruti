using Shruti.Core.Audio;
using Shruti.Core.Platform;
using Shruti.Core.Triggers;
using Shruti.Transcription.Abstractions;

namespace Shruti.Core.Dictation;

public sealed record DictationRequest
{
    public DictationRequest(
        TranscriptionSessionOptions transcriptionOptions,
        DictationInsertionMode insertionMode = DictationInsertionMode.AutoInsert,
        AudioCaptureOptions? audioOptions = null,
        TextInsertionOptions? insertionOptions = null,
        DictationTriggerEvent? trigger = null,
        IProgress<DictationStatus>? statusProgress = null,
        Action<IAudioCaptureSession>? captureSessionStarted = null)
    {
        TranscriptionOptions = transcriptionOptions;
        InsertionMode = insertionMode;
        AudioOptions = audioOptions ?? new AudioCaptureOptions();
        InsertionOptions = insertionOptions ?? new TextInsertionOptions();
        Trigger = trigger;
        StatusProgress = statusProgress;
        CaptureSessionStarted = captureSessionStarted;
    }

    public TranscriptionSessionOptions TranscriptionOptions { get; }

    public DictationInsertionMode InsertionMode { get; }

    public AudioCaptureOptions AudioOptions { get; }

    public TextInsertionOptions InsertionOptions { get; }

    public DictationTriggerEvent? Trigger { get; }

    public IProgress<DictationStatus>? StatusProgress { get; }

    public Action<IAudioCaptureSession>? CaptureSessionStarted { get; }

    public static DictationRequest AutoInsert(
        TranscriptionSessionOptions transcriptionOptions,
        AudioCaptureOptions? audioOptions = null,
        TextInsertionOptions? insertionOptions = null,
        DictationTriggerEvent? trigger = null,
        IProgress<DictationStatus>? statusProgress = null)
    {
        return new DictationRequest(
            transcriptionOptions,
            DictationInsertionMode.AutoInsert,
            audioOptions,
            insertionOptions,
            trigger,
            statusProgress);
    }

    public static DictationRequest PreviewFirst(
        TranscriptionSessionOptions transcriptionOptions,
        AudioCaptureOptions? audioOptions = null,
        DictationTriggerEvent? trigger = null,
        IProgress<DictationStatus>? statusProgress = null)
    {
        return new DictationRequest(
            transcriptionOptions,
            DictationInsertionMode.PreviewFirst,
            audioOptions,
            insertionOptions: null,
            trigger,
            statusProgress);
    }

    public static DictationRequest CopyOnly(
        TranscriptionSessionOptions transcriptionOptions,
        AudioCaptureOptions? audioOptions = null,
        DictationTriggerEvent? trigger = null,
        IProgress<DictationStatus>? statusProgress = null)
    {
        return new DictationRequest(
            transcriptionOptions,
            DictationInsertionMode.CopyOnly,
            audioOptions,
            insertionOptions: null,
            trigger,
            statusProgress);
    }
}
