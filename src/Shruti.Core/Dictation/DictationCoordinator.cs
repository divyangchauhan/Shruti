using Shruti.Core.Audio;
using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;

namespace Shruti.Core.Dictation;

public sealed class DictationCoordinator
{
    private readonly ITargetFocusService _targetFocusService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITranscriptionProvider _transcriptionProvider;

    public DictationCoordinator(
        ITargetFocusService targetFocusService,
        IAudioCaptureService audioCaptureService,
        ITextInsertionService textInsertionService,
        ITranscriptionProvider transcriptionProvider)
    {
        _targetFocusService = targetFocusService ?? throw new ArgumentNullException(nameof(targetFocusService));
        _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _textInsertionService = textInsertionService ?? throw new ArgumentNullException(nameof(textInsertionService));
        _transcriptionProvider = transcriptionProvider ?? throw new ArgumentNullException(nameof(transcriptionProvider));
    }

    public async Task<DictationRunResult> RunOnceAsync(
        DictationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var statusHistory = new List<DictationStatus>();
        FocusTarget? target = null;
        ITranscriptionSession? transcriptionSession = null;
        IAudioCaptureSession? audioCaptureSession = null;

        void Transition(DictationSessionState state, string message)
        {
            var status = new DictationStatus(state, message);
            statusHistory.Add(status);
            request.StatusProgress?.Report(status);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            Transition(DictationSessionState.PreparingTarget, "Capturing target");
            target = await _targetFocusService.CaptureCurrentTargetAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            transcriptionSession = await _transcriptionProvider
                .CreateSessionAsync(request.TranscriptionOptions, cancellationToken)
                .ConfigureAwait(false);

            Transition(DictationSessionState.RequestingMicrophone, "Starting microphone capture");
            audioCaptureSession = await _audioCaptureService
                .StartAsync(request.AudioOptions, transcriptionSession.RequiredInputFormat, cancellationToken)
                .ConfigureAwait(false);

            request.CaptureSessionStarted?.Invoke(audioCaptureSession);

            Transition(DictationSessionState.Recording, "Recording");
            await foreach (var frame in audioCaptureSession.Frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await transcriptionSession
                    .PushAudioAsync(frame.PcmAudio, cancellationToken)
                    .ConfigureAwait(false);
            }

            await audioCaptureSession.StopAsync(cancellationToken).ConfigureAwait(false);

            Transition(DictationSessionState.TranscribingFinalAudio, "Transcribing final audio");
            var transcript = await transcriptionSession.CompleteAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await CompleteAsync(
                request,
                target,
                transcript,
                statusHistory,
                Transition,
                cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            await CancelSessionAsync(audioCaptureSession, transcriptionSession).ConfigureAwait(false);

            Transition(DictationSessionState.Cancelled, "Cancelled");
            return new DictationRunResult(
                DictationRunOutcome.Cancelled,
                statusHistory,
                target,
                Transcript: null,
                Message: "Dictation was cancelled.");
        }
        catch (Exception ex)
        {
            Transition(DictationSessionState.Failed, "Failed");
            return new DictationRunResult(
                DictationRunOutcome.Failed,
                statusHistory,
                target,
                Transcript: null,
                Message: ex.Message,
                Error: ex);
        }
        finally
        {
            if (audioCaptureSession is not null)
            {
                await audioCaptureSession.DisposeAsync().ConfigureAwait(false);
            }

            if (transcriptionSession is not null)
            {
                await transcriptionSession.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<DictationRunResult> InsertFinalizedTranscriptAsync(
        FocusTarget target,
        TranscriptResult transcript,
        TextInsertionOptions? insertionOptions,
        IProgress<DictationStatus>? statusProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(transcript);

        var statusHistory = new List<DictationStatus>();

        void Transition(DictationSessionState state, string message)
        {
            var status = new DictationStatus(state, message);
            statusHistory.Add(status);
            statusProgress?.Report(status);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await InsertFinalizedTranscriptCoreAsync(
                    target,
                    transcript,
                    insertionOptions ?? new TextInsertionOptions(),
                    statusHistory,
                    Transition,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Transition(DictationSessionState.Cancelled, "Cancelled");
            return new DictationRunResult(
                DictationRunOutcome.Cancelled,
                statusHistory,
                target,
                transcript,
                Message: "Transcript insertion was cancelled.");
        }
        catch (Exception ex)
        {
            Transition(DictationSessionState.Failed, "Failed");
            return new DictationRunResult(
                DictationRunOutcome.Failed,
                statusHistory,
                target,
                transcript,
                Message: ex.Message,
                Error: ex);
        }
    }

    private async Task<DictationRunResult> CompleteAsync(
        DictationRequest request,
        FocusTarget? target,
        TranscriptResult transcript,
        IReadOnlyList<DictationStatus> statusHistory,
        Action<DictationSessionState, string> transition,
        CancellationToken cancellationToken)
    {
        if (request.InsertionMode == DictationInsertionMode.PreviewFirst)
        {
            transition(DictationSessionState.Complete, "Preview required");
            return new DictationRunResult(
                DictationRunOutcome.PreviewRequired,
                statusHistory,
                target,
                transcript,
                Message: "Preview before insertion is enabled.");
        }

        if (request.InsertionMode == DictationInsertionMode.CopyOnly)
        {
            transition(DictationSessionState.Complete, "Copy only");
            return new DictationRunResult(
                DictationRunOutcome.CopyOnly,
                statusHistory,
                target,
                transcript,
                Message: "Copy-only mode is enabled.");
        }

        return await InsertFinalizedTranscriptCoreAsync(
                target,
                transcript,
                request.InsertionOptions,
                statusHistory,
                transition,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DictationRunResult> InsertFinalizedTranscriptCoreAsync(
        FocusTarget? target,
        TranscriptResult transcript,
        TextInsertionOptions insertionOptions,
        IReadOnlyList<DictationStatus> statusHistory,
        Action<DictationSessionState, string> transition,
        CancellationToken cancellationToken)
    {
        if (target is null)
        {
            transition(DictationSessionState.Complete, "Preview required");
            return new DictationRunResult(
                DictationRunOutcome.PreviewRequired,
                statusHistory,
                target,
                transcript,
                Message: "No target was available for insertion.");
        }

        var capability = await _textInsertionService.InspectAsync(target, cancellationToken).ConfigureAwait(false);
        if (!CanAutoInsert(capability, insertionOptions))
        {
            transition(DictationSessionState.Complete, "Preview required");
            return new DictationRunResult(
                DictationRunOutcome.PreviewRequired,
                statusHistory,
                target,
                transcript,
                InsertionCapability: capability,
                Message: capability.Message ?? "Target requires preview before insertion.");
        }

        transition(DictationSessionState.InsertingText, "Inserting text");

        var focusRestoreResult = await _targetFocusService.RestoreAsync(target, cancellationToken).ConfigureAwait(false);
        if (!focusRestoreResult.Restored)
        {
            transition(DictationSessionState.Complete, "Preview required");
            return new DictationRunResult(
                DictationRunOutcome.PreviewRequired,
                statusHistory,
                target,
                transcript,
                InsertionCapability: capability,
                FocusRestoreResult: focusRestoreResult,
                Message: focusRestoreResult.Message ?? "Target focus could not be restored.");
        }

        var insertionResult = await _textInsertionService
            .InsertAsync(target, transcript.Text, insertionOptions, cancellationToken)
            .ConfigureAwait(false);

        transition(DictationSessionState.Complete, insertionResult.Inserted ? "Complete" : "Preview required");

        if (insertionResult.Inserted)
        {
            return new DictationRunResult(
                DictationRunOutcome.Inserted,
                statusHistory,
                target,
                transcript,
                InsertionCapability: capability,
                FocusRestoreResult: focusRestoreResult,
                InsertionResult: insertionResult,
                Message: insertionResult.Message);
        }

        return new DictationRunResult(
            DictationRunOutcome.PreviewRequired,
            statusHistory,
            target,
            transcript,
            InsertionCapability: capability,
            FocusRestoreResult: focusRestoreResult,
            InsertionResult: insertionResult,
            Message: insertionResult.Message ?? "Text insertion did not complete.");
    }

    private static bool CanAutoInsert(
        TextInsertionCapability capability,
        TextInsertionOptions options)
    {
        return capability.Outcome switch
        {
            TextInsertionCapabilityOutcome.DirectInputAvailable => capability.PreferredMethod != TextInsertionMethod.None,
            TextInsertionCapabilityOutcome.ClipboardFallbackOnly => options.AllowClipboardFallback &&
                capability.PreferredMethod == TextInsertionMethod.ClipboardPaste,
            _ => false
        };
    }

    private static async Task CancelSessionAsync(
        IAudioCaptureSession? audioCaptureSession,
        ITranscriptionSession? transcriptionSession)
    {
        if (audioCaptureSession is not null)
        {
            try
            {
                await audioCaptureSession.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup after cancellation.
            }
        }

        if (transcriptionSession is not null)
        {
            try
            {
                await transcriptionSession.CancelAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup after cancellation.
            }
        }
    }
}
