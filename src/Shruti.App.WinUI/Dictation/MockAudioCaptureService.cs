using System.Runtime.CompilerServices;
using Shruti.Core.Audio;
using Shruti.Transcription.Abstractions;

namespace Shruti.App.WinUI.Dictation;

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
        private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isPaused;

        public IAsyncEnumerable<AudioFrame> Frames => EnumerateFrames();

        public IAsyncEnumerable<AudioLevelFrame> Levels => EnumerateLevels();

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetPaused(true);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetPaused(false);
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
            _stopRequested.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            RequestStop();
            return ValueTask.CompletedTask;
        }

        private async IAsyncEnumerable<AudioFrame> EnumerateFrames(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!IsPaused && !_stopRequested.Task.IsCompleted)
            {
                yield return new AudioFrame(CreateFrame(), TimeSpan.Zero);
            }

            await _stopRequested.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async IAsyncEnumerable<AudioLevelFrame> EnumerateLevels(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!IsPaused && !_stopRequested.Task.IsCompleted)
            {
                yield return new AudioLevelFrame(
                    Peak: 0.62f,
                    Rms: 0.38f,
                    DateTimeOffset.UtcNow);
            }

            await _stopRequested.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private bool IsPaused
        {
            get
            {
                lock (_sync)
                {
                    return _isPaused;
                }
            }
        }

        private void SetPaused(bool value)
        {
            lock (_sync)
            {
                _isPaused = value;
            }
        }

        private static byte[] CreateFrame()
        {
            return [1, 0, 1, 0, 1, 0, 1, 0];
        }
    }
}
