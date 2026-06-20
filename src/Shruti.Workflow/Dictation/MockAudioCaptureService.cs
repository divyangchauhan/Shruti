using System.Threading.Channels;
using Shruti.Core.Audio;
using Shruti.Transcription.Abstractions;

namespace Shruti.Workflow.Dictation;

public sealed class MockAudioCaptureService : IAudioCaptureService, IAudioCaptureControl
{
    private readonly object _sync = new();
    private MockAudioCaptureSession? _activeSession;
    private bool _stopRequestedBeforeStart;

    public MockAudioCaptureService()
    {
    }

    public int StartCount { get; private set; }

    public AudioFormat? LastRequestedOutputFormat { get; private set; }

    public AudioCaptureOptions? LastOptions { get; private set; }

    public Task<IReadOnlyList<AudioInputDevice>> ListInputDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AudioInputDevice> devices =
        [
            new AudioInputDevice("mock-default", "Mock microphone", IsDefault: true)
        ];

        return Task.FromResult(devices);
    }

    public Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        AudioFormat outputFormat,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = new MockAudioCaptureSession();

        lock (_sync)
        {
            StartCount++;
            LastRequestedOutputFormat = outputFormat;
            LastOptions = options;
            _activeSession = session;

            if (_stopRequestedBeforeStart)
            {
                _stopRequestedBeforeStart = false;
                session.RequestStop();
            }
        }

        return Task.FromResult<IAudioCaptureSession>(session);
    }

    public Task StopActiveCaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MockAudioCaptureSession? session;

        lock (_sync)
        {
            session = _activeSession;
            if (session is null)
            {
                _stopRequestedBeforeStart = true;
            }
        }

        session?.RequestStop();
        return Task.CompletedTask;
    }

    public Task PauseActiveCaptureAsync(CancellationToken cancellationToken = default)
    {
        MockAudioCaptureSession? session;

        lock (_sync)
        {
            session = _activeSession;
        }

        if (session is null)
        {
            return Task.CompletedTask;
        }

        PauseCount++;
        return session.PauseAsync(cancellationToken);
    }

    public Task ResumeActiveCaptureAsync(CancellationToken cancellationToken = default)
    {
        MockAudioCaptureSession? session;

        lock (_sync)
        {
            session = _activeSession;
        }

        if (session is null)
        {
            return Task.CompletedTask;
        }

        ResumeCount++;
        return session.ResumeAsync(cancellationToken);
    }

    public int PauseCount { get; private set; }

    public int ResumeCount { get; private set; }

    private sealed class MockAudioCaptureSession : IAudioCaptureSession
    {
        private readonly object _sync = new();
        private readonly Channel<AudioFrame> _frames = Channel.CreateUnbounded<AudioFrame>();
        private readonly Channel<AudioLevelFrame> _levels = Channel.CreateUnbounded<AudioLevelFrame>();
        private bool _isStopped;

        public MockAudioCaptureSession()
        {
            _frames.Writer.TryWrite(new AudioFrame(CreateFrame(), TimeSpan.Zero));
            _levels.Writer.TryWrite(new AudioLevelFrame(
                Peak: 0.62f,
                Rms: 0.38f,
                DateTimeOffset.UtcNow));
        }

        public IAsyncEnumerable<AudioFrame> Frames => _frames.Reader.ReadAllAsync();

        public IAsyncEnumerable<AudioLevelFrame> Levels => _levels.Reader.ReadAllAsync();

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
            cancellationToken.ThrowIfCancellationRequested();
            RequestStop();
            return Task.CompletedTask;
        }

        public void RequestStop()
        {
            lock (_sync)
            {
                if (_isStopped)
                {
                    return;
                }

                _isStopped = true;
            }

            _frames.Writer.TryComplete();
            _levels.Writer.TryComplete();
        }

        public ValueTask DisposeAsync()
        {
            RequestStop();
            return ValueTask.CompletedTask;
        }

        private static byte[] CreateFrame()
        {
            return [1, 0, 1, 0, 1, 0, 1, 0];
        }
    }
}
