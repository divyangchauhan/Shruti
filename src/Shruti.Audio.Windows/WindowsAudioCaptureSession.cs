using System.Diagnostics;
using System.Threading.Channels;
using Shruti.Core.Audio;
using Shruti.Transcription.Abstractions;

namespace Shruti.Audio.Windows;

internal sealed class WindowsAudioCaptureSession : IAudioCaptureSession
{
    private readonly object _sync = new();
    private readonly IWindowsAudioCaptureSource _source;
    private readonly PcmAudioNormalizer _normalizer;
    private readonly bool _enableLevelMeter;
    private readonly Action<WindowsAudioCaptureSession> _onCompleted;
    private readonly Channel<AudioFrame> _frames = Channel.CreateUnbounded<AudioFrame>();
    private readonly Channel<AudioLevelFrame> _levels = Channel.CreateUnbounded<AudioLevelFrame>();
    private readonly Stopwatch _elapsed = new();

    private bool _isPaused;
    private bool _isCompleted;
    private bool _isDisposed;

    public WindowsAudioCaptureSession(
        IWindowsAudioCaptureSource source,
        AudioFormat outputFormat,
        bool enableLevelMeter,
        Action<WindowsAudioCaptureSession> onCompleted)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _normalizer = new PcmAudioNormalizer(source.StreamFormat, outputFormat);
        _enableLevelMeter = enableLevelMeter;
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));

        _source.DataAvailable += Source_DataAvailable;
        _source.CaptureStopped += Source_CaptureStopped;
    }

    public IAsyncEnumerable<AudioFrame> Frames => _frames.Reader.ReadAllAsync();

    public IAsyncEnumerable<AudioLevelFrame> Levels => _levels.Reader.ReadAllAsync();

    public void Start()
    {
        ThrowIfDisposed();
        _elapsed.Restart();
        _source.StartCapture();
    }

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        lock (_sync)
        {
            _isPaused = true;
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        lock (_sync)
        {
            _isPaused = false;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsCompleted)
        {
            return Task.CompletedTask;
        }

        try
        {
            _source.StopCapture();
        }
        catch (Exception ex)
        {
            Complete(ex);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;

        try
        {
            _source.StopCapture();
        }
        catch
        {
            // Completion is still required even if the device is already gone.
        }

        Complete();
        _source.DataAvailable -= Source_DataAvailable;
        _source.CaptureStopped -= Source_CaptureStopped;
        _source.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private void Source_DataAvailable(object? sender, WindowsAudioDataAvailableEventArgs e)
    {
        if (IsPaused || IsCompleted || e.BytesRecorded == 0)
        {
            return;
        }

        try
        {
            byte[] pcmAudio = _normalizer.Normalize(e.Buffer.AsSpan(0, e.BytesRecorded));
            if (pcmAudio.Length == 0)
            {
                return;
            }

            _frames.Writer.TryWrite(new AudioFrame(pcmAudio, _elapsed.Elapsed));
            if (_enableLevelMeter)
            {
                _levels.Writer.TryWrite(CreateLevelFrame(pcmAudio));
            }
        }
        catch (Exception ex)
        {
            Complete(ex);
            try
            {
                _source.StopCapture();
            }
            catch
            {
                // The original conversion failure is reported through the frame stream.
            }
        }
    }

    private void Source_CaptureStopped(object? sender, WindowsAudioCaptureStoppedEventArgs e)
    {
        Complete(e.Exception);
    }

    private AudioLevelFrame CreateLevelFrame(ReadOnlySpan<byte> pcmAudio)
    {
        float peak = 0;
        double squaredTotal = 0;
        int sampleCount = pcmAudio.Length / sizeof(short);

        for (int index = 0; index < sampleCount; index++)
        {
            short rawSample = BitConverter.ToInt16(pcmAudio.Slice(index * sizeof(short), sizeof(short)));
            float sample = rawSample / AudioFormat.Pcm16SampleScale;
            float magnitude = MathF.Abs(sample);
            peak = MathF.Max(peak, magnitude);
            squaredTotal += sample * sample;
        }

        float rms = sampleCount == 0
            ? 0
            : (float)Math.Sqrt(squaredTotal / sampleCount);

        return new AudioLevelFrame(peak, rms, DateTimeOffset.UtcNow);
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

    private bool IsCompleted
    {
        get
        {
            lock (_sync)
            {
                return _isCompleted;
            }
        }
    }

    private void Complete(Exception? exception = null)
    {
        bool completed;
        lock (_sync)
        {
            completed = !_isCompleted;
            _isCompleted = true;
        }

        if (!completed)
        {
            return;
        }

        _elapsed.Stop();
        _frames.Writer.TryComplete(exception);
        _levels.Writer.TryComplete(exception);
        _onCompleted(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
