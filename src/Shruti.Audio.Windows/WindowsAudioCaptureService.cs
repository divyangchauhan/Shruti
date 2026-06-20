using Shruti.Core.Audio;
using Shruti.Transcription.Abstractions;

namespace Shruti.Audio.Windows;

public sealed class WindowsAudioCaptureService : IAudioCaptureService, IAudioCaptureControl, IDisposable
{
    private readonly object _sync = new();
    private readonly IWindowsAudioDeviceCatalog _deviceCatalog;
    private readonly IWindowsAudioCaptureFactory _captureFactory;
    private WindowsAudioCaptureSession? _activeSession;
    private bool _isDisposed;

    public WindowsAudioCaptureService(
        IWindowsAudioDeviceCatalog? deviceCatalog = null,
        IWindowsAudioCaptureFactory? captureFactory = null)
    {
        _deviceCatalog = deviceCatalog ?? new WasapiAudioDeviceCatalog();
        _captureFactory = captureFactory ?? new WasapiAudioCaptureFactory();
    }

    public Task<IReadOnlyList<AudioInputDevice>> ListInputDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        IReadOnlyList<AudioInputDevice> devices = _deviceCatalog
            .ListCaptureDevices()
            .Select(device => new AudioInputDevice(device.Id, device.DisplayName, device.IsDefault))
            .ToArray();

        return Task.FromResult(devices);
    }

    public Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        AudioFormat outputFormat,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outputFormat);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateOutputFormat(outputFormat);

        WindowsAudioCaptureDevice device = ResolveDevice(options.DeviceId);
        IWindowsAudioCaptureSource source = _captureFactory.Create(device);
        WindowsAudioCaptureSession session;

        try
        {
            session = new WindowsAudioCaptureSession(
                source,
                outputFormat,
                options.EnableLevelMeter,
                OnSessionCompleted);
        }
        catch
        {
            source.Dispose();
            throw;
        }

        bool sessionCanStart;
        lock (_sync)
        {
            sessionCanStart = !_isDisposed && _activeSession is null;
            if (sessionCanStart)
            {
                _activeSession = session;
            }
        }

        if (!sessionCanStart)
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WindowsAudioCaptureService));
            }

            throw new InvalidOperationException("A microphone capture session is already active.");
        }

        try
        {
            session.Start();
            return Task.FromResult<IAudioCaptureSession>(session);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
            }

            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public Task StopActiveCaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetActiveSession()?.StopAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public Task PauseActiveCaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetActiveSession()?.PauseAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public Task ResumeActiveCaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetActiveSession()?.ResumeAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        WindowsAudioCaptureSession? session = GetActiveSession();
        session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private WindowsAudioCaptureDevice ResolveDevice(string? requestedDeviceId)
    {
        IReadOnlyList<WindowsAudioCaptureDevice> devices = _deviceCatalog.ListCaptureDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No active Windows microphone input devices are available.");
        }

        WindowsAudioCaptureDevice? device = string.IsNullOrWhiteSpace(requestedDeviceId)
            ? devices.FirstOrDefault(candidate => candidate.IsDefault) ?? devices[0]
            : devices.FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                requestedDeviceId,
                StringComparison.OrdinalIgnoreCase));

        return device ?? throw new InvalidOperationException(
            "The selected microphone is no longer available.");
    }

    private WindowsAudioCaptureSession? GetActiveSession()
    {
        lock (_sync)
        {
            return _activeSession;
        }
    }

    private void OnSessionCompleted(WindowsAudioCaptureSession session)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeSession, session))
            {
                _activeSession = null;
            }
        }
    }

    private static void ValidateOutputFormat(AudioFormat outputFormat)
    {
        if (outputFormat.SampleRateHz <= 0 || outputFormat.ChannelCount != 1)
        {
            throw new ArgumentException("Windows microphone capture requires a positive mono output format.", nameof(outputFormat));
        }

        if (outputFormat.SampleFormat != AudioSampleFormat.Int16)
        {
            throw new NotSupportedException("Windows microphone capture currently normalizes audio to 16-bit PCM.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
