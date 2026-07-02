using Shruti.Core;
using Shruti.Core.Audio;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;

namespace Shruti.Workflow.Dictation;

public sealed class DictationShellController
{
    private readonly DictationCoordinator _coordinator;
    private readonly IAudioCaptureControl _audioCaptureControl;
    private readonly ITranscriptClipboard _clipboard;
    private readonly Func<CancellationToken, Task<TranscriptionSessionOptions>> _transcriptionOptionsFactory;
    private readonly object _stateSync = new();
    private readonly object _runSync = new();

    private CancellationTokenSource? _levelMonitorCancellation;
    private ActiveDictationRun? _activeRun;
    private Task? _levelMonitorTask;
    private AudioCaptureOptions _audioOptions = new();
    private DictationShellState _state = DictationShellState.Initial;

    public DictationShellController(
        DictationCoordinator coordinator,
        IAudioCaptureControl audioCaptureControl,
        ITranscriptClipboard clipboard,
        Func<CancellationToken, Task<TranscriptionSessionOptions>>? transcriptionOptionsFactory = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _audioCaptureControl = audioCaptureControl ?? throw new ArgumentNullException(nameof(audioCaptureControl));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _transcriptionOptionsFactory = transcriptionOptionsFactory ?? MockDictationAppServices.CreateTranscriptionOptionsAsync;
        State = DictationShellState.Initial;
    }

    public event EventHandler? StateChanged;

    public event EventHandler<AudioLevelFrame>? AudioLevelChanged;

    public DictationShellState State
    {
        get
        {
            lock (_stateSync)
            {
                return _state;
            }
        }
        private set
        {
            lock (_stateSync)
            {
                _state = value;
            }
        }
    }

    public DictationRunResult? LastResult { get; private set; }

    public AudioCaptureOptions AudioOptions => _audioOptions;

    public async Task StartAsync(DictationInsertionMode insertionMode)
    {
        if (State.IsRunning)
        {
            return;
        }

        ActiveDictationRun? activeRun = TryCreateActiveRun();
        if (activeRun is null)
        {
            return;
        }

        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SetState(new DictationShellState(
            DictationSessionState.PreparingTarget,
            insertionMode,
            "Starting dictation",
            "Capturing the target before recording.",
            string.Empty,
            "Target pending",
            IsRunning: true,
            CanStart: false,
            CanStop: true,
            CanCancel: true,
            CanPause: false,
            IsPaused: false,
            CanRetry: false,
            CanCopy: false));

        Task runTask = RunOnceAsync(insertionMode, activeRun, captureStarted);
        activeRun.SetCompletion(runTask);

        Task readyOrComplete = await Task.WhenAny(captureStarted.Task, runTask).ConfigureAwait(false);
        await readyOrComplete.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        ActiveDictationRun? activeRun = GetActiveRun();
        if (activeRun is null)
        {
            SetIdleMessage("No active dictation to stop.");
            return;
        }

        UpdateState(state => state with
        {
            StatusText = "Stopping recording",
            UserMessage = "Finalizing captured audio.",
            CanStop = false,
            CanPause = false,
            IsPaused = false
        });

        await _audioCaptureControl.StopActiveCaptureAsync().ConfigureAwait(false);
        await activeRun.WaitForCompletionAsync().ConfigureAwait(false);
    }

    public async Task CancelAsync()
    {
        ActiveDictationRun? activeRun = GetActiveRun();
        if (activeRun is null || !activeRun.TryCancel())
        {
            SetIdleMessage("No active dictation to cancel.");
            return;
        }

        UpdateState(state => state with
        {
            StatusText = "Cancelling dictation",
            UserMessage = "No text will be inserted.",
            CanStop = false,
            CanCancel = false,
            CanPause = false,
            IsPaused = false
        });

        await _audioCaptureControl.StopActiveCaptureAsync().ConfigureAwait(false);
        await activeRun.WaitForCompletionAsync().ConfigureAwait(false);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (!State.IsRunning)
        {
            SetIdleMessage("No active dictation to pause.");
            return;
        }

        if (State.SessionState == DictationSessionState.Paused)
        {
            await _audioCaptureControl.ResumeActiveCaptureAsync(cancellationToken).ConfigureAwait(false);
            UpdateState(state => state with
            {
                SessionState = DictationSessionState.Recording,
                StatusText = "Recording",
                UserMessage = "Recording audio.",
                CanStop = true,
                CanCancel = true,
                CanPause = true,
                IsPaused = false
            });
            return;
        }

        if (State.SessionState != DictationSessionState.Recording)
        {
            UpdateState(state => state with
            {
                StatusText = "Pause unavailable",
                UserMessage = "Pause is available only while recording is active."
            });
            return;
        }

        await _audioCaptureControl.PauseActiveCaptureAsync(cancellationToken).ConfigureAwait(false);
        UpdateState(state => state with
        {
            SessionState = DictationSessionState.Paused,
            StatusText = "Paused",
            UserMessage = "Recording is paused.",
            CanStop = true,
            CanCancel = true,
            CanPause = true,
            IsPaused = true
        });
    }

