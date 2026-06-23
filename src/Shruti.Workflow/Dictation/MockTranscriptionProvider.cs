using System.Threading.Channels;
using Shruti.Transcription.Abstractions;

namespace Shruti.Workflow.Dictation;

public sealed class MockTranscriptionProvider : ITranscriptionProvider
{
    public string Id => "mock-provider";

    public string DisplayName => "Mock provider";

    public MockTranscriptionSession? LastSession { get; private set; }

    public TaskCompletionSource? CompleteGate { get; set; }

    public Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<EngineCapability> capabilities =
        [
            new EngineCapability(
                Id,
                DisplayName,
                ComputeBackend.Cpu,
                "Mock CPU",
                SupportsStreaming: true,
                SupportsTimestamps: true,
                SupportsLanguageDetection: false,
                MeasuredRealtimeFactor: 0.05,
                Warnings: [])
        ];

        return Task.FromResult(capabilities);
    }

    public Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSession = new MockTranscriptionSession(CompleteGate);
        return Task.FromResult<ITranscriptionSession>(LastSession);
    }

    public sealed class MockTranscriptionSession : ITranscriptionSession
    {
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();
        private readonly TaskCompletionSource? _completeGate;
        private bool _partialTranscriptSent;

        public MockTranscriptionSession(TaskCompletionSource? completeGate)
        {
            _completeGate = completeGate;
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
                    Text: "hello from the Shruti mock dictation"));
            }

            return ValueTask.FromResult(TranscriptionAudioPushResult.Continue);
        }

        public async Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completeGate is not null)
            {
                await _completeGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            TranscriptResult result = TranscriptResult.FromText("hello from the Shruti mock dictation loop");
            _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.Completed, Text: result.Text));
            _events.Writer.TryComplete();
            return result;
        }

        public void EmitPartialTranscript(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.PartialText, Text: text));
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
}
