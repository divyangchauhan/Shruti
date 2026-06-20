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
    private readonly Func<TranscriptionSessionOptions> _transcriptionOptionsFactory;

    private CancellationTokenSource? _activeCancellation;
    private CancellationTokenSource? _levelMonitorCancellation;
    private Task? _activeRun;
    private Task? _levelMonitorTask;
    private AudioCaptureOptions _audioOptions = new();

    public DictationShellController(
        DictationCoordinator coordinator,
        IAudioCaptureControl audioCaptureControl,
        ITranscriptClipboard clipboard,
        Func<TranscriptionSessionOptions>? transcriptionOptionsFactory = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _audioCaptureControl = audioCaptureControl ?? throw new ArgumentNullException(nameof(audioCaptureControl));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _transcriptionOptionsFactory = transcriptionOptionsFactory ?? MockDictationAppServices.CreateTranscriptionOptions;
        State = DictationShellState.Initial;
    }

    public event EventHandler? StateChanged;

    public event EventHandler<AudioLevelFrame>? AudioLevelChanged;

    public DictationShellState State { get; private set; }

    public DictationRunResult? LastResult { get; private set; }

    public AudioCaptureOptions AudioOptions => _audioOptions;

    public async Task StartAsync(DictationInsertionMode insertionMode)
    {
        if (State.IsRunning)
        {
            return;
        }

        _activeCancellation = new CancellationTokenSource();
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

        Task activeRun = RunOnceAsync(insertionMode, _activeCancellation.Token, captureStarted);
        _activeRun = activeRun;

        Task readyOrComplete = await Task.WhenAny(captureStarted.Task, activeRun).ConfigureAwait(false);
        await readyOrComplete.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        Task? activeRun = _activeRun;
        if (activeRun is null)
        {
            SetIdleMessage("No active mock dictation to stop.");
            return;
        }

        SetState(State with
        {
            StatusText = "Stopping recording",
            UserMessage = "Finalizing captured audio.",
            CanStop = false,
            CanPause = false,
            IsPaused = false
        });

        await _audioCaptureControl.StopActiveCaptureAsync().ConfigureAwait(false);
        await activeRun.ConfigureAwait(false);
    }

    public async Task CancelAsync()
    {
        Task? activeRun = _activeRun;
        if (activeRun is null || _activeCancellation is null)
        {
            SetIdleMessage("No active mock dictation to cancel.");
            return;
        }

        SetState(State with
        {
            StatusText = "Cancelling dictation",
            UserMessage = "No text will be inserted.",
            CanStop = false,
            CanCancel = false,
            CanPause = false,
            IsPaused = false
        });

        _activeCancellation.Cancel();
        await _audioCaptureControl.StopActiveCaptureAsync().ConfigureAwait(false);
        await activeRun.ConfigureAwait(false);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (!State.IsRunning)
        {
            SetIdleMessage("No active mock dictation to pause.");
            return;
        }

        if (State.SessionState == DictationSessionState.Paused)
        {
            await _audioCaptureControl.ResumeActiveCaptureAsync(cancellationToken).ConfigureAwait(false);
            SetState(State with
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
            SetState(State with
            {
                StatusText = "Pause unavailable",
                UserMessage = "Pause is available only while mock recording is active."
            });
            return;
        }

        await _audioCaptureControl.PauseActiveCaptureAsync(cancellationToken).ConfigureAwait(false);
        SetState(State with
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

        SetState(State with
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
            SetState(State with
            {
                StatusText = "Nothing to copy",
                UserMessage = "Run mock dictation before copying a transcript."
            });
            return false;
        }

        await _clipboard.CopyTextAsync(text, cancellationToken).ConfigureAwait(false);
        SetState(State with
        {
            StatusText = "Copied",
            UserMessage = "Transcript copied from the preview."
        });

        return true;
    }

    private async Task RunOnceAsync(
        DictationInsertionMode insertionMode,
        CancellationToken cancellationToken,
        TaskCompletionSource captureStarted)
    {
        try
        {
            var progress = new SynchronousProgress<DictationStatus>(ApplyProgress);
            var request = new DictationRequest(
                _transcriptionOptionsFactory(),
                insertionMode,
                audioOptions: _audioOptions,
                statusProgress: progress,
                captureSessionStarted: session =>
                {
                    captureStarted.TrySetResult();

                    if (AudioLevelChanged is not null)
                    {
                        StartLevelMonitor(session);
                    }
                });

            var result = await _coordinator
                .RunOnceAsync(request, cancellationToken)
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
        finally
        {
            captureStarted.TrySetResult();
            await StopLevelMonitorAsync().ConfigureAwait(false);
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            _activeRun = null;
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
        SetState(State with
        {
            SessionState = status.State,
            StatusText = status.Message,
            UserMessage = DescribeActiveState(status.State),
            IsRunning = status.State is not DictationSessionState.Complete
                and not DictationSessionState.Cancelled
                and not DictationSessionState.Failed,
            CanStart = false,
            CanStop = status.State is DictationSessionState.Recording or DictationSessionState.Paused,
            CanCancel = status.State is not DictationSessionState.Complete
                and not DictationSessionState.Cancelled
                and not DictationSessionState.Failed,
            CanPause = status.State is DictationSessionState.Recording or DictationSessionState.Paused,
            IsPaused = status.State == DictationSessionState.Paused
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
            result.Outcome,
            result.Error?.Message);
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
            DictationInsertionMode.AutoInsert => "Auto insert will restore the mock target and insert the transcript.",
            DictationInsertionMode.PreviewFirst => "Preview first will stop before insertion.",
            DictationInsertionMode.CopyOnly => "Copy only will copy the transcript after transcription.",
            _ => "Ready."
        };
    }

    private void SetIdleMessage(string message)
    {
        SetState(State with
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
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
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