    public Task RetryAsync()
    {
        if (State.IsRunning)
        {
            return Task.CompletedTask;
        }

        return StartAsync(State.InsertionMode);
    }

    public void SetInsertionMode(DictationInsertionMode insertionMode)
    {
        if (State.IsRunning)
        {
            return;
        }

        UpdateState(state => state with
        {
            InsertionMode = insertionMode,
            StatusText = "Ready",
            UserMessage = DescribeInsertionMode(insertionMode)
        });
    }

    public void SetAudioInputDevice(string? deviceId)
    {
        if (State.IsRunning)
        {
            return;
        }

        _audioOptions = _audioOptions with { DeviceId = deviceId };
    }

    public async Task<bool> CopyTranscriptAsync(CancellationToken cancellationToken = default)
    {
        string text = State.TranscriptPreview;
        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateState(state => state with
            {
                StatusText = "Nothing to copy",
                UserMessage = "Run dictation before copying a transcript."
            });
            return false;
        }

        try
        {
            await _clipboard.CopyTextAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UpdateState(state => state with
            {
                StatusText = "Copy failed",
                UserMessage = "Shruti could not copy the transcript.",
                ErrorText = ex.Message
            });
            return false;
        }

        UpdateState(state => state with
        {
            StatusText = "Copied",
            UserMessage = "Transcript copied from the preview.",
            ErrorText = null
        });

