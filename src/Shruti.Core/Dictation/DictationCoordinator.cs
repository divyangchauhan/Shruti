using Shruti.Core.Audio;
using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;
using Shruti.Core.TranscriptQuality;

namespace Shruti.Core.Dictation;

public sealed class DictationCoordinator
{
    private readonly ITargetFocusService _targetFocusService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly ITranscriptQualityPipeline? _transcriptQualityPipeline;
    private readonly ISpeechActivityDetector? _speechActivityDetector;

    public DictationCoordinator(
        ITargetFocusService targetFocusService,
        IAudioCaptureService audioCaptureService,
        ITextInsertionService textInsertionService,
        ITranscriptionProvider transcriptionProvider,
        ITranscriptQualityPipeline? transcriptQualityPipeline = null,
        ISpeechActivityDetector? speechActivityDetector = null)
    {
        _targetFocusService = targetFocusService ?? throw new ArgumentNullException(nameof(targetFocusService));
        _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _textInsertionService = textInsertionService ?? throw new ArgumentNullException(nameof(textInsertionService));
        _transcriptionProvider = transcriptionProvider ?? throw new ArgumentNullException(nameof(transcriptionProvider));
        _transcriptQualityPipeline = transcriptQualityPipeline;
        _speechActivityDetector = speechActivityDetector;
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
        Task? transcriptEventTask = null;
        ITranscriptQualitySession? qualitySession = null;
        bool qualityStartFailed = false;
        var maximumAudioDurationReached = false;
        using var speechAudio = new MemoryStream();

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

            if (_transcriptQualityPipeline is not null)
            {
                try
                {
                    qualitySession = await _transcriptQualityPipeline.BeginAsync(target, request.QualitySettings,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    qualityStartFailed = true;
                    System.Diagnostics.Trace.TraceWarning("Transcript quality session unavailable; using raw output.");
                }
            }

            transcriptionSession = await _transcriptionProvider
                .CreateSessionAsync(request.TranscriptionOptions with
                { RecognitionHints = qualitySession?.RecognitionHints ?? request.TranscriptionOptions.RecognitionHints }, cancellationToken)
                .ConfigureAwait(false);

            if (request.TranscriptProgress is not null)
            {
                transcriptEventTask = ForwardTranscriptEventsAsync(
                    transcriptionSession.Events,
                    request.TranscriptProgress);
            }

            Transition(DictationSessionState.RequestingMicrophone, "Starting microphone capture");
            audioCaptureSession = await _audioCaptureService
                .StartAsync(request.AudioOptions, transcriptionSession.RequiredInputFormat, cancellationToken)
                .ConfigureAwait(false);

            request.CaptureSessionStarted?.Invoke(audioCaptureSession);

            Transition(DictationSessionState.Recording, "Recording");
            await foreach (var frame in audioCaptureSession.Frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                TranscriptionAudioPushResult pushResult = await transcriptionSession
                    .PushAudioAsync(frame.PcmAudio, cancellationToken)
                    .ConfigureAwait(false);

                long maximumBytes = checked((long)Math.Ceiling(request.TranscriptionOptions.EffectiveMaximumAudioDuration.TotalSeconds *
                    transcriptionSession.RequiredInputFormat.SampleRateHz * sizeof(short)));
                int accepted = checked((int)Math.Min(frame.PcmAudio.Length, Math.Max(0, maximumBytes - speechAudio.Length)));
                speechAudio.Write(frame.PcmAudio.Span[..accepted]);

                if (pushResult.ReachedMaximumDuration)
                {
                    maximumAudioDurationReached = true;
                    break;
                }
            }

            await audioCaptureSession.StopAsync(cancellationToken).ConfigureAwait(false);

            ReadOnlyMemory<byte> capturedAudio = speechAudio.GetBuffer().AsMemory(0, checked((int)speechAudio.Length));
            if (capturedAudio.IsEmpty || !Pcm16Signal.HasSignal(capturedAudio.Span) ||
                (_speechActivityDetector is not null &&
                    !await _speechActivityDetector.ContainsSpeechAsync(capturedAudio, cancellationToken).ConfigureAwait(false)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await transcriptionSession.CancelAsync().ConfigureAwait(false);
                return CompleteWithoutSpeech(target, statusHistory, Transition);
            }

            Transition(
                DictationSessionState.TranscribingFinalAudio,
                maximumAudioDurationReached
                    ? "Recording limit reached; finalizing captured audio"
                    : "Transcribing final audio");
            var transcript = await transcriptionSession.CompleteAsync(cancellationToken).ConfigureAwait(false);
            if (transcriptEventTask is not null)
            {
                await transcriptEventTask.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            RefinedTranscript refined = RefinedTranscript.Unchanged(transcript, qualityStartFailed);
            if (qualitySession is not null && !TranscriptText.IsEmptyOrNonSpeech(transcript.Text))
            {
                Transition(DictationSessionState.RefiningTranscript, "Refining transcript");
                using var refinementCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                refinementCancellation.CancelAfter(request.QualitySettings.RefinementTimeout);
                try
                {
                    refined = await qualitySession.RefineAsync(transcript, refinementCancellation.Token)
                        .WaitAsync(refinementCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    refined = RefinedTranscript.Unchanged(transcript, fallback: true);
                    System.Diagnostics.Trace.TraceWarning("Transcript refinement failed; using raw output.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await CompleteAsync(
                request,
                target,
                TranscriptResult.FromText(refined.Text),
                statusHistory,
                Transition,
                cancellationToken).ConfigureAwait(false);

            return result with
            {
                RefinedTranscript = refined,
                AudioDuration = TimeSpan.FromSeconds((double)speechAudio.Length / (transcriptionSession.RequiredInputFormat.SampleRateHz * sizeof(short)))
            };
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
            if (qualitySession is not null)
            {
                try { await qualitySession.DisposeAsync().ConfigureAwait(false); }
                catch { System.Diagnostics.Trace.TraceWarning("Transcript quality session disposal failed."); }
            }
            if (audioCaptureSession is not null)
            {
                await audioCaptureSession.DisposeAsync().ConfigureAwait(false);
            }

            if (transcriptionSession is not null)
            {
                await transcriptionSession.DisposeAsync().ConfigureAwait(false);
            }

            if (transcriptEventTask is not null)
            {
                try
                {
                    await transcriptEventTask.ConfigureAwait(false);
                }
                catch
                {
                    // The primary workflow has already translated its result or failure.
                }
            }
        }
    }

    public Task<DictationRunResult> InsertFinalizedTextAsync(
        FocusTarget target, string text, TextInsertionOptions? insertionOptions,
        IProgress<DictationStatus>? statusProgress, CancellationToken cancellationToken)
        => InsertFinalizedTranscriptAsync(target, TranscriptResult.FromText(text), insertionOptions, statusProgress, cancellationToken);

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
        if (TranscriptText.IsEmptyOrNonSpeech(transcript.Text))
            return CompleteWithoutSpeech(target, statusHistory, transition);

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
        if (TranscriptText.IsEmptyOrNonSpeech(transcript.Text))
            return CompleteWithoutSpeech(target, statusHistory, transition);

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

        transition(DictationSessionState.Complete, insertionResult.Succeeded ? "Complete" : "Preview required");

        if (insertionResult.Succeeded)
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

    private static DictationRunResult CompleteWithoutSpeech(
        FocusTarget? target,
        IReadOnlyList<DictationStatus> statusHistory,
        Action<DictationSessionState, string> transition)
    {
        transition(DictationSessionState.Complete, "No speech detected");
        return new DictationRunResult(DictationRunOutcome.NoSpeech, statusHistory, target,
            TranscriptResult.FromText(string.Empty), Message: "No speech detected. Nothing was inserted.");
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

    private static async Task ForwardTranscriptEventsAsync(
        IAsyncEnumerable<TranscriptEvent> events,
        IProgress<TranscriptEvent> progress)
    {
        await foreach (TranscriptEvent transcriptEvent in events.ConfigureAwait(false))
        {
            if (transcriptEvent.Kind != TranscriptEventKind.PartialText)
                progress.Report(transcriptEvent);
        }
    }
}
