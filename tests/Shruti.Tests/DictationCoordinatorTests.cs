using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Shruti.Core;
using Shruti.Core.Audio;
using Shruti.Core.Dictation;
using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;
using Xunit;

namespace Shruti.Tests;

public sealed class DictationCoordinatorTests
{
    [Fact]
    public async Task AutoInsert_RunCompletesAndInsertsTranscript()
    {
        var services = TestServices.Create();
        var request = DictationRequest.AutoInsert(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        Assert.Equal(DictationRunOutcome.Inserted, result.Outcome);
        Assert.True(result.Inserted);
        Assert.Equal("hello from shruti", result.Transcript?.Text);
        Assert.Equal("hello from shruti", services.TextInsertion.LastInsertedText);
        Assert.Equal(1, services.TargetFocus.CaptureCount);
        Assert.Equal(1, services.TargetFocus.RestoreCount);
        Assert.Equal(1, services.AudioCapture.StartCount);
        Assert.Equal(1, services.TextInsertion.InspectCount);
        Assert.Equal(1, services.TextInsertion.InsertCount);
        Assert.Equal(2, services.Transcription.LastSession?.PushedAudioChunkCount);
    }

    [Fact]
    public async Task AudioLimit_FinalizesCapturedAudioInsteadOfFailingTheRun()
    {
        var services = TestServices.Create(reachMaximumAudioDurationAfterFirstPush: true);

        DictationRunResult result = await services.Coordinator.RunOnceAsync(
            DictationRequest.AutoInsert(CreateTranscriptionOptions()),
            CancellationToken.None);

        Assert.Equal(DictationRunOutcome.Inserted, result.Outcome);
        Assert.Equal(1, services.Transcription.LastSession?.PushedAudioChunkCount);
        Assert.Equal(1, services.AudioCapture.LastSession?.StopCount);
        Assert.DoesNotContain(result.StatusHistory, status => status.State == DictationSessionState.Failed);
        Assert.Contains(
            result.StatusHistory,
            status => status.Message == "Recording limit reached; finalizing captured audio");
    }

    [Fact]
    public async Task TranscriptProgress_ForwardsLivePartialTextBeforeTheFinalResult()
    {
        var services = TestServices.Create();
        var transcriptProgress = new RecordingProgress<TranscriptEvent>();
        var request = new DictationRequest(
            CreateTranscriptionOptions(),
            transcriptProgress: transcriptProgress);

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        Assert.Equal(DictationRunOutcome.Inserted, result.Outcome);
        Assert.Contains(
            transcriptProgress.Values,
            transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.PartialText &&
                transcriptEvent.Text == "hello from shruti partial");
        Assert.Contains(
            transcriptProgress.Values,
            transcriptEvent => transcriptEvent.Kind == TranscriptEventKind.Completed &&
                transcriptEvent.Text == "hello from shruti");
    }

    [Fact]
    public async Task PreviewFirst_RunCompletesWithoutInsertionAndRequiresPreview()
    {
        var services = TestServices.Create();
        var request = DictationRequest.PreviewFirst(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        Assert.Equal(DictationRunOutcome.PreviewRequired, result.Outcome);
        Assert.True(result.RequiresPreview);
        Assert.Equal("hello from shruti", result.Transcript?.Text);
        Assert.Equal(0, services.TextInsertion.InspectCount);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.Equal(0, services.TargetFocus.RestoreCount);
    }

    [Fact]
    public async Task CopyOnly_RunCompletesWithoutInsertion()
    {
        var services = TestServices.Create();
        var request = DictationRequest.CopyOnly(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        Assert.Equal(DictationRunOutcome.CopyOnly, result.Outcome);
        Assert.True(result.ShouldCopyToClipboard);
        Assert.Equal("hello from shruti", result.Transcript?.Text);
        Assert.Equal(0, services.TextInsertion.InspectCount);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.Equal(0, services.TargetFocus.RestoreCount);
    }

    [Fact]
    public async Task Cancellation_DoesNotInsertText()
    {
        using var cancellation = new CancellationTokenSource();
        var services = TestServices.Create(onComplete: () => cancellation.Cancel());
        var request = DictationRequest.AutoInsert(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, cancellation.Token);

        Assert.Equal(DictationRunOutcome.Cancelled, result.Outcome);
        Assert.True(result.IsCancelled);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.Equal(0, services.TextInsertion.InspectCount);
        Assert.Equal(1, services.Transcription.LastSession?.CancelCount);
        Assert.Contains(
            result.StatusHistory,
            status => status.State == DictationSessionState.Cancelled);
    }

    [Fact]
    public async Task UnsupportedInsertionCapability_RequiresPreview()
    {
        var services = TestServices.Create(
            capability: new TextInsertionCapability(
                TextInsertionCapabilityOutcome.Unsupported,
                TextInsertionMethod.None,
                "Target does not support safe insertion."));
        var request = DictationRequest.AutoInsert(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        Assert.Equal(DictationRunOutcome.PreviewRequired, result.Outcome);
        Assert.True(result.RequiresPreview);
        Assert.Equal("Target does not support safe insertion.", result.Message);
        Assert.Equal(1, services.TextInsertion.InspectCount);
        Assert.Equal(0, services.TextInsertion.InsertCount);
        Assert.Equal(0, services.TargetFocus.RestoreCount);
    }

    [Fact]
    public async Task InsertFinalizedTranscriptAsync_RestoresTargetAndInsertsEditedPreview()
    {
        var services = TestServices.Create();

        var result = await services.Coordinator.InsertFinalizedTranscriptAsync(
            services.Target,
            TranscriptResult.FromText("edited preview text"),
            new TextInsertionOptions(AllowReplacingSelection: true),
            statusProgress: null,
            CancellationToken.None);

        Assert.Equal(DictationRunOutcome.Inserted, result.Outcome);
        Assert.Equal("edited preview text", services.TextInsertion.LastInsertedText);
        Assert.Equal(1, services.TargetFocus.RestoreCount);
        Assert.Equal(1, services.TextInsertion.InspectCount);
        Assert.Equal(1, services.TextInsertion.InsertCount);
        Assert.Contains(result.StatusHistory, status => status.State == DictationSessionState.InsertingText);
    }

    [Fact]
    public async Task ClipboardPasteSubmission_CompletesAsPreviewRecoveryWorkflow()
    {
        var services = TestServices.Create(
            capability: new TextInsertionCapability(
                TextInsertionCapabilityOutcome.ClipboardFallbackOnly,
                TextInsertionMethod.ClipboardPaste,
                "This target is more reliable with clipboard paste than direct text input."),
            insertionResult: new TextInsertionResult(
                Inserted: false,
                TextInsertionMethod.ClipboardPaste,
                "Clipboard paste was submitted but cannot be confirmed.",
                Submitted: true));
        var request = DictationRequest.AutoInsert(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        Assert.Equal(DictationRunOutcome.PreviewRequired, result.Outcome);
        Assert.True(result.RequiresPreview);
        Assert.False(result.Inserted);
        Assert.False(result.InsertionResult?.Inserted);
        Assert.True(result.InsertionResult?.Submitted);
        Assert.Equal("Clipboard paste was submitted but cannot be confirmed.", result.Message);
        Assert.Equal(1, services.TargetFocus.RestoreCount);
        Assert.Equal(1, services.TextInsertion.InsertCount);
    }

    [Fact]
    public async Task StatusHistory_FollowsExpectedCoreStates()
    {
        var services = TestServices.Create();
        var request = DictationRequest.AutoInsert(CreateTranscriptionOptions());

        var result = await services.Coordinator.RunOnceAsync(request, CancellationToken.None);

        var states = result.StatusHistory.Select(status => status.State).ToArray();
        Assert.Equal(
            [
                DictationSessionState.PreparingTarget,
                DictationSessionState.RequestingMicrophone,
                DictationSessionState.Recording,
                DictationSessionState.TranscribingFinalAudio,
                DictationSessionState.InsertingText,
                DictationSessionState.Complete
            ],
            states);
    }

    private static TranscriptionSessionOptions CreateTranscriptionOptions()
    {
        var model = new TranscriptionModelDescriptor(
            "mock",
            "Mock model",
            "mock-provider",
            "mock.gguf",
            "en",
            1024,
            new HashSet<ComputeBackend> { ComputeBackend.Cpu });

        return new TranscriptionSessionOptions(
            model,
            ComputeBackend.Cpu,
            "en",
            TranscriptionMode.Balanced);
    }

    private sealed class TestServices
    {
        private TestServices(
            FakeTargetFocusService targetFocus,
            FakeAudioCaptureService audioCapture,
            FakeTextInsertionService textInsertion,
            FakeTranscriptionProvider transcription)
        {
            TargetFocus = targetFocus;
            AudioCapture = audioCapture;
            TextInsertion = textInsertion;
            Transcription = transcription;
            Coordinator = new DictationCoordinator(
                targetFocus,
                audioCapture,
                textInsertion,
                transcription);
        }

        public DictationCoordinator Coordinator { get; }

        public FakeTargetFocusService TargetFocus { get; }

        public FakeAudioCaptureService AudioCapture { get; }

        public FakeTextInsertionService TextInsertion { get; }

        public FakeTranscriptionProvider Transcription { get; }

        public FocusTarget Target => TargetFocus.Target;

        public static TestServices Create(
            TextInsertionCapability? capability = null,
            TextInsertionResult? insertionResult = null,
            Action? onComplete = null,
            bool reachMaximumAudioDurationAfterFirstPush = false)
        {
            var targetFocus = new FakeTargetFocusService();
            var audioCapture = new FakeAudioCaptureService(
            [
                new AudioFrame(new byte[] { 1, 2, 3, 4 }, TimeSpan.Zero),
                new AudioFrame(new byte[] { 5, 6, 7, 8 }, TimeSpan.FromMilliseconds(10))
            ]);
            var textInsertion = new FakeTextInsertionService(
                capability ?? new TextInsertionCapability(
                    TextInsertionCapabilityOutcome.DirectInputAvailable,
                    TextInsertionMethod.DirectInput),
                insertionResult ?? new TextInsertionResult(
                    Inserted: true,
                    TextInsertionMethod.DirectInput));
            var transcription = new FakeTranscriptionProvider(onComplete, reachMaximumAudioDurationAfterFirstPush);

            return new TestServices(
                targetFocus,
                audioCapture,
                textInsertion,
                transcription);
        }
    }

    private sealed class FakeTargetFocusService : ITargetFocusService
    {
        private static readonly FocusTarget CapturedTarget = new(
            WindowHandle: new IntPtr(42),
            ProcessId: 100,
            ProcessName: "notepad",
            WindowTitle: "Untitled - Notepad",
            AutomationElementId: "edit",
            IsEditable: true,
            HasSelectedText: false);

        public FocusTarget Target => CapturedTarget;

        public int CaptureCount { get; private set; }

        public int RestoreCount { get; private set; }

        public Task<FocusTarget?> CaptureCurrentTargetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureCount++;
            return Task.FromResult<FocusTarget?>(CapturedTarget);
        }

        public Task<FocusRestoreResult> RestoreAsync(
            FocusTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            return Task.FromResult(new FocusRestoreResult(Restored: true));
        }
    }

    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        private readonly IReadOnlyList<AudioFrame> _frames;

        public FakeAudioCaptureService(IReadOnlyList<AudioFrame> frames)
        {
            _frames = frames;
        }

        public int StartCount { get; private set; }

        public AudioFormat? RequestedOutputFormat { get; private set; }

        public FakeAudioCaptureSession? LastSession { get; private set; }

        public Task<IReadOnlyList<AudioInputDevice>> ListInputDevicesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<AudioInputDevice> devices =
            [
                new AudioInputDevice("default", "Default microphone", IsDefault: true)
            ];

            return Task.FromResult(devices);
        }

        public Task<IAudioCaptureSession> StartAsync(
            AudioCaptureOptions options,
            AudioFormat outputFormat,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            RequestedOutputFormat = outputFormat;
            LastSession = new FakeAudioCaptureSession(_frames);
            return Task.FromResult<IAudioCaptureSession>(LastSession);
        }
    }

    private sealed class FakeAudioCaptureSession : IAudioCaptureSession
    {
        private readonly IReadOnlyList<AudioFrame> _frames;

        public FakeAudioCaptureSession(IReadOnlyList<AudioFrame> frames)
        {
            _frames = frames;
        }

        public IAsyncEnumerable<AudioFrame> Frames => EnumerateFrames();

        public IAsyncEnumerable<AudioLevelFrame> Levels => EnumerateLevels();

        public int StopCount { get; private set; }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private async IAsyncEnumerable<AudioFrame> EnumerateFrames(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var frame in _frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return frame;
            }
        }

        private static async IAsyncEnumerable<AudioLevelFrame> EnumerateLevels(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeTextInsertionService : ITextInsertionService
    {
        private readonly TextInsertionCapability _capability;
        private readonly TextInsertionResult _result;

        public FakeTextInsertionService(
            TextInsertionCapability capability,
            TextInsertionResult result)
        {
            _capability = capability;
            _result = result;
        }

        public int InspectCount { get; private set; }

        public int InsertCount { get; private set; }

        public string? LastInsertedText { get; private set; }

        public Task<TextInsertionCapability> InspectAsync(
            FocusTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCount++;
            return Task.FromResult(_capability);
        }

        public Task<TextInsertionResult> InsertAsync(
            FocusTarget target,
            string text,
            TextInsertionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InsertCount++;
            LastInsertedText = text;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeTranscriptionProvider : ITranscriptionProvider
    {
        private readonly Action? _onComplete;
        private readonly bool _reachMaximumAudioDurationAfterFirstPush;

        public FakeTranscriptionProvider(Action? onComplete, bool reachMaximumAudioDurationAfterFirstPush)
        {
            _onComplete = onComplete;
            _reachMaximumAudioDurationAfterFirstPush = reachMaximumAudioDurationAfterFirstPush;
        }

        public string Id => "mock-provider";

        public string DisplayName => "Mock provider";

        public FakeTranscriptionSession? LastSession { get; private set; }

        public Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<EngineCapability> capabilities =
            [
                new EngineCapability(
                    Id,
                    DisplayName,
                    ComputeBackend.Cpu,
                    "Mock CPU",
                    SupportsStreaming: false,
                    SupportsTimestamps: true,
                    SupportsLanguageDetection: false,
                    MeasuredRealtimeFactor: 0.1,
                    Warnings: [])
            ];

            return Task.FromResult(capabilities);
        }

        public Task<bool> CanRunModelAsync(
            TranscriptionModelDescriptor model,
            ComputeBackend requestedBackend,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<ITranscriptionSession> CreateSessionAsync(
            TranscriptionSessionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastSession = new FakeTranscriptionSession(_onComplete, _reachMaximumAudioDurationAfterFirstPush);
            return Task.FromResult<ITranscriptionSession>(LastSession);
        }
    }

    private sealed class FakeTranscriptionSession : ITranscriptionSession
    {
        private readonly Action? _onComplete;
        private readonly bool _reachMaximumAudioDurationAfterFirstPush;
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();
        private bool _partialTranscriptSent;

        public FakeTranscriptionSession(Action? onComplete, bool reachMaximumAudioDurationAfterFirstPush)
        {
            _onComplete = onComplete;
            _reachMaximumAudioDurationAfterFirstPush = reachMaximumAudioDurationAfterFirstPush;
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public IAsyncEnumerable<TranscriptEvent> Events => _events.Reader.ReadAllAsync();

        public int PushedAudioChunkCount { get; private set; }

        public int CancelCount { get; private set; }

        public ValueTask<TranscriptionAudioPushResult> PushAudioAsync(
            ReadOnlyMemory<byte> pcmAudio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PushedAudioChunkCount++;
            if (!_partialTranscriptSent)
            {
                _partialTranscriptSent = true;
                _events.Writer.TryWrite(new TranscriptEvent(
                    TranscriptEventKind.PartialText,
                    Text: "hello from shruti partial"));
            }

            return ValueTask.FromResult(
                _reachMaximumAudioDurationAfterFirstPush && PushedAudioChunkCount == 1
                    ? TranscriptionAudioPushResult.FinalizeAtMaximumDuration
                    : TranscriptionAudioPushResult.Continue);
        }

        public Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onComplete?.Invoke();
            TranscriptResult result = TranscriptResult.FromText("hello from shruti");
            _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.Completed, Text: result.Text));
            _events.Writer.TryComplete();
            return Task.FromResult(result);
        }

        public Task CancelAsync()
        {
            CancelCount++;
            _events.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        private readonly object _sync = new();
        private readonly List<T> _values = [];

        public IReadOnlyList<T> Values
        {
            get
            {
                lock (_sync)
                {
                    return _values.ToArray();
                }
            }
        }

        public void Report(T value)
        {
            lock (_sync)
            {
                _values.Add(value);
            }
        }
    }
}
