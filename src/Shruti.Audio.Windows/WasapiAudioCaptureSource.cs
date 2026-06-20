using NAudio.CoreAudioApi;
using NAudio.Wave;
using Shruti.Transcription.Abstractions;

namespace Shruti.Audio.Windows;

public sealed class WasapiAudioCaptureSource : IWindowsAudioCaptureSource
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private bool _isDisposed;

    public WasapiAudioCaptureSource(MMDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _capture = new WasapiCapture(_device);
        StreamFormat = CreateStreamFormat(_capture.WaveFormat);
        _capture.DataAvailable += Capture_DataAvailable;
        _capture.RecordingStopped += Capture_RecordingStopped;
    }

    public WindowsAudioStreamFormat StreamFormat { get; }

    public event EventHandler<WindowsAudioDataAvailableEventArgs>? DataAvailable;

    public event EventHandler<WindowsAudioCaptureStoppedEventArgs>? CaptureStopped;

    public void StartCapture()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _capture.StartRecording();
    }

    public void StopCapture()
    {
        if (_isDisposed)
        {
            return;
        }

        _capture.StopRecording();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _capture.DataAvailable -= Capture_DataAvailable;
        _capture.RecordingStopped -= Capture_RecordingStopped;
        _capture.Dispose();
        _device.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        DataAvailable?.Invoke(this, new WindowsAudioDataAvailableEventArgs(e.Buffer, e.BytesRecorded));
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        CaptureStopped?.Invoke(this, new WindowsAudioCaptureStoppedEventArgs(e.Exception));
    }

    private static WindowsAudioStreamFormat CreateStreamFormat(WaveFormat format)
    {
        AudioSampleFormat sampleFormat = format.Encoding == WaveFormatEncoding.IeeeFloat
            ? AudioSampleFormat.Float32
            : AudioSampleFormat.Int16;

        return new WindowsAudioStreamFormat(
            format.SampleRate,
            format.Channels,
            sampleFormat,
            format.BitsPerSample);
    }
}
