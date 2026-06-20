using System.Runtime.CompilerServices;
using Shruti.Transcription.Abstractions;

namespace Shruti.Workflow.Dictation;

public sealed class MockTranscriptionProvider : ITranscriptionProvider
{
    public string Id => "mock-provider";

    public string DisplayName => "Mock provider";

    public MockTranscriptionSession? LastSession { get; private set; }

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
                SupportsStreaming: false,
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
        LastSession = new MockTranscriptionSession();
        return Task.FromResult<ITranscriptionSession>(LastSession);
    }

    public sealed class MockTranscriptionSession : ITranscriptionSession
    {
        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public IAsyncEnumerable<TranscriptEvent> Events => EnumerateEvents();

        public int PushedAudioChunkCount { get; private set; }

        public int CancelCount { get; private set; }

        public ValueTask PushAudioAsync(
            ReadOnlyMemory<byte> pcmAudio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PushedAudioChunkCount++;
            return ValueTask.CompletedTask;
        }

        public Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TranscriptResult.FromText("hello from the Shruti mock dictation loop"));
        }

        public Task CancelAsync()
        {
            CancelCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private static async IAsyncEnumerable<TranscriptEvent> EnumerateEvents(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }
}
