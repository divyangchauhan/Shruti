using Shruti.Core;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;

namespace Shruti.App.WinUI.Dictation;

public sealed class DictationShellController
{
    private readonly DictationCoordinator _coordinator;
    private readonly MockAudioCaptureService _audioCaptureService;
    private readonly ITranscriptClipboard _clipboard;

    private CancellationTokenSource? _activeCancellation;
    private Task? _activeRun;

    public DictationShellController(
        DictationCoordinator coordinator,
        MockAudioCaptureService audioCaptureService,
        ITranscriptClipboard clipboard)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        State = DictationShellState.Initial;
    }

    public event EventHandler? StateChanged;

    public DictationShellState State { get; private set; }

    public DictationRunResult? LastResult { get; private set; }

    public Task StartAsync(DictationInsertionMode insertionMode)
    {
        if (State.IsRunning)
        {
            return Task.CompletedTask;
        }

        _activeCancellation = new CancellationTokenSource();

        SetState(new DictationShellState(
            DictationSessionState.PreparingTarget,
            insertionMode,
            "Starting mock dictation",
            "Capturing the mock target before recording.",
            string.Empty,
            "Mock target pending",
            IsRunning: true,
            CanStart: false,
            CanStop: true,
            CanCancel: true,
            CanPause: false,
            IsPaused: false,
            CanRetry: false,
            CanCopy: false));

        _activeRun = RunOnceAsync(insertionMode, _activeCancellation.Token);
        return Task.CompletedTask;
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
            StatusText = "Stopping mock recording",
            UserMessage = "Finalizing captured audio.",
            CanStop = false,
            CanPause = false,
            IsPaused = false
        });

        await _audioCaptureService.StopActiveCaptureAsync().ConfigureAwait(false);
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
            StatusText = "Cancelling mock dictation",
            UserMessage = "No text will be inserted.",
            CanStop = false,
            CanCancel = false,
            CanPause = false,
            IsPaused = false
        });

        _activeCancellation.Cancel();
        await _audioCaptureService.StopActiveCaptureAsync().ConfigureAwait(false);
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
            await _audioCaptureService.ResumeActiveCaptureAsync(cancellationToken).ConfigureAwait(false);
            SetState(State with
            {
                SessionState = DictationSessionState.Recording,
                StatusText = "Recording",
                UserMessage = "Recording mock audio.",
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

        await _audioCaptureService.PauseActiveCaptureAsync(cancellationToken).ConfigureAwait(false);
        SetState(State with
        {
            SessionState = DictationSessionState.Paused,
            StatusText = "Paused",
            UserMessage = "Mock recording is paused.",
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
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = new SynchronousProgress<DictationStatus>(ApplyProgress);
            var request = new DictationRequest(
                MockDictationAppServices.CreateTranscriptionOptions(),
                insertionMode,
                statusProgress: progress);

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
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            _activeRun = null;
        }
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
            DictationRunOutcome.Inserted => "Inserted into the mock target.",
            DictationRunOutcome.PreviewRequired => result.Message ?? "Preview is ready before insertion.",
            DictationRunOutcome.CopyOnly => "Copied transcript for copy-only mode.",
            DictationRunOutcome.Cancelled => "Cancelled. Nothing was inserted.",
            DictationRunOutcome.Failed => result.Message ?? "Mock dictation failed.",
            _ => result.Message ?? "Mock dictation finished."
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
            DictationSessionState.PreparingTarget => "Capturing the mock target.",
            DictationSessionState.RequestingMicrophone => "Starting the mock microphone.",
            DictationSessionState.Recording => "Recording mock audio.",
            DictationSessionState.Paused => "Mock recording is paused.",
            DictationSessionState.TranscribingFinalAudio => "Generating the mock transcript.",
            DictationSessionState.InsertingText => "Restoring target and inserting mock text.",
            DictationSessionState.Complete => "Mock dictation complete.",
            DictationSessionState.Cancelled => "Mock dictation cancelled.",
            DictationSessionState.Failed => "Mock dictation failed.",
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