        return true;
    }

    public async Task InsertPreviewAsync(
        string text,
        bool allowReplacingSelection,
        CancellationToken cancellationToken = default)
    {
        if (State.IsRunning)
        {
            return;
        }

        DictationRunResult? previousResult = LastResult;
        if (!State.CanInsertPreview || previousResult?.Target is null || !previousResult.RequiresPreview)
        {
            SetIdleMessage("No transcript is ready for insertion.");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateState(state => state with
            {
                StatusText = "Transcript is empty",
                UserMessage = "Enter transcript text before inserting.",
                ErrorText = "Shruti will not insert an empty transcript."
            });
            return;
        }

        UpdateState(state => state with
        {
            SessionState = DictationSessionState.InsertingText,
            StatusText = "Inserting text",
            UserMessage = "Restoring the captured target and inserting the transcript.",
            TranscriptPreview = text,
            IsRunning = true,
            CanStart = false,
            CanStop = false,
            CanCancel = false,
            CanPause = false,
            IsPaused = false,
            CanRetry = false,
            CanCopy = false,
            CanInsertPreview = false,
            ErrorText = null
        });

        var progress = new SynchronousProgress<DictationStatus>(ApplyProgress);
        var options = new TextInsertionOptions(
            AllowReplacingSelection: allowReplacingSelection);
        DictationRunResult result = await _coordinator
            .InsertFinalizedTranscriptAsync(
                previousResult.Target,
                TranscriptResult.FromText(text),
                options,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        LastResult = result;
        SetState(CreateCompletedState(result, State.InsertionMode));
    }

    private async Task RunOnceAsync(
        DictationInsertionMode insertionMode,
        ActiveDictationRun activeRun,
        TaskCompletionSource captureStarted)
    {
        try
        {
            await Task.Yield();

            var progress = new SynchronousProgress<DictationStatus>(ApplyProgress);
            var transcriptProgress = new SynchronousProgress<TranscriptEvent>(ApplyTranscriptEvent);
            TranscriptionSessionOptions transcriptionOptions = await _transcriptionOptionsFactory(activeRun.Token)
                .ConfigureAwait(false);
            var request = new DictationRequest(
                transcriptionOptions,
                insertionMode,
                audioOptions: _audioOptions,
                statusProgress: progress,
                transcriptProgress: transcriptProgress,
                captureSessionStarted: session =>
                {
                    captureStarted.TrySetResult();

                    if (AudioLevelChanged is not null)
                    {
                        StartLevelMonitor(session);
                    }
                });

            var result = await _coordinator
                .RunOnceAsync(request, activeRun.Token)
                .ConfigureAwait(false);

            if (result.ShouldCopyToClipboard && !string.IsNullOrWhiteSpace(result.Transcript?.Text))
            {
                await _clipboard
                    .CopyTextAsync(result.Transcript.Text, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            LastResult = result;
            SetState(CreateCompletedState(result, insertionMode));
        }
        catch (Exception ex)
        {
            var result = new DictationRunResult(
                DictationRunOutcome.Failed,
                [new DictationStatus(DictationSessionState.Failed, "Failed")],
                Target: null,
                Transcript: null,
                Message: ex.Message,
                Error: ex);
            LastResult = result;
            SetState(CreateCompletedState(result, insertionMode));
        }
        finally
        {
            captureStarted.TrySetResult();
            await StopLevelMonitorAsync().ConfigureAwait(false);
            CompleteActiveRun(activeRun);
        }
    }

    private void StartLevelMonitor(IAudioCaptureSession session)
    {
        if (_levelMonitorTask is not null)
        {
            throw new InvalidOperationException("An audio-level monitor is already active.");
        }

        var cancellation = new CancellationTokenSource();
        _levelMonitorCancellation = cancellation;
        _levelMonitorTask = MonitorLevelsAsync(session, cancellation.Token);
    }

    private async Task MonitorLevelsAsync(
        IAudioCaptureSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AudioLevelFrame level in session.Levels
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                AudioLevelChanged?.Invoke(this, level);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The completed dictation run disposes its level monitor.
        }
        catch
        {
            // The coordinator reports capture failures through the dictation result.
        }
    }

    private async Task StopLevelMonitorAsync()
    {
        CancellationTokenSource? cancellation = _levelMonitorCancellation;
        Task? monitorTask = _levelMonitorTask;
        _levelMonitorCancellation = null;
        _levelMonitorTask = null;

        cancellation?.Cancel();
        if (monitorTask is not null)
        {
            await monitorTask.ConfigureAwait(false);
        }

        cancellation?.Dispose();
    }

    private void ApplyProgress(DictationStatus status)
    {
        UpdateState(state => state with
        {
            SessionState = status.State,
            StatusText = status.Message,
            UserMessage = DescribeActiveState(status.State),
            IsRunning = status.State is not DictationSessionState.Complete
                and not DictationSessionState.Cancelled
                and not DictationSessionState.Failed,
            CanStart = false,
            CanStop = status.State is DictationSessionState.Recording or DictationSessionState.Paused,
            CanCancel = HasActiveRun() && status.State is not DictationSessionState.Complete
                and not DictationSessionState.Cancelled
                and not DictationSessionState.Failed,
            CanPause = status.State is DictationSessionState.Recording or DictationSessionState.Paused,
            IsPaused = status.State == DictationSessionState.Paused
        });
    }

    private void ApplyTranscriptEvent(TranscriptEvent transcriptEvent)
    {
        if (transcriptEvent.Kind is not TranscriptEventKind.PartialText and not TranscriptEventKind.Completed ||
            string.IsNullOrWhiteSpace(transcriptEvent.Text))
        {
            return;
        }

        UpdateState(state => state with
        {
            TranscriptPreview = transcriptEvent.Text,
            ErrorText = null
        });
    }

    private static DictationShellState CreateCompletedState(
        DictationRunResult result,
        DictationInsertionMode insertionMode)
    {
        string transcript = result.Transcript?.Text ?? string.Empty;
        string target = FormatTarget(result.Target);
        string message = result.Outcome switch
        {
            DictationRunOutcome.Inserted => "Inserted into the target.",
            DictationRunOutcome.PreviewRequired => result.Message ?? "Preview is ready before insertion.",
            DictationRunOutcome.CopyOnly => "Copied transcript for copy-only mode.",
            DictationRunOutcome.Cancelled => "Cancelled. Nothing was inserted.",
            DictationRunOutcome.Failed => result.Message ?? "Dictation failed.",
            _ => result.Message ?? "Dictation finished."
        };

        return new DictationShellState(
            result.StatusHistory.LastOrDefault()?.State ?? DictationSessionState.Complete,
            insertionMode,
            result.Outcome == DictationRunOutcome.Failed ? "Failed" : "Complete",
            message,
            transcript,
            target,
            IsRunning: false,
            CanStart: true,
            CanStop: false,
            CanCancel: false,
            CanPause: false,
            IsPaused: false,
            CanRetry: result.Outcome is not DictationRunOutcome.Cancelled,
            CanCopy: !string.IsNullOrWhiteSpace(transcript),
            LastOutcome: result.Outcome,
            ErrorText: result.Error?.Message,
            CanInsertPreview: result.RequiresPreview &&
                result.Target is not null &&
                !string.IsNullOrWhiteSpace(transcript) &&
                result.InsertionResult?.Submitted is not true);
    }

    private static string FormatTarget(FocusTarget? target)
    {
        if (target is null)
        {
            return "No target captured";
        }

        if (string.IsNullOrWhiteSpace(target.WindowTitle))
        {
            return target.ProcessName;
        }

        return $"{target.ProcessName} - {target.WindowTitle}";
    }

    private static string DescribeActiveState(DictationSessionState state)
    {
        return state switch
        {
            DictationSessionState.PreparingTarget => "Capturing the target.",
            DictationSessionState.RequestingMicrophone => "Starting the microphone.",
            DictationSessionState.Recording => "Recording audio.",
            DictationSessionState.Paused => "Recording is paused.",
            DictationSessionState.TranscribingFinalAudio => "Generating the transcript.",
            DictationSessionState.InsertingText => "Restoring target and inserting text.",
            DictationSessionState.Complete => "Dictation complete.",
            DictationSessionState.Cancelled => "Dictation cancelled.",
            DictationSessionState.Failed => "Dictation failed.",
            _ => "Ready."
        };
    }

    private static string DescribeInsertionMode(DictationInsertionMode insertionMode)
    {
        return insertionMode switch
        {
            DictationInsertionMode.AutoInsert => "Auto insert will restore the captured target and insert the transcript.",
            DictationInsertionMode.PreviewFirst => "Preview first will stop before insertion.",
            DictationInsertionMode.CopyOnly => "Copy only will copy the transcript after transcription.",
            _ => "Ready."
        };
    }

    private void SetIdleMessage(string message)
    {
        UpdateState(state => state with
        {
            SessionState = DictationSessionState.Idle,
            StatusText = "Ready",
            UserMessage = message,
            IsRunning = false,
            CanStart = true,
            CanStop = false,
            CanCancel = false,
            CanPause = false,
            IsPaused = false
        });
    }

    private void SetState(DictationShellState state)
    {
        UpdateState(_ => state);
    }

    private void UpdateState(Func<DictationShellState, DictationShellState> update)
    {
        EventHandler? stateChanged;
        lock (_stateSync)
        {
            _state = update(_state);
            stateChanged = StateChanged;
        }

        stateChanged?.Invoke(this, EventArgs.Empty);
    }

    private ActiveDictationRun? TryCreateActiveRun()
    {
        lock (_runSync)
        {
            if (_activeRun is not null)
            {
                return null;
            }

            var activeRun = new ActiveDictationRun();
            _activeRun = activeRun;
            return activeRun;
        }
    }

    private ActiveDictationRun? GetActiveRun()
    {
        lock (_runSync)
        {
            return _activeRun;
        }
    }

    private bool HasActiveRun()
    {
        lock (_runSync)
        {
            return _activeRun is not null;
        }
    }

    private void CompleteActiveRun(ActiveDictationRun activeRun)
    {
        lock (_runSync)
        {
            if (ReferenceEquals(_activeRun, activeRun))
            {
                _activeRun = null;
            }
        }

        activeRun.Complete();
    }

    private sealed class ActiveDictationRun
    {
        private readonly object _sync = new();
        private readonly TaskCompletionSource<Task> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _cancellation;
        private int _cancellationCalls;
        private bool _isComplete;

        public ActiveDictationRun()
        {
            _cancellation = new CancellationTokenSource();
            Token = _cancellation.Token;
        }

        public CancellationToken Token { get; }

        public void SetCompletion(Task completion)
        {
            _completion.TrySetResult(completion);
        }

        public async Task WaitForCompletionAsync()
        {
            Task completion = await _completion.Task.ConfigureAwait(false);
            await completion.ConfigureAwait(false);
        }

        public bool TryCancel()
        {
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_isComplete || _cancellation is null)
                {
                    return false;
                }

                _cancellationCalls++;
                cancellation = _cancellation;
            }

            try
            {
                cancellation.Cancel();
                return true;
            }
            finally
            {
                CancellationTokenSource? cancellationToDispose = null;
                lock (_sync)
                {
                    _cancellationCalls--;
                    if (_isComplete && _cancellationCalls == 0)
                    {
                        cancellationToDispose = _cancellation;
                        _cancellation = null;
                    }
                }

                cancellationToDispose?.Dispose();
            }
        }

        public void Complete()
        {
            CancellationTokenSource? cancellationToDispose = null;
            lock (_sync)
            {
                if (_isComplete)
                {
                    return;
                }

                _isComplete = true;
                if (_cancellationCalls == 0)
                {
                    cancellationToDispose = _cancellation;
                    _cancellation = null;
                }
            }

            cancellationToDispose?.Dispose();
        }
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}
