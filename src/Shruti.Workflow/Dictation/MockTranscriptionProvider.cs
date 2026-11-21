using Shruti.Transcription.Abstractions;

namespace Shruti.Workflow.Dictation;

public sealed class MockTranscriptionProvider : ITranscriptionProvider
{
    public string Id => "mock-provider";

    public string DisplayName => "Mock provider";

    public MockTranscriptionSession? LastSession { get; private set; }

    public TaskCompletionSource? CompleteGate { get; set; }

    public string ResultText { get; set; } = "hello from the Shruti mock dictation loop";

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
                SupportsTimestamps: true,
                SupportsLanguageDetection: false,
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
        LastSession = new MockTranscriptionSession(CompleteGate, ResultText);
        return Task.FromResult<ITranscriptionSession>(LastSession);
    }

    public sealed class MockTranscriptionSession : ITranscriptionSession
    {
        private readonly TaskCompletionSource? _completeGate;
        private readonly string _resultText;

        public MockTranscriptionSession(TaskCompletionSource? completeGate, string resultText)
        {
            _completeGate = completeGate;
            _resultText = resultText;
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public int CancelCount { get; private set; }

        public async Task<TranscriptResult> TranscribeAsync(
            ReadOnlyMemory<byte> pcmAudio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_completeGate is not null)
            {
                await _completeGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return TranscriptResult.FromText(_resultText);
        }

        public Task CancelAsync()
        {
            CancelCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
